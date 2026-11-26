using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Domain.Statistics;

namespace FDNDA.Waterpolo.Domain.Tests;

public class OvertimeRulesTests
{
    [Fact]
    public void El_preset_demo_no_tiene_prorroga_ni_tanda()
    {
        var demo = RulesetSnapshot.DemoPreset();

        Assert.Equal(0, demo.OvertimePeriodCount);
        Assert.False(demo.PenaltyShootoutEnabled);
        Assert.Equal(demo.PeriodCount, demo.MaxPeriods);
    }

    [Fact]
    public void Las_reglas_con_prorroga_se_marcan_sin_aprobacion_tecnica()
    {
        var rules = MatchFixture.RulesWith(overtimePeriods: 2);

        Assert.Equal(RulesetSnapshot.CustomPresetName, rules.PresetName);
        Assert.False(rules.IsDemoPreset);
        Assert.Equal(6, rules.MaxPeriods);
        Assert.True(rules.IsOvertime(5));
        Assert.False(rules.IsOvertime(4));
    }

    [Fact]
    public void Sin_prorroga_su_duracion_no_cambia_el_preset()
    {
        var demo = RulesetSnapshot.DemoPreset();
        var same = RulesetSnapshot.ForEvent(
            demo.PeriodCount, demo.PeriodDurationSeconds, demo.BreakDurationSeconds, demo.HalftimeDurationSeconds,
            demo.TimeoutsPerTeam, demo.ShotClockSeconds, demo.ShotClockAlternateSeconds, demo.PenaltyShootoutEnabled,
            overtimePeriodCount: 0, overtimePeriodDurationSeconds: 5 * 60);

        Assert.True(same.IsDemoPreset);
    }

    [Theory]
    [InlineData(4, -1)]
    [InlineData(18, 3)]
    public void Prorroga_fuera_de_rango_se_rechaza(int periods, int overtime)
    {
        var ex = Assert.Throws<DomainException>(() => MatchFixture.RulesWith(periods, overtime));
        Assert.Equal("reglas.valor_invalido", ex.Code);
    }

    [Theory]
    [InlineData(59)]
    [InlineData(3601)]
    public void Duracion_de_prorroga_fuera_de_rango_se_rechaza(int seconds)
    {
        var ex = Assert.Throws<DomainException>(() =>
            RulesetSnapshot.ForEvent(4, 480, 120, 180, 2, 30, 20, true, 2, seconds));
        Assert.Equal("reglas.valor_invalido", ex.Code);
    }
}

public class OvertimeTests
{
    [Fact]
    public void Con_empate_al_final_del_reglamentario_se_juega_la_prorroga()
    {
        var f = new MatchFixture().Started(MatchFixture.RulesWith(overtimePeriods: 2)).PlayUntilBreakAfter(4);

        f.Match.StartNextPeriod(f.Operator, MatchFixture.T0);

        Assert.Equal(5, f.Match.CurrentPeriod);
        Assert.True(f.Match.IsInOvertime);
        Assert.Equal(MatchStage.Period, f.Match.Stage);
    }

    [Fact]
    public void Sin_empate_no_hay_prorroga()
    {
        var f = new MatchFixture().Started(MatchFixture.RulesWith(overtimePeriods: 2));
        f.Goal(TeamSide.Home, 0);
        f.PlayUntilBreakAfter(4);

        var ex = Assert.Throws<DomainException>(() => f.Match.StartNextPeriod(f.Operator, MatchFixture.T0));

        Assert.Equal("partido.prorroga_requiere_empate", ex.Code);
        Assert.Equal(4, f.Match.CurrentPeriod);
    }

    [Fact]
    public void Los_periodos_de_prorroga_se_juegan_completos_aunque_se_desempate()
    {
        var f = new MatchFixture().Started(MatchFixture.RulesWith(overtimePeriods: 2)).PlayUntilBreakAfter(4);
        f.Match.StartNextPeriod(f.Operator, MatchFixture.T0);
        f.Goal(TeamSide.Away, 1);
        f.Match.StartBreak(f.Operator, MatchFixture.T0);

        f.Match.StartNextPeriod(f.Operator, MatchFixture.T0);

        Assert.Equal(6, f.Match.CurrentPeriod);
    }

    [Fact]
    public void No_se_pasa_de_la_ultima_prorroga()
    {
        var f = new MatchFixture().Started(MatchFixture.RulesWith(overtimePeriods: 1)).PlayUntilBreakAfter(5);

        var ex = Assert.Throws<DomainException>(() => f.Match.StartNextPeriod(f.Operator, MatchFixture.T0));

        Assert.Equal("partido.sin_mas_periodos", ex.Code);
    }

    [Fact]
    public void Los_goles_de_la_prorroga_suman_al_resultado_y_llevan_su_periodo()
    {
        var f = new MatchFixture().Started(MatchFixture.RulesWith(overtimePeriods: 2)).PlayUntilBreakAfter(4);
        f.Match.StartNextPeriod(f.Operator, MatchFixture.T0);

        var goal = f.Goal(TeamSide.Home, 2);

        Assert.Equal(new MatchScore(1, 0), f.Match.Score);
        Assert.Equal(5, goal.Period.Value);
        Assert.Single(MatchStatistics.Scorers(f.Match));
    }
}

public class ShootoutTests
{
    private static MatchFixture ReadyForShootout(int overtimePeriods = 0)
    {
        var f = new MatchFixture().Started(MatchFixture.RulesWith(overtimePeriods: overtimePeriods));
        f.Goal(TeamSide.Home, 0);
        f.Goal(TeamSide.Away, 0);
        return f.PlayUntilBreakAfter(4 + overtimePeriods);
    }

    [Fact]
    public void La_tanda_se_inicia_con_empate_tras_todos_los_periodos()
    {
        var f = ReadyForShootout();

        Assert.True(f.Match.CanStartShootout);
        f.Match.StartShootout(f.Operator, MatchFixture.T0);

        Assert.Equal(MatchStage.Shootout, f.Match.Stage);
        Assert.True(f.Match.HasShootout);
        Assert.Equal(MatchStatusChangeKind.ShootoutStarted, f.Match.StatusChanges[^1].Kind);
        Assert.Equal(new MatchScore(0, 0), f.Match.ShootoutScore);
    }

    [Fact]
    public void La_tanda_tambien_se_inicia_al_terminar_el_ultimo_periodo_sin_descanso()
    {
        var f = new MatchFixture().Started(MatchFixture.RulesWith()).PlayUntilBreakAfter(3);
        f.Match.StartNextPeriod(f.Operator, MatchFixture.T0);
        Assert.Equal((4, MatchStage.Period), (f.Match.CurrentPeriod, f.Match.Stage));

        f.Match.StartShootout(f.Operator, MatchFixture.T0);

        Assert.Equal(MatchStage.Shootout, f.Match.Stage);
        Assert.Equal(4, f.Match.CurrentPeriod);
    }

    [Fact]
    public void Sin_tanda_en_las_reglas_no_se_puede_iniciar()
    {
        var f = new MatchFixture().Started(MatchFixture.RulesWith(shootout: false)).PlayUntilBreakAfter(4);

        var ex = Assert.Throws<DomainException>(() => f.Match.StartShootout(f.Operator, MatchFixture.T0));

        Assert.Equal("partido.tanda_no_disponible", ex.Code);
        Assert.Equal("Las reglas de este partido no tienen tanda de penales.", ex.Message);
    }

    [Fact]
    public void Sin_empate_no_hay_tanda()
    {
        var f = ReadyForShootout();
        f.Goal(TeamSide.Home, 1);

        Assert.False(f.Match.CanStartShootout);
        var ex = Assert.Throws<DomainException>(() => f.Match.StartShootout(f.Operator, MatchFixture.T0));

        Assert.Equal("La tanda solo se juega con el resultado empatado.", ex.Message);
    }

    [Fact]
    public void Con_prorroga_configurada_la_tanda_espera_a_que_se_juegue()
    {
        var f = new MatchFixture().Started(MatchFixture.RulesWith(overtimePeriods: 2)).PlayUntilBreakAfter(4);

        var ex = Assert.Throws<DomainException>(() => f.Match.StartShootout(f.Operator, MatchFixture.T0));
        Assert.Contains("prórroga", ex.Message, StringComparison.Ordinal);

        f.Match.StartNextPeriod(f.Operator, MatchFixture.T0);
        f.Match.StartBreak(f.Operator, MatchFixture.T0);
        f.Match.StartNextPeriod(f.Operator, MatchFixture.T0);
        f.Match.StartShootout(f.Operator, MatchFixture.T0); // desde el último periodo de prórroga, sin descanso

        Assert.Equal(MatchStage.Shootout, f.Match.Stage);
    }

    [Fact]
    public void Los_lanzamientos_no_suman_al_resultado_reglamentario_ni_a_los_goleadores()
    {
        var f = ReadyForShootout();
        f.Match.StartShootout(f.Operator, MatchFixture.T0);

        f.ShootoutAttempt(TeamSide.Home, 0, converted: true);
        f.ShootoutAttempt(TeamSide.Away, 0, converted: false);
        f.ShootoutAttempt(TeamSide.Home, 1, converted: true);

        Assert.Equal(new MatchScore(1, 1), f.Match.Score);
        Assert.Equal(new MatchScore(2, 0), f.Match.ShootoutScore);
        Assert.Equal(2, MatchStatistics.Scorers(f.Match).Sum(s => s.Goals));
        Assert.Equal(1, MatchStatistics.Totals(f.Match, TeamSide.Home).Goals);
        Assert.Equal(0, MatchStatistics.Totals(f.Match, TeamSide.Home).PenaltiesConverted);
    }

    [Fact]
    public void El_lanzamiento_exige_jugador_de_la_nomina_y_resultado()
    {
        var f = ReadyForShootout();
        f.Match.StartShootout(f.Operator, MatchFixture.T0);
        var clock = ClockContext.Unavailable(ClockSource.Simulated);

        var foreign = Assert.Throws<DomainException>(() => f.Match.RegisterIncident(
            Guid.NewGuid(), IncidentType.ShootoutAttempt, TeamSide.Home, f.Player(TeamSide.Away, 0).Id, clock, f.Operator,
            MatchFixture.T0, PenaltyResult.Converted));
        var withoutResult = Assert.Throws<DomainException>(() => f.Match.RegisterIncident(
            Guid.NewGuid(), IncidentType.ShootoutAttempt, TeamSide.Home, f.Player(TeamSide.Home, 0).Id, clock, f.Operator,
            MatchFixture.T0));

        Assert.Equal("jugador.ajeno_al_equipo", foreign.Code);
        Assert.Equal("incidencia.resultado_penal_invalido", withoutResult.Code);
    }

    [Fact]
    public void Fuera_de_la_tanda_no_se_registran_lanzamientos_y_durante_ella_no_hay_goles()
    {
        var f = ReadyForShootout();

        var early = Assert.Throws<DomainException>(() => f.ShootoutAttempt(TeamSide.Home, 0, converted: true));
        f.Match.StartShootout(f.Operator, MatchFixture.T0);
        var goal = Assert.Throws<DomainException>(() => f.Goal(TeamSide.Home, 0));
        var penalty = Assert.Throws<DomainException>(() => f.Match.RegisterIncident(
            Guid.NewGuid(), IncidentType.Penalty, TeamSide.Home, f.Player(TeamSide.Home, 0).Id,
            ClockContext.Unavailable(ClockSource.Simulated), f.Operator, MatchFixture.T0, PenaltyResult.Converted));

        Assert.Equal("incidencia.fuera_de_tanda", early.Code);
        Assert.Equal("incidencia.gol_en_tanda", goal.Code);
        Assert.Equal("incidencia.gol_en_tanda", penalty.Code);
    }

    [Fact]
    public void Un_lanzamiento_se_corrige_y_se_anula_con_revisiones()
    {
        var f = ReadyForShootout();
        f.Match.StartShootout(f.Operator, MatchFixture.T0);
        var attempt = f.ShootoutAttempt(TeamSide.Home, 0, converted: true);

        f.Match.CorrectIncident(attempt.Id, TeamSide.Home, f.Player(TeamSide.Home, 2).Id, "Lanzó el gorro 3", f.Operator,
            MatchFixture.T0, PenaltyResult.NotConverted);
        Assert.Equal(new MatchScore(0, 0), f.Match.ShootoutScore);

        f.Match.VoidIncident(attempt.Id, "Lanzamiento repetido", f.Operator, MatchFixture.T0);
        Assert.True(attempt.IsVoided);
        Assert.Equal(3, attempt.Revisions.Count);
    }

    [Fact]
    public void No_se_finaliza_con_la_tanda_empatada()
    {
        var f = ReadyForShootout();
        f.Match.StartShootout(f.Operator, MatchFixture.T0);
        f.ShootoutAttempt(TeamSide.Home, 0, converted: true);
        f.ShootoutAttempt(TeamSide.Away, 0, converted: true);

        var ex = Assert.Throws<DomainException>(() => f.Match.Finish(f.Operator, MatchFixture.T0));
        Assert.Equal("partido.tanda_empatada", ex.Code);

        f.ShootoutAttempt(TeamSide.Home, 1, converted: false);
        f.ShootoutAttempt(TeamSide.Away, 1, converted: true);
        f.Match.Finish(f.Operator, MatchFixture.T0);

        Assert.Equal(MatchStatus.Finished, f.Match.Status);
        Assert.True(f.Match.HasShootout);
        Assert.Equal(new MatchScore(1, 2), f.Match.ShootoutScore);
    }

    [Fact]
    public void Al_reabrir_un_partido_cerrado_en_la_tanda_vuelve_a_la_tanda()
    {
        var f = ReadyForShootout();
        f.Match.StartShootout(f.Operator, MatchFixture.T0);
        f.ShootoutAttempt(TeamSide.Home, 0, converted: true);
        f.Match.Finish(f.Operator, MatchFixture.T0);

        f.Match.Reopen("Revisar lanzamientos", f.Operator, MatchFixture.T0);

        Assert.Equal(MatchStage.Shootout, f.Match.Stage);
    }

    [Fact]
    public void Con_tanda_no_se_acepta_un_cambio_que_desempate_el_reglamentario()
    {
        var f = ReadyForShootout();
        var homeGoal = f.Match.Incidents.First(i => i.Effective.Side == TeamSide.Home);
        f.Match.StartShootout(f.Operator, MatchFixture.T0);

        var voiding = Assert.Throws<DomainException>(() =>
            f.Match.VoidIncident(homeGoal.Id, "Gol mal registrado", f.Operator, MatchFixture.T0));
        var moving = Assert.Throws<DomainException>(() =>
            f.Match.CorrectGoalScorer(homeGoal.Id, TeamSide.Away, f.Player(TeamSide.Away, 1).Id, "Era de la visita", f.Operator, MatchFixture.T0));

        Assert.Equal("partido.tanda_requiere_empate", voiding.Code);
        Assert.Equal("partido.tanda_requiere_empate", moving.Code);
        Assert.False(homeGoal.IsVoided);

        // Cambiar el goleador dentro del mismo equipo no desempata: se acepta.
        f.Match.CorrectGoalScorer(homeGoal.Id, TeamSide.Home, f.Player(TeamSide.Home, 2).Id, "Era el gorro 3", f.Operator, MatchFixture.T0);
        Assert.Equal(new MatchScore(1, 1), f.Match.Score);
    }

    [Fact]
    public void La_tanda_se_anula_con_motivo_solo_sin_lanzamientos_vigentes()
    {
        var f = ReadyForShootout();
        var homeGoal = f.Match.Incidents.First(i => i.Effective.Side == TeamSide.Home);
        f.Match.StartShootout(f.Operator, MatchFixture.T0);
        var attempt = f.ShootoutAttempt(TeamSide.Home, 0, converted: true);

        var withAttempts = Assert.Throws<DomainException>(() => f.Match.CancelShootout("Iniciada por error", f.Operator, MatchFixture.T0));
        var withoutReason = Assert.Throws<DomainException>(() => f.Match.CancelShootout(" ", f.Operator, MatchFixture.T0));
        Assert.Equal("partido.tanda_con_lanzamientos", withAttempts.Code);
        Assert.Equal("motivo.requerido", withoutReason.Code);

        f.Match.VoidIncident(attempt.Id, "Tanda iniciada por error", f.Operator, MatchFixture.T0);
        f.Match.CancelShootout("Faltaba registrar un gol del cuarto periodo", f.Operator, MatchFixture.T0);

        Assert.Equal(MatchStage.Break, f.Match.Stage);
        Assert.False(f.Match.HasShootout);
        Assert.Equal("Faltaba registrar un gol del cuarto periodo", f.Match.StatusChanges[^1].Reason);

        // Sin tanda, el reglamentario ya se puede corregir.
        f.Match.VoidIncident(homeGoal.Id, "Gol mal registrado", f.Operator, MatchFixture.T0);
        Assert.Equal(new MatchScore(0, 1), f.Match.Score);
    }
}
