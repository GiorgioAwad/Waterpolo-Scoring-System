using System.Globalization;
using FDNDA.Waterpolo.Domain.Competitions;

namespace FDNDA.Waterpolo.Desktop.Presentation;

/// <summary>Textos de las reglas de un evento o partido para la mesa.</summary>
internal static class RulesTexts
{
    /// <summary>Duración como minutos y segundos, por ejemplo <c>8:00</c> o <c>7:30</c>.</summary>
    public static string Duration(int seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");

    /// <summary>
    /// Lee una duración escrita como <c>m:ss</c> o como minutos enteros (<c>8</c> = 8:00).
    /// Devuelve falso si el texto no es una duración.
    /// </summary>
    public static bool TryParseDuration(string? text, out int seconds)
    {
        seconds = 0;
        var value = text?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return false;
        }

        var parts = value.Split(':');
        if (parts.Length == 1)
        {
            if (!TryParseWhole(parts[0], out var minutes) || minutes > 600)
            {
                return false;
            }

            seconds = minutes * 60;
            return true;
        }

        if (parts.Length != 2 || parts[1].Length != 2
            || !TryParseWhole(parts[0], out var m) || !TryParseWhole(parts[1], out var s) || s > 59 || m > 600)
        {
            return false;
        }

        seconds = (m * 60) + s;
        return true;
    }

    public static bool TryParseWhole(string? text, out int value) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    /// <summary>Resumen de una línea: periodos, descansos, tiempos muertos, posesión, prórroga y tanda.</summary>
    public static string Summary(RulesetSnapshot rules) =>
        $"{rules.PeriodCount} periodos × {Duration(rules.PeriodDurationSeconds)} · " +
        $"descanso {Duration(rules.BreakDurationSeconds)}, medio tiempo {Duration(rules.HalftimeDurationSeconds)} · " +
        $"{rules.TimeoutsPerTeam} tiempos muertos por equipo · " +
        $"posesión {rules.ShotClockSeconds} s / {rules.ShotClockAlternateSeconds} s · " +
        $"{Overtime(rules.OvertimePeriodCount, rules.OvertimePeriodDurationSeconds)} · " +
        $"tanda de penales: {(rules.PenaltyShootoutEnabled ? "sí" : "no")}";

    /// <summary>«sin prórroga» o «prórroga 2 × 3:00».</summary>
    public static string Overtime(int periods, int seconds) =>
        periods == 0 ? "sin prórroga" : $"prórroga {periods} × {Duration(seconds)}";
}
