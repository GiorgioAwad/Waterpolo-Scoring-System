using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Application.Reports;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Desktop.ViewModels;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Infrastructure.Export;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;

namespace FDNDA.Waterpolo.Desktop.Tests;

/// <summary>R8 — prórroga y tanda en la mesa, el marcador y el editor de reglas, sin ventanas.</summary>
public class OvertimeShootoutViewModelTests
{
    /// <summary>Valores de prueba, no reglamentarios.</summary>
    private static readonly RulesetSnapshot OneOvertimeAndShootout =
        RulesetSnapshot.ForEvent(4, 8 * 60, 2 * 60, 3 * 60, 2, 30, 20, penaltyShootoutEnabled: true,
            overtimePeriodCount: 1, overtimePeriodDurationSeconds: 3 * 60);

    [Fact]
    public async Task La_mesa_pasa_por_prorroga_y_tanda_y_el_marcador_muestra_la_tanda_aparte_sin_aviso_animado()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        await SetRulesAsync(storage, desk, OneOvertimeAndShootout);
        using var vm = CreateViewModel(storage, desk);
        using var scoreboard = WithImmediateContext(() => new MarcadorViewModel(desk.Hub, new FixedTiming(), new ScoreboardSettings()));
        await vm.InitializeAsync();
        await vm.StartMatchCommand.ExecuteAsync(null);
        vm.SelectedHomePlayer = vm.HomeRoster[0];
        vm.SelectedAwayPlayer = vm.AwayRoster[0];
        await vm.GoalHomeCommand.ExecuteAsync(null);
        await vm.GoalAwayCommand.ExecuteAsync(null);

        for (var period = 1; period <= 3; period++)
        {
            await vm.StartBreakCommand.ExecuteAsync(null);
            Assert.Equal("_Siguiente periodo", vm.NextPeriodLabel);
            await vm.StartNextPeriodCommand.ExecuteAsync(null);
        }

        await vm.StartBreakCommand.ExecuteAsync(null);
        Assert.Equal("Iniciar _prórroga", vm.NextPeriodLabel);
        Assert.False(vm.StartShootoutCommand.CanExecute(null));
        Assert.Contains("prórroga", vm.ShootoutHint, StringComparison.Ordinal);
        await vm.StartNextPeriodCommand.ExecuteAsync(null);
        Assert.Equal(("PRÓRROGA 1", "PRÓRROGA 1", "Prórroga 1 iniciada."), (vm.StageText, scoreboard.StageText, vm.StatusMessage));
        await vm.GoalHomeCommand.ExecuteAsync(null);
        Assert.Equal("Pr1", vm.Incidents[0].Period);
        await vm.GoalAwayCommand.ExecuteAsync(null);
        await vm.StartBreakCommand.ExecuteAsync(null);
        Assert.Equal("DESCANSO · tras prórroga 1", vm.StageText);
        Assert.Equal("_Siguiente periodo", vm.NextPeriodLabel); // la única prórroga ya se jugó
        Assert.True(vm.StartShootoutCommand.CanExecute(null));

        await vm.StartShootoutCommand.ExecuteAsync(null);

        Assert.Equal(("TANDA DE PENALES", "PENALES"), (vm.StageText, scoreboard.StageText));
        Assert.True(vm.IsShootoutStage);
        Assert.Equal(IncidentOption.DuringShootout, vm.RegistrationOptions);
        Assert.Equal((IncidentType.ShootoutAttempt, PenaltyResult.Converted),
            (vm.SelectedRegistrationOption!.Type, vm.SelectedRegistrationOption.PenaltyResult));
        Assert.False(vm.GoalHomeCommand.CanExecute(null));
        Assert.True(vm.IncidentHomeCommand.CanExecute(null));
        Assert.True(scoreboard.HasShootout);
        Assert.False(scoreboard.ShowClocks); // rediseño 2026-11-25: en la tanda no se muestran los relojes

        await vm.IncidentHomeCommand.ExecuteAsync(null);
        Assert.Null(ActionBanner.For(desk.Publications[^1], new ScoreboardSettings()));
        vm.SelectedRegistrationOption = vm.RegistrationOptions.Single(o => o.Type == IncidentType.ShootoutAttempt && o.PenaltyResult == PenaltyResult.NotConverted);
        await vm.IncidentAwayCommand.ExecuteAsync(null);

        Assert.Equal((2, 2), (vm.HomeScore, vm.AwayScore));
        Assert.Equal(("2", "2"), (scoreboard.HomeScoreText, scoreboard.AwayScoreText));
        Assert.Equal("Penales: DEL 1 – 0 TIB", vm.ShootoutText);
        Assert.Contains("Penales: DEL 1 – 0 TIB", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(("1", "0"), (scoreboard.HomeShootoutText, scoreboard.AwayShootoutText));
        Assert.Equal([true], scoreboard.HomeShootoutMarks.Select(m => m.Converted));
        Assert.Equal([false], scoreboard.AwayShootoutMarks.Select(m => m.Converted));
        Assert.Equal("Tanda", vm.Incidents[0].Period);
        Assert.Equal("Tanda: no convertido", vm.Incidents[0].TypeText);

        await vm.FinishMatchCommand.ExecuteAsync(null);
        Assert.Equal("RESULTADO FINAL", scoreboard.StageText);
        Assert.True(scoreboard.HasShootout);
        Assert.False(vm.IsShootoutStage);
        Assert.Equal(IncidentOption.DuringPlay, vm.RegistrationOptions);
    }

    [Fact]
    public async Task Sin_tanda_en_las_reglas_el_boton_queda_deshabilitado_y_explica_por_que()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        using var vm = CreateViewModel(storage, desk);
        await vm.InitializeAsync();
        await vm.StartMatchCommand.ExecuteAsync(null);
        for (var period = 1; period <= 3; period++)
        {
            await vm.StartBreakCommand.ExecuteAsync(null);
            await vm.StartNextPeriodCommand.ExecuteAsync(null);
        }

        await vm.StartBreakCommand.ExecuteAsync(null);

        Assert.False(vm.StartShootoutCommand.CanExecute(null));
        Assert.Equal("Las reglas de este partido no tienen tanda de penales.", vm.ShootoutHint);
        Assert.Equal("_Siguiente periodo", vm.NextPeriodLabel); // sin prórroga en las reglas, no se la ofrece
        await vm.StartNextPeriodCommand.ExecuteAsync(null);
        Assert.True(vm.IsStatusError);
        Assert.Equal("No quedan periodos reglamentarios y las reglas de este partido no tienen prórroga.", vm.StatusMessage);
        Assert.Contains("sin prórroga · tanda: no", vm.RulesText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_tanda_anulada_desde_la_mesa_vuelve_al_descanso_y_a_las_acciones_de_juego()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        await SetRulesAsync(storage, desk, RulesetSnapshot.ForEvent(4, 480, 120, 180, 2, 30, 20, penaltyShootoutEnabled: true));
        using var vm = CreateViewModel(storage, desk);
        await vm.InitializeAsync();
        await vm.StartMatchCommand.ExecuteAsync(null);
        for (var period = 1; period <= 3; period++)
        {
            await vm.StartBreakCommand.ExecuteAsync(null);
            await vm.StartNextPeriodCommand.ExecuteAsync(null);
        }

        await vm.StartShootoutCommand.ExecuteAsync(null); // 0–0, desde el cuarto periodo
        Assert.True(vm.CancelShootoutCommand.CanExecute(null));

        await vm.CancelShootoutCommand.ExecuteAsync(null);

        Assert.False(vm.IsShootoutStage);
        Assert.False(vm.HasShootout);
        Assert.Equal("DESCANSO · tras periodo 4", vm.StageText);
        Assert.Equal(IncidentOption.DuringPlay, vm.RegistrationOptions);
        Assert.Equal("Motivo ficticio", (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!.History[^1].Reason);
    }

    [Fact]
    public void El_editor_de_reglas_lee_y_valida_la_prorroga()
    {
        var editor = new RulesEditorViewModel(null!, Guid.NewGuid(), "Evento", RulesetSnapshot.DemoPreset(), 0, 0);
        Assert.Equal(("0", "3:00"), (editor.OvertimePeriodsText, editor.OvertimeDurationText));

        editor.OvertimePeriodsText = "2";
        editor.OvertimeDurationText = "5";
        Assert.True(editor.TryBuild(out var rules, out _));
        Assert.Equal((2, 300), (rules.OvertimePeriodCount, rules.OvertimePeriodDurationSeconds));
        Assert.Contains("sin aprobación técnica", editor.OutcomeText, StringComparison.Ordinal);

        editor.OvertimePeriodsText = "dos";
        Assert.Equal("Revise: Escriba los periodos de prórroga como número entero (0 = sin prórroga).", editor.OutcomeText);

        editor.OvertimePeriodsText = "17";
        Assert.False(editor.TryBuild(out _, out var problem));
        Assert.Contains("no deben pasar de 20", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Los_resultados_muestran_la_tanda_en_su_columna()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        await SetRulesAsync(storage, desk, RulesetSnapshot.ForEvent(4, 480, 120, 180, 2, 30, 20, penaltyShootoutEnabled: true));
        using var vm = CreateViewModel(storage, desk);
        await vm.InitializeAsync();
        await vm.StartMatchCommand.ExecuteAsync(null);
        for (var period = 1; period <= 3; period++)
        {
            await vm.StartBreakCommand.ExecuteAsync(null);
            await vm.StartNextPeriodCommand.ExecuteAsync(null);
        }

        await vm.StartShootoutCommand.ExecuteAsync(null);
        vm.SelectedAwayPlayer = vm.AwayRoster[0];
        await vm.IncidentAwayCommand.ExecuteAsync(null);
        await vm.FinishMatchCommand.ExecuteAsync(null);
        var reports = new ReportOperations(desk.Repository, desk.Catalog, new ReportFileWriter(storage.Time), storage.Time, storage.Log);

        var item = MatchResultItem.From((await reports.GetMatchReportAsync(TestDesk.MatchId))!);

        Assert.Equal(("0 – 0", "0 – 1"), (item.ScoreText, item.ShootoutText));
    }

    // ---------- Apoyo ----------

    private static async Task SetRulesAsync(TempStorage storage, TestDesk desk, RulesetSnapshot rules)
    {
        var catalog = new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true);
        Assert.True((await catalog.UpdateTournamentRulesAsync(DemoDataSeeder.TournamentId, rules)).IsSuccess);
    }

    private static MesaViewModel CreateViewModel(TempStorage storage, TestDesk desk) => WithImmediateContext(() =>
        new MesaViewModel(desk.Operations, desk.Queries, desk.Hub, new FixedTiming(), null, desk.Operator,
            new ScoreboardHost(), new Prompts(), new ScoreboardSettings(),
            new AppInfo("Prueba", AppProfile.Demo, storage.Root, storage.Paths.DatabasePath, 4, "", desk.Operator.Name)));

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
}
