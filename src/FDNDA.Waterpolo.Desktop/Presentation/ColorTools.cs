using System.Globalization;

namespace FDNDA.Waterpolo.Desktop.Presentation;

/// <summary>
/// Cálculos de color del marcador sobre textos <c>#RRGGBB</c> (con o sin «#»). Un color inválido
/// nunca lanza: se usa un valor predeterminado seguro.
/// </summary>
public static class ColorTools
{
    /// <summary>Color neutro para equipos sin color válido (el mismo predeterminado del marcador).</summary>
    public const string Fallback = "#37474F";

    /// <summary>Con luminancia mayor que esta, el texto negro contrasta mejor que el blanco.</summary>
    public const double DarkTextThreshold = 0.3;

    /// <summary>Luminancia relativa (sRGB, 0 negro – 1 blanco). Inválido → 0.</summary>
    public static double Luminance(string? hex)
    {
        if (!TryParse(hex, out var r, out var g, out var b))
        {
            return 0;
        }

        return (0.2126 * Linear(r)) + (0.7152 * Linear(g)) + (0.0722 * Linear(b));
    }

    /// <summary>Texto legible sobre el color: negro sobre colores claros, blanco sobre el resto.</summary>
    public static string TextOn(string? hex) =>
        TryParse(hex, out _, out _, out _) && Luminance(hex) > DarkTextThreshold ? "#000000" : "#FFFFFF";

    /// <summary>El mismo tono más oscuro: cada canal × (1 − <paramref name="amount"/>).</summary>
    public static string Darken(string? hex, double amount)
    {
        if (!TryParse(hex, out var r, out var g, out var b))
        {
            TryParse(Fallback, out r, out g, out b);
        }

        var keep = 1 - Math.Clamp(amount, 0, 1);
        return $"#{Scale(r, keep):X2}{Scale(g, keep):X2}{Scale(b, keep):X2}";
    }

    /// <summary>El color con opacidad, como <c>#AARRGGBB</c>. Inválido → transparente.</summary>
    public static string WithAlpha(string? hex, double alpha)
    {
        if (!TryParse(hex, out var r, out var g, out var b))
        {
            return "#00000000";
        }

        return $"#{Scale(255, Math.Clamp(alpha, 0, 1)):X2}{r:X2}{g:X2}{b:X2}";
    }

    private static int Scale(int channel, double factor) =>
        (int)Math.Round(channel * factor, MidpointRounding.AwayFromZero);

    private static double Linear(int channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static bool TryParse(string? hex, out int r, out int g, out int b)
    {
        r = g = b = 0;
        var text = hex?.Trim() ?? string.Empty;
        if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        if (text.Length != 6
            || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        r = (value >> 16) & 0xFF;
        g = (value >> 8) & 0xFF;
        b = value & 0xFF;
        return true;
    }
}
