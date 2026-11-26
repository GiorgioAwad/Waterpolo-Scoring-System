namespace FDNDA.Waterpolo.Application.Abstractions;

/// <summary>Por qué se creó un respaldo; se refleja en el nombre del archivo.</summary>
public enum BackupReason
{
    /// <summary>Pedido por el operador.</summary>
    Manual = 1,

    /// <summary>Automático, antes de iniciar un partido.</summary>
    BeforeMatch = 2,

    /// <summary>Automático, al finalizar un partido.</summary>
    AfterMatch = 3,

    /// <summary>Copia de seguridad de la base en uso, antes de reemplazarla por un respaldo.</summary>
    BeforeRestore = 4,

    /// <summary>Automático, antes de migrar el esquema.</summary>
    BeforeMigration = 5,

    /// <summary>Archivo de la carpeta de respaldos con otro nombre (p. ej. copiado a mano).</summary>
    Other = 6,
}

/// <summary>Archivo de respaldo en la carpeta de respaldos.</summary>
public sealed record BackupFile(string Path, string FileName, BackupReason Reason, DateTimeOffset CreatedAtUtc, long SizeBytes);

/// <summary>
/// Lo que contiene un archivo de respaldo, leído sin tocar la base en uso. Si
/// <see cref="Problem"/> no es nulo, el archivo no se puede restaurar.
/// </summary>
public sealed record BackupInspection(
    string Path,
    string? Problem,
    int SchemaVersion,
    int SupportedSchemaVersion,
    int Tournaments,
    int Teams,
    int Matches,
    int MatchesInProgress,
    int Incidents,
    bool ContainsDemoData)
{
    public bool IsValid => Problem is null;

    public static BackupInspection Invalid(string path, string problem, int supportedSchemaVersion) =>
        new(path, problem, 0, supportedSchemaVersion, 0, 0, 0, 0, 0, false);
}

/// <summary>Restauración terminada: la base en uso es ahora la del respaldo (migrada si hacía falta).</summary>
public sealed record RestoreOutcome(string SafetyBackupPath, int RestoredSchemaVersion, int SchemaVersion);

/// <summary>
/// La restauración no se completó. <see cref="RolledBack"/> indica si la base anterior quedó en uso
/// (o si nunca se tocó); si es falso, la base pudo quedar a medias y hay que restaurar
/// <see cref="SafetyBackupPath"/>.
/// </summary>
public sealed class RestoreFailedException(string message, bool rolledBack, string? safetyBackupPath, Exception? inner = null)
    : Exception(message, inner)
{
    public bool RolledBack { get; } = rolledBack;

    public string? SafetyBackupPath { get; } = safetyBackupPath;
}

/// <summary>
/// Respaldos de la base local: crear (copia consistente y verificada), listar, examinar y
/// restaurar. Restaurar valida integridad y versión, respalda antes la base en uso y vuelve a
/// ella si algo falla.
/// </summary>
public interface IBackupStore
{
    /// <summary>Carpeta donde se guardan los respaldos de este perfil.</summary>
    string BackupDirectory { get; }

    Task<BackupFile> CreateAsync(BackupReason reason, CancellationToken ct = default);

    /// <summary>Respaldos de la carpeta, del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<BackupFile>> ListAsync(CancellationToken ct = default);

    Task<BackupInspection> InspectAsync(string path, CancellationToken ct = default);

    /// <exception cref="RestoreFailedException">No se restauró; el mensaje explica en qué estado quedó la base.</exception>
    Task<RestoreOutcome> RestoreAsync(string path, CancellationToken ct = default);
}
