using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Backups;
using FDNDA.Waterpolo.Application.Identity;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Desktop.Presentation;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;

namespace FDNDA.Waterpolo.Desktop.ViewModels;

/// <summary>
/// Ventana de mesa. Invoca casos de uso y observa snapshots inmutables; no escribe SQL,
/// no lee hardware ni recalcula el marcador. Los cambios llegan desde otros hilos y se
/// despachan al hilo de WPF.
/// </summary>
public sealed partial class MesaViewModel : ObservableObject, IDisposable
{
    public const int MesaTabIndex = 2;

    public const int ResultsTabIndex = 3;

    public const int DiagnosticsTabIndex = 4;

    private readonly MatchOperations _operations;
    private readonly MatchQueries _queries;
    private readonly IMatchSnapshotFeed _feed;
    private readonly ITimingFeed _timing;
    private readonly ISimulationControl? _simulation;
    private readonly OperatorRef _operator;
    private readonly IScoreboardHost _scoreboard;
    private readonly IPromptService _prompts;
    private readonly SynchronizationContext _ui;

    private MatchSnapshot? _snapshot;
    private Guid? _correctionIncidentId;

    public MesaViewModel(
        MatchOperations operations,
        MatchQueries queries,
        IMatchSnapshotFeed feed,
        ITimingFeed timing,
        ISimulationControl? simulation,
        OperatorRef @operator,
        IScoreboardHost scoreboard,
        IPromptService prompts,
        ScoreboardSettings scoreboardSettings,
        AppInfo info)
    {
        _operations = operations;
        _queries = queries;
        _feed = feed;
        _timing = timing;
        _simulation = simulation;
        _operator = @operator;
        _scoreboard = scoreboard;
        _prompts = prompts;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        ScoreboardSettings = scoreboardSettings;
        Info = info;

        _feed.Published += OnSnapshotPublished;
        _timing.Changed += OnTimingChanged;
        _scoreboard.Notice += OnScoreboardNotice;
        _scoreboard.StateChanged += OnScoreboardStateChanged;

        ApplyTiming(_timing.Current);
        RefreshMonitors();
    }

    public ScoreboardSettings ScoreboardSettings { get; }

    public AppInfo Info { get; }

    /// <summary>Eventos, equipos y programación de partidos. Al programar uno, la lista se actualiza.</summary>
    public CatalogViewModel? Catalog
    {
        get => _catalog;
        init
        {
            _catalog = value;
            if (value is not null)
            {
                value.MatchScheduled += OnMatchScheduled;
                value.MatchChanged += OnMatchScheduled;
            }
        }
    }

    private readonly CatalogViewModel? _catalog;

    /// <summary>Estado de conexiones del diagnóstico (consola, señal, marcador, pantallas, almacenamiento).</summary>
    public ConnectionStatusViewModel? Connections { get; init; }

    /// <summary>
    /// Respaldos: la mesa respalda automáticamente antes de iniciar y al finalizar un partido y,
    /// tras una restauración, recarga partidos, eventos y nóminas.
    /// </summary>
    public BackupsViewModel? Backups
    {
        get => _backupsViewModel;
        init
        {
            _backupsViewModel = value;
            if (value is not null)
            {
                value.ReloadAfterRestore = ReloadAfterRestoreAsync;
            }
        }
    }

    private readonly BackupsViewModel? _backupsViewModel;

    /// <summary>Identidad visual del partido abierto (escudos y banderas en la cabecera de la mesa).</summary>
    public IScoreboardIdentityFeed? Identity
    {
        get => _identityFeed;
        init
        {
            _identityFeed = value;
            if (value is not null)
            {
                _identity = value.Current;
                value.Changed += OnIdentityChanged;
            }
        }
    }

    private readonly IScoreboardIdentityFeed? _identityFeed;
    private ScoreboardIdentity? _identity;

    /// <summary>Resultados y exportación; se actualizan cada vez que se abre su pestaña.</summary>
    public ResultsViewModel? Results { get; init; }

    public bool HasSimulator => _simulation is not null;

    /// <summary>
    /// Reproducción de una grabación de cronometraje (R9, perfil demo). Reemplaza al simulador;
    /// la mesa solo muestra el avance y permite volver a empezar.
    /// </summary>
    public IReplayControl? Replay
    {
        get => _replay;
        init
        {
            _replay = value;
            if (value is not null)
            {
                value.ProgressChanged += OnReplayProgressChanged;
            }

            UpdateReplayText();
        }
    }

    private readonly IReplayControl? _replay;

    public bool HasReplay => _replay is not null;

    [ObservableProperty]
    public partial string ReplayText { get; set; } = string.Empty;

    public ObservableCollection<MatchListItem> Matches { get; } = [];

    public ObservableCollection<PlayerOption> HomeRoster { get; } = [];

    public ObservableCollection<PlayerOption> AwayRoster { get; } = [];

    public ObservableCollection<PlayerOption> CorrectionPlayers { get; } = [];

    public ObservableCollection<IncidentItem> Incidents { get; } = [];

    /// <summary>Acciones de F6/F9: las de juego o, durante la tanda, los lanzamientos.</summary>
    public ObservableCollection<IncidentOption> RegistrationOptions { get; } = new(IncidentOption.DuringPlay);

    public ObservableCollection<IncidentOption> CorrectionOptions { get; } = [];

    public ObservableCollection<TeamOption> CorrectionTeams { get; } = [];

    [ObservableProperty]
    public partial IncidentOption? SelectedRegistrationOption { get; set; } = IncidentOption.All[1];

    [ObservableProperty]
    public partial IncidentOption? SelectedCorrectionOption { get; set; }

    [ObservableProperty]
    public partial TeamOption? SelectedCorrectionTeam { get; set; }

    public bool CorrectionNeedsPlayer => SelectedIncident is { Type: not IncidentType.Timeout };

    public bool CorrectionIsTimeout => SelectedIncident?.Type == IncidentType.Timeout;

    public bool HasSelectedIncident => SelectedIncident is not null;

    public ObservableCollection<MonitorOption> Monitors { get; } = [];

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; } = MesaTabIndex;

    [ObservableProperty]
    public partial MatchListItem? SelectedMatch { get; set; }

    [ObservableProperty]
    public partial bool HasMatch { get; set; }

    [ObservableProperty]
    public partial string MatchTitle { get; set; } = "Sin partido abierto";

    [ObservableProperty]
    public partial string HomeName { get; set; } = "—";

    [ObservableProperty]
    public partial string AwayName { get; set; } = "—";

    [ObservableProperty]
    public partial string HomeColor { get; set; } = "#455A64";

    [ObservableProperty]
    public partial string AwayColor { get; set; } = "#455A64";

    [ObservableProperty]
    public partial string HomeCapColor { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ImageData? HomeCrest { get; set; }

    [ObservableProperty]
    public partial ImageData? HomeFlag { get; set; }

    [ObservableProperty]
    public partial ImageData? AwayCrest { get; set; }

    [ObservableProperty]
    public partial ImageData? AwayFlag { get; set; }

    [ObservableProperty]
    public partial string AwayCapColor { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int HomeScore { get; set; }

    [ObservableProperty]
    public partial int AwayScore { get; set; }

    [ObservableProperty]
    public partial string StageText { get; set; } = "—";

    [ObservableProperty]
    public partial string RulesText { get; set; } = string.Empty;

    /// <summary>Resultado de la tanda, aparte del reglamentario; vacío si no hay tanda.</summary>
    [ObservableProperty]
    public partial string ShootoutText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasShootout { get; set; }

    [ObservableProperty]
    public partial bool IsShootoutStage { get; set; }

    /// <summary>«Siguiente periodo» o, tras el último reglamentario, «Iniciar prórroga» (con su letra de acceso).</summary>
    [ObservableProperty]
    public partial string NextPeriodLabel { get; set; } = "_Siguiente periodo";

    /// <summary>Por qué no se puede iniciar la tanda ahora (se muestra como ayuda del botón).</summary>
    [ObservableProperty]
    public partial string ShootoutHint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GameClockText { get; set; } = ClockFormatter.GameClockUnavailable;

    [ObservableProperty]
    public partial string ShotClockText { get; set; } = ClockFormatter.ShotClockUnavailable;

    [ObservableProperty]
    public partial string SourceLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SignalText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RunningText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? TimingNotice { get; set; }

    [ObservableProperty]
    public partial bool IsSignalOk { get; set; }

    /// <summary>Aviso si el periodo que informa la consola no coincide con el de la mesa (que no cambia solo).</summary>
    [ObservableProperty]
    public partial string? PeriodMismatchText { get; set; }

    [ObservableProperty]
    public partial bool IsSimulation { get; set; }

    [ObservableProperty]
    public partial PlayerOption? SelectedHomePlayer { get; set; }

    [ObservableProperty]
    public partial PlayerOption? SelectedAwayPlayer { get; set; }

    [ObservableProperty]
    public partial IncidentItem? SelectedIncident { get; set; }

    [ObservableProperty]
    public partial PlayerOption? SelectedCorrectionPlayer { get; set; }

    [ObservableProperty]
    public partial string ReasonText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial MonitorOption? SelectedMonitor { get; set; }

    [ObservableProperty]
    public partial bool ScoreboardFullScreen { get; set; } = true;

    [ObservableProperty]
    public partial bool IsScoreboardOpen { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Listo.";

    [ObservableProperty]
    public partial bool IsStatusError { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Carga la lista y recupera el partido en juego, si lo hay; si no, abre el primero.</summary>
    public async Task InitializeAsync()
    {
        await RefreshMatchesAsync();

        var active = await _queries.FindInProgressMatchIdAsync();
        var toOpen = active ?? Matches.FirstOrDefault()?.MatchId;
        if (toOpen is { } matchId)
        {
            var result = await _operations.OpenMatchAsync(matchId);
            if (result.IsSuccess)
            {
                ApplySnapshot(result.Snapshot!);
                SetStatus(
                    active is not null
                        ? "Se recuperó el partido en juego con su último estado confirmado."
                        : "Partido abierto. Use «Iniciar partido» para comenzar.",
                    isError: false);
            }
            else
            {
                SetStatus(result.Message ?? "No se pudo abrir el partido.", isError: true);
            }
        }
        else
        {
            SetStatus("No hay partidos programados en este perfil.", isError: false);
        }
    }

    public void Dispose()
    {
        if (_catalog is not null)
        {
            _catalog.MatchScheduled -= OnMatchScheduled;
            _catalog.MatchChanged -= OnMatchScheduled;
        }

        if (_backupsViewModel is not null)
        {
            _backupsViewModel.ReloadAfterRestore = null;
        }

        Connections?.Dispose();
        if (_replay is not null)
        {
            _replay.ProgressChanged -= OnReplayProgressChanged;
        }

        if (_identityFeed is not null)
        {
            _identityFeed.Changed -= OnIdentityChanged;
        }

        _feed.Published -= OnSnapshotPublished;
        _timing.Changed -= OnTimingChanged;
        _scoreboard.Notice -= OnScoreboardNotice;
        _scoreboard.StateChanged -= OnScoreboardStateChanged;
    }

    // ---------- Partidos ----------

    [RelayCommand]
    private async Task RefreshMatchesAsync()
    {
        try
        {
            var selectedId = SelectedMatch?.MatchId ?? _snapshot?.MatchId;
            var list = await _queries.ListAsync();
            Matches.Clear();
            foreach (var summary in list)
            {
                Matches.Add(MatchListItem.From(summary));
            }

            SelectedMatch = Matches.FirstOrDefault(m => m.MatchId == selectedId) ?? Matches.FirstOrDefault();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus("No se pudo leer la lista de partidos del almacenamiento local.", isError: true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelectedMatch))]
    private async Task OpenSelectedMatchAsync()
    {
        if (SelectedMatch is not { } selected)
        {
            return;
        }

        await RunAsync(() => _operations.OpenMatchAsync(selected.MatchId), "Partido abierto en la mesa.");
        SelectedTabIndex = MesaTabIndex;
    }

    private bool CanOpenSelectedMatch() => SelectedMatch is not null && !IsBusy;

    /// <summary>
    /// Se programó, reprogramó o anuló un partido: la lista se recarga y ese partido queda
    /// seleccionado, sin abrirlo en la mesa.
    /// </summary>
    private void OnMatchScheduled(object? sender, Guid matchId) => _ui.Post(async _ =>
    {
        await RefreshMatchesAsync();
        SelectedMatch = Matches.FirstOrDefault(m => m.MatchId == matchId) ?? SelectedMatch;
    }, null);

    // ---------- Estado del partido ----------

    [RelayCommand(CanExecute = nameof(CanStartMatch))]
    private async Task StartMatchAsync()
    {
        if (_snapshot is not { } match || IsBusy)
        {
            return;
        }

        var backupNote = string.Empty;
        if (_backupsViewModel is not null)
        {
            IsBusy = true;
            BackupResult? backup;
            try
            {
                SetStatus("Creando el respaldo previo al partido…", isError: false);
                backup = await _backupsViewModel.CreateAutomaticAsync(BackupReason.BeforeMatch);
            }
            finally
            {
                IsBusy = false;
            }

            if (backup is { IsSuccess: false } && !_prompts.Confirm(
                "Respaldo previo al partido",
                $"{backup.Message}\n\n¿Iniciar el partido igualmente, sin respaldo previo?"))
            {
                SetStatus("El partido no se inició: no se pudo crear el respaldo previo.", isError: true);
                return;
            }

            backupNote = backup switch
            {
                null => string.Empty,
                { IsSuccess: true } => " Respaldo previo creado.",
                _ => " Sin respaldo previo (falló).",
            };
        }

        await RunAsync(
            () => _operations.StartMatchAsync(new StartMatch(Guid.NewGuid(), match.MatchId, _operator, match.Version)),
            "Partido iniciado: periodo 1." + backupNote);
    }

    private bool CanStartMatch() => !IsBusy && _snapshot?.Status == MatchStatus.Scheduled;

    [RelayCommand(CanExecute = nameof(CanStartBreak))]
    private Task StartBreakAsync() => RunAsync(
        () => _operations.StartBreakAsync(new StartBreak(Guid.NewGuid(), _snapshot!.MatchId, _operator, _snapshot.Version)),
        "Descanso iniciado.");

    private bool CanStartBreak() => !IsBusy && _snapshot is { Status: MatchStatus.InProgress, Stage: MatchStage.Period };

    [RelayCommand(CanExecute = nameof(CanStartNextPeriod))]
    private async Task StartNextPeriodAsync()
    {
        var result = await RunAsync(
            () => _operations.StartNextPeriodAsync(new StartNextPeriod(Guid.NewGuid(), _snapshot!.MatchId, _operator, _snapshot.Version)),
            null);
        if (result is { Outcome: CommandOutcome.Applied, Snapshot: { } snapshot })
        {
            SetStatus(snapshot.IsOvertime ? $"Prórroga {snapshot.OvertimeNumber} iniciada." : "Nuevo periodo iniciado.", isError: false);
        }
    }

    private bool CanStartNextPeriod() => !IsBusy && _snapshot is { Status: MatchStatus.InProgress, Stage: MatchStage.Break };

    [RelayCommand(CanExecute = nameof(CanStartShootout))]
    private async Task StartShootoutAsync()
    {
        if (!_prompts.Confirm(
            "Tanda de penales",
            "¿Iniciar la tanda de penales? El resultado reglamentario queda fijo; los lanzamientos se registran aparte con F6 (local) y F9 (visita)."))
        {
            return;
        }

        await RunAsync(
            () => _operations.StartShootoutAsync(new StartShootout(Guid.NewGuid(), _snapshot!.MatchId, _operator, _snapshot.Version)),
            "Tanda de penales iniciada. Elija el lanzador y registre cada lanzamiento.");
    }

    private bool CanStartShootout() => !IsBusy && _snapshot is { CanStartShootout: true };

    [RelayCommand(CanExecute = nameof(CanCancelShootout))]
    private async Task CancelShootoutAsync()
    {
        var reason = _prompts.AskReason(
            "Anular la tanda",
            "Indique por qué se anula la tanda (por ejemplo, se inició por error). Antes deben anularse sus lanzamientos. El partido vuelve al descanso.");
        if (reason is null)
        {
            return;
        }

        await RunAsync(
            () => _operations.CancelShootoutAsync(new CancelShootout(Guid.NewGuid(), _snapshot!.MatchId, reason, _operator, _snapshot.Version)),
            "Tanda anulada: el partido volvió al descanso.");
    }

    private bool CanCancelShootout() => !IsBusy && _snapshot is { Status: MatchStatus.InProgress, Stage: MatchStage.Shootout };

    [RelayCommand(CanExecute = nameof(CanFinishMatch))]
    private async Task FinishMatchAsync()
    {
        if (!_prompts.Confirm("Finalizar partido", "¿Finalizar el partido? Para registrar cambios después habrá que reabrirlo con un motivo."))
        {
            return;
        }

        var result = await RunAsync(
            () => _operations.FinishMatchAsync(new FinishMatch(Guid.NewGuid(), _snapshot!.MatchId, _operator, _snapshot.Version)),
            "Partido finalizado.");
        if (result?.IsSuccess != true || _backupsViewModel is null)
        {
            return;
        }

        var backup = await _backupsViewModel.CreateAutomaticAsync(BackupReason.AfterMatch);
        if (backup is not null)
        {
            SetStatus(
                backup.IsSuccess
                    ? "Partido finalizado. Respaldo de cierre creado."
                    : $"Partido finalizado, pero no se pudo crear el respaldo de cierre: {backup.Message} Créelo desde «Resultados y respaldos».",
                isError: !backup.IsSuccess);
        }
    }

    private bool CanFinishMatch() => !IsBusy && _snapshot?.Status == MatchStatus.InProgress;

    [RelayCommand(CanExecute = nameof(CanReopenMatch))]
    private async Task ReopenMatchAsync()
    {
        var reason = _prompts.AskReason("Reabrir partido", "Indique el motivo de la reapertura. Quedará registrado con su nombre y la hora.");
        if (reason is null)
        {
            return;
        }

        await RunAsync(
            () => _operations.ReopenMatchAsync(new ReopenMatch(Guid.NewGuid(), _snapshot!.MatchId, reason, _operator, _snapshot.Version)),
            "Partido reabierto.");
    }

    private bool CanReopenMatch() => !IsBusy && _snapshot?.Status == MatchStatus.Finished;

    // ---------- Incidencias ----------

    [RelayCommand(CanExecute = nameof(CanGoalHome))]
    private Task GoalHomeAsync() => RegisterGoalAsync(SelectedHomePlayer);

    private bool CanGoalHome() => CanRegisterGoal() && SelectedHomePlayer is not null;

    [RelayCommand(CanExecute = nameof(CanGoalAway))]
    private Task GoalAwayAsync() => RegisterGoalAsync(SelectedAwayPlayer);

    private bool CanGoalAway() => CanRegisterGoal() && SelectedAwayPlayer is not null;

    private bool CanRegisterIncident() => !IsBusy && _snapshot?.Status == MatchStatus.InProgress;

    /// <summary>Durante la tanda no hay goles de juego: se registran lanzamientos.</summary>
    private bool CanRegisterGoal() => CanRegisterIncident() && _snapshot?.Stage != MatchStage.Shootout;

    [RelayCommand(CanExecute = nameof(CanIncidentHome))]
    private Task IncidentHomeAsync() => RegisterSelectedIncidentAsync(TeamSide.Home, SelectedHomePlayer);

    private bool CanIncidentHome() => CanRegisterSelected(SelectedHomePlayer);

    [RelayCommand(CanExecute = nameof(CanIncidentAway))]
    private Task IncidentAwayAsync() => RegisterSelectedIncidentAsync(TeamSide.Away, SelectedAwayPlayer);

    private bool CanIncidentAway() => CanRegisterSelected(SelectedAwayPlayer);

    private bool CanRegisterSelected(PlayerOption? player) => CanRegisterIncident() &&
        SelectedRegistrationOption is { } option && (option.Type == IncidentType.Timeout || player is not null);

    private async Task RegisterSelectedIncidentAsync(TeamSide side, PlayerOption? player)
    {
        if (_snapshot is null || SelectedRegistrationOption is not { } option)
        {
            return;
        }

        var command = new RegisterIncident(Guid.NewGuid(), _snapshot.MatchId, option.Type, side,
            option.Type == IncidentType.Timeout ? null : player?.RosterEntryId, _operator, _snapshot.Version,
            option.PenaltyResult, option.ExclusionKind);
        var result = await RunAsync(() => _operations.RegisterIncidentAsync(command), null);
        if (result is { Outcome: CommandOutcome.Applied, Snapshot: { } snapshot })
        {
            var incident = snapshot.Incidents.Single(i => i.IncidentId == command.CommandId);
            var team = side == TeamSide.Home ? snapshot.Home.Code : snapshot.Away.Code;
            if (option.Type == IncidentType.ShootoutAttempt)
            {
                // En la tanda el reloj no corre: no se avisa la falta de señal, se muestra el parcial.
                SetStatus($"{option.Label} · {team} registrado. {MatchTexts.Shootout(snapshot)}", isError: false);
                return;
            }

            var clockNotice = incident.Clock.Quality == ClockQuality.Live ? string.Empty : " SIN reloj: no había señal de tiempo vigente.";
            SetStatus($"{option.Label} · {team} registrado.{clockNotice}", isError: false);
        }
    }

    private async Task RegisterGoalAsync(PlayerOption? player)
    {
        if (player is null || _snapshot is null)
        {
            return;
        }

        // Un identificador nuevo por pulsación: reenviar el mismo comando no suma dos veces.
        var command = new RegisterGoal(Guid.NewGuid(), _snapshot.MatchId, player.Side, player.RosterEntryId, _operator, _snapshot.Version);
        var result = await RunAsync(() => _operations.RegisterGoalAsync(command), null);
        if (result is { Outcome: CommandOutcome.Applied, Snapshot: { } snapshot })
        {
            var incident = snapshot.Incidents.FirstOrDefault(i => i.IncidentId == command.CommandId);
            var withoutClock = incident is not null && incident.Clock.Quality != ClockQuality.Live;
            SetStatus(
                withoutClock
                    ? $"Gol de {player.LabelWithTeam} registrado SIN reloj: no había señal de tiempo vigente."
                    : $"Gol de {player.LabelWithTeam} registrado.",
                isError: false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCorrectIncident))]
    private async Task CorrectIncidentAsync()
    {
        if (SelectedIncident is not { } incident || SelectedCorrectionOption is not { } option || _snapshot is null)
        {
            return;
        }

        var result = await RunAsync(
            () => _operations.CorrectIncidentAsync(new CorrectIncident(
                Guid.NewGuid(), _snapshot.MatchId, incident.IncidentId,
                incident.Type == IncidentType.Timeout ? SelectedCorrectionTeam!.Side : SelectedCorrectionPlayer!.Side,
                incident.Type == IncidentType.Timeout ? null : SelectedCorrectionPlayer!.RosterEntryId,
                ReasonText, _operator, _snapshot.Version, option.PenaltyResult, option.ExclusionKind)),
            $"Incidencia n.º {incident.Sequence} corregida.");

        if (result?.Outcome == CommandOutcome.Applied)
        {
            ReasonText = string.Empty;
        }
    }

    private bool CanCorrectIncident() =>
        CanRegisterIncident() && SelectedIncident is { IsVoided: false } && SelectedCorrectionOption is not null
        && (CorrectionIsTimeout ? SelectedCorrectionTeam is not null : SelectedCorrectionPlayer is not null)
        && !string.IsNullOrWhiteSpace(ReasonText);

    [RelayCommand(CanExecute = nameof(CanVoidIncident))]
    private async Task VoidIncidentAsync()
    {
        if (SelectedIncident is not { } incident || _snapshot is null)
        {
            return;
        }

        var result = await RunAsync(
            () => _operations.VoidIncidentAsync(new VoidIncident(
                Guid.NewGuid(), _snapshot.MatchId, incident.IncidentId, ReasonText, _operator, _snapshot.Version)),
            $"Incidencia n.º {incident.Sequence} anulada.");

        if (result?.Outcome == CommandOutcome.Applied)
        {
            ReasonText = string.Empty;
        }
    }

    private bool CanVoidIncident() =>
        CanRegisterIncident() && SelectedIncident is { IsVoided: false } && !string.IsNullOrWhiteSpace(ReasonText);

    // ---------- Simulador (solo demo) ----------

    [RelayCommand(CanExecute = nameof(HasSimulator))]
    private void SimStart() => _simulation!.StartClock();

    [RelayCommand(CanExecute = nameof(HasSimulator))]
    private void SimStop() => _simulation!.StopClock();

    [RelayCommand(CanExecute = nameof(HasSimulator))]
    private void SimResetShot() => _simulation!.ResetShotClock();

    [RelayCommand(CanExecute = nameof(HasSimulator))]
    private void SimLoadPeriod()
    {
        var rules = _snapshot?.Rules;
        var overtime = _snapshot?.IsOvertime == true;
        var seconds = overtime ? rules!.OvertimePeriodDurationSeconds : rules?.PeriodDurationSeconds ?? 480;
        _simulation!.LoadPeriod(seconds, rules?.ShotClockSeconds ?? 30);
        SetStatus(
            overtime ? "Simulador: relojes cargados para una prórroga." : "Simulador: relojes cargados para un periodo nuevo.",
            isError: false);
    }

    /// <summary>Duración del tiempo muerto del simulador; la real la marca la consola.</summary>
    public const int SimulatedTimeoutSeconds = 60;

    [RelayCommand(CanExecute = nameof(HasSimulator))]
    private void SimStartTimeout()
    {
        _simulation!.StartTimeout(SimulatedTimeoutSeconds);
        SetStatus("Simulador: tiempo muerto de 60 s en curso; el reloj de partido se detuvo.", isError: false);
    }

    [RelayCommand(CanExecute = nameof(HasSimulator))]
    private void SimClearTimeout()
    {
        _simulation!.ClearTimeout();
        SetStatus("Simulador: tiempo muerto terminado.", isError: false);
    }

    [RelayCommand(CanExecute = nameof(HasSimulator))]
    private void SimDropSignal()
    {
        _simulation!.DropSignal();
        SetStatus("Simulador: se cortó la señal de tiempo.", isError: false);
    }

    [RelayCommand(CanExecute = nameof(HasSimulator))]
    private void SimRestoreSignal()
    {
        _simulation!.RestoreSignal();
        SetStatus("Simulador: señal de tiempo restablecida.", isError: false);
    }

    [RelayCommand(CanExecute = nameof(HasSimulator))]
    private void SimRunScript()
    {
        _simulation!.RunDemoScript();
        SetStatus("Simulador: guion demo en curso (30 s): juego, reset, pausa, pérdida de señal y reconexión.", isError: false);
    }

    // ---------- Reproducción (solo demo) ----------

    [RelayCommand(CanExecute = nameof(HasReplay))]
    private void RestartReplay()
    {
        _replay!.Restart();
        UpdateReplayText();
        SetStatus($"Reproducción reiniciada desde el comienzo de «{_replay.RecordingName}».", isError: false);
    }

    private void OnReplayProgressChanged(object? sender, EventArgs e) => _ui.Post(_ => UpdateReplayText(), null);

    private void UpdateReplayText()
    {
        if (_replay is not { } replay)
        {
            return;
        }

        var progress = replay.Progress;
        ReplayText = !progress.IsLoaded
            ? $"«{replay.RecordingName}»: no se pudo cargar (ver el aviso de la fuente de tiempo)."
            : progress.IsFinished
                ? $"«{replay.RecordingName}»: reproducción terminada ({ReplayTime(progress.Duration)}). Sin datos nuevos la señal figura perdida."
                : $"«{replay.RecordingName}»: {ReplayTime(progress.Position)} de {ReplayTime(progress.Duration)}.";
    }

    private static string ReplayTime(TimeSpan value) =>
        string.Create(MatchTexts.Culture, $"{(int)value.TotalMinutes}:{value.Seconds:00}");

    // ---------- Marcador ----------

    [RelayCommand]
    private void RefreshMonitors()
    {
        var previous = SelectedMonitor?.DeviceName;
        Monitors.Clear();
        foreach (var monitor in _scoreboard.GetMonitors())
        {
            Monitors.Add(monitor);
        }

        // Por defecto, la primera pantalla que no sea la principal (la del público).
        SelectedMonitor = Monitors.FirstOrDefault(m => m.DeviceName == previous)
            ?? Monitors.FirstOrDefault(m => !m.IsPrimary)
            ?? Monitors.FirstOrDefault();
    }

    /// <summary>
    /// Vuelve a leer las pantallas conectadas (tras enchufar o desenchufar el proyector o el LED)
    /// y comprueba el estado de conexiones. No mueve el marcador ni toca el partido.
    /// </summary>
    [RelayCommand]
    private void RedetectScreens()
    {
        RefreshMonitors();
        Connections?.CheckNowCommand.Execute(null);
        SetStatus(Monitors.Count == 1
            ? "Se detectó 1 pantalla. Para el marcador a pantalla completa conecte la del público y vuelva a detectar."
            : $"Se detectaron {Monitors.Count} pantallas; elija la del público en «Mesa de control» antes de abrir el marcador.", isError: false);
    }

    [RelayCommand]
    private void OpenScoreboard()
    {
        try
        {
            _scoreboard.Open(SelectedMonitor, ScoreboardFullScreen);
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo abrir el marcador: {ex.Message}", isError: true);
        }
    }

    [RelayCommand(CanExecute = nameof(IsScoreboardOpen))]
    private void CloseScoreboard() => _scoreboard.Close();

    /// <summary>Lleva a la pestaña de diagnóstico, donde está el estado de conexiones.</summary>
    [RelayCommand]
    private void ShowDiagnostics() => SelectedTabIndex = DiagnosticsTabIndex;

    /// <summary>Última actualización lanzada al abrir la pestaña de resultados (las pruebas la esperan).</summary>
    public Task ResultsTabRefresh { get; private set; } = Task.CompletedTask;

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == ResultsTabIndex)
        {
            ResultsTabRefresh = RefreshResultsTabAsync();
        }
    }

    private async Task RefreshResultsTabAsync()
    {
        if (Results is not null)
        {
            await Results.RefreshAsync();
        }

        if (_backupsViewModel is not null)
        {
            await _backupsViewModel.RefreshCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private void OpenDataFolder() => _prompts.OpenFolder(Info.DataDirectory);

    // ---------- Recepción de estado ----------

    private void OnSnapshotPublished(object? sender, MatchSnapshotPublished e) =>
        _ui.Post(_ => ApplySnapshot(e.Snapshot), null);

    /// <summary>
    /// Tras restaurar, la base es otra: se descarta el partido en memoria (su versión puede ser
    /// mayor que la restaurada) y se recargan la lista, el partido y el catálogo.
    /// </summary>
    public async Task ReloadAfterRestoreAsync()
    {
        ClearMatch();
        await InitializeAsync();
        if (_catalog is not null)
        {
            await _catalog.RefreshCommand.ExecuteAsync(null);
        }

        if (Results is not null)
        {
            await Results.RefreshAsync();
        }

        var opened = _snapshot is { } s ? $" Partido abierto: {s.Label}." : " No hay partidos en los datos restaurados.";
        SetStatus("Respaldo restaurado: se recargaron partidos, eventos y nóminas." + opened, isError: false);
    }

    private void ClearMatch()
    {
        _snapshot = null;
        _correctionIncidentId = null;
        PeriodMismatchText = null;
        HasMatch = false;
        MatchTitle = "Sin partido abierto";
        HomeName = "—";
        AwayName = "—";
        HomeScore = 0;
        AwayScore = 0;
        StageText = "—";
        RulesText = string.Empty;
        ShootoutText = string.Empty;
        HasShootout = false;
        IsShootoutStage = false;
        ShootoutHint = string.Empty;
        NextPeriodLabel = "_Siguiente periodo";
        ApplyRegistrationOptions(inShootout: false);
        HomeRoster.Clear();
        AwayRoster.Clear();
        CorrectionPlayers.Clear();
        Incidents.Clear();
        SelectedIncident = null;
        ApplyIdentity();
        RefreshCommands();
    }

    private void OnIdentityChanged(object? sender, ScoreboardIdentity identity) => _ui.Post(_ =>
    {
        _identity = identity;
        ApplyIdentity();
    }, null);

    /// <summary>Escudos y banderas solo si la identidad es la del partido abierto.</summary>
    private void ApplyIdentity()
    {
        var identity = _snapshot is { } snapshot && _identity?.MatchId == snapshot.MatchId ? _identity : null;
        HomeCrest = identity?.Home.Crest;
        HomeFlag = identity?.Home.Flag;
        AwayCrest = identity?.Away.Crest;
        AwayFlag = identity?.Away.Flag;
    }

    private void OnTimingChanged(object? sender, TimingView view) => _ui.Post(_ => ApplyTiming(view), null);

    private void OnScoreboardNotice(object? sender, string message) => _ui.Post(_ => SetStatus(message, isError: false), null);

    private void OnScoreboardStateChanged(object? sender, EventArgs e) => _ui.Post(_ =>
    {
        IsScoreboardOpen = _scoreboard.IsOpen;
        RefreshMonitors();
    }, null);

    private void ApplySnapshot(MatchSnapshot snapshot)
    {
        var previous = _snapshot;
        if (previous is not null && previous.MatchId == snapshot.MatchId && previous.Version > snapshot.Version)
        {
            return; // nunca se retrocede a una proyección más vieja
        }

        _snapshot = snapshot;
        UpdatePeriodMismatch();
        HasMatch = true;
        MatchTitle = $"{snapshot.TournamentName} · {snapshot.Label}";
        HomeName = snapshot.Home.Name;
        AwayName = snapshot.Away.Name;
        HomeColor = snapshot.Home.ColorHex;
        AwayColor = snapshot.Away.ColorHex;
        HomeCapColor = $"Gorro {snapshot.Home.CapColor.ToLowerInvariant()}";
        AwayCapColor = $"Gorro {snapshot.Away.CapColor.ToLowerInvariant()}";
        HomeScore = snapshot.Home.Score;
        AwayScore = snapshot.Away.Score;
        StageText = MatchTexts.DeskStage(snapshot);
        ApplyIdentity();
        RulesText = snapshot.Rules is { } rules
            ? $"Reglas del partido: {rules.PresetName} · {rules.PeriodCount} × {RulesTexts.Duration(rules.PeriodDurationSeconds)} · " +
              $"posesión {rules.ShotClockSeconds} s · {RulesTexts.Overtime(rules.OvertimePeriodCount, rules.OvertimePeriodDurationSeconds)} · " +
              $"tanda: {(rules.PenaltyShootoutEnabled ? "sí" : "no")}"
            : "Las reglas del evento se copian al iniciar el partido.";
        ShootoutText = MatchTexts.Shootout(snapshot);
        HasShootout = snapshot.Shootout is not null;
        IsShootoutStage = snapshot.Status == MatchStatus.InProgress && snapshot.Stage == MatchStage.Shootout;
        ShootoutHint = snapshot.ShootoutBlocker ?? (snapshot.CanStartShootout ? "Resultado empatado: se puede iniciar la tanda." : string.Empty);
        // Solo se nombra la prórroga si queda alguna por jugar; si no, el botón conserva su texto.
        NextPeriodLabel = snapshot.Rules is { } r && snapshot.CurrentPeriod >= r.PeriodCount
            && snapshot.CurrentPeriod < r.PeriodCount + r.OvertimePeriodCount
            ? (snapshot.IsOvertime ? "_Siguiente prórroga" : "Iniciar _prórroga")
            : "_Siguiente periodo";
        ApplyRegistrationOptions(IsShootoutStage);

        if (previous?.MatchId != snapshot.MatchId)
        {
            // La nómina del partido está congelada: solo se reconstruye al cambiar de partido.
            Fill(HomeRoster, snapshot.Home.Roster.Select(r => new PlayerOption(TeamSide.Home, r.RosterEntryId, r.Cap, r.PlayerName, snapshot.Home.Code)));
            Fill(AwayRoster, snapshot.Away.Roster.Select(r => new PlayerOption(TeamSide.Away, r.RosterEntryId, r.Cap, r.PlayerName, snapshot.Away.Code)));
            Fill(CorrectionPlayers, HomeRoster.Concat(AwayRoster).ToList());
            Fill(CorrectionTeams, new[] { new TeamOption(TeamSide.Home, snapshot.Home.Code), new TeamOption(TeamSide.Away, snapshot.Away.Code) });
            SelectedHomePlayer = null;
            SelectedAwayPlayer = null;
            SelectedCorrectionPlayer = null;
            ReasonText = string.Empty;
        }

        var selectedIncidentId = SelectedIncident?.IncidentId;
        Fill(Incidents, snapshot.Incidents.OrderByDescending(i => i.Sequence).Select(i => IncidentItem.From(snapshot, i)));
        SelectedIncident = Incidents.FirstOrDefault(i => i.IncidentId == selectedIncidentId);

        var listed = Matches.FirstOrDefault(m => m.MatchId == snapshot.MatchId);
        if (listed is not null && listed.Status != MatchTexts.Status(snapshot.Status))
        {
            var index = Matches.IndexOf(listed);
            Matches[index] = listed with { Status = MatchTexts.Status(snapshot.Status) };
            SelectedMatch = Matches[index];
        }

        RefreshCommands();
    }

    private void ApplyTiming(TimingView view)
    {
        GameClockText = ClockFormatter.FormatGameClock(view.GameClock, view.Precision);
        ShotClockText = ClockFormatter.FormatShotClock(view.ShotClock);
        SourceLabel = view.SourceLabel;
        SignalText = MatchTexts.Signal(view);
        RunningText = MatchTexts.Running(view);
        TimingNotice = view.Notice;
        IsSignalOk = view.Signal is SignalState.Live or SignalState.Intermission;
        IsSimulation = view.IsSimulation;
        UpdateReplayText();
        UpdatePeriodMismatch();
    }

    private void UpdatePeriodMismatch()
    {
        var view = _timing.Current;
        PeriodMismatchText = _snapshot is { Status: MatchStatus.InProgress, Stage: MatchStage.Period } match
            && view is { Signal: SignalState.Live, SourcePeriod: { } console }
            && console != match.CurrentPeriod
                ? $"La consola indica el periodo {console}; la mesa está en el periodo {match.CurrentPeriod}. Revise si falta pulsar «Siguiente periodo»."
                : null;
    }

    /// <summary>Cambia las acciones de F6/F9 al entrar o salir de la tanda, con una opción preseleccionada.</summary>
    private void ApplyRegistrationOptions(bool inShootout)
    {
        var wanted = inShootout ? IncidentOption.DuringShootout : IncidentOption.DuringPlay;
        if (RegistrationOptions.SequenceEqual(wanted))
        {
            return;
        }

        Fill(RegistrationOptions, wanted);
        SelectedRegistrationOption = wanted[0];
    }

    private async Task<CommandResult?> RunAsync(Func<Task<CommandResult>> action, string? successMessage)
    {
        if (IsBusy)
        {
            return null; // bloquea el doble envío accidental
        }

        IsBusy = true;
        try
        {
            var result = await action();
            switch (result.Outcome)
            {
                case CommandOutcome.Applied:
                case CommandOutcome.AlreadyApplied:
                    if (result.Snapshot is not null)
                    {
                        ApplySnapshot(result.Snapshot);
                    }

                    if (successMessage is not null)
                    {
                        SetStatus(successMessage, isError: false);
                    }

                    break;
                default:
                    SetStatus(result.Message ?? "La acción no se pudo completar.", isError: true);
                    break;
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus($"Error inesperado: {ex.Message}. El marcador no cambió.", isError: true);
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

    partial void OnIsBusyChanged(bool value) => RefreshCommands();

    partial void OnSelectedMatchChanged(MatchListItem? value)
    {
        OpenSelectedMatchCommand.NotifyCanExecuteChanged();
        if (_catalog is not null)
        {
            _catalog.ListedMatch = value;
        }
    }

    partial void OnSelectedHomePlayerChanged(PlayerOption? value)
    {
        GoalHomeCommand.NotifyCanExecuteChanged();
        IncidentHomeCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedAwayPlayerChanged(PlayerOption? value)
    {
        GoalAwayCommand.NotifyCanExecuteChanged();
        IncidentAwayCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedIncidentChanged(IncidentItem? value)
    {
        if (_correctionIncidentId != value?.IncidentId)
        {
            _correctionIncidentId = value?.IncidentId;
            Fill(CorrectionOptions, IncidentOption.All.Where(o => o.Type == value?.Type));
            SelectedCorrectionOption = CorrectionOptions.FirstOrDefault(o =>
                o.PenaltyResult == value?.PenaltyResult && o.ExclusionKind == value?.ExclusionKind);
            SelectedCorrectionPlayer = CorrectionPlayers.FirstOrDefault(p => p.RosterEntryId == value?.RosterEntryId);
            SelectedCorrectionTeam = CorrectionTeams.FirstOrDefault(t => t.Side == value?.Side);
            ReasonText = string.Empty;
        }

        OnPropertyChanged(nameof(CorrectionNeedsPlayer));
        OnPropertyChanged(nameof(CorrectionIsTimeout));
        OnPropertyChanged(nameof(HasSelectedIncident));
        RefreshCorrectionCommands();
    }

    partial void OnSelectedRegistrationOptionChanged(IncidentOption? value)
    {
        IncidentHomeCommand.NotifyCanExecuteChanged();
        IncidentAwayCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedCorrectionOptionChanged(IncidentOption? value) => RefreshCorrectionCommands();

    partial void OnSelectedCorrectionTeamChanged(TeamOption? value) => RefreshCorrectionCommands();

    partial void OnSelectedCorrectionPlayerChanged(PlayerOption? value) => RefreshCorrectionCommands();

    partial void OnReasonTextChanged(string value) => RefreshCorrectionCommands();

    partial void OnIsScoreboardOpenChanged(bool value) => CloseScoreboardCommand.NotifyCanExecuteChanged();

    private void RefreshCorrectionCommands()
    {
        CorrectIncidentCommand.NotifyCanExecuteChanged();
        VoidIncidentCommand.NotifyCanExecuteChanged();
    }

    private void RefreshCommands()
    {
        OpenSelectedMatchCommand.NotifyCanExecuteChanged();
        StartMatchCommand.NotifyCanExecuteChanged();
        StartBreakCommand.NotifyCanExecuteChanged();
        StartNextPeriodCommand.NotifyCanExecuteChanged();
        StartShootoutCommand.NotifyCanExecuteChanged();
        CancelShootoutCommand.NotifyCanExecuteChanged();
        FinishMatchCommand.NotifyCanExecuteChanged();
        ReopenMatchCommand.NotifyCanExecuteChanged();
        GoalHomeCommand.NotifyCanExecuteChanged();
        GoalAwayCommand.NotifyCanExecuteChanged();
        IncidentHomeCommand.NotifyCanExecuteChanged();
        IncidentAwayCommand.NotifyCanExecuteChanged();
        RefreshCorrectionCommands();
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
