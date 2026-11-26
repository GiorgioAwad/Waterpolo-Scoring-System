using System.Threading.Channels;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Domain.Common;

namespace FDNDA.Waterpolo.Infrastructure.Timing;

/// <summary>
/// Fuente de tiempo simulada para desarrollo y demostración. Siempre se identifica como
/// «DEMO / SIMULACIÓN». Permite desarrollar y demostrar, pero no valida la lectura del
/// Colorado System 6 en producción.
/// </summary>
public sealed class SimulatedTimingSource : ITimingSource, ISimulationControl
{
    public const string Label = "DEMO / SIMULACIÓN";

    private readonly TimeProvider _time;
    private readonly SimulationOptions _options;
    private readonly SimulationEngine _engine;
    private readonly Channel<TimingSnapshot> _channel;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _stop = new();

    private Task? _loop;
    private long _lastPumpTimestamp;
    private long _sequence;
    private SimulationScript? _script;
    private TimeSpan _scriptElapsed;
    private int _scriptIndex;

    public SimulatedTimingSource(TimeProvider time, SimulationOptions? options = null)
    {
        _time = time;
        _options = options ?? new SimulationOptions();
        _engine = new SimulationEngine(_options);
        _lastPumpTimestamp = time.GetTimestamp();
        _channel = Channel.CreateBounded<TimingSnapshot>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
    }

    public ClockSource Kind => ClockSource.Simulated;

    public string DisplayLabel => Label;

    public Task<TimingStartResult> StartAsync(CancellationToken ct = default)
    {
        lock (_sync)
        {
            _lastPumpTimestamp = _time.GetTimestamp();
            _loop ??= Task.Run(RunAsync, CancellationToken.None);
        }

        return Task.FromResult(TimingStartResult.Started);
    }

    public async Task StopAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loop is { } loop)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // cierre normal
            }
        }

        _channel.Writer.TryComplete();
    }

    public IAsyncEnumerable<TimingSnapshot> ReadSnapshotsAsync(CancellationToken ct = default) =>
        _channel.Reader.ReadAllAsync(ct);

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _stop.Dispose();
    }

    public void StartClock() => Apply(SimulationCommand.StartClock);

    public void StopClock() => Apply(SimulationCommand.StopClock);

    public void ResetShotClock() => Apply(SimulationCommand.ResetShotClock);

    public void DropSignal() => Apply(SimulationCommand.DropSignal);

    public void RestoreSignal() => Apply(SimulationCommand.RestoreSignal);

    public void LoadPeriod(int gameClockSeconds, int shotClockSeconds)
    {
        lock (_sync)
        {
            AdvanceToNow();
            _script = null;
            _engine.Load(gameClockSeconds, shotClockSeconds);
            Emit();
        }
    }

    public void RunDemoScript() => RunScript(SimulationScript.Standard);

    public void StartTimeout(int seconds)
    {
        lock (_sync)
        {
            AdvanceToNow();
            _engine.StartTimeout(seconds);
            Emit();
        }
    }

    public void ClearTimeout()
    {
        lock (_sync)
        {
            AdvanceToNow();
            _engine.ClearTimeout();
            Emit();
        }
    }

    /// <summary>Inicia un guion desde este instante. Reemplaza al guion en curso.</summary>
    public void RunScript(SimulationScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        lock (_sync)
        {
            AdvanceToNow();
            _script = script;
            _scriptElapsed = TimeSpan.Zero;
            _scriptIndex = 0;
            ApplyDueScriptSteps();
            Emit();
        }
    }

    /// <summary>
    /// Avanza la simulación hasta el instante actual del reloj monotónico y emite una muestra
    /// si hay señal. El bucle interno lo invoca en cada tick; las pruebas lo invocan directamente
    /// con un reloj controlable.
    /// </summary>
    /// <returns>La muestra emitida, o nulo si no hay señal.</returns>
    public TimingSnapshot? Pump()
    {
        lock (_sync)
        {
            AdvanceToNow();
            return Emit();
        }
    }

    private void Apply(SimulationCommand command)
    {
        lock (_sync)
        {
            AdvanceToNow();
            _engine.Apply(command);
            Emit();
        }
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(_options.TickInterval, _time);
        while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
        {
            Pump();
        }
    }

    private void AdvanceToNow()
    {
        var now = _time.GetTimestamp();
        var remaining = _time.GetElapsedTime(_lastPumpTimestamp, now);
        _lastPumpTimestamp = now;

        // Aplica cada paso del guion en su instante exacto dentro del intervalo transcurrido.
        while (_script is { } script && _scriptIndex < script.Steps.Count)
        {
            var untilStep = script.Steps[_scriptIndex].At - _scriptElapsed;
            if (untilStep > remaining)
            {
                break;
            }

            if (untilStep > TimeSpan.Zero)
            {
                _engine.Advance(untilStep);
                _scriptElapsed += untilStep;
                remaining -= untilStep;
            }

            ApplyDueScriptSteps();
        }

        _engine.Advance(remaining);
        if (_script is not null)
        {
            _scriptElapsed += remaining;
        }
    }

    private void ApplyDueScriptSteps()
    {
        while (_script is { } script && _scriptIndex < script.Steps.Count && script.Steps[_scriptIndex].At <= _scriptElapsed)
        {
            _engine.Apply(script.Steps[_scriptIndex].Command);
            _scriptIndex++;
        }

        if (_script is { } current && _scriptIndex >= current.Steps.Count)
        {
            _script = null;
        }
    }

    private TimingSnapshot? Emit()
    {
        if (!_engine.IsSignalUp)
        {
            return null; // sin señal no se publica nada: el consumidor debe notar el silencio
        }

        var snapshot = new TimingSnapshot(
            ClockSource.Simulated,
            _engine.SessionId,
            _engine.GameClockRemainingMs,
            _engine.ShotClockRemainingMs,
            TimingPrecision.Tenths,
            _engine.IsRunning,
            Period: null,
            Sequence: ++_sequence,
            ReceivedTimestamp: _lastPumpTimestamp,
            ReceivedAtUtc: _time.GetUtcNow(),
            SampleQuality.Valid,
            _engine.TimeoutClockRemainingMs);

        _channel.Writer.TryWrite(snapshot);
        return snapshot;
    }
}
