using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Infrastructure.Persistence;

namespace FDNDA.Waterpolo.Infrastructure.Storage;

public sealed record StorageReport(AppPaths Paths, MigrationReport Migration, int SchemaVersion);

/// <summary>
/// Prepara el almacenamiento local una sola vez antes de habilitar la mesa: carpetas,
/// migraciones (con respaldo previo) y, solo en el perfil demo, los datos de demostración.
/// </summary>
public sealed class StorageInitializer(AppPaths paths, TimeProvider time, IDiagnosticLog log)
{
    public async Task<StorageReport> InitializeAsync(CancellationToken ct = default)
    {
        paths.EnsureDirectories();

        var database = new SqliteDatabase(paths.DatabasePath);
        var migrations = new MigrationRunner(database, new SqliteBackupService(database), time);
        var report = await migrations.MigrateAsync(paths.BackupDirectory, ct).ConfigureAwait(false);

        if (report.Applied)
        {
            log.Info($"Esquema migrado de v{report.PreviousVersion} a v{report.CurrentVersion}.");
        }

        if (paths.Profile == AppProfile.Demo)
        {
            var seeder = new DemoDataSeeder(database, new SqliteMatchRepository(database), time);
            await seeder.SeedAsync(ct).ConfigureAwait(false);
        }

        return new StorageReport(paths, report, report.CurrentVersion);
    }
}
