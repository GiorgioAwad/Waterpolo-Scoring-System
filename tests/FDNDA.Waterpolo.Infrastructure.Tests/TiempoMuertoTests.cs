using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;
using FDNDA.Waterpolo.Infrastructure.Timing;
using FDNDA.Waterpolo.Infrastructure.Timing.Colorado;

namespace FDNDA.Waterpolo.Infrastructure.Tests;

/// <summary>
/// Reloj de tiempo muerto de la consola (bytes 21–24 de la trama <c>CTSWP1.1c</c>) y detección de su fin
/// para el aviso del marcador. Spec: docs/superpowers/specs/2026-11-25-avisos-y-faltas-design.md §4.
/// </summary>
public class TiempoMuertoTests
{
    private static readonly Guid Session = Guid.NewGuid();

    // ---------- Decodificador ----------

    [Theory]
    [InlineData("1:00", 60_000)]
    [InlineData(" :59", 59_000)]
    [InlineData(" :00", 0)]
    public void La_trama_entrega_el_reloj_de_tiempo_muerto(string field, int expectedMs)
    {
        var reading = Assert.Single(new CtswpFrameDecoder().Feed(CtswpDecoderTests.Frame(timeout: field)));

        Assert.Equal(expectedMs, reading.TimeoutClockRemainingMs);
        Assert.Equal(360_000, reading.GameClockRemainingMs);
    }

    [Fact]
    public void Sin_tiempo_muerto_el_campo_va_en_blanco_y_un_valor_invalido_descarta_la_trama()
    {
        var decoder = new CtswpFrameDecoder();

        Assert.Null(Assert.Single(decoder.Feed(CtswpDecoderTests.Frame())).TimeoutClockRemainingMs);
        Assert.Empty(decoder.Feed(CtswpDecoderTests.Frame(timeout: " :6x")));
        Assert.Equal(1L, decoder.InvalidFrames);
    }

    [Theory]
    [InlineData("s6-com2-03-guiada.txt")]
    [InlineData("s6-com2-04-casos-limite.txt")]
    public void En_las_capturas_reales_hay_un_tiempo_muerto_que_empieza_en_un_minuto_y_termina_una_vez(string name)
    {
        var capture = CtsCaptureFile.Load(CtswpDecoderTests.CapturePath(name));
        var time = new ManualTimeProvider();
        var tracker = Tracker(time, ClockSource.ColoradoSystem6);
        var decoder = new CtswpFrameDecoder();
        var session = Guid.NewGuid();
        var elapsed = TimeSpan.Zero;
        long sequence = 0;
        var peak = 0;

        foreach (var chunk in capture.Chunks)
        {
            time.Advance(chunk.At - elapsed);
            elapsed = chunk.At;
            if (chunk.IsReconnection)
            {
                decoder.Reset();
                session = Guid.NewGuid();
            }

            foreach (var reading in decoder.Feed(chunk.Bytes))
            {
                tracker.Accept(new TimingSnapshot(
                    ClockSource.ColoradoSystem6, session, reading.GameClockRemainingMs, reading.ShotClockRemainingMs,
                    reading.Precision, reading.IsRunning, reading.Period, ++sequence, time.GetTimestamp(), time.GetUtcNow(),
                    SampleQuality.Valid, reading.TimeoutClockRemainingMs));
                peak = Math.Max(peak, tracker.Evaluate().TimeoutClock.RemainingMs ?? 0);
            }
        }

        Assert.Equal(60_000, peak); // «1:00» al pulsar Start Timeout
        Assert.Equal(1, tracker.Evaluate().TimeoutsEnded); // Clear Timeout, una sola vez
    }

    // ---------- Seguimiento: cuándo terminó ----------

    [Fact]
    public void El_fin_se_cuenta_al_llegar_a_cero_o_al_borrarse_y_no_otra_vez_al_pasar_de_cero_a_blanco()
    {
        var (time, tracker) = Create();
        long seq = 0;

        Feed(time, tracker, ++seq, 2_000);
        Assert.Equal(2_000, tracker.Evaluate().TimeoutClock.RemainingMs);
        Feed(time, tracker, ++seq, 1_000);
        Feed(time, tracker, ++seq, 0);
        Assert.Equal(1, tracker.Evaluate().TimeoutsEnded);
        Assert.Equal(0, tracker.Evaluate().TimeoutClock.RemainingMs);

        Feed(time, tracker, ++seq, null); // Clear Timeout después de llegar a 0
        Assert.Equal(1, tracker.Evaluate().TimeoutsEnded);

        Feed(time, tracker, ++seq, 60_000);
        Feed(time, tracker, ++seq, null); // terminado antes de tiempo
        var view = tracker.Evaluate();
        Assert.Equal(2, view.TimeoutsEnded);
        Assert.False(view.TimeoutClock.IsCurrent);
    }

    [Fact]
    public void Una_perdida_de_senal_o_una_reconexion_no_cuentan_como_fin()
    {
        var (time, tracker) = Create();

        Feed(time, tracker, 1, 50_000);
        time.AdvanceMs(2_000);
        var lost = tracker.Evaluate();
        Assert.Equal(SignalState.Lost, lost.Signal);
        Assert.False(lost.TimeoutClock.IsCurrent); // sin señal no se muestra la cuenta

        time.AdvanceMs(4_000);
        Feed(time, tracker, 2, null); // vuelve la señal ya sin tiempo muerto: no se sabe cuándo terminó
        Assert.Equal(0, tracker.Evaluate().TimeoutsEnded);

        Feed(time, tracker, 3, 40_000);
        Feed(time, tracker, 4, null, Guid.NewGuid()); // reconexión: sesión nueva
        Assert.Equal(0, tracker.Evaluate().TimeoutsEnded);
    }

    // ---------- Simulador ----------

    [Fact]
    public void El_simulador_corre_el_tiempo_muerto_aparte_queda_en_cero_y_se_termina_a_mano()
    {
        var time = new ManualTimeProvider();
        var source = new SimulatedTimingSource(time);
        var tracker = Tracker(time, source.Kind);
        source.StartClock();
        time.AdvanceMs(1_000);
        Assert.True(tracker.Accept(source.Pump()!));
        var game = tracker.Evaluate().GameClock.RemainingMs;

        source.StartTimeout(3);
        for (var i = 0; i < 40; i++)
        {
            time.AdvanceMs(100);
            Assert.True(tracker.Accept(source.Pump()!));
        }

        var view = tracker.Evaluate();
        Assert.Equal(game, view.GameClock.RemainingMs); // el tiempo muerto detiene el partido
        Assert.False(view.IsRunning);
        Assert.Equal(0, view.TimeoutClock.RemainingMs); // llegó a 0 y quedó ahí
        Assert.Equal(1, view.TimeoutsEnded);

        source.ClearTimeout();
        Assert.True(tracker.Accept(source.Pump()!));
        Assert.False(tracker.Evaluate().TimeoutClock.IsCurrent);
        Assert.Equal(1, tracker.Evaluate().TimeoutsEnded);
    }

    [Theory]
    [InlineData(60_000, "1:00")]
    [InlineData(59_100, "1:00")]
    [InlineData(59_000, "59")]
    [InlineData(4_200, "5")]
    public void Formato_del_reloj_de_tiempo_muerto(int ms, string expected) =>
        Assert.Equal(expected, ClockFormatter.FormatTimeoutClock(new ClockReading(ms)));

    // ---------- Apoyo ----------

    private static (ManualTimeProvider Time, TimingStateTracker Tracker) Create()
    {
        var time = new ManualTimeProvider();
        return (time, Tracker(time, ClockSource.Simulated));
    }

    private static TimingStateTracker Tracker(ManualTimeProvider time, ClockSource source) =>
        new(source, "PRUEBA", time, new TimingOptions { StaleAfter = TimeSpan.FromMilliseconds(1500) });

    /// <summary>Muestra 500 ms después de la anterior, con el reloj de partido detenido y el de tiempo muerto indicado.</summary>
    private static void Feed(ManualTimeProvider time, TimingStateTracker tracker, long sequence, int? timeoutMs, Guid? session = null)
    {
        time.AdvanceMs(500);
        Assert.True(tracker.Accept(new TimingSnapshot(
            ClockSource.Simulated, session ?? Session, 300_000, 20_000, TimingPrecision.Seconds, IsRunning: false, Period: 2,
            sequence, time.GetTimestamp(), time.GetUtcNow(), SampleQuality.Valid, timeoutMs)));
    }
}
