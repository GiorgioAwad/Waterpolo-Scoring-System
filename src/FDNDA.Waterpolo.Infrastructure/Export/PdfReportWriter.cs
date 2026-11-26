using System.Globalization;
using System.Text;
using FDNDA.Waterpolo.Application.Reports;

namespace FDNDA.Waterpolo.Infrastructure.Export;

/// <summary>
/// Escritor PDF mínimo y propio (PDF 1.4, A4 vertical): solo texto y líneas con las fuentes
/// estándar Helvetica y Helvetica-Bold, que todo lector de PDF trae, en codificación WinAnsi
/// (cubre los acentos, la ñ y las comillas latinas del español). No incrusta fuentes ni imágenes.
/// Las tablas cortan el texto que no cabe con «…» y repiten el encabezado al cambiar de página.
/// Ver arquitectura, decisión D31.
/// </summary>
public static class PdfReportWriter
{
    private const double PageWidth = 595.28;
    private const double PageHeight = 841.89;
    private const double Margin = 42;
    private const double FooterBaseline = 26;
    private const double BodyBottom = Margin + 12;
    private const double CellPadding = 3;

    public static void Write(ReportDocument document, Stream destination, DateTimeOffset creationUtc)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        var layout = new Layout();
        layout.Render(document);
        WriteFile(layout.Pages, document, destination, creationUtc);
    }

    // ---------- Maquetación ----------

    private sealed class Layout
    {
        private double _y;

        public List<StringBuilder> Pages { get; } = [];

        private StringBuilder Page => Pages[^1];

        private static double Usable => PageWidth - (2 * Margin);

        public void Render(ReportDocument document)
        {
            NewPage();
            foreach (var line in Fonts.Wrap(document.Title, Usable, bold: true, 16))
            {
                Text(line, Margin, _y - 16, bold: true, 16);
                _y -= 20;
            }

            _y -= 2;
            foreach (var paragraph in document.Lines)
            {
                var strong = paragraph.StartsWith("DATOS DE DEMOSTRACIÓN", StringComparison.Ordinal);
                foreach (var line in Fonts.Wrap(paragraph, Usable, strong, 10))
                {
                    Ensure(13);
                    Text(line, Margin, _y - 10, strong, 10, gray: strong ? 0 : 0.2);
                    _y -= 13;
                }
            }

            foreach (var section in document.Sections)
            {
                _y -= 12;
                Section(section);
            }

            var total = Pages.Count;
            for (var i = 0; i < total; i++)
            {
                var footer = Pages[i];
                var number = $"Página {i + 1} de {total}";
                Text(footer, Fonts.Truncate(document.Footer, Usable - 80, bold: false, 8), Margin, FooterBaseline, bold: false, 8, gray: 0.35);
                Text(footer, number, PageWidth - Margin - Fonts.Measure(number, bold: false, 8), FooterBaseline, bold: false, 8, gray: 0.35);
                Line(footer, Margin, FooterBaseline + 11, PageWidth - Margin, FooterBaseline + 11, 0.4, 0.75);
            }
        }

        private void Section(ReportSection section)
        {
            var total = section.Columns.Sum(c => c.Width);
            var widths = section.Columns.Select(c => c.Width / total * Usable).ToArray();
            var headers = section.Columns.Select((c, i) => Fonts.Wrap(c.Header, widths[i] - (2 * CellPadding), bold: true, 8, maxLines: 2)).ToArray();
            var headerHeight = (headers.Max(h => h.Count) * 9.5) + 6;

            Ensure(18 + headerHeight + 14);
            Text(section.Heading, Margin, _y - 12, bold: true, 12);
            _y -= 18;
            Header(section, widths, headers, headerHeight);

            if (section.Rows.Count == 0)
            {
                Ensure(14);
                Text(section.EmptyText, Margin + CellPadding, _y - 10, bold: false, 9, gray: 0.35);
                _y -= 14;
            }

            foreach (var row in section.Rows)
            {
                if (_y - 13 < BodyBottom)
                {
                    NewPage();
                    Text($"{section.Heading} (continuación)", Margin, _y - 11, bold: true, 11);
                    _y -= 17;
                    Header(section, widths, headers, headerHeight);
                }

                var x = Margin;
                for (var i = 0; i < section.Columns.Count; i++)
                {
                    var value = i < row.Count ? row[i] : string.Empty;
                    Cell(value, x, widths[i], section.Columns[i].Align, _y - 9.5, bold: false, 9);
                    x += widths[i];
                }

                _y -= 13;
                Line(Page, Margin, _y + 1, PageWidth - Margin, _y + 1, 0.3, 0.88);
            }

            if (section.Note is { } note)
            {
                _y -= 3;
                foreach (var line in Fonts.Wrap(note, Usable, bold: false, 8.5))
                {
                    Ensure(11);
                    Text(line, Margin, _y - 8.5, bold: false, 8.5, gray: 0.35);
                    _y -= 11;
                }
            }
        }

        private void Header(ReportSection section, double[] widths, List<string>[] headers, double height)
        {
            Line(Page, Margin, _y, PageWidth - Margin, _y, 0.8, 0.2);
            var x = Margin;
            for (var i = 0; i < section.Columns.Count; i++)
            {
                var lineY = _y - 10;
                foreach (var line in headers[i])
                {
                    Cell(line, x, widths[i], section.Columns[i].Align, lineY, bold: true, 8);
                    lineY -= 9.5;
                }

                x += widths[i];
            }

            _y -= height;
            Line(Page, Margin, _y, PageWidth - Margin, _y, 0.8, 0.2);
        }

        private void Cell(string value, double x, double width, ColumnAlign align, double baseline, bool bold, double size)
        {
            var text = Fonts.Truncate(value, width - (2 * CellPadding), bold, size);
            var measured = Fonts.Measure(text, bold, size);
            var left = align switch
            {
                ColumnAlign.Right => x + width - CellPadding - measured,
                ColumnAlign.Center => x + ((width - measured) / 2),
                _ => x + CellPadding,
            };
            Text(text, left, baseline, bold, size);
        }

        private void Ensure(double height)
        {
            if (_y - height < BodyBottom)
            {
                NewPage();
            }
        }

        private void NewPage()
        {
            Pages.Add(new StringBuilder());
            _y = PageHeight - Margin;
        }

        private void Text(string text, double x, double baseline, bool bold, double size, double gray = 0) =>
            Text(Page, text, x, baseline, bold, size, gray);

        private static void Text(StringBuilder page, string text, double x, double baseline, bool bold, double size, double gray = 0)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            page.Append(CultureInfo.InvariantCulture, $"{N(gray)} g BT /{(bold ? "F2" : "F1")} {N(size)} Tf {N(x)} {N(baseline)} Td ");
            page.Append('(').Append(Fonts.Escape(text)).Append(") Tj ET\n");
        }

        private static void Line(StringBuilder page, double x1, double y1, double x2, double y2, double width, double gray) =>
            page.Append(CultureInfo.InvariantCulture, $"{N(gray)} G {N(width)} w {N(x1)} {N(y1)} m {N(x2)} {N(y2)} l S\n");
    }

    // ---------- Archivo ----------

    private static void WriteFile(List<StringBuilder> pages, ReportDocument document, Stream destination, DateTimeOffset creationUtc)
    {
        var output = new MemoryStream();
        var offsets = new List<long>();

        void Raw(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            output.Write(bytes, 0, bytes.Length);
        }

        void Object(string body)
        {
            offsets.Add(output.Position);
            Raw($"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }

        Raw("%PDF-1.4\n");
        output.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]); // marca de archivo binario

        var pageIds = Enumerable.Range(0, pages.Count).Select(i => 5 + (2 * i)).ToList();
        Object("<< /Type /Catalog /Pages 2 0 R >>");
        Object($"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] /Count {pages.Count} >>");
        Object("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Object("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        for (var i = 0; i < pages.Count; i++)
        {
            var content = pages[i].ToString();
            Object(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(PageWidth)} {N(PageHeight)}] " +
                $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {pageIds[i] + 1} 0 R >>");
            Object($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream");
        }

        var stamp = creationUtc.ToUniversalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        Object($"<< /Title {Utf16(document.Title)} /Producer (Marcador de Waterpolo FDNDA) /Creator (Marcador de Waterpolo FDNDA) /CreationDate (D:{stamp}Z) >>");
        var infoId = offsets.Count;

        var xref = output.Position;
        var table = new StringBuilder();
        table.Append(CultureInfo.InvariantCulture, $"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            table.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        table.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Info {infoId} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Raw(table.ToString());

        output.Position = 0;
        output.CopyTo(destination);
    }

    /// <summary>Texto del diccionario de información en UTF-16BE con marca de orden (admite cualquier carácter).</summary>
    private static string Utf16(string text)
    {
        var builder = new StringBuilder("<FEFF");
        foreach (var b in Encoding.BigEndianUnicode.GetBytes(text))
        {
            builder.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.Append('>').ToString();
    }

    private static string N(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    // ---------- Fuentes estándar ----------

    /// <summary>Anchos (milésimas de em) y codificación WinAnsi de Helvetica y Helvetica-Bold.</summary>
    internal static class Fonts
    {
        // Métricas AFM de Adobe para los caracteres 32–126.
        private static readonly short[] Regular =
        [
            278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
            1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
            333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
            556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
        ];

        private static readonly short[] Bold =
        [
            278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
            975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
            333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
            611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
        ];

        private static readonly Dictionary<char, byte> WinAnsiExtras = new()
        {
            ['€'] = 0x80, ['‚'] = 0x82, ['ƒ'] = 0x83, ['„'] = 0x84, ['…'] = 0x85, ['†'] = 0x86, ['‡'] = 0x87,
            ['ˆ'] = 0x88, ['‰'] = 0x89, ['Š'] = 0x8A, ['‹'] = 0x8B, ['Œ'] = 0x8C, ['Ž'] = 0x8E, ['‘'] = 0x91,
            ['’'] = 0x92, ['“'] = 0x93, ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97, ['˜'] = 0x98,
            ['™'] = 0x99, ['š'] = 0x9A, ['›'] = 0x9B, ['œ'] = 0x9C, ['ž'] = 0x9E, ['Ÿ'] = 0x9F,
        };

        public static double Measure(string text, bool bold, double size) =>
            text.Sum(c => Width(c, bold)) * size / 1000d;

        private static int Width(char c, bool bold)
        {
            var table = bold ? Bold : Regular;
            if (c is >= ' ' and <= '~')
            {
                return table[c - ' '];
            }

            return c switch
            {
                '«' or '»' or '–' => 556,
                '·' => 278,
                '—' or '…' or '‰' => 1000,
                '¿' => 611,
                '¡' => 333,
                '°' => 400,
                'º' or 'ª' => bold ? 365 : 370,
                '‘' or '’' => bold ? 278 : 222,
                '“' or '”' => bold ? 500 : 333,
                _ => BaseWidth(c, table),
            };
        }

        /// <summary>Una letra acentuada mide lo mismo que su letra base (á = a, Ñ = N).</summary>
        private static int BaseWidth(char c, short[] table)
        {
            var decomposed = c.ToString().Normalize(NormalizationForm.FormD);
            return decomposed.Length > 0 && decomposed[0] is >= ' ' and <= '~' ? table[decomposed[0] - ' '] : 556;
        }

        public static string Truncate(string text, double maxWidth, bool bold, double size)
        {
            text ??= string.Empty;
            if (Measure(text, bold, size) <= maxWidth)
            {
                return text;
            }

            var cut = text.Length;
            while (cut > 0 && Measure(text[..cut].TrimEnd() + "…", bold, size) > maxWidth)
            {
                cut--;
            }

            return cut == 0 ? string.Empty : text[..cut].TrimEnd() + "…";
        }

        /// <summary>Reparte un texto en líneas que caben en el ancho, cortando por palabras.</summary>
        public static List<string> Wrap(string text, double maxWidth, bool bold, double size, int maxLines = int.MaxValue)
        {
            var lines = new List<string>();
            var current = string.Empty;
            foreach (var word in (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (Measure(candidate, bold, size) <= maxWidth || current.Length == 0)
                {
                    current = candidate;
                    continue;
                }

                lines.Add(current);
                current = word;
            }

            if (current.Length > 0 || lines.Count == 0)
            {
                lines.Add(current);
            }

            if (lines.Count > maxLines)
            {
                var kept = lines.Take(maxLines).ToList();
                kept[^1] = Truncate(string.Join(" ", lines.Skip(maxLines - 1)), maxWidth, bold, size);
                lines = kept;
            }

            return [.. lines.Select(l => Truncate(l, maxWidth, bold, size))];
        }

        /// <summary>Texto para un literal de cadena PDF: WinAnsi, con paréntesis, barra y bytes altos escapados.</summary>
        public static string Escape(string text)
        {
            var builder = new StringBuilder(text.Length + 8);
            foreach (var c in text)
            {
                var b = Encode(c);
                switch (b)
                {
                    case (byte)'(' or (byte)')' or (byte)'\\':
                        builder.Append('\\').Append((char)b);
                        break;
                    case < 0x20 or >= 0x7F:
                        builder.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                        break;
                    default:
                        builder.Append((char)b);
                        break;
                }
            }

            return builder.ToString();
        }

        private static byte Encode(char c) => c switch
        {
            >= ' ' and <= '~' => (byte)c,
            >= ' ' and <= 'ÿ' => (byte)c,
            _ when WinAnsiExtras.TryGetValue(c, out var b) => b,
            '\t' or '\r' or '\n' => (byte)' ',
            _ => (byte)'?',
        };
    }
}
