using System.Threading.Channels;
using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Infrastructure.Timing.Colorado;

namespace FDNDA.Waterpolo.Infrastructure.Timing;

// Adaptador de SOLO LECTURA del Colorado Time Systems System 6.
//
// Ruta de datos observada con la consola de la FDNDA (2026-11-24, docs/colorado-system6.md §7):
// COM PORT 2, 115200 baudios 8-N-1, tramas ASCII «CTSWP1.1c». Es evidencia propia, no documentación
// oficial. El programa nunca envía nada a la consola: el transporte no tiene operación de envío.
//
//   IColoradoTransport     → entrega bytes crudos de la ruta de datos (solo recepción).
//   CtswpFrameDecoder      → convierte bytes en lecturas; tolera tramas fragmentadas,
//                            concatenadas, inválidas y fuera de rango.

/// <summary>Transporte de solo recepción hacia la consola. No expone ninguna operación de envío.</summary>
public interface IColoradoTransport : IAsyncDisposable
{
    /// <summary>Descripción de la ruta física (puerto, parámetros), para diagnóstico.</summary>
    string Description { get; }

    /// <summary>Abre la ruta de datos en modo de solo lectura.</summary>
    Task OpenAsync(CancellationToken ct);

    /// <summary>Bloques de bytes tal como llegan; pueden cortar o unir tramas. Termina o falla si se pierde el puerto.</summary>
    IAsyncEnumerable<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken ct);
}

/// <summary>Lectura decodificada de la consola. Cada reloj es opcional y puede llegar en tramas distintas.</summary>
public sealed record ColoradoReading(
    int? GameClockRemainingMs, int? ShotClockRemainingMs, TimingPrecision Precision, bool? IsRunning, int? Period,
    int? TimeoutClockRemainingMs = null);

/// <summary>Decodificador incremental del protocolo de la consola. Sin estado de transporte.</summary>
public interface IColoradoFrameDecoder
{
    /// <summary>Alimenta bytes recibidos y devuelve las lecturas válidas completas que resulten.</summary>
    IReadOnlyList<ColoradoReading> Feed(ReadOnlySpan<byte> bytes);

    /// <summary>Descarta el estado parcial (p. ej. al reconectar).</summary>
    void Reset();
}

/// <summary>
/// Fuente de tiempo del Colorado System 6. Abre el transporte, decodifica las tramas y publica una
/// muestra por trama válida. Si el puerto no abre o se pierde, reintenta con espera creciente y cada
/// conexión nueva es una sesión nueva (no se mezclan valores). Sin puerto configurado, arrancar
/// devuelve «no disponible»: nunca simula éxito ni cambia a otra fuente.
/// </summary>
public sealed class ColoradoSystem6TimingSource : ITimingSource
{
    public const string NotConfiguredMessage =
        "Falta configurar el puerto de la consola Colorado: inicie el programa con «--puerto COMx» " +
        "(el puerto del adaptador USB a RS-232 conectado a COM PORT 2 de la consola).";

    public static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);

    private readonly Func<IColoradoTransport>? _transportFactory;
    private readonly TimeProvider _time;
    private readonly IDiagnosticLog? _log;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly CtswpFrameDecoder _decoder = new();
    private readonly Channel<TimingSnapshot> _channel = Channel.CreateBounded<TimingSnapshot>(new BoundedChannelOptions(256)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private long _sequence;
    private int _connections;
    private long _validFrames;
    private long _invalidFrames;

    /// <summary>Consola sin puerto configurado: arrancar devuelve «no disponible».</summary>
    public ColoradoSystem6TimingSource()
    {
        _time = TimeProvider.System;
        _delay = Task.Delay;
    }

    /// <param name="transportFactory">Crea un transporte nuevo en cada intento de conexión.</param>
    /// <param name="time">Reloj monotónico para marcar la recepción.</param>
    /// <param name="log">Registro de diagnóstico (estados de conexión y contadores).</param>
    /// <param name="delay">Espera entre intentos (pruebas); por defecto, <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/>.</param>
    public ColoradoSystem6TimingSource(
        Func<IColoradoTransport> transportFactory,
        TimeProvider time,
        IDiagnosticLog? log = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(transportFactory);
        ArgumentNullException.ThrowIfNull(time);
        _transportFactory = transportFactory;
        _time = time;
        _log = log;
        _delay = delay ?? ((span, ct) => Task.Delay(span, time, ct));
    }

    public ClockSource Kind => ClockSource.ColoradoSystem6;

    public string DisplayLabel => "Colorado System 6";

    /// <summary>Tramas válidas recibidas desde el arranque.</summary>
    public long ValidFrames => Interlocked.Read(ref _validFrames);

    /// <summary>Tramas descartadas desde el arranque (cortadas, inválidas o de otra versión).</summary>
    public long InvalidFrames => Interlocked.Read(ref _invalidFrames);

    /// <summary>Conexiones recuperadas después de la primera.</summary>
    public int Reconnections => Math.Max(0, Volatile.Read(ref _connections) - 1);

    public Task<TimingStartResult> StartAsync(CancellationToken ct = default)
    {
        if (_transportFactory is null)
        {
            return Task.FromResult(TimingStartResult.Unavailable(NotConfiguredMessage));
        }

        _loop ??= Task.Run(() => RunAsync(_stop.Token), CancellationToken.None);
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
                // detención normal
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

    private async Task RunAsync(CancellationToken ct)
    {
        var retry = FirstRetryDelay;
        string? lastFailure = null; // un mismo fallo repetido se anota una sola vez
        while (!ct.IsCancellationRequested)
        {
            var transport = _transportFactory!();
            try
            {
                await transport.OpenAsync(ct).ConfigureAwait(false);
                var session = Guid.NewGuid();
                _decoder.Reset();
                var connection = Interlocked.Increment(ref _connections);
                lastFailure = null;
                _log?.Info(connection == 1
                    ? $"Consola Colorado: {transport.Description} abierto."
                    : $"Consola Colorado: {transport.Description} abierto de nuevo (reconexión {connection - 1}).");

                await foreach (var chunk in transport.ReceiveAsync(ct).ConfigureAwait(false))
                {
                    if (Publish(chunk.Span, session))
                    {
                        retry = FirstRetryDelay;
                    }
                }

                if (ct.IsCancellationRequested)
                {
                    return; // cierre normal del programa
                }

                _log?.Warn($"Consola Colorado: {transport.Description} dejó de entregar datos.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or ArgumentException)
            {
                if (ex.Message != lastFailure)
                {
                    lastFailure = ex.Message;
                    _log?.Warn($"Consola Colorado: {transport.Description} no disponible ({ex.Message}). " +
                        $"Tramas válidas {ValidFrames}, descartadas {InvalidFrames}. Se reintenta cada {MaxRetryDelay.TotalSeconds:0} s como máximo.");
                }
            }
            finally
            {
                await transport.DisposeAsync().ConfigureAwait(false);
            }

            try
            {
                await _delay(retry, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            retry = retry * 2 < MaxRetryDelay ? retry * 2 : MaxRetryDelay;
        }
    }

    /// <summary>Decodifica un bloque y publica una muestra por lectura válida. Devuelve verdadero si hubo alguna.</summary>
    private bool Publish(ReadOnlySpan<byte> bytes, Guid session)
    {
        var readings = _decoder.Feed(bytes);
        Interlocked.Exchange(ref _validFrames, _decoder.ValidFrames);
        Interlocked.Exchange(ref _invalidFrames, _decoder.InvalidFrames);
        foreach (var reading in readings)
        {
            _channel.Writer.TryWrite(new TimingSnapshot(
                ClockSource.ColoradoSystem6,
                session,
                reading.GameClockRemainingMs,
                reading.ShotClockRemainingMs,
                reading.Precision,
                reading.IsRunning,
                reading.Period,
                Interlocked.Increment(ref _sequence),
                _time.GetTimestamp(),
                _time.GetUtcNow(),
                SampleQuality.Valid,
                reading.TimeoutClockRemainingMs));
        }

        return readings.Count > 0;
    }
}
