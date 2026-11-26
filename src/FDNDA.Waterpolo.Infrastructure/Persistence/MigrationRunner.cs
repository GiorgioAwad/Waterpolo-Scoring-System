using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace FDNDA.Waterpolo.Infrastructure.Persistence;

public sealed record MigrationReport(int PreviousVersion, int CurrentVersion, string? PreMigrationBackupPath)
{
    public bool Applied => CurrentVersion > PreviousVersion;
}

/// <summary>La base fue creada por una versión más nueva del programa.</summary>
public sealed class DatabaseTooNewException(int databaseVersion, int supportedVersion)
    : Exception($"La base de datos es de una versión más nueva ({databaseVersion}) que la que este programa admite ({supportedVersion}).")
{
    public int DatabaseVersion { get; } = databaseVersion;

    public int SupportedVersion { get; } = supportedVersion;
}

/// <summary>
/// Aplica migraciones versionadas embebidas (<c>NNNN_nombre.sql</c>), cada una en su
/// transacción. Si la base ya tenía datos, la respalda antes de migrar.
/// </summary>
public sealed class MigrationRunner
{
    private const string ResourceMarker = ".Persistence.Migrations.";

    private readonly SqliteDatabase _database;
    private readonly SqliteBackupService _backups;
    private readonly TimeProvider _time;

    public MigrationRunner(SqliteDatabase database, SqliteBackupService backups, TimeProvider time)
    {
        _database = database;
        _backups = backups;
        _time = time;
    }

    /// <summary>Versión de esquema más alta que conoce este programa.</summary>
    public static int LatestKnownVersion => LoadMigrations().Max(m => m.Version);

    public async Task<MigrationReport> MigrateAsync(string backupDirectory, CancellationToken ct = default)
    {
        var migrations = LoadMigrations();
        var latest = migrations.Max(m => m.Version);

        int current;
        await using (var connection = await _database.OpenAsync(ct).ConfigureAwait(false))
        {
            await using (var setup = connection.CreateCommand())
            {
                setup.CommandText =
                    """
                    PRAGMA journal_mode = WAL;
                    CREATE TABLE IF NOT EXISTS schema_migrations (
                        version         INTEGER PRIMARY KEY NOT NULL,
                        name            TEXT NOT NULL,
                        applied_at_utc  TEXT NOT NULL
                    );
                    """;
                await setup.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            current = await ReadVersionAsync(connection, ct).ConfigureAwait(false);
        }

        if (current > latest)
        {
            throw new DatabaseTooNewException(current, latest);
        }

        var pending = migrations.Where(m => m.Version > current).OrderBy(m => m.Version).ToList();
        if (pending.Count == 0)
        {
            return new MigrationReport(current, current, null);
        }

        // Respaldo previo solo si ya había un esquema con datos que proteger.
        string? backupPath = null;
        if (current > 0)
        {
            var stamp = _time.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            backupPath = Path.Combine(backupDirectory, $"pre-migracion-v{current}-{stamp}.db");
            await _backups.CreateBackupAsync(backupPath, ct).ConfigureAwait(false);
        }

        await using (var connection = await _database.OpenAsync(ct).ConfigureAwait(false))
        {
            foreach (var migration in pending)
            {
                // Las que reconstruyen tablas (procedimiento oficial de SQLite para cambiar un CHECK)
                // corren sin claves foráneas; el PRAGMA no tiene efecto dentro de una transacción.
                if (migration.DisablesForeignKeys)
                {
                    await PragmaAsync(connection, "PRAGMA foreign_keys = OFF;", ct).ConfigureAwait(false);
                }

                try
                {
                    await ApplyAsync(connection, migration, ct).ConfigureAwait(false);
                }
                finally
                {
                    if (migration.DisablesForeignKeys)
                    {
                        await PragmaAsync(connection, "PRAGMA foreign_keys = ON;", ct).ConfigureAwait(false);
                    }
                }
            }
        }

        return new MigrationReport(current, latest, backupPath);
    }

    private async Task ApplyAsync(SqliteConnection connection, Migration migration, CancellationToken ct)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var script = Db.Command(connection, transaction, migration.Sql))
        {
            await script.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        if (migration.DisablesForeignKeys)
        {
            // Sin claves foráneas activas, nada impidió dejar referencias rotas: se comprueba antes de confirmar.
            await using var check = Db.Command(connection, transaction, "PRAGMA foreign_key_check;");
            await using var reader = await check.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"La migración {migration.Version:0000}_{migration.Name} dejó referencias rotas en la tabla {reader.GetString(0)}; no se aplicó.");
            }
        }

        await using (var record = Db.Command(
            connection,
            transaction,
            "INSERT INTO schema_migrations (version, name, applied_at_utc) VALUES ($version, $name, $at);"))
        {
            record.With("$version", migration.Version)
                .With("$name", migration.Name)
                .With("$at", Db.Utc(_time.GetUtcNow()));
            await record.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static async Task PragmaAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using var command = Db.Command(connection, null, sql);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> GetCurrentVersionAsync(CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);
        return await ReadVersionAsync(connection, ct).ConfigureAwait(false);
    }

    private static async Task<int> ReadVersionAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static List<Migration> LoadMigrations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var migrations = new List<Migration>();

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            var markerIndex = resource.IndexOf(ResourceMarker, StringComparison.Ordinal);
            if (markerIndex < 0 || !resource.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // «0001_inicial.sql» → versión 1, nombre «inicial»
            var fileName = resource[(markerIndex + ResourceMarker.Length)..^4];
            var separator = fileName.IndexOf('_', StringComparison.Ordinal);
            if (separator <= 0 || !int.TryParse(fileName[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var version))
            {
                throw new InvalidOperationException($"Nombre de migración no válido: {resource}");
            }

            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"No se pudo leer la migración {resource}");
            using var reader = new StreamReader(stream);
            migrations.Add(new Migration(version, fileName[(separator + 1)..], reader.ReadToEnd()));
        }

        if (migrations.Count == 0)
        {
            throw new InvalidOperationException("No se encontraron migraciones embebidas.");
        }

        if (migrations.GroupBy(m => m.Version).Any(g => g.Count() > 1))
        {
            throw new InvalidOperationException("Hay migraciones con el mismo número de versión.");
        }

        return migrations;
    }

    /// <summary>Marca, en una línea propia del script, de las migraciones que reconstruyen tablas.</summary>
    private const string DisableForeignKeysMarker = "-- @desactivar-claves-foraneas";

    private sealed record Migration(int Version, string Name, string Sql)
    {
        public bool DisablesForeignKeys =>
            Sql.Split('\n').Any(line => line.Trim().Equals(DisableForeignKeysMarker, StringComparison.Ordinal));
    }
}
