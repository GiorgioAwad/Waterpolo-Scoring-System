using System.Globalization;
using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Desktop.ViewModels;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;

namespace FDNDA.Waterpolo.Desktop.Tests;

/// <summary>R2 — pestaña de eventos y equipos y formulario de programación, sobre SQLite temporal.</summary>
public class CatalogViewModelTests
{
    [Fact]
    public async Task Desde_la_interfaz_se_crean_evento_y_dos_equipos_y_se_programa_el_partido()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (desk, catalog, mesa) = await CreateAsync(storage);

        catalog.NewTournamentCommand.Execute(null);
        catalog.TournamentName = "Copa de Prueba";
        await catalog.SaveTournamentCommand.ExecuteAsync(null);
        Assert.False(catalog.IsStatusError, catalog.StatusMessage);
        Assert.Equal("Copa de Prueba", catalog.SelectedTournament!.Name);

        await CreateTeamAsync(catalog, "AMB", "Equipo Ámbar", "#FFB300", "Amarillo");
        await CreateTeamAsync(catalog, "BRU", "Equipo Bruma", "#546E7A", "Gris");
        Assert.Equal(4, catalog.Teams.Count);

        catalog.ScheduleTournament = catalog.Tournaments.Single(t => t.Name == "Copa de Prueba");
        catalog.ScheduleHome = catalog.Teams.Single(t => t.Code == "AMB");
        catalog.ScheduleAway = catalog.Teams.Single(t => t.Code == "BRU");
        Assert.Equal("#FFB300", catalog.ScheduleHomeColor); // propuesto desde el catálogo
        Assert.Equal("Gris", catalog.ScheduleAwayCap);
        Assert.Contains("0 local · 0 visita", catalog.ScheduleRosterInfo, StringComparison.Ordinal);
        Assert.False(catalog.ScheduleMatchCommand.CanExecute(null)); // falta la descripción
        catalog.ScheduleLabel = "Fecha 1";
        catalog.ScheduleDateText = "14/03/2027 15:30";
        catalog.ScheduleAwayCap = "Negro";
        await catalog.ScheduleMatchCommand.ExecuteAsync(null);

        Assert.False(catalog.IsStatusError, catalog.StatusMessage);
        var listed = Assert.Single(mesa.Matches, m => m.Label == "Fecha 1");
        Assert.Equal("Copa de Prueba", listed.Tournament);
        Assert.Equal("Equipo Ámbar", listed.Home);
        Assert.Equal(listed, mesa.SelectedMatch); // queda seleccionado, sin abrirse en la mesa
        Assert.Equal(TestDesk.MatchId, desk.Hub.Current!.MatchId);

        var stored = (await new SqliteMatchRepository(storage.OpenDatabase()).GetAsync(listed.MatchId))!;
        var expectedUtc = new DateTimeOffset(new DateTime(2027, 3, 14, 15, 30, 0, DateTimeKind.Local)).ToUniversalTime();
        Assert.Equal(expectedUtc, stored.ScheduledAtUtc);
        Assert.Equal("Negro", stored.Away.CapColor);
        Assert.Equal(1, Assert.Single(catalog.Tournaments, t => t.Name == "Copa de Prueba").MatchCount);
        Assert.Empty(catalog.ScheduleLabel); // el formulario queda listo para el siguiente
    }

    [Fact]
    public async Task Mientras_se_guarda_un_segundo_envio_del_formulario_no_programa_otro_partido()
    {
        await using var storage = await TempStorage.CreateAsync();
        var paused = new PausingRepository(new SqliteMatchRepository(storage.OpenDatabase()));
        var (desk, catalog, _) = await CreateAsync(storage, paused);
        catalog.ScheduleHome = catalog.Teams.Single(t => t.Code == "DEL");
        catalog.ScheduleAway = catalog.Teams.Single(t => t.Code == "TIB");
        catalog.ScheduleLabel = "Fecha 2";

        paused.Pause = true;
        var first = catalog.ScheduleMatchCommand.ExecuteAsync(null);
        await paused.Entered.Task;
        Assert.True(catalog.IsBusy);
        Assert.False(catalog.ScheduleMatchCommand.CanExecute(null));
        await catalog.ScheduleMatchCommand.ExecuteAsync(null); // doble clic mientras se guarda
        paused.Release.SetResult();
        await first;

        Assert.Equal(1, paused.Saves);
        Assert.Equal(3, (await desk.Queries.ListAsync()).Count);
        Assert.False(catalog.IsBusy);
    }

    [Fact]
    public async Task Si_falla_el_guardado_el_reintento_usa_el_mismo_partido_y_no_lo_duplica()
    {
        await using var storage = await TempStorage.CreateAsync();
        var failing = new FailingSaveRepository(new SqliteMatchRepository(storage.OpenDatabase())) { FailSaves = false };
        var (desk, catalog, _) = await CreateAsync(storage, failing);
        catalog.ScheduleHome = catalog.Teams.Single(t => t.Code == "DEL");
        catalog.ScheduleAway = catalog.Teams.Single(t => t.Code == "TIB");
        catalog.ScheduleLabel = "Fecha 4";

        failing.FailSaves = true;
        await catalog.ScheduleMatchCommand.ExecuteAsync(null);
        Assert.True(catalog.IsStatusError);
        Assert.Equal("Fecha 4", catalog.ScheduleLabel);

        failing.FailSaves = false;
        await catalog.ScheduleMatchCommand.ExecuteAsync(null);
        await catalog.ScheduleMatchCommand.ExecuteAsync(null); // el formulario ya se vació: no programa otro

        Assert.False(catalog.ScheduleMatchCommand.CanExecute(null));
        Assert.Single(await desk.Queries.ListAsync(), m => m.Label == "Fecha 4");
        Assert.Equal(3, (await desk.Queries.ListAsync()).Count);
    }

    [Fact]
    public async Task Una_fecha_mal_escrita_no_programa_y_se_explica()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (desk, catalog, _) = await CreateAsync(storage);
        catalog.ScheduleHome = catalog.Teams.Single(t => t.Code == "DEL");
        catalog.ScheduleAway = catalog.Teams.Single(t => t.Code == "TIB");
        catalog.ScheduleLabel = "Fecha 3";
        catalog.ScheduleDateText = "2027-03-14";

        await catalog.ScheduleMatchCommand.ExecuteAsync(null);

        Assert.True(catalog.IsStatusError);
        Assert.Contains("14/03/2027 15:30", catalog.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(2, (await desk.Queries.ListAsync()).Count);
        Assert.Equal("Fecha 3", catalog.ScheduleLabel); // no se pierde lo escrito
    }

    [Fact]
    public async Task El_mismo_equipo_en_ambos_lados_muestra_el_rechazo()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (desk, catalog, _) = await CreateAsync(storage);
        catalog.ScheduleHome = catalog.Teams.Single(t => t.Code == "DEL");
        catalog.ScheduleAway = catalog.Teams.Single(t => t.Code == "DEL");
        catalog.ScheduleLabel = "Inválido";

        await catalog.ScheduleMatchCommand.ExecuteAsync(null);

        Assert.True(catalog.IsStatusError);
        Assert.Contains("distintos", catalog.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(2, (await desk.Queries.ListAsync()).Count);
    }

    [Fact]
    public async Task Editar_un_equipo_desde_la_lista_conserva_su_identidad_y_muestra_la_nomina()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (_, catalog, _) = await CreateAsync(storage);

        catalog.SelectedTournament = Assert.Single(catalog.Tournaments);
        catalog.SelectedTeam = catalog.Teams.Single(t => t.Code == "TIB");
        Assert.Equal(13, catalog.RosterLines.Count);
        Assert.Contains("13 jugadores", catalog.RosterTitle, StringComparison.Ordinal);
        Assert.Equal("Editar equipo", catalog.TeamFormTitle);

        catalog.TeamName = "Tiburones del Norte";
        await catalog.SaveTeamCommand.ExecuteAsync(null);

        Assert.False(catalog.IsStatusError, catalog.StatusMessage);
        Assert.Equal(2, catalog.Teams.Count);
        Assert.Equal("Tiburones del Norte", catalog.SelectedTeam!.Name);
        Assert.Equal(DemoDataSeeder.AwayTeamId, catalog.SelectedTeam.TeamId);
    }

    [Fact]
    public async Task Un_codigo_repetido_muestra_error_y_no_crea_el_equipo()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (_, catalog, _) = await CreateAsync(storage);

        await CreateTeamAsync(catalog, "DEL", "Copia", "#000000", "Negro");

        Assert.True(catalog.IsStatusError);
        Assert.Equal(2, catalog.Teams.Count);
        Assert.Equal("Nuevo equipo", catalog.TeamFormTitle); // el formulario conserva lo escrito
        Assert.Equal("Copia", catalog.TeamName);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("01/12/2027 09:05", true)]
    [InlineData("1/12/2027 9:05", false)]
    [InlineData("31/02/2027 10:00", false)]
    [InlineData("mañana", false)]
    public void El_formato_de_fecha_es_el_local_dd_MM_yyyy_HH_mm(string text, bool valid)
    {
        Assert.Equal(valid, CatalogViewModel.TryParseSchedule(text, out var utc));
        if (valid && text.Length > 0)
        {
            Assert.Equal(TimeSpan.Zero, utc!.Value.Offset);
            Assert.Equal(
                DateTime.ParseExact(text, "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                utc.Value.ToLocalTime().DateTime);
        }
    }

    private static async Task CreateTeamAsync(CatalogViewModel catalog, string code, string name, string color, string cap)
    {
        catalog.NewTeamCommand.Execute(null);
        catalog.TeamCode = code;
        catalog.TeamName = name;
        catalog.TeamColor = color;
        catalog.TeamCapColor = cap;
        await catalog.SaveTeamCommand.ExecuteAsync(null);
    }

    private static async Task<(TestDesk Desk, CatalogViewModel Catalog, MesaViewModel Mesa)> CreateAsync(
        TempStorage storage, IMatchRepository? repository = null)
    {
        var desk = await new TestDesk(storage, repository).SignInAsync(storage);
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new ImmediateContext());
        try
        {
            var catalog = new CatalogViewModel(
                new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true), desk.Operations);
            var mesa = new MesaViewModel(desk.Operations, desk.Queries, desk.Hub, new FixedTiming(), null, desk.Operator,
                new NoScoreboard(), new AcceptingPrompts(), new ScoreboardSettings(),
                new AppInfo("Prueba", AppProfile.Demo, storage.Root, storage.Paths.DatabasePath, 2, "", desk.Operator.Name))
            {
                Catalog = catalog,
            };

            await mesa.InitializeAsync();
            await catalog.InitializeAsync();
            return (desk, catalog, mesa);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>Retiene el guardado hasta que la prueba lo libera, para simular un disco lento.</summary>
    private sealed class PausingRepository(IMatchRepository inner) : IMatchRepository
    {
        public bool Pause { get; set; }
        public int Saves { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<Match?> GetAsync(Guid id, CancellationToken ct = default) => inner.GetAsync(id, ct);
        public Task<IReadOnlyList<MatchSummary>> ListAsync(CancellationToken ct = default) => inner.ListAsync(ct);
        public Task<Guid?> FindInProgressMatchIdAsync(CancellationToken ct = default) => inner.FindInProgressMatchIdAsync(ct);
        public Task<bool> IsCommandProcessedAsync(Guid id, CancellationToken ct = default) => inner.IsCommandProcessedAsync(id, ct);

        public async Task SaveAsync(Match match, CommandStamp stamp, CancellationToken ct = default)
        {
            Saves++;
            if (Pause)
            {
                Entered.TrySetResult();
                await Release.Task;
            }

            await inner.SaveAsync(match, stamp, ct);
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

    private sealed class AcceptingPrompts : IPromptService
    {
        public string? AskReason(string title, string message) => "Motivo ficticio";
        public bool Confirm(string title, string message) => true;
        public void OpenFolder(string path) { }
    }
}
