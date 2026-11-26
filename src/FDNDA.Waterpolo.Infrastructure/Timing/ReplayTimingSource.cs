using System.Text;
using System.Threading.Channels;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Domain.Common;

namespace FDNDA.Waterpolo.Infrastructure.Timing;

/// <summary>
/// Reproduce una grabación normalizada de cronometraje (R9). Siempre se identifica como
/// «REPRODUCCIÓN»: sirve para ensayar la mesa y el marcador sin la consola, no valida la lectura
/// del Colorado System 6. Determinista: cada muestra se entrega con la marca monotónica de su
/// instante programado (comienzo + t_ms), así que el resultado no depende de cuándo despierte el
/// bucle. Un hueco en la grabación es silencio (la mesa ve la pérdida de señal) y un cambio de
/// sesión es una reconexión. Si la grabación no se puede cargar, arrancar devuelve «no
/// disponible» con el motivo: nunca se pasa a otra fuente.
/// </summary>
public sealed class ReplayTimingSource : ITimingSource, IReplayControl
{
    public const string Label = "REPRODUCCIÓN";

    private static readonly TimeSpan DefaultTick = TimeSpan.FromMilliseconds(50);

    private readonly Func<TimingRecording> _load;
    private readonly TimeProvider _time;
    private readonly TimeSpan _tick;
    private readonly Channel<TimingSnapshot> _channel;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<int, Guid> _sessions = [];

    private TimingRecording? _recording;
    private Task? _loop;
    private long _runStart;
    private int _next;
    private long _sequence;
    private int _reportedSecond = -1;

    /// <summary>Reproduce el archivo indicado; se lee al arrancar.</summary>
    public ReplayTimingSource(string path, TimeProvider time, TimeSpan? tick = null)
        : this(() => Load(path), Path.GetFileName(path), time, tick)
    {
    }

    private ReplayTimingSource(Func<TimingRecording> load, string name, TimeProvider time, TimeSpan? tick)
    {
        _load = load;
        _time = time;
        _tick = tick ?? DefaultTick;
        RecordingName = name;
        _channel = Channel.CreateBounded<TimingSnapshot>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
    }

    /// <summary>Reproduce una grabación ya leída (pruebas).</summary>
    public static ReplayTimingSource FromRecording(TimingRecording recording, TimeProvider time, TimeSpan? tick = null)
    {
        ArgumentNullException.ThrowIfNull(recording);
        return new ReplayTimingSource(() => recording, recording.Name, time, tick);
    }

    public ClockSource Kind => ClockSource.Replay;

    public string DisplayLabel => Label;

    public string RecordingName { get; }

    public event EventHandler? ProgressChanged;

    public ReplayProgress Progress
    {
        get
        {
            lock (_sync)
            {
                if (_recording is not { } recording)
                {
                    return new ReplayProgress(TimeSpan.Zero, TimeSpan.Zero, IsLoaded: false);
                }

                var elapsed = _time.GetElapsedTime(_runStart);
                return new ReplayProgress(elapsed < recording.Duration ? elapsed : recording.Duration, recording.Duration, IsLoaded: true);
            }
        }
    }

    /// <summary>Lee y valida el archivo: UTF-8 y tamaño acotado.</summary>
    public static TimingRecording Load(string path)
    {
        var name = Path.GetFileName(path);
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new TimingRecordingException($"No existe el archivo «{name}».");
        }

        if (info.Length > TimingRecordingParser.MaxBytes)
        {
            throw new TimingRecordingException($"«{name}» supera los 8 MB permitidos para una grabación.");
        }

        string text;
        try
        {
            text = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
        }
        catch (DecoderFallbackException)
        {
            throw new TimingRecordingException($"«{name}» no está en UTF-8.");
        }

        return TimingRecordingParser.Parse(text, name);
    }

    public Task<TimingStartResult> StartAsync(CancellationToken ct = default)
    {
        TimingRecording recording;
        try
        {
            recording = _load();
        }
        catch (Exception ex) when (ex is TimingRecordingException or IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(TimingStartResult.Unavailable($"No se pudo cargar la reproducción «{RecordingName}». {ex.Message}"));
        }

        lock (_sync)
        {
            _recording = recording;
            BeginRun();
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

    public void Restart()
    {
        lock (_sync)
        {
            if (_recording is null)
            {
                return;
            }

            BeginRun();
        }

        Pump();
    }

    /// <summary>
    /// Entrega las muestras cuyo instante ya llegó. El bucle interno lo invoca en cada tick; las
    /// pruebas lo invocan directamente con un reloj controlable.
    /// </summary>
    /// <returns>Las muestras entregadas en esta llamada, en orden.</returns>
    public IReadOnlyList<TimingSnapshot> Pump()
    {
        var emitted = new List<TimingSnapshot>();
        var progressChanged = false;
        lock (_sync)
        {
            if (_recording is not { } recording)
            {
                return emitted;
            }

            var elapsed = _time.GetElapsedTime(_runStart);
            while (_next < recording.Samples.Count && recording.Samples[_next].At <= elapsed)
            {
                var sample = recording.Samples[_next++];
                var snapshot = new TimingSnapshot(
                    ClockSource.Replay,
                    SessionFor(sample.Session),
                    sample.GameClockRemainingMs,
                    sample.ShotClockRemainingMs,
                    sample.Precision,
                    sample.IsRunning,
                    sample.Period,
                    ++_sequence,
                    _runStart + (long)(sample.At.TotalSeconds * _time.TimestampFrequency),
                    _time.GetUtcNow(),
                    SampleQuality.Valid);
                _channel.Writer.TryWrite(snapshot);
                emitted.Add(snapshot);
            }

            // Avisa a la mesa una vez por segundo de grabación (también en cortes y al final).
            var second = (int)(elapsed < recording.Duration ? elapsed : recording.Duration).TotalSeconds;
            if (second != _reportedSecond)
            {
                _reportedSecond = second;
                progressChanged = true;
            }
        }

        if (progressChanged)
        {
            ProgressChanged?.Invoke(this, EventArgs.Empty);
        }

        return emitted;
    }

    /// <summary>Comienza una pasada: desde ahora, con sesiones nuevas (no hereda valores de la anterior).</summary>
    private void BeginRun()
    {
        _runStart = _time.GetTimestamp();
        _next = 0;
        _reportedSecond = -1;
        _sessions.Clear();
    }

    private Guid SessionFor(int session)
    {
        if (!_sessions.TryGetValue(session, out var id))
        {
            id = Guid.NewGuid();
            _sessions[session] = id;
        }

        return id;
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(_tick, _time);
        while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
        {
            Pump();
        }
    }
}
