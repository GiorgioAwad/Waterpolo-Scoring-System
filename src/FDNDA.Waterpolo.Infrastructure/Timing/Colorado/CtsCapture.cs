using System.Globalization;
using System.Text;
using FDNDA.Waterpolo.Application.Timing;

namespace FDNDA.Waterpolo.Infrastructure.Timing.Colorado;

// Captura cruda de la salida de tablero, en texto para poder revisarla y compararla a mano:
//
//   # comentarios (puerto, velocidad, fecha, quién autorizó…)
//   formato,fdnda-captura-cts,1
//   0,BC7F6E5D...                  t_ms desde el comienzo, bytes recibidos en hexadecimal
//   12,A3...
//   reconexion,5400                el puerto se volvió a abrir: lo que sigue es otra sesión
//
// La escribe la herramienta tools/FDNDA.Waterpolo.Captura, que solo lee el puerto. Las pruebas usan
// capturas sintéticas armadas con CtsScoreboardDecoder.AddressByte/DataByte.

/// <summary>Bloque de bytes recibido en un instante, o una marca de reconexión (sin bytes).</summary>
public sealed record CaptureChunk(TimeSpan At, byte[] Bytes, bool IsReconnection);

public sealed record CtsCapture(string Name, IReadOnlyList<CaptureChunk> Chunks)
{
    public TimeSpan Duration => Chunks.Count == 0 ? TimeSpan.Zero : Chunks[^1].At;

    public long ByteCount => Chunks.Sum(c => (long)c.Bytes.Length);
}

/// <summary>Lee y escribe capturas. Rechaza un archivo con errores indicando la línea.</summary>
public static class CtsCaptureFile
{
    public const string FormatLine = "formato,fdnda-captura-cts,1";

    public static string Header(IEnumerable<string> comments) =>
        string.Concat(comments.Select(c => "# " + c.Replace('\n', ' ').Replace('\r', ' ') + "\n")) + FormatLine + "\n";

    public static string Line(TimeSpan at, ReadOnlySpan<byte> bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"{(long)at.TotalMilliseconds},{Convert.ToHexString(bytes)}\n");

    public static string ReconnectionLine(TimeSpan at) =>
        string.Create(CultureInfo.InvariantCulture, $"reconexion,{(long)at.TotalMilliseconds}\n");

    public static CtsCapture Parse(string text, string name)
    {
        ArgumentNullException.ThrowIfNull(text);
        var chunks = new List<CaptureChunk>();
        var formatSeen = false;
        var lineNumber = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = raw.TrimEnd('\r').Trim().TrimStart('﻿');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (!formatSeen)
            {
                if (line != FormatLine)
                {
                    throw new TimingRecordingException($"Línea {lineNumber}: se esperaba «{FormatLine}». No es una captura de este programa.");
                }

                formatSeen = true;
                continue;
            }

            var comma = line.IndexOf(',', StringComparison.Ordinal);
            if (comma < 0)
            {
                throw new TimingRecordingException($"Línea {lineNumber}: falta la coma entre el instante y los bytes.");
            }

            var isReconnection = line.StartsWith("reconexion,", StringComparison.Ordinal);
            var timeText = isReconnection ? line[(comma + 1)..] : line[..comma];
            if (!long.TryParse(timeText, NumberStyles.None, CultureInfo.InvariantCulture, out var ms) || ms > TimingRecordingParser.MaxTimeMs)
            {
                throw new TimingRecordingException($"Línea {lineNumber}: el instante debe ser un entero de milisegundos.");
            }

            var at = TimeSpan.FromMilliseconds(ms);
            if (chunks.Count > 0 && at < chunks[^1].At)
            {
                throw new TimingRecordingException($"Línea {lineNumber}: el instante es menor que el de la línea anterior.");
            }

            if (isReconnection)
            {
                chunks.Add(new CaptureChunk(at, [], IsReconnection: true));
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromHexString(line[(comma + 1)..]);
            }
            catch (FormatException)
            {
                throw new TimingRecordingException($"Línea {lineNumber}: los bytes deben estar en hexadecimal (pares de 0-9 y A-F).");
            }

            chunks.Add(new CaptureChunk(at, bytes, IsReconnection: false));
        }

        if (!formatSeen)
        {
            throw new TimingRecordingException($"El archivo está vacío o no tiene la línea «{FormatLine}».");
        }

        return new CtsCapture(name, chunks);
    }

    public static CtsCapture Load(string path)
    {
        var name = Path.GetFileName(path);
        if (!File.Exists(path))
        {
            throw new TimingRecordingException($"No existe el archivo «{name}».");
        }

        return Parse(File.ReadAllText(path, Encoding.UTF8), name);
    }
}

/// <summary>Convierte una captura en una grabación normalizada que reproduce <see cref="ReplayTimingSource"/>.</summary>
public static class CtsCaptureConverter
{
    public static TimingRecording ToRecording(CtsCapture capture, CtsWaterPoloChannels? channels = null)
    {
        ArgumentNullException.ThrowIfNull(capture);
        var interpreter = new CtsWaterPoloInterpreter(channels);
        var samples = new List<RecordedSample>();
        var session = 1;
        foreach (var chunk in capture.Chunks)
        {
            if (chunk.IsReconnection)
            {
                Add(samples, interpreter.Flush(), chunk.At, session);
                interpreter.Reset();
                session++;
                continue;
            }

            Add(samples, interpreter.Feed(chunk.Bytes), chunk.At, session);
        }

        Add(samples, interpreter.Flush(), capture.Duration, session);
        if (samples.Count == 0)
        {
            throw new TimingRecordingException(
                "La captura no produjo ninguna lectura de reloj con los canales indicados. Revise con «analizar» qué canales llegan.");
        }

        var name = Path.ChangeExtension(capture.Name, ".csv");
        return new TimingRecording(name, samples, capture.Duration > samples[^1].At ? capture.Duration : samples[^1].At);
    }

    /// <summary>Verdadero si la captura trae tramas <c>CTSWP</c> (COM PORT 2) y no el flujo de tablero.</summary>
    public static bool IsCtswp(CtsCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        var bytes = capture.Chunks.SelectMany(c => c.Bytes).Take(4096).ToArray();
        return bytes.AsSpan().IndexOf("CTSWP"u8) >= 0;
    }

    /// <summary>Convierte una captura de COM PORT 2 (tramas <c>CTSWP1.1c</c>); cada reconexión es una sesión nueva.</summary>
    public static TimingRecording FromCtswp(CtsCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        var decoder = new CtswpFrameDecoder();
        var samples = new List<RecordedSample>();
        var session = 1;
        foreach (var chunk in capture.Chunks)
        {
            if (chunk.IsReconnection)
            {
                decoder.Reset();
                session++;
                continue;
            }

            Add(samples, decoder.Feed(chunk.Bytes), chunk.At, session);
        }

        if (samples.Count == 0)
        {
            throw new TimingRecordingException("La captura no tiene ninguna trama CTSWP válida.");
        }

        var name = Path.ChangeExtension(capture.Name, ".csv");
        return new TimingRecording(name, samples, capture.Duration > samples[^1].At ? capture.Duration : samples[^1].At);
    }

    private static void Add(List<RecordedSample> samples, IReadOnlyList<ColoradoReading> readings, TimeSpan at, int session)
    {
        foreach (var r in readings)
        {
            samples.Add(new RecordedSample(at, session, r.GameClockRemainingMs, r.ShotClockRemainingMs, r.IsRunning, r.Period, r.Precision));
        }
    }
}

/// <summary>Resumen por canal de una captura, para identificar qué canal lleva cada reloj en la consola real.</summary>
public static class CtsCaptureAnalyzer
{
    public static string Report(CtsCapture capture, CtsWaterPoloChannels? channels = null, int samplesPerChannel = 6)
    {
        ArgumentNullException.ThrowIfNull(capture);
        var map = channels ?? new CtsWaterPoloChannels();
        var decoder = new CtsScoreboardDecoder();
        var seen = new SortedDictionary<int, ChannelStats>();
        foreach (var chunk in capture.Chunks)
        {
            if (chunk.IsReconnection)
            {
                decoder.Reset();
                continue;
            }

            foreach (var update in decoder.Feed(chunk.Bytes))
            {
                if (!seen.TryGetValue(update.Channel, out var stats))
                {
                    stats = new ChannelStats(chunk.At);
                    seen[update.Channel] = stats;
                }

                stats.Add(chunk.At, update, samplesPerChannel);
            }
        }

        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"Captura «{capture.Name}»: {Seconds(capture.Duration)}, {capture.ByteCount} bytes, " +
            $"{capture.Chunks.Count(c => c.IsReconnection)} reconexiones.");
        report.AppendLine(CultureInfo.InvariantCulture, $"Canales completos: {decoder.Updates}; bytes de dato sin canal descartados: {decoder.DiscardedBytes}.");
        report.AppendLine("Dígitos: posiciones 0 a 7, «_» = en blanco. Formato: código por posición, «.» = sin código.");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"Supuestos actuales (scorebox-consoles): partido = canal {map.GameClock}, posesión = canal {map.ShotClock}, periodo = canal {map.Period}.");
        foreach (var (channel, stats) in seen)
        {
            var role = channel == map.GameClock ? " ← partido (supuesto)"
                : channel == map.ShotClock ? " ← posesión (supuesto)"
                : channel == map.Period ? " ← periodo (supuesto)" : string.Empty;
            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture,
                $"Canal {channel} (0x{channel:X2}){role}: {stats.Count} actualizaciones entre {Seconds(stats.First)} y {Seconds(stats.Last)}.");
            foreach (var sample in stats.Samples)
            {
                report.AppendLine("  " + sample);
            }
        }

        return report.ToString();
    }

    private static string Seconds(TimeSpan t) => string.Create(CultureInfo.InvariantCulture, $"{t.TotalSeconds:0.0} s");

    private sealed class ChannelStats(TimeSpan first)
    {
        private string? _last;

        public int Count { get; private set; }

        public TimeSpan First { get; } = first;

        public TimeSpan Last { get; private set; } = first;

        public List<string> Samples { get; } = [];

        /// <summary>Guarda las primeras lecturas distintas, para ver cómo cambian los dígitos.</summary>
        public void Add(TimeSpan at, CtsModuleUpdate update, int max)
        {
            Count++;
            Last = at;
            var digits = string.Concat(update.Digits.Select(d => d is { } v ? (char)('0' + v) : '_'));
            var formats = update.FormatsKnown
                ? string.Concat(update.Formats.Select(f => f is { } v ? v.ToString("X", CultureInfo.InvariantCulture) : "."))
                : "(sin formato)";
            var text = $"{digits}  formato {formats}";
            if (text != _last && Samples.Count < max)
            {
                Samples.Add(string.Create(CultureInfo.InvariantCulture, $"{Seconds(at),8}  {text}"));
            }

            _last = text;
        }
    }
}
