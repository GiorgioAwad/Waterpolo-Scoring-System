using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;
using FDNDA.Waterpolo.Infrastructure.Timing;

namespace FDNDA.Waterpolo.Infrastructure.Tests;

/// <summary>Transiciones de la señal de demo con un reloj controlable (sin esperas reales).</summary>
public class SimulatedTimingTests
{
    private static readonly TimingOptions Options = new() { StaleAfter = TimeSpan.FromMilliseconds(1500) };

    [Fact]
    public void Detenido_emite_los_valores_iniciales_y_no_descuenta()
    {
        var (time, source, tracker) = Create();

        Step(time, source, tracker, steps: 20); // 2 s detenido

        var view = tracker.Evaluate();
        Assert.Equal(SignalState.Live, view.Signal);
        Assert.Equal(480_000, view.GameClock.RemainingMs);
        Assert.Equal(30_000, view.ShotClock.RemainingMs);
        Assert.False(view.IsRunning);
        Assert.True(view.IsSimulation);
        Assert.Equal(SimulatedTimingSource.Label, view.SourceLabel);
    }

    [Fact]
    public void En_juego_descuenta_ambos_relojes_y_la_pausa_los_detiene()
    {
        var (time, source, tracker) = Create();

        source.StartClock();
        Step(time, source, tracker, steps: 25); // 2,5 s en juego
        Assert.Equal(477_500, tracker.Evaluate().GameClock.RemainingMs);
        Assert.Equal(27_500, tracker.Evaluate().ShotClock.RemainingMs);
        Assert.True(tracker.Evaluate().IsRunning);

        source.StopClock();
        Step(time, source, tracker, steps: 30); // 3 s en pausa
        var paused = tracker.Evaluate();
        Assert.Equal(SignalState.Live, paused.Signal); // pausa no es pérdida de señal
        Assert.Equal(477_500, paused.GameClock.RemainingMs);
        Assert.Equal(27_500, paused.ShotClock.RemainingMs);
        Assert.False(paused.IsRunning);
    }

    [Fact]
    public void Reset_de_posesion_no_altera_el_reloj_de_partido()
    {
        var (time, source, tracker) = Create();
        source.StartClock();
        Step(time, source, tracker, steps: 50);

        source.ResetShotClock();
        Step(time, source, tracker, steps: 10);

        var view = tracker.Evaluate();
        Assert.Equal(474_000, view.GameClock.RemainingMs);
        Assert.Equal(29_000, view.ShotClock.RemainingMs);
    }

    [Fact]
    public void Perdida_de_senal_deja_de_mostrar_ambos_relojes_como_vigentes()
    {
        var (time, source, tracker) = Create();
        source.StartClock();
        Step(time, source, tracker, steps: 10);

        source.DropSignal();
        Assert.Null(Step(time, source, tracker, steps: 5)); // 0,5 s: no llegan muestras
        var justDropped = tracker.Evaluate();
        Assert.Equal(SignalState.Live, justDropped.Signal); // aún dentro del umbral
        Assert.Equal(479_000, justDropped.GameClock.RemainingMs); // último valor recibido, sin extrapolar

        Step(time, source, tracker, steps: 11); // supera el umbral de 1,5 s
        var lost = tracker.Evaluate();
        Assert.Equal(SignalState.Lost, lost.Signal);
        Assert.False(lost.GameClock.IsCurrent);
        Assert.False(lost.ShotClock.IsCurrent);
        Assert.Null(lost.GameClock.RemainingMs);
        Assert.Null(lost.IsRunning);
        Assert.NotNull(lost.Notice);
        Assert.Equal(ClockFormatter.GameClockUnavailable, ClockFormatter.FormatGameClock(lost.GameClock, lost.Precision));
        Assert.Equal(ClockFormatter.ShotClockUnavailable, ClockFormatter.FormatShotClock(lost.ShotClock));
    }

    [Fact]
    public void Reconexion_usa_datos_nuevos_de_una_sesion_nueva()
    {
        var (time, source, tracker) = Create();
        source.StartClock();
        var before = Step(time, source, tracker, steps: 10)!; // 1 s en juego

        source.DropSignal();
        Step(time, source, tracker, steps: 50); // 5 s sin señal; la «consola» sigue contando
        Assert.Equal(SignalState.Lost, tracker.Evaluate().Signal);

        source.RestoreSignal();
        var after = Step(time, source, tracker, steps: 1)!;

        var view = tracker.Evaluate();
        Assert.Equal(SignalState.Live, view.Signal);
        Assert.NotEqual(before.SessionId, after.SessionId);
        Assert.True(after.Sequence > before.Sequence);
        Assert.Equal(473_900, view.GameClock.RemainingMs); // 6,1 s transcurridos: no se reanuda desde el valor viejo
        Assert.Equal(23_900, view.ShotClock.RemainingMs);
        Assert.Null(view.Notice);
    }

    [Fact]
    public void El_guion_demo_es_reproducible()
    {
        var first = RunStandardScript();
        var second = RunStandardScript();

        // 27 s en juego (0–10 y 13–30); posesión reiniciada en 26 s y detenida en 30 s.
        Assert.Equal((453_000, 26_000), first.Final);
        Assert.Equal(first.Final, second.Final);
        Assert.Equal(first.SamplesWhileDropped, second.SamplesWhileDropped);
        Assert.Equal(0, first.SamplesWhileDropped); // entre 16 s y 21 s no llega nada
        Assert.Equal(2, first.Sessions); // la reconexión abre una sesión nueva
        Assert.True(first.SawLost);
    }

    [Fact]
    public void Fin_de_periodo_detiene_el_reloj_en_cero()
    {
        var time = new ManualTimeProvider();
        var source = new SimulatedTimingSource(time, new SimulationOptions { GameClockSeconds = 2, ShotClockSeconds = 30 });
        var tracker = new TimingStateTracker(source.Kind, source.DisplayLabel, time, Options);

        source.StartClock();
        Step(time, source, tracker, steps: 40);

        var view = tracker.Evaluate();
        Assert.Equal(0, view.GameClock.RemainingMs);
        Assert.Equal(28_000, view.ShotClock.RemainingMs); // se detiene con el reloj de partido
        Assert.False(view.IsRunning);
        Assert.Equal("0.0", ClockFormatter.FormatGameClock(view.GameClock, view.Precision));
    }

    [Fact]
    public void Cargar_periodo_repone_los_relojes_detenidos()
    {
        var (time, source, tracker) = Create();
        source.StartClock();
        Step(time, source, tracker, steps: 30);

        source.LoadPeriod(gameClockSeconds: 300, shotClockSeconds: 25);
        Step(time, source, tracker, steps: 10);

        var view = tracker.Evaluate();
        Assert.Equal(300_000, view.GameClock.RemainingMs);
        Assert.Equal(25_000, view.ShotClock.RemainingMs);
        Assert.False(view.IsRunning);
    }

    private static (ManualTimeProvider Time, SimulatedTimingSource Source, TimingStateTracker Tracker) Create()
    {
        var time = new ManualTimeProvider();
        var source = new SimulatedTimingSource(time);
        var tracker = new TimingStateTracker(source.Kind, source.DisplayLabel, time, Options);
        return (time, source, tracker);
    }

    /// <summary>Avanza el reloj de la prueba en pasos de 100 ms, como el bucle real, y devuelve la última muestra.</summary>
    private static TimingSnapshot? Step(ManualTimeProvider time, SimulatedTimingSource source, TimingStateTracker tracker, int steps)
    {
        TimingSnapshot? last = null;
        for (var i = 0; i < steps; i++)
        {
            time.AdvanceMs(100);
            last = source.Pump();
            if (last is not null)
            {
                Assert.True(tracker.Accept(last));
            }
        }

        return last;
    }

    private static (
        (int Game, int Shot) Final, int SamplesWhileDropped, int Sessions, bool SawLost) RunStandardScript()
    {
        var (time, source, tracker) = Create();
        var sessions = new HashSet<Guid>();
        var samplesWhileDropped = 0;
        var sawLost = false;
        var elapsedMs = 0;

        source.RunScript(SimulationScript.Standard);
        while (elapsedMs < 31_000)
        {
            time.AdvanceMs(100);
            elapsedMs += 100;
            var sample = source.Pump();
            if (sample is not null)
            {
                sessions.Add(sample.SessionId);
                tracker.Accept(sample);
                if (elapsedMs is > 16_000 and < 21_000)
                {
                    samplesWhileDropped++;
                }
            }

            sawLost |= tracker.Evaluate().Signal == SignalState.Lost;
        }

        var view = tracker.Evaluate();
        return ((view.GameClock.RemainingMs!.Value, view.ShotClock.RemainingMs!.Value), samplesWhileDropped, sessions.Count, sawLost);
    }
}

/// <summary>Reglas de vigencia por reloj y descarte de lecturas que no deben mostrarse.</summary>
public class TimingStateTrackerTests
{
    private static readonly Guid Session = Guid.NewGuid();

    [Fact]
    public void Sin_muestras_la_senal_esta_en_espera_y_los_relojes_no_estan_vigentes()
    {
        var (_, tracker) = Create();

        var view = tracker.Evaluate();

        Assert.Equal(SignalState.Waiting, view.Signal);
        Assert.False(view.GameClock.IsCurrent);
        Assert.False(view.ShotClock.IsCurrent);
    }

    [Fact]
    public void Cada_reloj_conserva_su_propia_vigencia_cuando_llegan_por_separado()
    {
        var (time, tracker) = Create();

        Assert.True(tracker.Accept(Sample(time, 1, game: 300_000, shot: 20_000)));
        time.AdvanceMs(1000);
        Assert.True(tracker.Accept(Sample(time, 2, game: 299_000, shot: null))); // trama solo con reloj de partido
        time.AdvanceMs(1000);
        Assert.True(tracker.Accept(Sample(time, 3, game: 298_000, shot: null)));

        var view = tracker.Evaluate();
        Assert.Equal(SignalState.Live, view.Signal);
        Assert.Equal(298_000, view.GameClock.RemainingMs);
        Assert.False(view.ShotClock.IsCurrent); // su última lectura tiene 2 s: ya no se muestra como vigente
    }

    [Fact]
    public void Descarta_lecturas_repetidas_antiguas_invalidas_y_fuera_de_rango()
    {
        var (time, tracker) = Create();
        Assert.True(tracker.Accept(Sample(time, 10, game: 300_000, shot: 20_000)));

        Assert.False(tracker.Accept(Sample(time, 10, game: 111_000, shot: 1_000))); // repetida
        Assert.False(tracker.Accept(Sample(time, 9, game: 111_000, shot: 1_000))); // fuera de orden
        Assert.False(tracker.Accept(Sample(time, 11, game: -5, shot: 1_000))); // negativa
        Assert.False(tracker.Accept(Sample(time, 12, game: int.MaxValue, shot: 1_000))); // fuera de rango
        Assert.False(tracker.Accept(Sample(time, 13, game: null, shot: null))); // no trae ningún reloj
        Assert.False(tracker.Accept(Sample(time, 14, game: 111_000, shot: 1_000) with { Quality = SampleQuality.Suspect }));
        Assert.False(tracker.Accept(Sample(time, 15, game: 111_000, shot: 1_000) with { Source = ClockSource.Replay }));

        var old = Sample(time, 16, game: 111_000, shot: 1_000);
        time.AdvanceMs(1400);
        Assert.True(tracker.Accept(Sample(time, 17, game: 298_600, shot: 18_600)));
        time.AdvanceMs(200);
        Assert.False(tracker.Accept(old)); // lectura antigua entregada tarde

        var view = tracker.Evaluate();
        Assert.Equal(298_600, view.GameClock.RemainingMs);
        Assert.Equal(18_600, view.ShotClock.RemainingMs);
    }

    [Fact]
    public void Una_sesion_nueva_no_hereda_valores_de_la_anterior()
    {
        var (time, tracker) = Create();
        tracker.Accept(Sample(time, 1, game: 300_000, shot: 20_000));
        time.AdvanceMs(500);

        var newSession = Guid.NewGuid();
        Assert.True(tracker.Accept(Sample(time, 2, game: 250_000, shot: null) with { SessionId = newSession }));

        var view = tracker.Evaluate();
        Assert.Equal(250_000, view.GameClock.RemainingMs);
        Assert.False(view.ShotClock.IsCurrent); // el 20 s era de la sesión anterior
    }

    [Fact]
    public async Task Colorado_sin_puerto_configurado_devuelve_no_disponible_y_nunca_muestra_relojes()
    {
        var time = new ManualTimeProvider();
        var source = new ColoradoSystem6TimingSource();
        var log = new NullLog();
        await using var monitor = new TimingMonitor(source, time, new TimingOptions(), log);

        var start = await source.StartAsync();
        await monitor.StartAsync();

        Assert.False(start.IsAvailable);
        Assert.Contains("--puerto", start.Reason, StringComparison.Ordinal);

        var view = monitor.Current;
        Assert.Equal(SignalState.Unavailable, view.Signal);
        Assert.Equal(ClockSource.ColoradoSystem6, view.Source);
        Assert.False(view.IsSimulation); // no cambia por su cuenta a demo
        Assert.False(view.GameClock.IsCurrent);
        Assert.False(view.ShotClock.IsCurrent);
        Assert.Contains("--puerto", view.Notice, StringComparison.Ordinal);

        var context = monitor.Capture();
        Assert.Equal(ClockQuality.Unavailable, context.Quality);
        Assert.Null(context.GameClockRemainingMs);
    }

    [Theory]
    [InlineData(480_000, TimingPrecision.Tenths, "8:00")]
    [InlineData(479_999, TimingPrecision.Tenths, "7:59")]
    [InlineData(60_000, TimingPrecision.Tenths, "1:00")]
    [InlineData(59_940, TimingPrecision.Tenths, "59.9")]
    [InlineData(5_230, TimingPrecision.Tenths, "5.2")]
    [InlineData(5_230, TimingPrecision.Seconds, "0:05")]
    [InlineData(0, TimingPrecision.Seconds, "0:00")]
    public void Formato_del_reloj_de_partido(int ms, TimingPrecision precision, string expected)
    {
        Assert.Equal(expected, ClockFormatter.FormatGameClock(new ClockReading(ms), precision));
    }

    [Theory]
    [InlineData(30_000, "30")]
    [InlineData(29_999, "30")]
    [InlineData(29_000, "29")]
    [InlineData(100, "1")]
    [InlineData(0, "0")]
    public void Formato_del_reloj_de_posesion(int ms, string expected)
    {
        Assert.Equal(expected, ClockFormatter.FormatShotClock(new ClockReading(ms)));
    }

    [Fact]
    public void En_el_descanso_de_la_mesa_el_silencio_de_la_fuente_no_es_perdida_de_senal()
    {
        // La consola Colorado no emite durante el descanso entre periodos (colorado-system6.md §7).
        var (time, tracker) = Create();
        tracker.Accept(Sample(time, 1, game: 0, shot: 28_000));
        tracker.SetIntermission(true);
        time.AdvanceMs(30_000);

        var view = tracker.Evaluate();

        Assert.Equal(SignalState.Intermission, view.Signal);
        Assert.Null(view.Notice); // sin alarma
        Assert.False(view.GameClock.IsCurrent); // tampoco se muestra un tiempo viejo
        Assert.False(view.ShotClock.IsCurrent);
    }

    [Fact]
    public void Fuera_del_descanso_el_mismo_silencio_es_perdida_de_senal()
    {
        var (time, tracker) = Create();
        tracker.Accept(Sample(time, 1, game: 0, shot: 28_000));
        tracker.SetIntermission(true);
        tracker.SetIntermission(false); // la mesa empezó el periodo siguiente y la consola sigue callada
        time.AdvanceMs(30_000);

        Assert.Equal(SignalState.Lost, tracker.Evaluate().Signal);
    }

    [Fact]
    public void Si_llegan_datos_durante_el_descanso_la_senal_esta_vigente()
    {
        var (time, tracker) = Create();
        tracker.SetIntermission(true);
        tracker.Accept(Sample(time, 1, game: 360_000, shot: 28_000));

        var view = tracker.Evaluate();

        Assert.Equal(SignalState.Live, view.Signal);
        Assert.Equal(360_000, view.GameClock.RemainingMs);
    }

    [Fact]
    public void El_periodo_de_la_fuente_se_publica_solo_mientras_la_senal_esta_vigente()
    {
        var (time, tracker) = Create();
        tracker.Accept(Sample(time, 1, game: 300_000, shot: null) with { Period = 3 });
        tracker.Accept(Sample(time, 2, game: 299_000, shot: null)); // una muestra sin periodo no lo borra

        var live = tracker.Evaluate();
        time.AdvanceMs(2_000);
        var lost = tracker.Evaluate();

        Assert.Equal(3, live.SourcePeriod);
        Assert.Null(lost.SourcePeriod);
    }

    private static (ManualTimeProvider Time, TimingStateTracker Tracker) Create()
    {
        var time = new ManualTimeProvider();
        var tracker = new TimingStateTracker(
            ClockSource.Simulated, "PRUEBA", time, new TimingOptions { StaleAfter = TimeSpan.FromMilliseconds(1500) });
        return (time, tracker);
    }

    private static TimingSnapshot Sample(ManualTimeProvider time, long sequence, int? game, int? shot) =>
        new(
            ClockSource.Simulated,
            Session,
            game,
            shot,
            TimingPrecision.Tenths,
            IsRunning: true,
            Period: null,
            sequence,
            time.GetTimestamp(),
            time.GetUtcNow(),
            SampleQuality.Valid);
}
