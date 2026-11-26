using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;
using FDNDA.Waterpolo.Domain.Matches;

namespace FDNDA.Waterpolo.Domain.Tests;

/// <summary>Partido de prueba con tres jugadores ficticios por lado.</summary>
internal sealed class MatchFixture
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 11, 15, 0, 0, TimeSpan.Zero);

    public MatchFixture()
    {
        Home = new MatchTeam(Guid.NewGuid(), "AAA", "Equipo A", "#0B63CE", "Blanco");
        Away = new MatchTeam(Guid.NewGuid(), "BBB", "Equipo B", "#C62828", "Azul");
        HomeRoster = BuildRoster(TeamSide.Home);
        AwayRoster = BuildRoster(TeamSide.Away);
        Match = Match.Schedule(
            Guid.NewGuid(), Guid.NewGuid(), "Evento de prueba", "Partido de prueba", T0, Home, Away, HomeRoster.Concat(AwayRoster));
    }

    public OperatorRef Operator { get; } = new(Guid.NewGuid(), "Operadora Uno");

    public OperatorRef OtherOperator { get; } = new(Guid.NewGuid(), "Operador Dos");

    public RulesetSnapshot Rules { get; } = RulesetSnapshot.DemoPreset();

    public MatchTeam Home { get; }

    public MatchTeam Away { get; }

    public IReadOnlyList<MatchRosterEntry> HomeRoster { get; }

    public IReadOnlyList<MatchRosterEntry> AwayRoster { get; }

    public Match Match { get; }

    public static ClockContext LiveClock(int gameMs = 400_000, int shotMs = 20_000) =>
        new(ClockSource.Simulated, ClockQuality.Live, gameMs, shotMs);

    public MatchFixture Started() => Started(Rules);

    public MatchFixture Started(RulesetSnapshot rules)
    {
        Match.Start(rules, Operator, T0);
        return this;
    }

    /// <summary>Reglas del evento con prórroga y tanda configurables (valores de prueba, no reglamentarios).</summary>
    public static RulesetSnapshot RulesWith(int periods = 4, int overtimePeriods = 0, bool shootout = true) =>
        RulesetSnapshot.ForEvent(periods, 8 * 60, 2 * 60, 3 * 60, 2, 30, 20, shootout, overtimePeriods, 3 * 60);

    public MatchRosterEntry Player(TeamSide side, int rosterIndex) =>
        (side == TeamSide.Home ? HomeRoster : AwayRoster)[rosterIndex];

    /// <summary>Juega hasta el descanso tras el último periodo indicado.</summary>
    public MatchFixture PlayUntilBreakAfter(int period)
    {
        Match.StartBreak(Operator, T0);
        while (Match.CurrentPeriod < period)
        {
            Match.StartNextPeriod(Operator, T0);
            Match.StartBreak(Operator, T0);
        }

        return this;
    }

    public MatchIncident ShootoutAttempt(TeamSide side, int rosterIndex, bool converted) =>
        Match.RegisterIncident(
            Guid.NewGuid(), IncidentType.ShootoutAttempt, side, Player(side, rosterIndex).Id, ClockContext.Unavailable(ClockSource.Simulated),
            Operator, T0.AddMinutes(40), converted ? PenaltyResult.Converted : PenaltyResult.NotConverted);

    public MatchIncident Goal(TeamSide side, int rosterIndex, Guid? incidentId = null)
    {
        var entry = (side == TeamSide.Home ? HomeRoster : AwayRoster)[rosterIndex];
        return Match.RegisterGoal(incidentId ?? Guid.NewGuid(), side, entry.Id, LiveClock(), Operator, T0.AddMinutes(1));
    }

    private static List<MatchRosterEntry> BuildRoster(TeamSide side) =>
        Enumerable.Range(1, 3)
            .Select(cap => new MatchRosterEntry(Guid.NewGuid(), side, Guid.NewGuid(), $"Jugador {side} {cap}", new CapNumber(cap)))
            .ToList();
}
