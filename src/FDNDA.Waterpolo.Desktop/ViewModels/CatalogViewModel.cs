using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Application.Identity;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Desktop.Presentation;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;
using FDNDA.Waterpolo.Domain.Matches;

namespace FDNDA.Waterpolo.Desktop.ViewModels;

/// <summary>Evento en la lista del catálogo, con sus reglas vigentes.</summary>
public sealed record TournamentItem(Guid TournamentId, string Name, RulesetSnapshot Rules, int MatchCount, int ScheduledMatchCount)
{
    public string RulesPresetName => Rules.PresetName;

    public string RulesText => $"Reglas: {Rules.PresetName}. {RulesTexts.Summary(Rules)}.";

    public static TournamentItem From(TournamentSummary summary) => new(
        summary.TournamentId,
        summary.Name,
        summary.Rules,
        summary.MatchCount,
        summary.ScheduledMatchCount);
}

/// <summary>Color de la paleta rápida, con su nombre para la ayuda y la automatización.</summary>
public sealed record ColorSwatch(string Name, string Hex);

/// <summary>Equipo en la lista del catálogo.</summary>
public sealed record TeamItem(Guid TeamId, string Code, string Name, string ColorHex, string CapColor)
{
    public string Label => $"{Code} · {Name}";

    public static TeamItem From(Team team) => new(team.Id, team.Code, team.Name, team.ColorHex, team.CapColor);
}

/// <summary>
/// Pestaña «Eventos y nóminas» y formulario de programación de la pestaña «Partidos».
/// Llama a los casos de uso del catálogo y a <see cref="MatchOperations.ScheduleMatchAsync"/>;
/// no escribe SQL. Las altas usan un identificador generado al abrir el formulario, de modo
/// que un doble envío no crea dos registros. La nómina se edita a mano o se importa desde un
/// archivo con vista previa (<see cref="RosterImportViewModel"/>).
/// </summary>
public sealed partial class CatalogViewModel : ObservableObject
{
    public const string DateFormat = "dd/MM/yyyy HH:mm";

    private readonly CatalogOperations _catalog;
    private readonly MatchOperations _matches;
    private readonly ICatalogDialogs? _dialogs;
    private readonly IdentityOperations? _identity;

    private Guid _pendingTournamentId = Guid.NewGuid();
    private Guid _pendingTeamId = Guid.NewGuid();
    private Guid _pendingMatchId = Guid.NewGuid();
    private Guid _pendingEntryId = Guid.NewGuid();
    private Guid _pendingPlayerId = Guid.NewGuid();

    /// <param name="identity">Escudos, banderas e identidad del evento (R7); sin él esas acciones no están disponibles.</param>
    public CatalogViewModel(CatalogOperations catalog, MatchOperations matches, ICatalogDialogs? dialogs = null, IdentityOperations? identity = null)
    {
        _catalog = catalog;
        _matches = matches;
        _dialogs = dialogs;
        _identity = identity;
    }

    /// <summary>Se programó un partido nuevo (identificador del partido).</summary>
    public event EventHandler<Guid>? MatchScheduled;

    /// <summary>Se reprogramó, se actualizó la nómina o se anuló un partido (identificador del partido).</summary>
    public event EventHandler<Guid>? MatchChanged;

    /// <summary>Operador que firma reprogramaciones y anulaciones; sin él esas acciones no están disponibles.</summary>
    public OperatorRef? Operator { get; init; }

    /// <summary>Lectura del partido elegido para cargarlo en el formulario al reprogramar.</summary>
    public MatchQueries? Queries { get; init; }

    public ObservableCollection<TournamentItem> Tournaments { get; } = [];

    public ObservableCollection<TeamItem> Teams { get; } = [];

    public ObservableCollection<TournamentRosterLine> RosterLines { get; } = [];

    /// <summary>Colores frecuentes de equipos y selecciones; un clic los aplica.</summary>
    public static IReadOnlyList<ColorSwatch> Palette { get; } =
    [
        new("Blanco", "#FFFFFF"), new("Negro", "#212121"), new("Gris", "#757575"), new("Rojo", "#C62828"),
        new("Granate", "#7B1E2B"), new("Naranja", "#EF6C00"), new("Amarillo", "#F9A825"), new("Dorado", "#B8860B"),
        new("Verde", "#2E7D32"), new("Turquesa", "#00897B"), new("Celeste", "#4FC3F7"), new("Azul", "#0B63CE"),
        new("Azul marino", "#0D1F4F"), new("Morado", "#6A1B9A"), new("Rosa", "#D81B60"), new("Vinotinto", "#5D1A2B"),
    ];

    /// <summary>Colores de gorro frecuentes; el campo admite cualquier otro texto.</summary>
    public static IReadOnlyList<string> CapColorSuggestions { get; } = ["Blanco", "Azul", "Negro", "Rojo", "Amarillo", "Verde"];

    // ---------- Eventos ----------

    [ObservableProperty]
    public partial TournamentItem? SelectedTournament { get; set; }

    [ObservableProperty]
    public partial string TournamentName { get; set; } = string.Empty;

    public string TournamentFormTitle => SelectedTournament is null ? "Nuevo evento" : "Editar evento";

    public string TournamentRulesText => SelectedTournament?.RulesText
        ?? "Un evento nuevo nace con el preset DEMO (no reglamentario); después puede editar sus reglas con «Reglas del evento…».";

    // ---------- Equipos ----------

    [ObservableProperty]
    public partial TeamItem? SelectedTeam { get; set; }

    [ObservableProperty]
    public partial string TeamCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TeamName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TeamColor { get; set; } = "#0B63CE";

    [ObservableProperty]
    public partial string TeamCapColor { get; set; } = string.Empty;

    public string TeamFormTitle => SelectedTeam is null ? "Nuevo equipo" : "Editar equipo";

    /// <summary>Escudo del equipo elegido (vista previa).</summary>
    [ObservableProperty]
    public partial ImageData? TeamCrest { get; set; }

    [ObservableProperty]
    public partial ImageData? TeamFlag { get; set; }

    [ObservableProperty]
    public partial string TeamCrestText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TeamFlagText { get; set; } = string.Empty;

    public bool CanEditTeamImages => SelectedTeam is not null && _identity is not null && _dialogs is not null;

    public string TeamImagesHint => SelectedTeam is null
        ? "Guarde o elija un equipo para asignarle escudo y bandera."
        : "PNG o JPEG, hasta 2 MB. Se ven en la mesa y en el marcador, también en partidos ya programados.";

    public string RosterTitle => SelectedTournament is null || SelectedTeam is null
        ? "Nómina: elija un evento y un equipo"
        : $"Nómina de {SelectedTeam.Code} en «{SelectedTournament.Name}» · {RosterLines.Count} jugadores";

    // ---------- Nómina ----------

    [ObservableProperty]
    public partial TournamentRosterLine? SelectedRosterLine { get; set; }

    [ObservableProperty]
    public partial string RosterPlayerName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RosterCapText { get; set; } = string.Empty;

    public bool CanEditRoster => SelectedTournament is not null && SelectedTeam is not null;

    public string RosterFormTitle => SelectedRosterLine is null ? "Agregar jugador" : $"Editar jugador (gorro {SelectedRosterLine.Cap})";

    public string ImportHint => SelectedTournament is null
        ? "Elija el evento para importar su nómina."
        : $"Importa al evento «{SelectedTournament.Name}» todos los equipos del archivo (CSV UTF-8 o XLSX).";

    // ---------- Programación ----------

    [ObservableProperty]
    public partial TournamentItem? ScheduleTournament { get; set; }

    [ObservableProperty]
    public partial string ScheduleLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ScheduleDateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial TeamItem? ScheduleHome { get; set; }

    [ObservableProperty]
    public partial TeamItem? ScheduleAway { get; set; }

    [ObservableProperty]
    public partial string ScheduleHomeColor { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ScheduleHomeCap { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ScheduleAwayColor { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ScheduleAwayCap { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ScheduleRosterInfo { get; set; } = string.Empty;

    /// <summary>Partido elegido en la lista de la pestaña «Partidos» (lo fija la mesa).</summary>
    [ObservableProperty]
    public partial MatchListItem? ListedMatch { get; set; }

    /// <summary>Verdadero mientras el formulario edita un partido programado en vez de crear uno.</summary>
    [ObservableProperty]
    public partial bool IsRescheduling { get; set; }

    private Guid? _reschedulingMatchId;
    private int? _reschedulingVersion;
    private string _reschedulingLabel = string.Empty;

    public string ScheduleFormTitle => IsRescheduling ? $"Reprogramar «{_reschedulingLabel}»" : "Programar partido";

    public string ScheduleButtonText => IsRescheduling ? "Guardar reprogramación" : "Programar partido";

    /// <summary>Evento y equipos solo se eligen al programar; al reprogramar quedan fijos.</summary>
    public bool CanChooseScheduleTeams => !IsRescheduling;

    public string ListedMatchHint => ListedMatch switch
    {
        null => "Elija un partido de la lista.",
        { IsScheduled: true } => "Un partido aún no iniciado se puede reprogramar, actualizar su nómina desde el evento o anular.",
        { IsCancelled: true } => "Partido anulado: queda en la lista como registro.",
        _ => "Un partido iniciado o finalizado ya no se reprograma ni se anula.",
    };

    // ---------- Estado ----------

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsStatusError { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public async Task InitializeAsync() => await RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            var tournamentId = SelectedTournament?.TournamentId;
            var teamId = SelectedTeam?.TeamId;
            var scheduleTournamentId = ScheduleTournament?.TournamentId;
            var homeId = ScheduleHome?.TeamId;
            var awayId = ScheduleAway?.TeamId;

            // Recargar las listas vacía las selecciones de los combos, y elegir un equipo propone sus
            // colores del catálogo: lo escrito en el formulario se guarda y se repone.
            var typed = (HomeColor: ScheduleHomeColor, HomeCap: ScheduleHomeCap, AwayColor: ScheduleAwayColor, AwayCap: ScheduleAwayCap);

            Fill(Tournaments, (await _catalog.ListTournamentsAsync()).Select(TournamentItem.From));
            Fill(Teams, (await _catalog.ListTeamsAsync()).Select(TeamItem.From));

            SelectedTournament = Tournaments.FirstOrDefault(t => t.TournamentId == tournamentId);
            SelectedTeam = Teams.FirstOrDefault(t => t.TeamId == teamId);
            ScheduleTournament = Tournaments.FirstOrDefault(t => t.TournamentId == scheduleTournamentId) ?? Tournaments.FirstOrDefault();
            ScheduleHome = Teams.FirstOrDefault(t => t.TeamId == homeId);
            ScheduleAway = Teams.FirstOrDefault(t => t.TeamId == awayId);
            if (ScheduleHome is not null)
            {
                (ScheduleHomeColor, ScheduleHomeCap) = (typed.HomeColor, typed.HomeCap);
            }

            if (ScheduleAway is not null)
            {
                (ScheduleAwayColor, ScheduleAwayCap) = (typed.AwayColor, typed.AwayCap);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus("No se pudo leer el catálogo del almacenamiento local.", isError: true);
        }
    }

    // ---------- Comandos de eventos ----------

    [RelayCommand]
    private void NewTournament()
    {
        SelectedTournament = null;
        TournamentName = string.Empty;
        _pendingTournamentId = Guid.NewGuid();
    }

    [RelayCommand(CanExecute = nameof(CanSaveTournament))]
    private async Task SaveTournamentAsync()
    {
        var editing = SelectedTournament;
        var id = editing?.TournamentId ?? _pendingTournamentId;
        var result = await RunAsync(() => editing is null
            ? _catalog.CreateTournamentAsync(id, TournamentName)
            : _catalog.RenameTournamentAsync(id, TournamentName));

        if (result?.IsSuccess == true)
        {
            if (editing is null)
            {
                _pendingTournamentId = Guid.NewGuid();
            }

            await RefreshAsync();
            SelectedTournament = Tournaments.FirstOrDefault(t => t.TournamentId == id);
        }
    }

    private bool CanSaveTournament() => !IsBusy && !string.IsNullOrWhiteSpace(TournamentName);

    /// <summary>Abre el editor de reglas del evento elegido con sus reglas vigentes.</summary>
    [RelayCommand(CanExecute = nameof(CanEditRules))]
    private async Task EditRulesAsync()
    {
        if (_dialogs is null || SelectedTournament is not { } item || IsBusy)
        {
            return;
        }

        RulesEditorViewModel editor;
        try
        {
            // Se lee de nuevo: desde que se cargó la lista pudo iniciarse o programarse un partido.
            var current = (await _catalog.ListTournamentsAsync()).FirstOrDefault(t => t.TournamentId == item.TournamentId);
            if (current is null)
            {
                SetStatus("El evento ya no existe. Pulse «Actualizar».", isError: true);
                return;
            }

            editor = new RulesEditorViewModel(
                _catalog, current.TournamentId, current.Name, current.Rules, current.ScheduledMatchCount, current.MatchCount - current.ScheduledMatchCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus("No se pudieron leer las reglas del evento del almacenamiento local.", isError: true);
            return;
        }

        if (await _dialogs.ShowRulesEditorAsync(editor))
        {
            SetStatus(editor.ResultMessage ?? "Reglas guardadas.", isError: false);
            await RefreshAsync();
        }
        else
        {
            SetStatus("Edición de reglas cancelada: no se guardó nada.", isError: false);
        }
    }

    private bool CanEditRules() => !IsBusy && SelectedTournament is not null && _dialogs is not null;

    /// <summary>Abre el editor de identidad y plantilla del evento elegido (título, colores, logos, auspiciadores).</summary>
    [RelayCommand(CanExecute = nameof(CanEditIdentity))]
    private async Task EditIdentityAsync()
    {
        if (_dialogs is null || _identity is null || SelectedTournament is not { } item || IsBusy)
        {
            return;
        }

        EventIdentityViewModel editor;
        try
        {
            var current = await _identity.GetEventBrandingAsync(item.TournamentId);
            editor = new EventIdentityViewModel(_identity, _dialogs, item.TournamentId, item.Name, current);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus("No se pudo leer la identidad del evento del almacenamiento local.", isError: true);
            return;
        }

        SetStatus(
            await _dialogs.ShowEventIdentityAsync(editor)
                ? editor.ResultMessage ?? "Identidad del evento guardada."
                : "Edición de identidad cancelada: no se guardó nada.",
            isError: false);
    }

    private bool CanEditIdentity() => !IsBusy && SelectedTournament is not null && _dialogs is not null && _identity is not null;

    // ---------- Comandos de equipos ----------

    [RelayCommand]
    private void NewTeam()
    {
        SelectedTeam = null;
        TeamCode = string.Empty;
        TeamName = string.Empty;
        TeamColor = "#0B63CE";
        TeamCapColor = string.Empty;
        _pendingTeamId = Guid.NewGuid();
    }

    [RelayCommand(CanExecute = nameof(CanSaveTeam))]
    private async Task SaveTeamAsync()
    {
        var editing = SelectedTeam;
        var id = editing?.TeamId ?? _pendingTeamId;
        var result = await RunAsync(() => editing is null
            ? _catalog.CreateTeamAsync(id, TeamCode, TeamName, TeamColor, TeamCapColor)
            : _catalog.UpdateTeamAsync(id, TeamCode, TeamName, TeamColor, TeamCapColor));

        if (result?.IsSuccess == true)
        {
            if (editing is null)
            {
                _pendingTeamId = Guid.NewGuid();
            }

            await RefreshAsync();
            SelectedTeam = Teams.FirstOrDefault(t => t.TeamId == id);
        }
    }

    private bool CanSaveTeam() =>
        !IsBusy && !string.IsNullOrWhiteSpace(TeamCode) && !string.IsNullOrWhiteSpace(TeamName)
        && !string.IsNullOrWhiteSpace(TeamColor) && !string.IsNullOrWhiteSpace(TeamCapColor);

    // ---------- Colores ----------

    /// <summary>Aplica un color de la paleta al equipo del formulario.</summary>
    [RelayCommand]
    private void PickTeamColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            TeamColor = hex;
        }
    }

    [RelayCommand]
    private void ChooseTeamColor() => TeamColor = _dialogs?.PickColor(TeamColor) ?? TeamColor;

    [RelayCommand]
    private void ChooseHomeColor() => ScheduleHomeColor = _dialogs?.PickColor(ScheduleHomeColor) ?? ScheduleHomeColor;

    [RelayCommand]
    private void ChooseAwayColor() => ScheduleAwayColor = _dialogs?.PickColor(ScheduleAwayColor) ?? ScheduleAwayColor;

    // ---------- Escudo y bandera ----------

    [RelayCommand(CanExecute = nameof(CanChangeTeamImage))]
    private Task LoadTeamCrestAsync() => LoadTeamImageAsync(AssetKind.Crest);

    [RelayCommand(CanExecute = nameof(CanRemoveTeamCrest))]
    private Task RemoveTeamCrestAsync() => SetTeamImageAsync(AssetKind.Crest, null);

    [RelayCommand(CanExecute = nameof(CanChangeTeamImage))]
    private Task LoadTeamFlagAsync() => LoadTeamImageAsync(AssetKind.Flag);

    [RelayCommand(CanExecute = nameof(CanRemoveTeamFlag))]
    private Task RemoveTeamFlagAsync() => SetTeamImageAsync(AssetKind.Flag, null);

    private bool CanChangeTeamImage() => !IsBusy && CanEditTeamImages;

    private bool CanRemoveTeamCrest() => CanChangeTeamImage() && TeamCrest is not null;

    private bool CanRemoveTeamFlag() => CanChangeTeamImage() && TeamFlag is not null;

    private async Task LoadTeamImageAsync(AssetKind kind)
    {
        if (_dialogs is null || _identity is null || SelectedTeam is not { } team || IsBusy)
        {
            return;
        }

        var path = _dialogs.PickImageFile($"{(kind == AssetKind.Crest ? "Escudo" : "Bandera")} de {team.Code}");
        if (path is null)
        {
            return;
        }

        var loaded = await _identity.LoadImageAsync(path, kind);
        if (loaded.Image is null)
        {
            SetStatus(loaded.Message ?? "No se pudo cargar la imagen.", isError: true);
            return;
        }

        await SetTeamImageAsync(kind, loaded.Image);
    }

    private async Task SetTeamImageAsync(AssetKind kind, ImageAsset? image)
    {
        if (_identity is not { } identity || SelectedTeam is not { } team)
        {
            return;
        }

        if (image is null && _dialogs is not null && !_dialogs.Confirm(
            kind == AssetKind.Crest ? "Quitar escudo" : "Quitar bandera",
            $"¿Quitar {(kind == AssetKind.Crest ? "el escudo" : "la bandera")} de {team.Code}? Dejará de verse en la mesa y el marcador."))
        {
            return;
        }

        var result = await RunAsync(() => identity.SetTeamImageAsync(team.TeamId, kind, image));
        if (result is not null)
        {
            await LoadTeamImagesAsync();
        }
    }

    private async Task LoadTeamImagesAsync()
    {
        var images = TeamImages.None;
        try
        {
            if (_identity is not null && SelectedTeam is { } team)
            {
                images = await _identity.GetTeamImagesAsync(team.TeamId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus("No se pudieron leer las imágenes del equipo.", isError: true);
        }

        TeamCrest = images.Crest is { } crest ? new ImageData(crest.Id, crest.Content) : null;
        TeamFlag = images.Flag is { } flag ? new ImageData(flag.Id, flag.Content) : null;
        TeamCrestText = images.Crest?.Description ?? "Sin escudo";
        TeamFlagText = images.Flag?.Description ?? "Sin bandera";
        RemoveTeamCrestCommand.NotifyCanExecuteChanged();
        RemoveTeamFlagCommand.NotifyCanExecuteChanged();
    }

    // ---------- Comandos de nómina ----------

    [RelayCommand]
    private void NewRosterPlayer()
    {
        SelectedRosterLine = null;
        RosterPlayerName = string.Empty;
        RosterCapText = SuggestCap();
        _pendingEntryId = Guid.NewGuid();
        _pendingPlayerId = Guid.NewGuid();
    }

    [RelayCommand(CanExecute = nameof(CanSaveRosterPlayer))]
    private async Task SaveRosterPlayerAsync()
    {
        if (SelectedTournament is not { } tournament || SelectedTeam is not { } team)
        {
            return;
        }

        if (!int.TryParse(RosterCapText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var cap))
        {
            SetStatus("El gorro debe ser un número entero entre 1 y 99.", isError: true);
            return;
        }

        var editing = SelectedRosterLine;
        var entryId = editing?.EntryId ?? _pendingEntryId;
        var playerId = _pendingPlayerId;
        var result = await RunAsync(() => editing is null
            ? _catalog.AddRosterPlayerAsync(tournament.TournamentId, team.TeamId, entryId, playerId, RosterPlayerName, cap)
            : _catalog.UpdateRosterPlayerAsync(tournament.TournamentId, team.TeamId, entryId, RosterPlayerName, cap));

        if (result?.IsSuccess == true)
        {
            await LoadRosterAsync();
            if (editing is null)
            {
                NewRosterPlayer(); // listo para el siguiente
            }
            else
            {
                SelectedRosterLine = RosterLines.FirstOrDefault(r => r.EntryId == entryId);
            }

            await RefreshScheduleRosterAsync();
        }
    }

    private bool CanSaveRosterPlayer() =>
        !IsBusy && CanEditRoster && !string.IsNullOrWhiteSpace(RosterPlayerName) && !string.IsNullOrWhiteSpace(RosterCapText);

    [RelayCommand(CanExecute = nameof(CanRemoveRosterPlayer))]
    private async Task RemoveRosterPlayerAsync()
    {
        if (SelectedTournament is not { } tournament || SelectedTeam is not { } team || SelectedRosterLine is not { } line)
        {
            return;
        }

        if (_dialogs is not null && !_dialogs.Confirm(
            "Quitar de la nómina",
            $"¿Quitar a «{line.PlayerName}» (gorro {line.Cap}) de la nómina de {team.Code} en «{tournament.Name}»?\n\nLos partidos ya programados conservan su nómina."))
        {
            return;
        }

        var result = await RunAsync(() => _catalog.RemoveRosterPlayerAsync(tournament.TournamentId, team.TeamId, line.EntryId));
        if (result?.IsSuccess == true)
        {
            SelectedRosterLine = null;
            await LoadRosterAsync();
            NewRosterPlayer();
            await RefreshScheduleRosterAsync();
        }
    }

    private bool CanRemoveRosterPlayer() => !IsBusy && CanEditRoster && SelectedRosterLine is not null;

    [RelayCommand(CanExecute = nameof(CanImportRoster))]
    private async Task ImportRosterAsync()
    {
        if (_dialogs is null || SelectedTournament is not { } tournament || IsBusy)
        {
            return;
        }

        var path = _dialogs.PickRosterFile();
        if (path is null)
        {
            return;
        }

        RosterPreviewResult preview;
        IsBusy = true;
        try
        {
            SetStatus($"Leyendo «{System.IO.Path.GetFileName(path)}»…", isError: false);
            preview = await _catalog.PreviewRosterImportAsync(tournament.TournamentId, path);
        }
        finally
        {
            IsBusy = false;
        }

        if (preview.Preview is null)
        {
            SetStatus(preview.Message ?? "No se pudo leer el archivo.", isError: true);
            return;
        }

        var dialog = new RosterImportViewModel(_catalog, preview.Preview);
        if (await _dialogs.ShowRosterImportAsync(dialog))
        {
            SetStatus(dialog.ResultMessage ?? "Nómina importada.", isError: false);
            await RefreshAsync();
            await LoadRosterAsync();
            await RefreshScheduleRosterAsync();
        }
        else
        {
            SetStatus("Importación cancelada: no se guardó nada.", isError: false);
        }
    }

    private bool CanImportRoster() => !IsBusy && SelectedTournament is not null && _dialogs is not null;

    private string SuggestCap()
    {
        var used = RosterLines.Select(r => r.Cap).ToHashSet();
        var free = Enumerable.Range(1, 99).FirstOrDefault(c => !used.Contains(c));
        return CanEditRoster && free > 0 ? free.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    // ---------- Programar partido ----------

    [RelayCommand(CanExecute = nameof(CanScheduleMatch))]
    private async Task ScheduleMatchAsync()
    {
        if (IsBusy || ScheduleTournament is not { } tournament || ScheduleHome is not { } home || ScheduleAway is not { } away)
        {
            return;
        }

        if (!TryParseSchedule(ScheduleDateText, out var scheduledAtUtc))
        {
            SetStatus("Escriba la fecha y hora como día/mes/año hora:minutos, por ejemplo 14/03/2027 15:30, o déjela vacía.", isError: true);
            return;
        }

        if (IsRescheduling)
        {
            await SaveRescheduleAsync(scheduledAtUtc);
            return;
        }

        var matchId = _pendingMatchId;
        var command = new ScheduleMatch(
            matchId,
            tournament.TournamentId,
            ScheduleLabel,
            scheduledAtUtc,
            home.TeamId,
            away.TeamId,
            ScheduleHomeColor,
            ScheduleHomeCap,
            ScheduleAwayColor,
            ScheduleAwayCap);

        IsBusy = true;
        try
        {
            var result = await _matches.ScheduleMatchAsync(command);
            if (!result.IsSuccess)
            {
                SetStatus(result.Message ?? "No se pudo programar el partido.", isError: true);
                return;
            }

            var roster = result.Snapshot is { } s ? $" Nómina congelada: {s.Home.Roster.Count} local · {s.Away.Roster.Count} visita." : string.Empty;
            SetStatus($"Partido «{ScheduleLabel.Trim()}» programado en «{tournament.Name}».{roster}", isError: false);
            _pendingMatchId = Guid.NewGuid();
            ScheduleLabel = string.Empty;
            ScheduleDateText = string.Empty;
            MatchScheduled?.Invoke(this, matchId);
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

    private bool CanScheduleMatch() =>
        !IsBusy && ScheduleTournament is not null && ScheduleHome is not null && ScheduleAway is not null
        && !string.IsNullOrWhiteSpace(ScheduleLabel);

    // ---------- Reprogramar, actualizar nómina y anular ----------

    /// <summary>Carga el partido elegido en el formulario para reprogramarlo (descripción, fecha, colores y gorros).</summary>
    [RelayCommand(CanExecute = nameof(CanChangeListedMatch))]
    private async Task BeginRescheduleAsync()
    {
        if (Queries is null || ListedMatch is not { IsScheduled: true } listed || IsBusy)
        {
            return;
        }

        MatchSnapshot? snapshot;
        try
        {
            snapshot = await Queries.GetSnapshotAsync(listed.MatchId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus("No se pudo leer el partido del almacenamiento local.", isError: true);
            return;
        }

        if (snapshot is not { Status: MatchStatus.Scheduled })
        {
            SetStatus("El partido ya no está programado. Actualice la lista.", isError: true);
            return;
        }

        _reschedulingMatchId = snapshot.MatchId;
        _reschedulingVersion = snapshot.Version;
        _reschedulingLabel = snapshot.Label;
        IsRescheduling = true;

        ScheduleTournament = Tournaments.FirstOrDefault(t => t.TournamentId == listed.TournamentId) ?? ScheduleTournament;
        ScheduleHome = Teams.FirstOrDefault(t => t.TeamId == snapshot.Home.TeamId);
        ScheduleAway = Teams.FirstOrDefault(t => t.TeamId == snapshot.Away.TeamId);
        ScheduleLabel = snapshot.Label;
        ScheduleDateText = listed.ScheduledAtUtc is { } at ? at.ToLocalTime().ToString(DateFormat, MatchTexts.Culture) : string.Empty;
        (ScheduleHomeColor, ScheduleHomeCap) = (snapshot.Home.ColorHex, snapshot.Home.CapColor);
        (ScheduleAwayColor, ScheduleAwayCap) = (snapshot.Away.ColorHex, snapshot.Away.CapColor);
        SetStatus($"Reprogramando «{snapshot.Label}»: cambie descripción, fecha, colores o gorros y guarde. Los equipos no cambian.", isError: false);
    }

    /// <summary>Sale del modo reprogramación sin guardar.</summary>
    [RelayCommand]
    private void CancelReschedule()
    {
        if (!IsRescheduling)
        {
            return;
        }

        EndReschedule();
        SetStatus("Reprogramación cancelada: no se guardó nada.", isError: false);
    }

    private async Task SaveRescheduleAsync(DateTimeOffset? scheduledAtUtc)
    {
        if (Operator is not { } op || _reschedulingMatchId is not { } matchId)
        {
            return;
        }

        var command = new RescheduleMatch(Guid.NewGuid(), matchId, ScheduleLabel, scheduledAtUtc,
            ScheduleHomeColor, ScheduleHomeCap, ScheduleAwayColor, ScheduleAwayCap, op, _reschedulingVersion);
        var result = await RunMatchCommandAsync(() => _matches.RescheduleMatchAsync(command));
        if (result is { IsSuccess: true, Snapshot: { } snapshot })
        {
            EndReschedule();
            SetStatus($"Partido «{snapshot.Label}» reprogramado.", isError: false);
            MatchChanged?.Invoke(this, matchId);
        }
    }

    private void EndReschedule()
    {
        IsRescheduling = false;
        _reschedulingMatchId = null;
        _reschedulingVersion = null;
        ScheduleLabel = string.Empty;
        ScheduleDateText = string.Empty;
        OnScheduleHomeChanged(ScheduleHome);
        OnScheduleAwayChanged(ScheduleAway);
    }

    /// <summary>Reemplaza la nómina del partido elegido por la vigente del evento, tras confirmar.</summary>
    [RelayCommand(CanExecute = nameof(CanChangeListedMatch))]
    private async Task RefreshMatchRosterAsync()
    {
        if (Operator is not { } op || ListedMatch is not { IsScheduled: true } listed || IsBusy)
        {
            return;
        }

        if (_dialogs is not null && !_dialogs.Confirm(
            "Actualizar nómina del partido",
            $"¿Reemplazar la nómina de «{listed.Label}» ({listed.Home} vs {listed.Away}) por la nómina vigente de ambos equipos en «{listed.Tournament}»?\n\nEl partido aún no se ha iniciado, así que no hay incidencias que dependan de la nómina anterior."))
        {
            return;
        }

        var result = await RunMatchCommandAsync(() => _matches.RefreshMatchRosterAsync(new RefreshMatchRoster(Guid.NewGuid(), listed.MatchId, op)));
        if (result is { IsSuccess: true, Snapshot: { } s })
        {
            SetStatus($"Nómina de «{s.Label}» actualizada desde el evento: {s.Home.Roster.Count} local · {s.Away.Roster.Count} visita.", isError: false);
            MatchChanged?.Invoke(this, listed.MatchId);
        }
    }

    /// <summary>Anula el partido elegido con un motivo obligatorio. No se borra: queda como «Anulado».</summary>
    [RelayCommand(CanExecute = nameof(CanChangeListedMatch))]
    private async Task CancelMatchAsync()
    {
        if (Operator is not { } op || ListedMatch is not { IsScheduled: true } listed || IsBusy)
        {
            return;
        }

        var reason = _dialogs?.AskReason(
            "Anular partido",
            $"Indique por qué se anula «{listed.Label}» ({listed.Home} vs {listed.Away}). El partido no se borra: queda como «Anulado» con su nombre y la hora, y ya no se podrá iniciar.");
        if (reason is null)
        {
            return;
        }

        var result = await RunMatchCommandAsync(() => _matches.CancelMatchAsync(new CancelMatch(Guid.NewGuid(), listed.MatchId, reason, op)));
        if (result?.IsSuccess == true)
        {
            if (_reschedulingMatchId == listed.MatchId)
            {
                EndReschedule();
            }

            SetStatus($"Partido «{listed.Label}» anulado. Queda en la lista como registro.", isError: false);
            MatchChanged?.Invoke(this, listed.MatchId);
            await RefreshAsync();
        }
    }

    private bool CanChangeListedMatch() =>
        !IsBusy && Operator is not null && Queries is not null && ListedMatch is { IsScheduled: true };

    private async Task<CommandResult?> RunMatchCommandAsync(Func<Task<CommandResult>> action)
    {
        if (IsBusy)
        {
            return null;
        }

        IsBusy = true;
        try
        {
            var result = await action();
            if (!result.IsSuccess)
            {
                SetStatus(result.Message ?? "No se pudo guardar el cambio del partido.", isError: true);
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus($"Error inesperado: {ex.Message}", isError: true);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Convierte la hora local escrita por el operador (es-PE) a UTC. Vacío es válido.</summary>
    public static bool TryParseSchedule(string? text, out DateTimeOffset? utc)
    {
        utc = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!DateTime.TryParseExact(text.Trim(), DateFormat, MatchTexts.Culture, DateTimeStyles.AssumeLocal, out var local))
        {
            return false;
        }

        utc = new DateTimeOffset(local).ToUniversalTime();
        return true;
    }

    // ---------- Reacciones ----------

    partial void OnSelectedTournamentChanged(TournamentItem? value)
    {
        if (value is not null)
        {
            TournamentName = value.Name;
        }

        OnPropertyChanged(nameof(TournamentFormTitle));
        OnPropertyChanged(nameof(TournamentRulesText));
        OnPropertyChanged(nameof(ImportHint));
        ImportRosterCommand.NotifyCanExecuteChanged();
        EditRulesCommand.NotifyCanExecuteChanged();
        EditIdentityCommand.NotifyCanExecuteChanged();
        _ = LoadRosterAsync();
    }

    partial void OnSelectedTeamChanged(TeamItem? value)
    {
        if (value is not null)
        {
            TeamCode = value.Code;
            TeamName = value.Name;
            TeamColor = value.ColorHex;
            TeamCapColor = value.CapColor;
        }

        OnPropertyChanged(nameof(TeamFormTitle));
        OnPropertyChanged(nameof(CanEditTeamImages));
        OnPropertyChanged(nameof(TeamImagesHint));
        LoadTeamCrestCommand.NotifyCanExecuteChanged();
        LoadTeamFlagCommand.NotifyCanExecuteChanged();
        _ = LoadTeamImagesAsync();
        _ = LoadRosterAsync();
    }

    partial void OnSelectedRosterLineChanged(TournamentRosterLine? value)
    {
        if (value is not null)
        {
            RosterPlayerName = value.PlayerName;
            RosterCapText = value.Cap.ToString(CultureInfo.InvariantCulture);
        }

        OnPropertyChanged(nameof(RosterFormTitle));
        RemoveRosterPlayerCommand.NotifyCanExecuteChanged();
    }

    partial void OnRosterPlayerNameChanged(string value) => SaveRosterPlayerCommand.NotifyCanExecuteChanged();

    partial void OnRosterCapTextChanged(string value) => SaveRosterPlayerCommand.NotifyCanExecuteChanged();

    partial void OnTournamentNameChanged(string value) => SaveTournamentCommand.NotifyCanExecuteChanged();

    partial void OnTeamCodeChanged(string value) => SaveTeamCommand.NotifyCanExecuteChanged();

    partial void OnTeamNameChanged(string value) => SaveTeamCommand.NotifyCanExecuteChanged();

    partial void OnTeamColorChanged(string value) => SaveTeamCommand.NotifyCanExecuteChanged();

    partial void OnTeamCapColorChanged(string value) => SaveTeamCommand.NotifyCanExecuteChanged();

    partial void OnScheduleTournamentChanged(TournamentItem? value) => _ = RefreshScheduleRosterAsync();

    partial void OnScheduleHomeChanged(TeamItem? value)
    {
        ScheduleHomeColor = value?.ColorHex ?? string.Empty;
        ScheduleHomeCap = value?.CapColor ?? string.Empty;
        _ = RefreshScheduleRosterAsync();
    }

    partial void OnScheduleAwayChanged(TeamItem? value)
    {
        ScheduleAwayColor = value?.ColorHex ?? string.Empty;
        ScheduleAwayCap = value?.CapColor ?? string.Empty;
        _ = RefreshScheduleRosterAsync();
    }

    partial void OnScheduleLabelChanged(string value) => ScheduleMatchCommand.NotifyCanExecuteChanged();

    partial void OnListedMatchChanged(MatchListItem? value)
    {
        OnPropertyChanged(nameof(ListedMatchHint));
        NotifyListedMatchCommands();
    }

    partial void OnIsReschedulingChanged(bool value)
    {
        OnPropertyChanged(nameof(ScheduleFormTitle));
        OnPropertyChanged(nameof(ScheduleButtonText));
        OnPropertyChanged(nameof(CanChooseScheduleTeams));
    }

    private void NotifyListedMatchCommands()
    {
        BeginRescheduleCommand.NotifyCanExecuteChanged();
        RefreshMatchRosterCommand.NotifyCanExecuteChanged();
        CancelMatchCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        SaveTournamentCommand.NotifyCanExecuteChanged();
        SaveTeamCommand.NotifyCanExecuteChanged();
        ScheduleMatchCommand.NotifyCanExecuteChanged();
        SaveRosterPlayerCommand.NotifyCanExecuteChanged();
        RemoveRosterPlayerCommand.NotifyCanExecuteChanged();
        ImportRosterCommand.NotifyCanExecuteChanged();
        EditRulesCommand.NotifyCanExecuteChanged();
        EditIdentityCommand.NotifyCanExecuteChanged();
        LoadTeamCrestCommand.NotifyCanExecuteChanged();
        LoadTeamFlagCommand.NotifyCanExecuteChanged();
        RemoveTeamCrestCommand.NotifyCanExecuteChanged();
        RemoveTeamFlagCommand.NotifyCanExecuteChanged();
        NotifyListedMatchCommands();
    }

    private async Task LoadRosterAsync()
    {
        var selectedEntry = SelectedRosterLine?.EntryId;
        try
        {
            var lines = SelectedTournament is { } t && SelectedTeam is { } team
                ? await _catalog.GetTournamentRosterAsync(t.TournamentId, team.TeamId)
                : [];
            Fill(RosterLines, lines);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RosterLines.Clear();
            SetStatus("No se pudo leer la nómina del almacenamiento local.", isError: true);
        }

        OnPropertyChanged(nameof(RosterTitle));
        OnPropertyChanged(nameof(CanEditRoster));
        SelectedRosterLine = RosterLines.FirstOrDefault(r => r.EntryId == selectedEntry);
        if (SelectedRosterLine is null)
        {
            RosterPlayerName = string.Empty;
            RosterCapText = SuggestCap();
        }

        SaveRosterPlayerCommand.NotifyCanExecuteChanged();
    }

    private async Task RefreshScheduleRosterAsync()
    {
        ScheduleMatchCommand.NotifyCanExecuteChanged();
        if (ScheduleTournament is not { } tournament || ScheduleHome is null || ScheduleAway is null)
        {
            ScheduleRosterInfo = string.Empty;
            return;
        }

        try
        {
            var home = await _catalog.GetTournamentRosterAsync(tournament.TournamentId, ScheduleHome.TeamId);
            var away = await _catalog.GetTournamentRosterAsync(tournament.TournamentId, ScheduleAway.TeamId);
            ScheduleRosterInfo = home.Count == 0 || away.Count == 0
                ? $"Nómina en el evento: {home.Count} local · {away.Count} visita. Sin nómina solo se podrán registrar tiempos muertos; cárguela antes en «Eventos y nóminas»."
                : $"Nómina en el evento: {home.Count} local · {away.Count} visita. Se copia al partido al programarlo.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ScheduleRosterInfo = "No se pudo leer la nómina del evento.";
        }
    }

    private async Task<CatalogResult?> RunAsync(Func<Task<CatalogResult>> action)
    {
        if (IsBusy)
        {
            return null; // bloquea el doble envío accidental
        }

        IsBusy = true;
        try
        {
            var result = await action();
            SetStatus(result.Message ?? (result.IsSuccess ? "Guardado." : "No se pudo guardar."), isError: !result.IsSuccess);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus($"Error inesperado: {ex.Message}", isError: true);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
