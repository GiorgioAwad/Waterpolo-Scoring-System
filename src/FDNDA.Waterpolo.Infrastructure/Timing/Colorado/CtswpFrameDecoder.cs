using System.Text;
using FDNDA.Waterpolo.Application.Timing;

namespace FDNDA.Waterpolo.Infrastructure.Timing.Colorado;

/// <summary>
/// Decodificador incremental de las tramas <c>CTSWP1.1c</c> que la System 6 de waterpolo emite por
/// COM PORT 2 (115200 baudios, 8-N-1). Formato observado con la consola de la FDNDA el 2026-11-24
/// (evidencia propia, no documentación oficial; ver docs/colorado-system6.md §7):
/// <c>STX</c> + 97 bytes + <c>EOT</c>; tiempo de partido en 10–14 (<c>m:ss</c> o <c>:ss</c>), posesión
/// en 18–19, periodo en 20 y reloj de tiempo muerto en 21–24 (en blanco sin tiempo muerto). Solo se
/// usan los relojes y el periodo: goles, tiempos muertos y exclusiones los registra la mesa, no se
/// toman de la consola.
/// </summary>
/// <remarks>
/// La consola manda segundos enteros (redondeados hacia arriba respecto de su pantalla). Ante cualquier
/// duda la trama entera se descarta y se cuenta: el programa muestra «sin señal» antes que un tiempo
/// dudoso. Un STX dentro de una trama la corta (consola apagada a mitad) y empieza otra.
/// </remarks>
public sealed class CtswpFrameDecoder : IColoradoFrameDecoder
{
    public const string Header = "CTSWP1.1c";
    public const int BodyLength = 97;
    public const byte Stx = 0x02;
    public const byte Eot = 0x04;

    private static readonly byte[] HeaderBytes = Encoding.ASCII.GetBytes(Header);

    private readonly List<byte> _body = new(BodyLength + 1);
    private bool _inFrame;

    /// <summary>Tramas completas y válidas.</summary>
    public long ValidFrames { get; private set; }

    /// <summary>Tramas descartadas: cortadas, de otra longitud, de otra versión o con campos inválidos.</summary>
    public long InvalidFrames { get; private set; }

    /// <summary>Bytes recibidos fuera de cualquier trama.</summary>
    public long DiscardedBytes { get; private set; }

    public IReadOnlyList<ColoradoReading> Feed(ReadOnlySpan<byte> bytes)
    {
        var readings = new List<ColoradoReading>();
        foreach (var b in bytes)
        {
            if (b == Stx)
            {
                if (_inFrame)
                {
                    InvalidFrames++; // trama cortada por el inicio de otra
                }

                _inFrame = true;
                _body.Clear();
                continue;
            }

            if (!_inFrame)
            {
                DiscardedBytes++;
                continue;
            }

            if (b == Eot)
            {
                _inFrame = false;
                if (Parse(_body) is { } reading)
                {
                    ValidFrames++;
                    readings.Add(reading);
                }
                else
                {
                    InvalidFrames++;
                }

                continue;
            }

            if (_body.Count > BodyLength)
            {
                _inFrame = false; // sin EOT: no se acumula sin límite
                InvalidFrames++;
                DiscardedBytes++;
                continue;
            }

            _body.Add(b);
        }

        return readings;
    }

    public void Reset()
    {
        _inFrame = false;
        _body.Clear();
    }

    private static ColoradoReading? Parse(List<byte> body)
    {
        if (body.Count != BodyLength
            || !HeaderBytes.AsSpan().SequenceEqual(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(body)[..HeaderBytes.Length])
            || body[9] != 0x00
            || body[95] != 0xFF
            || body[96] != 0xFF)
        {
            return null;
        }

        var game = Text(body, 10, 5);
        var shot = Text(body, 18, 2);
        var period = Text(body, 20, 1);
        var timeout = Text(body, 21, 4);

        if (!TryGameClock(game, out var gameMs) || !TrySeconds(shot, out var shotMs) || !TryPeriod(period, out var p)
            || !TryGameClock(timeout, out var timeoutMs))
        {
            return null;
        }

        return gameMs is null && shotMs is null
            ? null
            : new ColoradoReading(gameMs, shotMs, TimingPrecision.Seconds, IsRunning: null, p, timeoutMs);
    }

    private static string Text(List<byte> body, int start, int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = (char)body[start + i];
        }

        return new string(chars);
    }

    /// <summary><c>m:ss</c>, <c>mm:ss</c> o <c>:ss</c> alineado a la derecha; en blanco = sin dato.</summary>
    private static bool TryGameClock(string field, out int? ms)
    {
        ms = null;
        var text = field.TrimStart(' ');
        if (text.Length == 0)
        {
            return true;
        }

        var colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0 || colon > 2 || text.Length - colon - 1 != 2)
        {
            return false;
        }

        var minutesText = text[..colon];
        var secondsText = text[(colon + 1)..];
        if (!AllDigits(minutesText) || !AllDigits(secondsText))
        {
            return false;
        }

        var minutes = minutesText.Length == 0 ? 0 : Digits(minutesText);
        var seconds = Digits(secondsText);
        if (seconds >= 60)
        {
            return false;
        }

        ms = ((minutes * 60) + seconds) * 1000;
        return true;
    }

    /// <summary>Uno o dos dígitos alineados a la derecha; en blanco = sin dato.</summary>
    private static bool TrySeconds(string field, out int? ms)
    {
        ms = null;
        var text = field.TrimStart(' ');
        if (text.Length == 0)
        {
            return true;
        }

        if (!AllDigits(text))
        {
            return false;
        }

        ms = Digits(text) * 1000;
        return true;
    }

    private static bool TryPeriod(string field, out int? period)
    {
        period = null;
        if (field == " ")
        {
            return true;
        }

        if (field[0] is < '1' or > '9')
        {
            return false;
        }

        period = field[0] - '0';
        return true;
    }

    private static bool AllDigits(string text) => text.All(c => c is >= '0' and <= '9');

    private static int Digits(string text) => text.Aggregate(0, (n, c) => (n * 10) + (c - '0'));
}
