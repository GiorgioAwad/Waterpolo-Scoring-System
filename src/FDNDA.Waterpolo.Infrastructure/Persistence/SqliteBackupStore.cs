using System.Globalization;
using FDNDA.Waterpolo.Application.Abstractions;
using Microsoft.Data.Sqlite;

namespace FDNDA.Waterpolo.Infrastructure.Persistence;

/// <summary>
/// Respaldos de la base local en la carpeta <c>respaldos</c> del perfil. Copia y restaura con la
/// API de backup de SQLite (nunca copia el archivo activo ignorando el WAL). La restauración
/// escribe el respaldo sobre la base en uso dentro de la transacción de la API de backup: si
/// falla a mitad de la copia, la base no cambia; si falla después (migración o verificación),
/// vuelve a la copia de seguridad tomada justo antes.
/// </summary>
public sealed class SqliteBackupStore : IBackupStore
{
    private const string Extension = ".db";

    private static readonly (string Prefix, BackupReason Reason)[] Prefixes =
    [
        ("respaldo-manual-", BackupReason.Manual),
        ("respaldo-antes-partido-", BackupReason.BeforeMatch),
        ("respaldo-fin-partido-", BackupReason.AfterMatch),
        ("antes-de-restaurar-", BackupReason.BeforeRestore),
        ("pre-migracion-", BackupReason.BeforeMigration),
    ];

    private static readonly string[] RequiredTables =
        ["schema_migrations", "tournament", "team", "match", "match_incident", "incident_revision"];

    private readonly SqliteDatabase _database;
    private readonly SqliteBackupService _backups;
    private readonly TimeProvider _time;
    private readonly Func<CancellationToken, Task> _migrate;

    /// <param name="migrate">
    /// Lleva la base restaurada al esquema actual. Por defecto, <see cref="MigrationRunner"/> (con su
    /// respaldo previo); las pruebas lo sustituyen para provocar un fallo después de copiar.
    /// </param>
    public SqliteBackupStore(SqliteDatabase database, string backupDirectory, TimeProvider time, Func<CancellationToken, Task>? migrate = null)
    {
        _database = database;
        _backups = new SqliteBackupService(database);
        _time = time;
        BackupDirectory = Path.GetFullPath(backupDirectory);
        _migrate = migrate ?? (ct => new MigrationRunner(database, _backups, time).MigrateAsync(BackupDirectory, ct));
    }

    public string BackupDirectory { get; }

    public async Task<BackupFile> CreateAsync(BackupReason reason, CancellationToken ct = default)
    {
        Directory.CreateDirectory(BackupDirectory);
        var now = _time.GetUtcNow();
        var stamp = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var prefix = Prefixes.First(p => p.Reason == reason).Prefix;
        var path = Path.Combine(BackupDirectory, prefix + stamp + Extension);
        for (var n = 2; File.Exists(path); n++)
        {
            path = Path.Combine(BackupDirectory, $"{prefix}{stamp}-{n}{Extension}");
        }

        await _backups.CreateBackupAsync(path, ct).ConfigureAwait(false);
        return Describe(new FileInfo(path));
    }

    public Task<IReadOnlyList<BackupFile>> ListAsync(CancellationToken ct = default)
    {
        IReadOnlyList<BackupFile> files = Directory.Exists(BackupDirectory)
            ? [.. new DirectoryInfo(BackupDirectory).EnumerateFiles("*" + Extension)
                .Select(Describe)
                .OrderByDescending(f => f.CreatedAtUtc)
                .ThenByDescending(f => f.FileName, StringComparer.Ordinal)]
            : [];
        return Task.FromResult(files);
    }

    public async Task<BackupInspection> InspectAsync(string path, CancellationToken ct = default)
    {
        var supported = MigrationRunner.LatestKnownVersion;
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return BackupInspection.Invalid(fullPath, "No se encontró el archivo de respaldo.", supported);
        }

        if (string.Equals(fullPath, _database.DatabasePath, StringComparison.OrdinalIgnoreCase))
        {
            return BackupInspection.Invalid(fullPath, "Ese archivo es la base en uso, no un respaldo.", supported);
        }

        var damage = await SqliteBackupService.CheckIntegrityAsync(fullPath, ct).ConfigureAwait(false);
        if (damage is not null)
        {
            return BackupInspection.Invalid(fullPath, "El archivo está dañado o no es una base de datos del marcador.", supported);
        }

        try
        {
            await using var connection = new SqliteConnection(ReadOnly(fullPath));
            await connection.OpenAsync(ct).ConfigureAwait(false);

            foreach (var table in RequiredTables)
            {
                if (await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;", ct, table) == 0)
                {
                    return BackupInspection.Invalid(fullPath, "El archivo no es un respaldo del Marcador de Waterpolo.", supported);
                }
            }

            var version = await ScalarAsync(connection, "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;", ct);
            if (version > supported)
            {
                return BackupInspection.Invalid(
                    fullPath,
                    $"El respaldo es de una versión más nueva del programa (esquema v{version}; este admite hasta v{supported}). Actualice el programa antes de restaurarlo.",
                    supported);
            }

            if (version < 1)
            {
                return BackupInspection.Invalid(fullPath, "El respaldo no tiene versión de esquema.", supported);
            }

            return new BackupInspection(
                fullPath,
                null,
                version,
                supported,
                await ScalarAsync(connection, "SELECT COUNT(*) FROM tournament;", ct),
                await ScalarAsync(connection, "SELECT COUNT(*) FROM team;", ct),
                await ScalarAsync(connection, "SELECT COUNT(*) FROM match;", ct),
                await ScalarAsync(connection, "SELECT COUNT(*) FROM match WHERE status = 'InProgress';", ct),
                await ScalarAsync(connection, "SELECT COUNT(*) FROM match_incident;", ct),
                await ScalarAsync(connection, "SELECT COUNT(*) FROM tournament WHERE is_demo = 1;", ct) > 0);
        }
        catch (SqliteException)
        {
            return BackupInspection.Invalid(fullPath, "El archivo está dañado o no es una base de datos del marcador.", supported);
        }
    }

    public async Task<RestoreOutcome> RestoreAsync(string path, CancellationToken ct = default)
    {
        var inspection = await InspectAsync(path, ct).ConfigureAwait(false);
        if (!inspection.IsValid)
        {
            throw new RestoreFailedException($"{inspection.Problem} No se restauró nada.", rolledBack: true, safetyBackupPath: null);
        }

        string safety;
        try
        {
            safety = (await CreateAsync(BackupReason.BeforeRestore, ct).ConfigureAwait(false)).Path;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new RestoreFailedException(
                "No se pudo respaldar la base en uso antes de restaurar; no se restauró nada. Revise el espacio libre.",
                rolledBack: true,
                safetyBackupPath: null,
                ex);
        }

        try
        {
            await CopyIntoLiveAsync(inspection.Path, ct).ConfigureAwait(false);
            await _migrate(ct).ConfigureAwait(false);
            await VerifyLiveAsync(ct).ConfigureAwait(false);
            return new RestoreOutcome(safety, inspection.SchemaVersion, MigrationRunner.LatestKnownVersion);
        }
        catch (Exception ex)
        {
            try
            {
                // Sin cancelación: volver a la base anterior no se interrumpe.
                await CopyIntoLiveAsync(safety, CancellationToken.None).ConfigureAwait(false);
                await VerifyLiveAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception rollback)
            {
                throw new RestoreFailedException(
                    $"No se pudo restaurar el respaldo ni volver a la base anterior. Cierre el programa y pida ayuda: la base anterior está en «{safety}».",
                    rolledBack: false,
                    safety,
                    new AggregateException(ex, rollback));
            }

            throw new RestoreFailedException(
                $"No se pudo restaurar el respaldo; se volvió a la base anterior, que sigue en uso. Detalle: {ex.Message}",
                rolledBack: true,
                safety,
                ex);
        }
    }

    /// <summary>Copia el archivo sobre la base en uso con la API de backup (una sola transacción de escritura).</summary>
    private async Task CopyIntoLiveAsync(string sourcePath, CancellationToken ct)
    {
        await using var source = new SqliteConnection(ReadOnly(sourcePath));
        await source.OpenAsync(ct).ConfigureAwait(false);
        await using var live = await _database.OpenAsync(ct).ConfigureAwait(false);
        source.BackupDatabase(live);
    }

    private async Task VerifyLiveAsync(CancellationToken ct)
    {
        var problem = await SqliteBackupService.CheckIntegrityAsync(_database.DatabasePath, ct).ConfigureAwait(false);
        if (problem is not null)
        {
            throw new IOException($"La base restaurada no superó la verificación de integridad: {problem}");
        }

        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);
        var version = await ScalarAsync(connection, "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;", ct);
        if (version != MigrationRunner.LatestKnownVersion)
        {
            throw new IOException($"La base restaurada quedó en el esquema v{version} y se esperaba v{MigrationRunner.LatestKnownVersion}.");
        }
    }

    private BackupFile Describe(FileInfo file)
    {
        var name = Path.GetFileNameWithoutExtension(file.Name);
        foreach (var (prefix, reason) in Prefixes)
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            // «pre-migracion-v1-20261118-101500» o «…-101500-2»: fecha y hora son los dos últimos
            // tramos, o los dos anteriores si hay un número de repetición.
            var parts = name.Split('-');
            var at = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
            if (parts.Length >= 2 && TryParseStamp(parts[^2], parts[^1], out var exact))
            {
                at = exact;
            }
            else if (parts.Length >= 3 && TryParseStamp(parts[^3], parts[^2], out var repeated))
            {
                at = repeated;
            }

            return new BackupFile(file.FullName, file.Name, reason, at, file.Length);
        }

        return new BackupFile(file.FullName, file.Name, BackupReason.Other, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero), file.Length);
    }

    private static bool TryParseStamp(string date, string time, out DateTimeOffset at) =>
        DateTimeOffset.TryParseExact(
            date + "-" + time, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out at);

    private static string ReadOnly(string path) => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadOnly,
        Pooling = false,
    }.ToString();

    private static async Task<int> ScalarAsync(SqliteConnection connection, string sql, CancellationToken ct, string? name = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (name is not null)
        {
            command.Parameters.AddWithValue("$name", name);
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }
}
