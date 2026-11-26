using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;

namespace FDNDA.Waterpolo.Domain.Tests;

/// <summary>R10: reprogramar, actualizar la nómina y anular un partido aún no iniciado.</summary>
public class RescheduleTests
{
    private static readonly DateTimeOffset NewDate = new(2027, 3, 15, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reprogramar_cambia_descripcion_fecha_colores_y_gorros_y_queda_en_el_historial()
    {
        var f = new MatchFixture();
        var home = f.Match.Home;

        f.Match.Reschedule("Semifinal", NewDate, "#123456", "Negro", "#ABCDEF", "Amarillo", f.Operator, MatchFixture.T0);

        Assert.Equal("Semifinal", f.Match.Label);
        Assert.Equal(NewDate, f.Match.ScheduledAtUtc);
        Assert.Equal("#123456", f.Match.Home.ColorHex);
        Assert.Equal("Negro", f.Match.Home.CapColor);
        Assert.Equal("#ABCDEF", f.Match.Away.ColorHex);
        Assert.Equal("Amarillo", f.Match.Away.CapColor);
        Assert.Equal((home.TeamId, home.Code, home.Name), (f.Match.Home.TeamId, f.Match.Home.Code, f.Match.Home.Name));
        Assert.Equal(MatchStatus.Scheduled, f.Match.Status);

        var change = Assert.Single(f.Match.StatusChanges);
        Assert.Equal(MatchStatusChangeKind.Rescheduled, change.Kind);
        Assert.Equal(MatchStatus.Scheduled, change.ToStatus);
        Assert.Equal(f.Operator, change.Operator);
        Assert.True(f.Match.PendingChanges.HasChanges);
    }

    [Fact]
    public void Reprogramar_puede_dejar_el_partido_sin_fecha()
    {
        var f = new MatchFixture();

        f.Match.Reschedule(f.Match.Label, null, f.Home.ColorHex, f.Home.CapColor, f.Away.ColorHex, f.Away.CapColor, f.Operator, MatchFixture.T0);

        Assert.Null(f.Match.ScheduledAtUtc);
    }

    [Fact]
    public void Reprogramar_sin_cambios_se_rechaza_y_no_deja_rastro()
    {
        var f = new MatchFixture();

        var ex = Assert.Throws<DomainException>(() => f.Match.Reschedule(
            f.Match.Label, f.Match.ScheduledAtUtc, f.Home.ColorHex, f.Home.CapColor, f.Away.ColorHex, f.Away.CapColor, f.Operator, MatchFixture.T0));

        Assert.Equal("partido.sin_cambio", ex.Code);
        Assert.Empty(f.Match.StatusChanges);
    }

    [Fact]
    public void Reprogramar_con_datos_invalidos_no_cambia_nada()
    {
        var f = new MatchFixture();

        Assert.Throws<DomainException>(() =>
            f.Match.Reschedule("Final", NewDate, "azul", "Negro", f.Away.ColorHex, f.Away.CapColor, f.Operator, MatchFixture.T0));
        Assert.Throws<DomainException>(() =>
            f.Match.Reschedule(" ", NewDate, f.Home.ColorHex, f.Home.CapColor, f.Away.ColorHex, f.Away.CapColor, f.Operator, MatchFixture.T0));

        Assert.Equal("Partido de prueba", f.Match.Label);
        Assert.Equal(MatchFixture.T0, f.Match.ScheduledAtUtc);
        Assert.Equal(f.Home, f.Match.Home);
        Assert.Empty(f.Match.StatusChanges);
    }

    [Fact]
    public void Un_partido_iniciado_no_se_reprograma_ni_cambia_su_nomina_ni_se_anula()
    {
        var f = new MatchFixture().Started();

        var reschedule = Assert.Throws<DomainException>(() => f.Match.Reschedule(
            "Otra", NewDate, f.Home.ColorHex, f.Home.CapColor, f.Away.ColorHex, f.Away.CapColor, f.Operator, MatchFixture.T0));
        var roster = Assert.Throws<DomainException>(() => f.Match.ReplaceRoster(f.HomeRoster, f.Operator, MatchFixture.T0));
        var cancel = Assert.Throws<DomainException>(() => f.Match.Cancel("Error de programación", f.Operator, MatchFixture.T0));

        Assert.All([reschedule, roster, cancel], ex => Assert.Equal("partido.ya_iniciado", ex.Code));
        Assert.Equal(MatchStatus.InProgress, f.Match.Status);
    }

    [Fact]
    public void Actualizar_la_nomina_reemplaza_la_copia_del_partido()
    {
        var f = new MatchFixture();
        var nueva = f.HomeRoster.Take(2)
            .Append(new MatchRosterEntry(Guid.NewGuid(), TeamSide.Home, Guid.NewGuid(), "Jugadora Nueva", new CapNumber(9)))
            .Concat(f.AwayRoster)
            .ToList();

        f.Match.ReplaceRoster(nueva, f.Operator, MatchFixture.T0);

        Assert.Equal(6, f.Match.Roster.Count);
        Assert.Contains(f.Match.Roster, r => r.PlayerName == "Jugadora Nueva" && r.Cap.Value == 9);
        Assert.DoesNotContain(f.Match.Roster, r => r.Id == f.HomeRoster[2].Id);
        Assert.True(f.Match.PendingChanges.RosterReplaced);
        Assert.Equal(MatchStatusChangeKind.RosterReplaced, Assert.Single(f.Match.StatusChanges).Kind);
    }

    [Fact]
    public void Actualizar_la_nomina_igual_a_la_del_partido_se_rechaza()
    {
        var f = new MatchFixture();
        var igual = f.HomeRoster.Concat(f.AwayRoster)
            .Select(r => new MatchRosterEntry(Guid.NewGuid(), r.Side, r.PlayerId, r.PlayerName, r.Cap))
            .ToList();

        var ex = Assert.Throws<DomainException>(() => f.Match.ReplaceRoster(igual, f.Operator, MatchFixture.T0));

        Assert.Equal("partido.sin_cambio", ex.Code);
        Assert.False(f.Match.PendingChanges.RosterReplaced);
    }

    [Fact]
    public void Actualizar_la_nomina_con_gorro_repetido_se_rechaza_y_conserva_la_anterior()
    {
        var f = new MatchFixture();
        var repetida = f.HomeRoster
            .Append(new MatchRosterEntry(Guid.NewGuid(), TeamSide.Home, Guid.NewGuid(), "Otra", new CapNumber(1)))
            .ToList();

        var ex = Assert.Throws<DomainException>(() => f.Match.ReplaceRoster(repetida, f.Operator, MatchFixture.T0));

        Assert.Equal("nomina.gorro_duplicado", ex.Code);
        Assert.Equal(6, f.Match.Roster.Count);
        Assert.Empty(f.Match.StatusChanges);
    }

    [Fact]
    public void Anular_exige_motivo_y_deja_el_partido_anulado_sin_borrarlo()
    {
        var f = new MatchFixture();

        var sinMotivo = Assert.Throws<DomainException>(() => f.Match.Cancel("  ", f.Operator, MatchFixture.T0));
        Assert.Equal("motivo.requerido", sinMotivo.Code);

        f.Match.Cancel("Programado por error", f.Operator, MatchFixture.T0);

        Assert.Equal(MatchStatus.Cancelled, f.Match.Status);
        var change = Assert.Single(f.Match.StatusChanges);
        Assert.Equal(MatchStatusChangeKind.Cancelled, change.Kind);
        Assert.Equal("Programado por error", change.Reason);
        Assert.Equal(6, f.Match.Roster.Count);
    }

    [Fact]
    public void Un_partido_anulado_no_se_inicia_ni_se_reprograma_ni_se_vuelve_a_anular()
    {
        var f = new MatchFixture();
        f.Match.Cancel("Programado por error", f.Operator, MatchFixture.T0);

        var start = Assert.Throws<DomainException>(() => f.Match.Start(f.Rules, f.Operator, MatchFixture.T0));
        var reschedule = Assert.Throws<DomainException>(() => f.Match.Reschedule(
            "Otra", NewDate, f.Home.ColorHex, f.Home.CapColor, f.Away.ColorHex, f.Away.CapColor, f.Operator, MatchFixture.T0));
        var cancel = Assert.Throws<DomainException>(() => f.Match.Cancel("Otra vez", f.Operator, MatchFixture.T0));
        var goal = Assert.Throws<DomainException>(() => f.Goal(TeamSide.Home, 0));

        Assert.All([start, reschedule, cancel, goal], ex => Assert.Equal("partido.anulado", ex.Code));
        Assert.Single(f.Match.StatusChanges);
    }
}
