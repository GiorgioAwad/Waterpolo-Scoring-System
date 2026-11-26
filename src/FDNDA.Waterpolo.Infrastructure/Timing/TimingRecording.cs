using System.Globalization;
using FDNDA.Waterpolo.Application.Timing;

namespace FDNDA.Waterpolo.Infrastructure.Timing;

// Formato normalizado de cronometraje (R9). Es un formato PROPIO del programa para guardar y
// reproducir lecturas ya decodificadas; NO es el protocolo del Colorado System 6, que sigue sin
// evidencia (docs/colorado-system6.md). Las capturas autorizadas de la consola se convertirán a
// este formato cuando exista el decodificador (hito B).
//
//   # comentario (se ignora, igual que las líneas vacías)
//   formato,fdnda-cronometraje-normalizado,1
//   t_ms,sesion,partido_ms,posesion_ms,en_marcha,periodo,precision
//   0,1,480000,30000,0,1,decimas
//   250,1,479750,29750,1,1,decimas
//   ...
//   fin,40000                       (opcional: silencio final hasta ese instante)
//
// t_ms: instante de la muestra desde el comienzo, no decreciente. sesion: número de conexión
// (1, 2…), no decreciente; un cambio equivale a una reconexión. Un hueco sin filas es un corte de
// señal. partido_ms y posesion_ms: tiempo restante; vacío = la muestra no trae ese reloj (no es
// cero). en_marcha: 1, 0 o vacío (sin evidencia). periodo: vacío o 1–20. precision: segundos,
// decimas, centesimas, milisegundos o desconocida.

/// <summary>Una lectura normalizada de la grabación.</summary>
public sealed record RecordedSample(
    TimeSpan At,
    int Session,
    int? GameClockRemainingMs,
    int? ShotClockRemainingMs,
    bool? IsRunning,
    int? Period,
    TimingPrecision Precision);

/// <summary>Grabación normalizada, ya validada.</summary>
public sealed record TimingRecording(string Name, IReadOnlyList<RecordedSample> Samples, TimeSpan Duration);

/// <summary>Archivo de grabación inválido. El mensaje indica la línea y el motivo, en español.</summary>
public sealed class TimingRecordingException(string message) : Exception(message);

/// <summary>Escribe una grabación en el formato normalizado (lo lee <see cref="TimingRecordingParser"/>).</summary>
public static class TimingRecordingWriter
{
    public static string Write(TimingRecording recording, IEnumerable<string>? comments = null)
    {
        ArgumentNullException.ThrowIfNull(recording);
        var text = new System.Text.StringBuilder();
        foreach (var comment in comments ?? [])
        {
            text.Append("# ").Append(comment.Replace('\n', ' ').Replace('\r', ' ')).Append('\n');
        }

        text.Append(TimingRecordingParser.FormatLine).Append('\n').Append(TimingRecordingParser.Header).Append('\n');
        foreach (var s in recording.Samples)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"{(long)s.At.TotalMilliseconds},{s.Session},{s.GameClockRemainingMs},{s.ShotClockRemainingMs},{Running(s.IsRunning)},{s.Period},{Precision(s.Precision)}\n");
        }

        if (recording.Samples.Count > 0 && recording.Duration > recording.Samples[^1].At)
        {
            text.Append(CultureInfo.InvariantCulture, $"fin,{(long)recording.Duration.TotalMilliseconds}\n");
        }

        return text.ToString();
    }

    private static string Running(bool? running) => running switch
    {
        true => "1",
        false => "0",
        null => string.Empty,
    };

    private static string Precision(TimingPrecision precision) => precision switch
    {
        TimingPrecision.Seconds => "segundos",
        TimingPrecision.Tenths => "decimas",
        TimingPrecision.Hundredths => "centesimas",
        TimingPrecision.Milliseconds => "milisegundos",
        _ => "desconocida",
    };
}

/// <summary>Lee y valida grabaciones en el formato normalizado. Rechaza el archivo completo ante cualquier error.</summary>
public static class TimingRecordingParser
{
    public const string FormatLine = "formato,fdnda-cronometraje-normalizado,1";

    public const string Header = "t_ms,sesion,partido_ms,posesion_ms,en_marcha,periodo,precision";

    /// <summary>Tamaño máximo del archivo: una grabación de un partido completo cabe con holgura.</summary>
    public const long MaxBytes = 8 * 1024 * 1024;

    /// <summary>Valor máximo de un reloj (2 h), igual que el límite de <see cref="TimingOptions.MaxClockMs"/>.</summary>
    public const int MaxClockMs = 2 * 60 * 60 * 1000;

    /// <summary>Instante máximo de una muestra (6 h).</summary>
    public const long MaxTimeMs = 6L * 60 * 60 * 1000;

    public static TimingRecording Parse(string text, string name)
    {
        ArgumentNullException.ThrowIfNull(text);
        var samples = new List<RecordedSample>();
        var state = 0; // 0: espera formato; 1: espera encabezado; 2: muestras; 3: tras «fin»
        TimeSpan? end = null;
        var lineNumber = 0;

        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;
            var line = rawLine.TrimEnd('\r').Trim();
            if (lineNumber == 1)
            {
                line = line.TrimStart('﻿');
            }

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            switch (state)
            {
                case 0:
                    if (!line.Equals(FormatLine, StringComparison.Ordinal))
                    {
                        throw Error(lineNumber, $"se esperaba «{FormatLine}». No es una grabación normalizada de este programa.");
                    }

                    state = 1;
                    break;
                case 1:
                    if (!line.Equals(Header, StringComparison.Ordinal))
                    {
                        throw Error(lineNumber, $"se esperaba el encabezado «{Header}».");
                    }

                    state = 2;
                    break;
                case 2 when line.StartsWith("fin,", StringComparison.Ordinal):
                    var finMs = ParseTime(line[4..], lineNumber);
                    if (samples.Count > 0 && finMs < samples[^1].At)
                    {
                        throw Error(lineNumber, "«fin» no puede ser anterior a la última muestra.");
                    }

                    end = finMs;
                    state = 3;
                    break;
                case 2:
                    samples.Add(ParseSample(line, lineNumber, samples.Count > 0 ? samples[^1] : null));
                    break;
                default:
                    throw Error(lineNumber, "no puede haber datos después de «fin».");
            }
        }

        if (state < 2)
        {
            throw new TimingRecordingException(state == 0
                ? $"El archivo está vacío o no tiene la línea «{FormatLine}»."
                : $"Falta el encabezado «{Header}».");
        }

        if (samples.Count == 0)
        {
            throw new TimingRecordingException("La grabación no tiene ninguna muestra.");
        }

        return new TimingRecording(name, samples, end ?? samples[^1].At);
    }

    private static RecordedSample ParseSample(string line, int lineNumber, RecordedSample? previous)
    {
        var fields = line.Split(',');
        if (fields.Length != 7)
        {
            throw Error(lineNumber, $"tiene {fields.Length} columnas y se esperaban 7.");
        }

        var at = ParseTime(fields[0], lineNumber);
        if (previous is not null && at < previous.At)
        {
            throw Error(lineNumber, "t_ms es menor que el de la muestra anterior; las muestras deben estar en orden.");
        }

        if (!TryWhole(fields[1], out var session) || session < 1)
        {
            throw Error(lineNumber, "sesion debe ser un número entero desde 1.");
        }

        if (previous is not null && session < previous.Session)
        {
            throw Error(lineNumber, "sesion no puede volver a un número anterior.");
        }

        var game = ParseClock(fields[2], "partido_ms", lineNumber);
        var shot = ParseClock(fields[3], "posesion_ms", lineNumber);
        if (game is null && shot is null)
        {
            throw Error(lineNumber, "la muestra no trae ningún reloj (partido_ms y posesion_ms vacíos).");
        }

        bool? running = fields[4].Trim() switch
        {
            "" => null,
            "1" => true,
            "0" => false,
            var other => throw Error(lineNumber, $"en_marcha debe ser 1, 0 o vacío, no «{other}»."),
        };

        int? period = null;
        if (fields[5].Trim().Length > 0)
        {
            if (!TryWhole(fields[5], out var p) || p is < 1 or > 20)
            {
                throw Error(lineNumber, "periodo debe estar vacío o entre 1 y 20.");
            }

            period = p;
        }

        var precision = fields[6].Trim() switch
        {
            "segundos" => TimingPrecision.Seconds,
            "decimas" => TimingPrecision.Tenths,
            "centesimas" => TimingPrecision.Hundredths,
            "milisegundos" => TimingPrecision.Milliseconds,
            "desconocida" => TimingPrecision.Unknown,
            var other => throw Error(lineNumber, $"precision «{other}» no es válida (segundos, decimas, centesimas, milisegundos o desconocida)."),
        };

        return new RecordedSample(at, session, game, shot, running, period, precision);
    }

    private static TimeSpan ParseTime(string text, int lineNumber)
    {
        if (!long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var ms) || ms > MaxTimeMs)
        {
            throw Error(lineNumber, "t_ms debe ser un número entero de milisegundos entre 0 y 21600000 (6 h).");
        }

        return TimeSpan.FromMilliseconds(ms);
    }

    private static int? ParseClock(string text, string column, int lineNumber)
    {
        if (text.Trim().Length == 0)
        {
            return null;
        }

        if (!TryWhole(text, out var value) || value > MaxClockMs)
        {
            throw Error(lineNumber, $"{column} debe estar vacío o ser un entero de milisegundos entre 0 y {MaxClockMs}.");
        }

        return value;
    }

    private static bool TryWhole(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static TimingRecordingException Error(int lineNumber, string message) =>
        new($"Línea {lineNumber}: {message}");
}
