using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Backups;
using FDNDA.Waterpolo.Desktop.Presentation;

namespace FDNDA.Waterpolo.Desktop.ViewModels;

/// <summary>Diálogos de la sección de respaldos.</summary>
public interface IBackupDialogs
{
    /// <summary>Ruta del respaldo elegido (por ejemplo en una memoria USB); nula si el operador cancela.</summary>
    string? PickBackupFile(string initialDirectory);

    bool Confirm(string title, string message);

    void OpenFolder(string path);
}

/// <summary>Respaldo en la lista de la pestaña.</summary>
public sealed record BackupItem(string Path, string FileName, string ReasonText, string CreatedText, string SizeText)
{
    public static BackupItem From(BackupFile file) => new(
        file.Path,
        file.FileName,
        Reason(file.Reason),
        MatchTexts.LocalTime(file.CreatedAtUtc),
        Size(file.SizeBytes));

    public static string Reason(BackupReason reason) => reason switch
    {
        BackupReason.Manual => "Manual",
        BackupReason.BeforeMatch => "Antes del partido",
        BackupReason.AfterMatch => "Al finalizar el partido",
        BackupReason.BeforeRestore => "Base anterior a una restauración",
        BackupReason.BeforeMigration => "Antes de actualizar el programa",
        _ => "Otro",
    };

    private static string Size(long bytes) => bytes < 1024 * 1024
        ? $"{Math.Max(1, bytes / 1024)} KB"
        : (bytes / (1024d * 1024d)).ToString("0.0", MatchTexts.Culture) + " MB";
}

/// <summary>
/// Sección «Respaldos» de la pestaña «Resultados y respaldos» (RF10): crear un respaldo, ver los
/// existentes, abrir la carpeta y restaurar uno de la lista o de otro archivo. La mesa lo usa para
/// los respaldos automáticos antes de iniciar y al finalizar un partido.
/// </summary>
public sealed partial class BackupsViewModel : ObservableObject
{
    private readonly BackupOperations _backups;
    private readonly IBackupDialogs? _dialogs;

    public BackupsViewModel(BackupOperations backups, IBackupDialogs? dialogs = null)
    {
        _backups = backups;
        _dialogs = dialogs;
    }

    /// <summary>
    /// Recarga partidos, eventos y nóminas después de restaurar; la mesa la asigna. Se espera
    /// antes de dar por terminada la restauración.
    /// </summary>
    public Func<Task>? ReloadAfterRestore { get; set; }

    public ObservableCollection<BackupItem> Items { get; } = [];

    public string FolderText => _backups.BackupDirectory;

    [ObservableProperty]
    public partial BackupItem? SelectedBackup { get; set; }

    /// <summary>Respaldar automáticamente antes de iniciar y al finalizar cada partido.</summary>
    [ObservableProperty]
    public partial bool AutoBackupEnabled { get; set; } = true;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsStatusError { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public string ListTitle => Items.Count == 1 ? "1 respaldo en la carpeta" : $"{Items.Count} respaldos en la carpeta";

    public Task InitializeAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var selected = SelectedBackup?.Path;
        try
        {
            var files = await _backups.ListAsync();
            Items.Clear();
            foreach (var file in files)
            {
                Items.Add(BackupItem.From(file));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Items.Clear();
            SetStatus("No se pudo leer la carpeta de respaldos.", isError: true);
        }

        SelectedBackup = Items.FirstOrDefault(i => i.Path == selected);
        OnPropertyChanged(nameof(ListTitle));
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task CreateBackupAsync()
    {
        IsBusy = true;
        try
        {
            SetStatus("Creando respaldo…", isError: false);
            var result = await _backups.CreateAsync(BackupReason.Manual);
            SetStatus(result.Message, !result.IsSuccess);
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
    }

    /// <summary>
    /// Respaldo automático de la mesa. Devuelve nulo si los respaldos automáticos están
    /// desactivados; si no, el resultado (correcto o fallido).
    /// </summary>
    public async Task<BackupResult?> CreateAutomaticAsync(BackupReason reason)
    {
        if (!AutoBackupEnabled)
        {
            return null;
        }

        var result = await _backups.CreateAsync(reason);
        SetStatus(result.Message, !result.IsSuccess);
        await RefreshAsync();
        return result;
    }

    [RelayCommand(CanExecute = nameof(CanRestoreSelected))]
    private Task RestoreSelectedAsync() => SelectedBackup is { } item ? RestoreAsync(item.Path) : Task.CompletedTask;

    private bool CanRestoreSelected() => CanAct() && SelectedBackup is not null;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task RestoreFromFileAsync()
    {
        var path = _dialogs?.PickBackupFile(_backups.BackupDirectory);
        if (path is not null)
        {
            await RestoreAsync(path);
        }
    }

    [RelayCommand]
    private void OpenFolder() => _dialogs?.OpenFolder(_backups.BackupDirectory);

    private bool CanAct() => !IsBusy;

    private async Task RestoreAsync(string path)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            SetStatus("Revisando el respaldo…", isError: false);
            var preview = await _backups.PreviewRestoreAsync(path);
            if (!preview.CanRestore)
            {
                SetStatus($"No se puede restaurar «{System.IO.Path.GetFileName(path)}»: {preview.Message}", isError: true);
                return;
            }

            var question =
                $"¿Restaurar «{System.IO.Path.GetFileName(path)}»?\n\n{preview.Message}\n\n" +
                "Los datos actuales se reemplazarán por los del respaldo. Antes se guardará una copia de la base actual en la carpeta de respaldos.";
            if (_dialogs is not null && !_dialogs.Confirm("Restaurar respaldo", question))
            {
                SetStatus("Restauración cancelada: no se cambió nada.", isError: false);
                return;
            }

            SetStatus("Restaurando…", isError: false);
            var result = await _backups.RestoreAsync(path);
            SetStatus(result.Message, !result.IsSuccess);
            if (result.IsSuccess && ReloadAfterRestore is { } reload)
            {
                await reload();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus($"Error inesperado: {ex.Message}", isError: true);
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
    }

    partial void OnSelectedBackupChanged(BackupItem? value) => RestoreSelectedCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value)
    {
        CreateBackupCommand.NotifyCanExecuteChanged();
        RestoreSelectedCommand.NotifyCanExecuteChanged();
        RestoreFromFileCommand.NotifyCanExecuteChanged();
    }
}
