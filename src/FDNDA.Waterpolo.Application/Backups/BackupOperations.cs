using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Matches;

namespace FDNDA.Waterpolo.Application.Backups;

/// <summary>Resultado de crear o restaurar un respaldo.</summary>
public sealed record BackupResult(CommandOutcome Outcome, string Message, string? Path = null)
{
    public bool IsSuccess => Outcome is CommandOutcome.Applied or CommandOutcome.AlreadyApplied;
}

/// <summary>Vista previa de una restauración: qué trae el archivo y si se puede restaurar ahora.</summary>
public sealed record RestorePreview(BackupInspection? Inspection, bool CanRestore, string Message);

/// <summary>
/// Casos de uso de respaldo y restauración (RF10). Restaurar se impide con un partido en juego,
/// con un archivo dañado o de una versión más nueva y, en producción, con datos de demostración.
/// El trabajo con archivos corre fuera del hilo que llama.
/// </summary>
public sealed class BackupOperations
{
    private readonly IBackupStore _store;
    private readonly IMatchRepository _matches;
    private readonly IDiagnosticLog _log;
    private readonly AppProfile _profile;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BackupOperations(IBackupStore store, IMatchRepository matches, IDiagnosticLog log, AppProfile profile)
    {
        _store = store;
        _matches = matches;
        _log = log;
        _profile = profile;
    }

    public string BackupDirectory => _store.BackupDirectory;

    public Task<IReadOnlyList<BackupFile>> ListAsync(CancellationToken ct = default) =>
        Task.Run(() => _store.ListAsync(ct), ct);

    /// <summary>Crea un respaldo verificado en la carpeta de respaldos.</summary>
    public async Task<BackupResult> CreateAsync(BackupReason reason, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var file = await Task.Run(() => _store.CreateAsync(reason, ct), ct).ConfigureAwait(false);
            _log.Info($"Respaldo creado ({reason}): {file.FileName}.");
            return new BackupResult(CommandOutcome.Applied, $"Respaldo creado: {file.FileName}.", file.Path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error("No se pudo crear el respaldo.", ex);
            return new BackupResult(
                CommandOutcome.Failed,
                "No se pudo crear el respaldo. Revise el espacio libre y los permisos de la carpeta de datos.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Examina el archivo sin tocar la base en uso y explica si se puede restaurar.</summary>
    public async Task<RestorePreview> PreviewRestoreAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var inspection = await Task.Run(() => _store.InspectAsync(path, ct), ct).ConfigureAwait(false);
            var blocker = Blocker(inspection) ?? await InProgressBlockerAsync(ct).ConfigureAwait(false);
            return new RestorePreview(inspection, blocker is null, blocker ?? Describe(inspection));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error("No se pudo examinar el respaldo.", ex);
            return new RestorePreview(null, false, "No se pudo leer el archivo de respaldo.");
        }
    }

    /// <summary>
    /// Reemplaza la base en uso por el respaldo. Antes vuelve a comprobar que no haya un partido en
    /// juego y que el archivo sea válido; respalda la base actual y vuelve a ella si algo falla.
    /// </summary>
    public async Task<BackupResult> RestoreAsync(string path, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var preview = await PreviewRestoreAsync(path, ct).ConfigureAwait(false);
            if (!preview.CanRestore)
            {
                return new BackupResult(CommandOutcome.Rejected, preview.Message);
            }

            var outcome = await Task.Run(() => _store.RestoreAsync(path, ct), ct).ConfigureAwait(false);
            _log.Info($"Respaldo restaurado desde {System.IO.Path.GetFileName(path)}; base anterior guardada en {outcome.SafetyBackupPath}.");
            var migrated = outcome.RestoredSchemaVersion < outcome.SchemaVersion
                ? $" Se actualizó del esquema v{outcome.RestoredSchemaVersion} al v{outcome.SchemaVersion}."
                : string.Empty;
            return new BackupResult(
                CommandOutcome.Applied,
                $"Respaldo restaurado.{migrated} La base anterior quedó guardada en «{System.IO.Path.GetFileName(outcome.SafetyBackupPath)}».",
                outcome.SafetyBackupPath);
        }
        catch (RestoreFailedException ex)
        {
            _log.Error($"La restauración falló (base anterior en uso: {ex.RolledBack}).", ex);
            return new BackupResult(CommandOutcome.Failed, ex.Message, ex.SafetyBackupPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error("La restauración falló.", ex);
            return new BackupResult(CommandOutcome.Failed, "No se pudo restaurar el respaldo. La base en uso no se modificó.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private string? Blocker(BackupInspection inspection)
    {
        if (!inspection.IsValid)
        {
            return inspection.Problem;
        }

        if (_profile == AppProfile.Produccion && inspection.ContainsDemoData)
        {
            return "El respaldo contiene datos de demostración y no se restaura en el perfil de producción. Ábralo en el perfil demo.";
        }

        return null;
    }

    private async Task<string?> InProgressBlockerAsync(CancellationToken ct) =>
        await _matches.FindInProgressMatchIdAsync(ct).ConfigureAwait(false) is null
            ? null
            : "Hay un partido en juego. Finalícelo antes de restaurar: no se restaura en pleno partido.";

    private static string Describe(BackupInspection i)
    {
        var text =
            $"Contiene {Count(i.Tournaments, "evento", "eventos")}, {Count(i.Teams, "equipo", "equipos")}, " +
            $"{Count(i.Matches, "partido", "partidos")} y {Count(i.Incidents, "incidencia", "incidencias")} (esquema v{i.SchemaVersion}).";
        if (i.SchemaVersion < i.SupportedSchemaVersion)
        {
            text += $" Al restaurarlo se actualizará al esquema v{i.SupportedSchemaVersion}.";
        }

        if (i.MatchesInProgress > 0)
        {
            text += " Trae un partido en juego: la mesa lo recuperará al restaurar.";
        }

        return text;
    }

    private static string Count(int value, string one, string many) => value == 1 ? $"1 {one}" : $"{value} {many}";
}
