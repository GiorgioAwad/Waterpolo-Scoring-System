using System.Globalization;
using FDNDA.Waterpolo.Application.Matches;

namespace FDNDA.Waterpolo.Desktop.Presentation;

/// <summary>Textos del marcador del público (rediseño «A · Transmisión»).</summary>
public static class ScoreboardTexts
{
    /// <summary>Ordinal abreviado en español: «1.er», «2.º», «3.er», «4.º»…</summary>
    public static string Ordinal(int number) =>
        number.ToString(CultureInfo.InvariantCulture) + (number is 1 or 3 ? ".er" : ".º");

    /// <summary>Ordinal femenino abreviado para las faltas: «1.ª», «2.ª», «3.ª»…</summary>
    public static string FoulOrdinal(int number) => number.ToString(CultureInfo.InvariantCulture) + ".ª";

    /// <summary>Línea de faltas del aviso: «2.ª FALTA».</summary>
    public static string FoulLine(int fouls) => fouls > 0 ? $"{FoulOrdinal(fouls)} FALTA" : string.Empty;

    /// <summary>«3.er PERIODO» o, pasados los reglamentarios, «PRÓRROGA 1».</summary>
    public static string PeriodLine(MatchSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.IsOvertime
            ? $"PRÓRROGA {snapshot.OvertimeNumber.ToString(CultureInfo.InvariantCulture)}"
            : $"{Ordinal(snapshot.CurrentPeriod)} PERIODO";
    }

    /// <summary>Periodo que acaba de terminar, para el descanso: «FIN DEL 2.º PERIODO».</summary>
    public static string BreakLine(MatchSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.IsOvertime
            ? $"FIN DE LA PRÓRROGA {snapshot.OvertimeNumber.ToString(CultureInfo.InvariantCulture)}"
            : $"FIN DEL {Ordinal(snapshot.CurrentPeriod)} PERIODO";
    }
}
