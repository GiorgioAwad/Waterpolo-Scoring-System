using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;
using FDNDA.Waterpolo.Infrastructure.Timing;
using FDNDA.Waterpolo.Infrastructure.Timing.Colorado;

namespace FDNDA.Waterpolo.Infrastructure.Tests;

/// <summary>
/// Hito B (preparación): decodificador del flujo de tablero CTS e intérprete de waterpolo. Todos los
/// bytes son SINTÉTICOS, armados según el formato que describen proyectos de terceros; no hay capturas
/// reales de la consola. Estas pruebas fijan la implementación, no validan el protocolo.
/// </summary>
public class CtsScoreboardTests
{
    [Fact]
    public void Los_bytes_de_direccion_y_de_dato_siguen_el_formato_descrito()
    {
        // Valores derivados del formato descrito por scorebox-consoles y coloradoScoreboard.
        Assert.Equal(0xBC, CtsScoreboardDecoder.AddressByte(1, format: false));
        Assert.Equal(0xBD, CtsScoreboardDecoder.AddressByte(1, format: true));
        Assert.Equal(0xB8, CtsScoreboardDecoder.AddressByte(3, format: false));
        Assert.Equal(0xA2, CtsScoreboardDecoder.AddressByte(14, format: false));
        Assert.Equal(0xA4, CtsScoreboardDecoder.AddressByte(13, format: false));
        Assert.Equal(0x0A, CtsScoreboardDecoder.DataByte(0, 5));   // nibble bajo invertido
        Assert.Equal(0x70, CtsScoreboardDecoder.DataByte(7, 15));  // 15 = dígito apagado
    }

    [Fact]
    public void Un_canal_se_entrega_completo_al_llegar_la_lectura_de_otro_con_digitos_en_blanco_y_formato()
    {
        var decoder = new CtsScoreboardDecoder();

        var updates = decoder.Feed([.. Module(1, "_____746", "......22"), .. Module(3, "____28__", ".....2..")]);
        var last = decoder.Flush();

        var game = Assert.Single(updates);
        Assert.Equal(1, game.Channel);
        Assert.Equal(new int?[] { null, null, null, null, null, 7, 4, 6 }, game.Digits);
        Assert.True(game.FormatsKnown);
        Assert.Equal(2, game.Formats[6]);
        Assert.Equal(3, Assert.Single(last).Channel);
        Assert.Equal((2L, 0L), (decoder.Updates, decoder.DiscardedBytes));
    }

    [Fact]
    public void Tramas_cortadas_en_cualquier_punto_o_juntas_dan_el_mismo_resultado()
    {
        byte[] stream = [.. Cycle(), .. Cycle(), .. Module(13, "12____03", "........")];
        var whole = new CtsScoreboardDecoder();
        var byByte = new CtsScoreboardDecoder();

        var a = whole.Feed(stream).Concat(whole.Flush()).ToList();
        var b = stream.SelectMany(x => byByte.Feed([x])).ToList(); // primero todos los bytes, uno por uno
        b.AddRange(byByte.Flush());

        Assert.Equal(7, a.Count);
        Assert.Equal(a.Select(Describe), b.Select(Describe));
    }

    [Fact]
    public void Los_datos_sin_canal_se_descartan_y_se_cuentan()
    {
        var decoder = new CtsScoreboardDecoder();

        // Conexión a mitad de una trama: tres bytes de dato sin su dirección.
        var updates = decoder.Feed([0x0A, 0x1B, 0x2C, .. Module(1, "_____746", "......22"), .. Module(3, "____28__", null)]);

        Assert.Equal(3, decoder.DiscardedBytes);
        Assert.Equal(1, Assert.Single(updates).Channel);
    }

    [Fact]
    public void Un_formato_sin_su_lectura_se_recuerda_y_reiniciar_lo_olvida()
    {
        var decoder = new CtsScoreboardDecoder();
        decoder.Feed(FormatOnly(1, "......22"));

        var update = decoder.Feed([.. Module(1, "_____746", null), .. Module(3, "____28__", null)]).Single();
        decoder.Reset();
        var afterReset = decoder.Feed([.. Module(1, "_____746", null), .. Module(3, "____28__", null)]).Single();

        Assert.True(update.FormatsKnown);
        Assert.Equal(2, update.Formats[7]);
        Assert.False(afterReset.FormatsKnown);
    }

    [Theory]
    [InlineData("_____746", "......22", 466_000, TimingPrecision.Seconds)]     // 7:46
    [InlineData("____1203", "......22", 723_000, TimingPrecision.Seconds)]     // 12:03
    [InlineData("_____453", "......2.", 45_300, TimingPrecision.Tenths)]       // último minuto 45.3
    [InlineData("______98", "......2.", 9_800, TimingPrecision.Tenths)]        // 9.8
    [InlineData("______0_", "......2.", 0, TimingPrecision.Seconds)]           // fin del periodo
    public void El_reloj_de_partido_se_lee_con_la_hipotesis_de_las_marcas(string digits, string formats, int expectedMs, TimingPrecision precision)
    {
        var reading = Interpret(Module(1, digits, formats)).Single();

        Assert.Equal((expectedMs, (int?)null, precision), (reading.GameClockRemainingMs!.Value, reading.ShotClockRemainingMs, reading.Precision));
        Assert.Null(reading.IsRunning); // el flujo no informa la marcha: no se deduce
    }

    [Theory]
    [InlineData("_____746", null)]        // formato nunca recibido
    [InlineData("_____746", "........")]  // sin marcas: ambiguo
    [InlineData("_____775", "......22")]  // 75 segundos
    [InlineData("_1234567", "......22")]  // cinco dígitos de minutos
    [InlineData("________", "......2.")]  // posición marcada en blanco
    public void Una_lectura_ambigua_o_fuera_de_rango_del_partido_no_entrega_valor(string digits, string? formats)
    {
        Assert.Empty(Interpret(Module(1, digits, formats)));
    }

    [Theory]
    [InlineData("____28__", ".....2..", 28_000, TimingPrecision.Seconds)]
    [InlineData("_____5__", ".....2..", 5_000, TimingPrecision.Seconds)]
    [InlineData("____045_", ".....2..", 4_500, TimingPrecision.Tenths)]
    public void La_posesion_se_lee_con_una_sola_marca(string digits, string formats, int expectedMs, TimingPrecision precision)
    {
        var reading = Interpret(Module(3, digits, formats)).Single();

        Assert.Equal((expectedMs, (int?)null, precision), (reading.ShotClockRemainingMs!.Value, reading.GameClockRemainingMs, reading.Precision));
    }

    [Theory]
    [InlineData("____28__", "....22..")]  // dos marcas
    [InlineData("____28__", "........")]  // ninguna
    [InlineData("___128__", ".....2..")]  // 128 s, más que el máximo
    public void Una_posesion_ambigua_o_fuera_de_rango_no_entrega_valor(string digits, string formats)
    {
        Assert.Empty(Interpret(Module(3, digits, formats)));
    }

    [Fact]
    public void El_periodo_acompana_a_las_lecturas_siguientes_y_los_canales_se_pueden_cambiar()
    {
        IColoradoFrameDecoder standard = new CtsWaterPoloInterpreter();
        var custom = new CtsWaterPoloInterpreter(new CtsWaterPoloChannels(GameClock: 5, ShotClock: 6, Period: 7));

        var readings = standard.Feed([.. Module(14, "_3______", null), .. Module(1, "_____746", "......22"), .. Module(3, "____28__", ".....2.."), .. Module(9, "________", null)]);
        var ignored = custom.Feed([.. Module(1, "_____746", "......22"), .. Module(3, "____28__", ".....2..")]);
        var moved = custom.Feed([.. Module(5, "_____746", "......22"), .. Module(9, "________", null)]);

        Assert.Equal([3, 3], readings.Select(r => r.Period));
        Assert.Equal(466_000, readings[0].GameClockRemainingMs);
        Assert.Equal(28_000, readings[1].ShotClockRemainingMs);
        Assert.Empty(ignored);
        Assert.Equal(466_000, Assert.Single(moved).GameClockRemainingMs);
    }

    [Theory]
    [InlineData("", "vacío")]
    [InlineData("0,BC", "Línea 1: se esperaba «formato,fdnda-captura-cts,1»")]
    [InlineData("formato,fdnda-captura-cts,1\nBC", "Línea 2: falta la coma")]
    [InlineData("formato,fdnda-captura-cts,1\nx,BC", "Línea 2: el instante")]
    [InlineData("formato,fdnda-captura-cts,1\n10,BC\n5,BC", "Línea 3: el instante es menor")]
    [InlineData("formato,fdnda-captura-cts,1\n0,BZ", "Línea 2: los bytes deben estar en hexadecimal")]
    public void Una_captura_invalida_se_rechaza_con_la_linea(string text, string expected)
    {
        var ex = Assert.Throws<TimingRecordingException>(() => CtsCaptureFile.Parse(text, "c.txt"));
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Una_captura_se_convierte_en_una_grabacion_que_se_reproduce_con_la_reconexion_como_sesion_nueva()
    {
        var capture = CtsCaptureFile.Parse(SyntheticCapture(), "captura-sintetica.txt");

        var recording = CtsCaptureConverter.ToRecording(capture);
        var text = TimingRecordingWriter.Write(recording, ["Prueba: captura sintética."]);
        var reparsed = TimingRecordingParser.Parse(text, "grabacion.csv");

        Assert.Equal("captura-sintetica.csv", recording.Name);
        Assert.Equal(recording.Samples, reparsed.Samples);
        Assert.Equal(recording.Duration, reparsed.Duration);
        Assert.Equal([1, 2], reparsed.Samples.Select(s => s.Session).Distinct());
        Assert.Equal(480_000, reparsed.Samples.First(s => s.GameClockRemainingMs is not null).GameClockRemainingMs);
        Assert.Contains(reparsed.Samples, s => s.Session == 2 && s.GameClockRemainingMs == 470_000);
        Assert.All(reparsed.Samples, s => Assert.Equal(1, s.Period));

        var time = new ManualTimeProvider();
        await using var source = ReplayTimingSource.FromRecording(reparsed, time, TimeSpan.FromHours(1));
        var tracker = new TimingStateTracker(source.Kind, source.DisplayLabel, time, new TimingOptions());
        await source.StartAsync();
        time.AdvanceMs(1200);
        foreach (var snapshot in source.Pump())
        {
            tracker.Accept(snapshot);
        }

        // Cada canal se entrega al llegar el siguiente: en esta captura (un ciclo por bloque) la posesión
        // del bloque de 1 s sale con el bloque siguiente, así que a 1,2 s se ve la de 0,25 s.
        Assert.Equal((SignalState.Live, 479_000, 30_000), (tracker.Evaluate().Signal, tracker.Evaluate().GameClock.RemainingMs, tracker.Evaluate().ShotClock.RemainingMs));
    }

    [Fact]
    public void Una_captura_sin_los_canales_supuestos_lo_dice_y_el_analisis_muestra_lo_que_llega()
    {
        var capture = CtsCaptureFile.Parse(SyntheticCapture(), "captura-sintetica.txt");

        var ex = Assert.Throws<TimingRecordingException>(() =>
            CtsCaptureConverter.ToRecording(capture, new CtsWaterPoloChannels(GameClock: 20, ShotClock: 21, Period: 22)));
        var report = CtsCaptureAnalyzer.Report(capture);

        Assert.Contains("no produjo ninguna lectura de reloj", ex.Message, StringComparison.Ordinal);
        Assert.Contains("1 reconexiones", report, StringComparison.Ordinal);
        Assert.Contains("Canal 1 (0x01) ← partido (supuesto)", report, StringComparison.Ordinal);
        Assert.Contains("Canal 3 (0x03) ← posesión (supuesto)", report, StringComparison.Ordinal);
        Assert.Contains("_____800  formato 00000022", report, StringComparison.Ordinal);
        Assert.Contains("_____759  formato 00000022", report, StringComparison.Ordinal);
    }

    // ---------- Apoyo: bytes sintéticos ----------

    /// <summary>Lectura de un canal (8 dígitos, «_» = en blanco) y, si se indica, su formato («.» = 0, «2» = marca).</summary>
    private static byte[] Module(int channel, string digits, string? formats)
    {
        var bytes = new List<byte> { CtsScoreboardDecoder.AddressByte(channel, format: false) };
        for (var p = 0; p < 8; p++)
        {
            bytes.Add(CtsScoreboardDecoder.DataByte(p, digits[p] == '_' ? 15 : digits[p] - '0'));
        }

        if (formats is not null)
        {
            bytes.AddRange(FormatOnly(channel, formats));
        }

        return [.. bytes];
    }

    private static byte[] FormatOnly(int channel, string formats)
    {
        var bytes = new List<byte> { CtsScoreboardDecoder.AddressByte(channel, format: true) };
        for (var p = 0; p < 8; p++)
        {
            bytes.Add(CtsScoreboardDecoder.DataByte(p, formats[p] == '.' ? 0 : Convert.ToInt32(formats[p].ToString(), 16)));
        }

        return [.. bytes];
    }

    private static byte[] Cycle() =>
        [.. Module(1, "_____746", "......22"), .. Module(3, "____28__", ".....2.."), .. Module(14, "_1______", null)];

    private static string Describe(CtsModuleUpdate u) =>
        $"{u.Channel}:{string.Join(',', u.Digits)}|{string.Join(',', u.Formats)}|{u.FormatsKnown}";

    private static List<ColoradoReading> Interpret(byte[] module)
    {
        var interpreter = new CtsWaterPoloInterpreter();
        return [.. interpreter.Feed(module), .. interpreter.Flush()];
    }

    /// <summary>Captura SINTÉTICA: 8:00 / 30 detenido, luego en marcha; corte; reconexión en 7:50.</summary>
    private static string SyntheticCapture()
    {
        var text = new System.Text.StringBuilder(CtsCaptureFile.Header(["Captura SINTÉTICA para pruebas; no viene de la consola."]));
        void Add(int ms, int game, int shot)
        {
            var g = $"_____{game / 60}{game % 60:00}";
            var s = $"____{shot:00}__";
            text.Append(CtsCaptureFile.Line(TimeSpan.FromMilliseconds(ms),
                [.. Module(14, "_1______", null), .. Module(1, g, "......22"), .. Module(3, s, ".....2..")]));
        }

        Add(0, 480, 30);
        Add(250, 480, 30);
        Add(1000, 479, 29);
        Add(2000, 478, 28);
        text.Append(CtsCaptureFile.ReconnectionLine(TimeSpan.FromMilliseconds(6000)));
        Add(6100, 470, 20);
        Add(6400, 470, 20);
        return text.ToString();
    }
}
