using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;

namespace FDNDA.Waterpolo.Application.Matches;

/// <summary>
/// Casos de uso que cambian el partido. Hay un único escritor: los comandos se ejecutan
/// de uno en uno. Flujo: cargar → validar → aplicar en el dominio → commit → publicar.
/// El snapshot solo se publica después de confirmar la escritura.
/// </summary>
public sealed class MatchOperations
{
    private const string StorageFailureMessage =
        "No se pudo guardar el cambio. El marcador no se modificó. Revise el disco y vuelva a intentar.";

    private readonly IMatchRepository _matches;
    private readonly ICatalogRepository _tournaments;
    private readonly IMatchSnapshotPublisher _publisher;
    private readonly IClockContextProvider _clock;
    private readonly TimeProvider _time;
    private readonly IDiagnosticLog _log;
    private readonly SemaphoreSlim _writer = new(1, 1);

    public MatchOperations(
        IMatchRepository matches,
        ICatalogRepository tournaments,
        IMatchSnapshotPublisher publisher,
        IClockContextProvider clock,
        TimeProvider time,
        IDiagnosticLog log)
    {
        _matches = matches;
        _tournaments = tournaments;
        _publisher = publisher;
        _clock = clock;
        _time = time;
        _log = log;
    }

    /// <summary>
    /// Abre un partido en la mesa: lo carga desde los datos persistidos y publica su estado.
    /// No cambia datos ni dispara animaciones.
    /// </summary>
    public async Task<CommandResult> OpenMatchAsync(Guid matchId, CancellationToken ct = default)
    {
        await _writer.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var match = await _matches.GetAsync(matchId, ct).ConfigureAwait(false);
            if (match is null)
            {
                return new CommandResult(CommandOutcome.Rejected, "El partido no existe.", null);
            }

            var snapshot = MatchSnapshotProjector.Project(match);
            _publisher.Publish(snapshot, new MatchChange(MatchChangeKind.Loaded));
            return new CommandResult(CommandOutcome.Applied, null, snapshot);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error("No se pudo abrir el partido.", ex);
            return new CommandResult(CommandOutcome.Failed, "No se pudo leer el partido desde el almacenamiento local.", null);
        }
        finally
        {
            _writer.Release();
        }
    }

    public Task<CommandResult> StartMatchAsync(StartMatch command, CancellationToken ct = default) =>
        ExecuteAsync(command.CommandId, nameof(StartMatch), command.MatchId, command.ExpectedVersion, async (match, now) =>
        {
            var active = await _matches.FindInProgressMatchIdAsync(ct).ConfigureAwait(false);
            if (active is not null && active != match.Id)
            {
                throw new DomainException(
                    "partido.otro_activo",
                    "Ya hay otro partido en juego. Finalícelo antes de iniciar este.");
            }

            var tournament = await _tournaments.GetAsync(match.TournamentId, ct).ConfigureAwait(false)
                ?? throw new DomainException("evento.no_existe", "No se encontró el evento del partido.");

            match.Start(tournament.Rules, command.Operator, now);
            return new MatchChange(MatchChangeKind.Started);
        }, ct);

    public Task<CommandResult> StartBreakAsync(StartBreak command, CancellationToken ct = default) =>
        ExecuteAsync(command.CommandId, nameof(StartBreak), command.MatchId, command.ExpectedVersion, (match, now) =>
        {
            match.StartBreak(command.Operator, now);
            return Task.FromResult(new MatchChange(MatchChangeKind.StageChanged));
        }, ct);

    public Task<CommandResult> StartNextPeriodAsync(StartNextPeriod command, CancellationToken ct = default) =>
        ExecuteAsync(command.CommandId, nameof(StartNextPeriod), command.MatchId, command.ExpectedVersion, (match, now) =>
        {
            match.StartNextPeriod(command.Operator, now);
            return Task.FromResult(new MatchChange(MatchChangeKind.StageChanged));
        }, ct);

    public Task<CommandResult> StartShootoutAsync(StartShootout command, CancellationToken ct = default) =>
        ExecuteAsync(command.CommandId, nameof(StartShootout), command.MatchId, command.ExpectedVersion, (match, now) =>
        {
            match.StartShootout(command.Operator, now);
            return Task.FromResult(new MatchChange(MatchChangeKind.StageChanged));
        }, ct);

    public Task<CommandResult> CancelShootoutAsync(CancelShootout command, CancellationToken ct = default) =>
        ExecuteAsync(command.CommandId, nameof(CancelShootout), command.MatchId, command.ExpectedVersion, (match, now) =>
        {
            match.CancelShootout(command.Reason, command.Operator, now);
            return Task.FromResult(new MatchChange(MatchChangeKind.StageChanged));
        }, ct);

    public Task<CommandResult> FinishMatchAsync(FinishMatch command, CancellationToken ct = default) =>
        ExecuteAsync(command.CommandId, nameof(FinishMatch), command.MatchId, command.ExpectedVersion, (match, now) =>
        {
            match.Finish(command.Operator, now);
            return Task.FromResult(new MatchChange(MatchChangeKind.Finished));
        }, ct);

    public Task<CommandResult> ReopenMatchAsync(ReopenMatch command, CancellationToken ct = default) =>
        ExecuteAsync(command.CommandId, nameof(ReopenMatch), command.MatchId, command.ExpectedVersion, async (match, now) =>
        {
            var active = await _matches.FindInProgressMatchIdAsync(ct).ConfigureAwait(false);
            if (active is not null && active != match.Id)
            {
                throw new DomainException(
                    "partido.otro_activo",
                    "Ya hay otro partido en juego. Finalícelo antes de reabrir este.");
            }

            match.Reopen(command.Reason, command.Operator, now);
            return new MatchChange(MatchChangeKind.Reopened);
        }, ct);

    /// <summary>
    /// Registra un gol. El contexto de reloj se captura de la fuente en este momento; si no
    /// hay señal vigente, el gol se guarda con contexto temporal no confirmado.
    /// </summary>
    public Task<CommandResult> RegisterGoalAsync(RegisterGoal command, CancellationToken ct = default) =>
        ExecuteAsync(command.CommandId, nameof(RegisterGoal), command.MatchId, command.ExpectedVersion, (match, now) =>
        {
            // El CommandId identifica también la incidencia: el reintento nunca crea otra.
            var incident = match.RegisterGoal(
                command.CommandId, command.Side, command.RosterEntryId, _clock.Capture(), command.Operator, now);
            return Task.FromResult(new MatchChange(MatchChangeKind.GoalRegistered, incident.Id));
        }, ct);

    public Task<CommandResult> CorrectGoalScorerAsync(CorrectGoalScorer command, CancellationToken ct = default) =>
        ExecuteAsync(command.CommandId, nameof(CorrectGoalScorer), command.MatchId, command.ExpectedVersion, (match, now) =>
        {
            match.CorrectGoalScorer(
                command.IncidentId, command.NewSide, command.NewRosterEntryId, command.Reason, command.Operator, now);
            return Task.FromResult(new MatchChange(MatchChangeKind.IncidentCorrected, command.IncidentId));
        }, ct);

    public Task<CommandResult> RegisterIncidentAsync(RegisterIncident command, CancellationToken ct = default) =>
        ExecuteAsync(command.CommandId, nameof(RegisterIncident), command.MatchId, command.ExpectedVersion, (match, now) =>
        {
            var incident = match.RegisterIncident(command.CommandId, command.Type, command.Side, command.RosterEntryId,
                _clock.Capture(), command.Operator, now, command.PenaltyResult, command.ExclusionKind);
            return Task.FromResult(new MatchChange(
                incident.CountsAsGoal ? MatchChangeKind.GoalRegistered : MatchChangeKind.IncidentRegistered, incident.Id));
        }, ct);

    public Task<CommandResult> CorrectIncidentAsync(CorrectIncident command, CancellationToken ct = default) =>
        ExecuteAsync(command.CommandId, nameof(CorrectIncident), command.MatchId, command.ExpectedVersion, (match, now) =>
        {
            match.CorrectIncident(command.IncidentId, command.NewSide, command.NewRosterEntryId, command.Reason,
                command.Operator, now, command.PenaltyResult, command.ExclusionKind);
            return Task.FromResult(new MatchChange(MatchChangeKind.IncidentCorrected, command.IncidentId));
        }, ct);

    public Task<CommandResult> VoidIncidentAsync(VoidIncident command, CancellationToken ct = default) =>
        ExecuteAsync(command.CommandId, nameof(VoidIncident), command.MatchId, command.ExpectedVersion, (match, now) =>
        {
            match.VoidIncident(command.IncidentId, command.Reason, command.Operator, now);
            return Task.FromResult(new MatchChange(MatchChangeKind.IncidentVoided, command.IncidentId));
        }, ct);

    /// <summary>
    /// Programa un partido de un evento. Congela en el partido el nombre del evento, los datos
    /// de cada equipo (con los colores y gorros elegidos para este partido) y la nómina vigente
    /// de cada equipo en el evento. No cambia el partido abierto en la mesa ni publica nada.
    /// El identificador del partido es también el del comando: reenviarlo no crea otro.
    /// </summary>
    public async Task<CommandResult> ScheduleMatchAsync(ScheduleMatch command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.MatchId == Guid.Empty)
        {
            return new CommandResult(CommandOutcome.Rejected, "El partido no tiene identificador.", null);
        }

        await _writer.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Domain.Matches.Match match;
            try
            {
                if (await _matches.IsCommandProcessedAsync(command.MatchId, ct).ConfigureAwait(false))
                {
                    var existing = await _matches.GetAsync(command.MatchId, ct).ConfigureAwait(false);
                    return new CommandResult(
                        CommandOutcome.AlreadyApplied,
                        "El partido ya estaba programado.",
                        existing is null ? null : MatchSnapshotProjector.Project(existing));
                }

                var tournament = await _tournaments.GetAsync(command.TournamentId, ct).ConfigureAwait(false);
                var home = await _tournaments.GetTeamAsync(command.HomeTeamId, ct).ConfigureAwait(false);
                var away = await _tournaments.GetTeamAsync(command.AwayTeamId, ct).ConfigureAwait(false);
                if (tournament is null)
                {
                    return new CommandResult(CommandOutcome.Rejected, "Elija el evento del partido.", null);
                }

                if (home is null || away is null)
                {
                    return new CommandResult(CommandOutcome.Rejected, "Elija el equipo local y el equipo visitante.", null);
                }

                var homeRoster = await _tournaments.GetTournamentRosterAsync(tournament.Id, home.Id, ct).ConfigureAwait(false);
                var awayRoster = await _tournaments.GetTournamentRosterAsync(tournament.Id, away.Id, ct).ConfigureAwait(false);

                try
                {
                    var roster = homeRoster
                        .Select(r => new MatchRosterEntry(Guid.NewGuid(), TeamSide.Home, r.PlayerId, r.PlayerName, new CapNumber(r.Cap)))
                        .Concat(awayRoster.Select(r =>
                            new MatchRosterEntry(Guid.NewGuid(), TeamSide.Away, r.PlayerId, r.PlayerName, new CapNumber(r.Cap))));

                    match = Domain.Matches.Match.Schedule(
                        command.MatchId,
                        tournament.Id,
                        tournament.Name,
                        command.Label,
                        command.ScheduledAtUtc,
                        ForMatch(home, command.HomeColorHex, command.HomeCapColor),
                        ForMatch(away, command.AwayColorHex, command.AwayCapColor),
                        roster);
                }
                catch (DomainException ex)
                {
                    return new CommandResult(CommandOutcome.Rejected, ex.Message, null);
                }

                await _matches.SaveAsync(match, new CommandStamp(command.MatchId, nameof(ScheduleMatch), _time.GetUtcNow()), ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Error("No se pudo programar el partido.", ex);
                return new CommandResult(
                    CommandOutcome.Failed, "No se pudo guardar el partido. Revise el disco y vuelva a intentar.", null);
            }

            return new CommandResult(CommandOutcome.Applied, null, MatchSnapshotProjector.Project(match));
        }
        finally
        {
            _writer.Release();
        }
    }

    /// <summary>
    /// Reprograma un partido aún no iniciado. Si es el abierto en la mesa, se publica sin
    /// animaciones; si no, la mesa y el marcador no cambian.
    /// </summary>
    public Task<CommandResult> RescheduleMatchAsync(RescheduleMatch command, CancellationToken ct = default) =>
        ExecuteAsync(command.CommandId, nameof(RescheduleMatch), command.MatchId, command.ExpectedVersion, (match, now) =>
        {
            match.Reschedule(command.Label, command.ScheduledAtUtc, command.HomeColorHex, command.HomeCapColor,
                command.AwayColorHex, command.AwayCapColor, command.Operator, now);
            return Task.FromResult(new MatchChange(MatchChangeKind.PreMatchChanged));
        }, ct, publishOnlyIfOpen: true);

    /// <summary>
    /// Reemplaza la nómina de un partido aún no iniciado por la vigente de sus dos equipos en el
    /// evento. Un jugador que sigue en la nómina conserva su identificador en el partido.
    /// </summary>
    public Task<CommandResult> RefreshMatchRosterAsync(RefreshMatchRoster command, CancellationToken ct = default) =>
        ExecuteAsync(command.CommandId, nameof(RefreshMatchRoster), command.MatchId, command.ExpectedVersion, async (match, now) =>
        {
            var home = await _tournaments.GetTournamentRosterAsync(match.TournamentId, match.Home.TeamId, ct).ConfigureAwait(false);
            var away = await _tournaments.GetTournamentRosterAsync(match.TournamentId, match.Away.TeamId, ct).ConfigureAwait(false);
            var current = match.Roster.ToDictionary(r => (r.Side, r.PlayerId), r => r.Id);
            MatchRosterEntry Entry(TeamSide side, TournamentRosterLine line) => new(
                current.TryGetValue((side, line.PlayerId), out var id) ? id : Guid.NewGuid(),
                side, line.PlayerId, line.PlayerName, new CapNumber(line.Cap));

            match.ReplaceRoster(
                home.Select(l => Entry(TeamSide.Home, l)).Concat(away.Select(l => Entry(TeamSide.Away, l))), command.Operator, now);
            return new MatchChange(MatchChangeKind.PreMatchChanged);
        }, ct, publishOnlyIfOpen: true);

    /// <summary>Anula un partido aún no iniciado, con motivo. Queda en la lista como «Anulado».</summary>
    public Task<CommandResult> CancelMatchAsync(CancelMatch command, CancellationToken ct = default) =>
        ExecuteAsync(command.CommandId, nameof(CancelMatch), command.MatchId, command.ExpectedVersion, (match, now) =>
        {
            match.Cancel(command.Reason, command.Operator, now);
            return Task.FromResult(new MatchChange(MatchChangeKind.PreMatchChanged));
        }, ct, publishOnlyIfOpen: true);

    private static MatchTeam ForMatch(Domain.Competitions.Team team, string? colorHex, string? capColor) =>
        new(
            team.Id,
            team.Code,
            team.Name,
            string.IsNullOrWhiteSpace(colorHex) ? team.ColorHex : colorHex,
            string.IsNullOrWhiteSpace(capColor) ? team.CapColor : capColor);

    private async Task<CommandResult> ExecuteAsync(
        Guid commandId,
        string commandType,
        Guid matchId,
        int? expectedVersion,
        Func<Match, DateTimeOffset, Task<MatchChange>> apply,
        CancellationToken ct,
        bool publishOnlyIfOpen = false)
    {
        if (commandId == Guid.Empty)
        {
            return new CommandResult(CommandOutcome.Rejected, "El comando no tiene identificador.", null);
        }

        await _writer.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Match? match;
            try
            {
                var alreadyProcessed = await _matches.IsCommandProcessedAsync(commandId, ct).ConfigureAwait(false);
                match = await _matches.GetAsync(matchId, ct).ConfigureAwait(false);
                if (alreadyProcessed)
                {
                    var current = match is null ? null : MatchSnapshotProjector.Project(match);
                    return new CommandResult(CommandOutcome.AlreadyApplied, "La acción ya estaba registrada.", current);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Error($"No se pudo leer el partido para {commandType}.", ex);
                return new CommandResult(CommandOutcome.Failed, StorageFailureMessage, null);
            }

            if (match is null)
            {
                return new CommandResult(CommandOutcome.Rejected, "El partido no existe.", null);
            }

            if (expectedVersion is { } expected && expected != match.Version)
            {
                return new CommandResult(
                    CommandOutcome.Rejected,
                    "El partido cambió mientras se enviaba la acción. Revise el estado y vuelva a intentar.",
                    MatchSnapshotProjector.Project(match));
            }

            var now = _time.GetUtcNow();
            MatchChange change;
            try
            {
                change = await apply(match, now).ConfigureAwait(false);
            }
            catch (DomainException ex)
            {
                // El agregado en memoria se descarta: el estado confirmado sigue en la base.
                return new CommandResult(CommandOutcome.Rejected, ex.Message, null);
            }

            try
            {
                await _matches.SaveAsync(match, new CommandStamp(commandId, commandType, now), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Error($"Falló la escritura de {commandType}.", ex);
                return new CommandResult(CommandOutcome.Failed, StorageFailureMessage, null);
            }

            var snapshot = MatchSnapshotProjector.Project(match);
            if (!publishOnlyIfOpen || _publisher.CurrentMatchId == match.Id)
            {
                _publisher.Publish(snapshot, change);
            }

            return new CommandResult(CommandOutcome.Applied, null, snapshot);
        }
        finally
        {
            _writer.Release();
        }
    }
}
