using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;
using Microsoft.Data.Sqlite;

namespace FDNDA.Waterpolo.Infrastructure.Tests;

/// <summary>Casos de uso reales sobre SQLite temporal: escritura, idempotencia, auditoría y recuperación.</summary>
public class MatchOperationsTests
{
    [Fact]
    public async Task Gol_se_guarda_y_solo_entonces_se_publica_el_nuevo_marcador()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        var scorer = started.Home.Roster[4];

        var result = await desk.GoalAsync(TeamSide.Home, scorer.RosterEntryId);

        Assert.Equal(CommandOutcome.Applied, result.Outcome);
        Assert.Equal(1, result.Snapshot!.Home.Score);
        Assert.Equal(0, result.Snapshot.Away.Score);

        var published = desk.Publications[^1];
        Assert.Equal(MatchChangeKind.GoalRegistered, published.Change.Kind);
        Assert.Same(result.Snapshot, published.Snapshot);
        Assert.Same(result.Snapshot, desk.Hub.Current);

        var incident = Assert.Single(result.Snapshot.Incidents);
        Assert.Equal(scorer.PlayerName, incident.PlayerName);
        Assert.Equal(scorer.Cap, incident.Cap);
        Assert.Equal(1, incident.Period);
        Assert.Equal(desk.Operator.Name, incident.RegisteredBy);
        Assert.Equal(ClockQuality.Live, incident.Clock.Quality);
        Assert.Equal(412_300, incident.Clock.GameClockRemainingMs);
        Assert.Equal(18_700, incident.Clock.ShotClockRemainingMs);
        Assert.Equal(storage.Time.GetUtcNow(), incident.RegisteredAtUtc);
    }

    [Fact]
    public async Task El_mismo_comando_de_gol_no_suma_dos_veces()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        var scorer = started.Home.Roster[0];
        var commandId = Guid.NewGuid();

        var first = await desk.GoalAsync(TeamSide.Home, scorer.RosterEntryId, commandId);
        var publicationsAfterFirst = desk.Publications.Count;
        var retry = await desk.GoalAsync(TeamSide.Home, scorer.RosterEntryId, commandId);

        Assert.Equal(CommandOutcome.Applied, first.Outcome);
        Assert.Equal(CommandOutcome.AlreadyApplied, retry.Outcome);
        Assert.True(retry.IsSuccess);
        Assert.Equal(1, retry.Snapshot!.Home.Score);
        Assert.Equal(publicationsAfterFirst, desk.Publications.Count); // el reintento no vuelve a publicar ni a celebrar

        var fromDisk = await desk.Queries.GetSnapshotAsync(TestDesk.MatchId);
        Assert.Equal(1, fromDisk!.Home.Score);
        Assert.Single(fromDisk.Incidents);
        Assert.Equal(1L, await ScalarAsync(storage, "SELECT COUNT(*) FROM match_incident;"));
    }

    [Fact]
    public async Task Se_rechaza_un_jugador_ajeno_al_equipo_sin_escribir_ni_publicar()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        var publications = desk.Publications.Count;

        var foreign = await desk.GoalAsync(TeamSide.Home, Guid.NewGuid());
        var otherTeam = await desk.GoalAsync(TeamSide.Home, started.Away.Roster[0].RosterEntryId);

        Assert.Equal(CommandOutcome.Rejected, foreign.Outcome);
        Assert.Contains("nómina", foreign.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CommandOutcome.Rejected, otherTeam.Outcome);
        Assert.Equal(publications, desk.Publications.Count);
        Assert.Equal(0L, await ScalarAsync(storage, "SELECT COUNT(*) FROM match_incident;"));
        Assert.Equal(started.Version, (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!.Version);
    }

    [Fact]
    public async Task Corregir_y_anular_conservan_la_auditoria_en_la_base()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        var goal = await desk.GoalAsync(TeamSide.Home, started.Home.Roster[0].RosterEntryId);
        var incidentId = goal.Snapshot!.Incidents[0].IncidentId;
        await desk.GoalAsync(TeamSide.Home, started.Home.Roster[1].RosterEntryId);

        storage.Time.Advance(TimeSpan.FromSeconds(30));
        var corrected = await desk.Operations.CorrectGoalScorerAsync(new CorrectGoalScorer(
            Guid.NewGuid(), TestDesk.MatchId, incidentId, TeamSide.Home, started.Home.Roster[2].RosterEntryId, "Gorro mal leído", desk.Operator));

        Assert.Equal(CommandOutcome.Applied, corrected.Outcome);
        Assert.Equal(2, corrected.Snapshot!.Home.Score); // corregir no duplica goles
        Assert.Equal(MatchChangeKind.IncidentCorrected, desk.Publications[^1].Change.Kind);
        Assert.Equal(started.Home.Roster[2].PlayerName, corrected.Snapshot.Incidents[0].PlayerName);
        Assert.DoesNotContain(corrected.Snapshot.Scorers, s => s.PlayerName == started.Home.Roster[0].PlayerName);

        storage.Time.Advance(TimeSpan.FromSeconds(30));
        var voided = await desk.Operations.VoidIncidentAsync(new VoidIncident(
            Guid.NewGuid(), TestDesk.MatchId, incidentId, "Gol invalidado", desk.Operator));

        Assert.Equal(CommandOutcome.Applied, voided.Outcome);
        Assert.Equal(1, voided.Snapshot!.Home.Score); // anular resta exactamente uno
        Assert.Equal(MatchChangeKind.IncidentVoided, desk.Publications[^1].Change.Kind);

        // Estado derivado de los datos persistidos, con una conexión nueva.
        var fromDisk = await new MatchQueries(new SqliteMatchRepository(storage.OpenDatabase())).GetSnapshotAsync(TestDesk.MatchId);
        var incident = fromDisk!.Incidents[0];
        Assert.True(incident.IsVoided);
        Assert.Equal(2, fromDisk.Incidents.Count); // sin borrado físico
        Assert.Equal(1, fromDisk.Home.Score);

        Assert.Collection(
            incident.Revisions,
            original =>
            {
                Assert.Equal(RevisionKind.Original, original.Kind);
                Assert.Equal(started.Home.Roster[0].PlayerName, original.PlayerName);
                Assert.Null(original.Reason);
            },
            correction =>
            {
                Assert.Equal(RevisionKind.Correction, correction.Kind);
                Assert.Equal(started.Home.Roster[2].PlayerName, correction.PlayerName);
                Assert.Equal("Gorro mal leído", correction.Reason);
                Assert.Equal(desk.Operator.Name, correction.OperatorName);
            },
            voidRevision =>
            {
                Assert.Equal(RevisionKind.Void, voidRevision.Kind);
                Assert.Equal("Gol invalidado", voidRevision.Reason);
                Assert.Equal(desk.Operator.Name, voidRevision.OperatorName);
                Assert.Equal(storage.Time.GetUtcNow(), voidRevision.AtUtc);
            });

        Assert.Equal(4L, await ScalarAsync(storage, "SELECT COUNT(*) FROM incident_revision;"));
    }

    [Fact]
    public async Task El_partido_se_recupera_desde_sqlite_tras_reiniciar()
    {
        await using var storage = await TempStorage.CreateAsync();

        // Primera «ejecución» del programa.
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        await desk.GoalAsync(TeamSide.Home, started.Home.Roster[0].RosterEntryId);
        await desk.GoalAsync(TeamSide.Away, started.Away.Roster[3].RosterEntryId);
        await desk.GoalAsync(TeamSide.Home, started.Home.Roster[0].RosterEntryId);
        await desk.Operations.StartBreakAsync(new StartBreak(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        var last = await desk.Operations.StartNextPeriodAsync(new StartNextPeriod(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));

        // Segunda «ejecución»: objetos nuevos, sin nada en memoria; el arranque no vuelve a sembrar ni migrar datos.
        var report = await new Storage.StorageInitializer(storage.Paths, storage.Time, storage.Log).InitializeAsync();
        var restarted = new TestDesk(storage);
        var activeId = await restarted.Queries.FindInProgressMatchIdAsync();
        var opened = await restarted.Operations.OpenMatchAsync(activeId!.Value);

        Assert.False(report.Migration.Applied);
        Assert.Equal(TestDesk.MatchId, activeId);
        Assert.Equal(CommandOutcome.Applied, opened.Outcome);
        var recovered = opened.Snapshot!;
        Assert.Equal(MatchStatus.InProgress, recovered.Status);
        Assert.Equal(MatchStage.Period, recovered.Stage);
        Assert.Equal(2, recovered.CurrentPeriod);
        Assert.Equal(2, recovered.Home.Score);
        Assert.Equal(1, recovered.Away.Score);
        Assert.Equal(3, recovered.Incidents.Count);
        Assert.Equal(last.Snapshot!.Version, recovered.Version);
        Assert.Equal(2, recovered.Scorers.Single(s => s.PlayerName == started.Home.Roster[0].PlayerName).Goals);

        // Abrir el partido publica el estado sin disparar animaciones de acciones pasadas.
        var publication = Assert.Single(restarted.Publications);
        Assert.Equal(MatchChangeKind.Loaded, publication.Change.Kind);
    }

    [Fact]
    public async Task Si_la_escritura_falla_no_se_reporta_exito_ni_cambia_el_marcador()
    {
        await using var storage = await TempStorage.CreateAsync();
        var failing = new FailingSaveRepository(new SqliteMatchRepository(storage.OpenDatabase())) { FailSaves = false };
        var desk = await new TestDesk(storage, failing).SignInAsync(storage);
        var started = await desk.StartAsync();
        var publications = desk.Publications.Count;
        var commandId = Guid.NewGuid();

        failing.FailSaves = true;
        var failed = await desk.GoalAsync(TeamSide.Home, started.Home.Roster[0].RosterEntryId, commandId);

        Assert.Equal(CommandOutcome.Failed, failed.Outcome);
        Assert.False(failed.IsSuccess);
        Assert.Contains("No se pudo guardar", failed.Message, StringComparison.Ordinal);
        Assert.Equal(publications, desk.Publications.Count);
        Assert.Equal(0, desk.Hub.Current!.Home.Score); // el marcador anterior se conserva
        Assert.Equal(0L, await ScalarAsync(storage, "SELECT COUNT(*) FROM match_incident;"));
        Assert.NotEmpty(storage.Log.Errors);

        // Reintentar el mismo comando cuando el disco vuelve a responder lo aplica una sola vez.
        failing.FailSaves = false;
        var retried = await desk.GoalAsync(TeamSide.Home, started.Home.Roster[0].RosterEntryId, commandId);
        Assert.Equal(CommandOutcome.Applied, retried.Outcome);
        Assert.Equal(1, retried.Snapshot!.Home.Score);
    }

    [Fact]
    public async Task Solo_puede_haber_un_partido_en_juego()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        await desk.StartAsync();

        var second = await desk.Operations.StartMatchAsync(new StartMatch(Guid.NewGuid(), DemoDataSeeder.SecondMatchId, desk.Operator));

        Assert.Equal(CommandOutcome.Rejected, second.Outcome);
        Assert.Contains("otro partido", second.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(MatchStatus.Scheduled, (await desk.Queries.GetSnapshotAsync(DemoDataSeeder.SecondMatchId))!.Status);
    }

    [Fact]
    public async Task Una_version_esperada_antigua_se_rechaza()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        await desk.GoalAsync(TeamSide.Home, started.Home.Roster[0].RosterEntryId);

        var stale = await desk.Operations.RegisterGoalAsync(new RegisterGoal(
            Guid.NewGuid(), TestDesk.MatchId, TeamSide.Home, started.Home.Roster[1].RosterEntryId, desk.Operator, ExpectedVersion: started.Version));

        Assert.Equal(CommandOutcome.Rejected, stale.Outcome);
        Assert.Equal(1, (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!.Home.Score);
    }

    [Fact]
    public async Task El_repositorio_detecta_escrituras_concurrentes_sobre_la_misma_version()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        var repository = new SqliteMatchRepository(storage.OpenDatabase());

        var copyA = (await repository.GetAsync(TestDesk.MatchId))!;
        var copyB = (await repository.GetAsync(TestDesk.MatchId))!;
        var now = storage.Time.GetUtcNow();
        copyA.RegisterGoal(Guid.NewGuid(), TeamSide.Home, started.Home.Roster[0].RosterEntryId, ClockContext.Unknown, desk.Operator, now);
        copyB.RegisterGoal(Guid.NewGuid(), TeamSide.Home, started.Home.Roster[1].RosterEntryId, ClockContext.Unknown, desk.Operator, now);

        await repository.SaveAsync(copyA, new CommandStamp(Guid.NewGuid(), "Prueba", now));
        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            repository.SaveAsync(copyB, new CommandStamp(Guid.NewGuid(), "Prueba", now)));

        Assert.Equal(1L, await ScalarAsync(storage, "SELECT COUNT(*) FROM match_incident;")); // la segunda se revirtió completa
    }

    [Fact]
    public async Task Cambiar_las_reglas_del_evento_no_altera_un_partido_iniciado()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();

        await ExecuteAsync(storage, "UPDATE tournament SET rules_period_count = 2, rules_period_seconds = 300, rules_shot_clock_seconds = 25;");

        var afterChange = await desk.Queries.GetSnapshotAsync(TestDesk.MatchId);
        Assert.Equal(started.Rules, afterChange!.Rules);
        Assert.Equal(4, afterChange.Rules!.PeriodCount);
        Assert.Equal(480, afterChange.Rules.PeriodDurationSeconds);

        // Un partido aún no iniciado sí toma la configuración vigente del evento al iniciar.
        await desk.Operations.FinishMatchAsync(new FinishMatch(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        var second = await desk.Operations.StartMatchAsync(new StartMatch(Guid.NewGuid(), DemoDataSeeder.SecondMatchId, desk.Operator));
        Assert.Equal(2, second.Snapshot!.Rules!.PeriodCount);
        Assert.Equal(300, second.Snapshot.Rules.PeriodDurationSeconds);
    }

    [Fact]
    public async Task Sin_senal_vigente_el_gol_se_guarda_con_contexto_temporal_no_confirmado()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        desk.Clock.Context = ClockContext.Unavailable(ClockSource.Simulated);

        var result = await desk.GoalAsync(TeamSide.Away, started.Away.Roster[0].RosterEntryId);

        Assert.Equal(CommandOutcome.Applied, result.Outcome);
        Assert.Equal(1, result.Snapshot!.Away.Score);
        var clock = (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!.Incidents[0].Clock;
        Assert.Equal(ClockQuality.Unavailable, clock.Quality);
        Assert.Equal(ClockSource.Simulated, clock.Source);
        Assert.Null(clock.GameClockRemainingMs);
        Assert.Null(clock.ShotClockRemainingMs);
    }

    [Fact]
    public async Task Reabrir_un_cierre_exige_motivo_y_queda_atribuido()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        await desk.StartAsync();
        await desk.Operations.FinishMatchAsync(new FinishMatch(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));

        var withoutReason = await desk.Operations.ReopenMatchAsync(new ReopenMatch(Guid.NewGuid(), TestDesk.MatchId, " ", desk.Operator));
        var reopened = await desk.Operations.ReopenMatchAsync(
            new ReopenMatch(Guid.NewGuid(), TestDesk.MatchId, "Falta registrar un gol", desk.Operator));

        Assert.Equal(CommandOutcome.Rejected, withoutReason.Outcome);
        Assert.Equal(CommandOutcome.Applied, reopened.Outcome);
        Assert.Equal(MatchStatus.InProgress, reopened.Snapshot!.Status);

        var history = (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!.History;
        Assert.Equal(
            new[] { MatchStatusChangeKind.Started, MatchStatusChangeKind.Finished, MatchStatusChangeKind.Reopened },
            history.Select(h => h.Kind).ToArray());
        Assert.Equal("Falta registrar un gol", history[^1].Reason);
        Assert.Equal(desk.Operator.Name, history[^1].OperatorName);
    }

    [Fact]
    public async Task La_base_impide_borrar_o_modificar_incidencias_confirmadas()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        await desk.GoalAsync(TeamSide.Home, started.Home.Roster[0].RosterEntryId);

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(storage, "DELETE FROM match_incident;"));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(storage, "UPDATE match_incident SET period = 3;"));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(storage, "DELETE FROM incident_revision;"));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(storage, "UPDATE incident_revision SET side = 'Away';"));

        Assert.Equal(1, (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!.Home.Score);
    }

    private static async Task<long> ScalarAsync(TempStorage storage, string sql)
    {
        await using var connection = await storage.OpenDatabase().OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(TempStorage storage, string sql)
    {
        await using var connection = await storage.OpenDatabase().OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
