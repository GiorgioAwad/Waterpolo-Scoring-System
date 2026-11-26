using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;
using FDNDA.Waterpolo.Infrastructure.Timing;

namespace FDNDA.Waterpolo.Infrastructure.Tests;

/// <summary>
/// Hito B: fuente de tiempo del Colorado System 6 sobre un transporte de solo recepción. El transporte
/// es de prueba (sin puerto serie); las tramas usan el formato observado en la consola de la FDNDA.
/// </summary>
public class ColoradoSourceTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Las_tramas_de_la_consola_llegan_como_muestras_de_la_fuente_Colorado()
    {
        var link = new FakeTransport();
        await using var source = Source([link], out _);
        Assert.True((await source.StartAsync()).IsAvailable);
        await using var samples = source.ReadSnapshotsAsync().GetAsyncEnumerator();

        var frame = CtswpDecoderTests.Frame(game: " 5:59", shot: "27", period: '2');
        link.Send(frame[..30]);
        link.Send(frame[30..]);
        var first = await Next(samples);
        link.Send(CtswpDecoderTests.Frame(game: " 5:58", shot: "26", period: '2'));
        var second = await Next(samples);

        Assert.Equal(ClockSource.ColoradoSystem6, first.Source);
        Assert.Equal((359_000, 27_000, 2), (first.GameClockRemainingMs, first.ShotClockRemainingMs, first.Period));
        Assert.Equal(TimingPrecision.Seconds, first.Precision);
        Assert.Null(first.IsRunning);
        Assert.Equal(SampleQuality.Valid, first.Quality);
        Assert.Equal(first.SessionId, second.SessionId);
        Assert.True(second.Sequence > first.Sequence);
        Assert.Equal(358_000, second.GameClockRemainingMs);
    }

    [Fact]
    public async Task Las_tramas_invalidas_no_se_publican_y_se_cuentan()
    {
        var link = new FakeTransport();
        await using var source = Source([link], out _);
        await source.StartAsync();
        await using var samples = source.ReadSnapshotsAsync().GetAsyncEnumerator();

        var invalid = CtswpDecoderTests.Frame(game: " 5:75");
        link.Send([.. invalid, .. CtswpDecoderTests.Frame(game: " 4:00")]);
        var sample = await Next(samples);

        Assert.Equal(240_000, sample.GameClockRemainingMs);
        Assert.Equal((1L, 1L), (source.ValidFrames, source.InvalidFrames));
    }

    [Fact]
    public async Task Al_perder_el_puerto_reconecta_con_una_sesion_nueva_y_sin_restos_de_la_anterior()
    {
        var first = new FakeTransport();
        var second = new FakeTransport();
        await using var source = Source([first, second], out var delays);
        await source.StartAsync();
        await using var samples = source.ReadSnapshotsAsync().GetAsyncEnumerator();

        first.Send(CtswpDecoderTests.Frame(game: " 3:00"));
        var before = await Next(samples);
        var cut = CtswpDecoderTests.Frame(game: " 2:59");
        first.Send(cut[..50]);
        first.Disconnect(); // cable desenchufado a mitad de una trama
        second.Send(cut[50..]);
        second.Send(CtswpDecoderTests.Frame(game: " 2:30"));
        var after = await Next(samples);

        Assert.Equal(180_000, before.GameClockRemainingMs);
        Assert.Equal(150_000, after.GameClockRemainingMs); // la mitad suelta no se unió a nada
        Assert.NotEqual(before.SessionId, after.SessionId);
        Assert.True(first.Disposed);
        Assert.Single(delays);
        Assert.Equal(1, source.Reconnections);
    }

    [Fact]
    public async Task Si_el_puerto_no_abre_sigue_intentando_con_espera_creciente_sin_declararse_no_disponible()
    {
        var missing1 = new FakeTransport { OpenFailure = new IOException("No existe COM5.") };
        var missing2 = new FakeTransport { OpenFailure = new UnauthorizedAccessException("COM5 está en uso.") };
        var link = new FakeTransport();
        await using var source = Source([missing1, missing2, link], out var delays);

        var start = await source.StartAsync();
        await using var samples = source.ReadSnapshotsAsync().GetAsyncEnumerator();
        link.Send(CtswpDecoderTests.Frame());
        var sample = await Next(samples);

        Assert.True(start.IsAvailable);
        Assert.Equal(360_000, sample.GameClockRemainingMs);
        Assert.Equal(2, delays.Count);
        Assert.True(delays[1] > delays[0]);
        Assert.True(missing1.Disposed && missing2.Disposed);
    }

    [Fact]
    public async Task La_espera_entre_intentos_tiene_un_tope()
    {
        var attempts = Enumerable.Range(0, 12).Select(_ => new FakeTransport { OpenFailure = new IOException("sin puerto") }).ToList();
        var link = new FakeTransport();
        await using var source = Source([.. attempts, link], out var delays);

        await source.StartAsync();
        await using var samples = source.ReadSnapshotsAsync().GetAsyncEnumerator();
        link.Send(CtswpDecoderTests.Frame());
        await Next(samples);

        Assert.Equal(12, delays.Count);
        Assert.Equal(ColoradoSystem6TimingSource.MaxRetryDelay, delays.Max());
    }

    [Fact]
    public async Task Un_mismo_fallo_repetido_se_anota_una_sola_vez_en_el_registro()
    {
        var attempts = Enumerable.Range(0, 6).Select(_ => new FakeTransport { OpenFailure = new IOException("No existe COM5.") }).ToList();
        var link = new FakeTransport();
        var log = new WarningLog();
        var next = 0;
        IReadOnlyList<FakeTransport> links = [.. attempts, link];
        await using var source = new ColoradoSystem6TimingSource(
            () => links[Math.Min(next++, links.Count - 1)], new ManualTimeProvider(), log, delay: (_, _) => Task.CompletedTask);

        await source.StartAsync();
        await using var samples = source.ReadSnapshotsAsync().GetAsyncEnumerator();
        link.Send(CtswpDecoderTests.Frame());
        await Next(samples);

        Assert.Single(log.Warnings, w => w.Contains("No existe COM5.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task En_el_descanso_de_la_mesa_el_silencio_de_la_consola_no_es_perdida_de_senal()
    {
        // La consola no emite durante el descanso (colorado-system6.md §7): la mesa manda.
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var time = new ManualTimeProvider();
        var link = new FakeTransport();
        var source = new ColoradoSystem6TimingSource(() => link, time, new NullLog(), delay: (_, _) => Task.CompletedTask);
        await using var monitor = new TimingMonitor(source, time, new TimingOptions(), new NullLog());
        monitor.FollowMatch(desk.Hub);
        await monitor.StartAsync();
        await desk.StartAsync();
        link.Send(CtswpDecoderTests.Frame(game: "  :00", period: '1'));
        await Until(() => monitor.Current is { Signal: SignalState.Live, SourcePeriod: 1 });

        await desk.Operations.StartBreakAsync(new StartBreak(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        time.AdvanceMs(30_000);
        await Until(() => monitor.Current.Signal == SignalState.Intermission);
        var inBreak = monitor.Current;

        await desk.Operations.StartNextPeriodAsync(new StartNextPeriod(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        await Until(() => monitor.Current.Signal == SignalState.Lost); // periodo en la mesa y la consola sigue callada

        link.Send(CtswpDecoderTests.Frame(game: " 6:00", period: '2'));
        await Until(() => monitor.Current is { Signal: SignalState.Live, SourcePeriod: 2 });

        Assert.Null(inBreak.Notice);
        Assert.False(inBreak.GameClock.IsCurrent);
    }

    [Fact]
    public async Task Al_cerrar_el_programa_no_se_anota_una_perdida_de_datos()
    {
        // El puerto serie real termina la recepción sin excepción al cancelarse.
        var link = new FakeTransport { EndsQuietlyOnCancel = true };
        var log = new WarningLog();
        var source = new ColoradoSystem6TimingSource(() => link, new ManualTimeProvider(), log, delay: (_, _) => Task.CompletedTask);
        await source.StartAsync();
        await using (var samples = source.ReadSnapshotsAsync().GetAsyncEnumerator())
        {
            link.Send(CtswpDecoderTests.Frame());
            await Next(samples);
        }

        await source.DisposeAsync();

        Assert.Empty(log.Warnings);
    }

    [Fact]
    public async Task Sin_puerto_configurado_queda_no_disponible_con_el_motivo()
    {
        await using var source = new ColoradoSystem6TimingSource();

        var start = await source.StartAsync();

        Assert.False(start.IsAvailable);
        Assert.Contains("--puerto", start.Reason, StringComparison.Ordinal);
    }

    private static ColoradoSystem6TimingSource Source(IReadOnlyList<FakeTransport> links, out List<TimeSpan> delays)
    {
        var recorded = new List<TimeSpan>();
        delays = recorded;
        var next = 0;
        return new ColoradoSystem6TimingSource(
            () => links[Math.Min(next++, links.Count - 1)],
            new ManualTimeProvider(),
            new NullLog(),
            delay: (span, _) =>
            {
                lock (recorded)
                {
                    recorded.Add(span);
                }

                return Task.CompletedTask;
            });
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("No se cumplió la condición esperada.");
            }

            await Task.Delay(20);
        }
    }

    private static async Task<TimingSnapshot> Next(IAsyncEnumerator<TimingSnapshot> samples)
    {
        var move = samples.MoveNextAsync().AsTask();
        if (await Task.WhenAny(move, Task.Delay(Wait)) != move)
        {
            throw new TimeoutException("No llegó ninguna muestra.");
        }

        Assert.True(await move);
        return samples.Current;
    }

    private sealed class WarningLog : FDNDA.Waterpolo.Application.Abstractions.IDiagnosticLog
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (_warnings)
                {
                    return [.. _warnings];
                }
            }
        }

        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
            lock (_warnings)
            {
                _warnings.Add(message);
            }
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }

    /// <summary>Transporte de prueba: entrega lo que se le manda y simula un cable desenchufado.</summary>
    private sealed class FakeTransport : IColoradoTransport
    {
        private readonly Channel<byte[]> _bytes = Channel.CreateUnbounded<byte[]>();

        public Exception? OpenFailure { get; init; }

        public bool EndsQuietlyOnCancel { get; init; }

        public bool Disposed { get; private set; }

        public string Description => "transporte de prueba";

        public Task OpenAsync(CancellationToken ct) => OpenFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;

        public void Send(byte[] bytes) => _bytes.Writer.TryWrite(bytes);

        public void Disconnect() => _bytes.Writer.TryComplete(new IOException("El puerto se cerró."));

        public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReceiveAsync([EnumeratorCancellation] CancellationToken ct)
        {
            var reader = _bytes.Reader;
            while (true)
            {
                bool more;
                try
                {
                    more = await reader.WaitToReadAsync(ct);
                }
                catch (OperationCanceledException) when (EndsQuietlyOnCancel)
                {
                    yield break;
                }

                if (!more)
                {
                    yield break;
                }

                while (reader.TryRead(out var bytes))
                {
                    yield return bytes;
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
