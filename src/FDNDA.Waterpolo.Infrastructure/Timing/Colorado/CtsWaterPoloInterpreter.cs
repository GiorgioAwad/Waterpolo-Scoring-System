using FDNDA.Waterpolo.Application.Timing;

namespace FDNDA.Waterpolo.Infrastructure.Timing.Colorado;

/// <summary>
/// Canales del tablero que llevan cada dato de waterpolo. Los valores por defecto son los que usa
/// scorebox-consoles (MIT) para la System 6 con waterpolo. **Hipótesis**: el manual F872 dice que la
/// asignación de canales a módulos se puede cambiar en la consola («Define Module»), así que hay que
/// confirmarlos con una captura de la consola de la FDNDA (herramienta de captura, comando «analizar»).
/// </summary>
public sealed record CtsWaterPoloChannels(int GameClock = 1, int ShotClock = 3, int Period = 14)
{
    /// <summary>Posesión máxima aceptada; lo que exceda se descarta como lectura dudosa.</summary>
    public int MaxShotClockSeconds { get; init; } = 120;
}

/// <summary>
/// Convierte el flujo de tablero CTS en lecturas de reloj de waterpolo. Implementa
/// <see cref="IColoradoFrameDecoder"/>; no tiene transporte ni escribe nada.
/// </summary>
/// <remarks>
/// Interpretación de los dígitos (hipótesis de scorebox-consoles, sin validar): el código de formato 2
/// marca posiciones de la puntuación. En el reloj de partido, con dos o más marcas los dígitos antes de
/// la primera son minutos y los marcados son segundos (m:ss); con una marca, los dígitos hasta la marca
/// son segundos y el siguiente, décimas (último minuto, ss.d). En la posesión debe haber exactamente
/// una marca: los dígitos hasta ella son segundos y el siguiente, décimas. Ante cualquier ambigüedad
/// (sin formato conocido, sin marcas, valores fuera de rango) **no se entrega valor**: el programa
/// muestra «sin señal» antes que un tiempo dudoso. El estado de marcha no se deduce (sin evidencia).
/// </remarks>
public sealed class CtsWaterPoloInterpreter(CtsWaterPoloChannels? channels = null) : IColoradoFrameDecoder
{
    public const int FormatMarker = 2;

    private readonly CtsScoreboardDecoder _decoder = new();
    private int? _period;

    public CtsWaterPoloChannels Channels { get; } = channels ?? new CtsWaterPoloChannels();

    public CtsScoreboardDecoder Decoder => _decoder;

    public IReadOnlyList<ColoradoReading> Feed(ReadOnlySpan<byte> bytes) => Interpret(_decoder.Feed(bytes));

    /// <summary>Entrega lo que quedó pendiente al final de una captura.</summary>
    public IReadOnlyList<ColoradoReading> Flush() => Interpret(_decoder.Flush());

    public void Reset()
    {
        _decoder.Reset();
        _period = null;
    }

    /// <summary>Tiempo de partido en milisegundos, o nulo si la lectura no es inequívoca.</summary>
    public static (int Ms, TimingPrecision Precision)? ReadGameClock(CtsModuleUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var markers = Markers(update);
        if (markers is null || markers.Count == 0)
        {
            return null;
        }

        if (markers.Count >= 2)
        {
            var minutes = Concat(update.Digits, 0, markers[0]);
            var seconds = string.Concat(markers.Select(p => update.Digits[p]?.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            if (minutes.Length > 2 || seconds.Length is < 1 or > 2)
            {
                return null;
            }

            var m = minutes.Length == 0 ? 0 : int.Parse(minutes, System.Globalization.CultureInfo.InvariantCulture);
            var s = int.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture);
            return s < 60 && m <= 60 ? (((m * 60) + s) * 1000, TimingPrecision.Seconds) : null;
        }

        return SecondsAndTenths(update, markers[0], maxSeconds: 59);
    }

    /// <summary>Tiempo de posesión en milisegundos, o nulo si la lectura no es inequívoca.</summary>
    public (int Ms, TimingPrecision Precision)? ReadShotClock(CtsModuleUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var markers = Markers(update);
        return markers is { Count: 1 } ? SecondsAndTenths(update, markers[0], Channels.MaxShotClockSeconds) : null;
    }

    /// <summary>Periodo: segundo dígito del canal de periodo (1–20), como en scorebox-consoles.</summary>
    public static int? ReadPeriod(CtsModuleUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return update.Digits[1] is { } p and >= 1 ? p : null;
    }

    private List<ColoradoReading> Interpret(IReadOnlyList<CtsModuleUpdate> updates)
    {
        var readings = new List<ColoradoReading>();
        foreach (var update in updates)
        {
            if (update.Channel == Channels.Period)
            {
                _period = ReadPeriod(update) ?? _period;
            }

            if (update.Channel == Channels.GameClock && ReadGameClock(update) is { } game)
            {
                readings.Add(new ColoradoReading(game.Ms, null, game.Precision, IsRunning: null, _period));
            }

            if (update.Channel == Channels.ShotClock && ReadShotClock(update) is { } shot)
            {
                readings.Add(new ColoradoReading(null, shot.Ms, shot.Precision, IsRunning: null, _period));
            }
        }

        return readings;
    }

    private static List<int>? Markers(CtsModuleUpdate update) =>
        update.FormatsKnown
            ? [.. Enumerable.Range(0, CtsScoreboardDecoder.Positions).Where(p => update.Formats[p] == FormatMarker)]
            : null;

    private static (int Ms, TimingPrecision Precision)? SecondsAndTenths(CtsModuleUpdate update, int marker, int maxSeconds)
    {
        if (update.Digits[marker] is null)
        {
            return null; // la posición marcada debe tener un dígito
        }

        var seconds = Concat(update.Digits, 0, marker + 1);
        if (seconds.Length is < 1 or > 3)
        {
            return null;
        }

        var s = int.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture);
        if (s > maxSeconds)
        {
            return null;
        }

        var tenths = Enumerable.Range(marker + 1, CtsScoreboardDecoder.Positions - marker - 1)
            .Select(p => update.Digits[p])
            .FirstOrDefault(d => d is not null);
        return tenths is { } t ? ((s * 1000) + (t * 100), TimingPrecision.Tenths) : (s * 1000, TimingPrecision.Seconds);
    }

    /// <summary>Dígitos encendidos de las posiciones [desde, hasta), en orden; los apagados no cuentan.</summary>
    private static string Concat(IReadOnlyList<int?> digits, int from, int to) =>
        string.Concat(Enumerable.Range(from, Math.Max(0, to - from))
            .Select(p => digits[p]?.ToString(System.Globalization.CultureInfo.InvariantCulture)));
}
