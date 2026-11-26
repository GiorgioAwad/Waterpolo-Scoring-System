using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Desktop.Presentation;
using FDNDA.Waterpolo.Domain.Common;

namespace FDNDA.Waterpolo.Desktop.ViewModels;

/// <summary>Gravedad de un elemento del estado de conexiones.</summary>
public enum ConnectionLevel
{
    /// <summary>No se usa en este perfil (p. ej. la consola en modo demo).</summary>
    Inactive,

    /// <summary>Conectado y funcionando.</summary>
    Ok,

    /// <summary>Funciona, pero algo requiere atención.</summary>
    Warning,

    /// <summary>Contingencia: desconectado o sin datos.</summary>
    Fault,
}

/// <summary>Estado del almacenamiento local, comprobado por <see cref="IStorageProbe"/>.</summary>
public sealed record StorageStatus(bool DatabaseFound, long? FreeBytes, string DriveName);

/// <summary>Comprueba la base de datos y el espacio libre del disco de datos.</summary>
public interface IStorageProbe
{
    StorageStatus Check();
}

/// <summary>Una fila del panel: qué se vigila, cómo está y por qué.</summary>
public sealed partial class ConnectionItem(string key, string name) : ObservableObject
{
    public string Key { get; } = key;

    public string Name { get; } = name;

    [ObservableProperty]
    public partial ConnectionLevel Level { get; set; } = ConnectionLevel.Inactive;

    [ObservableProperty]
    public partial string StateText { get; set; } = "—";

    [ObservableProperty]
    public partial string Detail { get; set; } = string.Empty;
}

/// <summary>Cambio de estado registrado en el historial de conexión.</summary>
public sealed record ConnectionEvent(string TimeText, ConnectionLevel Level, string Message);

/// <summary>
/// Panel «Estado de conexiones» del diagnóstico: consola, señal y relojes, marcador público,
/// pantallas y almacenamiento, con un estado general y el historial de contingencias de esta sesión.
/// Solo observa: no cambia la fuente de tiempo ni abre conexiones. Nunca muestra la consola como
/// conectada sin muestras válidas de ella.
/// </summary>
public sealed partial class ConnectionStatusViewModel : ObservableObject, IDisposable
{
    public const string ColoradoKey = "consola";
    public const string SignalKey = "senal";
    public const string GameClockKey = "reloj_partido";
    public const string ShotClockKey = "reloj_posesion";
    public const string ScoreboardKey = "marcador";
    public const string MonitorsKey = "pantallas";
    public const string StorageKey = "almacenamiento";

    /// <summary>Por debajo de este espacio libre el almacenamiento queda en aviso.</summary>
    public const long LowDiskBytes = 1L * 1024 * 1024 * 1024;

    private const int MaxHistory = 100;

    private readonly ITimingFeed _timing;
    private readonly IScoreboardHost _scoreboard;
    private readonly IStorageProbe _storage;
    private readonly TimeProvider _time;
    private readonly SynchronizationContext _ui;
    private readonly ITimer? _storageTimer;
    private TimingView _view;
    private SignalState? _previousSignal;
    private DateTimeOffset? _signalLostAtUtc;
    private bool _initialized;

    public ConnectionStatusViewModel(ITimingFeed timing, IScoreboardHost scoreboard, IStorageProbe storage, TimeProvider time, bool watchStorage = true)
    {
        _timing = timing;
        _scoreboard = scoreboard;
        _storage = storage;
        _time = time;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _view = timing.Current;

        Items =
        [
            new ConnectionItem(ColoradoKey, "Consola Colorado System 6"),
            new ConnectionItem(SignalKey, "Señal de tiempo"),
            new ConnectionItem(GameClockKey, "Reloj de partido"),
            new ConnectionItem(ShotClockKey, "Reloj de posesión"),
            new ConnectionItem(ScoreboardKey, "Marcador público"),
            new ConnectionItem(MonitorsKey, "Pantallas"),
            new ConnectionItem(StorageKey, "Almacenamiento local"),
        ];

        _timing.Changed += OnTimingChanged;
        _scoreboard.StateChanged += OnScoreboardChanged;
        _scoreboard.Notice += OnScoreboardNotice;
        Evaluate();
        _initialized = true;

        if (watchStorage)
        {
            _storageTimer = time.CreateTimer(_ => _ui.Post(__ => CheckStorage(), null), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }
    }

    public IReadOnlyList<ConnectionItem> Items { get; }

    /// <summary>Historial de cambios de esta sesión, el más reciente primero.</summary>
    public ObservableCollection<ConnectionEvent> History { get; } = [];

    [ObservableProperty]
    public partial ConnectionLevel OverallLevel { get; set; }

    [ObservableProperty]
    public partial string OverallText { get; set; } = string.Empty;

    /// <summary>Texto corto para la barra de estado.</summary>
    [ObservableProperty]
    public partial string ShortText { get; set; } = string.Empty;

    public ConnectionItem Item(string key) => Items.Single(i => i.Key == key);

    /// <summary>Vuelve a comprobar todo, incluido el almacenamiento.</summary>
    [RelayCommand]
    private void CheckNow()
    {
        Evaluate();
        Record(OverallLevel, "Comprobación manual: " + OverallText);
    }

    public void Dispose()
    {
        _storageTimer?.Dispose();
        _timing.Changed -= OnTimingChanged;
        _scoreboard.StateChanged -= OnScoreboardChanged;
        _scoreboard.Notice -= OnScoreboardNotice;
    }

    private void OnTimingChanged(object? sender, TimingView view) => _ui.Post(_ =>
    {
        _view = view;
        EvaluateTiming();
        UpdateOverall();
    }, null);

    private void OnScoreboardChanged(object? sender, EventArgs e) => _ui.Post(_ =>
    {
        EvaluateScreens();
        UpdateOverall();
    }, null);

    private void OnScoreboardNotice(object? sender, string message) => _ui.Post(_ =>
    {
        Record(ConnectionLevel.Warning, message);
        EvaluateScreens();
        UpdateOverall();
    }, null);

    private void CheckStorage()
    {
        EvaluateStorage();
        UpdateOverall();
    }

    private void Evaluate()
    {
        EvaluateTiming();
        EvaluateScreens();
        EvaluateStorage();
        UpdateOverall();
    }

    private void EvaluateTiming()
    {
        var view = _view;
        if (_previousSignal != view.Signal)
        {
            _signalLostAtUtc = view.Signal == SignalState.Lost ? _time.GetUtcNow() : null;
            _previousSignal = view.Signal;
        }

        var since = _signalLostAtUtc is { } lost ? $" desde las {Clock(lost)}" : string.Empty;

        // Consola: solo «conectada» si la fuente activa es la consola y hay muestras vigentes.
        if (view.Source != ClockSource.ColoradoSystem6)
        {
            Set(ColoradoKey, ConnectionLevel.Inactive, "No se usa en este perfil",
                $"La fuente de tiempo activa es «{view.SourceLabel}». La consola solo se lee con el perfil de producción y la opción --puerto.");
        }
        else
        {
            switch (view.Signal)
            {
                case SignalState.Live:
                    Set(ColoradoKey, ConnectionLevel.Ok, "Conectada", "Se reciben datos válidos de la consola.");
                    break;
                case SignalState.Waiting:
                    Set(ColoradoKey, ConnectionLevel.Warning, "Esperando datos", "La lectura inició, pero aún no llega ninguna muestra válida. Revise el cable y la consola.");
                    break;
                case SignalState.Lost:
                    Set(ColoradoKey, ConnectionLevel.Fault, "SIN DATOS" + since, "Dejaron de llegar datos de la consola. Revise el cable y la consola; los relojes se muestran sin señal.");
                    break;
                case SignalState.Intermission:
                    Set(ColoradoKey, ConnectionLevel.Ok, "En descanso", "La consola no envía datos durante el descanso entre periodos; vuelven al empezar el periodo siguiente.");
                    break;
                default:
                    Set(ColoradoKey, ConnectionLevel.Fault, "NO DISPONIBLE", view.Notice ?? "La lectura de la consola no está disponible.");
                    break;
            }
        }

        var source = $"Fuente: {view.SourceLabel}.";
        switch (view.Signal)
        {
            case SignalState.Live:
                Set(SignalKey, ConnectionLevel.Ok, "Vigente", $"{source} {MatchTexts.Running(view)}.");
                break;
            case SignalState.Waiting:
                Set(SignalKey, ConnectionLevel.Warning, "Esperando datos", $"{source} Aún no llega ninguna lectura.");
                break;
            case SignalState.Lost:
                Set(SignalKey, ConnectionLevel.Fault, "PERDIDA" + since, $"{source} No llegan lecturas vigentes; no se registra hora de juego en las incidencias.");
                break;
            case SignalState.Intermission:
                Set(SignalKey, ConnectionLevel.Ok, "Descanso", $"{source} Descanso entre periodos según la mesa: la fuente no envía tiempos.");
                break;
            default:
                Set(SignalKey, ConnectionLevel.Fault, "NO DISPONIBLE", $"{source} {view.Notice}");
                break;
        }

        EvaluateClock(GameClockKey, view.GameClock, ClockFormatter.GameClockUnavailable, view.Signal);
        EvaluateClock(ShotClockKey, view.ShotClock, ClockFormatter.ShotClockUnavailable, view.Signal);
    }

    private void EvaluateClock(string key, ClockReading reading, string placeholder, SignalState signal)
    {
        if (signal == SignalState.Intermission)
        {
            Set(key, ConnectionLevel.Inactive, "Descanso", $"Se muestra «{placeholder}» durante el descanso entre periodos.");
        }
        else if (reading.IsCurrent)
        {
            Set(key, ConnectionLevel.Ok, "Vigente", "Llegan lecturas recientes de este reloj.");
        }
        else if (signal == SignalState.Live)
        {
            Set(key, ConnectionLevel.Warning, "Sin lectura", $"Hay señal, pero no llegan lecturas de este reloj; se muestra «{placeholder}».");
        }
        else
        {
            Set(key, ConnectionLevel.Fault, "Sin señal", $"Se muestra «{placeholder}» hasta que vuelva la señal.");
        }
    }

    private void EvaluateScreens()
    {
        IReadOnlyList<MonitorOption> monitors;
        try
        {
            monitors = _scoreboard.GetMonitors();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            monitors = [];
        }

        if (monitors.Count >= 2)
        {
            Set(MonitorsKey, ConnectionLevel.Ok, $"{monitors.Count} pantallas", string.Join(" · ", monitors.Select(m => m.Label)));
        }
        else if (monitors.Count == 1)
        {
            Set(MonitorsKey, ConnectionLevel.Warning, "1 pantalla", "Solo hay una pantalla conectada: conecte la pantalla del público para el marcador a pantalla completa.");
        }
        else
        {
            Set(MonitorsKey, ConnectionLevel.Warning, "Sin datos", "No se pudo consultar las pantallas. Pulse «Actualizar pantallas».");
        }

        if (!_scoreboard.IsOpen)
        {
            Set(ScoreboardKey, ConnectionLevel.Warning, "Cerrado", "El público no ve el marcador. Ábralo desde la pestaña «Mesa de control».");
        }
        else if (_scoreboard.Placement is { } placement)
        {
            Set(ScoreboardKey,
                placement.FullScreen ? ConnectionLevel.Ok : ConnectionLevel.Warning,
                placement.FullScreen ? "Abierto a pantalla completa" : "Abierto en ventana",
                $"{placement.Monitor.Label}.{(placement.FullScreen ? string.Empty : " En ventana el público puede ver bordes y la barra de tareas.")}");
        }
        else
        {
            Set(ScoreboardKey, ConnectionLevel.Ok, "Abierto", "El marcador está abierto.");
        }
    }

    private void EvaluateStorage()
    {
        StorageStatus status;
        try
        {
            status = _storage.Check();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Set(StorageKey, ConnectionLevel.Fault, "NO ACCESIBLE", "No se pudo consultar la carpeta de datos. Revise el disco.");
            return;
        }

        var free = status.FreeBytes is { } bytes
            ? string.Create(MatchTexts.Culture, $"{bytes / (1024.0 * 1024 * 1024):0.0} GB libres en {status.DriveName}")
            : "espacio libre desconocido";
        if (!status.DatabaseFound)
        {
            Set(StorageKey, ConnectionLevel.Fault, "BASE NO ENCONTRADA", $"No se encuentra el archivo de la base de datos ({free}). Revise el disco o la carpeta de datos.");
        }
        else if (status.FreeBytes is { } left && left < LowDiskBytes)
        {
            Set(StorageKey, ConnectionLevel.Warning, "Poco espacio", $"Base accesible, pero quedan {free}. Libere espacio antes del partido.");
        }
        else
        {
            Set(StorageKey, ConnectionLevel.Ok, "Accesible", $"Base de datos encontrada · {free}");
        }
    }

    private void Set(string key, ConnectionLevel level, string state, string detail)
    {
        var item = Item(key);
        var changed = item.Level != level || item.StateText != state;
        item.Level = level;
        item.StateText = state;
        item.Detail = detail;
        if (changed && _initialized)
        {
            Record(level, $"{item.Name}: {state}");
        }
    }

    private void UpdateOverall()
    {
        var faults = Items.Where(i => i.Level == ConnectionLevel.Fault).ToList();
        var warnings = Items.Where(i => i.Level == ConnectionLevel.Warning).ToList();
        if (faults.Count > 0)
        {
            OverallLevel = ConnectionLevel.Fault;
            OverallText = "CONTINGENCIA: " + string.Join(" · ", faults.Select(f => $"{f.Name} ({f.StateText})"));
            ShortText = faults.Count == 1 ? "1 contingencia" : $"{faults.Count} contingencias";
        }
        else if (warnings.Count > 0)
        {
            OverallLevel = ConnectionLevel.Warning;
            OverallText = "Atención: " + string.Join(" · ", warnings.Select(w => $"{w.Name} ({w.StateText})"));
            ShortText = warnings.Count == 1 ? "1 aviso" : $"{warnings.Count} avisos";
        }
        else
        {
            OverallLevel = ConnectionLevel.Ok;
            OverallText = "Todo conectado y en orden.";
            ShortText = "Todo en orden";
        }
    }

    private void Record(ConnectionLevel level, string message)
    {
        History.Insert(0, new ConnectionEvent(Clock(_time.GetUtcNow()), level, message));
        while (History.Count > MaxHistory)
        {
            History.RemoveAt(History.Count - 1);
        }
    }

    private static string Clock(DateTimeOffset utc) => utc.ToLocalTime().ToString("HH:mm:ss", MatchTexts.Culture);
}
