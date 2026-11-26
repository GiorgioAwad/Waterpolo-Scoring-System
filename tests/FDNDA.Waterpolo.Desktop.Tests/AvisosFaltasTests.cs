using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Desktop.Presentation;
using FDNDA.Waterpolo.Desktop.ViewModels;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;

namespace FDNDA.Waterpolo.Desktop.Tests;

/// <summary>
/// Avisos de falta personal, fin de tiempo muerto y anuncios de periodo, y faltas por jugador en el
/// marcador del público. Spec: docs/superpowers/specs/2026-11-25-avisos-y-faltas-design.md.
/// </summary>
public class AvisosFaltasTests
{
    // ---------- Faltas por jugador ----------

    [Fact]
    public async Task La_falta_personal_sale_en_ambar_con_el_gorro_y_su_numero_de_falta()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, new ManualTiming(), new ScoreboardSettings()));
        var player = started.Away.Roster[2];

        await RegisterAsync(desk, IncidentType.PersonalFoul, TeamSide.Away, player.RosterEntryId);

        var foul = scoreboard.Banner!;
        Assert.Equal((ActionBannerKind.Foul, "FALTA\nPERSONAL", "1.ª FALTA"), (foul.Kind, foul.Headline, foul.FoulLine));
        Assert.Equal(("#F2994A", "#5C2A06", "#FFFFFF"), (foul.Background, foul.BackgroundDark, foul.Foreground));
        Assert.True(foul.ShowsCap && foul.HasFoulLine);
        Assert.False(foul.IsHome || foul.IsGoal || foul.IsTimeout);
        Assert.Equal((player.Cap, player.PlayerName), (foul.Cap, foul.PlayerName));
        Assert.Equal($"FALTA PERSONAL  ·  {started.Away.Name}  ·  {player.Cap}  {player.PlayerName}  ·  1.ª FALTA", scoreboard.BannerText);

        // La exclusión del mismo jugador es su segunda falta y lo dice.
        await RegisterAsync(desk, IncidentType.Exclusion, TeamSide.Away, player.RosterEntryId, ExclusionKind.Exclusion);
        Assert.Equal((ActionBannerKind.Exclusion, "2.ª FALTA"), (scoreboard.Banner!.Kind, scoreboard.Banner.FoulLine));
        Assert.Equal(2, scoreboard.BannerGeneration);

        // El gol no lleva línea de faltas.
        await desk.GoalAsync(TeamSide.Away, player.RosterEntryId);
        Assert.False(scoreboard.Banner!.HasFoulLine);
    }

    [Fact]
    public async Task Los_gorros_con_faltas_se_ven_bajo_el_resultado_y_el_nuevo_late()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        await desk.Operations.OpenMatchAsync(TestDesk.MatchId);
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, new ManualTiming(), new ScoreboardSettings()));
        Assert.False(scoreboard.ShowFouls); // en la previa no hay resultado ni faltas

        var started = await desk.StartAsync();
        Assert.True(scoreboard.ShowFouls);
        Assert.Empty(scoreboard.HomeFouls);
        var (first, second) = (started.Home.Roster[4], started.Home.Roster[1]);

        await RegisterAsync(desk, IncidentType.PersonalFoul, TeamSide.Home, first.RosterEntryId);
        await RegisterAsync(desk, IncidentType.Exclusion, TeamSide.Home, first.RosterEntryId, ExclusionKind.Expulsion);
        var last = await RegisterAsync(desk, IncidentType.PersonalFoul, TeamSide.Home, second.RosterEntryId);

        Assert.Equal([(second.Cap, 1, true), (first.Cap, 2, false)], scoreboard.HomeFouls.Select(c => (c.Cap, c.Fouls, c.IsNew)));
        Assert.Equal([true, true, false], scoreboard.HomeFouls[1].Dots);
        Assert.False(scoreboard.HomeFouls[1].HasExtra);
        Assert.Empty(scoreboard.AwayFouls);
        Assert.Equal($"Gorro {first.Cap}, {first.PlayerName}: 2 faltas", scoreboard.HomeFouls[1].Description);

        // Anular la falta la quita; corregir o anular no hace latir a nadie.
        await desk.Operations.VoidIncidentAsync(new VoidIncident(Guid.NewGuid(), TestDesk.MatchId, last, "No fue falta", desk.Operator));
        Assert.Equal([(first.Cap, 2, false)], scoreboard.HomeFouls.Select(c => (c.Cap, c.Fouls, c.IsNew)));

        // Un gol no marca ningún gorro como nuevo.
        await desk.GoalAsync(TeamSide.Home, first.RosterEntryId);
        Assert.DoesNotContain(scoreboard.HomeFouls, c => c.IsNew);
    }

    [Fact]
    public void Con_mas_de_tres_faltas_se_llenan_los_tres_puntos_y_se_muestra_el_numero()
    {
        var chip = new FoulChip(Guid.NewGuid(), 7, "Jugador Ficticio", 4, IsNew: false);

        Assert.Equal([true, true, true], chip.Dots);
        Assert.True(chip.HasExtra);
        Assert.Equal("4", chip.ExtraText);
        Assert.Equal("Gorro 7, Jugador Ficticio: 1 falta", (chip with { Fouls = 1 }).Description);
    }

    [Fact]
    public async Task Sin_el_aviso_de_falta_no_hay_animacion_pero_el_gorro_suma()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        var settings = new ScoreboardSettings { FoulAnimationsEnabled = false };
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, new ManualTiming(), settings));

        await RegisterAsync(desk, IncidentType.PersonalFoul, TeamSide.Home, started.Home.Roster[0].RosterEntryId);

        Assert.Equal(0, scoreboard.BannerGeneration);
        Assert.Equal(1, Assert.Single(scoreboard.HomeFouls).Fouls);
        Assert.False(scoreboard.HomeFouls[0].IsNew); // sin aviso no hay latido
    }

    [Theory]
    [InlineData(1, "1.ª FALTA")]
    [InlineData(3, "3.ª FALTA")]
    [InlineData(0, "")]
    public void La_linea_de_faltas_usa_el_ordinal_femenino(int fouls, string expected) =>
        Assert.Equal(expected, ScoreboardTexts.FoulLine(fouls));

    // ---------- Anuncios de periodo ----------

    [Fact]
    public async Task Inicio_descanso_periodo_y_final_se_anuncian_y_reabrir_no()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        await desk.Operations.OpenMatchAsync(TestDesk.MatchId);
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, new ManualTiming(), new ScoreboardSettings()));
        Assert.Equal(0, scoreboard.AnnouncementGeneration); // abrir el partido no anuncia

        var started = await desk.StartAsync();
        Assert.Equal((ScoreboardAnnouncement.PeriodStarted, 1), (scoreboard.Announcement, scoreboard.AnnouncementGeneration));

        await desk.GoalAsync(TeamSide.Home, started.Home.Roster[0].RosterEntryId);
        Assert.Equal(1, scoreboard.AnnouncementGeneration); // un gol no es un anuncio de periodo

        await desk.Operations.StartBreakAsync(new StartBreak(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        Assert.Equal((ScoreboardAnnouncement.BreakStarted, 2), (scoreboard.Announcement, scoreboard.AnnouncementGeneration));

        await desk.Operations.StartNextPeriodAsync(new StartNextPeriod(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        Assert.Equal((ScoreboardAnnouncement.PeriodStarted, 3), (scoreboard.Announcement, scoreboard.AnnouncementGeneration));

        await desk.Operations.FinishMatchAsync(new FinishMatch(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        Assert.Equal((ScoreboardAnnouncement.Finished, 4), (scoreboard.Announcement, scoreboard.AnnouncementGeneration));

        Assert.True((await desk.Operations.ReopenMatchAsync(
            new ReopenMatch(Guid.NewGuid(), TestDesk.MatchId, "Error de mesa", desk.Operator))).IsSuccess);
        Assert.Equal(4, scoreboard.AnnouncementGeneration);
    }

    [Fact]
    public async Task Sin_el_anuncio_de_periodos_no_se_anuncia_nada()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var settings = new ScoreboardSettings { PeriodAnimationsEnabled = false };
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, new ManualTiming(), settings));

        await desk.StartAsync();
        settings.PeriodAnimationsEnabled = true;
        settings.AnimationsEnabled = false;
        await desk.Operations.StartBreakAsync(new StartBreak(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));

        Assert.Equal(0, scoreboard.AnnouncementGeneration);
    }

    // ---------- Tiempo muerto: cuenta y fin ----------

    [Fact]
    public async Task Durante_el_tiempo_muerto_el_anillo_muestra_su_cuenta_y_al_terminar_sale_el_aviso_del_equipo()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        var timing = new ManualTiming();
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, timing, new ScoreboardSettings()));
        await RegisterAsync(desk, IncidentType.Timeout, TeamSide.Away, null);
        Assert.Equal(1, scoreboard.BannerGeneration);

        timing.Publish(Live(timeoutMs: 60_000, ended: 0));
        Assert.True(scoreboard.TimeoutActive);
        Assert.Equal(("1:00", 1.0), (scoreboard.TimeoutClockText, scoreboard.TimeoutFraction));

        timing.Publish(Live(timeoutMs: 15_000, ended: 0));
        Assert.Equal(("15", 0.25), (scoreboard.TimeoutClockText, scoreboard.TimeoutFraction));
        Assert.Equal(1, scoreboard.BannerGeneration);

        timing.Publish(Live(timeoutMs: null, ended: 1));
        Assert.False(scoreboard.TimeoutActive);
        var banner = scoreboard.Banner!;
        Assert.Equal((ActionBannerKind.TimeoutEnd, "FIN DEL\nTIEMPO\nMUERTO"), (banner.Kind, banner.Headline));
        Assert.True(banner.IsTimeout);
        Assert.False(banner.IsHome || banner.ShowsCap || banner.HasFoulLine);
        Assert.Equal(("#2F80ED", "#0B2A63"), (banner.Background, banner.BackgroundDark));
        Assert.Equal($"FIN DEL TIEMPO MUERTO  ·  {started.Away.Name}", scoreboard.BannerText);
        Assert.Equal(2, scoreboard.BannerGeneration);

        timing.Publish(Live(timeoutMs: null, ended: 1)); // la misma cuenta no vuelve a animar
        Assert.Equal(2, scoreboard.BannerGeneration);
    }

    [Fact]
    public async Task Sin_tiempo_muerto_registrado_en_el_periodo_su_fin_no_tiene_equipo_y_no_se_anuncia()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        await desk.StartAsync();
        await RegisterAsync(desk, IncidentType.Timeout, TeamSide.Home, null); // del 1.er periodo
        await desk.Operations.StartBreakAsync(new StartBreak(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        await desk.Operations.StartNextPeriodAsync(new StartNextPeriod(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        var timing = new ManualTiming();
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, timing, new ScoreboardSettings()));

        timing.Publish(Live(timeoutMs: 30_000, ended: 0));
        timing.Publish(Live(timeoutMs: null, ended: 1));

        Assert.Equal(0, scoreboard.BannerGeneration);
        Assert.False(scoreboard.TimeoutActive);
    }

    [Fact]
    public async Task Al_abrir_la_pantalla_no_se_anuncian_tiempos_muertos_ya_terminados_ni_si_esta_desactivado()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        await desk.StartAsync();
        await RegisterAsync(desk, IncidentType.Timeout, TeamSide.Home, null);
        var timing = new ManualTiming();
        timing.Publish(Live(timeoutMs: null, ended: 3));
        var settings = new ScoreboardSettings();
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, timing, settings));
        Assert.Equal(0, scoreboard.BannerGeneration);

        settings.TimeoutAnimationsEnabled = false;
        timing.Publish(Live(timeoutMs: null, ended: 4));
        Assert.Equal(0, scoreboard.BannerGeneration);

        settings.TimeoutAnimationsEnabled = true;
        timing.Publish(Live(timeoutMs: null, ended: 5));
        Assert.Equal((ActionBannerKind.TimeoutEnd, 1), (scoreboard.Banner!.Kind, scoreboard.BannerGeneration));
    }

    // ---------- Apoyo ----------

    private static TimingView Live(int? timeoutMs, int ended) =>
        new(ClockSource.Simulated, "DEMO / SIMULACIÓN", SignalState.Live, null,
            new ClockReading(300_000), new ClockReading(20_000), false, TimingPrecision.Seconds,
            TimeoutClock: new ClockReading(timeoutMs), TimeoutsEnded: ended);

    private static async Task<Guid> RegisterAsync(
        TestDesk desk, IncidentType type, TeamSide side, Guid? rosterEntryId, ExclusionKind? exclusion = null)
    {
        var result = await desk.Operations.RegisterIncidentAsync(new RegisterIncident(
            Guid.NewGuid(), TestDesk.MatchId, type, side, rosterEntryId, desk.Operator, ExclusionKind: exclusion));
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
