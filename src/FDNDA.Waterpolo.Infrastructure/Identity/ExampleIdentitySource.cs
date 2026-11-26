using System.Reflection;
using FDNDA.Waterpolo.Application.Abstractions;

namespace FDNDA.Waterpolo.Infrastructure.Identity;

/// <summary>
/// Plantilla de ejemplo para el Panamericano de Waterpolo Sub-17 2027. Todas sus imágenes son
/// originales y genéricas (carpeta <c>samples/identidad-ejemplo</c>, embebidas en el programa):
/// no son marcas oficiales del evento, de la FDNDA ni de ningún auspiciador real. Se reemplazan
/// por las oficiales cuando el organizador las entregue.
/// </summary>
public sealed class ExampleIdentitySource : IExampleIdentitySource
{
    private const string Prefix = "FDNDA.Waterpolo.Identidad.";

    public const string Title = "Panamericano de Waterpolo Sub-17 2027";

    public const string Subtitle = "Plantilla de ejemplo · recursos genéricos, no oficiales";

    public ExampleIdentity Load() => new(
        Title,
        Subtitle,
        "#0B2A4A",
        "#FFC83D",
        Resource("logo-evento-ejemplo.png"),
        Resource("logo-organizacion-ejemplo.png"),
        [
            ("Auspiciador A (ejemplo)", Resource("auspiciador-a-ejemplo.png")),
            ("Auspiciador B (ejemplo)", Resource("auspiciador-b-ejemplo.png")),
            ("Auspiciador C (ejemplo)", Resource("auspiciador-c-ejemplo.png")),
        ]);

    /// <summary>Lee una imagen de ejemplo embebida.</summary>
    public static ImageFile Resource(string fileName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Prefix + fileName)
            ?? throw new InvalidOperationException($"Falta el recurso de ejemplo «{fileName}».");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return new ImageFile(fileName, memory.ToArray());
    }
}
