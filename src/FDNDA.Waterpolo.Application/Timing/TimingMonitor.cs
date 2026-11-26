using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;

namespace FDNDA.Waterpolo.Application.Timing;

/// <summary>Lectura del estado de cronometraje para las ventanas.</summary>
public interface ITimingFeed
{
    TimingView Current { get; }

    /// <summary>Cambió la vista. Puede llegar desde cualquier hilo: despachar al hilo de UI.</summary>
    event EventHandler<TimingView>? Changed;
}

/// <summary>Captura el contexto de reloj para una incidencia en el momento del registro.</summary>
public interface IClockContextProvider
{
    ClockContext Capture();
}

/// <summary>
/// Consume la fuente de cronometraje en segundo plano, sin bloquear la UI, y publica una
/// vista con la vigencia de cada reloj. Reevalúa periódicamente para detectar la pérdida
/// de señal aunque no lleguen muestras.
/// </summary>
public sealed class TimingMonitor : ITimingFeed, IClockContextProvider, IAsyncDisposable
{
    private readonly ITimingSource _source;
    private readonly TimeProvider _time;
    private readonly TimingOptions _options;
    private readonly IDiagnosticLog _log;
    private readonly TimingStateTracker _tracker;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _sync = new();

    private TimingView _current;
    private IMatchSnapshotFeed? _matches;
    private Task? _readLoop;
    private Task? _evaluationLoop;

    public TimingMonitor(ITimingSource source, TimeProvider time, TimingOptions options, IDiagnosticLog log)
    {
        _source = source;
        _time = time;
        _options = options;
        _log = log;
        _tracker = new TimingStateTracker(source.Kind, source.DisplayLabel, time, options);
        _current = TimingView.Initial(source.Kind, source.DisplayLabel);
    }

    public TimingView Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public event EventHandler<TimingView>? Changed;

    /// <summary>
    /// Arranca la fuente configurada. Si no está disponible lo deja explícito: nunca cambia
    /// por su cuenta a demo ni a un reloj manual.
    /// </summary>
    public async Task StartAsync()
    {
        TimingStartResult result;
        try
        {
            result = await _source.StartAsync(_stop.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error("La fuente de tiempo no pudo iniciar.", ex);
            result = TimingStartResult.Unavailable("La fuente de tiempo no pudo iniciar. Revise el diagnóstico.");
        }

        if (!result.IsAvailable)
        {
            _tracker.MarkUnavailable(result.Reason ?? "Fuente de tiempo no disponible.");
            _log.Warn($"Fuente de tiempo {_source.Kind} no disponible: {result.Reason}");
            Refresh();
            return;
        }

        _log.Info($"Fuente de tiempo iniciada: {_source.Kind}.");
        _readLoop = Task.Run(ReadLoopAsync);
        _evaluationLoop = Task.Run(EvaluationLoopAsync);
    }

    /// <summary>
    /// Sigue el partido abierto en la mesa: mientras esté en descanso entre periodos, el silencio de
    /// la fuente se presenta como descanso y no como pérdida (la consola no emite en el descanso).
    /// </summary>
    public void FollowMatch(IMatchSnapshotFeed matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        _matches = matches;
        matches.Published += OnMatchPublished;
        ApplyStage(matches.Current);
    }

    public ClockContext Capture()
    {
        var view = _tracker.Evaluate();
        if (view.Signal == SignalState.Live && (view.GameClock.IsCurrent || view.ShotClock.IsCurrent))
        {
            return new ClockContext(view.Source, ClockQuality.Live, view.GameClock.RemainingMs, view.ShotClock.RemainingMs);
        }

        return ClockContext.Unavailable(view.Source);
    }

    public async ValueTask DisposeAsync()
    {
        if (_matches is not null)
        {
            _matches.Published -= OnMatchPublished;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        foreach (var loop in new[] { _readLoop, _evaluationLoop })
        {
            if (loop is null)
            {
                continue;
            }

            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // cierre normal
            }
        }

        try
        {
            await _source.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error("Error al detener la fuente de tiempo.", ex);
        }

        await _source.DisposeAsync().ConfigureAwait(false);
        _stop.Dispose();
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            await foreach (var snapshot in _source.ReadSnapshotsAsync(_stop.Token).ConfigureAwait(false))
            {
                if (_tracker.Accept(snapshot))
                {
                    Refresh();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // cierre normal
        }
        catch (Exception ex)
        {
            _log.Error("Se interrumpió la lectura de la fuente de tiempo.", ex);
        }
    }

    private async Task EvaluationLoopAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(_options.EvaluationInterval, _time);
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
            {
                Refresh();
            }
        }
        catch (OperationCanceledException)
        {
            // cierre normal
        }
    }

    private void OnMatchPublished(object? sender, MatchSnapshotPublished published) => ApplyStage(published.Snapshot);

    private void ApplyStage(MatchSnapshot? match)
    {
        _tracker.SetIntermission(match is { Status: MatchStatus.InProgress, Stage: MatchStage.Break });
        Refresh();
    }

    private void Refresh()
    {
        var view = _tracker.Evaluate();
        SignalState previousSignal;
        lock (_sync)
        {
            if (view == _current)
            {
                return;
            }

            previousSignal = _current.Signal;
            _current = view;
        }

        if (previousSignal != view.Signal)
        {
            _log.Info($"Señal de tiempo: {previousSignal} → {view.Signal}.");
        }

        Changed?.Invoke(this, view);
    }
}
