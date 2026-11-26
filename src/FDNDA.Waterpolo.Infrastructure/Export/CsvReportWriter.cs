using System.Text;
using FDNDA.Waterpolo.Application.Reports;

namespace FDNDA.Waterpolo.Infrastructure.Export;

/// <summary>
/// CSV UTF-8 con BOM (Excel lo abre con acentos), separado por comas y con fin de línea CRLF
/// (RFC 4180). Cada tabla va precedida por su título y separada por una línea en blanco.
/// Los textos que una hoja de cálculo interpretaría como fórmula se neutralizan.
/// </summary>
public static class CsvReportWriter
{
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];

    public static void Write(ReportDocument document, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true)
        {
            NewLine = "\r\n",
        };

        Row(writer, document.Title);
        foreach (var line in document.Lines)
        {
            Row(writer, line);
        }

        foreach (var section in document.Sections)
        {
            writer.WriteLine();
            Row(writer, section.Heading);
            Row(writer, [.. section.Columns.Select(c => c.Header)]);
            if (section.Rows.Count == 0)
            {
                Row(writer, section.EmptyText);
            }

            foreach (var row in section.Rows)
            {
                Row(writer, [.. row]);
            }

            if (section.Note is { } note)
            {
                Row(writer, note);
            }
        }

        writer.WriteLine();
        Row(writer, document.Footer);
    }

    /// <summary>
    /// Un campo listo para el CSV: neutraliza fórmulas anteponiendo un apóstrofo y lo pone entre
    /// comillas si lleva separador, comillas o saltos de línea.
    /// </summary>
    public static string Field(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && Array.IndexOf(FormulaStarts, text[0]) >= 0)
        {
            text = "'" + text;
        }

        var needsQuotes = text.IndexOfAny([',', '"', '\r', '\n']) >= 0 || text != text.Trim();
        return needsQuotes ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;
    }

    private static void Row(StreamWriter writer, params string[] fields) =>
        writer.WriteLine(string.Join(",", fields.Select(Field)));
}
