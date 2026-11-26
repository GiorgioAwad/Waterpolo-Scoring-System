using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Desktop.ViewModels;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;
using static FDNDA.Waterpolo.Desktop.ViewModels.ConnectionStatusViewModel;

namespace FDNDA.Waterpolo.Desktop.Tests;

/// <summary>
/// Hito B: la consola Colorado en la mesa y en el diagnóstico. La consola no emite durante el descanso
/// entre periodos y manda su propio periodo (docs/colorado-system6.md §7).
/// </summary>
public class ColoradoDeskTests
{
    private static readonly TimingView ConsoleLive = new(
        ClockSource.ColoradoSystem6, "Colorado System 6", SignalState.Live, null,
        new ClockReading(360_000), new ClockReading(28_000), null, TimingPrecision.Seconds, SourcePeriod: 1);

    private static readonly TimingView ConsoleInBreak = ConsoleLive with
    {
        Signal = SignalState.Intermission,
        GameClock = ClockReading.NotAvailable,
        ShotClock = ClockReading.NotAvailable,
        SourcePeriod = null,
    };

    [Fact]
    public async Task En_el_descanso_la_mesa_lo_dice_sin_alarma_y_sin_tiempos()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var feed = new FakeTiming { Current = ConsoleLive };
        using var vm = Mesa(desk, storage, feed);

        feed.Raise(ConsoleInBreak);

        Assert.Equal("Descanso: la consola no envía tiempos", vm.SignalText);
        Assert.True(vm.IsSignalOk); // no se pinta como falla
        Assert.Null(vm.TimingNotice); // sin el aviso rojo de pérdida
        Assert.Equal(("--:--", "--"), (vm.GameClockText, vm.ShotClockText));
    }

    [Fact]
    public async Task La_mesa_avisa_si_el_periodo_de_la_consola_no_coincide_con_el_suyo_sin_cambiarlo()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var feed = new FakeTiming { Current = ConsoleLive };
        using var vm = Mesa(desk, storage, feed);
        await desk.StartAsync(); // la mesa queda en el periodo 1

        Assert.Null(vm.PeriodMismatchText);

        feed.Raise(ConsoleLive with { SourcePeriod = 2 });
        Assert.Equal("La consola indica el periodo 2; la mesa está en el periodo 1. Revise si falta pulsar «Siguiente periodo».", vm.PeriodMismatchText);
        Assert.Equal(1, desk.Hub.Current!.CurrentPeriod); // la mesa no cambia sola

        feed.Raise(ConsoleLive with { SourcePeriod = 1 });
        Assert.Null(vm.PeriodMismatchText);
    }

    [Fact]
    public async Task Sin_partido_en_juego_no_hay_aviso_de_periodo()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var feed = new FakeTiming { Current = ConsoleLive with { SourcePeriod = 3 } };
        using var vm = Mesa(desk, storage, feed);

        Assert.Null(vm.PeriodMismatchText);
    }

    [Fact]
    public void En_el_descanso_el_diagnostico_no_cuenta_una_contingencia()
    {
        var status = Status(new FakeTiming { Current = ConsoleLive }, out var timing);

        timing.Raise(ConsoleInBreak);

        var console = status.Item(ColoradoKey);
        Assert.Equal((ConnectionLevel.Ok, "En descanso"), (console.Level, console.StateText));
        Assert.Contains("descanso entre periodos", console.Detail, StringComparison.Ordinal);
        Assert.Equal((ConnectionLevel.Ok, "Descanso"), (status.Item(SignalKey).Level, status.Item(SignalKey).StateText));
        Assert.Equal(ConnectionLevel.Inactive, status.Item(GameClockKey).Level);
        Assert.Equal(ConnectionLevel.Inactive, status.Item(ShotClockKey).Level);
        Assert.DoesNotContain(status.History, e => e.Level is ConnectionLevel.Fault or ConnectionLevel.Warning);
    }

    private static MesaViewModel Mesa(TestDesk desk, TempStorage storage, FakeTiming feed) =>
        WithImmediateContext(() => new MesaViewModel(desk.Operations, desk.Queries, desk.Hub, feed, null, desk.Operator,
            new ScoreboardHost(), new Prompts(), new ScoreboardSettings(),
            new AppInfo("Prueba", AppProfile.Produccion, storage.Root, storage.Paths.DatabasePath, 4, "", desk.Operator.Name)));

    private static ConnectionStatusViewModel Status(FakeTiming feed, out FakeTiming timing)
    {
        timing = feed;
        return WithImmediateContext(() => new ConnectionStatusViewModel(feed, new ScoreboardHost(), new FakeStorage(), new ManualTimeProvider(), watchStorage: false));
    }

    private static T WithImmediateContext<T>(Func<T> create)
    {
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new ImmediateContext());
            return create();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class ImmediateContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }

    private sealed class ScoreboardHost : IScoreboardHost
    {
        public bool IsOpen => false;
        public IReadOnlyList<MonitorOption> GetMonitors() => [];
        public void Open(MonitorOption? monitor, bool fullScreen) { }
        public void Close() { }
        public event EventHandler<string>? Notice { add { } remove { } }
        public event EventHandler? StateChanged { add { } remove { } }
    }

    private sealed class Prompts : IPromptService
    {
        public string? AskReason(string title, string message) => "Motivo ficticio";
        public bool Confirm(string title, string message) => true;
        public void OpenFolder(string path) { }
    }

    private sealed class FakeStorage : IStorageProbe
    {
        public StorageStatus Check() => new(true, 80L * 1024 * 1024 * 1024, "G:");
    }

    private sealed class FakeTiming : ITimingFeed
    {
        public TimingView Current { get; set; } = null!;

        public event EventHandler<TimingView>? Changed;

        public void Raise(TimingView view)
        {
            Current = view;
            Changed?.Invoke(this, view);
        }
    }
}
