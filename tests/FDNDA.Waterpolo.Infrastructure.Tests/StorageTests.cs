using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Infrastructure.Diagnostics;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Storage;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;

namespace FDNDA.Waterpolo.Infrastructure.Tests;

public class StorageTests
{
    [Fact]
    public async Task Inicializar_crea_el_esquema_y_los_datos_demo_una_sola_vez()
    {
        await using var storage = await TempStorage.CreateAsync();
        var initializer = new StorageInitializer(storage.Paths, storage.Time, storage.Log);

        var second = await initializer.InitializeAsync();
        var third = await initializer.InitializeAsync();

        Assert.Equal(MigrationRunner.LatestKnownVersion, second.SchemaVersion);
        Assert.False(second.Migration.Applied);
        Assert.False(third.Migration.Applied);

        var matches = await new SqliteMatchRepository(storage.OpenDatabase()).ListAsync();
        Assert.Equal(2, matches.Count);
        Assert.All(matches, m => Assert.Equal(MatchStatus.Scheduled, m.Status));

        var first = await new MatchQueries(new SqliteMatchRepository(storage.OpenDatabase())).GetSnapshotAsync(DemoDataSeeder.FirstMatchId);
        Assert.Equal(13, first!.Home.Roster.Count);
        Assert.Equal(13, first.Away.Roster.Count);
        Assert.NotEqual(first.Home.TeamId, first.Away.TeamId);
        Assert.Equal(Enumerable.Range(1, 13), first.Home.Roster.Select(r => r.Cap));

        Assert.Equal(26L, await CountAsync(storage, "player"));
        Assert.Equal(26L, await CountAsync(storage, "tournament_roster_entry"));
        Assert.Equal(1L, await CountAsync(storage, "tournament"));
    }

    [Fact]
    public async Task Produccion_usa_otra_base_y_no_recibe_datos_demo()
    {
        var root = Path.Combine(Path.GetTempPath(), "fdnda-waterpolo-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var time = new ManualTimeProvider();
            var demo = AppPaths.ForProfile(AppProfile.Demo, root);
            var production = AppPaths.ForProfile(AppProfile.Produccion, root);
            await new StorageInitializer(demo, time, new NullLog()).InitializeAsync();
            await new StorageInitializer(production, time, new NullLog()).InitializeAsync();

            Assert.NotEqual(demo.DatabasePath, production.DatabasePath);
            Assert.Equal(2, (await new SqliteMatchRepository(new SqliteDatabase(demo.DatabasePath)).ListAsync()).Count);
            Assert.Empty(await new SqliteMatchRepository(new SqliteDatabase(production.DatabasePath)).ListAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Una_base_de_version_mas_nueva_no_se_abre()
    {
        await using var storage = await TempStorage.CreateAsync();
        await using (var connection = await storage.OpenDatabase().OpenAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO schema_migrations (version, name, applied_at_utc) VALUES (999, 'futura', '2030-01-01T00:00:00Z');";
            await command.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<DatabaseTooNewException>(() =>
            new StorageInitializer(storage.Paths, storage.Time, storage.Log).InitializeAsync());

        Assert.Equal(999, ex.DatabaseVersion);
    }

    [Fact]
    public async Task El_respaldo_es_una_copia_integra_con_los_datos_confirmados()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        await desk.GoalAsync(TeamSide.Home, started.Home.Roster[0].RosterEntryId);
        await desk.GoalAsync(TeamSide.Away, started.Away.Roster[0].RosterEntryId);

        var backupPath = Path.Combine(storage.Paths.BackupDirectory, "prueba.db");
        var service = new SqliteBackupService(storage.OpenDatabase());
        var created = await service.CreateBackupAsync(backupPath);

        Assert.True(File.Exists(created));
        Assert.Null(await SqliteBackupService.CheckIntegrityAsync(created));

        var restored = await new MatchQueries(new SqliteMatchRepository(new SqliteDatabase(created))).GetSnapshotAsync(TestDesk.MatchId);
        Assert.Equal(1, restored!.Home.Score);
        Assert.Equal(1, restored.Away.Score);
        Assert.Equal(2, restored.Incidents.Count);

        // No se sobrescribe un respaldo existente.
        await Assert.ThrowsAsync<IOException>(() => service.CreateBackupAsync(backupPath));
    }

    [Fact]
    public async Task Un_archivo_danado_no_supera_la_verificacion_de_integridad()
    {
        await using var storage = await TempStorage.CreateAsync();
        var corrupt = Path.Combine(storage.Root, "danado.db");
        await File.WriteAllBytesAsync(corrupt, Enumerable.Repeat((byte)0x5A, 8192).ToArray());

        Assert.NotNull(await SqliteBackupService.CheckIntegrityAsync(corrupt));
    }

    [Fact]
    public async Task No_se_abren_dos_instancias_sobre_los_mismos_datos()
    {
        await using var storage = await TempStorage.CreateAsync();

        using (var first = InstanceLock.TryAcquire(storage.Paths.LockFilePath))
        {
            Assert.NotNull(first);
            Assert.Null(InstanceLock.TryAcquire(storage.Paths.LockFilePath));
        }

        using var afterRelease = InstanceLock.TryAcquire(storage.Paths.LockFilePath);
        Assert.NotNull(afterRelease);
    }

    [Fact]
    public async Task El_log_rota_y_queda_acotado()
    {
        await using var storage = await TempStorage.CreateAsync();
        var log = new RotatingFileLog(storage.Paths.LogDirectory, storage.Time, maxBytes: 2_000, maxFiles: 3);

        for (var i = 0; i < 400; i++)
        {
            log.Info($"Evento de diagnóstico número {i}");
        }

        var files = Directory.GetFiles(storage.Paths.LogDirectory, "waterpolo*.log");
        Assert.InRange(files.Length, 2, 3);
        Assert.All(files, f => Assert.True(new FileInfo(f).Length < 4_000));
    }

    private static async Task<long> CountAsync(TempStorage storage, string table)
    {
        await using var connection = await storage.OpenDatabase().OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
