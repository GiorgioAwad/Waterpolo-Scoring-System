using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FDNDA.Waterpolo.Application.Catalog;

namespace FDNDA.Waterpolo.Desktop.ViewModels;

/// <summary>Diálogos del catálogo: elegir el archivo, mostrar la vista previa, editar reglas y confirmar acciones.</summary>
public interface ICatalogDialogs
{
    /// <summary>Ruta del archivo elegido; nula si el operador cancela.</summary>
    string? PickRosterFile();

    /// <summary>Muestra la vista previa. Verdadero si el operador confirmó y la importación se guardó.</summary>
    Task<bool> ShowRosterImportAsync(RosterImportViewModel preview);

    /// <summary>Muestra el editor de reglas del evento. Verdadero si las reglas se guardaron.</summary>
    Task<bool> ShowRulesEditorAsync(RulesEditorViewModel editor);

    bool Confirm(string title, string message);

    /// <summary>Pide un motivo obligatorio; nulo si el operador cancela.</summary>
    string? AskReason(string title, string message) => null;

    /// <summary>Selector de color de Windows. Devuelve <c>#RRGGBB</c>, o nulo si el operador cancela.</summary>
    string? PickColor(string? currentHex);

    /// <summary>Ruta de una imagen PNG o JPEG elegida; nula si el operador cancela.</summary>
    string? PickImageFile(string title) => null;

    /// <summary>Muestra el editor de identidad del evento. Verdadero si se guardó o quitó la identidad.</summary>
    Task<bool> ShowEventIdentityAsync(EventIdentityViewModel editor) => Task.FromResult(false);
}

/// <summary>Opción de una lista de decisiones, con el texto que ve el operador.</summary>
public sealed record ResolutionOption(RosterResolution Value, string Text);

/// <summary>Fila de la vista previa. Las filas ambiguas exigen elegir una opción.</summary>
public sealed partial class RosterImportRowItem : ObservableObject
{
    private readonly Action _changed;

    public RosterImportRowItem(RosterPreviewRow row, Action changed)
    {
        Source = row;
        _changed = changed;
        Options = [.. row.Options.Select(o => new ResolutionOption(o, Describe(row, o)))];
    }

    public RosterPreviewRow Source { get; }

    public int Row => Source.Row;

    public string Team => Source.TeamCode;

    public string Player => Source.PlayerName;

    public int Cap => Source.Cap;

    public string StatusText => Source.Status switch
    {
        RosterRowStatus.New => "Nuevo",
        RosterRowStatus.Unchanged => "Sin cambios",
        _ => SelectedOption is null ? "Decidir" : "Decidido",
    };

    public bool NeedsDecision => Source.Status == RosterRowStatus.Ambiguous;

    public string Detail => Source.Detail;

    public IReadOnlyList<ResolutionOption> Options { get; }

    [ObservableProperty]
    public partial ResolutionOption? SelectedOption { get; set; }

    partial void OnSelectedOptionChanged(ResolutionOption? value)
    {
        OnPropertyChanged(nameof(StatusText));
        _changed();
    }

    private static string Describe(RosterPreviewRow row, RosterResolution resolution) => resolution switch
    {
        RosterResolution.RenameExisting => "Mismo jugador: corregir su nombre",
        RosterResolution.ReplaceExisting => $"Otro jugador: reemplaza a «{row.Existing?.PlayerName}»",
        RosterResolution.MoveExistingCap when row.Target(resolution) is { } mover =>
            $"Mismo jugador: «{mover.PlayerName}» pasa del gorro {mover.Cap} al {row.Cap}",
        RosterResolution.AddAsNew => "Otro jugador con el mismo nombre: agregarlo",
        RosterResolution.Skip => "Omitir esta fila",
        _ => resolution.ToString(),
    };
}

/// <summary>
/// Vista previa de la importación de una nómina. Nada se guarda hasta pulsar «Importar»; cerrar
/// o cancelar descarta la vista previa. Las filas ambiguas no tienen decisión por defecto.
/// </summary>
public sealed partial class RosterImportViewModel : ObservableObject
{
    private readonly CatalogOperations _catalog;

    public RosterImportViewModel(CatalogOperations catalog, RosterPreview preview)
    {
        _catalog = catalog;
        Preview = preview;
        Rows = [.. preview.Rows.Select(r => new RosterImportRowItem(r, Reevaluate))];
        Errors = [.. preview.Errors.Select(e => e.ToString())];
        Notices = preview.Notices;
        Reevaluate();
    }

    /// <summary>Se pide cerrar la ventana: verdadero si se importó.</summary>
    public event EventHandler<bool>? CloseRequested;

    public RosterPreview Preview { get; }

    public string Title => $"Importar nómina — {Preview.TournamentName}";

    public string Heading => $"Archivo «{Preview.FileName}» para el evento «{Preview.TournamentName}»";

    public IReadOnlyList<RosterImportRowItem> Rows { get; }

    public IReadOnlyList<string> Errors { get; }

    public IReadOnlyList<string> Notices { get; }

    public bool HasErrors => Errors.Count > 0;

    public bool HasNotices => Notices.Count > 0;

    public string CountsText => HasErrors
        ? $"{Errors.Count} {(Errors.Count == 1 ? "error" : "errores")}: corrija el archivo y vuelva a abrirlo. No se importará nada."
        : $"{Preview.Count(RosterRowStatus.New)} nuevos · {Preview.Count(RosterRowStatus.Unchanged)} sin cambios · {Preview.Count(RosterRowStatus.Ambiguous)} por decidir";

    /// <summary>Lo que hará la importación o lo que falta para poder confirmar.</summary>
    [ObservableProperty]
    public partial string PlanText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsPlanReady { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Mensaje del resultado tras importar.</summary>
    public string? ResultMessage { get; private set; }

    public Dictionary<int, RosterResolution> Choices() =>
        Rows.Where(r => r.SelectedOption is not null).ToDictionary(r => r.Row, r => r.SelectedOption!.Value);

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        if (IsBusy)
        {
            return; // bloquea el doble envío
        }

        IsBusy = true;
        var imported = false;
        try
        {
            var result = await _catalog.ConfirmRosterImportAsync(Preview, Choices());
            imported = result.IsSuccess;
            if (imported)
            {
                ResultMessage = result.Message;
            }
            else
            {
                StatusMessage = result.Message ?? "No se pudo importar la nómina.";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = $"Error inesperado: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        if (imported)
        {
            CloseRequested?.Invoke(this, true); // después de soltar IsBusy: la ventana no se cierra mientras guarda
        }
    }

    private bool CanImport() => !IsBusy && IsPlanReady;

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    partial void OnIsBusyChanged(bool value) => ImportCommand.NotifyCanExecuteChanged();

    partial void OnIsPlanReadyChanged(bool value) => ImportCommand.NotifyCanExecuteChanged();

    private void Reevaluate()
    {
        var plan = RosterImportPlanner.Plan(Preview, Choices());
        if (!plan.IsReady)
        {
            IsPlanReady = false;
            PlanText = string.Join(" ", plan.Problems);
        }
        else if (!plan.Changes!.Summary.HasChanges)
        {
            IsPlanReady = false;
            PlanText = "Nada que importar: la nómina ya está al día con este archivo.";
        }
        else
        {
            IsPlanReady = true;
            PlanText = $"Al importar: {plan.Changes.Summary}.";
        }
    }
}
