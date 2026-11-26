using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using FDNDA.Waterpolo.Application.Abstractions;

namespace FDNDA.Waterpolo.Infrastructure.Import;

/// <summary>
/// Lee nóminas en CSV UTF-8 o en XLSX (primera hoja). El XLSX se lee directamente como paquete
/// ZIP con XML, sin Microsoft Office, sin ejecutar macros y sin dependencias externas. Se
/// rechazan XLSM, XLS y cualquier otra extensión. Los límites se comprueban antes de interpretar.
/// </summary>
public sealed class RosterFileReader : IRosterFileReader
{
    /// <summary>Interpreta texto CSV ya decodificado (sin validar tamaño ni codificación).</summary>
    public static RosterTable ParseCsv(string text) => CsvRosterReader.Parse(text);

    public RosterTable Read(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".csv" => CsvRosterReader.Read(path, ct),
            ".xlsx" => XlsxRosterReader.Read(path, ct),
            ".xlsm" => throw new RosterFileException(
                "Los libros de Excel con macros (.xlsm) no se aceptan. Guarde la nómina como «Libro de Excel (.xlsx)» o como «CSV UTF-8»."),
            ".xls" => throw new RosterFileException(
                "El formato antiguo de Excel (.xls) no se acepta. Guarde la nómina como «Libro de Excel (.xlsx)» o como «CSV UTF-8»."),
            _ => throw new RosterFileException("Elija un archivo .csv (UTF-8) o .xlsx con la plantilla de nómina."),
        };
    }
}

/// <summary>CSV UTF-8 (con o sin BOM), separado por comas, punto y coma o tabulador; comillas según RFC 4180.</summary>
internal static class CsvRosterReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static RosterTable Read(string path, CancellationToken ct)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new RosterFileException("El archivo no existe.");
        }

        if (info.Length > RosterImportLimits.MaxCsvBytes)
        {
            throw new RosterFileException($"El archivo pesa más de {RosterImportLimits.MaxCsvBytes / (1024 * 1024)} MB; una nómina no necesita tanto.");
        }

        var bytes = File.ReadAllBytes(path);
        ct.ThrowIfCancellationRequested();
        if (Array.IndexOf(bytes, (byte)0) >= 0)
        {
            throw new RosterFileException("El archivo no es un CSV de texto. Si es un libro de Excel, guárdelo como .xlsx o como «CSV UTF-8».");
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new RosterFileException(
                "El archivo no está en UTF-8 (las tildes y la ñ se perderían). En Excel use «Guardar como» → «CSV UTF-8 (delimitado por comas)».");
        }

        return Parse(text.TrimStart('﻿'), ct);
    }

    public static RosterTable Parse(string text, CancellationToken ct = default)
    {
        var delimiter = DetectDelimiter(text);
        var rows = new List<RosterTableRow>();
        var cells = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;
        var record = 1;
        var recordStartLine = 1;
        var line = 1;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    field.Append(c);
                }

                continue;
            }

            if (c == '"' && !fieldStarted)
            {
                inQuotes = true;
                fieldStarted = true;
            }
            else if (c == delimiter)
            {
                cells.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                cells.Add(field.ToString());
                rows.Add(new RosterTableRow(record, cells));
                cells = [];
                field.Clear();
                fieldStarted = false;
                record++;
                line++;
                recordStartLine = line;
                if (rows.Count > RosterImportLimits.MaxDataRows + 10_000)
                {
                    throw new RosterFileException($"El archivo tiene demasiadas filas; el máximo es {RosterImportLimits.MaxDataRows} jugadores.");
                }

                ct.ThrowIfCancellationRequested();
            }
            else
            {
                field.Append(c);
                fieldStarted = true;
            }
        }

        if (inQuotes)
        {
            throw new RosterFileException($"Fila {record}: hay unas comillas sin cerrar (empiezan en la línea {recordStartLine} del archivo).");
        }

        if (field.Length > 0 || cells.Count > 0)
        {
            cells.Add(field.ToString());
            rows.Add(new RosterTableRow(record, cells));
        }

        return new RosterTable(rows);
    }

    /// <summary>Elige el separador del encabezado: Excel en algunas configuraciones regionales guarda con punto y coma.</summary>
    private static char DetectDelimiter(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        var header = end < 0 ? text : text[..end];
        char[] candidates = [',', ';', '\t'];
        var best = candidates.MaxBy(c => header.Count(h => h == c)); // ante un empate gana la coma
        return header.Contains(best, StringComparison.Ordinal) ? best : ',';
    }
}

/// <summary>
/// Lector mínimo de XLSX (Office Open XML): primera hoja del libro, textos compartidos, textos en
/// línea y números. No evalúa fórmulas: toma el último valor guardado en la celda.
/// </summary>
internal static class XlsxRosterReader
{
    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const int MaxEntries = 500;

    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    public static RosterTable Read(string path, CancellationToken ct)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new RosterFileException("El archivo no existe.");
        }

        if (info.Length > RosterImportLimits.MaxXlsxBytes)
        {
            throw new RosterFileException($"El libro pesa más de {RosterImportLimits.MaxXlsxBytes / (1024 * 1024)} MB; una nómina no necesita tanto.");
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxEntries)
            {
                throw Damaged();
            }

            if (zip.GetEntry("xl/vbaProject.bin") is not null || ContentTypesDeclareMacros(zip))
            {
                throw new RosterFileException(
                    "El libro contiene macros y no se acepta. Guarde la nómina como «Libro de Excel (.xlsx)» sin macros o como «CSV UTF-8».");
            }

            var sheetPath = FirstSheetPath(zip);
            var shared = ReadSharedStrings(zip, ct);
            return ReadSheet(Entry(zip, sheetPath) ?? throw Damaged(), shared, ct);
        }
        catch (InvalidDataException)
        {
            throw Damaged();
        }
        catch (XmlException)
        {
            throw Damaged();
        }
    }

    private static RosterFileException Damaged() =>
        new("El archivo no es un libro de Excel (.xlsx) válido o está dañado. Ábralo en Excel y vuelva a guardarlo como .xlsx.");

    private static ZipArchiveEntry? Entry(ZipArchive zip, string path) =>
        zip.GetEntry(path) ?? zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, path, StringComparison.OrdinalIgnoreCase));

    private static XmlReader OpenXml(ZipArchiveEntry entry)
    {
        if (entry.Length > RosterImportLimits.MaxXlsxPartBytes)
        {
            throw new RosterFileException("El libro es demasiado grande para una nómina.");
        }

        return XmlReader.Create(new BoundedStream(entry.Open(), RosterImportLimits.MaxXlsxPartBytes), XmlSettings);
    }

    private static bool ContentTypesDeclareMacros(ZipArchive zip)
    {
        var entry = Entry(zip, "[Content_Types].xml");
        if (entry is null)
        {
            throw Damaged();
        }

        using var xml = OpenXml(entry);
        while (xml.Read())
        {
            if (xml.NodeType == XmlNodeType.Element
                && xml.GetAttribute("ContentType") is { } type
                && type.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string FirstSheetPath(ZipArchive zip)
    {
        string? relationId = null;
        using (var xml = OpenXml(Entry(zip, "xl/workbook.xml") ?? throw Damaged()))
        {
            while (xml.Read())
            {
                if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "sheet" && xml.NamespaceURI == MainNs)
                {
                    relationId = xml.GetAttribute("id", RelNs);
                    break;
                }
            }
        }

        if (relationId is null)
        {
            throw Damaged();
        }

        using (var xml = OpenXml(Entry(zip, "xl/_rels/workbook.xml.rels") ?? throw Damaged()))
        {
            while (xml.Read())
            {
                if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "Relationship" && xml.NamespaceURI == PackageRelNs
                    && xml.GetAttribute("Id") == relationId)
                {
                    var target = xml.GetAttribute("Target") ?? throw Damaged();
                    return target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                }
            }
        }

        throw Damaged();
    }

    private static List<string> ReadSharedStrings(ZipArchive zip, CancellationToken ct)
    {
        var result = new List<string>();
        var entry = Entry(zip, "xl/sharedStrings.xml");
        if (entry is null)
        {
            return result;
        }

        using var xml = OpenXml(entry);
        while (xml.Read())
        {
            if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "si" && xml.NamespaceURI == MainNs)
            {
                result.Add(ReadText(xml));
                ct.ThrowIfCancellationRequested();
            }
        }

        return result;
    }

    /// <summary>Texto de un elemento <c>si</c> o <c>is</c>: concatena los <c>t</c>, sin la guía fonética (<c>rPh</c>).</summary>
    private static string ReadText(XmlReader xml)
    {
        if (xml.IsEmptyElement)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        var depth = xml.Depth;
        var phoneticDepth = -1;
        var advance = true;

        // ReadElementContentAsString ya deja el lector en el nodo siguiente: no se vuelve a leer.
        while (!advance || xml.Read())
        {
            advance = true;
            if (xml.Depth <= depth)
            {
                break; // cierre de si/is
            }

            if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "rPh")
            {
                phoneticDepth = xml.IsEmptyElement ? -1 : xml.Depth;
            }
            else if (xml.NodeType == XmlNodeType.EndElement && xml.Depth == phoneticDepth)
            {
                phoneticDepth = -1;
            }
            else if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "t" && phoneticDepth < 0)
            {
                text.Append(xml.ReadElementContentAsString());
                advance = false;
            }
        }

        return text.ToString();
    }

    private static RosterTable ReadSheet(ZipArchiveEntry entry, List<string> shared, CancellationToken ct)
    {
        var rows = new List<RosterTableRow>();
        using var xml = OpenXml(entry);
        var rowNumber = 0;
        while (xml.Read())
        {
            if (xml.NodeType != XmlNodeType.Element || xml.LocalName != "row" || xml.NamespaceURI != MainNs)
            {
                continue;
            }

            rowNumber = int.TryParse(xml.GetAttribute("r"), NumberStyles.None, CultureInfo.InvariantCulture, out var r) ? r : rowNumber + 1;
            var cells = new SortedDictionary<int, string>();
            if (!xml.IsEmptyElement)
            {
                var rowDepth = xml.Depth;
                var nextColumn = 0;
                while (xml.Read() && xml.Depth > rowDepth)
                {
                    if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "c")
                    {
                        var column = ColumnIndex(xml.GetAttribute("r")) ?? nextColumn;
                        nextColumn = column + 1;
                        if (column > 200)
                        {
                            continue; // una nómina no usa columnas tan lejanas
                        }

                        cells[column] = ReadCell(xml, shared);
                    }
                }
            }

            if (rows.Count > RosterImportLimits.MaxDataRows + 10_000)
            {
                throw new RosterFileException($"El libro tiene demasiadas filas; el máximo es {RosterImportLimits.MaxDataRows} jugadores.");
            }

            var width = cells.Count == 0 ? 0 : cells.Keys.Max() + 1;
            var values = new string[width];
            for (var i = 0; i < width; i++)
            {
                values[i] = cells.TryGetValue(i, out var v) ? v : string.Empty;
            }

            rows.Add(new RosterTableRow(rowNumber, values));
            ct.ThrowIfCancellationRequested();
        }

        return new RosterTable(rows);
    }

    private static string ReadCell(XmlReader xml, List<string> shared)
    {
        var type = xml.GetAttribute("t");
        if (xml.IsEmptyElement)
        {
            return string.Empty;
        }

        var depth = xml.Depth;
        string? value = null;
        string? inline = null;
        var advance = true;
        while (!advance || xml.Read())
        {
            advance = true;
            if (xml.Depth <= depth)
            {
                break; // cierre de c
            }

            if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "v")
            {
                value = xml.ReadElementContentAsString();
                advance = false;
            }
            else if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "is")
            {
                inline = ReadText(xml);
            }
        }

        return type switch
        {
            "s" => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < shared.Count
                ? shared[index]
                : throw Damaged(),
            "inlineStr" => inline ?? string.Empty,
            "str" or "e" or "b" => value ?? string.Empty,
            _ => NormalizeNumber(value),
        };
    }

    /// <summary>Un número entero guardado como «7» o «7.0» se lee como «7»; los demás se dejan tal cual.</summary>
    private static string NormalizeNumber(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && Math.Abs(number) < 1e9 && number == Math.Floor(number)
            ? ((long)number).ToString(CultureInfo.InvariantCulture)
            : value;
    }

    /// <summary>Índice de columna (A = 0) a partir de una referencia como «C12».</summary>
    private static int? ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }

        var index = 0;
        var letters = 0;
        foreach (var c in reference)
        {
            if (c is >= 'A' and <= 'Z')
            {
                index = (index * 26) + (c - 'A' + 1);
                letters++;
            }
            else
            {
                break;
            }
        }

        return letters is 0 or > 3 ? null : index - 1;
    }

    /// <summary>Corta la lectura si una parte descomprime más de lo permitido (protección ante archivos ZIP maliciosos).</summary>
    private sealed class BoundedStream(Stream inner, long limit) : Stream
    {
        private long _read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            _read += n;
            if (_read > limit)
            {
                throw new RosterFileException("El libro es demasiado grande para una nómina.");
            }

            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
