using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;

namespace FDNDA.Waterpolo.Domain.Statistics;

/// <summary>Goles vigentes de un jugador de la nómina del partido.</summary>
public sealed record ScorerLine(TeamSide Side, Guid RosterEntryId, int Cap, string PlayerName, int Goals);

/// <summary>Faltas vigentes de un jugador: faltas personales, exclusiones y expulsiones.</summary>
public sealed record PlayerFoulLine(TeamSide Side, Guid RosterEntryId, int Cap, string PlayerName, int Fouls);

/// <summary>
/// Totales de un equipo en un partido, contados sobre la última revisión de cada incidencia no
/// anulada. Los goles incluyen los penales convertidos (cada uno aporta uno). Una exclusión sin
/// clase registrada (datos anteriores a R1) cuenta como exclusión.
/// </summary>
public sealed record TeamTotals(
    int Goals, int Exclusions, int Expulsions, int PersonalFouls, int PenaltiesConverted, int PenaltiesMissed, int Timeouts);

/// <summary>Estadísticas derivadas de la última revisión válida de cada incidencia.</summary>
public static class MatchStatistics
{
    public static TeamTotals Totals(Match match, TeamSide side)
    {
        ArgumentNullException.ThrowIfNull(match);

        var current = match.Incidents.Where(i => !i.IsVoided && i.Effective.Side == side).ToList();
        return new TeamTotals(
            Goals: current.Count(i => i.CountsAsGoal),
            Exclusions: current.Count(i => i.Type == IncidentType.Exclusion && i.Effective.ExclusionKind != ExclusionKind.Expulsion),
            Expulsions: current.Count(i => i.Type == IncidentType.Exclusion && i.Effective.ExclusionKind == ExclusionKind.Expulsion),
            PersonalFouls: current.Count(i => i.Type == IncidentType.PersonalFoul),
            PenaltiesConverted: current.Count(i => i.Type == IncidentType.Penalty && i.Effective.PenaltyResult == PenaltyResult.Converted),
            PenaltiesMissed: current.Count(i => i.Type == IncidentType.Penalty && i.Effective.PenaltyResult != PenaltyResult.Converted),
            Timeouts: current.Count(i => i.Type == IncidentType.Timeout));
    }

    /// <summary>
    /// Faltas de cada jugador sobre la última revisión de cada incidencia no anulada: faltas personales,
    /// exclusiones y expulsiones (decisión del usuario, 2026-11-25). Solo cuenta; no aplica ninguna
    /// sanción por acumulación. Solo jugadores con faltas, por equipo y gorro.
    /// </summary>
    public static IReadOnlyList<PlayerFoulLine> PlayerFouls(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);

        return match.Incidents
            .Where(i => !i.IsVoided
                && i.Type is IncidentType.PersonalFoul or IncidentType.Exclusion
                && i.Effective.RosterEntryId is not null)
            .GroupBy(i => i.Effective.RosterEntryId!.Value)
            .Select(group =>
            {
                var entry = match.FindRosterEntry(group.Key)!;
                return new PlayerFoulLine(entry.Side, entry.Id, entry.Cap.Value, entry.PlayerName, group.Count());
            })
            .OrderBy(line => line.Side)
            .ThenBy(line => line.Cap)
            .ToList();
    }

    public static IReadOnlyList<ScorerLine> Scorers(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);

        return match.Incidents
            .Where(i => i.CountsAsGoal && i.Effective.RosterEntryId is not null)
            .GroupBy(i => i.Effective.RosterEntryId!.Value)
            .Select(group =>
            {
                var entry = match.FindRosterEntry(group.Key)!;
                return new ScorerLine(entry.Side, entry.Id, entry.Cap.Value, entry.PlayerName, group.Count());
            })
            .OrderByDescending(line => line.Goals)
            .ThenBy(line => line.Side)
            .ThenBy(line => line.Cap)
            .ToList();
    }
}
