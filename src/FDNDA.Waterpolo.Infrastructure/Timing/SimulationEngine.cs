namespace FDNDA.Waterpolo.Infrastructure.Timing;

/// <summary>Acciones del simulador; imitan lo que en operación real hace la consola.</summary>
public enum SimulationCommand
{
    StartClock = 1,
    StopClock = 2,
    ResetShotClock = 3,
    DropSignal = 4,
    RestoreSignal = 5,
}

/// <summary>Paso de un guion: aplicar <paramref name="Command"/> cuando el guion lleva <paramref name="At"/>.</summary>
public sealed record SimulationStep(TimeSpan At, SimulationCommand Command);

/// <summary>Guion reproducible de juego/pausa/reset/pérdida/reconexión.</summary>
public sealed record SimulationScript(IReadOnlyList<SimulationStep> Steps)
{
    /// <summary>Guion demo estándar (30 s): juego, reset, pausa, reanudación, pérdida y reconexión.</summary>
    public static SimulationScript Standard { get; } = new(
    [
        new(TimeSpan.FromSeconds(0), SimulationCommand.StartClock),
        new(TimeSpan.FromSeconds(6), SimulationCommand.ResetShotClock),
        new(TimeSpan.FromSeconds(10), SimulationCommand.StopClock),
        new(TimeSpan.FromSeconds(13), SimulationCommand.StartClock),
        new(TimeSpan.FromSeconds(16), SimulationCommand.DropSignal),
        new(TimeSpan.FromSeconds(21), SimulationCommand.RestoreSignal),
        new(TimeSpan.FromSeconds(26), SimulationCommand.ResetShotClock),
        new(TimeSpan.FromSeconds(30), SimulationCommand.StopClock),
    ]);
}

public sealed record SimulationOptions
{
    public int GameClockSeconds { get; init; } = 8 * 60;

    public int ShotClockSeconds { get; init; } = 30;

    /// <summary>Cada cuánto emite una muestra (también cuando el reloj está detenido).</summary>
    public TimeSpan TickInterval { get; init; } = TimeSpan.FromMilliseconds(100);
}

/// <summary>
/// Estado determinista de la «consola» simulada. No conoce hilos ni tiempo real: avanza
/// solo con el tiempo transcurrido que se le indica. Los relojes siguen contando durante
/// una pérdida de señal, igual que lo haría una consola real con el cable desconectado.
/// </summary>
public sealed class SimulationEngine
{
    private long _gameTicks;
    private long _shotTicks;
    private long _shotResetTicks;
    private long? _timeoutTicks;

    public SimulationEngine(SimulationOptions options)
    {
        Load(options.GameClockSeconds, options.ShotClockSeconds);
    }

    public bool IsRunning { get; private set; }

    public bool IsSignalUp { get; private set; } = true;

    /// <summary>Sesión de conexión; cambia en cada reconexión.</summary>
    public Guid SessionId { get; private set; } = Guid.NewGuid();

    public int GameClockRemainingMs => (int)(_gameTicks / TimeSpan.TicksPerMillisecond);

    public int ShotClockRemainingMs => (int)(_shotTicks / TimeSpan.TicksPerMillisecond);

    /// <summary>Reloj de tiempo muerto; nulo sin tiempo muerto. Llega a 0 y queda ahí hasta terminarlo.</summary>
    public int? TimeoutClockRemainingMs => _timeoutTicks is { } t ? (int)(t / TimeSpan.TicksPerMillisecond) : null;

    /// <summary>Inicia un tiempo muerto: detiene el reloj de partido; el de tiempo muerto corre aparte.</summary>
    public void StartTimeout(int seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);
        IsRunning = false;
        _timeoutTicks = TimeSpan.FromSeconds(seconds).Ticks;
    }

    /// <summary>Termina el tiempo muerto en curso, haya llegado o no a 0.</summary>
    public void ClearTimeout() => _timeoutTicks = null;

    public void Load(int gameClockSeconds, int shotClockSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gameClockSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(shotClockSeconds);
        IsRunning = false;
        _gameTicks = TimeSpan.FromSeconds(gameClockSeconds).Ticks;
        _shotResetTicks = TimeSpan.FromSeconds(shotClockSeconds).Ticks;
        _shotTicks = _shotResetTicks;
    }

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return;
        }

        if (_timeoutTicks is { } timeout)
        {
            _timeoutTicks = Math.Max(0, timeout - elapsed.Ticks);
        }

        if (!IsRunning)
        {
            return;
        }

        // El reloj de posesión solo corre mientras corre el de partido.
        var ticks = Math.Min(elapsed.Ticks, _gameTicks);
        _gameTicks -= ticks;
        _shotTicks = Math.Max(0, _shotTicks - ticks);

        if (_gameTicks == 0)
        {
            IsRunning = false; // fin del periodo
        }
    }

    public void Apply(SimulationCommand command)
    {
        switch (command)
        {
            case SimulationCommand.StartClock:
                IsRunning = _gameTicks > 0;
                break;
            case SimulationCommand.StopClock:
                IsRunning = false;
                break;
            case SimulationCommand.ResetShotClock:
                _shotTicks = _shotResetTicks;
                break;
            case SimulationCommand.DropSignal:
                IsSignalUp = false;
                break;
            case SimulationCommand.RestoreSignal:
                if (!IsSignalUp)
                {
                    IsSignalUp = true;
                    SessionId = Guid.NewGuid();
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }
}
