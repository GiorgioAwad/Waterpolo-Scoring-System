using FDNDA.Waterpolo.Application.Reports;

namespace FDNDA.Waterpolo.Infrastructure.Export;

/// <summary>
/// Escribe resúmenes en archivos. Escribe primero un temporal en la misma carpeta y luego lo
/// mueve sobre el destino: si algo falla (disco lleno, archivo abierto en Excel), no queda un
/// archivo a medias y el que existía no se pierde.
/// </summary>
public sealed class ReportFileWriter(TimeProvider time) : IReportWriter
{
    public async Task WriteAsync(ReportDocument document, ReportFormat format, string path, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var target = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                switch (format)
                {
                    case ReportFormat.Csv:
                        CsvReportWriter.Write(document, stream);
                        break;
                    case ReportFormat.Pdf:
                        PdfReportWriter.Write(document, stream, time.GetUtcNow());
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(format));
                }

                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            ct.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
