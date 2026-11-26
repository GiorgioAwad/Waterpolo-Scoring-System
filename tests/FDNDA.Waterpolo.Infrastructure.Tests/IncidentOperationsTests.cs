using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;
using Microsoft.Data.Sqlite;

namespace FDNDA.Waterpolo.Infrastructure.Tests;

public class IncidentOperationsTests
{
    [Theory]
    [InlineData(IncidentType.Goal, null, null)]
    [InlineData(IncidentType.Exclusion, null, ExclusionKind.Exclusion)]
    [InlineData(IncidentType.Exclusion, null, ExclusionKind.Expulsion)]
    [InlineData(IncidentType.PersonalFoul, null, null)]
    [InlineData(IncidentType.Penalty, PenaltyResult.Converted, null)]
    [InlineData(IncidentType.Penalty, PenaltyResult.NotConverted, null)]
    [InlineData(IncidentType.Timeout, null, null)]
    public async Task Cada_tipo_persiste_revisiones_rechaza_duplicados_y_se_recupera_sin_repetir_avisos(
        IncidentType type, PenaltyResult? penaltyResult, ExclusionKind? exclusionKind)
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        desk.Clock.Context = ClockContext.Unavailable(ClockSource.Simulated);
        var command = new RegisterIncident(Guid.NewGuid(), TestDesk.MatchId, type, TeamSide.Home,
            type == IncidentType.Timeout ? null : started.Home.Roster[0].RosterEntryId, desk.Operator,
            started.Version, penaltyResult, exclusionKind);
        var observedCommitted = false;
        desk.Hub.Published += (_, publication) =>
        {
            // Abrir otra conexión desde el suscriptor demuestra que el COMMIT ya es visible.
            using var connection = storage.OpenDatabase().OpenAsync().GetAwaiter().GetResult();
            using var query = connection.CreateCommand();
            query.CommandText = "SELECT COUNT(*) FROM processed_command WHERE command_id = $id;";
            query.Parameters.AddWithValue("$id", command.CommandId.ToString("D"));
            observedCommitted = (long)query.ExecuteScalar()! == 1;
        };
        var registered = await desk.Operations.RegisterIncidentAsync(command);
        Assert.Equal(CommandOutcome.Applied, registered.Outcome);
        Assert.True(observedCommitted);
        var publicationCount = desk.Publications.Count;
        var duplicate = await desk.Operations.RegisterIncidentAsync(command);
        Assert.Equal(CommandOutcome.AlreadyApplied, duplicate.Outcome);
        Assert.Equal(publicationCount, desk.Publications.Count);
        var expectedScore = type == IncidentType.Goal || penaltyResult == PenaltyResult.Converted ? 1 : 0;
        Assert.Equal(expectedScore, registered.Snapshot!.Home.Score);
        Assert.Equal(expectedScore == 1 ? MatchChangeKind.GoalRegistered : MatchChangeKind.IncidentRegistered,
            desk.Publications[^1].Change.Kind);

        storage.Time.AdvanceMs(500);
        var correctionCommand = new CorrectIncident(Guid.NewGuid(), TestDesk.MatchId, command.CommandId,
            TeamSide.Away, type == IncidentType.Timeout ? null : started.Away.Roster[1].RosterEntryId,
            "Equipo y jugador corregidos", desk.Operator, registered.Snapshot.Version, penaltyResult, exclusionKind);
        var corrected = await desk.Operations.CorrectIncidentAsync(correctionCommand);
        Assert.Equal(CommandOutcome.Applied, corrected.Outcome);
        Assert.Equal(MatchChangeKind.IncidentCorrected, desk.Publications[^1].Change.Kind);
        Assert.Equal(0, corrected.Snapshot!.Home.Score);
        Assert.Equal(expectedScore, corrected.Snapshot.Away.Score);
        publicationCount = desk.Publications.Count;
        Assert.Equal(CommandOutcome.AlreadyApplied, (await desk.Operations.CorrectIncidentAsync(correctionCommand)).Outcome);
        Assert.Equal(publicationCount, desk.Publications.Count);

        var reopened = await new TestDesk(storage).Operations.OpenMatchAsync(TestDesk.MatchId);
        var incident = Assert.Single(reopened.Snapshot!.Incidents);
        Assert.Equal(type, incident.Type);
        Assert.Equal(2, incident.Revisions.Count);
        Assert.Equal(1, incident.Period);
        Assert.Equal(ClockQuality.Unavailable, incident.Clock.Quality);
        Assert.Null(incident.Clock.GameClockRemainingMs);
        Assert.Null(incident.Clock.ShotClockRemainingMs);
        Assert.Equal(penaltyResult, incident.PenaltyResult);
        Assert.Equal(exclusionKind, incident.ExclusionKind);
        Assert.Equal(TeamSide.Home, incident.Revisions[0].Side);
        Assert.Equal(TeamSide.Away, incident.Revisions[1].Side);
        Assert.Equal("Equipo y jugador corregidos", incident.Revisions[1].Reason);
        Assert.Equal(storage.Time.GetUtcNow(), incident.Revisions[1].AtUtc);

        var voidCommand = new VoidIncident(Guid.NewGuid(), TestDesk.MatchId, command.CommandId, "Anulada en mesa", desk.Operator);
        var voided = await desk.Operations.VoidIncidentAsync(voidCommand);
        Assert.Equal(CommandOutcome.Applied, voided.Outcome);
        publicationCount = desk.Publications.Count;
        Assert.Equal(CommandOutcome.AlreadyApplied, (await desk.Operations.VoidIncidentAsync(voidCommand)).Outcome);
        Assert.Equal(publicationCount, desk.Publications.Count);
        var fromDisk = (await new TestDesk(storage).Queries.GetSnapshotAsync(TestDesk.MatchId))!;
        Assert.Equal(0, fromDisk.Home.Score);
        Assert.Equal(0, fromDisk.Away.Score);
        Assert.Empty(fromDisk.Scorers);
        var annulled = Assert.Single(fromDisk.Incidents);
        Assert.True(annulled.IsVoided);
        Assert.Equal(3, annulled.Revisions.Count);
        Assert.Equal(penaltyResult, annulled.Revisions[2].PenaltyResult);
        Assert.Equal(exclusionKind, annulled.Revisions[2].ExclusionKind);
    }

    [Fact]
    public async Task Cambiar_resultado_del_penal_y_detalle_de_exclusion_conserva_el_original()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        var player = started.Home.Roster[0].RosterEntryId;
        var penaltyId = Guid.NewGuid();
        var exclusionId = Guid.NewGuid();
        await desk.Operations.RegisterIncidentAsync(new RegisterIncident(penaltyId, TestDesk.MatchId, IncidentType.Penalty,
            TeamSide.Home, player, desk.Operator, PenaltyResult: PenaltyResult.NotConverted));
        await desk.Operations.RegisterIncidentAsync(new RegisterIncident(exclusionId, TestDesk.MatchId, IncidentType.Exclusion,
            TeamSide.Home, player, desk.Operator, ExclusionKind: ExclusionKind.Exclusion));
        await desk.Operations.CorrectIncidentAsync(new CorrectIncident(Guid.NewGuid(), TestDesk.MatchId, penaltyId,
            TeamSide.Home, player, "Sí fue convertido", desk.Operator, PenaltyResult: PenaltyResult.Converted));
        await desk.Operations.CorrectIncidentAsync(new CorrectIncident(Guid.NewGuid(), TestDesk.MatchId, exclusionId,
            TeamSide.Home, player, "Se registró expulsión", desk.Operator, ExclusionKind: ExclusionKind.Expulsion));

        var snapshot = (await new TestDesk(storage).Queries.GetSnapshotAsync(TestDesk.MatchId))!;
        Assert.Equal(1, snapshot.Home.Score);
        Assert.Equal(1, Assert.Single(snapshot.Scorers).Goals);
        var penalty = snapshot.Incidents.Single(i => i.IncidentId == penaltyId);
        Assert.Equal(PenaltyResult.NotConverted, penalty.Revisions[0].PenaltyResult);
        Assert.Equal(PenaltyResult.Converted, penalty.PenaltyResult);
        Assert.Equal(ExclusionKind.Exclusion, snapshot.Incidents[1].Revisions[0].ExclusionKind);
        Assert.Equal(ExclusionKind.Expulsion, snapshot.Incidents[1].ExclusionKind);
    }

    [Theory]
    [InlineData(IncidentType.Exclusion, null, ExclusionKind.Exclusion)]
    [InlineData(IncidentType.PersonalFoul, null, null)]
    [InlineData(IncidentType.Penalty, PenaltyResult.Converted, null)]
    [InlineData(IncidentType.Timeout, null, null)]
    public async Task Una_escritura_fallida_no_publica_ni_deja_datos_y_el_reintento_es_seguro(
        IncidentType type, PenaltyResult? result, ExclusionKind? exclusion)
    {
        await using var storage = await TempStorage.CreateAsync();
        var repository = new FailingSaveRepository(new SqliteMatchRepository(storage.OpenDatabase())) { FailSaves = false };
        var desk = await new TestDesk(storage, repository).SignInAsync(storage);
        var started = await desk.StartAsync();
        var count = desk.Publications.Count;
        var command = new RegisterIncident(Guid.NewGuid(), TestDesk.MatchId, type, TeamSide.Home,
            type == IncidentType.Timeout ? null : started.Home.Roster[0].RosterEntryId, desk.Operator,
            started.Version, result, exclusion);
        repository.FailSaves = true;
        Assert.Equal(CommandOutcome.Failed, (await desk.Operations.RegisterIncidentAsync(command)).Outcome);
        Assert.Equal(count, desk.Publications.Count);
        Assert.Empty((await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!.Incidents);
        Assert.False(await repository.IsCommandProcessedAsync(command.CommandId));
        repository.FailSaves = false;
        Assert.Equal(CommandOutcome.Applied, (await desk.Operations.RegisterIncidentAsync(command)).Outcome);
        Assert.Single((await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!.Incidents);
    }

    [Fact]
    public async Task Una_version_antigua_o_jugador_invalido_no_escribe_incidencias_nuevas()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        var command = new RegisterIncident(Guid.NewGuid(), TestDesk.MatchId, IncidentType.PersonalFoul,
            TeamSide.Home, started.Home.Roster[0].RosterEntryId, desk.Operator, started.Version);
        Assert.Equal(CommandOutcome.Applied, (await desk.Operations.RegisterIncidentAsync(command)).Outcome);
        var count = desk.Publications.Count;
        Assert.Equal(CommandOutcome.Rejected, (await desk.Operations.RegisterIncidentAsync(command with { CommandId = Guid.NewGuid() })).Outcome);
        Assert.Equal(CommandOutcome.Rejected, (await desk.Operations.RegisterIncidentAsync(command with
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = null, RosterEntryId = started.Away.Roster[0].RosterEntryId,
        })).Outcome);
        Assert.Equal(count, desk.Publications.Count);
        Assert.Single((await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!.Incidents);
    }

    [Fact]
    public async Task Un_fallo_dentro_de_la_transaccion_revierte_version_incidencia_y_comando()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        await using (var connection = await storage.OpenDatabase().OpenAsync())
        await using (var sql = connection.CreateCommand())
        {
            sql.CommandText = "CREATE TRIGGER fallo_prueba BEFORE INSERT ON incident_revision BEGIN SELECT RAISE(ABORT, 'Fallo de prueba'); END;";
            await sql.ExecuteNonQueryAsync();
        }

        var count = desk.Publications.Count;
        var command = new RegisterIncident(Guid.NewGuid(), TestDesk.MatchId, IncidentType.Penalty, TeamSide.Home,
            started.Home.Roster[0].RosterEntryId, desk.Operator, PenaltyResult: PenaltyResult.Converted);
        Assert.Equal(CommandOutcome.Failed, (await desk.Operations.RegisterIncidentAsync(command)).Outcome);
        Assert.Equal(count, desk.Publications.Count);
        var recovered = (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!;
        Assert.Equal(started.Version, recovered.Version);
        Assert.Empty(recovered.Incidents);
        Assert.False(await desk.Repository.IsCommandProcessedAsync(command.CommandId));
    }

    [Fact]
    public async Task Las_restricciones_nuevas_y_la_auditoria_siguen_protegidas_en_sqlite()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        await desk.StartAsync();
        var command = new RegisterIncident(Guid.NewGuid(), TestDesk.MatchId, IncidentType.Timeout, TeamSide.Home, null, desk.Operator);
        await desk.Operations.RegisterIncidentAsync(command);
        await using var connection = await storage.OpenDatabase().OpenAsync();
        foreach (var sql in new[]
        {
            "DELETE FROM match_incident;", "DELETE FROM incident_revision;",
            "UPDATE incident_revision SET penalty_result = 'Converted';",
            "INSERT INTO incident_revision SELECT incident_id, 2, 'Correction', side, roster_entry_id, 'Prueba', operator_id, operator_name, at_utc, 'Converted', exclusion_kind FROM incident_revision;",
        })
        {
            await using var query = connection.CreateCommand();
            query.CommandText = sql;
            await Assert.ThrowsAsync<SqliteException>(() => query.ExecuteNonQueryAsync());
        }
    }
}
