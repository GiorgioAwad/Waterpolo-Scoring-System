using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Backups;
using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;
using Microsoft.Data.Sqlite;

namespace FDNDA.Waterpolo.Infrastructure.Tests;

/// <summary>R5 — respaldo y restauración desde la aplicación sobre SQLite temporal (RF10).</summary>
public class BackupRestoreTests
{
    [Fact]
    public async Task Respaldar_y_restaurar_en_otra_carpeta_conserva_partidos_incidencias_revisiones_y_reglas()
    {
        await using var origin = await TempStorage.CreateAsync();
        var desk = await new TestDesk(origin).SignInAsync(origin);
        var catalog = new CatalogOperations(desk.Catalog, origin.Time, origin.Log, demoData: true);
        var custom = RulesetSnapshot.ForEvent(4, 420, 120, 180, 2, 28, 18, false);
        Assert.True((await catalog.UpdateTournamentRulesAsync(DemoDataSeeder.TournamentId, custom)).IsSuccess);
        var started = await desk.StartAsync();
        await desk.GoalAsync(TeamSide.Home, started.Home.Roster[0].RosterEntryId);
        var second = (await desk.GoalAsync(TeamSide.Home, started.Home.Roster[1].RosterEntryId)).Snapshot!;
        var corrected = await desk.Operations.CorrectIncidentAsync(new CorrectIncident(
            Guid.NewGuid(), TestDesk.MatchId, second.Incidents[^1].IncidentId, TeamSide.Away, second.Away.Roster[2].RosterEntryId,
            "Goleador corregido", desk.Operator));
        Assert.True(corrected.IsSuccess, corrected.Message);
        var finished = (await desk.Operations.FinishMatchAsync(new FinishMatch(Guid.NewGuid(), TestDesk.MatchId, desk.Operator))).Snapshot!;
        var created = await Backups(origin, desk).CreateAsync(BackupReason.AfterMatch);
        Assert.True(created.IsSuccess, created.Message);

        await using var target = await TempStorage.CreateAsync();
        var targetDesk = await new TestDesk(target).SignInAsync(target, "Operadora de otra instalación");
        var operations = Backups(target, targetDesk);
        var preview = await operations.PreviewRestoreAsync(created.Path!);
        var restored = await operations.RestoreAsync(created.Path!);

        Assert.True(preview.CanRestore, preview.Message);
        Assert.Contains("2 partidos y 2 incidencias", preview.Message, StringComparison.Ordinal);
        Assert.Equal(CommandOutcome.Applied, restored.Outcome);
        Assert.StartsWith("Respaldo restaurado.", restored.Message, StringComparison.Ordinal);
        var after = (await new TestDesk(target).Queries.GetSnapshotAsync(TestDesk.MatchId))!;
        Assert.Equal(MatchStatus.Finished, after.Status);
        Assert.Equal((1, 1), (after.Home.Score, after.Away.Score));
        Assert.Equal(finished.Version, after.Version);
        Assert.Equal(
            finished.Incidents.Select(i => (i.IncidentId, i.Side, i.Cap, i.Revisions.Count)),
            after.Incidents.Select(i => (i.IncidentId, i.Side, i.Cap, i.Revisions.Count)));
        Assert.Equal("Goleador corregido", after.Incidents[^1].Revisions[^1].Reason);
        Assert.Equal(finished.Rules, after.Rules);
        Assert.Equal(custom, (await new SqliteCatalogRepository(target.OpenDatabase()).GetAsync(DemoDataSeeder.TournamentId))!.Rules);
        Assert.Null(await SqliteBackupService.CheckIntegrityAsync(target.Paths.DatabasePath));
        Assert.True(File.Exists(restored.Path)); // la base anterior de esta instalación quedó guardada
    }

    [Fact]
    public async Task No_se_restaura_con_un_partido_en_juego_y_no_se_toca_la_base()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var operations = Backups(storage, desk);
        var backup = (await operations.CreateAsync(BackupReason.Manual)).Path!;
        await desk.StartAsync();

        var preview = await operations.PreviewRestoreAsync(backup);
        var result = await operations.RestoreAsync(backup);

        Assert.False(preview.CanRestore);
        Assert.Equal(CommandOutcome.Rejected, result.Outcome);
        Assert.Contains("partido en juego", result.Message, StringComparison.Ordinal);
        Assert.Equal(MatchStatus.InProgress, (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!.Status);
        Assert.DoesNotContain(await operations.ListAsync(), f => f.Reason == BackupReason.BeforeRestore);
    }

    [Fact]
    public async Task Un_archivo_danado_o_ajeno_no_se_restaura()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var operations = Backups(storage, desk);
        var corrupt = Path.Combine(storage.Root, "danado.db");
        await File.WriteAllBytesAsync(corrupt, [.. Enumerable.Range(0, 4096).Select(i => (byte)(i * 7))]);
        var foreign = Path.Combine(storage.Root, "otra-base.db");
        await using (var connection = await new SqliteDatabase(foreign).OpenAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE notas (texto TEXT);";
            await command.ExecuteNonQueryAsync();
        }

        var damaged = await operations.RestoreAsync(corrupt);
        var other = await operations.RestoreAsync(foreign);
        var missing = await operations.PreviewRestoreAsync(Path.Combine(storage.Root, "no-existe.db"));
        var itself = await operations.PreviewRestoreAsync(storage.Paths.DatabasePath);

        Assert.Equal(CommandOutcome.Rejected, damaged.Outcome);
        Assert.Contains("dañado", damaged.Message, StringComparison.Ordinal);
        Assert.Contains("no es un respaldo del Marcador", other.Message, StringComparison.Ordinal);
        Assert.Contains("No se encontró", missing.Message, StringComparison.Ordinal);
        Assert.Contains("base en uso", itself.Message, StringComparison.Ordinal);
        Assert.Empty(await operations.ListAsync());
        Assert.Equal(2, (await desk.Queries.ListAsync()).Count);
    }

    [Fact]
    public async Task Un_respaldo_de_una_version_mas_nueva_no_se_restaura()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var operations = Backups(storage, desk);
        var backup = (await operations.CreateAsync(BackupReason.Manual)).Path!;
        await using (var connection = await new SqliteDatabase(backup).OpenAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO schema_migrations VALUES (99, 'futura', '2030-01-01T00:00:00Z');";
            await command.ExecuteNonQueryAsync();
        }

        var result = await operations.RestoreAsync(backup);

        Assert.Equal(CommandOutcome.Rejected, result.Outcome);
        Assert.Contains("versión más nueva", result.Message, StringComparison.Ordinal);
        Assert.Contains("v99", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Si_falla_despues_de_copiar_se_vuelve_a_la_base_anterior()
    {
        await using var origin = await TempStorage.CreateAsync();
        var originDesk = await new TestDesk(origin).SignInAsync(origin);
        var backup = (await Backups(origin, originDesk).CreateAsync(BackupReason.Manual)).Path!;

        await using var target = await TempStorage.CreateAsync();
        var desk = await new TestDesk(target).SignInAsync(target);
        var catalog = new CatalogOperations(desk.Catalog, target.Time, target.Log, demoData: true);
        Assert.True((await catalog.RenameTournamentAsync(DemoDataSeeder.TournamentId, "Torneo que no debe perderse")).IsSuccess);
        var failing = new SqliteBackupStore(
            target.OpenDatabase(), target.Paths.BackupDirectory, target.Time,
            _ => throw new IOException("Fallo simulado al migrar la base restaurada."));
        var operations = new BackupOperations(failing, desk.Repository, target.Log, AppProfile.Demo);

        var result = await operations.RestoreAsync(backup);

        Assert.Equal(CommandOutcome.Failed, result.Outcome);
        Assert.Contains("se volvió a la base anterior", result.Message, StringComparison.Ordinal);
        Assert.Equal("Torneo que no debe perderse", (await catalog.GetTournamentAsync(DemoDataSeeder.TournamentId))!.Name);
        Assert.Null(await SqliteBackupService.CheckIntegrityAsync(target.Paths.DatabasePath));
        Assert.Contains(await operations.ListAsync(), f => f.Reason == BackupReason.BeforeRestore && f.Path == result.Path);
    }

    [Fact]
    public async Task Un_respaldo_de_esquema_anterior_se_restaura_y_se_migra()
    {
        await using var target = await TempStorage.CreateAsync();
        var versionOne = await IncidentMigrationTests.CreateVersionOneAsync(target);
        var desk = await new TestDesk(target).SignInAsync(target);
        var operations = Backups(target, desk);

        var preview = await operations.PreviewRestoreAsync(versionOne.DatabasePath);
        var result = await operations.RestoreAsync(versionOne.DatabasePath);

        Assert.Contains($"se actualizará al esquema v{MigrationRunner.LatestKnownVersion}", preview.Message, StringComparison.Ordinal);
        Assert.Contains("partido en juego", preview.Message, StringComparison.Ordinal);
        Assert.Equal(CommandOutcome.Applied, result.Outcome);
        Assert.Contains($"del esquema v1 al v{MigrationRunner.LatestKnownVersion}", result.Message, StringComparison.Ordinal);
        var recovered = (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!;
        Assert.Equal(MatchStatus.InProgress, recovered.Status);
        Assert.Equal(1, recovered.Away.Score);
        Assert.Contains(await operations.ListAsync(), f => f.Reason == BackupReason.BeforeMigration);
    }

    [Fact]
    public async Task Produccion_no_restaura_un_respaldo_con_datos_de_demostracion()
    {
        await using var demo = await TempStorage.CreateAsync();
        var demoDesk = await new TestDesk(demo).SignInAsync(demo);
        var backup = (await Backups(demo, demoDesk).CreateAsync(BackupReason.Manual)).Path!;
        await using var production = await TempStorage.CreateAsync(AppProfile.Produccion);
        var desk = new TestDesk(production);
        var operations = new BackupOperations(
            new SqliteBackupStore(production.OpenDatabase(), production.Paths.BackupDirectory, production.Time),
            desk.Repository, production.Log, AppProfile.Produccion);

        var result = await operations.RestoreAsync(backup);

        Assert.Equal(CommandOutcome.Rejected, result.Outcome);
        Assert.Contains("datos de demostración", result.Message, StringComparison.Ordinal);
        Assert.Empty(await desk.Queries.ListAsync());
    }

    [Fact]
    public async Task Los_respaldos_se_listan_del_mas_reciente_al_mas_antiguo_con_su_motivo()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var operations = Backups(storage, desk);

        await operations.CreateAsync(BackupReason.BeforeMatch);
        storage.Time.Advance(TimeSpan.FromMinutes(50));
        await operations.CreateAsync(BackupReason.AfterMatch);
        await operations.CreateAsync(BackupReason.Manual);
        await operations.CreateAsync(BackupReason.Manual); // mismo segundo: no pisa al anterior

        var list = await operations.ListAsync();

        Assert.Equal(4, list.Count);
        Assert.Equal(BackupReason.BeforeMatch, list[^1].Reason);
        Assert.Equal(storage.Time.GetUtcNow().AddMinutes(-50), list[^1].CreatedAtUtc);
        Assert.Equal(2, list.Count(f => f.Reason == BackupReason.Manual));
        Assert.Contains(list, f => f.FileName.EndsWith("-2.db", StringComparison.Ordinal) && f.CreatedAtUtc == storage.Time.GetUtcNow());
        Assert.All(list, f => Assert.True(f.SizeBytes > 0));
        Assert.All(list, f => Assert.StartsWith("respaldo-", f.FileName, StringComparison.Ordinal));
    }

    private static BackupOperations Backups(TempStorage storage, TestDesk desk) => new(
        new SqliteBackupStore(storage.OpenDatabase(), storage.Paths.BackupDirectory, storage.Time),
        desk.Repository,
        storage.Log,
        AppProfile.Demo);
}
