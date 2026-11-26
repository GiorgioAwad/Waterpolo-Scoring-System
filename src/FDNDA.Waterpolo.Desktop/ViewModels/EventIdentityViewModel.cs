using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FDNDA.Waterpolo.Application.Identity;
using FDNDA.Waterpolo.Domain.Competitions;

namespace FDNDA.Waterpolo.Desktop.ViewModels;

/// <summary>Auspiciador en el editor: nombre editable e imagen ya validada.</summary>
public sealed partial class SponsorItem(string name, ImageAsset image) : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = name;

    public ImageAsset Image { get; } = image;

    public ImageData Preview { get; } = new(image.Id, image.Content);

    public string Description => Image.Description;
}

/// <summary>
/// Diálogo «Identidad y plantilla» de un evento (RF01, RF08): título y subtítulo del marcador,
/// colores, logo del evento, logo de la organización y auspiciadores. Las imágenes se validan al
/// elegirlas, pero nada se guarda hasta pulsar «Guardar identidad». La plantilla de ejemplo del
/// Panamericano Sub-17 2027 llena el formulario con recursos genéricos y no oficiales.
/// </summary>
public sealed partial class EventIdentityViewModel : ObservableObject
{
    public const string DefaultPrimaryColor = MarcadorViewModel.DefaultHeaderColor;

    public const string DefaultAccentColor = MarcadorViewModel.DefaultAccentColor;

    private readonly IdentityOperations _identity;
    private readonly ICatalogDialogs _dialogs;
    private readonly Guid _tournamentId;

    public EventIdentityViewModel(
        IdentityOperations identity, ICatalogDialogs dialogs, Guid tournamentId, string tournamentName, EventBranding? current)
    {
        _identity = identity;
        _dialogs = dialogs;
        _tournamentId = tournamentId;
        TournamentName = tournamentName;
        HasSavedIdentity = current is not null;
        Load(current is null
            ? new EventBrandingDraft(tournamentName, null, null, null, null, null, [])
            : EventBrandingDraft.From(current));
        StatusMessage = current is null
            ? "El evento aún no tiene identidad propia: el marcador muestra el nombre del evento con los colores predeterminados."
            : string.Empty;
    }

    /// <summary>El operador cerró el diálogo: verdadero si se guardó o quitó la identidad.</summary>
    public event EventHandler<bool>? CloseRequested;

    public string TournamentName { get; }

    public string Title => $"Identidad y plantilla — {TournamentName}";

    public bool HasSavedIdentity { get; }

    public static string LimitsNote =>
        $"Imágenes {ImageRules.AcceptedFormatsText}, hasta {ImageInspector.SizeText(ImageRules.MaxBytes)} y entre {ImageRules.MinSidePixels} y {ImageRules.MaxSidePixels} px por lado. " +
        $"Hasta {ImageRules.MaxSponsors} auspiciadores. Las imágenes se guardan dentro de la base y viajan en el respaldo.";

    public static string OfficialNote =>
        "Use solo logos y marcas que la FDNDA o el organizador hayan entregado. La plantilla de ejemplo trae recursos genéricos, no oficiales.";

    public ObservableCollection<SponsorItem> Sponsors { get; } = [];

    [ObservableProperty]
    public partial string DisplayTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Subtitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PrimaryColor { get; set; } = DefaultPrimaryColor;

    [ObservableProperty]
    public partial string AccentColor { get; set; } = DefaultAccentColor;

    [ObservableProperty]
    public partial ImageAsset? Logo { get; set; }

    [ObservableProperty]
    public partial ImageAsset? OrganizerLogo { get; set; }

    [ObservableProperty]
    public partial SponsorItem? SelectedSponsor { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsStatusError { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public ImageData? LogoPreview => Preview(Logo);

    public ImageData? OrganizerLogoPreview => Preview(OrganizerLogo);

    public string LogoText => Logo?.Description ?? "Sin logo del evento";

    public string OrganizerLogoText => OrganizerLogo?.Description ?? "Sin logo de la organización (identidad FDNDA)";

    public string SponsorsTitle => $"Auspiciadores ({Sponsors.Count} de {ImageRules.MaxSponsors}), en el orden en que se muestran";

    /// <summary>Mensaje del guardado, para la barra de la pestaña.</summary>
    public string? ResultMessage { get; private set; }

    // ---------- Imágenes ----------

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task LoadLogoAsync()
    {
        if (await PickAsync("Logo del evento", AssetKind.EventLogo) is { } image)
        {
            Logo = image;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void RemoveLogo() => Logo = null;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task LoadOrganizerLogoAsync()
    {
        if (await PickAsync("Logo de la organización", AssetKind.EventLogo) is { } image)
        {
            OrganizerLogo = image;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void RemoveOrganizerLogo() => OrganizerLogo = null;

    [RelayCommand(CanExecute = nameof(CanAddSponsor))]
    private async Task AddSponsorAsync()
    {
        if (await PickAsync("Auspiciador", AssetKind.Sponsor) is { } image)
        {
            var item = new SponsorItem(System.IO.Path.GetFileNameWithoutExtension(image.FileName), image);
            Sponsors.Add(item);
            SelectedSponsor = item;
            SponsorsChanged();
        }
    }

    private bool CanAddSponsor() => !IsBusy && Sponsors.Count < ImageRules.MaxSponsors;

    [RelayCommand(CanExecute = nameof(HasSelectedSponsor))]
    private void RemoveSponsor()
    {
        if (SelectedSponsor is { } item)
        {
            var index = Sponsors.IndexOf(item);
            Sponsors.Remove(item);
            SelectedSponsor = Sponsors.Count == 0 ? null : Sponsors[Math.Min(index, Sponsors.Count - 1)];
            SponsorsChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSponsor))]
    private void MoveSponsorUp() => MoveSelected(-1);

    [RelayCommand(CanExecute = nameof(HasSelectedSponsor))]
    private void MoveSponsorDown() => MoveSelected(+1);

    private bool HasSelectedSponsor() => !IsBusy && SelectedSponsor is not null;

    // ---------- Colores y plantilla ----------

    [RelayCommand]
    private void ChoosePrimaryColor() => PrimaryColor = _dialogs.PickColor(PrimaryColor) ?? PrimaryColor;

    [RelayCommand]
    private void ChooseAccentColor() => AccentColor = _dialogs.PickColor(AccentColor) ?? AccentColor;

    /// <summary>Llena el formulario con la plantilla de ejemplo. No guarda hasta «Guardar identidad».</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task UseExampleAsync()
    {
        if ((Logo is not null || OrganizerLogo is not null || Sponsors.Count > 0)
            && !_dialogs.Confirm(
                "Plantilla de ejemplo",
                "La plantilla de ejemplo reemplaza el título, los colores, los logos y los auspiciadores del formulario.\n\n¿Continuar? Nada se guarda hasta pulsar «Guardar identidad»."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var example = await _identity.LoadExampleAsync();
            if (example.Draft is null)
            {
                SetStatus(example.Message ?? "No se pudo cargar la plantilla de ejemplo.", isError: true);
                return;
            }

            Load(example.Draft);
            SetStatus("Plantilla de ejemplo cargada (recursos genéricos, no oficiales). Revísela y pulse «Guardar identidad».", isError: false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void ResetColors()
    {
        PrimaryColor = DefaultPrimaryColor;
        AccentColor = DefaultAccentColor;
    }

    // ---------- Guardar ----------

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task SaveAsync()
    {
        var draft = new EventBrandingDraft(
            DisplayTitle,
            Subtitle,
            PrimaryColor,
            AccentColor,
            Logo,
            OrganizerLogo,
            [.. Sponsors.Select(s => new SponsorDraft(s.Name, s.Image))]);

        IsBusy = true;
        try
        {
            var result = await _identity.SaveEventBrandingAsync(_tournamentId, draft);
            if (!result.IsSuccess)
            {
                SetStatus(result.Message ?? "No se pudo guardar la identidad.", isError: true);
                return;
            }

            ResultMessage = result.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus($"Error inesperado: {ex.Message}", isError: true);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        CloseRequested?.Invoke(this, true);
    }

    /// <summary>Quita la identidad guardada: el marcador vuelve a la presentación predeterminada.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveIdentity))]
    private async Task RemoveIdentityAsync()
    {
        if (!_dialogs.Confirm(
            "Quitar identidad",
            $"¿Quitar la identidad de «{TournamentName}»?\n\nEl marcador volverá a mostrar el nombre del evento con los colores predeterminados. Los partidos y resultados no cambian."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _identity.RemoveEventBrandingAsync(_tournamentId);
            if (!result.IsSuccess)
            {
                SetStatus(result.Message ?? "No se pudo quitar la identidad.", isError: true);
                return;
            }

            ResultMessage = result.Message;
        }
        finally
        {
            IsBusy = false;
        }

        CloseRequested?.Invoke(this, true);
    }

    private bool CanRemoveIdentity() => !IsBusy && HasSavedIdentity;

    [RelayCommand]
    private void Cancel()
    {
        if (!IsBusy)
        {
            CloseRequested?.Invoke(this, false);
        }
    }

    private bool IsIdle() => !IsBusy;

    private async Task<ImageAsset?> PickAsync(string what, AssetKind kind)
    {
        var path = _dialogs.PickImageFile(what);
        if (path is null)
        {
            return null;
        }

        IsBusy = true;
        try
        {
            var loaded = await _identity.LoadImageAsync(path, kind);
            if (loaded.Image is null)
            {
                SetStatus(loaded.Message ?? "No se pudo cargar la imagen.", isError: true);
                return null;
            }

            SetStatus($"{what}: {loaded.Image.Description}. Se guardará al pulsar «Guardar identidad».", isError: false);
            return loaded.Image;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Load(EventBrandingDraft draft)
    {
        DisplayTitle = draft.Title;
        Subtitle = draft.Subtitle ?? string.Empty;
        PrimaryColor = draft.PrimaryColorHex ?? DefaultPrimaryColor;
        AccentColor = draft.AccentColorHex ?? DefaultAccentColor;
        Logo = draft.Logo;
        OrganizerLogo = draft.OrganizerLogo;
        Sponsors.Clear();
        foreach (var sponsor in draft.Sponsors)
        {
            Sponsors.Add(new SponsorItem(sponsor.Name, sponsor.Image));
        }

        SelectedSponsor = Sponsors.FirstOrDefault();
        SponsorsChanged();
    }

    private void MoveSelected(int delta)
    {
        if (SelectedSponsor is not { } item)
        {
            return;
        }

        var index = Sponsors.IndexOf(item);
        var target = index + delta;
        if (target >= 0 && target < Sponsors.Count)
        {
            Sponsors.Move(index, target);
            SelectedSponsor = item;
        }
    }

    private void SponsorsChanged()
    {
        OnPropertyChanged(nameof(SponsorsTitle));
        AddSponsorCommand.NotifyCanExecuteChanged();
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
    }

    private static ImageData? Preview(ImageAsset? image) => image is null ? null : new ImageData(image.Id, image.Content);

    partial void OnLogoChanged(ImageAsset? value)
    {
        OnPropertyChanged(nameof(LogoPreview));
        OnPropertyChanged(nameof(LogoText));
    }

    partial void OnOrganizerLogoChanged(ImageAsset? value)
    {
        OnPropertyChanged(nameof(OrganizerLogoPreview));
        OnPropertyChanged(nameof(OrganizerLogoText));
    }

    partial void OnSelectedSponsorChanged(SponsorItem? value)
    {
        RemoveSponsorCommand.NotifyCanExecuteChanged();
        MoveSponsorUpCommand.NotifyCanExecuteChanged();
        MoveSponsorDownCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        LoadLogoCommand.NotifyCanExecuteChanged();
        RemoveLogoCommand.NotifyCanExecuteChanged();
        LoadOrganizerLogoCommand.NotifyCanExecuteChanged();
        RemoveOrganizerLogoCommand.NotifyCanExecuteChanged();
        AddSponsorCommand.NotifyCanExecuteChanged();
        RemoveSponsorCommand.NotifyCanExecuteChanged();
        MoveSponsorUpCommand.NotifyCanExecuteChanged();
        MoveSponsorDownCommand.NotifyCanExecuteChanged();
        UseExampleCommand.NotifyCanExecuteChanged();
        ResetColorsCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        RemoveIdentityCommand.NotifyCanExecuteChanged();
    }
}
