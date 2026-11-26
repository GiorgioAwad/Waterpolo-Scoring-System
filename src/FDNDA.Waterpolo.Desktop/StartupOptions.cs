using FDNDA.Waterpolo.Application.Abstractions;

namespace FDNDA.Waterpolo.Desktop;

/// <summary>
/// Opciones de arranque:
///   --perfil demo|produccion   Perfil de datos y fuente de tiempo. Por defecto: demo (ver docs/plan-entrega.md).
///   --operador "Nombre"        Identifica al operador sin mostrar el diálogo inicial.
///   --datos "carpeta"          Raíz de datos alternativa (pruebas y diagnóstico). Sin esta opción
///                              se usa la variable de entorno FDNDA_WATERPOLO_DATOS y, si no existe,
///                              la carpeta FDNDA/Waterpolo de LocalAppData.
///   --reproduccion "archivo"   Reproduce una grabación normalizada de cronometraje (etiqueta
///                              «REPRODUCCIÓN») en lugar del simulador. Solo con el perfil demo.
///   --puerto COMx              Puerto serie del adaptador conectado a COM PORT 2 de la consola Colorado
///                              (solo lectura). Solo con el perfil produccion; sin él, la consola queda
///                              «no disponible» con el motivo.
/// </summary>
public sealed record StartupOptions(AppProfile Profile, string? OperatorName, string? DataRoot, string? ReplayPath = null, string? ConsolePort = null)
{
    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        var profile = AppProfile.Demo;
        string? operatorName = null;
        string? dataRoot = null;
        string? replayPath = null;
        string? consolePort = null;

        for (var i = 0; i < args.Count; i++)
        {
            var hasValue = i + 1 < args.Count;
            switch (args[i].ToLowerInvariant())
            {
                case "--perfil" when hasValue:
                    profile = args[++i].ToLowerInvariant() switch
                    {
                        "demo" => AppProfile.Demo,
                        "produccion" or "producción" => AppProfile.Produccion,
                        var other => throw new ArgumentException($"Perfil desconocido: «{other}». Use demo o produccion."),
                    };
                    break;
                case "--operador" when hasValue:
                    operatorName = args[++i];
                    break;
                case "--datos" when hasValue:
                    dataRoot = args[++i];
                    break;
                case "--reproduccion" or "--reproducción" when hasValue:
                    replayPath = args[++i];
                    break;
                case "--puerto" when hasValue:
                    consolePort = ParsePort(args[++i]);
                    break;
                default:
                    throw new ArgumentException($"Opción de arranque no reconocida: «{args[i]}».");
            }
        }

        // En producción la fuente es la consola: el programa no ensaya con datos grabados sobre
        // partidos oficiales ni pasa a otra fuente por una opción de arranque.
        if (replayPath is not null && profile != AppProfile.Demo)
        {
            throw new ArgumentException("La reproducción de cronometraje solo se usa con el perfil demo.");
        }

        if (consolePort is not null && profile != AppProfile.Produccion)
        {
            throw new ArgumentException("El puerto de la consola solo se usa con el perfil produccion.");
        }

        return new StartupOptions(profile, operatorName, dataRoot, replayPath, consolePort);
    }

    /// <summary>Nombre de puerto de Windows (COM1 a COM256), en mayúsculas.</summary>
    private static string ParsePort(string value)
    {
        var port = value.Trim().ToUpperInvariant();
        return port.Length is > 3 and <= 6 && port.StartsWith("COM", StringComparison.Ordinal) && port[3..].All(char.IsAsciiDigit)
            && int.Parse(port[3..], System.Globalization.CultureInfo.InvariantCulture) is >= 1 and <= 256
            ? port
            : throw new ArgumentException($"Puerto no válido: «{value}». Use el nombre de Windows, por ejemplo COM5.");
    }
}
