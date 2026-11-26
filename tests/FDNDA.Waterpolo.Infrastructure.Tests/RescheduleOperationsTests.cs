using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Application.Reports;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Infrastructure.Export;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;
using Microsoft.Data.Sqlite;

namespace FDNDA.Waterpolo.Infrastructure.Tests;

/// <summary>R10 — reprogramar, actualizar la nómina y anular partidos aún no iniciados, sobre SQLite temporal.</summary>
public class RescheduleOperationsTests
{
    private static readonly DateTimeOffset NewDate = new(2027, 3, 15, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reprogramar_guarda_descripcion_fecha_colores_y_gorros_y_sobrevive_al_reinicio()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var before = (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!;

        var result = await desk.Operations.RescheduleMatchAsync(new RescheduleMatch(
            Guid.NewGuid(), TestDesk.MatchId, "Final adelantada", NewDate, "#FFFFFF", "Blanco", "#212121", "Negro", desk.Operator, before.Version));

        Assert.Equal(CommandOutcome.Applied, result.Outcome);
        var stored = (await new SqliteMatchRepository(storage.OpenDatabase()).GetAsync(TestDesk.MatchId))!;
        Assert.Equal("Final adelantada", stored.Label);
        Assert.Equal(NewDate, stored.ScheduledAtUtc);
        Assert.Equal(("#FFFFFF", "Blanco"), (stored.Home.ColorHex, stored.Home.CapColor));
        Assert.Equal(("#212121", "Negro"), (stored.Away.ColorHex, stored.Away.CapColor));
        Assert.Equal(before.Home.Name, stored.Home.Name);
        Assert.Equal(MatchStatus.Scheduled, stored.Status);
        Assert.Equal(MatchStatusChangeKind.Rescheduled, Assert.Single(stored.StatusChanges).Kind);
        Assert.Contains(await desk.Queries.ListAsync(), m => m.MatchId == TestDesk.MatchId && m.Label == "Final adelantada" && m.ScheduledAtUtc == NewDate);
    }

    [Fact]
    public async Task Reprogramar_el_partido_abierto_lo_publica_sin_animar_y_otro_no_cambia_la_mesa()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        await desk.Operations.OpenMatchAsync(TestDesk.MatchId);
        var publications = desk.Publications.Count;

        var other = (await desk.Queries.GetSnapshotAsync(DemoDataSeeder.SecondMatchId))!;
        await desk.Operations.RescheduleMatchAsync(new RescheduleMatch(
            Guid.NewGuid(), DemoDataSeeder.SecondMatchId, "Vuelta", null, other.Home.ColorHex, other.Home.CapColor,
            other.Away.ColorHex, other.Away.CapColor, desk.Operator));

        Assert.Equal(publications, desk.Publications.Count);
        Assert.Equal(TestDesk.MatchId, desk.Hub.Current!.MatchId);

        var open = desk.Hub.Current!;
        await desk.Operations.RescheduleMatchAsync(new RescheduleMatch(
            Guid.NewGuid(), TestDesk.MatchId, "Ida", null, open.Home.ColorHex, open.Home.CapColor,
            open.Away.ColorHex, open.Away.CapColor, desk.Operator));

        var published = desk.Publications[^1];
        Assert.Equal(MatchChangeKind.PreMatchChanged, published.Change.Kind);
        Assert.Equal("Ida", published.Snapshot.Label);
    }

    [Fact]
    public async Task Reprogramar_con_el_mismo_comando_no_repite_el_cambio()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var s = (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!;
        var command = new RescheduleMatch(
            Guid.NewGuid(), TestDesk.MatchId, "Otra descripción", null, s.Home.ColorHex, s.Home.CapColor, s.Away.ColorHex, s.Away.CapColor, desk.Operator);

        Assert.Equal(CommandOutcome.Applied, (await desk.Operations.RescheduleMatchAsync(command)).Outcome);
        Assert.Equal(CommandOutcome.AlreadyApplied, (await desk.Operations.RescheduleMatchAsync(command)).Outcome);

        var stored = (await desk.Repository.GetAsync(TestDesk.MatchId))!;
        Assert.Single(stored.StatusChanges);
    }

    [Fact]
    public async Task Un_partido_iniciado_no_se_reprograma_ni_se_anula_ni_cambia_su_nomina()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();

        var reschedule = await desk.Operations.RescheduleMatchAsync(new RescheduleMatch(
            Guid.NewGuid(), TestDesk.MatchId, "Otra", NewDate, started.Home.ColorHex, started.Home.CapColor,
            started.Away.ColorHex, started.Away.CapColor, desk.Operator));
        var roster = await desk.Operations.RefreshMatchRosterAsync(new RefreshMatchRoster(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        var cancel = await desk.Operations.CancelMatchAsync(new CancelMatch(Guid.NewGuid(), TestDesk.MatchId, "Error", desk.Operator));

        Assert.All([reschedule, roster, cancel], r => Assert.Equal(CommandOutcome.Rejected, r.Outcome));
        Assert.Contains("ya fue iniciado", reschedule.Message);
        Assert.Equal(MatchStatus.InProgress, (await desk.Repository.GetAsync(TestDesk.MatchId))!.Status);
    }

    [Fact]
    public async Task Actualizar_la_nomina_copia_la_vigente_del_evento_al_partido_no_iniciado()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var catalog = new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true);
        var roster = await catalog.GetTournamentRosterAsync(DemoDataSeeder.TournamentId, DemoDataSeeder.HomeTeamId);
        Assert.Equal(CommandOutcome.Applied, (await catalog.AddRosterPlayerAsync(
            DemoDataSeeder.TournamentId, DemoDataSeeder.HomeTeamId, Guid.NewGuid(), Guid.NewGuid(), "Jugadora Ficticia Nueva", 14)).Outcome);
        Assert.Equal(CommandOutcome.Applied, (await catalog.UpdateRosterPlayerAsync(
            DemoDataSeeder.TournamentId, DemoDataSeeder.HomeTeamId, roster[0].EntryId, roster[0].PlayerName, 20)).Outcome);
        Assert.Equal(CommandOutcome.Applied, (await catalog.RemoveRosterPlayerAsync(
            DemoDataSeeder.TournamentId, DemoDataSeeder.HomeTeamId, roster[1].EntryId)).Outcome);
        Assert.Equal(13, (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!.Home.Roster.Count); // antes: congelada

        var result = await desk.Operations.RefreshMatchRosterAsync(new RefreshMatchRoster(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));

        Assert.Equal(CommandOutcome.Applied, result.Outcome);
        var home = (await new MatchQueries(new SqliteMatchRepository(storage.OpenDatabase())).GetSnapshotAsync(TestDesk.MatchId))!.Home.Roster;
        Assert.Equal(13, home.Count);
        Assert.Contains(home, r => r.PlayerName == "Jugadora Ficticia Nueva" && r.Cap == 14);
        Assert.Contains(home, r => r.PlayerName == roster[0].PlayerName && r.Cap == 20);
        Assert.DoesNotContain(home, r => r.PlayerName == roster[1].PlayerName);
        Assert.Equal(13, result.Snapshot!.Home.Roster.Count);

        // Sin cambios en el evento, volver a actualizar no escribe nada.
        var again = await desk.Operations.RefreshMatchRosterAsync(new RefreshMatchRoster(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        Assert.Equal(CommandOutcome.Rejected, again.Outcome);
        Assert.Contains("ya coincide", again.Message);
    }

    [Fact]
    public async Task Tras_actualizar_la_nomina_se_registran_goles_de_los_jugadores_nuevos()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var catalog = new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true);
        await catalog.AddRosterPlayerAsync(DemoDataSeeder.TournamentId, DemoDataSeeder.HomeTeamId, Guid.NewGuid(), Guid.NewGuid(), "Jugadora Ficticia Nueva", 14);
        await desk.Operations.RefreshMatchRosterAsync(new RefreshMatchRoster(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));

        var started = await desk.StartAsync();
        var nueva = Assert.Single(started.Home.Roster, r => r.Cap == 14);
        var goal = await desk.GoalAsync(TeamSide.Home, nueva.RosterEntryId);

        Assert.Equal(CommandOutcome.Applied, goal.Outcome);
        Assert.Equal(1, goal.Snapshot!.Home.Score);
    }

    [Fact]
    public async Task Anular_conserva_el_partido_con_motivo_y_lo_saca_de_resultados_y_conteos()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var catalog = new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true);

        var sinMotivo = await desk.Operations.CancelMatchAsync(new CancelMatch(Guid.NewGuid(), TestDesk.MatchId, " ", desk.Operator));
        Assert.Equal(CommandOutcome.Rejected, sinMotivo.Outcome);

        var result = await desk.Operations.CancelMatchAsync(new CancelMatch(Guid.NewGuid(), TestDesk.MatchId, "Programado por error", desk.Operator));

        Assert.Equal(CommandOutcome.Applied, result.Outcome);
        var stored = (await desk.Repository.GetAsync(TestDesk.MatchId))!;
        Assert.Equal(MatchStatus.Cancelled, stored.Status);
        Assert.Equal("Programado por error", Assert.Single(stored.StatusChanges).Reason);
        Assert.Contains(await desk.Queries.ListAsync(), m => m.MatchId == TestDesk.MatchId && m.Status == MatchStatus.Cancelled);

        var tournament = Assert.Single(await catalog.ListTournamentsAsync(), t => t.TournamentId == DemoDataSeeder.TournamentId);
        Assert.Equal(1, tournament.MatchCount); // el anulado no cuenta
        Assert.Equal(1, tournament.ScheduledMatchCount);

        var start = await desk.Operations.StartMatchAsync(new StartMatch(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        Assert.Equal(CommandOutcome.Rejected, start.Outcome);
        Assert.Contains("anulado", start.Message);
    }

    [Fact]
    public async Task El_partido_anulado_aparece_como_tal_en_el_resumen_del_evento_sin_sumar()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        await desk.Operations.CancelMatchAsync(new CancelMatch(Guid.NewGuid(), TestDesk.MatchId, "Programado por error", desk.Operator));
        var reports = new ReportOperations(desk.Repository, desk.Catalog, new ReportFileWriter(storage.Time), storage.Time, storage.Log);

        var report = (await reports.GetTournamentReportAsync(DemoDataSeeder.TournamentId))!;

        var cancelled = Assert.Single(report.Matches, m => m.MatchId == TestDesk.MatchId);
        Assert.False(cancelled.HasBeenPlayed);
        var document = ReportDocuments.ForTournament(report, storage.Time.GetUtcNow(), TimeZoneInfo.Utc);
        Assert.Contains(document.Lines, l => l.Contains("1 anulado", StringComparison.Ordinal));
        Assert.Contains(document.Sections[0].Rows, row => row.Contains("Anulado"));
    }

    [Fact]
    public async Task Migrar_una_base_antigua_permite_anular_el_programado_y_protege_la_nomina_del_partido_en_juego()
    {
        await using var storage = await TempStorage.CreateAsync();
        var database = await IncidentMigrationTests.CreateVersionOneAsync(storage);

        await new MigrationRunner(database, new SqliteBackupService(database), storage.Time).MigrateAsync(storage.Paths.BackupDirectory);

        OperatorRef v1Operator;
        await using (var connection = await database.OpenAsync())
        {
            await using var who = connection.CreateCommand();
            who.CommandText = "SELECT id FROM operator_profile LIMIT 1;";
            v1Operator = new OperatorRef(Guid.Parse((string)(await who.ExecuteScalarAsync())!), "Operador ficticio v1");

            await using var triggers = connection.CreateCommand();
            triggers.CommandText = """
                SELECT COUNT(*) FROM sqlite_schema WHERE type = 'trigger' AND name IN (
                    'trg_match_roster_entry_no_delete', 'trg_match_roster_entry_no_update',
                    'trg_match_status_change_no_delete', 'trg_match_status_change_no_update');
                """;
            Assert.Equal(4L, (long)(await triggers.ExecuteScalarAsync())!);
        }

        // En la base v1 el primer partido está en juego y el segundo, programado.
        var desk = new TestDesk(storage, new SqliteMatchRepository(database));
        var cancel = await desk.Operations.CancelMatchAsync(new CancelMatch(Guid.NewGuid(), DemoDataSeeder.SecondMatchId, "Duplicado", v1Operator));
        var roster = await desk.Operations.RefreshMatchRosterAsync(new RefreshMatchRoster(Guid.NewGuid(), TestDesk.MatchId, v1Operator));

        Assert.Equal(CommandOutcome.Applied, cancel.Outcome);
        Assert.Equal(CommandOutcome.Rejected, roster.Outcome);
        var inPlay = (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!;
        Assert.Equal((0, 1), (inPlay.Home.Score, inPlay.Away.Score));
    }

    [Fact]
    public async Task La_base_impide_cambiar_el_estado_a_uno_desconocido_y_borrar_la_nomina_de_un_partido_iniciado()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        await desk.StartAsync();

        await using var connection = new SqliteConnection($"Data Source={storage.Paths.DatabasePath}");
        await connection.OpenAsync();
        await using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM match_roster_entry WHERE match_id = $id;";
        delete.Parameters.AddWithValue("$id", TestDesk.MatchId.ToString("D"));
        var ex = await Assert.ThrowsAsync<SqliteException>(() => delete.ExecuteNonQueryAsync());
        Assert.Contains("nómina", ex.Message);

        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE match SET status = 'Borrado' WHERE id = $id;";
        update.Parameters.AddWithValue("$id", TestDesk.MatchId.ToString("D"));
        await Assert.ThrowsAsync<SqliteException>(() => update.ExecuteNonQueryAsync());
    }
}
