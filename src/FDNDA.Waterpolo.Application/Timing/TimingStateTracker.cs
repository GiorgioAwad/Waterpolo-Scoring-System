using FDNDA.Waterpolo.Domain.Common;

namespace FDNDA.Waterpolo.Application.Timing;

/// <summary>Estado de la señal de cronometraje.</summary>
public enum SignalState
{
    /// <summary>La fuente no pudo iniciar (p. ej. adaptador pendiente de validación).</summary>
    Unavailable = 0,

    /// <summary>Fuente iniciada; aún no llegó ninguna muestra válida.</summary>
    Waiting = 1,

    /// <summary>Hay muestras recientes.</summary>
    Live = 2,

    /// <summary>Hubo señal, pero dejó de llegar.</summary>
    Lost = 3,

    /// <summary>
    /// Descanso entre periodos según la mesa: la fuente calla y no es una pérdida. La consola Colorado
    /// no emite durante el descanso (docs/colorado-system6.md §7). Los relojes tampoco se muestran.
    /// </summary>
    Intermission = 4,
}

/// <summary>
/// Lectura de un reloj para mostrar. Si no está vigente no lleva valor: la interfaz
/// muestra «--:--» o «--» en lugar de aparentar continuidad.
/// </summary>
public readonly record struct ClockReading(int? RemainingMs)
{
    public bool IsCurrent => RemainingMs is not null;

    public static ClockReading NotAvailable => new(null);
}

/// <summary>Vista inmutable del cronometraje para la mesa y el marcador.</summary>
/// <param name="SourcePeriod">Periodo que informa la fuente, solo mientras la señal está vigente.</param>
/// <param name="TimeoutClock">Reloj de tiempo muerto vigente; sin valor si no hay tiempo muerto o no hay señal.</param>
/// <param name="TimeoutsEnded">
/// Tiempos muertos terminados vistos en vivo desde que existe la fuente (sube de a uno). El marcador anima
/// cuando sube, no al abrirse.
/// </param>
public sealed record TimingView(
    ClockSource Source,
    string SourceLabel,
    SignalState Signal,
    string? Notice,
    ClockReading GameClock,
    ClockReading ShotClock,
    bool? IsRunning,
    TimingPrecision Precision,
    int? SourcePeriod = null,
    ClockReading TimeoutClock = default,
    int TimeoutsEnded = 0)
{
    public bool IsSimulation => Source is ClockSource.Simulated or ClockSource.Replay;

    public static TimingView Initial(ClockSource source, string label) =>
        new(source, label, SignalState.Waiting, null, ClockReading.NotAvailable, ClockReading.NotAvailable, null, TimingPrecision.Unknown);
}

/// <summary>Umbrales provisionales; los definitivos se fijan con medidas reales del equipo.</summary>
public sealed record TimingOptions
{
    /// <summary>Antigüedad máxima de una lectura para considerarla vigente.</summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>Cada cuánto se reevalúa la vigencia aunque no lleguen muestras.</summary>
    public TimeSpan EvaluationInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Valor máximo aceptado para un reloj; lo que exceda se descarta como fuera de rango.</summary>
    public int MaxClockMs { get; init; } = 2 * 60 * 60 * 1000;
}

/// <summary>
/// Mantiene la validez y la frescura de cada reloj por separado. Usa el reloj monotónico
/// para medir antigüedad. No extrapola tiempos: solo muestra lo último recibido mientras
/// siga vigente. Descarta lecturas antiguas, repetidas, inválidas o de una sesión anterior.
/// </summary>
public sealed class TimingStateTracker
{
    private readonly TimeProvider _time;
    private readonly TimingOptions _options;
    private readonly ClockSource _source;
    private readonly string _label;
    private readonly object _sync = new();

    private bool _unavailable;
    private string? _unavailableReason;
    private Guid? _sessionId;
    private long _lastSequence = long.MinValue;
    private long? _lastAcceptedTimestamp;
    private (int Value, long Timestamp)? _gameClock;
    private (int Value, long Timestamp)? _shotClock;
    private (int Value, long Timestamp)? _timeoutClock;
    private int _timeoutsEnded;
    private bool? _isRunning;
    private int? _period;
    private bool _intermission;
    private TimingPrecision _precision = TimingPrecision.Unknown;

    public TimingStateTracker(ClockSource source, string label, TimeProvider time, TimingOptions options)
    {
        _source = source;
        _label = label;
        _time = time;
        _options = options;
    }

    /// <summary>Marca la fuente como no disponible con un motivo explícito.</summary>
    public void MarkUnavailable(string reason)
    {
        lock (_sync)
        {
            _unavailable = true;
            _unavailableReason = reason;
            _gameClock = null;
            _shotClock = null;
            _timeoutClock = null;
            _isRunning = null;
        }
    }

    /// <summary>
    /// La mesa indica si el partido está en descanso entre periodos. En descanso, el silencio de la
    /// fuente se presenta como <see cref="SignalState.Intermission"/> en lugar de una pérdida.
    /// </summary>
    public void SetIntermission(bool intermission)
    {
        lock (_sync)
        {
            _intermission = intermission;
        }
    }

    /// <summary>Procesa una muestra. Devuelve falso si se descartó.</summary>
    public bool Accept(TimingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_sync)
        {
            if (_unavailable || snapshot.Source != _source || snapshot.Quality != SampleQuality.Valid)
            {
                return false;
            }

            if (snapshot.Sequence <= _lastSequence)
            {
                return false; // repetida o fuera de orden
            }

            if (_time.GetElapsedTime(snapshot.ReceivedTimestamp) > _options.StaleAfter)
            {
                return false; // lectura antigua entregada tarde
            }

            if (!InRange(snapshot.GameClockRemainingMs) || !InRange(snapshot.ShotClockRemainingMs)
                || !InRange(snapshot.TimeoutClockRemainingMs))
            {
                return false;
            }

            if (snapshot.GameClockRemainingMs is null && snapshot.ShotClockRemainingMs is null)
            {
                return false; // no aporta ningún reloj
            }

            if (_sessionId != snapshot.SessionId)
            {
                // Sesión nueva (reconexión): no se mezclan valores de la sesión anterior.
                _sessionId = snapshot.SessionId;
                _gameClock = null;
                _shotClock = null;
                _timeoutClock = null;
                _isRunning = null;
                _period = null;
            }

            // Fin del tiempo muerto: la muestra anterior (misma sesión, sin corte) lo traía en marcha y
            // esta lo trae en 0 o ya no lo trae. Una reconexión o una pérdida no cuentan como fin.
            if (_timeoutClock is { Value: > 0 } previous
                && _time.GetElapsedTime(previous.Timestamp, snapshot.ReceivedTimestamp) <= _options.StaleAfter
                && snapshot.TimeoutClockRemainingMs is null or 0)
            {
                _timeoutsEnded++;
            }

            _timeoutClock = snapshot.TimeoutClockRemainingMs is { } timeout ? (timeout, snapshot.ReceivedTimestamp) : null;

            _lastSequence = snapshot.Sequence;
            _period = snapshot.Period ?? _period;
            _lastAcceptedTimestamp = snapshot.ReceivedTimestamp;
            _precision = snapshot.Precision;
            _isRunning = snapshot.IsRunning;

            if (snapshot.GameClockRemainingMs is { } game)
            {
                _gameClock = (game, snapshot.ReceivedTimestamp);
            }

            if (snapshot.ShotClockRemainingMs is { } shot)
            {
                _shotClock = (shot, snapshot.ReceivedTimestamp);
            }

            return true;
        }
    }

    /// <summary>Evalúa la vigencia de cada reloj en este instante.</summary>
    public TimingView Evaluate()
    {
        lock (_sync)
        {
            if (_unavailable)
            {
                return new TimingView(
                    _source, _label, SignalState.Unavailable, _unavailableReason,
                    ClockReading.NotAvailable, ClockReading.NotAvailable, null, TimingPrecision.Unknown);
            }

            if (_lastAcceptedTimestamp is not { } last)
            {
                return new TimingView(
                    _source, _label, SignalState.Waiting, "Esperando datos de la fuente de tiempo.",
                    ClockReading.NotAvailable, ClockReading.NotAvailable, null, TimingPrecision.Unknown);
            }

            var live = IsFresh(last);
            if (!live && _intermission)
            {
                return new TimingView(
                    _source, _label, SignalState.Intermission, null,
                    ClockReading.NotAvailable, ClockReading.NotAvailable, null, _precision, TimeoutsEnded: _timeoutsEnded);
            }

            return new TimingView(
                _source,
                _label,
                live ? SignalState.Live : SignalState.Lost,
                live ? null : "Señal de tiempo no disponible.",
                Read(_gameClock),
                Read(_shotClock),
                live ? _isRunning : null,
                _precision,
                live ? _period : null,
                Read(_timeoutClock),
                _timeoutsEnded);
        }
    }

    private ClockReading Read((int Value, long Timestamp)? clock) =>
        clock is { } c && IsFresh(c.Timestamp) ? new ClockReading(c.Value) : ClockReading.NotAvailable;

    private bool IsFresh(long timestamp) => _time.GetElapsedTime(timestamp) <= _options.StaleAfter;

    private bool InRange(int? value) => value is null || (value >= 0 && value <= _options.MaxClockMs);
}
