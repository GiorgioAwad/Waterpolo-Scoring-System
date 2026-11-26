using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Desktop.Presentation;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;

namespace FDNDA.Waterpolo.Desktop.ViewModels;

/// <summary>
/// Diálogo «Reglas del evento» (RF03). Edita las reglas que tomarán los partidos del evento al
/// iniciarse; los iniciados o cerrados conservan las suyas. El preset DEMO se puede cargar; el de
/// competencia se muestra como pendiente de aprobación técnica y no tiene valores. Nada se guarda
/// hasta pulsar «Guardar reglas».
/// </summary>
public sealed partial class RulesEditorViewModel : ObservableObject
{
    private readonly CatalogOperations _catalog;
    private readonly Guid _tournamentId;
    private readonly RulesetSnapshot _current;

    public RulesEditorViewModel(
        CatalogOperations catalog, Guid tournamentId, string tournamentName, RulesetSnapshot current, int scheduledMatches, int otherMatches)
    {
        _catalog = catalog;
        _tournamentId = tournamentId;
        _current = current;
        TournamentName = tournamentName;
        ApplicabilityText = Applicability(scheduledMatches, otherMatches);
        Load(current);
    }

    /// <summary>El operador cerró el diálogo: verdadero si las reglas se guardaron.</summary>
    public event EventHandler<bool>? CloseRequested;

    public string TournamentName { get; }

    public string Title => $"Reglas del evento — {TournamentName}";

    public string CurrentPresetText => $"Reglas actuales: {_current.PresetName}";

    /// <summary>A qué partidos afecta guardar.</summary>
    public string ApplicabilityText { get; }

    public static string CompetitionPresetNote =>
        "Preset de competencia: pendiente de aprobación del área técnica de la FDNDA. El programa no trae valores reglamentarios ni declara homologación.";

    public static string ShotClockNote =>
        "La posesión configurada aquí solo alimenta la simulación de la mesa. El programa nunca escribe en la consola Colorado.";

    public static string ShootoutNote =>
        "Prórroga y tanda quedan a decisión de la mesa: la prórroga solo empieza con empate al terminar los periodos y la tanda, " +
        "con empate tras todos ellos. Son parámetros pendientes de aprobación técnica; 0 periodos de prórroga = sin prórroga.";

    [ObservableProperty]
    public partial string PeriodCountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PeriodDurationText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BreakDurationText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HalftimeDurationText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TimeoutsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ShotClockText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ShotClockAlternateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool PenaltyShootoutEnabled { get; set; }

    [ObservableProperty]
    public partial string OvertimePeriodsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OvertimeDurationText { get; set; } = string.Empty;

    /// <summary>Cómo quedarán guardadas con los valores escritos, o qué falta corregir.</summary>
    [ObservableProperty]
    public partial string OutcomeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Mensaje del guardado, para la barra de la pestaña.</summary>
    public string? ResultMessage { get; private set; }

    [RelayCommand]
    private void LoadDemo()
    {
        Load(RulesetSnapshot.DemoPreset());
        StatusMessage = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!TryBuild(out var rules, out var problem))
        {
            StatusMessage = problem;
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _catalog.UpdateTournamentRulesAsync(_tournamentId, rules);
            if (!result.IsSuccess)
            {
                StatusMessage = result.Message ?? "No se pudieron guardar las reglas.";
                return;
            }

            ResultMessage = result.Message;
            StatusMessage = string.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = $"Error inesperado: {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        CloseRequested?.Invoke(this, true);
    }

    private bool CanSave() => !IsBusy;

    [RelayCommand]
    private void Cancel()
    {
        if (!IsBusy)
        {
            CloseRequested?.Invoke(this, false);
        }
    }

    /// <summary>Convierte los textos del formulario en reglas válidas, o explica el primer problema.</summary>
    public bool TryBuild(out RulesetSnapshot rules, out string problem)
    {
        rules = _current;
        if (!RulesTexts.TryParseWhole(PeriodCountText, out var periods))
        {
            problem = "Escriba la cantidad de periodos como número entero.";
            return false;
        }

        if (!RulesTexts.TryParseDuration(PeriodDurationText, out var periodSeconds))
        {
            problem = "Escriba la duración del periodo como minutos:segundos (por ejemplo 8:00) o en minutos.";
            return false;
        }

        if (!RulesTexts.TryParseDuration(BreakDurationText, out var breakSeconds))
        {
            problem = "Escriba el descanso entre periodos como minutos:segundos (por ejemplo 2:00) o en minutos.";
            return false;
        }

        if (!RulesTexts.TryParseDuration(HalftimeDurationText, out var halftimeSeconds))
        {
            problem = "Escriba el descanso de medio tiempo como minutos:segundos (por ejemplo 3:00) o en minutos.";
            return false;
        }

        if (!RulesTexts.TryParseWhole(TimeoutsText, out var timeouts))
        {
            problem = "Escriba los tiempos muertos por equipo como número entero.";
            return false;
        }

        if (!RulesTexts.TryParseWhole(ShotClockText, out var shot) || !RulesTexts.TryParseWhole(ShotClockAlternateText, out var shotAlt))
        {
            problem = "Escriba la posesión en segundos enteros.";
            return false;
        }

        if (!RulesTexts.TryParseWhole(OvertimePeriodsText, out var overtimePeriods))
        {
            problem = "Escriba los periodos de prórroga como número entero (0 = sin prórroga).";
            return false;
        }

        if (!RulesTexts.TryParseDuration(OvertimeDurationText, out var overtimeSeconds))
        {
            problem = "Escriba la duración de la prórroga como minutos:segundos (por ejemplo 3:00) o en minutos.";
            return false;
        }

        try
        {
            rules = RulesetSnapshot.ForEvent(
                periods, periodSeconds, breakSeconds, halftimeSeconds, timeouts, shot, shotAlt, PenaltyShootoutEnabled,
                overtimePeriods, overtimeSeconds);
            problem = string.Empty;
            return true;
        }
        catch (DomainException ex)
        {
            problem = ex.Message;
            return false;
        }
    }

    private void Load(RulesetSnapshot rules)
    {
        PeriodCountText = rules.PeriodCount.ToString(CultureInfo.InvariantCulture);
        PeriodDurationText = RulesTexts.Duration(rules.PeriodDurationSeconds);
        BreakDurationText = RulesTexts.Duration(rules.BreakDurationSeconds);
        HalftimeDurationText = RulesTexts.Duration(rules.HalftimeDurationSeconds);
        TimeoutsText = rules.TimeoutsPerTeam.ToString(CultureInfo.InvariantCulture);
        ShotClockText = rules.ShotClockSeconds.ToString(CultureInfo.InvariantCulture);
        ShotClockAlternateText = rules.ShotClockAlternateSeconds.ToString(CultureInfo.InvariantCulture);
        PenaltyShootoutEnabled = rules.PenaltyShootoutEnabled;
        OvertimePeriodsText = rules.OvertimePeriodCount.ToString(CultureInfo.InvariantCulture);
        OvertimeDurationText = RulesTexts.Duration(rules.OvertimePeriodDurationSeconds);
        UpdateOutcome();
    }

    private void UpdateOutcome()
    {
        // Un error de guardado se refería a los valores anteriores: al cambiar uno deja de valer.
        if (!IsBusy)
        {
            StatusMessage = string.Empty;
        }

        if (!TryBuild(out var rules, out var problem))
        {
            OutcomeText = $"Revise: {problem}";
            return;
        }

        if (rules == _current)
        {
            OutcomeText = "Sin cambios respecto de las reglas actuales.";
            return;
        }

        OutcomeText = rules.IsDemoPreset
            ? "Se guardarán como preset DEMO (valores ilustrativos, no reglamentarios)."
            : $"Se guardarán como «{RulesetSnapshot.CustomPresetName}».";
    }

    private static string Applicability(int scheduled, int others)
    {
        var applies = scheduled switch
        {
            0 => "No hay partidos programados pendientes: las nuevas reglas las tomarán los partidos que se inicien después.",
            1 => "El partido programado de este evento tomará estas reglas al iniciarse.",
            _ => $"Los {scheduled} partidos programados de este evento tomarán estas reglas al iniciarse.",
        };
        var keeps = others switch
        {
            0 => string.Empty,
            1 => " El partido ya iniciado o cerrado conserva las reglas con las que empezó.",
            _ => $" Los {others} partidos ya iniciados o cerrados conservan las reglas con las que empezaron.",
        };
        return applies + keeps;
    }

    partial void OnPeriodCountTextChanged(string value) => UpdateOutcome();

    partial void OnPeriodDurationTextChanged(string value) => UpdateOutcome();

    partial void OnBreakDurationTextChanged(string value) => UpdateOutcome();

    partial void OnHalftimeDurationTextChanged(string value) => UpdateOutcome();

    partial void OnTimeoutsTextChanged(string value) => UpdateOutcome();

    partial void OnShotClockTextChanged(string value) => UpdateOutcome();

    partial void OnShotClockAlternateTextChanged(string value) => UpdateOutcome();

    partial void OnPenaltyShootoutEnabledChanged(bool value) => UpdateOutcome();

    partial void OnOvertimePeriodsTextChanged(string value) => UpdateOutcome();

    partial void OnOvertimeDurationTextChanged(string value) => UpdateOutcome();

    partial void OnIsBusyChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();
}
