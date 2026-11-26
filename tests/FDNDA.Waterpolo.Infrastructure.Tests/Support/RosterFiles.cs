using System.IO.Compression;
using System.Security;
using System.Text;

namespace FDNDA.Waterpolo.Infrastructure.Tests.Support;

/// <summary>Archivos de nómina para las pruebas: CSV y XLSX mínimos escritos en la carpeta temporal.</summary>
internal static class RosterFiles
{
    public const string Header = "equipo_codigo,equipo_nombre,jugador_nombre,numero_gorro";

    public static string Csv(TempStorage storage, params string[] lines) =>
        CsvWith(storage, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), lines);

    public static string CsvWith(TempStorage storage, Encoding encoding, params string[] lines)
    {
        var path = NewPath(storage, ".csv");
        File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", encoding);
        return path;
    }

    /// <summary>Plantilla de muestra del repositorio (<c>samples/</c>).</summary>
    public static string Sample(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FDNDA.Waterpolo.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "samples", fileName);
    }

    /// <summary>
    /// XLSX mínimo con una hoja. Las celdas numéricas se guardan como número; el resto alterna entre
    /// textos compartidos y textos en línea para ejercitar ambos caminos del lector.
    /// </summary>
    public static string Xlsx(TempStorage storage, IReadOnlyList<object[]> rows, bool macroContentType = false, string extension = ".xlsx")
    {
        var path = NewPath(storage, extension);
        var shared = new List<string>();
        var sheet = new StringBuilder();
        sheet.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
        for (var r = 0; r < rows.Count; r++)
        {
            sheet.Append($"<row r=\"{r + 1}\">");
            for (var c = 0; c < rows[r].Length; c++)
            {
                var reference = $"{(char)('A' + c)}{r + 1}";
                switch (rows[r][c])
                {
                    case null:
                        break;
                    case int number:
                        sheet.Append($"<c r=\"{reference}\"><v>{number}</v></c>");
                        break;
                    case double number:
                        sheet.Append($"<c r=\"{reference}\"><v>{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>");
                        break;
                    case string text when (r + c) % 2 == 0:
                        shared.Add(text);
                        sheet.Append($"<c r=\"{reference}\" t=\"s\"><v>{shared.Count - 1}</v></c>");
                        break;
                    case string text:
                        sheet.Append($"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(text)}</t></is></c>");
                        break;
                }
            }

            sheet.Append("</row>");
        }

        sheet.Append("</sheetData></worksheet>");

        var sst = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        foreach (var text in shared)
        {
            // Texto enriquecido en dos tramos y guía fonética: el lector debe unir los tramos y omitir la guía.
            var half = text.Length / 2;
            sst.Append($"<si><r><t xml:space=\"preserve\">{SecurityElement.Escape(text[..half])}</t></r><r><t xml:space=\"preserve\">{SecurityElement.Escape(text[half..])}</t></r><rPh sb=\"0\" eb=\"1\"><t>FONETICA</t></rPh></si>");
        }

        sst.Append("</sst>");
        var workbookType = macroContentType
            ? "application/vnd.ms-excel.sheet.macroEnabled.main+xml"
            : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";

        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Write(zip, "[Content_Types].xml", $"""<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="xml" ContentType="application/xml"/><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Override PartName="/xl/workbook.xml" ContentType="{workbookType}"/></Types>""");
            Write(zip, "_rels/.rels", """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Write(zip, "xl/workbook.xml", """<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Nómina" sheetId="1" r:id="rId7"/></sheets></workbook>""");
            Write(zip, "xl/_rels/workbook.xml.rels", """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId9" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/><Relationship Id="rId7" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/hoja1.xml"/></Relationships>""");
            Write(zip, "xl/worksheets/hoja1.xml", sheet.ToString());
            Write(zip, "xl/sharedStrings.xml", sst.ToString());
        }

        return path;
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var stream = zip.CreateEntry(name).Open();
        var bytes = new UTF8Encoding(false).GetBytes(content);
        stream.Write(bytes);
    }

    private static string NewPath(TempStorage storage, string extension)
    {
        var dir = Path.Combine(storage.Root, "archivos");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"nomina-{Guid.NewGuid():N}{extension}");
    }
}
