namespace FDNDA.Waterpolo.Application.Abstractions;

/// <summary>Fila leída de un archivo de nómina, con su número de fila en el archivo (la primera es 1).</summary>
public sealed record RosterTableRow(int Number, IReadOnlyList<string> Cells);

/// <summary>Contenido tabular de un archivo de nómina, sin interpretar.</summary>
public sealed record RosterTable(IReadOnlyList<RosterTableRow> Rows);

/// <summary>El archivo no se pudo leer como nómina. El mensaje está escrito para el operador.</summary>
public sealed class RosterFileException(string message) : Exception(message);

/// <summary>Límites de la importación de nóminas, comprobados antes de interpretar el contenido.</summary>
public static class RosterImportLimits
{
    /// <summary>Filas de datos como máximo (sin contar el encabezado).</summary>
    public const int MaxDataRows = 1000;

    /// <summary>Tamaño máximo de un CSV.</summary>
    public const long MaxCsvBytes = 2 * 1024 * 1024;

    /// <summary>Tamaño máximo de un XLSX comprimido.</summary>
    public const long MaxXlsxBytes = 5 * 1024 * 1024;

    /// <summary>Tamaño máximo descomprimido de cada parte que se lee de un XLSX.</summary>
    public const long MaxXlsxPartBytes = 20 * 1024 * 1024;
}

/// <summary>Lee un archivo de nómina (CSV UTF-8 o XLSX) como tabla de textos.</summary>
public interface IRosterFileReader
{
    /// <exception cref="RosterFileException">Formato no admitido, archivo dañado o fuera de los límites.</exception>
    RosterTable Read(string path, CancellationToken ct = default);
}
