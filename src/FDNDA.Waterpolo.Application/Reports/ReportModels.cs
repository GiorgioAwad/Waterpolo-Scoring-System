using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Domain.Statistics;

namespace FDNDA.Waterpolo.Application.Reports;

/// <summary>Un equipo en el resumen de un partido.</summary>
public sealed record TeamReport(Guid TeamId, string Code, string Name, TeamTotals Totals);

/// <summary>Goleador de un partido o de un evento. En el evento, el jugador se identifica por su id, no por el nombre.</summary>
public sealed record ScorerReport(Guid PlayerId, string TeamCode, int Cap, string PlayerName, int Goals);

/// <summary>Incidencia en el resumen, con los valores de su última revisión.</summary>
public sealed record IncidentReport(
    int Sequence,
    int Period,
    int? GameClockRemainingMs,
    TeamSide Side,
    string TeamCode,
    int? Cap,
    string? PlayerName,
    IncidentType Type,
    PenaltyResult? PenaltyResult,
    ExclusionKind? ExclusionKind,
    bool IsVoided,
    bool IsCorrected,
    int? OvertimeNumber = null);

/// <summary>Lanzamiento de la tanda, en el orden en que se registró.</summary>
public sealed record ShootoutAttemptReport(
    int Number,
    TeamSide Side,
    string TeamCode,
    int? Cap,
    string? PlayerName,
    bool Converted,
    bool IsVoided,
    bool IsCorrected);

/// <summary>Tanda de penales: no suma al resultado reglamentario ni a los goleadores.</summary>
public sealed record ShootoutReport(int HomeGoals, int AwayGoals, IReadOnlyList<ShootoutAttemptReport> Attempts);

/// <summary>
/// Resumen de un partido derivado de sus incidencias vigentes: el resultado coincide con el
/// marcador porque sale del mismo agregado.
/// </summary>
public sealed record MatchReport(
    Guid MatchId,
    Guid TournamentId,
    string TournamentName,
    string Label,
    MatchStatus Status,
    DateTimeOffset? ScheduledAtUtc,
    string? RulesPresetName,
    bool RulesAreDemo,
    TeamReport Home,
    TeamReport Away,
    IReadOnlyList<ScorerReport> Scorers,
    IReadOnlyList<IncidentReport> Incidents,
    bool WentToOvertime = false,
    ShootoutReport? Shootout = null)
{
    /// <summary>Un partido aún programado o anulado no suma en los totales del evento.</summary>
    public bool HasBeenPlayed => Status is MatchStatus.InProgress or MatchStatus.Finished;
}

/// <summary>Totales de un equipo en el evento (partidos iniciados o finalizados).</summary>
public sealed record TeamEventTotals(
    Guid TeamId,
    string Code,
    string Name,
    int Played,
    int GoalsFor,
    int GoalsAgainst,
    int Exclusions,
    int Expulsions,
    int PersonalFouls,
    int PenaltiesConverted,
    int Timeouts);

/// <summary>Resumen de un evento: resultados, goleadores y totales por equipo.</summary>
public sealed record TournamentReport(
    Guid TournamentId,
    string Name,
    bool IsDemo,
    IReadOnlyList<MatchReport> Matches,
    IReadOnlyList<ScorerReport> Scorers,
    IReadOnlyList<TeamEventTotals> Teams);

/// <summary>Arma los resúmenes a partir de los agregados persistidos; no guarda nada.</summary>
public static class ReportBuilder
{
    public static MatchReport ForMatch(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);
        var code = new Dictionary<TeamSide, string> { [TeamSide.Home] = match.Home.Code, [TeamSide.Away] = match.Away.Code };

        var scorers = MatchStatistics.Scorers(match)
            .Select(s => new ScorerReport(match.FindRosterEntry(s.RosterEntryId)!.PlayerId, code[s.Side], s.Cap, s.PlayerName, s.Goals))
            .ToList();

        var regulationPeriods = match.Ruleset?.PeriodCount ?? int.MaxValue;
        var incidents = match.Incidents
            .Where(i => i.Type != IncidentType.ShootoutAttempt)
            .OrderBy(i => i.Sequence)
            .Select(i =>
            {
                var effective = i.Effective;
                var player = effective.RosterEntryId is { } id ? match.FindRosterEntry(id) : null;
                return new IncidentReport(
                    i.Sequence,
                    i.Period.Value,
                    i.Clock.Quality == ClockQuality.Live ? i.Clock.GameClockRemainingMs : null,
                    effective.Side,
                    code[effective.Side],
                    player?.Cap.Value,
                    player?.PlayerName,
                    i.Type,
                    effective.PenaltyResult,
                    effective.ExclusionKind,
                    i.IsVoided,
                    i.Revisions.Any(r => r.Kind == RevisionKind.Correction),
                    i.Period.Value > regulationPeriods ? i.Period.Value - regulationPeriods : null);
            })
            .ToList();

        ShootoutReport? shootout = null;
        if (match.HasShootout)
        {
            var score = match.ShootoutScore;
            shootout = new ShootoutReport(
                score.Home,
                score.Away,
                match.ShootoutAttempts.Select((a, index) =>
                {
                    var effective = a.Effective;
                    var player = effective.RosterEntryId is { } id ? match.FindRosterEntry(id) : null;
                    return new ShootoutAttemptReport(
                        index + 1,
                        effective.Side,
                        code[effective.Side],
                        player?.Cap.Value,
                        player?.PlayerName,
                        effective.PenaltyResult == PenaltyResult.Converted,
                        a.IsVoided,
                        a.Revisions.Any(r => r.Kind == RevisionKind.Correction));
                }).ToList());
        }

        return new MatchReport(
            match.Id,
            match.TournamentId,
            match.TournamentName,
            match.Label,
            match.Status,
            match.ScheduledAtUtc,
            match.Ruleset?.PresetName,
            match.Ruleset?.IsDemoPreset ?? false,
            new TeamReport(match.Home.TeamId, match.Home.Code, match.Home.Name, MatchStatistics.Totals(match, TeamSide.Home)),
            new TeamReport(match.Away.TeamId, match.Away.Code, match.Away.Name, MatchStatistics.Totals(match, TeamSide.Away)),
            scorers,
            incidents,
            match.Ruleset is { } rules && match.StatusChanges.Any(c => c.Kind == MatchStatusChangeKind.PeriodStarted && rules.IsOvertime(c.Period)),
            shootout);
    }

    public static TournamentReport ForTournament(Guid tournamentId, string name, bool isDemo, IEnumerable<MatchReport> matches)
    {
        var ordered = matches
            .Where(m => m.TournamentId == tournamentId)
            .OrderBy(m => m.ScheduledAtUtc is null)
            .ThenBy(m => m.ScheduledAtUtc)
            .ThenBy(m => m.Label, StringComparer.CurrentCulture)
            .ToList();
        var played = ordered.Where(m => m.HasBeenPlayed).ToList();

        // El jugador se agrupa por su identificador; el nombre y el gorro son los del último partido.
        var scorers = played
            .SelectMany(m => m.Scorers)
            .GroupBy(s => s.PlayerId)
            .Select(g => g.Last() with { Goals = g.Sum(s => s.Goals) })
            .OrderByDescending(s => s.Goals)
            .ThenBy(s => s.PlayerName, StringComparer.CurrentCulture)
            .ToList();

        var teams = played
            .SelectMany(m => new[] { (Team: m.Home, Rival: m.Away), (Team: m.Away, Rival: m.Home) })
            .GroupBy(x => x.Team.TeamId)
            .Select(g =>
            {
                var last = g.Last().Team;
                return new TeamEventTotals(
                    g.Key,
                    last.Code,
                    last.Name,
                    g.Count(),
                    g.Sum(x => x.Team.Totals.Goals),
                    g.Sum(x => x.Rival.Totals.Goals),
                    g.Sum(x => x.Team.Totals.Exclusions),
                    g.Sum(x => x.Team.Totals.Expulsions),
                    g.Sum(x => x.Team.Totals.PersonalFouls),
                    g.Sum(x => x.Team.Totals.PenaltiesConverted),
                    g.Sum(x => x.Team.Totals.Timeouts));
            })
            .OrderBy(t => t.Name, StringComparer.CurrentCulture)
            .ToList();

        return new TournamentReport(tournamentId, name, isDemo, ordered, scorers, teams);
    }
}
