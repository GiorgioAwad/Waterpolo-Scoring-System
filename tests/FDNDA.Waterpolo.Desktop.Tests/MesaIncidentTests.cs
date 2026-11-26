using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Desktop.ViewModels;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;

namespace FDNDA.Waterpolo.Desktop.Tests;

/// <summary>ViewModels reales sobre SQLite temporal, sin crear ventanas ni usar esperas de reloj.</summary>
public class MesaIncidentTests
{
    [Fact]
    public async Task El_tiempo_muerto_se_registra_sin_jugador_aunque_haya_un_jugador_seleccionado()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        await desk.StartAsync();
        using var vm = CreateViewModel(storage, desk);
        await vm.InitializeAsync();
        vm.SelectedRegistrationOption = vm.RegistrationOptions.Single(o => o.Type == IncidentType.Timeout);
        Assert.False(vm.HasSelectedIncident);
        Assert.True(vm.IncidentHomeCommand.CanExecute(null));
        Assert.False(vm.GoalHomeCommand.CanExecute(null));
        vm.SelectedHomePlayer = vm.HomeRoster[0];
        await vm.IncidentHomeCommand.ExecuteAsync(null);
        Assert.Null(Assert.Single((await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!.Incidents).RosterEntryId);
        vm.SelectedIncident = Assert.Single(vm.Incidents);
        Assert.True(vm.HasSelectedIncident);
        Assert.True(vm.CorrectionIsTimeout);
        Assert.False(vm.CorrectionNeedsPlayer);
        vm.SelectedCorrectionTeam = vm.CorrectionTeams.Single(t => t.Side == TeamSide.Away);
        vm.ReasonText = "Lo pidió la visita";
        await vm.CorrectIncidentCommand.ExecuteAsync(null);
        Assert.Equal(TeamSide.Away, Assert.Single((await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!.Incidents).Side);
        Assert.False(vm.IsStatusError);
    }

    [Theory]
    [InlineData(IncidentType.Exclusion)]
    [InlineData(IncidentType.PersonalFoul)]
    [InlineData(IncidentType.Penalty)]
    public async Task Las_acciones_de_jugador_se_habilitan_al_elegir_nomina_y_se_deshabilitan_al_cerrar(IncidentType type)
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        using var vm = CreateViewModel(storage, desk);
        await vm.InitializeAsync();
        vm.SelectedRegistrationOption = vm.RegistrationOptions.First(o => o.Type == type);
        vm.SelectedHomePlayer = vm.HomeRoster[0];
        Assert.False(vm.IncidentHomeCommand.CanExecute(null));
        await vm.StartMatchCommand.ExecuteAsync(null);
        Assert.True(vm.IncidentHomeCommand.CanExecute(null));
        Assert.False(vm.IncidentAwayCommand.CanExecute(null));
        vm.SelectedAwayPlayer = vm.AwayRoster[0];
        Assert.True(vm.IncidentAwayCommand.CanExecute(null));
        await vm.IncidentAwayCommand.ExecuteAsync(null);
        Assert.Equal(type, Assert.Single((await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!.Incidents).Type);
        await vm.FinishMatchCommand.ExecuteAsync(null);
        Assert.False(vm.IncidentHomeCommand.CanExecute(null));
        Assert.False(vm.IncidentAwayCommand.CanExecute(null));
    }

    [Fact]
    public async Task Corregir_resultado_del_penal_actualiza_ambas_vistas_y_conserva_historial()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        await desk.StartAsync();
        using var vm = CreateViewModel(storage, desk);
        using var scoreboard = WithImmediateContext(() => new MarcadorViewModel(desk.Hub, new FixedTiming(),
            new ScoreboardSettings { AnimationsEnabled = false }));
        await vm.InitializeAsync();
        vm.SelectedHomePlayer = vm.HomeRoster[0];
        vm.SelectedRegistrationOption = vm.RegistrationOptions.Single(o => o.PenaltyResult == PenaltyResult.Converted);
        await vm.IncidentHomeCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.HomeScore);
        Assert.Equal("1", scoreboard.HomeScoreText);
        vm.SelectedIncident = Assert.Single(vm.Incidents);
        Assert.Equal("Penal convertido", vm.SelectedIncident.TypeText);
        Assert.All(vm.CorrectionOptions, o => Assert.Equal(IncidentType.Penalty, o.Type));
        Assert.False(vm.CorrectIncidentCommand.CanExecute(null)); // motivo obligatorio
        vm.SelectedCorrectionOption = vm.CorrectionOptions.Single(o => o.PenaltyResult == PenaltyResult.NotConverted);
        vm.ReasonText = "No fue gol";
        await vm.CorrectIncidentCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.HomeScore);
        Assert.Equal("0", scoreboard.HomeScoreText);
        Assert.False(scoreboard.IsBannerVisible);
        Assert.Empty(vm.ReasonText);
        Assert.Equal(2, Assert.Single(vm.Incidents).History.Count);
        Assert.Contains("Penal no convertido", vm.Incidents[0].History[1], StringComparison.Ordinal);
        vm.ReasonText = "Anulado";
        await vm.VoidIncidentCommand.ExecuteAsync(null);
        Assert.True(Assert.Single(vm.Incidents).IsVoided);
        Assert.False(vm.CorrectIncidentCommand.CanExecute(null));
        Assert.False(vm.VoidIncidentCommand.CanExecute(null));
    }

    [Fact]
    public async Task Corregir_exclusion_a_expulsion_muestra_la_decision_sin_cambiar_el_marcador()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        await desk.StartAsync();
        using var vm = CreateViewModel(storage, desk);
        await vm.InitializeAsync();
        vm.SelectedHomePlayer = vm.HomeRoster[0];
        await vm.IncidentHomeCommand.ExecuteAsync(null);
        vm.SelectedIncident = Assert.Single(vm.Incidents);
        vm.SelectedCorrectionOption = vm.CorrectionOptions.Single(o => o.ExclusionKind == ExclusionKind.Expulsion);
        vm.ReasonText = "Decisión arbitral registrada";
        await vm.CorrectIncidentCommand.ExecuteAsync(null);
        Assert.Equal("Expulsión", Assert.Single(vm.Incidents).TypeText);
        Assert.Equal(0, vm.HomeScore);
        Assert.True(vm.GoalHomeCommand.CanExecute(null)); // la aplicación no aplica sanciones
    }

    [Fact]
    public async Task Un_fallo_de_guardado_conserva_el_marcador_y_muestra_error_en_la_mesa()
    {
        await using var storage = await TempStorage.CreateAsync();
        var failing = new FailingSaveRepository(new SqliteMatchRepository(storage.OpenDatabase())) { FailSaves = false };
        var desk = await new TestDesk(storage, failing).SignInAsync(storage);
        await desk.StartAsync();
        using var vm = CreateViewModel(storage, desk);
        await vm.InitializeAsync();
        vm.SelectedHomePlayer = vm.HomeRoster[0];
        vm.SelectedRegistrationOption = vm.RegistrationOptions.Single(o => o.PenaltyResult == PenaltyResult.Converted);
        failing.FailSaves = true;
        await vm.IncidentHomeCommand.ExecuteAsync(null);
        Assert.True(vm.IsStatusError);
        Assert.Contains("No se pudo guardar", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(0, vm.HomeScore);
        Assert.Empty(vm.Incidents);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Mientras_se_guarda_no_se_envia_una_segunda_accion_desde_el_otro_equipo()
    {
        await using var storage = await TempStorage.CreateAsync();
        var blocked = new PausedRepository(new SqliteMatchRepository(storage.OpenDatabase()));
        var desk = await new TestDesk(storage, blocked).SignInAsync(storage);
        await desk.StartAsync();
        using var vm = CreateViewModel(storage, desk);
        await vm.InitializeAsync();
        vm.SelectedRegistrationOption = vm.RegistrationOptions.Single(o => o.Type == IncidentType.Timeout);
        blocked.Pause = true;
        var first = vm.IncidentHomeCommand.ExecuteAsync(null);
        await blocked.Entered.Task;
        Assert.True(vm.IsBusy);
        Assert.False(vm.IncidentAwayCommand.CanExecute(null));
        await vm.IncidentAwayCommand.ExecuteAsync(null);
        blocked.Release.SetResult();
        await first;
        Assert.Single((await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!.Incidents);
        Assert.False(vm.IsBusy);
    }

    private static MesaViewModel CreateViewModel(TempStorage storage, TestDesk desk) => WithImmediateContext(() =>
        new MesaViewModel(desk.Operations, desk.Queries, desk.Hub, new FixedTiming(), null, desk.Operator,
            new ScoreboardHost(), new Prompts(), new ScoreboardSettings(),
            new AppInfo("Prueba", AppProfile.Demo, storage.Root, storage.Paths.DatabasePath, 2, "", desk.Operator.Name)));

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

    private sealed class FixedTiming : ITimingFeed
    {
        public TimingView Current => TimingView.Initial(ClockSource.Simulated, "DEMO / SIMULACIÓN");
        public event EventHandler<TimingView>? Changed { add { } remove { } }
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

    private sealed class PausedRepository(IMatchRepository inner) : IMatchRepository
    {
        public bool Pause { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<Match?> GetAsync(Guid id, CancellationToken ct = default) => inner.GetAsync(id, ct);
        public Task<IReadOnlyList<MatchSummary>> ListAsync(CancellationToken ct = default) => inner.ListAsync(ct);
        public Task<Guid?> FindInProgressMatchIdAsync(CancellationToken ct = default) => inner.FindInProgressMatchIdAsync(ct);
        public Task<bool> IsCommandProcessedAsync(Guid id, CancellationToken ct = default) => inner.IsCommandProcessedAsync(id, ct);
        public async Task SaveAsync(Match match, CommandStamp stamp, CancellationToken ct = default)
        {
            if (Pause)
            {
                Entered.SetResult();
                await Release.Task;
            }

            await inner.SaveAsync(match, stamp, ct);
        }
    }
}
