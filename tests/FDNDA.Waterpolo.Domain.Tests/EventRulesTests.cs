using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;

namespace FDNDA.Waterpolo.Domain.Tests;

/// <summary>R4 — reglas por evento (RF03): preset demo identificado y reglas personalizadas sin aprobación técnica.</summary>
public class EventRulesTests
{
    [Fact]
    public void Los_valores_del_demo_se_guardan_como_el_preset_demo_identificado()
    {
        var demo = RulesetSnapshot.DemoPreset();

        var rules = RulesetSnapshot.ForEvent(
            demo.PeriodCount, demo.PeriodDurationSeconds, demo.BreakDurationSeconds, demo.HalftimeDurationSeconds,
            demo.TimeoutsPerTeam, demo.ShotClockSeconds, demo.ShotClockAlternateSeconds, demo.PenaltyShootoutEnabled);

        Assert.Equal(demo, rules);
        Assert.True(rules.IsDemoPreset);
        Assert.Contains("no reglamentarios", rules.PresetName, StringComparison.Ordinal);
    }

    [Fact]
    public void Valores_distintos_del_demo_quedan_como_reglas_personalizadas_sin_aprobacion_tecnica()
    {
        var rules = RulesetSnapshot.ForEvent(4, 7 * 60, 2 * 60, 3 * 60, 2, 30, 20, penaltyShootoutEnabled: true);

        Assert.False(rules.IsDemoPreset);
        Assert.Equal(RulesetSnapshot.CustomPresetName, rules.PresetName);
        Assert.Contains("sin aprobación técnica", rules.PresetName, StringComparison.Ordinal);
        Assert.False(rules.HasSameValues(RulesetSnapshot.DemoPreset()));
    }

    [Theory]
    [InlineData(0, 480, 120, 180, 2, 30, 20, "periodos")]
    [InlineData(21, 480, 120, 180, 2, 30, 20, "periodos")]
    [InlineData(4, 59, 120, 180, 2, 30, 20, "periodo")]
    [InlineData(4, 480, 3601, 180, 2, 30, 20, "descanso entre periodos")]
    [InlineData(4, 480, 120, -1, 2, 30, 20, "medio tiempo")]
    [InlineData(4, 480, 120, 180, 11, 30, 20, "tiempos muertos")]
    [InlineData(4, 480, 120, 180, 2, 4, 4, "posesión")]
    [InlineData(4, 480, 120, 180, 2, 20, 30, "posesión reducida")]
    public void Un_valor_fuera_de_rango_se_rechaza_explicando_el_rango(
        int periods, int periodSeconds, int breakSeconds, int halftimeSeconds, int timeouts, int shot, int shotAlt, string mentions)
    {
        var ex = Assert.Throws<DomainException>(() =>
            RulesetSnapshot.ForEvent(periods, periodSeconds, breakSeconds, halftimeSeconds, timeouts, shot, shotAlt, false));

        Assert.Equal("reglas.valor_invalido", ex.Code);
        Assert.Contains(mentions, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("entre", ex.Message, StringComparison.Ordinal);
    }
}
