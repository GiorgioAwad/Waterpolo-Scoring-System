using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Domain.Statistics;

namespace FDNDA.Waterpolo.Domain.Tests;

public class IncidentTests
{
    [Theory]
    [InlineData(IncidentType.Goal, null, null)]
    [InlineData(IncidentType.Exclusion, null, ExclusionKind.Exclusion)]
    [InlineData(IncidentType.Exclusion, null, ExclusionKind.Expulsion)]
    [InlineData(IncidentType.PersonalFoul, null, null)]
    [InlineData(IncidentType.Penalty, PenaltyResult.Converted, null)]
    [InlineData(IncidentType.Penalty, PenaltyResult.NotConverted, null)]
    [InlineData(IncidentType.Timeout, null, null)]
    public void Cada_incidencia_se_registra_corrige_y_anula_con_auditoria(
        IncidentType type, PenaltyResult? result, ExclusionKind? exclusion)
    {
        var f = new MatchFixture().Started();
        var clock = MatchFixture.LiveClock();
        f.Match.StartBreak(f.Operator, MatchFixture.T0);
        var original = f.Match.RegisterIncident(Guid.NewGuid(), type, TeamSide.Home,
            type == IncidentType.Timeout ? null : f.HomeRoster[0].Id, clock, f.Operator, MatchFixture.T0, result, exclusion);

        Assert.Equal(type, original.Type);
        Assert.Equal(1, original.Period.Value); // registro tardío durante descanso
        Assert.Same(clock, original.Clock);
        Assert.Equal(MatchFixture.T0, original.RegisteredAtUtc);
        Assert.Equal(f.Operator, original.Original.Operator);

        var correction = f.Match.CorrectIncident(original.Id, TeamSide.Away,
            type == IncidentType.Timeout ? null : f.AwayRoster[1].Id, "Equipo equivocado", f.OtherOperator,
            MatchFixture.T0.AddMinutes(1), result, exclusion);
        Assert.Equal(TeamSide.Home, original.Original.Side);
        Assert.Equal(TeamSide.Away, correction.Side);
        Assert.Equal("Equipo equivocado", correction.Reason);
        Assert.Equal(f.OtherOperator, correction.Operator);
        Assert.Equal(result, correction.PenaltyResult);
        Assert.Equal(exclusion, correction.ExclusionKind);

        var voided = f.Match.VoidIncident(original.Id, "Decisión de mesa", f.Operator, MatchFixture.T0.AddMinutes(2));
        Assert.True(original.IsVoided);
        Assert.Equal(3, original.Revisions.Count);
        Assert.Equal(result, voided.PenaltyResult);
        Assert.Equal(exclusion, voided.ExclusionKind);
        Assert.Equal(new MatchScore(0, 0), f.Match.Score);
        Assert.Empty(MatchStatistics.Scorers(f.Match));
        Assert.Throws<DomainException>(() => f.Match.CorrectIncident(original.Id, TeamSide.Home,
            type == IncidentType.Timeout ? null : f.HomeRoster[0].Id, "No reactivar", f.Operator, MatchFixture.T0, result, exclusion));
    }

    [Fact]
    public void Las_faltas_de_cada_jugador_suman_personales_exclusiones_y_expulsiones_vigentes()
    {
        var f = new MatchFixture().Started();
        var clock = MatchFixture.LiveClock();
        var home = f.HomeRoster[2];
        var away = f.AwayRoster[0];
        MatchIncident Register(IncidentType type, TeamSide side, Guid player, ExclusionKind? exclusion = null) =>
            f.Match.RegisterIncident(Guid.NewGuid(), type, side, player, clock, f.Operator, MatchFixture.T0, null, exclusion);

        Register(IncidentType.PersonalFoul, TeamSide.Home, home.Id);
        Register(IncidentType.Exclusion, TeamSide.Home, home.Id, ExclusionKind.Exclusion);
        var expulsion = Register(IncidentType.Exclusion, TeamSide.Away, away.Id, ExclusionKind.Expulsion);
        var wrong = Register(IncidentType.PersonalFoul, TeamSide.Away, away.Id);
        f.Match.RegisterIncident(Guid.NewGuid(), IncidentType.Penalty, TeamSide.Home, home.Id, clock, f.Operator,
            MatchFixture.T0, PenaltyResult.NotConverted); // el lanzador del penal no suma falta
        f.Match.RegisterIncident(Guid.NewGuid(), IncidentType.Goal, TeamSide.Home, home.Id, clock, f.Operator, MatchFixture.T0);

        var fouls = MatchStatistics.PlayerFouls(f.Match);
        Assert.Equal([(TeamSide.Home, home.Cap.Value, 2), (TeamSide.Away, away.Cap.Value, 2)],
            fouls.Select(l => (l.Side, l.Cap, l.Fouls)));

        // La corrección cambia de jugador; la anulación deja de contar; no hay sanción automática.
        f.Match.CorrectIncident(wrong.Id, TeamSide.Away, f.AwayRoster[1].Id, "Otro gorro", f.Operator, MatchFixture.T0);
        f.Match.VoidIncident(expulsion.Id, "No hubo", f.Operator, MatchFixture.T0);
        fouls = MatchStatistics.PlayerFouls(f.Match);
        Assert.Equal([(home.Id, 2), (f.AwayRoster[1].Id, 1)], fouls.Select(l => (l.RosterEntryId, l.Fouls)));
    }

    [Fact]
    public void El_penal_convertido_suma_un_solo_gol_y_sus_correcciones_actualizan_resultado_y_goleador()
    {
        var f = new MatchFixture().Started();
        var penalty = f.Match.RegisterIncident(Guid.NewGuid(), IncidentType.Penalty, TeamSide.Home,
            f.HomeRoster[0].Id, MatchFixture.LiveClock(), f.Operator, MatchFixture.T0, PenaltyResult.Converted);
        Assert.Single(f.Match.Incidents);
        Assert.Equal(new MatchScore(1, 0), f.Match.Score);
        Assert.Equal(1, Assert.Single(MatchStatistics.Scorers(f.Match)).Goals);

        f.Match.CorrectIncident(penalty.Id, TeamSide.Home, f.HomeRoster[0].Id,
            "No entró", f.Operator, MatchFixture.T0, PenaltyResult.NotConverted);
        Assert.Equal(new MatchScore(0, 0), f.Match.Score);
        Assert.Empty(MatchStatistics.Scorers(f.Match));

        f.Match.CorrectIncident(penalty.Id, TeamSide.Away, f.AwayRoster[1].Id,
            "Anotó la visita", f.OtherOperator, MatchFixture.T0, PenaltyResult.Converted);
        Assert.Equal(new MatchScore(0, 1), f.Match.Score);
        Assert.Equal(f.AwayRoster[1].Id, Assert.Single(MatchStatistics.Scorers(f.Match)).RosterEntryId);
        Assert.Equal(PenaltyResult.Converted, penalty.Original.PenaltyResult);
        Assert.Equal(PenaltyResult.NotConverted, penalty.Revisions[1].PenaltyResult);
    }

    [Theory]
    [InlineData(IncidentType.Goal, null, null)]
    [InlineData(IncidentType.Exclusion, null, ExclusionKind.Exclusion)]
    [InlineData(IncidentType.PersonalFoul, null, null)]
    [InlineData(IncidentType.Penalty, PenaltyResult.Converted, null)]
    public void Las_incidencias_de_jugador_exigen_nomina_del_equipo_incluso_al_corregir(
        IncidentType type, PenaltyResult? result, ExclusionKind? exclusion)
    {
        var f = new MatchFixture().Started();
        foreach (var invalid in new Guid?[] { null, Guid.NewGuid(), f.AwayRoster[0].Id })
        {
            Assert.Throws<DomainException>(() => f.Match.RegisterIncident(Guid.NewGuid(), type, TeamSide.Home,
                invalid, ClockContext.Unknown, f.Operator, MatchFixture.T0, result, exclusion));
        }

        var incident = f.Match.RegisterIncident(Guid.NewGuid(), type, TeamSide.Home, f.HomeRoster[0].Id,
            ClockContext.Unknown, f.Operator, MatchFixture.T0, result, exclusion);
        Assert.Throws<DomainException>(() => f.Match.CorrectIncident(incident.Id, TeamSide.Away, f.HomeRoster[0].Id,
            "Equipo", f.Operator, MatchFixture.T0, result, exclusion));
        Assert.Single(incident.Revisions);
    }

    [Theory]
    [InlineData(IncidentType.Penalty, null, null)]
    [InlineData(IncidentType.Penalty, (PenaltyResult)999, null)]
    [InlineData(IncidentType.Goal, PenaltyResult.Converted, null)]
    [InlineData(IncidentType.Exclusion, null, null)]
    [InlineData(IncidentType.Exclusion, null, (ExclusionKind)999)]
    [InlineData(IncidentType.PersonalFoul, null, ExclusionKind.Expulsion)]
    [InlineData((IncidentType)999, null, null)]
    public void Se_rechazan_detalles_ausentes_o_incompatibles_con_el_tipo(
        IncidentType type, PenaltyResult? result, ExclusionKind? exclusion)
    {
        var f = new MatchFixture().Started();
        Assert.Throws<DomainException>(() => f.Match.RegisterIncident(Guid.NewGuid(), type, TeamSide.Home,
            f.HomeRoster[0].Id, ClockContext.Unknown, f.Operator, MatchFixture.T0, result, exclusion));
        Assert.Empty(f.Match.Incidents);
    }

    [Fact]
    public void El_tiempo_muerto_no_admite_jugador_y_no_se_bloquea_por_un_limite_no_aprobado()
    {
        var f = new MatchFixture().Started();
        Assert.Throws<DomainException>(() => f.Match.RegisterIncident(Guid.NewGuid(), IncidentType.Timeout,
            TeamSide.Home, f.HomeRoster[0].Id, ClockContext.Unknown, f.Operator, MatchFixture.T0));
        for (var i = 0; i <= f.Rules.TimeoutsPerTeam; i++)
        {
            f.Match.RegisterIncident(Guid.NewGuid(), IncidentType.Timeout, TeamSide.Home, null,
                ClockContext.Unknown, f.Operator, MatchFixture.T0);
        }

        var timeout = f.Match.Incidents[0];
        Assert.Throws<DomainException>(() => f.Match.CorrectIncident(timeout.Id, TeamSide.Away,
            f.AwayRoster[0].Id, "No lleva jugador", f.Operator, MatchFixture.T0));
        Assert.Equal(f.Rules.TimeoutsPerTeam + 1, f.Match.Incidents.Count);
    }

    [Fact]
    public void Acumular_faltas_y_registrar_expulsion_no_aplica_sanciones_automaticas()
    {
        var f = new MatchFixture().Started();
        for (var i = 0; i < 10; i++)
        {
            f.Match.RegisterIncident(Guid.NewGuid(), IncidentType.PersonalFoul, TeamSide.Home,
                f.HomeRoster[0].Id, ClockContext.Unknown, f.Operator, MatchFixture.T0);
        }

        f.Match.RegisterIncident(Guid.NewGuid(), IncidentType.Exclusion, TeamSide.Home, f.HomeRoster[0].Id,
            ClockContext.Unknown, f.Operator, MatchFixture.T0, exclusionKind: ExclusionKind.Expulsion);
        f.Goal(TeamSide.Home, 0);
        Assert.Equal(12, f.Match.Incidents.Count);
        Assert.Equal(new MatchScore(1, 0), f.Match.Score);
    }

    [Theory]
    [InlineData(IncidentType.Exclusion, null, ExclusionKind.Exclusion)]
    [InlineData(IncidentType.PersonalFoul, null, null)]
    [InlineData(IncidentType.Penalty, PenaltyResult.NotConverted, null)]
    [InlineData(IncidentType.Timeout, null, null)]
    public void Registrar_y_corregir_exigen_partido_en_juego_y_motivo_para_las_revisiones(
        IncidentType type, PenaltyResult? result, ExclusionKind? exclusion)
    {
        var f = new MatchFixture();
        Guid? player = type == IncidentType.Timeout ? null : f.HomeRoster[0].Id;
        Assert.Throws<DomainException>(() => f.Match.RegisterIncident(Guid.NewGuid(), type, TeamSide.Home,
            player, ClockContext.Unknown, f.Operator, MatchFixture.T0, result, exclusion));
        f.Started();
        var incident = f.Match.RegisterIncident(Guid.NewGuid(), type, TeamSide.Home, player,
            ClockContext.Unknown, f.Operator, MatchFixture.T0, result, exclusion);
        Assert.Throws<DomainException>(() => f.Match.CorrectIncident(incident.Id, TeamSide.Away,
            type == IncidentType.Timeout ? null : f.AwayRoster[0].Id, " ", f.Operator, MatchFixture.T0, result, exclusion));
        Assert.Throws<DomainException>(() => f.Match.VoidIncident(incident.Id, " ", f.Operator, MatchFixture.T0));
        Assert.Throws<DomainException>(() => f.Match.CorrectIncident(incident.Id, TeamSide.Home,
            player, "Sin cambio", f.Operator, MatchFixture.T0, result, exclusion));
        f.Match.Finish(f.Operator, MatchFixture.T0);
        Assert.Throws<DomainException>(() => f.Match.RegisterIncident(Guid.NewGuid(), type, TeamSide.Home,
            player, ClockContext.Unknown, f.Operator, MatchFixture.T0, result, exclusion));
        Assert.Throws<DomainException>(() => f.Match.CorrectIncident(incident.Id, TeamSide.Away,
            type == IncidentType.Timeout ? null : f.AwayRoster[0].Id, "Cerrado", f.Operator, MatchFixture.T0, result, exclusion));
        Assert.Single(incident.Revisions);
    }
}
