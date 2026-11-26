using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Desktop.Presentation;
using FDNDA.Waterpolo.Desktop.ViewModels;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;

namespace FDNDA.Waterpolo.Desktop.Tests;

/// <summary>
/// Rediseño del marcador del público («A · Transmisión», 2026-11-25): lógica de presentación
/// sin ventanas. Spec: docs/superpowers/specs/2026-11-25-marcador-publico-design.md.
/// </summary>
public class MarcadorDisenoTests
{
    // ---------- Colores y textos ----------

    [Theory]
    [InlineData("#F2C200", "#000000")]
    [InlineData("#74ACDF", "#000000")]
    [InlineData("#D91023", "#FFFFFF")]
    [InlineData("#005AAA", "#FFFFFF")]
    [InlineData("azul", "#FFFFFF")]
    public void Los_colores_claros_llevan_texto_negro_y_los_oscuros_blanco(string color, string expected) =>
        Assert.Equal(expected, ColorTools.TextOn(color));

    [Fact]
    public void Oscurecer_y_dar_opacidad_conservan_el_tono_y_un_color_invalido_no_falla()
    {
        Assert.Equal("#804020", ColorTools.Darken("#FF8040", 0.5));
        Assert.Equal("#804020", ColorTools.Darken("FF8040", 0.5));
        Assert.Equal("#61D91023", ColorTools.WithAlpha("#D91023", 0.38));
        Assert.Equal("#00000000", ColorTools.WithAlpha("rojo", 0.38));
        Assert.Equal(ColorTools.Darken(ColorTools.Fallback, 0.5), ColorTools.Darken("rojo", 0.5));
        Assert.Equal(0, ColorTools.Luminance("#000000"), 3);
        Assert.Equal(1, ColorTools.Luminance("#FFFFFF"), 3);
    }

    [Theory]
    [InlineData(1, "1.er")]
    [InlineData(2, "2.º")]
    [InlineData(3, "3.er")]
    [InlineData(4, "4.º")]
    [InlineData(11, "11.º")]
    public void Los_ordinales_son_los_del_publico(int number, string expected) =>
        Assert.Equal(expected, ScoreboardTexts.Ordinal(number));

    // ---------- Columna central, puntos de periodo y atenuados ----------

    [Fact]
    public async Task El_marcador_pasa_por_previa_juego_descanso_y_final_con_sus_textos()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        await desk.Operations.OpenMatchAsync(TestDesk.MatchId);
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, new ManualTiming(), new ScoreboardSettings()));

        Assert.Equal(ScoreboardCenter.Preview, scoreboard.CenterMode);
        Assert.Equal(("PRÓXIMO PARTIDO", "VS"), (scoreboard.PeriodText, scoreboard.CenterHeadline));
        Assert.False(scoreboard.ShowClocks);
        Assert.False(scoreboard.ShowPeriodPips);

        var started = await desk.StartAsync();
        Assert.Equal(ScoreboardCenter.Clocks, scoreboard.CenterMode);
        Assert.Equal("1.er PERIODO", scoreboard.PeriodText);
        Assert.True(scoreboard.ShowClocks);
        Assert.True(scoreboard.ShowPeriodPips);
        Assert.Equal([true, false, false, false], scoreboard.PeriodPips.Select(p => p.Played));
        Assert.Equal(ColorTools.WithAlpha(started.Home.ColorHex, 0.38), scoreboard.HomeGlowColor);
        Assert.Equal(ColorTools.WithAlpha(started.Away.ColorHex, 0.38), scoreboard.AwayGlowColor);

        await desk.Operations.StartBreakAsync(new StartBreak(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        Assert.Equal(ScoreboardCenter.Break, scoreboard.CenterMode);
        Assert.Equal(("FIN DEL 1.er PERIODO", "DESCANSO"), (scoreboard.PeriodText, scoreboard.CenterHeadline));
        Assert.False(scoreboard.ShowClocks); // la consola no emite en el descanso
        Assert.True(scoreboard.ShowPeriodPips);

        await desk.Operations.StartNextPeriodAsync(new StartNextPeriod(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        Assert.Equal("2.º PERIODO", scoreboard.PeriodText);
        Assert.Equal([true, true, false, false], scoreboard.PeriodPips.Select(p => p.Played));
        Assert.False(scoreboard.HomeDimmed || scoreboard.AwayDimmed); // en juego nadie se atenúa

        await desk.GoalAsync(TeamSide.Home, started.Home.Roster[0].RosterEntryId);
        await desk.Operations.FinishMatchAsync(new FinishMatch(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        Assert.Equal(ScoreboardCenter.Final, scoreboard.CenterMode);
        Assert.Equal(("RESULTADO FINAL", "FINAL"), (scoreboard.PeriodText, scoreboard.CenterHeadline));
        Assert.False(scoreboard.HasCenterNote);
        Assert.False(scoreboard.ShowClocks);
        Assert.False(scoreboard.ShowPeriodPips);
        Assert.Equal((false, true), (scoreboard.HomeDimmed, scoreboard.AwayDimmed));
    }

    [Fact]
    public async Task Un_final_empatado_no_atenua_a_nadie()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, new ManualTiming(), new ScoreboardSettings()));
        await desk.StartAsync();

        await desk.Operations.FinishMatchAsync(new FinishMatch(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));

        Assert.Equal(ScoreboardCenter.Final, scoreboard.CenterMode);
        Assert.Equal((false, false), (scoreboard.HomeDimmed, scoreboard.AwayDimmed));
    }

    [Fact]
    public async Task En_la_tanda_no_hay_relojes_y_el_final_atenua_al_que_pierde_la_tanda()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        await SetRulesAsync(storage, desk, RulesetSnapshot.ForEvent(4, 480, 120, 180, 2, 30, 20, penaltyShootoutEnabled: true));
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, new ManualTiming(), new ScoreboardSettings()));
        var started = await desk.StartAsync();
        await PlayToLastPeriodAsync(desk);

        Assert.True((await desk.Operations.StartShootoutAsync(new StartShootout(Guid.NewGuid(), TestDesk.MatchId, desk.Operator))).IsSuccess);
        Assert.Equal(ScoreboardCenter.Shootout, scoreboard.CenterMode);
        Assert.Equal(("TANDA DE PENALES", "PENALES"), (scoreboard.PeriodText, scoreboard.CenterHeadline));
        Assert.Equal("Reglamentario 0 – 0", scoreboard.CenterNote);
        Assert.True(scoreboard.HasCenterNote);
        Assert.False(scoreboard.ShowClocks);
        Assert.False(scoreboard.ShowPeriodPips);

        await RegisterAsync(desk, IncidentType.ShootoutAttempt, TeamSide.Away, started.Away.Roster[0].RosterEntryId,
            penalty: PenaltyResult.Converted);
        await desk.Operations.FinishMatchAsync(new FinishMatch(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));

        Assert.Equal(ScoreboardCenter.Final, scoreboard.CenterMode);
        Assert.Equal("Penales 0 – 1", scoreboard.CenterNote);
        Assert.Equal((true, false), (scoreboard.HomeDimmed, scoreboard.AwayDimmed));
    }

    [Fact]
    public async Task En_la_prorroga_no_hay_puntos_de_periodo_y_el_final_lo_indica()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        await SetRulesAsync(storage, desk, RulesetSnapshot.ForEvent(4, 480, 120, 180, 2, 30, 20, penaltyShootoutEnabled: false,
            overtimePeriodCount: 1, overtimePeriodDurationSeconds: 180));
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, new ManualTiming(), new ScoreboardSettings()));
        await desk.StartAsync();
        await PlayToLastPeriodAsync(desk);

        await desk.Operations.StartBreakAsync(new StartBreak(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        Assert.Equal("FIN DEL 4.º PERIODO", scoreboard.PeriodText);
        await desk.Operations.StartNextPeriodAsync(new StartNextPeriod(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        Assert.Equal("PRÓRROGA 1", scoreboard.PeriodText);
        Assert.False(scoreboard.ShowPeriodPips);

        await desk.Operations.StartBreakAsync(new StartBreak(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        Assert.Equal("FIN DE LA PRÓRROGA 1", scoreboard.PeriodText);

        await desk.Operations.FinishMatchAsync(new FinishMatch(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        Assert.Equal("Tras prórroga", scoreboard.CenterNote);
    }

    // ---------- Anillo de posesión ----------

    [Fact]
    public async Task El_anillo_de_posesion_es_la_fraccion_de_la_posesion_completa_y_sin_senal_se_vacia()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var timing = new ManualTiming();
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, timing, new ScoreboardSettings()));

        timing.Publish(Live(gameMs: 300_000, shotMs: 18_000));
        Assert.Equal(0, scoreboard.ShotClockFraction); // sin partido no hay reglas: no se adivina la posesión completa

        await desk.StartAsync(); // reglas DEMO: posesión de 30 s
        Assert.Equal(0.6, scoreboard.ShotClockFraction, 6);
        Assert.False(scoreboard.ClocksDimmed);

        timing.Publish(Live(gameMs: 299_000, shotMs: 45_000));
        Assert.Equal(1, scoreboard.ShotClockFraction, 6);

        timing.Publish(new TimingView(ClockSource.Simulated, "DEMO / SIMULACIÓN", SignalState.Lost, null,
            ClockReading.NotAvailable, ClockReading.NotAvailable, null, TimingPrecision.Unknown));
        Assert.Equal(0, scoreboard.ShotClockFraction);
        Assert.True(scoreboard.ClocksDimmed);
        Assert.Equal(("--:--", "--"), (scoreboard.GameClockText, scoreboard.ShotClockText));
        Assert.True(scoreboard.ShowSignalNotice);
    }

    // ---------- Avisos «toma del lado» ----------

    [Fact]
    public async Task El_gol_sale_del_lado_del_equipo_con_su_color_y_el_jugador()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, new ManualTiming(), new ScoreboardSettings()));
        var scorer = started.Home.Roster[0];

        await desk.GoalAsync(TeamSide.Home, scorer.RosterEntryId);

        var banner = scoreboard.Banner!;
        Assert.Equal(ActionBannerKind.Goal, banner.Kind);
        Assert.True(banner.IsHome);
        Assert.True(banner.IsGoal);
        Assert.False(banner.ShowsCap);
        Assert.Equal("¡GOL!", banner.Headline);
        Assert.Equal((scorer.Cap, scorer.PlayerName, started.Home.Name), (banner.Cap, banner.PlayerName, banner.TeamName));
        Assert.Equal(started.Home.ColorHex, banner.Background);
        Assert.Equal(ColorTools.Darken(started.Home.ColorHex, 0.55), banner.BackgroundDark);
        Assert.Equal(ColorTools.TextOn(started.Home.ColorHex), banner.Foreground);
        Assert.Equal(banner.Text, scoreboard.BannerText);
        Assert.Equal(1, scoreboard.BannerGeneration);

        var shooter = started.Away.Roster[1];
        await RegisterAsync(desk, IncidentType.Penalty, TeamSide.Away, shooter.RosterEntryId, penalty: PenaltyResult.Converted);
        Assert.Equal("¡GOL DE PENAL!", scoreboard.Banner!.Headline);
        Assert.False(scoreboard.Banner.IsHome);
        Assert.Equal(started.Away.ColorHex, scoreboard.Banner.Background);
        Assert.Equal(2, scoreboard.BannerGeneration);
    }

    [Fact]
    public async Task La_exclusion_y_la_expulsion_usan_rojos_fijos_y_muestran_el_gorro()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, new ManualTiming(), new ScoreboardSettings()));
        var player = started.Away.Roster[3];

        await RegisterAsync(desk, IncidentType.Exclusion, TeamSide.Away, player.RosterEntryId, ExclusionKind.Exclusion);
        var exclusion = scoreboard.Banner!;
        Assert.Equal((ActionBannerKind.Exclusion, "EXCLUSIÓN"), (exclusion.Kind, exclusion.Headline));
        Assert.Equal(("#E53935", "#6A100E", "#FFFFFF"), (exclusion.Background, exclusion.BackgroundDark, exclusion.Foreground));
        Assert.True(exclusion.ShowsCap);
        Assert.False(exclusion.IsExpulsion || exclusion.IsGoal || exclusion.IsTimeout || exclusion.IsHome);
        Assert.Equal(player.Cap, exclusion.Cap);

        await RegisterAsync(desk, IncidentType.Exclusion, TeamSide.Home, started.Home.Roster[2].RosterEntryId, ExclusionKind.Expulsion);
        var expulsion = scoreboard.Banner!;
        Assert.Equal((ActionBannerKind.Expulsion, "EXPULSIÓN"), (expulsion.Kind, expulsion.Headline));
        Assert.Equal(("#9E1220", "#2E0308", "#FFFFFF"), (expulsion.Background, expulsion.BackgroundDark, expulsion.Foreground));
        Assert.True(expulsion.ShowsCap && expulsion.IsExpulsion && expulsion.IsHome);
        Assert.StartsWith("EXPULSIÓN  ·  ", scoreboard.BannerText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task El_tiempo_muerto_sale_en_azul_sin_jugador()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, new ManualTiming(), new ScoreboardSettings()));

        await RegisterAsync(desk, IncidentType.Timeout, TeamSide.Home, null);

        var banner = scoreboard.Banner!;
        Assert.Equal(ActionBannerKind.Timeout, banner.Kind);
        Assert.True(banner.IsTimeout && banner.IsHome);
        Assert.False(banner.ShowsCap);
        Assert.Equal("TIEMPO\nMUERTO", banner.Headline);
        Assert.Null(banner.Cap);
        Assert.Null(banner.PlayerName);
        Assert.Equal(("#2F80ED", "#0B2A63", "#FFFFFF"), (banner.Background, banner.BackgroundDark, banner.Foreground));
        Assert.Equal($"TIEMPO MUERTO  ·  {started.Home.Name}", scoreboard.BannerText);
    }

    [Fact]
    public async Task Cada_aviso_nuevo_sube_la_generacion_y_corregir_anular_o_desactivar_no()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        var settings = new ScoreboardSettings();
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, new ManualTiming(), settings));
        Assert.Equal(0, scoreboard.BannerGeneration);
        Assert.Null(scoreboard.Banner);
        var player = started.Home.Roster[1];

        var exclusion = await RegisterAsync(desk, IncidentType.Exclusion, TeamSide.Home, player.RosterEntryId, ExclusionKind.Exclusion);
        Assert.Equal(1, scoreboard.BannerGeneration);

        await desk.Operations.CorrectIncidentAsync(new CorrectIncident(
            Guid.NewGuid(), TestDesk.MatchId, exclusion, TeamSide.Home, player.RosterEntryId, "Fue expulsión", desk.Operator,
            ExclusionKind: ExclusionKind.Expulsion));
        await desk.Operations.VoidIncidentAsync(new VoidIncident(Guid.NewGuid(), TestDesk.MatchId, exclusion, "No hubo exclusión", desk.Operator));
        Assert.Equal(1, scoreboard.BannerGeneration);

        settings.TimeoutAnimationsEnabled = false;
        await RegisterAsync(desk, IncidentType.Timeout, TeamSide.Away, null);
        Assert.Equal(1, scoreboard.BannerGeneration);

        await desk.GoalAsync(TeamSide.Away, started.Away.Roster[0].RosterEntryId);
        Assert.Equal(2, scoreboard.BannerGeneration);
    }

    // ---------- Apoyo ----------

    private static TimingView Live(int gameMs, int shotMs) =>
        new(ClockSource.Simulated, "DEMO / SIMULACIÓN", SignalState.Live, null,
            new ClockReading(gameMs), new ClockReading(shotMs), true, TimingPrecision.Seconds);

    /// <summary>Juega del 1.er al 4.º periodo con descansos, sin goles.</summary>
    private static async Task PlayToLastPeriodAsync(TestDesk desk)
    {
        for (var period = 1; period <= 3; period++)
        {
            await desk.Operations.StartBreakAsync(new StartBreak(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
            await desk.Operations.StartNextPeriodAsync(new StartNextPeriod(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        }
    }

    private static async Task SetRulesAsync(TempStorage storage, TestDesk desk, RulesetSnapshot rules)
    {
        var catalog = new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true);
        Assert.True((await catalog.UpdateTournamentRulesAsync(DemoDataSeeder.TournamentId, rules)).IsSuccess);
    }

    private static async Task<Guid> RegisterAsync(
        TestDesk desk, IncidentType type, TeamSide side, Guid? rosterEntryId, ExclusionKind? exclusion = null, PenaltyResult? penalty = null)
    {
        var result = await desk.Operations.RegisterIncidentAsync(new RegisterIncident(
            Guid.NewGuid(), TestDesk.MatchId, type, side, rosterEntryId, desk.Operator, PenaltyResult: penalty, ExclusionKind: exclusion));
        Assert.True(result.IsSuccess, result.Message);
        return result.Snapshot!.Incidents.OrderBy(i => i.Sequence).Last().IncidentId;
    }

    private static T Immediate<T>(Func<T> create)
    {
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new ImmediateContext());
            return create();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class ImmediateContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }

    /// <summary>Cronometraje que la prueba publica a mano.</summary>
    private sealed class ManualTiming : ITimingFeed
    {
        public TimingView Current { get; private set; } = TimingView.Initial(ClockSource.Simulated, "DEMO / SIMULACIÓN");

        public event EventHandler<TimingView>? Changed;

        public void Publish(TimingView view)
        {
            Current = view;
            Changed?.Invoke(this, view);
        }
    }
}
