using FDNDA.Waterpolo.Application.Abstractions;

namespace FDNDA.Waterpolo.Infrastructure.Storage;

/// <summary>De dónde salió la carpeta raíz de datos.</summary>
public enum DataRootOrigin
{
    /// <summary>Opción de arranque <c>--datos</c>.</summary>
    StartupOption,

    /// <summary>Variable de entorno <see cref="AppPaths.DataRootVariable"/>.</summary>
    EnvironmentVariable,

    /// <summary>Perfil del usuario de Windows (<c>%LocalAppData%\FDNDA\Waterpolo</c>).</summary>
    UserProfile,
}

/// <summary>
/// Rutas de datos, separadas de los binarios. La raíz se elige en este orden: opción
/// <c>--datos</c>, variable de entorno <see cref="DataRootVariable"/> y, si no hay ninguna,
/// <c>%LocalAppData%\FDNDA\Waterpolo</c>. Demo y producción usan carpetas y bases distintas.
/// </summary>
public sealed record AppPaths(AppProfile Profile, string DataDirectory)
{
    /// <summary>Variable de entorno con la carpeta raíz de datos (p. ej. en otro disco).</summary>
    public const string DataRootVariable = "FDNDA_WATERPOLO_DATOS";

    public DataRootOrigin Origin { get; init; } = DataRootOrigin.StartupOption;

    public string DatabasePath => Path.Combine(DataDirectory, "waterpolo.db");

    public string BackupDirectory => Path.Combine(DataDirectory, "respaldos");

    public string LogDirectory => Path.Combine(DataDirectory, "logs");

    public string LockFilePath => Path.Combine(DataDirectory, "instancia.lock");

    /// <summary>Texto para el diagnóstico: cómo se eligió la carpeta.</summary>
    public string OriginText => Origin switch
    {
        DataRootOrigin.StartupOption => "opción de arranque --datos",
        DataRootOrigin.EnvironmentVariable => $"variable de entorno {DataRootVariable}",
        _ => "predeterminada (perfil del usuario de Windows)",
    };

    /// <param name="profile">Perfil de datos.</param>
    /// <param name="rootOverride">Raíz de la opción <c>--datos</c>; nula para buscar la variable de entorno.</param>
    /// <param name="readVariable">Lectura de variables de entorno; las pruebas la sustituyen.</param>
    public static AppPaths ForProfile(AppProfile profile, string? rootOverride = null, Func<string, string?>? readVariable = null)
    {
        string root;
        DataRootOrigin origin;
        var variable = (readVariable ?? Environment.GetEnvironmentVariable)(DataRootVariable);
        if (!string.IsNullOrWhiteSpace(rootOverride))
        {
            root = Path.GetFullPath(rootOverride);
            origin = DataRootOrigin.StartupOption;
        }
        else if (!string.IsNullOrWhiteSpace(variable))
        {
            root = Path.GetFullPath(variable.Trim().Trim('"'));
            origin = DataRootOrigin.EnvironmentVariable;
        }
        else
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FDNDA", "Waterpolo");
            origin = DataRootOrigin.UserProfile;
        }

        var folder = profile == AppProfile.Demo ? "demo" : "produccion";
        return new AppPaths(profile, Path.Combine(root, folder)) { Origin = origin };
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
