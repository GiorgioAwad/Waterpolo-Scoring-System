using System.Globalization;

namespace FDNDA.Waterpolo.Application.Timing;

/// <summary>
/// Formato de los relojes para ambas ventanas. Un reloj no vigente se muestra como
/// «--:--» (partido) o «--» (posesión). El redondeo es provisional hasta validar cómo
/// lo presenta la consola real.
/// </summary>
public static class ClockFormatter
{
    public const string GameClockUnavailable = "--:--";
    public const string ShotClockUnavailable = "--";

    /// <summary>Reloj de partido: <c>m:ss</c>; bajo un minuto, <c>ss.d</c> si hay décimas.</summary>
    public static string FormatGameClock(ClockReading reading, TimingPrecision precision)
    {
        if (reading.RemainingMs is not { } ms)
        {
            return GameClockUnavailable;
        }

        var showTenths = ms < 60_000 && precision is TimingPrecision.Tenths or TimingPrecision.Hundredths or TimingPrecision.Milliseconds;
        if (showTenths)
        {
            var tenths = ms / 100;
            return string.Create(CultureInfo.InvariantCulture, $"{tenths / 10}.{tenths % 10}");
        }

        var totalSeconds = ms / 1000;
        return string.Create(CultureInfo.InvariantCulture, $"{totalSeconds / 60}:{totalSeconds % 60:00}");
    }

    /// <summary>Reloj de posesión en segundos enteros, redondeando hacia arriba.</summary>
    public static string FormatShotClock(ClockReading reading)
    {
        if (reading.RemainingMs is not { } ms)
        {
            return ShotClockUnavailable;
        }

        var seconds = (ms + 999) / 1000;
        return seconds.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reloj de tiempo muerto en segundos enteros hacia arriba: <c>m:ss</c> desde un minuto, si no solo
    /// los segundos. Sin lectura vigente, «--».
    /// </summary>
    public static string FormatTimeoutClock(ClockReading reading)
    {
        if (reading.RemainingMs is not { } ms)
        {
            return ShotClockUnavailable;
        }

        var seconds = (ms + 999) / 1000;
        return seconds >= 60
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}")
            : seconds.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Texto del contexto de reloj de una incidencia para el historial.</summary>
    public static string FormatIncidentClock(int? gameClockRemainingMs)
    {
        if (gameClockRemainingMs is not { } ms)
        {
            return "sin reloj";
        }

        var totalSeconds = ms / 1000;
        return string.Create(CultureInfo.InvariantCulture, $"{totalSeconds / 60}:{totalSeconds % 60:00}");
    }
}
