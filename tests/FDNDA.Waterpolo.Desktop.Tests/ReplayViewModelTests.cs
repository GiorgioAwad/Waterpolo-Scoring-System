using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Desktop.ViewModels;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;
using static FDNDA.Waterpolo.Desktop.ViewModels.ConnectionStatusViewModel;

namespace FDNDA.Waterpolo.Desktop.Tests;

/// <summary>R9 — la reproducción en el arranque, la mesa, el marcador y el diagnóstico, sin ventanas.</summary>
public class ReplayViewModelTests
{
    private static readonly TimingView ReplayLive = new(
        ClockSource.Replay, "REPRODUCCIÓN", SignalState.Live, null,
        new ClockReading(466_250), new ClockReading(22_250), true, TimingPrecision.Tenths);

    [Fact]
    public void La_reproduccion_se_elige_al_arrancar_solo_con_el_perfil_demo()
    {
        var demo = StartupOptions.Parse(["--perfil", "demo", "--reproduccion", @"C:\grabaciones\ensayo.csv"]);
        var defaultProfile = StartupOptions.Parse(["--reproducción", "ensayo.csv"]);
        var production = Assert.Throws<ArgumentException>(() =>
            StartupOptions.Parse(["--perfil", "produccion", "--reproduccion", "ensayo.csv"]));

        Assert.Equal((AppProfile.Demo, @"C:\grabaciones\ensayo.csv"), (demo.Profile, demo.ReplayPath));
        Assert.Equal("ensayo.csv", defaultProfile.ReplayPath);
        Assert.Null(StartupOptions.Parse(["--perfil", "demo"]).ReplayPath);
        Assert.Equal("La reproducción de cronometraje solo se usa con el perfil demo.", production.Message);
    }

    [Fact]
    public async Task La_mesa_muestra_el_avance_de_la_reproduccion_y_la_reinicia()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var feed = new FakeTiming { Current = ReplayLive };
        var replay = new FakeReplay { Progress = new ReplayProgress(TimeSpan.FromSeconds(12.4), TimeSpan.FromSeconds(38), IsLoaded: true) };
        using var vm = WithImmediateContext(() => new MesaViewModel(desk.Operations, desk.Queries, desk.Hub, feed, null, desk.Operator,
            new ScoreboardHost(), new Prompts(), new ScoreboardSettings(),
            new AppInfo("Prueba", AppProfile.Demo, storage.Root, storage.Paths.DatabasePath, 4, "", desk.Operator.Name))
        {
            Replay = replay,
        });

        Assert.True(vm.HasReplay);
        Assert.False(vm.HasSimulator);
        Assert.Equal(("REPRODUCCIÓN", true), (vm.SourceLabel, vm.IsSimulation));
        Assert.Equal("«guion.csv»: 0:12 de 0:38.", vm.ReplayText);

        feed.Raise(ReplayLive with { Signal = SignalState.Lost, GameClock = ClockReading.NotAvailable, ShotClock = ClockReading.NotAvailable });
        replay.Advance(new ReplayProgress(TimeSpan.FromSeconds(35), TimeSpan.FromSeconds(38), IsLoaded: true));
        Assert.Equal("«guion.csv»: 0:35 de 0:38.", vm.ReplayText);

        // Al final ya no cambia la vista del reloj (sigue sin señal): el avance llega por su propio aviso.
        replay.Advance(new ReplayProgress(TimeSpan.FromSeconds(38), TimeSpan.FromSeconds(38), IsLoaded: true));
        Assert.Equal("«guion.csv»: reproducción terminada (0:38). Sin datos nuevos la señal figura perdida.", vm.ReplayText);
        Assert.Equal(("--:--", "--"), (vm.GameClockText, vm.ShotClockText));

        Assert.True(vm.RestartReplayCommand.CanExecute(null));
        vm.RestartReplayCommand.Execute(null);

        Assert.Equal(1, replay.Restarts);
        Assert.Equal("«guion.csv»: 0:00 de 0:38.", vm.ReplayText);
        Assert.Equal("Reproducción reiniciada desde el comienzo de «guion.csv».", vm.StatusMessage);
    }

    [Fact]
    public async Task Sin_grabacion_cargada_la_mesa_lo_dice_y_el_simulador_no_aparece()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var feed = new FakeTiming
        {
            Current = new TimingView(ClockSource.Replay, "REPRODUCCIÓN", SignalState.Unavailable, "No se pudo cargar la reproducción «guion.csv». Línea 2: …",
                ClockReading.NotAvailable, ClockReading.NotAvailable, null, TimingPrecision.Unknown),
        };
        using var vm = WithImmediateContext(() => new MesaViewModel(desk.Operations, desk.Queries, desk.Hub, feed, null, desk.Operator,
            new ScoreboardHost(), new Prompts(), new ScoreboardSettings(),
            new AppInfo("Prueba", AppProfile.Demo, storage.Root, storage.Paths.DatabasePath, 4, "", desk.Operator.Name))
        {
            Replay = new FakeReplay { Progress = new ReplayProgress(TimeSpan.Zero, TimeSpan.Zero, IsLoaded: false) },
        });

        Assert.Equal("«guion.csv»: no se pudo cargar (ver el aviso de la fuente de tiempo).", vm.ReplayText);
        Assert.StartsWith("No se pudo cargar la reproducción", vm.TimingNotice, StringComparison.Ordinal);
        Assert.False(vm.HasSimulator);
    }

    [Fact]
    public void El_marcador_muestra_REPRODUCCION_y_el_diagnostico_no_da_la_consola_por_conectada()
    {
        var feed = new FakeTiming { Current = ReplayLive };
        using var scoreboard = WithImmediateContext(() => new MarcadorViewModel(new Application.Matches.MatchSnapshotHub(), feed, new ScoreboardSettings()));
        using var status = WithImmediateContext(() => new ConnectionStatusViewModel(feed, new ScoreboardHost(), new FakeStorage(), new ManualTimeProvider(), watchStorage: false));

        Assert.Equal((true, "REPRODUCCIÓN"), (scoreboard.ShowModeBadge, scoreboard.ModeBadge));
        Assert.Equal(("7:46", "23"), (scoreboard.GameClockText, scoreboard.ShotClockText)); // formato vigente: décimas solo en el último minuto; posesión hacia arriba
        var console = status.Item(ColoradoKey);
        Assert.Equal(ConnectionLevel.Inactive, console.Level);
        Assert.Contains("«REPRODUCCIÓN»", console.Detail, StringComparison.Ordinal);
        Assert.Equal(ConnectionLevel.Ok, status.Item(SignalKey).Level);
        Assert.Contains("Fuente: REPRODUCCIÓN.", status.Item(SignalKey).Detail, StringComparison.Ordinal);
    }

    // ---------- Apoyo ----------

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

    private sealed class FakeReplay : IReplayControl
    {
        public string RecordingName => "guion.csv";

        public ReplayProgress Progress { get; set; }

        public int Restarts { get; private set; }

        public event EventHandler? ProgressChanged;

        public void Advance(ReplayProgress progress)
        {
            Progress = progress;
            ProgressChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Restart()
        {
            Restarts++;
            Progress = Progress with { Position = TimeSpan.Zero };
        }
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
}
