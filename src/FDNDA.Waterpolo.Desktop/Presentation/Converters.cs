using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FDNDA.Waterpolo.Desktop.Presentation;

/// <summary>Visible cuando el valor es falso.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible cuando hay un objeto (no nulo).</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Texto en mayúsculas (es-PE), para el estilo de transmisión del marcador.</summary>
public sealed class UpperCaseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value as string)?.ToUpper(MatchTexts.Culture) ?? string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Visible si el valor (un enum) es uno de los nombres del parámetro, separados por «|»
/// (p. ej. «Break|Shootout»). Con «!» delante se invierte («!Preview»).
/// </summary>
public sealed class EnumVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = parameter as string ?? string.Empty;
        var negate = text.StartsWith('!');
        var names = (negate ? text[1..] : text).Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var match = value is not null && names.Contains(value.ToString(), StringComparer.Ordinal);
        return match != negate ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Arco del anillo de posesión: la fracción (0–1) se dibuja desde arriba en sentido horario sobre un
/// círculo de 269 px con trazo de 16 px (centro 134,5; radio 126,5). Vacío con 0 o un valor inválido.
/// </summary>
public sealed class ShotRingGeometryConverter : IValueConverter
{
    private const double Center = 134.5;
    private const double Radius = 126.5;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var fraction = value is double d && double.IsFinite(d) ? Math.Clamp(d, 0, 1) : 0;
        if (fraction <= 0)
        {
            return System.Windows.Media.Geometry.Empty;
        }

        if (fraction >= 0.9999)
        {
            return new System.Windows.Media.EllipseGeometry(new Point(Center, Center), Radius, Radius);
        }

        var angle = fraction * 2 * Math.PI;
        var start = new Point(Center, Center - Radius);
        var end = new Point(Center + (Radius * Math.Sin(angle)), Center - (Radius * Math.Cos(angle)));
        var figure = new System.Windows.Media.PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new System.Windows.Media.ArcSegment(
            end, new Size(Radius, Radius), 0, fraction > 0.5, System.Windows.Media.SweepDirection.Clockwise, isStroked: true));
        var geometry = new System.Windows.Media.PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Convierte una <see cref="FDNDA.Waterpolo.Application.Identity.ImageData"/> en imagen de WPF.
/// El parámetro es el alto de decodificación en píxeles (limita la memoria). Una imagen que WPF no
/// puede decodificar se muestra vacía: nunca detiene el marcador.
/// </summary>
public sealed class ImageDataConverter : IValueConverter
{
    private const int MaxCached = 64;

    // Solo se usa desde el hilo de la interfaz.
    private static readonly Dictionary<(Guid, int), System.Windows.Media.ImageSource?> Cache = [];

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not FDNDA.Waterpolo.Application.Identity.ImageData data)
        {
            return null;
        }

        var height = parameter is string text && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var h) ? h : 0;
        if (Cache.TryGetValue((data.AssetId, height), out var cached))
        {
            return cached;
        }

        System.Windows.Media.ImageSource? image;
        try
        {
            using var stream = new System.IO.MemoryStream(data.Content, writable: false);
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile;
            if (height > 0)
            {
                bitmap.DecodePixelHeight = height;
            }

            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            image = bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or System.IO.IOException or InvalidOperationException or ArgumentException)
        {
            image = null;
        }

        if (Cache.Count >= MaxCached)
        {
            Cache.Clear();
        }

        Cache[(data.AssetId, height)] = image;
        return image;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
