using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Application.Reports;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Desktop.ViewModels;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Infrastructure.Export;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;

namespace FDNDA.Waterpolo.Desktop.Tests;

/// <summary>R6 — sección «Resultados» de la pestaña «Resultados y respaldos» (RF09).</summary>
public class ResultsViewModelTests
{
    [Fact]
    public async Task La_pestaña_muestra_resultados_goleadores_y_totales_del_partido_elegido()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (desk, results, _) = await CreateAsync(storage);
        var started = await desk.StartAsync();
        await desk.GoalAsync(TeamSide.Home, started.Home.Roster[0].RosterEntryId);
        await desk.GoalAsync(TeamSide.Home, started.Home.Roster[0].RosterEntryId);
        await desk.Operations.RegisterIncidentAsync(new RegisterIncident(
            Guid.NewGuid(), TestDesk.MatchId, Domain.Matches.IncidentType.Timeout, TeamSide.Away, null, desk.Operator));

        await results.RefreshAsync();
        results.SelectedMatch = results.Matches.Single(m => m.MatchId == TestDesk.MatchId);
        await results.RefreshAsync();

        Assert.Equal("Torneo Demostración FDNDA", results.SelectedTournament!.Name);
        Assert.Equal(["2 – 0", "vs"], results.Matches.Select(m => m.ScoreText));
        Assert.Equal(["En juego", "Programado"], results.Matches.Select(m => m.StatusText));
        var scorer = Assert.Single(results.Scorers);
        Assert.Equal((started.Home.Roster[0].PlayerName, 2), (scorer.PlayerName, scorer.Goals));
        Assert.StartsWith("Partido demo 1: Delfines del Pacífico 2 – 0", results.MatchTitle, StringComparison.Ordinal);
        Assert.Equal([2, 0], results.MatchTeams.Select(t => t.Goals));
        Assert.Equal(1, results.MatchTeams[1].Timeouts);
        Assert.Contains("2 partidos (1 iniciados o finalizados)", results.TournamentSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exportar_el_evento_y_el_partido_pide_la_ruta_y_escribe_PDF_y_CSV()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (_, results, dialogs) = await CreateAsync(storage);
        await results.RefreshAsync();
        results.SelectedMatch = results.Matches[0];
        await results.RefreshAsync();
        var folder = Path.Combine(storage.Root, "exportes");

        dialogs.Folder = folder;
        await results.ExportTournamentPdfCommand.ExecuteAsync(null);
        await results.ExportTournamentCsvCommand.ExecuteAsync(null);
        await results.ExportMatchPdfCommand.ExecuteAsync(null);
        await results.ExportMatchCsvCommand.ExecuteAsync(null);

        Assert.False(results.IsStatusError, results.StatusMessage);
        Assert.StartsWith("Resumen exportado: resumen-partido-partido-demo-1-", results.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(
            [(".pdf", ReportFormat.Pdf), (".csv", ReportFormat.Csv), (".pdf", ReportFormat.Pdf), (".csv", ReportFormat.Csv)],
            dialogs.Requests.Select(r => (Path.GetExtension(r.Name), r.Format)));
        Assert.StartsWith("resumen-evento-torneo-demostracion-fdnda-", dialogs.Requests[0].Name, StringComparison.Ordinal);
        Assert.Equal(4, Directory.GetFiles(folder).Length);
        Assert.All(Directory.GetFiles(folder, "*.pdf"), f => Assert.StartsWith("%PDF-1.4", File.ReadAllText(f)[..8], StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancelar_la_ruta_no_exporta_nada()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (_, results, dialogs) = await CreateAsync(storage);
        await results.RefreshAsync();

        await results.ExportTournamentPdfCommand.ExecuteAsync(null);

        Assert.Equal("Exportación cancelada.", results.StatusMessage);
        Assert.Single(dialogs.Requests);
        Assert.False(results.ExportMatchPdfCommand.CanExecute(null)); // sin partido elegido
    }

    [Fact]
    public async Task Abrir_la_pestaña_desde_la_mesa_actualiza_resultados_y_respaldos()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (desk, results, _) = await CreateAsync(storage);
        var mesa = new MesaViewModel(desk.Operations, desk.Queries, desk.Hub, new FixedTiming(), null, desk.Operator,
            new ScoreboardHost(), new Prompts(), new ScoreboardSettings(),
            new AppInfo("Prueba", AppProfile.Demo, storage.Root, storage.Paths.DatabasePath, 2, "", desk.Operator.Name))
        {
            Results = results,
        };
        await mesa.InitializeAsync();
        await results.RefreshAsync();
        Assert.Equal("vs", results.Matches[0].ScoreText);

        await mesa.StartMatchCommand.ExecuteAsync(null);
        mesa.SelectedHomePlayer = mesa.HomeRoster[0];
        await mesa.GoalHomeCommand.ExecuteAsync(null);
        mesa.SelectedTabIndex = MesaViewModel.ResultsTabIndex;
        await mesa.ResultsTabRefresh;

        Assert.Equal("1 – 0", results.Matches[0].ScoreText);
        Assert.Equal("En juego", results.Matches[0].StatusText);
    }

    // ---------- Apoyo ----------

    private static async Task<(TestDesk Desk, ResultsViewModel Results, ReportDialogs Dialogs)> CreateAsync(TempStorage storage)
    {
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var dialogs = new ReportDialogs();
        var reports = new ReportOperations(desk.Repository, desk.Catalog, new ReportFileWriter(storage.Time), storage.Time, storage.Log);
        var catalog = new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true);
        var results = new ResultsViewModel(reports, catalog, dialogs);
        return (desk, results, dialogs);
    }

    private sealed class ReportDialogs : IReportDialogs
    {
        public string? Folder { get; set; }

        public List<(string Name, ReportFormat Format)> Requests { get; } = [];

        public string? PickSavePath(string suggestedFileName, ReportFormat format)
        {
            Requests.Add((suggestedFileName, format));
            return Folder is null ? null : Path.Combine(Folder, suggestedFileName);
        }
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

        public void Open(MonitorOption? monitor, bool fullScreen)
        {
        }

        public void Close()
        {
        }

        public event EventHandler<string>? Notice { add { } remove { } }

        public event EventHandler? StateChanged { add { } remove { } }
    }

    private sealed class Prompts : IPromptService
    {
        public string? AskReason(string title, string message) => "Motivo ficticio";

        public bool Confirm(string title, string message) => true;

        public void OpenFolder(string path)
        {
        }
    }
}
