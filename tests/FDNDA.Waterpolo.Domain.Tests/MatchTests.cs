using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Domain.Statistics;

namespace FDNDA.Waterpolo.Domain.Tests;

public class MatchSchedulingTests
{
    [Fact]
    public void Programar_rechaza_el_mismo_equipo_como_local_y_visita()
    {
        var team = new MatchTeam(Guid.NewGuid(), "AAA", "Equipo A", "#0B63CE", "Blanco");

        var ex = Assert.Throws<DomainException>(() =>
            Match.Schedule(Guid.NewGuid(), Guid.NewGuid(), "Evento", "Partido", null, team, team, []));

        Assert.Equal("partido.mismo_equipo", ex.Code);
    }

    [Fact]
    public void Programar_rechaza_gorro_duplicado_en_el_mismo_equipo()
    {
        var home = new MatchTeam(Guid.NewGuid(), "AAA", "Equipo A", "#0B63CE", "Blanco");
        var away = new MatchTeam(Guid.NewGuid(), "BBB", "Equipo B", "#C62828", "Azul");
        var roster = new[]
        {
            new MatchRosterEntry(Guid.NewGuid(), TeamSide.Home, Guid.NewGuid(), "Uno", new CapNumber(7)),
            new MatchRosterEntry(Guid.NewGuid(), TeamSide.Home, Guid.NewGuid(), "Dos", new CapNumber(7)),
        };

        var ex = Assert.Throws<DomainException>(() =>
            Match.Schedule(Guid.NewGuid(), Guid.NewGuid(), "Evento", "Partido", null, home, away, roster));

        Assert.Equal("nomina.gorro_duplicado", ex.Code);
    }

    [Fact]
    public void Programar_acepta_el_mismo_gorro_en_equipos_distintos()
    {
        var home = new MatchTeam(Guid.NewGuid(), "AAA", "Equipo A", "#0B63CE", "Blanco");
        var away = new MatchTeam(Guid.NewGuid(), "BBB", "Equipo B", "#C62828", "Azul");
        var roster = new[]
        {
            new MatchRosterEntry(Guid.NewGuid(), TeamSide.Home, Guid.NewGuid(), "Uno", new CapNumber(7)),
            new MatchRosterEntry(Guid.NewGuid(), TeamSide.Away, Guid.NewGuid(), "Dos", new CapNumber(7)),
        };

        var match = Match.Schedule(Guid.NewGuid(), Guid.NewGuid(), "Evento", "Partido", null, home, away, roster);

        Assert.Equal(2, match.Roster.Count);
        Assert.Equal(MatchStatus.Scheduled, match.Status);
        Assert.Equal(new MatchScore(0, 0), match.Score);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(-3)]
    public void Gorro_fuera_de_rango_se_rechaza(int value)
    {
        var ex = Assert.Throws<DomainException>(() => new CapNumber(value));
        Assert.Equal("gorro.fuera_de_rango", ex.Code);
    }

    [Fact]
    public void Contexto_de_reloj_no_vigente_no_puede_llevar_tiempos()
    {
        var ex = Assert.Throws<DomainException>(() =>
            new ClockContext(ClockSource.Simulated, ClockQuality.Unavailable, 120_000, null));

        Assert.Equal("reloj.contexto_invalido", ex.Code);
        Assert.Null(ClockContext.Unavailable(ClockSource.ColoradoSystem6).GameClockRemainingMs);
    }
}

public class MatchLifecycleTests
{
    [Fact]
    public void Iniciar_copia_las_reglas_y_pasa_a_periodo_1()
    {
        var f = new MatchFixture();

        f.Match.Start(f.Rules, f.Operator, MatchFixture.T0);

        Assert.Equal(MatchStatus.InProgress, f.Match.Status);
        Assert.Equal(MatchStage.Period, f.Match.Stage);
        Assert.Equal(1, f.Match.CurrentPeriod);
        Assert.Equal(f.Rules, f.Match.Ruleset);

        var change = Assert.Single(f.Match.StatusChanges);
        Assert.Equal(MatchStatusChangeKind.Started, change.Kind);
        Assert.Equal(f.Operator, change.Operator);
        Assert.Equal(MatchFixture.T0, change.AtUtc);
    }

    [Fact]
    public void No_se_puede_iniciar_dos_veces()
    {
        var f = new MatchFixture().Started();

        var ex = Assert.Throws<DomainException>(() => f.Match.Start(f.Rules, f.Operator, MatchFixture.T0));

        Assert.Equal("partido.ya_iniciado", ex.Code);
    }

    [Fact]
    public void Cambios_de_periodo_distinguen_descanso_y_respetan_la_cantidad_de_periodos()
    {
        var f = new MatchFixture().Started();

        for (var period = 1; period < f.Rules.PeriodCount; period++)
        {
            f.Match.StartBreak(f.Operator, MatchFixture.T0);
            Assert.Equal(MatchStage.Break, f.Match.Stage);
            Assert.Equal(period, f.Match.CurrentPeriod);

            f.Match.StartNextPeriod(f.Operator, MatchFixture.T0);
            Assert.Equal(MatchStage.Period, f.Match.Stage);
            Assert.Equal(period + 1, f.Match.CurrentPeriod);
        }

        f.Match.StartBreak(f.Operator, MatchFixture.T0);
        var ex = Assert.Throws<DomainException>(() => f.Match.StartNextPeriod(f.Operator, MatchFixture.T0));
        Assert.Equal("partido.sin_mas_periodos", ex.Code);
    }

    [Fact]
    public void No_se_inicia_un_periodo_sin_pasar_por_el_descanso()
    {
        var f = new MatchFixture().Started();

        var ex = Assert.Throws<DomainException>(() => f.Match.StartNextPeriod(f.Operator, MatchFixture.T0));

        Assert.Equal("partido.etapa_invalida", ex.Code);
    }

    [Fact]
    public void Finalizado_no_admite_incidencias_hasta_reabrir_con_motivo()
    {
        var f = new MatchFixture().Started();
        f.Goal(TeamSide.Home, 0);
        f.Match.Finish(f.Operator, MatchFixture.T0.AddMinutes(40));

        Assert.Equal(MatchStatus.Finished, f.Match.Status);
        Assert.Equal(MatchStage.Ended, f.Match.Stage);
        Assert.Equal("partido.finalizado", Assert.Throws<DomainException>(() => f.Goal(TeamSide.Home, 1)).Code);

        Assert.Equal(
            "motivo.requerido",
            Assert.Throws<DomainException>(() => f.Match.Reopen("  ", f.Operator, MatchFixture.T0)).Code);

        f.Match.Reopen("Gol no registrado en el último periodo", f.OtherOperator, MatchFixture.T0.AddMinutes(45));

        Assert.Equal(MatchStatus.InProgress, f.Match.Status);
        Assert.Equal(MatchStage.Period, f.Match.Stage); // vuelve a la etapa previa al cierre
        var reopened = f.Match.StatusChanges[^1];
        Assert.Equal(MatchStatusChangeKind.Reopened, reopened.Kind);
        Assert.Equal("Gol no registrado en el último periodo", reopened.Reason);
        Assert.Equal(f.OtherOperator, reopened.Operator);

        f.Goal(TeamSide.Home, 1);
        Assert.Equal(new MatchScore(2, 0), f.Match.Score);
    }

    [Fact]
    public void Reabrir_solo_aplica_a_un_partido_finalizado()
    {
        var f = new MatchFixture().Started();

        var ex = Assert.Throws<DomainException>(() => f.Match.Reopen("motivo", f.Operator, MatchFixture.T0));

        Assert.Equal("partido.no_finalizado", ex.Code);
    }
}

public class MatchIncidentTests
{
    [Fact]
    public void No_se_registra_gol_en_un_partido_no_iniciado()
    {
        var f = new MatchFixture();

        var ex = Assert.Throws<DomainException>(() => f.Goal(TeamSide.Home, 0));

        Assert.Equal("partido.no_iniciado", ex.Code);
    }

    [Fact]
    public void Gol_registra_equipo_jugador_periodo_reloj_hora_y_operador()
    {
        var f = new MatchFixture().Started();

        var incident = f.Goal(TeamSide.Away, 2);

        Assert.Equal(1, incident.Sequence);
        Assert.Equal(IncidentType.Goal, incident.Type);
        Assert.Equal(1, incident.Period.Value);
        Assert.Equal(ClockQuality.Live, incident.Clock.Quality);
        Assert.Equal(400_000, incident.Clock.GameClockRemainingMs);
        Assert.Equal(TeamSide.Away, incident.Effective.Side);
        Assert.Equal(f.AwayRoster[2].Id, incident.Effective.RosterEntryId);
        Assert.Equal(f.Operator, incident.Original.Operator);
        Assert.Equal(new MatchScore(0, 1), f.Match.Score);
    }

    [Fact]
    public void Se_rechaza_un_jugador_ajeno_a_la_nomina_del_partido()
    {
        var f = new MatchFixture().Started();

        var ex = Assert.Throws<DomainException>(() =>
            f.Match.RegisterGoal(Guid.NewGuid(), TeamSide.Home, Guid.NewGuid(), MatchFixture.LiveClock(), f.Operator, MatchFixture.T0));

        Assert.Equal("jugador.ajeno_a_nomina", ex.Code);
        Assert.Empty(f.Match.Incidents);
        Assert.Equal(new MatchScore(0, 0), f.Match.Score);
    }

    [Fact]
    public void Se_rechaza_un_jugador_del_equipo_contrario()
    {
        var f = new MatchFixture().Started();

        var ex = Assert.Throws<DomainException>(() =>
            f.Match.RegisterGoal(Guid.NewGuid(), TeamSide.Home, f.AwayRoster[0].Id, MatchFixture.LiveClock(), f.Operator, MatchFixture.T0));

        Assert.Equal("jugador.ajeno_al_equipo", ex.Code);
        Assert.Empty(f.Match.Incidents);
    }

    [Fact]
    public void La_misma_incidencia_no_suma_dos_veces()
    {
        var f = new MatchFixture().Started();
        var incidentId = Guid.NewGuid();
        f.Goal(TeamSide.Home, 0, incidentId);

        var ex = Assert.Throws<DomainException>(() => f.Goal(TeamSide.Home, 0, incidentId));

        Assert.Equal("incidencia.duplicada", ex.Code);
        Assert.Equal(new MatchScore(1, 0), f.Match.Score);
        Assert.Single(f.Match.Incidents);
    }

    [Fact]
    public void Corregir_goleador_actualiza_la_estadistica_sin_duplicar_goles_y_conserva_auditoria()
    {
        var f = new MatchFixture().Started();
        var incident = f.Goal(TeamSide.Home, 0);
        f.Goal(TeamSide.Home, 1);
        var correctedAt = MatchFixture.T0.AddMinutes(3);

        f.Match.CorrectGoalScorer(incident.Id, TeamSide.Home, f.HomeRoster[2].Id, "Gorro mal leído", f.OtherOperator, correctedAt);

        Assert.Equal(new MatchScore(2, 0), f.Match.Score);

        var scorers = MatchStatistics.Scorers(f.Match);
        Assert.DoesNotContain(scorers, s => s.RosterEntryId == f.HomeRoster[0].Id);
        Assert.Equal(1, scorers.Single(s => s.RosterEntryId == f.HomeRoster[2].Id).Goals);
        Assert.Equal(2, scorers.Sum(s => s.Goals));

        // Auditoría: valor anterior (revisión 1) y posterior (revisión 2), motivo, responsable y fecha.
        Assert.Equal(2, incident.Revisions.Count);
        Assert.Equal(RevisionKind.Original, incident.Revisions[0].Kind);
        Assert.Equal(f.HomeRoster[0].Id, incident.Revisions[0].RosterEntryId);
        Assert.Equal(f.Operator, incident.Revisions[0].Operator);
        Assert.Equal(RevisionKind.Correction, incident.Revisions[1].Kind);
        Assert.Equal(f.HomeRoster[2].Id, incident.Revisions[1].RosterEntryId);
        Assert.Equal("Gorro mal leído", incident.Revisions[1].Reason);
        Assert.Equal(f.OtherOperator, incident.Revisions[1].Operator);
        Assert.Equal(correctedAt, incident.Revisions[1].AtUtc);
    }

    [Fact]
    public void Corregir_al_equipo_contrario_mueve_el_gol_de_lado()
    {
        var f = new MatchFixture().Started();
        var incident = f.Goal(TeamSide.Home, 0);

        f.Match.CorrectGoalScorer(incident.Id, TeamSide.Away, f.AwayRoster[1].Id, "Equipo equivocado", f.Operator, MatchFixture.T0);

        Assert.Equal(new MatchScore(0, 1), f.Match.Score);
    }

    [Fact]
    public void Anular_un_gol_resta_exactamente_uno_y_conserva_el_historial()
    {
        var f = new MatchFixture().Started();
        var first = f.Goal(TeamSide.Home, 0);
        f.Goal(TeamSide.Home, 1);
        f.Goal(TeamSide.Away, 0);

        f.Match.VoidIncident(first.Id, "Gol invalidado por el árbitro", f.Operator, MatchFixture.T0.AddMinutes(2));

        Assert.Equal(new MatchScore(1, 1), f.Match.Score);
        Assert.Equal(3, f.Match.Incidents.Count); // sin borrado físico
        Assert.True(first.IsVoided);
        Assert.Equal(2, first.Revisions.Count);
        Assert.Equal(RevisionKind.Original, first.Original.Kind);
        Assert.Equal(f.HomeRoster[0].Id, first.Original.RosterEntryId);
        Assert.Equal(RevisionKind.Void, first.Effective.Kind);
        Assert.Equal("Gol invalidado por el árbitro", first.Effective.Reason);
        Assert.DoesNotContain(MatchStatistics.Scorers(f.Match), s => s.RosterEntryId == f.HomeRoster[0].Id);
    }

    [Fact]
    public void Una_incidencia_anulada_no_se_vuelve_a_anular_ni_corregir()
    {
        var f = new MatchFixture().Started();
        var incident = f.Goal(TeamSide.Home, 0);
        f.Match.VoidIncident(incident.Id, "Error de registro", f.Operator, MatchFixture.T0);

        Assert.Equal(
            "incidencia.anulada",
            Assert.Throws<DomainException>(() => f.Match.VoidIncident(incident.Id, "Otra vez", f.Operator, MatchFixture.T0)).Code);
        Assert.Equal(
            "incidencia.anulada",
            Assert.Throws<DomainException>(() =>
                f.Match.CorrectGoalScorer(incident.Id, TeamSide.Home, f.HomeRoster[1].Id, "Cambio", f.Operator, MatchFixture.T0)).Code);
        Assert.Equal(2, incident.Revisions.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Corregir_y_anular_exigen_motivo(string reason)
    {
        var f = new MatchFixture().Started();
        var incident = f.Goal(TeamSide.Home, 0);

        Assert.Equal(
            "motivo.requerido",
            Assert.Throws<DomainException>(() =>
                f.Match.CorrectGoalScorer(incident.Id, TeamSide.Home, f.HomeRoster[1].Id, reason, f.Operator, MatchFixture.T0)).Code);
        Assert.Equal(
            "motivo.requerido",
            Assert.Throws<DomainException>(() => f.Match.VoidIncident(incident.Id, reason, f.Operator, MatchFixture.T0)).Code);

        Assert.Single(incident.Revisions);
        Assert.Equal(new MatchScore(1, 0), f.Match.Score);
    }

    [Fact]
    public void Corregir_rechaza_un_jugador_ajeno_y_una_correccion_sin_cambio()
    {
        var f = new MatchFixture().Started();
        var incident = f.Goal(TeamSide.Home, 0);

        Assert.Equal(
            "jugador.ajeno_a_nomina",
            Assert.Throws<DomainException>(() =>
                f.Match.CorrectGoalScorer(incident.Id, TeamSide.Home, Guid.NewGuid(), "Cambio", f.Operator, MatchFixture.T0)).Code);
        Assert.Equal(
            "incidencia.sin_cambio",
            Assert.Throws<DomainException>(() =>
                f.Match.CorrectGoalScorer(incident.Id, TeamSide.Home, f.HomeRoster[0].Id, "Cambio", f.Operator, MatchFixture.T0)).Code);
    }

    [Fact]
    public void Las_incidencias_llevan_secuencia_por_partido_y_el_periodo_en_que_ocurren()
    {
        var f = new MatchFixture().Started();
        var first = f.Goal(TeamSide.Home, 0);
        f.Match.StartBreak(f.Operator, MatchFixture.T0);
        var duringBreak = f.Goal(TeamSide.Away, 0); // registro tardío: cuenta para el periodo recién jugado
        f.Match.StartNextPeriod(f.Operator, MatchFixture.T0);
        var third = f.Goal(TeamSide.Home, 1);

        Assert.Equal(new[] { 1, 2, 3 }, new[] { first.Sequence, duringBreak.Sequence, third.Sequence });
        Assert.Equal(new[] { 1, 1, 2 }, new[] { first.Period.Value, duringBreak.Period.Value, third.Period.Value });
    }

    [Fact]
    public void Los_cambios_pendientes_se_limpian_al_confirmar_la_escritura()
    {
        var f = new MatchFixture().Started();
        f.Goal(TeamSide.Home, 0);

        Assert.True(f.Match.PendingChanges.IsNew);
        Assert.Single(f.Match.PendingChanges.NewIncidents);
        Assert.Single(f.Match.PendingChanges.NewStatusChanges);

        f.Match.MarkPersisted(1);

        Assert.False(f.Match.PendingChanges.HasChanges);
        Assert.Equal(1, f.Match.Version);
    }
}

public class RulesetTests
{
    [Fact]
    public void El_preset_demo_esta_identificado_como_demo()
    {
        var rules = RulesetSnapshot.DemoPreset();

        Assert.True(rules.IsDemoPreset);
        Assert.Contains("DEMO", rules.PresetName, StringComparison.Ordinal);
    }

    [Fact]
    public void Reglas_con_valores_invalidos_se_rechazan()
    {
        var ex = Assert.Throws<DomainException>(() =>
            new RulesetSnapshot("X", false, 0, 480, 120, 180, 2, 30, 20, false));

        Assert.Equal("reglas.valor_invalido", ex.Code);
    }
}
