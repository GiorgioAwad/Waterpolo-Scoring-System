using System.Text;
using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Application.Reports;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Infrastructure.Export;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;
using Microsoft.Data.Sqlite;

namespace FDNDA.Waterpolo.Infrastructure.Tests;

/// <summary>R8 — prórroga y tanda de penales sobre SQLite temporal (RF04, RF05, RF09).</summary>
public class OvertimeShootoutOperationsTests
{
    /// <summary>Valores de prueba: no son un reglamento aprobado.</summary>
    private static readonly RulesetSnapshot WithOvertimeAndShootout =
        RulesetSnapshot.ForEvent(4, 8 * 60, 2 * 60, 3 * 60, 2, 30, 20, penaltyShootoutEnabled: true,
            overtimePeriodCount: 1, overtimePeriodDurationSeconds: 3 * 60);

    [Fact]
    public async Task Las_reglas_de_prorroga_se_guardan_en_el_evento_y_se_copian_al_partido()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, desk) = await CreateAsync(storage);

        var saved = await catalog.UpdateTournamentRulesAsync(DemoDataSeeder.TournamentId, WithOvertimeAndShootout);
        var started = await desk.StartAsync();

        Assert.Equal(CommandOutcome.Applied, saved.Outcome);
        var stored = (await catalog.GetTournamentAsync(DemoDataSeeder.TournamentId))!.Rules;
        Assert.Equal((1, 180, true), (stored.OvertimePeriodCount, stored.OvertimePeriodDurationSeconds, stored.PenaltyShootoutEnabled));
        Assert.Equal(RulesetSnapshot.CustomPresetName, stored.PresetName);
        Assert.Equal((1, 180, true), (started.Rules!.OvertimePeriodCount, started.Rules.OvertimePeriodDurationSeconds, started.Rules.PenaltyShootoutEnabled));

        var afterRestart = (await new TestDesk(storage).Queries.GetSnapshotAsync(TestDesk.MatchId))!;
        Assert.Equal(started.Rules, afterRestart.Rules);
    }

    [Fact]
    public async Task Partido_con_prorroga_y_tanda_se_recupera_tras_reiniciar_con_la_tanda_separada()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await PlayToShootoutAsync(storage);

        var started = await desk.Operations.StartShootoutAsync(new StartShootout(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        Assert.Equal(CommandOutcome.Applied, started.Outcome);
        Assert.Equal(MatchStage.Shootout, started.Snapshot!.Stage);
        var home = started.Snapshot.Home.Roster;
        var away = started.Snapshot.Away.Roster;

        await AttemptAsync(desk, TeamSide.Home, home[0].RosterEntryId, converted: true);
        await AttemptAsync(desk, TeamSide.Away, away[0].RosterEntryId, converted: true);
        var tied = await desk.Operations.FinishMatchAsync(new FinishMatch(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        await AttemptAsync(desk, TeamSide.Home, home[1].RosterEntryId, converted: true);
        await AttemptAsync(desk, TeamSide.Away, away[1].RosterEntryId, converted: false);
        var finished = await desk.Operations.FinishMatchAsync(new FinishMatch(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));

        Assert.Equal(CommandOutcome.Rejected, tied.Outcome);
        Assert.Equal("La tanda está empatada. Registre los lanzamientos que faltan antes de finalizar.", tied.Message);
        Assert.Equal(CommandOutcome.Applied, finished.Outcome);

        var recovered = (await new TestDesk(storage).Queries.GetSnapshotAsync(TestDesk.MatchId))!;
        Assert.Equal(MatchStatus.Finished, recovered.Status);
        Assert.Equal((2, 2), (recovered.Home.Score, recovered.Away.Score));
        Assert.Equal(4, recovered.Scorers.Sum(s => s.Goals)); // goles de juego; los de la tanda no cuentan
        Assert.Equal((2, 1), (recovered.Shootout!.HomeScore, recovered.Shootout.AwayScore));
        Assert.Equal(4, recovered.Shootout.Attempts.Count);
        Assert.Equal(
            [MatchStatusChangeKind.Started, MatchStatusChangeKind.ShootoutStarted, MatchStatusChangeKind.Finished],
            recovered.History.Select(h => h.Kind).Where(k => k is MatchStatusChangeKind.Started or MatchStatusChangeKind.ShootoutStarted or MatchStatusChangeKind.Finished));
        Assert.Contains(recovered.History, h => h.Kind == MatchStatusChangeKind.PeriodStarted && h.Period == 5);
        Assert.Null(recovered.ShootoutBlocker);
    }

    [Fact]
    public async Task Iniciar_la_tanda_dos_veces_con_el_mismo_comando_no_duplica_el_cambio()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await PlayToShootoutAsync(storage);
        var command = new StartShootout(Guid.NewGuid(), TestDesk.MatchId, desk.Operator);

        var first = await desk.Operations.StartShootoutAsync(command);
        var again = await desk.Operations.StartShootoutAsync(command);

        Assert.Equal(CommandOutcome.Applied, first.Outcome);
        Assert.Equal(CommandOutcome.AlreadyApplied, again.Outcome);
        var snapshot = (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!;
        Assert.Single(snapshot.History, h => h.Kind == MatchStatusChangeKind.ShootoutStarted);
    }

    [Fact]
    public async Task La_tanda_anulada_queda_en_el_historial_con_su_motivo_y_libera_el_resultado()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await PlayToShootoutAsync(storage);
        var started = (await desk.Operations.StartShootoutAsync(new StartShootout(Guid.NewGuid(), TestDesk.MatchId, desk.Operator))).Snapshot!;
        var attempt = await AttemptAsync(desk, TeamSide.Home, started.Home.Roster[0].RosterEntryId, converted: true);

        var blocked = await desk.Operations.CancelShootoutAsync(new CancelShootout(Guid.NewGuid(), TestDesk.MatchId, "Por error", desk.Operator));
        Assert.True((await desk.Operations.VoidIncidentAsync(new VoidIncident(
            Guid.NewGuid(), TestDesk.MatchId, attempt, "Tanda iniciada por error", desk.Operator))).IsSuccess);
        var cancelled = await desk.Operations.CancelShootoutAsync(
            new CancelShootout(Guid.NewGuid(), TestDesk.MatchId, "Faltaba un gol del cuarto periodo", desk.Operator));

        Assert.Equal(CommandOutcome.Rejected, blocked.Outcome);
        Assert.Equal(CommandOutcome.Applied, cancelled.Outcome);
        var recovered = (await new TestDesk(storage).Queries.GetSnapshotAsync(TestDesk.MatchId))!;
        Assert.Null(recovered.Shootout);
        Assert.Equal(MatchStage.Break, recovered.Stage);
        var change = recovered.History[^1];
        Assert.Equal((MatchStatusChangeKind.ShootoutCancelled, "Faltaba un gol del cuarto periodo"), (change.Kind, change.Reason));
        Assert.True((await desk.GoalAsync(TeamSide.Home, started.Home.Roster[2].RosterEntryId)).IsSuccess);
    }

    [Fact]
    public async Task La_base_rechaza_un_lanzamiento_sin_resultado_y_no_deja_borrarlo()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await PlayToShootoutAsync(storage);
        var started = (await desk.Operations.StartShootoutAsync(new StartShootout(Guid.NewGuid(), TestDesk.MatchId, desk.Operator))).Snapshot!;
        var attempt = await AttemptAsync(desk, TeamSide.Home, started.Home.Roster[0].RosterEntryId, converted: true);

        await using var connection = await storage.OpenDatabase().OpenAsync();
        await using var withoutResult = connection.CreateCommand();
        withoutResult.CommandText = """
            INSERT INTO incident_revision (incident_id, revision_number, kind, side, roster_entry_id, reason, operator_id, operator_name, at_utc)
            SELECT incident_id, 2, 'Correction', side, roster_entry_id, 'Sin resultado', operator_id, operator_name, at_utc
            FROM incident_revision WHERE incident_id = $id;
            """;
        withoutResult.Parameters.AddWithValue("$id", attempt.ToString("D"));
        await using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM match_incident WHERE type = 'ShootoutAttempt';";

        await Assert.ThrowsAsync<SqliteException>(() => withoutResult.ExecuteNonQueryAsync());
        await Assert.ThrowsAsync<SqliteException>(() => delete.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task El_resumen_separa_la_tanda_del_resultado_y_la_exporta_aparte()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await PlayToShootoutAsync(storage);
        var started = (await desk.Operations.StartShootoutAsync(new StartShootout(Guid.NewGuid(), TestDesk.MatchId, desk.Operator))).Snapshot!;
        await AttemptAsync(desk, TeamSide.Home, started.Home.Roster[0].RosterEntryId, converted: false);
        await AttemptAsync(desk, TeamSide.Away, started.Away.Roster[0].RosterEntryId, converted: true);
        await desk.Operations.FinishMatchAsync(new FinishMatch(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        var reports = new ReportOperations(desk.Repository, desk.Catalog, new ReportFileWriter(storage.Time), storage.Time, storage.Log);

        var match = (await reports.GetMatchReportAsync(TestDesk.MatchId))!;
        var tournament = (await reports.GetTournamentReportAsync(DemoDataSeeder.TournamentId))!;
        var document = (await reports.MatchDocumentAsync(TestDesk.MatchId))!;
        var path = Path.Combine(storage.Root, "partido-tanda.csv");
        Assert.True((await reports.ExportAsync(document, ReportFormat.Csv, path)).IsSuccess);
        var csv = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path));
        var eventCsvPath = Path.Combine(storage.Root, "evento-tanda.csv");
        Assert.True((await reports.ExportAsync((await reports.TournamentDocumentAsync(DemoDataSeeder.TournamentId))!, ReportFormat.Csv, eventCsvPath)).IsSuccess);
        var eventCsv = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(eventCsvPath));

        Assert.Equal((2, 2), (match.Home.Totals.Goals, match.Away.Totals.Goals));
        Assert.True(match.WentToOvertime);
        Assert.Equal((0, 1), (match.Shootout!.HomeGoals, match.Shootout.AwayGoals));
        Assert.DoesNotContain(match.Incidents, i => i.Type == IncidentType.ShootoutAttempt);
        Assert.Equal([1, 1], match.Incidents.Where(i => i.Period == 5).Select(i => i.OvertimeNumber));
        Assert.All(match.Incidents.Where(i => i.Period <= 4), i => Assert.Null(i.OvertimeNumber));
        Assert.Equal(4, match.Scorers.Sum(s => s.Goals));
        Assert.Equal(2, tournament.Teams.Single(t => t.TeamId == DemoDataSeeder.HomeTeamId).GoalsFor);
        Assert.Equal(("2 – 2 (pr.)", "0 – 1"), (ReportDocuments.ResultText(match), ReportDocuments.ShootoutText(match)));
        Assert.Contains(document.Sections, s => s.Heading == "Tanda de penales" && s.Rows.Count == 2);
        Assert.Contains("Tanda de penales", csv, StringComparison.Ordinal);
        Assert.Contains("No convertido", csv, StringComparison.Ordinal);
        Assert.Contains("Pr. 1", csv, StringComparison.Ordinal);
        Assert.Contains("Tanda", eventCsv, StringComparison.Ordinal);
        Assert.Contains("0 – 1", eventCsv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migrar_a_la_version_actual_reconstruye_las_tablas_sin_perder_datos_ni_protecciones()
    {
        await using var storage = await TempStorage.CreateAsync();
        var database = await IncidentMigrationTests.CreateVersionOneAsync(storage);
        var before = await CountsAsync(database);

        await new MigrationRunner(database, new SqliteBackupService(database), storage.Time).MigrateAsync(storage.Paths.BackupDirectory);

        Assert.Equal(before, await CountsAsync(database));
        await using var connection = await database.OpenAsync();
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
        Assert.Equal(1L, await ScalarAsync(connection, "PRAGMA foreign_keys;"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_schema WHERE name = 'ux_match_single_in_progress';"));
        Assert.Equal(5L, await ScalarAsync(connection, """
            SELECT COUNT(*) FROM sqlite_schema WHERE type = 'trigger' AND name IN (
                'trg_match_incident_no_delete', 'trg_match_incident_no_update', 'trg_match_status_change_no_delete',
                'trg_match_status_change_no_update', 'trg_incident_revision_validate_insert');
            """));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_schema WHERE name LIKE '%\\_new' ESCAPE '\\';"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT rules_overtime_period_count FROM tournament;"));
        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE match_incident SET period = 2;";
        await Assert.ThrowsAsync<SqliteException>(() => update.ExecuteNonQueryAsync());

        // El partido en juego de v1 sigue operable tras la migración.
        var desk = await new TestDesk(storage, new SqliteMatchRepository(database)).SignInAsync(storage);
        var snapshot = (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!;
        Assert.Equal((0, 1), (snapshot.Home.Score, snapshot.Away.Score));
        Assert.Equal(0, snapshot.Rules!.OvertimePeriodCount);
    }

    // ---------- Apoyo ----------

    private static async Task<(CatalogOperations Catalog, TestDesk Desk)> CreateAsync(TempStorage storage)
    {
        var desk = await new TestDesk(storage).SignInAsync(storage);
        return (new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true), desk);
    }

    /// <summary>Reglas con una prórroga y tanda; 1–1 en el reglamentario, 2–2 tras la prórroga; en el descanso final.</summary>
    private static async Task<TestDesk> PlayToShootoutAsync(TempStorage storage)
    {
        var (catalog, desk) = await CreateAsync(storage);
        Assert.True((await catalog.UpdateTournamentRulesAsync(DemoDataSeeder.TournamentId, WithOvertimeAndShootout)).IsSuccess);
        var started = await desk.StartAsync();
        var home = started.Home.Roster;
        var away = started.Away.Roster;
        Assert.True((await desk.GoalAsync(TeamSide.Home, home[0].RosterEntryId)).IsSuccess);
        Assert.True((await desk.GoalAsync(TeamSide.Away, away[0].RosterEntryId)).IsSuccess);

        for (var period = 1; period <= 4; period++)
        {
            Assert.True((await desk.Operations.StartBreakAsync(new StartBreak(Guid.NewGuid(), TestDesk.MatchId, desk.Operator))).IsSuccess);
            var next = await desk.Operations.StartNextPeriodAsync(new StartNextPeriod(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
            Assert.True(next.IsSuccess, next.Message);
        }

        var overtime = (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!;
        Assert.True(overtime.IsOvertime);
        Assert.Equal(1, overtime.OvertimeNumber);
        Assert.True((await desk.GoalAsync(TeamSide.Home, home[1].RosterEntryId)).IsSuccess);
        Assert.True((await desk.GoalAsync(TeamSide.Away, away[1].RosterEntryId)).IsSuccess);
        Assert.True((await desk.Operations.StartBreakAsync(new StartBreak(Guid.NewGuid(), TestDesk.MatchId, desk.Operator))).IsSuccess);
        Assert.True((await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!.CanStartShootout);
        return desk;
    }

    private static async Task<Guid> AttemptAsync(TestDesk desk, TeamSide side, Guid rosterEntryId, bool converted)
    {
        var command = new RegisterIncident(Guid.NewGuid(), TestDesk.MatchId, IncidentType.ShootoutAttempt, side, rosterEntryId, desk.Operator,
            PenaltyResult: converted ? PenaltyResult.Converted : PenaltyResult.NotConverted);
        var result = await desk.Operations.RegisterIncidentAsync(command);
        Assert.Equal(CommandOutcome.Applied, result.Outcome);
        Assert.Equal(MatchChangeKind.IncidentRegistered, desk.Publications[^1].Change.Kind);
        return command.CommandId;
    }

    private static async Task<(long, long, long, long)> CountsAsync(SqliteDatabase database)
    {
        await using var connection = await database.OpenAsync();
        return (
            await ScalarAsync(connection, "SELECT COUNT(*) FROM match;"),
            await ScalarAsync(connection, "SELECT COUNT(*) FROM match_incident;"),
            await ScalarAsync(connection, "SELECT COUNT(*) FROM incident_revision;"),
            await ScalarAsync(connection, "SELECT COUNT(*) FROM match_status_change;"));
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
