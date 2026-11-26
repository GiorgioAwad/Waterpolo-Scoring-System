using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Application.Identity;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Desktop.ViewModels;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Infrastructure.Identity;
using FDNDA.Waterpolo.Infrastructure.Import;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;

namespace FDNDA.Waterpolo.Desktop.Tests;

/// <summary>
/// R7 — avisos de exclusión y tiempo muerto, identidad en marcador y mesa, imágenes del equipo y
/// editor de identidad del evento. ViewModels reales sobre SQLite temporal, sin ventanas.
/// </summary>
public class IdentityViewModelTests
{
    // ---------- Avisos animados (RF08) ----------

    [Fact]
    public async Task La_exclusion_y_el_tiempo_muerto_se_anuncian_una_vez_y_corregir_o_anular_no_repite()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, new FixedTiming(), new ScoreboardSettings()));
        var player = started.Home.Roster[4];

        var exclusion = await RegisterAsync(desk, IncidentType.Exclusion, TeamSide.Home, player.RosterEntryId, ExclusionKind.Exclusion);
        Assert.True(scoreboard.IsBannerVisible);
        Assert.Equal(ActionBannerKind.Exclusion, scoreboard.BannerKind);
        Assert.Equal($"EXCLUSIÓN  ·  Delfines del Pacífico  ·  5  {player.PlayerName}  ·  1.ª FALTA", scoreboard.BannerText);

        var timeout = await RegisterAsync(desk, IncidentType.Timeout, TeamSide.Away, null);
        Assert.Equal(ActionBannerKind.Timeout, scoreboard.BannerKind);
        Assert.Equal("TIEMPO MUERTO  ·  Club Deportivo Tiburones del Norte", scoreboard.BannerText);

        // Correcciones y anulaciones posteriores: el aviso no cambia ni se repite.
        var published = desk.Publications.Count;
        await desk.Operations.CorrectIncidentAsync(new CorrectIncident(
            Guid.NewGuid(), TestDesk.MatchId, exclusion, TeamSide.Home, player.RosterEntryId, "Fue expulsión", desk.Operator,
            ExclusionKind: ExclusionKind.Expulsion));
        await desk.Operations.VoidIncidentAsync(new VoidIncident(Guid.NewGuid(), TestDesk.MatchId, timeout, "No hubo tiempo muerto", desk.Operator));
        Assert.Equal(published + 2, desk.Publications.Count);
        Assert.Equal("TIEMPO MUERTO  ·  Club Deportivo Tiburones del Norte", scoreboard.BannerText);
        Assert.All(desk.Publications.Skip(published), p => Assert.Null(ActionBanner.For(p, new ScoreboardSettings())));

        // Reabrir la pantalla toma el estado actual sin reproducir avisos pasados.
        using var reopened = Immediate(() => new MarcadorViewModel(desk.Hub, new FixedTiming(), new ScoreboardSettings()));
        Assert.False(reopened.IsBannerVisible);
        Assert.Empty(reopened.BannerText);
    }

    [Fact]
    public async Task Cada_tipo_de_aviso_se_desactiva_por_separado_desde_la_mesa()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        var settings = new ScoreboardSettings { ExclusionAnimationsEnabled = false };
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, new FixedTiming(), settings));

        await RegisterAsync(desk, IncidentType.Exclusion, TeamSide.Away, started.Away.Roster[0].RosterEntryId, ExclusionKind.Exclusion);
        Assert.False(scoreboard.IsBannerVisible);

        await desk.GoalAsync(TeamSide.Home, started.Home.Roster[0].RosterEntryId);
        Assert.Equal(ActionBannerKind.Goal, scoreboard.BannerKind);

        settings.TimeoutAnimationsEnabled = false;
        var lastText = scoreboard.BannerText;
        await RegisterAsync(desk, IncidentType.Timeout, TeamSide.Home, null);
        Assert.Equal(lastText, scoreboard.BannerText);

        settings.AnimationsEnabled = false; // el interruptor general apaga también el gol
        await desk.GoalAsync(TeamSide.Away, started.Away.Roster[1].RosterEntryId);
        Assert.Equal(lastText, scoreboard.BannerText);
        Assert.Null(ActionBanner.For(desk.Publications[^1], settings));
        Assert.NotNull(ActionBanner.For(desk.Publications[^1], new ScoreboardSettings()));
    }

    [Fact]
    public async Task El_penal_convertido_se_anuncia_como_gol_y_el_penal_fallado_no_se_anuncia()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, new FixedTiming(), new ScoreboardSettings()));
        var shooter = started.Away.Roster[2];

        // La falta personal se anuncia desde el 2026-11-25 (AvisosFaltasTests); el penal fallado, no.
        await RegisterAsync(desk, IncidentType.Penalty, TeamSide.Away, shooter.RosterEntryId, penalty: PenaltyResult.NotConverted);
        Assert.False(scoreboard.IsBannerVisible);

        await RegisterAsync(desk, IncidentType.Penalty, TeamSide.Away, shooter.RosterEntryId, penalty: PenaltyResult.Converted);
        Assert.Equal(ActionBannerKind.Goal, scoreboard.BannerKind);
        Assert.StartsWith("¡GOL DE PENAL!  Club Deportivo Tiburones del Norte  ·  3  ", scoreboard.BannerText, StringComparison.Ordinal);
    }

    // ---------- Identidad en el marcador y la mesa (RF07) ----------

    [Fact]
    public async Task El_marcador_y_la_mesa_muestran_la_identidad_del_partido_abierto_y_se_actualizan_al_cambiarla()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (desk, identity, hub) = await CreateIdentityAsync(storage);
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, new FixedTiming(), new ScoreboardSettings(), hub));
        using var mesa = Immediate(() => new MesaViewModel(desk.Operations, desk.Queries, desk.Hub, new FixedTiming(), null, desk.Operator,
            new NoScoreboard(), new Prompts(), new ScoreboardSettings(), Info(storage, desk)) { Identity = hub });

        await mesa.InitializeAsync();
        await identity.WhenIdleAsync();

        // Sin plantilla: nombre del evento, colores predeterminados y escudos de la demo.
        Assert.Equal("Torneo Demostración FDNDA", scoreboard.TournamentName);
        Assert.Equal(MarcadorViewModel.DefaultHeaderColor, scoreboard.HeaderColor);
        Assert.False(scoreboard.HasSponsors);
        Assert.NotNull(scoreboard.HomeCrest);
        Assert.NotNull(mesa.HomeCrest);
        Assert.Null(mesa.AwayFlag);

        var example = (await identity.LoadExampleAsync()).Draft!;
        Assert.True((await identity.SaveEventBrandingAsync(DemoDataSeeder.TournamentId, example)).IsSuccess);
        var flag = new ImageAsset(Guid.NewGuid(), AssetKind.Flag, "bandera.png", TestImages.Png(360, 240));
        Assert.True((await identity.SetTeamImageAsync(DemoDataSeeder.AwayTeamId, AssetKind.Flag, flag)).IsSuccess);

        Assert.Equal("Panamericano de Waterpolo Sub-17 2027", scoreboard.TournamentName);
        Assert.True(scoreboard.HasSubtitle);
        Assert.Equal(("#0B2A4A", "#FFC83D"), (scoreboard.HeaderColor, scoreboard.AccentColor));
        Assert.NotNull(scoreboard.EventLogo);
        Assert.NotNull(scoreboard.OrganizerLogo);
        Assert.Equal(3, scoreboard.Sponsors.Count);
        Assert.Equal(flag.Id, scoreboard.AwayFlag!.AssetId);
        Assert.Equal(flag.Id, mesa.AwayFlag!.AssetId);
    }

    [Fact]
    public async Task La_identidad_de_otro_partido_no_se_aplica_al_partido_mostrado()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        await desk.Operations.OpenMatchAsync(TestDesk.MatchId);
        var hub = new ScoreboardIdentityHub();
        using var scoreboard = Immediate(() => new MarcadorViewModel(desk.Hub, new FixedTiming(), new ScoreboardSettings(), hub));

        var crest = new ImageData(Guid.NewGuid(), TestImages.Png(100, 100));
        hub.Publish(new ScoreboardIdentity(
            DemoDataSeeder.SecondMatchId, DemoDataSeeder.TournamentId, "Otro título", null, "#000000", null, null, null, [],
            new TeamIdentity(DemoDataSeeder.AwayTeamId, crest, null), TeamIdentity.Empty(DemoDataSeeder.HomeTeamId)));

        Assert.Equal("Torneo Demostración FDNDA", scoreboard.TournamentName);
        Assert.Equal(MarcadorViewModel.DefaultHeaderColor, scoreboard.HeaderColor);
        Assert.Null(scoreboard.HomeCrest);
    }

    // ---------- Escudo y bandera desde «Eventos y nóminas» (RF01) ----------

    [Fact]
    public async Task El_escudo_del_equipo_se_carga_desde_un_archivo_y_un_archivo_invalido_no_cambia_nada()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (desk, identity, _) = await CreateIdentityAsync(storage);
        var dialogs = new Dialogs();
        var catalog = new CatalogViewModel(
            new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true), desk.Operations, dialogs, identity);
        await catalog.InitializeAsync();
        Assert.False(catalog.LoadTeamCrestCommand.CanExecute(null)); // sin equipo elegido

        catalog.SelectedTeam = catalog.Teams.Single(t => t.Code == "TIB");
        await WaitUntilAsync(() => catalog.TeamCrest is not null);
        Assert.Equal("escudo-tiburones-ejemplo.png · 320×380 px · 4 KB", catalog.TeamCrestText);
        Assert.Equal("Sin bandera", catalog.TeamFlagText);
        Assert.False(catalog.RemoveTeamFlagCommand.CanExecute(null));

        dialogs.ImagePath = await WriteFileAsync(storage, "bandera-tib.png", TestImages.Png(360, 240));
        await catalog.LoadTeamFlagCommand.ExecuteAsync(null);
        Assert.False(catalog.IsStatusError, catalog.StatusMessage);
        Assert.StartsWith("Bandera de TIB guardada: bandera-tib.png", catalog.StatusMessage, StringComparison.Ordinal);
        Assert.StartsWith("bandera-tib.png · 360×240 px", catalog.TeamFlagText, StringComparison.Ordinal);
        Assert.Equal("Bandera de TIB", dialogs.LastImageTitle);

        dialogs.ImagePath = await WriteFileAsync(storage, "escudo.gif", "GIF89a......"u8.ToArray());
        await catalog.LoadTeamCrestCommand.ExecuteAsync(null);
        Assert.True(catalog.IsStatusError);
        Assert.Contains("GIF no se admiten", catalog.StatusMessage, StringComparison.Ordinal);
        Assert.Equal("escudo-tiburones-ejemplo.png", (await identity.GetTeamImagesAsync(DemoDataSeeder.AwayTeamId)).Crest!.FileName);

        await catalog.RemoveTeamCrestCommand.ExecuteAsync(null);
        Assert.Equal("Sin escudo", catalog.TeamCrestText);
        Assert.Null((await identity.GetTeamImagesAsync(DemoDataSeeder.AwayTeamId)).Crest);
    }

    // ---------- Editor de identidad del evento (RF08) ----------

    [Fact]
    public async Task El_editor_carga_la_plantilla_de_ejemplo_agrega_y_ordena_auspiciadores_y_guarda_al_confirmar()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (desk, identity, _) = await CreateIdentityAsync(storage);
        var dialogs = new Dialogs();
        var catalog = new CatalogViewModel(
            new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true), desk.Operations, dialogs, identity);
        await catalog.InitializeAsync();
        catalog.SelectedTournament = catalog.Tournaments.Single();
        EventIdentityViewModel? editor = null;
        dialogs.OnIdentity = async e =>
        {
            editor = e;
            Assert.Equal("Torneo Demostración FDNDA", e.DisplayTitle); // sin identidad: propone el nombre del evento
            Assert.False(e.RemoveIdentityCommand.CanExecute(null));

            await e.UseExampleCommand.ExecuteAsync(null);
            Assert.Equal("Panamericano de Waterpolo Sub-17 2027", e.DisplayTitle);
            Assert.Equal(3, e.Sponsors.Count);
            Assert.NotNull(e.LogoPreview);

            dialogs.ImagePath = await WriteFileAsync(storage, "marca-local.png", TestImages.Png(400, 160));
            await e.AddSponsorCommand.ExecuteAsync(null);
            Assert.Equal("marca-local", e.SelectedSponsor!.Name);
            e.SelectedSponsor.Name = "Marca Local";
            e.MoveSponsorUpCommand.Execute(null);
            e.RemoveOrganizerLogoCommand.Execute(null);
            e.Subtitle = "Fase de grupos";
            await e.SaveCommand.ExecuteAsync(null);
            Assert.False(e.IsStatusError, e.StatusMessage);
            return true;
        };

        await catalog.EditIdentityCommand.ExecuteAsync(null);

        Assert.NotNull(editor);
        Assert.StartsWith("Identidad de «Torneo Demostración FDNDA» guardada", catalog.StatusMessage, StringComparison.Ordinal);
        var stored = (await identity.GetEventBrandingAsync(DemoDataSeeder.TournamentId))!;
        Assert.Equal("Fase de grupos", stored.Subtitle);
        Assert.Null(stored.OrganizerLogo);
        Assert.Equal(
            ["Auspiciador A (ejemplo)", "Auspiciador B (ejemplo)", "Marca Local", "Auspiciador C (ejemplo)"],
            stored.Sponsors.Select(s => s.Name));
    }

    [Fact]
    public async Task El_editor_no_guarda_una_plantilla_invalida_y_cancelar_no_deja_rastro()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (desk, identity, _) = await CreateIdentityAsync(storage);
        var dialogs = new Dialogs();
        var editor = new EventIdentityViewModel(identity, dialogs, DemoDataSeeder.TournamentId, "Torneo Demostración FDNDA", null);
        var closed = new List<bool>();
        editor.CloseRequested += (_, saved) => closed.Add(saved);

        editor.DisplayTitle = "Copa";
        editor.PrimaryColor = "azul";
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.True(editor.IsStatusError);
        Assert.Contains("#RRGGBB", editor.StatusMessage, StringComparison.Ordinal);
        Assert.Empty(closed);

        dialogs.ImagePath = await WriteFileAsync(storage, "grande.png", TestImages.Png(5000, 300));
        await editor.LoadLogoCommand.ExecuteAsync(null);
        Assert.Null(editor.Logo);
        Assert.Contains("5000×300", editor.StatusMessage, StringComparison.Ordinal);

        editor.CancelCommand.Execute(null);
        Assert.Equal([false], closed);
        Assert.Null(await identity.GetEventBrandingAsync(DemoDataSeeder.TournamentId));
    }

    // ---------- Apoyo ----------

    private static async Task<Guid> RegisterAsync(
        TestDesk desk, IncidentType type, TeamSide side, Guid? rosterEntryId, ExclusionKind? exclusion = null, PenaltyResult? penalty = null)
    {
        var result = await desk.Operations.RegisterIncidentAsync(new RegisterIncident(
            Guid.NewGuid(), TestDesk.MatchId, type, side, rosterEntryId, desk.Operator, PenaltyResult: penalty, ExclusionKind: exclusion));
        Assert.True(result.IsSuccess, result.Message);
        return result.Snapshot!.Incidents.OrderBy(i => i.Sequence).Last().IncidentId;
    }

    private static async Task<(TestDesk Desk, IdentityOperations Identity, ScoreboardIdentityHub Hub)> CreateIdentityAsync(TempStorage storage)
    {
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var hub = new ScoreboardIdentityHub();
        var identity = new IdentityOperations(
            new SqliteIdentityRepository(storage.OpenDatabase()), desk.Catalog, new ImageFileReader(), new ExampleIdentitySource(),
            desk.Hub, hub, storage.Time, storage.Log);
        return (desk, identity, hub);
    }

    private static async Task<string> WriteFileAsync(TempStorage storage, string name, byte[] content)
    {
        var folder = Path.Combine(storage.Root, "archivos");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        await File.WriteAllBytesAsync(path, content);
        return path;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Yield();
            await Task.Delay(5);
        }

        Assert.True(condition());
    }

    private static AppInfo Info(TempStorage storage, TestDesk desk) =>
        new("Prueba", AppProfile.Demo, storage.Root, storage.Paths.DatabasePath, 3, "", desk.Operator.Name);

    private static T Immediate<T>(Func<T> create)
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

    private sealed class Dialogs : ICatalogDialogs
    {
        public string? ImagePath { get; set; }
        public string? LastImageTitle { get; private set; }
        public Func<EventIdentityViewModel, Task<bool>>? OnIdentity { get; set; }
        public string? PickRosterFile() => null;
        public Task<bool> ShowRosterImportAsync(RosterImportViewModel preview) => Task.FromResult(false);
        public Task<bool> ShowRulesEditorAsync(RulesEditorViewModel editor) => Task.FromResult(false);
        public bool Confirm(string title, string message) => true;
        public string? PickColor(string? currentHex) => null;

        public string? PickImageFile(string title)
        {
            LastImageTitle = title;
            return ImagePath;
        }

        public Task<bool> ShowEventIdentityAsync(EventIdentityViewModel editor) => OnIdentity?.Invoke(editor) ?? Task.FromResult(false);
    }
}
