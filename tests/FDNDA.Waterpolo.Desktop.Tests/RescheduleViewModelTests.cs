using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Desktop.ViewModels;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;

namespace FDNDA.Waterpolo.Desktop.Tests;

/// <summary>R10 — reprogramar, actualizar la nómina y anular desde la pestaña «Partidos»; menores del formulario.</summary>
public class RescheduleViewModelTests
{
    [Fact]
    public async Task Reprogramar_carga_el_partido_en_el_formulario_y_guarda_sin_crear_otro()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (_, catalog, mesa, _) = await CreateAsync(storage);
        var count = mesa.Matches.Count;
        mesa.SelectedMatch = mesa.Matches.Single(m => m.MatchId == DemoDataSeeder.SecondMatchId);

        Assert.True(catalog.BeginRescheduleCommand.CanExecute(null));
        await catalog.BeginRescheduleCommand.ExecuteAsync(null);

        Assert.True(catalog.IsRescheduling);
        Assert.Contains("Reprogramar", catalog.ScheduleFormTitle, StringComparison.Ordinal);
        Assert.Equal("Partido demo 2 (vuelta)", catalog.ScheduleLabel);
        Assert.Equal(DemoDataSeeder.AwayTeamId, catalog.ScheduleHome!.TeamId); // la vuelta: local y visita invertidos
        Assert.Equal(DemoDataSeeder.TournamentId, catalog.ScheduleTournament!.TournamentId);
        Assert.Equal("#C62828", catalog.ScheduleHomeColor);

        catalog.ScheduleLabel = "Vuelta reprogramada";
        catalog.ScheduleDateText = "20/03/2027 18:00";
        catalog.ScheduleHomeColor = "#5D1A2B";
        catalog.ScheduleAwayCap = "Negro";
        await catalog.ScheduleMatchCommand.ExecuteAsync(null);

        Assert.False(catalog.IsStatusError, catalog.StatusMessage);
        Assert.False(catalog.IsRescheduling);
        Assert.Equal(count, mesa.Matches.Count);
        var listed = Assert.Single(mesa.Matches, m => m.MatchId == DemoDataSeeder.SecondMatchId);
        Assert.Equal("Vuelta reprogramada", listed.Label);
        Assert.Equal("20/03/2027 18:00", listed.Scheduled);
        var stored = (await new SqliteMatchRepository(storage.OpenDatabase()).GetAsync(DemoDataSeeder.SecondMatchId))!;
        Assert.Equal(("#5D1A2B", "Negro"), (stored.Home.ColorHex, stored.Away.CapColor));
        Assert.Empty(catalog.ScheduleLabel); // el formulario vuelve a «Programar partido»
    }

    [Fact]
    public async Task Cancelar_la_reprogramacion_no_guarda_y_devuelve_el_formulario_a_programar()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (_, catalog, mesa, _) = await CreateAsync(storage);
        mesa.SelectedMatch = mesa.Matches.Single(m => m.MatchId == DemoDataSeeder.SecondMatchId);
        await catalog.BeginRescheduleCommand.ExecuteAsync(null);
        catalog.ScheduleLabel = "No se guarda";

        catalog.CancelRescheduleCommand.Execute(null);

        Assert.False(catalog.IsRescheduling);
        Assert.Empty(catalog.ScheduleLabel);
        Assert.Equal("Programar partido", catalog.ScheduleFormTitle);
        Assert.Equal("Partido demo 2 (vuelta)", (await new SqliteMatchRepository(storage.OpenDatabase()).GetAsync(DemoDataSeeder.SecondMatchId))!.Label);
    }

    [Fact]
    public async Task Reprogramar_el_partido_abierto_actualiza_la_mesa()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (_, catalog, mesa, _) = await CreateAsync(storage);
        mesa.SelectedMatch = mesa.Matches.Single(m => m.MatchId == TestDesk.MatchId);
        await catalog.BeginRescheduleCommand.ExecuteAsync(null);
        catalog.ScheduleLabel = "Ida reprogramada";

        await catalog.ScheduleMatchCommand.ExecuteAsync(null);

        Assert.EndsWith("Ida reprogramada", mesa.MatchTitle, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anular_pide_motivo_marca_el_partido_como_anulado_y_ya_no_se_puede_reprogramar()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (_, catalog, mesa, dialogs) = await CreateAsync(storage);
        mesa.SelectedMatch = mesa.Matches.Single(m => m.MatchId == DemoDataSeeder.SecondMatchId);

        dialogs.Reason = null; // el operador cierra el diálogo
        await catalog.CancelMatchCommand.ExecuteAsync(null);
        Assert.Equal("Programado", mesa.SelectedMatch!.Status);

        dialogs.Reason = "Programado por error";
        await catalog.CancelMatchCommand.ExecuteAsync(null);

        Assert.False(catalog.IsStatusError, catalog.StatusMessage);
        Assert.Contains("anulado", catalog.StatusMessage, StringComparison.OrdinalIgnoreCase);
        var listed = Assert.Single(mesa.Matches, m => m.MatchId == DemoDataSeeder.SecondMatchId);
        Assert.Equal("Anulado", listed.Status);
        Assert.Equal(listed, mesa.SelectedMatch);
        Assert.False(catalog.BeginRescheduleCommand.CanExecute(null));
        Assert.False(catalog.CancelMatchCommand.CanExecute(null));
        Assert.False(catalog.RefreshMatchRosterCommand.CanExecute(null));
    }

    [Fact]
    public async Task Actualizar_la_nomina_del_partido_copia_la_del_evento_tras_confirmar()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (_, catalog, mesa, dialogs) = await CreateAsync(storage);
        catalog.SelectedTournament = catalog.Tournaments.Single(t => t.TournamentId == DemoDataSeeder.TournamentId);
        catalog.SelectedTeam = catalog.Teams.Single(t => t.TeamId == DemoDataSeeder.HomeTeamId);
        await Task.Yield();
        catalog.NewRosterPlayerCommand.Execute(null);
        catalog.RosterPlayerName = "Jugadora Ficticia Nueva";
        catalog.RosterCapText = "14";
        await catalog.SaveRosterPlayerCommand.ExecuteAsync(null);
        Assert.False(catalog.IsStatusError, catalog.StatusMessage);
        mesa.SelectedMatch = mesa.Matches.Single(m => m.MatchId == DemoDataSeeder.SecondMatchId);

        dialogs.ConfirmAnswer = false;
        await catalog.RefreshMatchRosterCommand.ExecuteAsync(null);
        Assert.Equal(13, (await new SqliteMatchRepository(storage.OpenDatabase()).GetAsync(DemoDataSeeder.SecondMatchId))!
            .Roster.Count(r => r.Side == TeamSide.Away));

        dialogs.ConfirmAnswer = true;
        await catalog.RefreshMatchRosterCommand.ExecuteAsync(null);

        Assert.False(catalog.IsStatusError, catalog.StatusMessage);
        Assert.Contains("13 local · 14 visita", catalog.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("nómina vigente", dialogs.LastConfirm, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_partido_iniciado_no_ofrece_reprogramar_anular_ni_actualizar_nomina()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (desk, catalog, mesa, _) = await CreateAsync(storage);
        await desk.StartAsync();
        await mesa.RefreshMatchesCommand.ExecuteAsync(null);

        mesa.SelectedMatch = mesa.Matches.Single(m => m.MatchId == TestDesk.MatchId);

        Assert.False(catalog.BeginRescheduleCommand.CanExecute(null));
        Assert.False(catalog.CancelMatchCommand.CanExecute(null));
        Assert.False(catalog.RefreshMatchRosterCommand.CanExecute(null));
    }

    [Fact]
    public async Task Actualizar_conserva_los_colores_y_gorros_escritos_en_el_formulario()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (_, catalog, _, _) = await CreateAsync(storage);
        catalog.ScheduleHome = catalog.Teams.Single(t => t.TeamId == DemoDataSeeder.HomeTeamId);
        catalog.ScheduleAway = catalog.Teams.Single(t => t.TeamId == DemoDataSeeder.AwayTeamId);
        catalog.ScheduleHomeColor = "#123456";
        catalog.ScheduleHomeCap = "Blanco con números";
        catalog.ScheduleAwayColor = "#654321";
        catalog.ScheduleAwayCap = "Negro";

        await catalog.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(DemoDataSeeder.HomeTeamId, catalog.ScheduleHome!.TeamId);
        Assert.Equal(("#123456", "Blanco con números"), (catalog.ScheduleHomeColor, catalog.ScheduleHomeCap));
        Assert.Equal(("#654321", "Negro"), (catalog.ScheduleAwayColor, catalog.ScheduleAwayCap));

        // Cambiar de equipo sí propone los colores del catálogo.
        catalog.ScheduleHome = catalog.Teams.Single(t => t.TeamId == DemoDataSeeder.AwayTeamId);
        Assert.Equal("#C62828", catalog.ScheduleHomeColor);
    }

    // ---------- Apoyo ----------

    private static async Task<(TestDesk Desk, CatalogViewModel Catalog, MesaViewModel Mesa, Dialogs Dialogs)> CreateAsync(TempStorage storage)
    {
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var dialogs = new Dialogs();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new ImmediateContext());
        try
        {
            var catalog = new CatalogViewModel(
                new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true), desk.Operations, dialogs)
            {
                Operator = desk.Operator,
                Queries = desk.Queries,
            };
            var mesa = new MesaViewModel(desk.Operations, desk.Queries, desk.Hub, new FixedTiming(), null, desk.Operator,
                new NoScoreboard(), new Prompts(), new ScoreboardSettings(),
                new AppInfo("Prueba", AppProfile.Demo, storage.Root, storage.Paths.DatabasePath, 2, "", desk.Operator.Name))
            {
                Catalog = catalog,
            };

            await mesa.InitializeAsync();
            await catalog.InitializeAsync();
            return (desk, catalog, mesa, dialogs);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class Dialogs : ICatalogDialogs
    {
        public string? Reason { get; set; } = "Motivo ficticio";
        public bool ConfirmAnswer { get; set; } = true;
        public string LastConfirm { get; private set; } = string.Empty;
        public string? PickRosterFile() => null;
        public Task<bool> ShowRosterImportAsync(RosterImportViewModel preview) => Task.FromResult(false);
        public Task<bool> ShowRulesEditorAsync(RulesEditorViewModel editor) => Task.FromResult(false);
        public string? PickColor(string? currentHex) => null;
        public string? AskReason(string title, string message) => Reason;

        public bool Confirm(string title, string message)
        {
            LastConfirm = message;
            return ConfirmAnswer;
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

    private sealed class NoScoreboard : IScoreboardHost
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
