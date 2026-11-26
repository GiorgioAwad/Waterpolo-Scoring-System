using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Domain.Statistics;

namespace FDNDA.Waterpolo.Application.Matches;

// Proyección inmutable del partido para la mesa y el marcador. Las ventanas observan
// estos snapshots; nunca recalculan el marcador por su cuenta.

public sealed record MatchSnapshot(
    Guid MatchId,
    int Version,
    string TournamentName,
    string Label,
    MatchStatus Status,
    MatchStage Stage,
    int CurrentPeriod,
    RulesSummary? Rules,
    TeamSnapshot Home,
    TeamSnapshot Away,
    IReadOnlyList<IncidentSnapshot> Incidents,
    IReadOnlyList<ScorerSnapshot> Scorers,
    IReadOnlyList<StatusChangeSnapshot> History,
    ShootoutSnapshot? Shootout,
    string? ShootoutBlocker,
    IReadOnlyList<PlayerFoulSnapshot>? Fouls = null)
{
    /// <summary>Faltas vigentes por jugador (personales, exclusiones y expulsiones); solo los que tienen.</summary>
    public IReadOnlyList<PlayerFoulSnapshot> PlayerFouls => Fouls ?? [];

    /// <summary>Faltas vigentes del jugador; 0 si no tiene.</summary>
    public int FoulsOf(Guid rosterEntryId) => PlayerFouls.FirstOrDefault(f => f.RosterEntryId == rosterEntryId)?.Fouls ?? 0;

    /// <summary>Verdadero si el periodo en curso (o recién jugado, en el descanso) es de prórroga.</summary>
    public bool IsOvertime => Rules is { } rules && CurrentPeriod > rules.PeriodCount;

    /// <summary>Número de la prórroga en curso (1, 2…); 0 en tiempo reglamentario.</summary>
    public int OvertimeNumber => IsOvertime ? CurrentPeriod - Rules!.PeriodCount : 0;

    /// <summary>La tanda se puede iniciar ahora (lo decide el dominio; la ventana solo lo muestra).</summary>
    public bool CanStartShootout => Status == MatchStatus.InProgress && ShootoutBlocker is null;
}

public sealed record RulesSummary(
    string PresetName,
    bool IsDemoPreset,
    int PeriodCount,
    int PeriodDurationSeconds,
    int ShotClockSeconds,
    int ShotClockAlternateSeconds,
    int OvertimePeriodCount,
    int OvertimePeriodDurationSeconds,
    bool PenaltyShootoutEnabled);

/// <summary>
/// Tanda de penales, separada del resultado reglamentario (<see cref="TeamSnapshot.Score"/>).
/// Solo existe si el partido entró en la tanda y no se anuló.
/// </summary>
public sealed record ShootoutSnapshot(int HomeScore, int AwayScore, IReadOnlyList<ShootoutAttemptSnapshot> Attempts)
{
    public IReadOnlyList<ShootoutAttemptSnapshot> For(TeamSide side) =>
        Attempts.Where(a => a.Side == side && !a.IsVoided).ToList();
}

/// <summary>Lanzamiento de la tanda con su valor vigente.</summary>
public sealed record ShootoutAttemptSnapshot(
    Guid IncidentId, TeamSide Side, int? Cap, string? PlayerName, bool Converted, bool IsVoided);

public sealed record TeamSnapshot(
    Guid TeamId,
    string Code,
    string Name,
    string ColorHex,
    string CapColor,
    int Score,
    IReadOnlyList<RosterEntrySnapshot> Roster);

public sealed record RosterEntrySnapshot(Guid RosterEntryId, int Cap, string PlayerName);

public sealed record IncidentSnapshot(
    Guid IncidentId,
    int Sequence,
    IncidentType Type,
    int Period,
    TeamSide Side,
    Guid? RosterEntryId,
    int? Cap,
    string? PlayerName,
    ClockContext Clock,
    DateTimeOffset RegisteredAtUtc,
    string RegisteredBy,
    bool IsVoided,
    IReadOnlyList<RevisionSnapshot> Revisions,
    PenaltyResult? PenaltyResult = null,
    ExclusionKind? ExclusionKind = null);

public sealed record RevisionSnapshot(
    int Number,
    RevisionKind Kind,
    TeamSide Side,
    int? Cap,
    string? PlayerName,
    string? Reason,
    string OperatorName,
    DateTimeOffset AtUtc,
    PenaltyResult? PenaltyResult = null,
    ExclusionKind? ExclusionKind = null);

public sealed record PlayerFoulSnapshot(TeamSide Side, Guid RosterEntryId, int Cap, string PlayerName, int Fouls);

public sealed record ScorerSnapshot(TeamSide Side, int Cap, string PlayerName, int Goals);

public sealed record StatusChangeSnapshot(
    int Sequence,
    MatchStatusChangeKind Kind,
    int Period,
    string? Reason,
    string OperatorName,
    DateTimeOffset AtUtc);

/// <summary>Proyecta el agregado persistido a un snapshot. Es la única fuente del marcador.</summary>
public static class MatchSnapshotProjector
{
    public static MatchSnapshot Project(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);
        var score = match.Score;

        return new MatchSnapshot(
            match.Id,
            match.Version,
            match.TournamentName,
            match.Label,
            match.Status,
            match.Stage,
            match.CurrentPeriod,
            match.Ruleset is { } rules
                ? new RulesSummary(
                    rules.PresetName,
                    rules.IsDemoPreset,
                    rules.PeriodCount,
                    rules.PeriodDurationSeconds,
                    rules.ShotClockSeconds,
                    rules.ShotClockAlternateSeconds,
                    rules.OvertimePeriodCount,
                    rules.OvertimePeriodDurationSeconds,
                    rules.PenaltyShootoutEnabled)
                : null,
            ProjectTeam(match, match.Home, TeamSide.Home, score.Home),
            ProjectTeam(match, match.Away, TeamSide.Away, score.Away),
            match.Incidents.Select(i => ProjectIncident(match, i)).ToList(),
            MatchStatistics.Scorers(match).Select(s => new ScorerSnapshot(s.Side, s.Cap, s.PlayerName, s.Goals)).ToList(),
            match.StatusChanges
                .Select(c => new StatusChangeSnapshot(c.Sequence, c.Kind, c.Period, c.Reason, c.Operator.Name, c.AtUtc))
                .ToList(),
            ProjectShootout(match),
            match.Status == MatchStatus.InProgress ? match.ShootoutBlocker : null,
            MatchStatistics.PlayerFouls(match)
                .Select(f => new PlayerFoulSnapshot(f.Side, f.RosterEntryId, f.Cap, f.PlayerName, f.Fouls))
                .ToList());
    }

    private static ShootoutSnapshot? ProjectShootout(Match match)
    {
        if (!match.HasShootout)
        {
            return null;
        }

        var score = match.ShootoutScore;
        return new ShootoutSnapshot(
            score.Home,
            score.Away,
            match.ShootoutAttempts.Select(a =>
            {
                var player = a.Effective.RosterEntryId is { } id ? match.FindRosterEntry(id) : null;
                return new ShootoutAttemptSnapshot(
                    a.Id, a.Effective.Side, player?.Cap.Value, player?.PlayerName,
                    a.Effective.PenaltyResult == PenaltyResult.Converted, a.IsVoided);
            }).ToList());
    }

    private static TeamSnapshot ProjectTeam(Match match, MatchTeam team, TeamSide side, int score) =>
        new(
            team.TeamId,
            team.Code,
            team.Name,
            team.ColorHex,
            team.CapColor,
            score,
            match.Roster
                .Where(r => r.Side == side)
                .OrderBy(r => r.Cap.Value)
                .Select(r => new RosterEntrySnapshot(r.Id, r.Cap.Value, r.PlayerName))
                .ToList());

    private static IncidentSnapshot ProjectIncident(Match match, MatchIncident incident)
    {
        var effective = incident.Effective;
        var player = effective.RosterEntryId is { } id ? match.FindRosterEntry(id) : null;

        return new IncidentSnapshot(
            incident.Id,
            incident.Sequence,
            incident.Type,
            incident.Period.Value,
            effective.Side,
            effective.RosterEntryId,
            player?.Cap.Value,
            player?.PlayerName,
            incident.Clock,
            incident.RegisteredAtUtc,
            incident.Original.Operator.Name,
            incident.IsVoided,
            incident.Revisions.Select(r =>
            {
                var revisionPlayer = r.RosterEntryId is { } rid ? match.FindRosterEntry(rid) : null;
                return new RevisionSnapshot(
                    r.Number,
                    r.Kind,
                    r.Side,
                    revisionPlayer?.Cap.Value,
                    revisionPlayer?.PlayerName,
                    r.Reason,
                    r.Operator.Name,
                    r.AtUtc,
                    r.PenaltyResult,
                    r.ExclusionKind);
            }).ToList(),
            effective.PenaltyResult,
            effective.ExclusionKind);
    }
}
