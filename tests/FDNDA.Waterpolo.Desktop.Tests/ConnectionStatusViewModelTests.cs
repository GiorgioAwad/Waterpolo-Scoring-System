using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Desktop.ViewModels;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;
using static FDNDA.Waterpolo.Desktop.ViewModels.ConnectionStatusViewModel;

namespace FDNDA.Waterpolo.Desktop.Tests;

/// <summary>Panel «Estado de conexiones» del diagnóstico, sin ventanas ni hardware.</summary>
public class ConnectionStatusViewModelTests
{
    private static readonly MonitorOption Primary = new(@"\\.\DISPLAY1", 0, 0, 1920, 1080, true, 1);
    private static readonly MonitorOption Public = new(@"\\.\DISPLAY2", 1920, 0, 1920, 1080, false, 2);

    [Fact]
    public void Con_simulador_marcador_a_pantalla_completa_y_dos_pantallas_todo_esta_en_orden()
    {
        var (status, _, _, _) = Create(Live(ClockSource.Simulated), open: new ScoreboardPlacement(Public, FullScreen: true));

        Assert.Equal(ConnectionLevel.Ok, status.OverallLevel);
        Assert.Equal("Todo conectado y en orden.", status.OverallText);
        Assert.Equal("Todo en orden", status.ShortText);
        var console = status.Item(ColoradoKey);
        Assert.Equal(ConnectionLevel.Inactive, console.Level); // en demo la consola no se usa: no se muestra «conectada»
        Assert.Contains("DEMO / SIMULACIÓN", console.Detail, StringComparison.Ordinal);
        Assert.Equal("Abierto a pantalla completa", status.Item(ScoreboardKey).StateText);
        Assert.Equal("2 pantallas", status.Item(MonitorsKey).StateText);
        Assert.Empty(status.History); // el estado inicial no se registra como contingencia
    }

    [Fact]
    public void Perder_la_señal_es_contingencia_con_hora_y_al_volver_queda_en_el_historial()
    {
        var (status, timing, _, time) = Create(Live(ClockSource.Simulated), open: new ScoreboardPlacement(Public, true));

        timing.Raise(Live(ClockSource.Simulated) with { Signal = SignalState.Lost, GameClock = ClockReading.NotAvailable, ShotClock = ClockReading.NotAvailable });

        Assert.Equal(ConnectionLevel.Fault, status.OverallLevel);
        var signal = status.Item(SignalKey);
        Assert.Equal(ConnectionLevel.Fault, signal.Level);
        Assert.StartsWith("PERDIDA desde las ", signal.StateText, StringComparison.Ordinal);
        Assert.Equal(ConnectionLevel.Fault, status.Item(GameClockKey).Level);
        Assert.Contains("--:--", status.Item(GameClockKey).Detail, StringComparison.Ordinal);
        Assert.Equal(ConnectionLevel.Fault, status.Item(ShotClockKey).Level);
        Assert.StartsWith("CONTINGENCIA: Señal de tiempo", status.OverallText, StringComparison.Ordinal);
        Assert.Equal("3 contingencias", status.ShortText);
        Assert.Contains(status.History, e => e.Level == ConnectionLevel.Fault && e.Message.StartsWith("Señal de tiempo: PERDIDA", StringComparison.Ordinal));

        time.Advance(TimeSpan.FromSeconds(5));
        timing.Raise(Live(ClockSource.Simulated));

        Assert.Equal(ConnectionLevel.Ok, status.OverallLevel);
        Assert.Equal("Señal de tiempo: Vigente", status.History.First(e => e.Message.StartsWith("Señal", StringComparison.Ordinal)).Message);
    }

    [Fact]
    public void Con_la_consola_pendiente_de_validacion_se_muestra_no_disponible_y_nunca_conectada()
    {
        var unavailable = new TimingView(ClockSource.ColoradoSystem6, "Colorado System 6", SignalState.Unavailable,
            "PENDIENTE DE VALIDACIÓN EN HARDWARE: …", ClockReading.NotAvailable, ClockReading.NotAvailable, null, TimingPrecision.Unknown);
        var (status, _, _, _) = Create(unavailable, open: new ScoreboardPlacement(Public, true));

        var console = status.Item(ColoradoKey);
        Assert.Equal(ConnectionLevel.Fault, console.Level);
        Assert.Equal("NO DISPONIBLE", console.StateText);
        Assert.StartsWith("PENDIENTE DE VALIDACIÓN EN HARDWARE", console.Detail, StringComparison.Ordinal);
        Assert.Equal(ConnectionLevel.Fault, status.OverallLevel);
    }

    [Fact]
    public void La_consola_solo_figura_conectada_cuando_llegan_datos_vigentes_de_ella()
    {
        var (status, timing, _, _) = Create(Live(ClockSource.ColoradoSystem6) with { Signal = SignalState.Waiting, GameClock = ClockReading.NotAvailable, ShotClock = ClockReading.NotAvailable },
            open: new ScoreboardPlacement(Public, true));
        Assert.Equal("Esperando datos", status.Item(ColoradoKey).StateText);
        Assert.Equal(ConnectionLevel.Warning, status.Item(ColoradoKey).Level);

        timing.Raise(Live(ClockSource.ColoradoSystem6));
        Assert.Equal(("Conectada", ConnectionLevel.Ok), (status.Item(ColoradoKey).StateText, status.Item(ColoradoKey).Level));

        timing.Raise(Live(ClockSource.ColoradoSystem6) with { Signal = SignalState.Lost, GameClock = ClockReading.NotAvailable, ShotClock = ClockReading.NotAvailable });
        Assert.StartsWith("SIN DATOS", status.Item(ColoradoKey).StateText, StringComparison.Ordinal);
        Assert.Equal(ConnectionLevel.Fault, status.Item(ColoradoKey).Level);
    }

    [Fact]
    public void Si_falta_un_reloj_con_señal_vigente_queda_en_aviso()
    {
        var (status, _, _, _) = Create(Live(ClockSource.Simulated) with { ShotClock = ClockReading.NotAvailable }, open: new ScoreboardPlacement(Public, true));

        Assert.Equal(ConnectionLevel.Ok, status.Item(GameClockKey).Level);
        Assert.Equal(("Sin lectura", ConnectionLevel.Warning), (status.Item(ShotClockKey).StateText, status.Item(ShotClockKey).Level));
        Assert.Equal(ConnectionLevel.Warning, status.OverallLevel);
        Assert.Equal("1 aviso", status.ShortText);
    }

    [Fact]
    public void Marcador_cerrado_en_ventana_o_con_una_sola_pantalla_son_avisos_y_los_cambios_se_registran()
    {
        var (status, _, scoreboard, _) = Create(Live(ClockSource.Simulated), open: null);
        Assert.Equal(("Cerrado", ConnectionLevel.Warning), (status.Item(ScoreboardKey).StateText, status.Item(ScoreboardKey).Level));

        scoreboard.Placement = new ScoreboardPlacement(Primary, FullScreen: false);
        scoreboard.RaiseChanged();
        Assert.Equal("Abierto en ventana", status.Item(ScoreboardKey).StateText);

        // Se desconecta la pantalla del público.
        scoreboard.Monitors = [Primary];
        scoreboard.RaiseNotice("Se desconectó la pantalla del marcador: se pasó a modo ventana en la pantalla principal.");
        Assert.Equal(("1 pantalla", ConnectionLevel.Warning), (status.Item(MonitorsKey).StateText, status.Item(MonitorsKey).Level));
        Assert.Contains(status.History, e => e.Message.StartsWith("Se desconectó la pantalla", StringComparison.Ordinal));
        Assert.Contains(status.History, e => e.Message == "Pantallas: 1 pantalla");
    }

    [Fact]
    public void El_almacenamiento_avisa_con_poco_espacio_y_es_contingencia_sin_la_base()
    {
        var storage = new FakeStorage { Status = new StorageStatus(true, 500L * 1024 * 1024, "G:") };
        var (status, _, _, _) = Create(Live(ClockSource.Simulated), open: new ScoreboardPlacement(Public, true), storage);
        Assert.Equal(("Poco espacio", ConnectionLevel.Warning), (status.Item(StorageKey).StateText, status.Item(StorageKey).Level));
        Assert.Contains("0.5 GB libres en G:", status.Item(StorageKey).Detail.Replace(',', '.'), StringComparison.Ordinal);

        storage.Status = new StorageStatus(false, 80L * 1024 * 1024 * 1024, "G:");
        status.CheckNowCommand.Execute(null);

        Assert.Equal(ConnectionLevel.Fault, status.Item(StorageKey).Level);
        Assert.Equal(ConnectionLevel.Fault, status.OverallLevel);
        Assert.StartsWith("Comprobación manual: CONTINGENCIA", status.History[0].Message, StringComparison.Ordinal);
    }

    // ---------- Apoyo ----------

    private static TimingView Live(ClockSource source) => new(
        source, source == ClockSource.Simulated ? "DEMO / SIMULACIÓN" : "Colorado System 6", SignalState.Live, null,
        new ClockReading(412_300), new ClockReading(18_700), true, TimingPrecision.Tenths);

    private static (ConnectionStatusViewModel Status, FakeTiming Timing, FakeScoreboard Scoreboard, ManualTimeProvider Time) Create(
        TimingView initial, ScoreboardPlacement? open, FakeStorage? storage = null)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new ImmediateContext());
        try
        {
            var timing = new FakeTiming { Current = initial };
            var scoreboard = new FakeScoreboard { Placement = open, Monitors = [Primary, Public] };
            var time = new ManualTimeProvider();
            var status = new ConnectionStatusViewModel(
                timing, scoreboard, storage ?? new FakeStorage(), time, watchStorage: false);
            return (status, timing, scoreboard, time);
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

    private sealed class FakeScoreboard : IScoreboardHost
    {
        public IReadOnlyList<MonitorOption> Monitors { get; set; } = [];

        public ScoreboardPlacement? Placement { get; set; }

        public bool IsOpen => Placement is not null;

        public IReadOnlyList<MonitorOption> GetMonitors() => Monitors;

        public void Open(MonitorOption? monitor, bool fullScreen)
        {
        }

        public void Close()
        {
        }

        public event EventHandler<string>? Notice;

        public event EventHandler? StateChanged;

        public void RaiseChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

        public void RaiseNotice(string message) => Notice?.Invoke(this, message);
    }

    private sealed class FakeStorage : IStorageProbe
    {
        public StorageStatus Status { get; set; } = new(true, 80L * 1024 * 1024 * 1024, "G:");

        public StorageStatus Check() => Status;
    }
}
