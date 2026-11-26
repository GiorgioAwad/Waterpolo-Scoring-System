using System.Text;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Infrastructure.Timing;
using FDNDA.Waterpolo.Infrastructure.Timing.Colorado;

namespace FDNDA.Waterpolo.Infrastructure.Tests;

/// <summary>
/// Hito B: tramas <c>CTSWP1.1c</c> que la System 6 de la FDNDA emite por COM PORT 2 (115200 8-N-1).
/// Las tramas sintéticas se arman con el mapa de campos de docs/colorado-system6.md §7; las capturas
/// reales están en docs/evidencia/capturas (consola prestada, 2026-11-24, solo lectura).
/// </summary>
public class CtswpDecoderTests
{
    [Fact]
    public void Una_trama_en_reposo_entrega_partido_posesion_y_periodo()
    {
        var decoder = new CtswpFrameDecoder();

        var reading = Assert.Single(decoder.Feed(Frame(game: " 6:00", shot: "28", period: '1')));

        Assert.Equal(360_000, reading.GameClockRemainingMs);
        Assert.Equal(28_000, reading.ShotClockRemainingMs);
        Assert.Equal(1, reading.Period);
        Assert.Equal(TimingPrecision.Seconds, reading.Precision);
        Assert.Null(reading.IsRunning); // la trama no dice si el reloj corre
        Assert.Equal((1L, 0L), (decoder.ValidFrames, decoder.InvalidFrames));
    }

    [Theory]
    [InlineData("  :34", 34_000)]
    [InlineData("  :00", 0)]
    [InlineData("10:00", 600_000)]
    [InlineData(" 0:59", 59_000)]
    public void El_tiempo_de_partido_admite_minutos_y_el_formato_del_ultimo_minuto(string game, int expectedMs)
    {
        var reading = Assert.Single(new CtswpFrameDecoder().Feed(Frame(game: game)));

        Assert.Equal(expectedMs, reading.GameClockRemainingMs);
    }

    [Fact]
    public void Posesion_en_blanco_entrega_solo_el_tiempo_de_partido()
    {
        var reading = Assert.Single(new CtswpFrameDecoder().Feed(Frame(shot: "  ")));

        Assert.Null(reading.ShotClockRemainingMs);
        Assert.Equal(360_000, reading.GameClockRemainingMs);
    }

    [Fact]
    public void Los_bytes_sueltos_fuera_de_una_trama_se_descartan_y_se_cuentan()
    {
        var decoder = new CtswpFrameDecoder();

        var readings = decoder.Feed([0x00, 0x41, .. Frame(), 0x00]);

        Assert.Single(readings);
        Assert.Equal(3L, decoder.DiscardedBytes);
        Assert.Equal(0L, decoder.InvalidFrames);
    }

    [Fact]
    public void La_misma_secuencia_cortada_byte_a_byte_da_el_mismo_resultado()
    {
        byte[] stream = [.. Frame(game: " 6:00"), .. Frame(game: " 5:59", shot: "27"), .. Frame(game: "  :34", shot: "07", period: '4')];
        var whole = new CtswpFrameDecoder();
        var byByte = new CtswpFrameDecoder();

        var a = whole.Feed(stream);
        var b = stream.SelectMany(x => byByte.Feed([x])).ToList();

        Assert.Equal(3, a.Count);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Una_trama_cortada_por_el_inicio_de_otra_se_descarta_y_la_siguiente_vale()
    {
        var decoder = new CtswpFrameDecoder();
        var cut = Frame(game: " 5:00")[..40]; // la consola se apagó a mitad de la trama

        var reading = Assert.Single(decoder.Feed([.. cut, .. Frame(game: " 6:00")]));

        Assert.Equal(360_000, reading.GameClockRemainingMs);
        Assert.Equal((1L, 1L), (decoder.ValidFrames, decoder.InvalidFrames));
    }

    [Fact]
    public void Reset_descarta_la_trama_a_medias()
    {
        var decoder = new CtswpFrameDecoder();
        var frame = Frame();
        decoder.Feed(frame.AsSpan(0, 50));

        decoder.Reset();
        var readings = decoder.Feed(frame.AsSpan(50));

        Assert.Empty(readings);
        Assert.Equal(0L, decoder.ValidFrames);
    }

    [Theory]
    [InlineData("encabezado")]
    [InlineData("corta")]
    [InlineData("larga")]
    [InlineData("segundos")]
    [InlineData("minutos")]
    [InlineData("posesion")]
    [InlineData("periodo")]
    [InlineData("cierre")]
    public void Una_trama_invalida_no_entrega_lectura_y_se_cuenta(string defecto)
    {
        var body = Body();
        switch (defecto)
        {
            case "encabezado": Encoding.ASCII.GetBytes("CTSWP1.2a").CopyTo(body, 0); break;
            case "corta": body = body[..96]; break;
            case "larga": body = [.. body, 0x20]; break;
            case "segundos": Encoding.ASCII.GetBytes(" 5:75").CopyTo(body, 10); break;
            case "minutos": Encoding.ASCII.GetBytes("5X:00").CopyTo(body, 10); break;
            case "posesion": Encoding.ASCII.GetBytes("2A").CopyTo(body, 18); break;
            case "periodo": body[20] = (byte)'X'; break;
            case "cierre": body[96] = 0x20; break;
        }

        var decoder = new CtswpFrameDecoder();
        var readings = decoder.Feed([0x02, .. body, 0x04]);

        Assert.Empty(readings);
        Assert.Equal((0L, 1L), (decoder.ValidFrames, decoder.InvalidFrames));
    }

    [Fact]
    public void Un_flujo_sin_fin_de_trama_no_crece_sin_limite()
    {
        var decoder = new CtswpFrameDecoder();
        byte[] noise = [0x02, .. Enumerable.Repeat((byte)0x30, 500)];

        decoder.Feed(noise);
        var reading = Assert.Single(decoder.Feed(Frame()));

        Assert.Equal(360_000, reading.GameClockRemainingMs);
        Assert.Equal(1L, decoder.InvalidFrames);
    }

    [Fact]
    public void La_captura_real_guiada_se_lee_completa_y_sin_tramas_invalidas()
    {
        var (decoder, readings) = DecodeCapture("s6-com2-02-guiada.txt");

        Assert.Equal(0L, decoder.InvalidFrames);
        Assert.Equal(readings.Count, (int)decoder.ValidFrames);
        Assert.True(readings.Count > 300);
        Assert.Equal((360_000, 28_000, 1), Clocks(readings[0]));
        Assert.Contains((359_000, 27_000, 1), readings.Select(Clocks)); // primer segundo en marcha
        Assert.Contains((353_000, 28_000, 1), readings.Select(Clocks)); // Reset de posesión
        Assert.Contains((353_000, 18_000, 1), readings.Select(Clocks)); // reset alterno («18»)
    }

    [Fact]
    public void La_captura_real_trae_el_ultimo_minuto_y_el_cambio_de_periodo()
    {
        var (decoder, readings) = DecodeCapture("s6-com2-03-guiada.txt");

        Assert.Equal(0L, decoder.InvalidFrames);
        Assert.Contains((360_000, 28_000, 2), readings.Select(Clocks));
        Assert.Contains((45_000, 18_000, 4), readings.Select(Clocks));
        Assert.Contains((34_000, 7_000, 4), readings.Select(Clocks)); // pantalla :33.99 y 06.99
    }

    [Fact]
    public void La_captura_real_con_cable_desenchufado_y_consola_apagada_no_produce_lecturas_falsas()
    {
        var (decoder, readings) = DecodeCapture("s6-com2-04-casos-limite.txt");

        // Al reabrir el puerto llega el final de una trama a medias, y al arrancar la consola, tres 0x00:
        // se descartan como bytes sueltos, sin inventar lecturas.
        Assert.Equal((302L, 0L, 44L), (decoder.ValidFrames, decoder.InvalidFrames, decoder.DiscardedBytes));
        Assert.Equal(302, readings.Count);
        Assert.Equal((34_000, 7_000, 4), Clocks(readings[0]));
        Assert.Equal((360_000, 28_000, 1), Clocks(readings[^1])); // tras el apagado, partido nuevo
    }

    [Fact]
    public void La_captura_real_se_convierte_en_una_grabacion_con_una_sesion_por_conexion()
    {
        var capture = CtsCaptureFile.Load(CapturePath("s6-com2-04-casos-limite.txt"));

        var recording = CtsCaptureConverter.FromCtswp(capture);

        Assert.Equal(302, recording.Samples.Count);
        Assert.Equal([1, 2], recording.Samples.Select(s => s.Session).Distinct());
        var first = recording.Samples[0];
        Assert.Equal((34_000, 7_000, 4, TimingPrecision.Seconds), (first.GameClockRemainingMs, first.ShotClockRemainingMs, first.Period, first.Precision));
        Assert.Null(first.IsRunning);
    }

    [Fact]
    public void El_formato_de_la_captura_se_reconoce_por_su_contenido()
    {
        var real = CtsCaptureFile.Load(CapturePath("s6-com2-02-guiada.txt"));
        var synthetic = CtsCaptureFile.Load(Path.Combine(Path.GetDirectoryName(CapturePath("x"))!, "..", "..", "..", "samples", "cronometraje", "captura-sintetica-cts.txt"));

        Assert.True(CtsCaptureConverter.IsCtswp(real));
        Assert.False(CtsCaptureConverter.IsCtswp(synthetic));
    }

    private static (int?, int?, int?) Clocks(ColoradoReading r) => (r.GameClockRemainingMs, r.ShotClockRemainingMs, r.Period);

    private static (CtswpFrameDecoder Decoder, List<ColoradoReading> Readings) DecodeCapture(string name)
    {
        var capture = CtsCaptureFile.Load(CapturePath(name));
        var decoder = new CtswpFrameDecoder();
        var readings = new List<ColoradoReading>();
        foreach (var chunk in capture.Chunks)
        {
            if (chunk.IsReconnection)
            {
                decoder.Reset();
            }

            readings.AddRange(decoder.Feed(chunk.Bytes));
        }

        return (decoder, readings);
    }

    internal static string CapturePath(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FDNDA.Waterpolo.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "docs", "evidencia", "capturas", name);
    }

    /// <summary>Trama completa (STX + cuerpo + EOT) con el mapa de campos de colorado-system6.md §7.</summary>
    internal static byte[] Frame(string game = " 6:00", string shot = "28", char period = '1', string timeout = "    ") =>
        [0x02, .. Body(game, shot, period, timeout), 0x04];

    /// <summary>Cuerpo de 97 bytes: los campos que no se usan quedan como en una consola en reposo.</summary>
    private static byte[] Body(string game = " 6:00", string shot = "28", char period = '1', string timeout = "    ")
    {
        var text = new StringBuilder()
            .Append("CTSWP1.1c").Append('\0')
            .Append(game).Append("   ").Append(shot).Append(period)
            .Append(timeout)                                  // reloj de tiempo muerto (21–24)
            .Append(" 0").Append(new string('0', 15))          // White: goles, tiempos muertos y dígitos (25–41)
            .Append(new string(' ', 18))                       // White: exclusiones (42–59)
            .Append(" 0").Append(new string('0', 15))          // Dark (60–76)
            .Append(new string(' ', 18))                       // Dark: exclusiones (77–94)
            .ToString();
        Assert.Equal(95, text.Length);
        return [.. Encoding.Latin1.GetBytes(text), 0xFF, 0xFF];
    }
}
