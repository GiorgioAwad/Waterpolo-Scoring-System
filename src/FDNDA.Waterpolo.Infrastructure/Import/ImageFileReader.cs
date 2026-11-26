using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Domain.Competitions;

namespace FDNDA.Waterpolo.Infrastructure.Import;

/// <summary>
/// Lee un archivo de imagen del disco. Rechaza por tamaño antes de leerlo para no cargar en
/// memoria un archivo enorme; el formato y las dimensiones los valida el dominio.
/// </summary>
public sealed class ImageFileReader : IImageFileReader
{
    public ImageFile Read(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new ImageFileException("El archivo no existe.");
        }

        if (info.Length > ImageRules.MaxBytes)
        {
            throw new ImageFileException(
                $"La imagen pesa {ImageInspector.SizeText(info.Length)}; el máximo es {ImageInspector.SizeText(ImageRules.MaxBytes)}. Redúzcala o expórtela de nuevo.");
        }

        ct.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var content = new byte[stream.Length];
        stream.ReadExactly(content);
        return new ImageFile(info.Name, content);
    }
}
