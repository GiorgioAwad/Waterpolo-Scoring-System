using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Desktop.ViewModels;
using FDNDA.Waterpolo.Infrastructure.Import;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;

namespace FDNDA.Waterpolo.Desktop.Tests;

/// <summary>R3 — nómina en la pestaña «Eventos y nóminas»: edición manual e importación con vista previa.</summary>
public class RosterViewModelTests
{
    [Fact]
    public async Task Desde_la_interfaz_se_agrega_corrige_y_quita_un_jugador_de_la_nomina()
    {
        await using var storage = await TempStorage.CreateAsync();
        var dialogs = new FakeDialogs();
        var (catalog, operations) = await CreateAsync(storage, dialogs);
        await SelectNewTournamentAndTeamAsync(catalog, operations);

        Assert.True(catalog.CanEditRoster);
        Assert.Equal("1", catalog.RosterCapText); // propone el primer gorro libre
        catalog.RosterPlayerName = "Ana Ola";
        await catalog.SaveRosterPlayerCommand.ExecuteAsync(null);
        Assert.False(catalog.IsStatusError, catalog.StatusMessage);
        Assert.Equal("Ana Ola", Assert.Single(catalog.RosterLines).PlayerName);
        Assert.Equal("2", catalog.RosterCapText);
        Assert.Empty(catalog.RosterPlayerName);

        catalog.RosterPlayerName = "Rosa Espuma";
        catalog.RosterCapText = "1";
        await catalog.SaveRosterPlayerCommand.ExecuteAsync(null);
        Assert.True(catalog.IsStatusError);
        Assert.Contains("ya es de «Ana Ola»", catalog.StatusMessage, StringComparison.Ordinal);
        Assert.Single(catalog.RosterLines);

        catalog.SelectedRosterLine = catalog.RosterLines[0];
        Assert.Equal("Editar jugador (gorro 1)", catalog.RosterFormTitle);
        catalog.RosterPlayerName = "Ana Ola Mar";
        catalog.RosterCapText = "7";
        await catalog.SaveRosterPlayerCommand.ExecuteAsync(null);
        var edited = Assert.Single(catalog.RosterLines);
        Assert.Equal(("Ana Ola Mar", 7), (edited.PlayerName, edited.Cap));

        catalog.SelectedRosterLine = edited;
        await catalog.RemoveRosterPlayerCommand.ExecuteAsync(null);
        Assert.Empty(catalog.RosterLines);
        Assert.Contains("quitado de la nómina", catalog.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Quitar_pide_confirmacion_y_si_se_rechaza_no_cambia_nada()
    {
        await using var storage = await TempStorage.CreateAsync();
        var dialogs = new FakeDialogs { ConfirmAnswer = false };
        var (catalog, _) = await CreateAsync(storage, dialogs);
        catalog.SelectedTournament = catalog.Tournaments.Single(t => t.TournamentId == DemoDataSeeder.TournamentId);
        catalog.SelectedTeam = catalog.Teams.Single(t => t.TeamId == DemoDataSeeder.HomeTeamId);
        catalog.SelectedRosterLine = catalog.RosterLines[0];

        await catalog.RemoveRosterPlayerCommand.ExecuteAsync(null);

        Assert.Equal(13, catalog.RosterLines.Count);
        Assert.Equal(1, dialogs.Confirmations);
    }

    [Fact]
    public async Task Desde_la_interfaz_se_importa_la_plantilla_tras_la_vista_previa()
    {
        await using var storage = await TempStorage.CreateAsync();
        var dialogs = new FakeDialogs { File = RosterFiles.Sample("plantilla-nomina.xlsx"), OnPreview = ImportAsync };
        var (catalog, operations) = await CreateAsync(storage, dialogs);
        await SelectNewTournamentAndTeamAsync(catalog, operations);

        await catalog.ImportRosterCommand.ExecuteAsync(null);

        var shown = dialogs.Shown!;
        Assert.Equal("6 nuevos · 0 sin cambios · 0 por decidir", shown.CountsText);
        Assert.Equal("Al importar: 6 jugadores agregados.", shown.PlanText);
        Assert.False(catalog.IsStatusError, catalog.StatusMessage);
        Assert.StartsWith("Nómina importada", catalog.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(["Marco Arrecife", "Luis Marea", "Diego Coral"], catalog.RosterLines.Select(r => r.PlayerName));
    }

    [Fact]
    public async Task Una_fila_ambigua_impide_importar_hasta_que_el_operador_decide()
    {
        await using var storage = await TempStorage.CreateAsync();
        var dialogs = new FakeDialogs();
        var (catalog, operations) = await CreateAsync(storage, dialogs);
        await SelectNewTournamentAndTeamAsync(catalog, operations);
        dialogs.File = RosterFiles.Sample("plantilla-nomina.csv");
        dialogs.OnPreview = ImportAsync;
        await catalog.ImportRosterCommand.ExecuteAsync(null);

        dialogs.File = RosterFiles.Csv(storage, RosterFiles.Header, "DEL,Delfines del Pacífico,Luis Marea Ríos,2");
        dialogs.OnPreview = async preview =>
        {
            var row = Assert.Single(preview.Rows);
            Assert.True(row.NeedsDecision);
            Assert.Null(row.SelectedOption); // sin decisión por defecto
            Assert.False(preview.ImportCommand.CanExecute(null));
            Assert.Equal("Falta decidir la fila 2.", preview.PlanText);
            Assert.Equal(
                ["Mismo jugador: corregir su nombre", "Otro jugador: reemplaza a «Luis Marea»", "Omitir esta fila"],
                row.Options.Select(o => o.Text));

            row.SelectedOption = row.Options.Single(o => o.Value == RosterResolution.RenameExisting);
            Assert.True(preview.ImportCommand.CanExecute(null));
            Assert.Equal("Al importar: 1 nombre corregido.", preview.PlanText);
            return await ImportAsync(preview);
        };

        await catalog.ImportRosterCommand.ExecuteAsync(null);

        Assert.False(catalog.IsStatusError, catalog.StatusMessage);
        Assert.Equal("Luis Marea Ríos", catalog.RosterLines.Single(r => r.Cap == 2).PlayerName);
    }

    [Fact]
    public async Task Cancelar_la_vista_previa_no_guarda_nada_y_lo_dice()
    {
        await using var storage = await TempStorage.CreateAsync();
        var dialogs = new FakeDialogs { File = RosterFiles.Sample("plantilla-nomina.csv"), OnPreview = _ => Task.FromResult(false) };
        var (catalog, operations) = await CreateAsync(storage, dialogs);
        await SelectNewTournamentAndTeamAsync(catalog, operations);

        await catalog.ImportRosterCommand.ExecuteAsync(null);

        Assert.NotNull(dialogs.Shown);
        Assert.Equal("Importación cancelada: no se guardó nada.", catalog.StatusMessage);
        Assert.Empty(catalog.RosterLines);
    }

    [Fact]
    public async Task Un_archivo_con_errores_muestra_fila_y_columna_y_no_deja_importar()
    {
        await using var storage = await TempStorage.CreateAsync();
        var dialogs = new FakeDialogs();
        var (catalog, operations) = await CreateAsync(storage, dialogs);
        await SelectNewTournamentAndTeamAsync(catalog, operations);
        dialogs.File = RosterFiles.Csv(storage, RosterFiles.Header, "DEL,Delfines del Pacífico,Marco Arrecife,cero");
        dialogs.OnPreview = preview =>
        {
            Assert.True(preview.HasErrors);
            Assert.StartsWith("Fila 2, columna numero_gorro (D):", Assert.Single(preview.Errors), StringComparison.Ordinal);
            Assert.False(preview.ImportCommand.CanExecute(null));
            return Task.FromResult(false);
        };

        await catalog.ImportRosterCommand.ExecuteAsync(null);

        Assert.Empty(catalog.RosterLines);
    }

    [Fact]
    public async Task Un_libro_con_macros_se_rechaza_sin_abrir_la_vista_previa()
    {
        await using var storage = await TempStorage.CreateAsync();
        var dialogs = new FakeDialogs();
        var (catalog, operations) = await CreateAsync(storage, dialogs);
        await SelectNewTournamentAndTeamAsync(catalog, operations);
        dialogs.File = RosterFiles.Xlsx(storage, [["equipo_codigo", "equipo_nombre", "jugador_nombre", "numero_gorro"]], extension: ".xlsm");

        await catalog.ImportRosterCommand.ExecuteAsync(null);

        Assert.Null(dialogs.Shown);
        Assert.True(catalog.IsStatusError);
        Assert.Contains("macros", catalog.StatusMessage, StringComparison.Ordinal);
    }

    // ---------- Colores de equipo ----------

    [Fact]
    public async Task El_color_del_equipo_se_elige_con_la_paleta_con_el_selector_o_escrito_sin_numeral()
    {
        await using var storage = await TempStorage.CreateAsync();
        var dialogs = new FakeDialogs();
        var (catalog, operations) = await CreateAsync(storage, dialogs);
        catalog.SelectedTeam = catalog.Teams.Single(t => t.TeamId == DemoDataSeeder.HomeTeamId);

        var rojo = Assert.Single(CatalogViewModel.Palette, c => c.Name == "Rojo");
        catalog.PickTeamColorCommand.Execute(rojo.Hex);
        Assert.Equal("#C62828", catalog.TeamColor);

        dialogs.PickedColor = "#123456";
        catalog.ChooseTeamColorCommand.Execute(null);
        Assert.Equal("#123456", catalog.TeamColor);
        dialogs.PickedColor = null; // cancelar el selector no cambia el color
        catalog.ChooseTeamColorCommand.Execute(null);
        Assert.Equal("#123456", catalog.TeamColor);

        catalog.TeamColor = "7b1e2b"; // escrito a mano, sin «#»
        await catalog.SaveTeamCommand.ExecuteAsync(null);
        Assert.False(catalog.IsStatusError, catalog.StatusMessage);
        Assert.Equal("#7B1E2B", (await operations.ListTeamsAsync()).Single(t => t.Id == DemoDataSeeder.HomeTeamId).ColorHex);

        catalog.TeamColor = "rojo";
        await catalog.SaveTeamCommand.ExecuteAsync(null);
        Assert.True(catalog.IsStatusError);
        Assert.StartsWith("Elija un color de la paleta", catalog.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task El_color_de_cada_equipo_del_partido_se_elige_con_el_selector()
    {
        await using var storage = await TempStorage.CreateAsync();
        var dialogs = new FakeDialogs { PickedColor = "#00897B" };
        var (catalog, _) = await CreateAsync(storage, dialogs);
        catalog.ScheduleHome = catalog.Teams.Single(t => t.TeamId == DemoDataSeeder.HomeTeamId);
        catalog.ScheduleAway = catalog.Teams.Single(t => t.TeamId == DemoDataSeeder.AwayTeamId);

        catalog.ChooseHomeColorCommand.Execute(null);
        dialogs.PickedColor = null;
        catalog.ChooseAwayColorCommand.Execute(null);

        Assert.Equal("#00897B", catalog.ScheduleHomeColor);
        Assert.Equal("#C62828", catalog.ScheduleAwayColor); // cancelado: queda el del catálogo
    }

    // ---------- Apoyo ----------

    private static async Task<bool> ImportAsync(RosterImportViewModel preview)
    {
        var closed = false;
        preview.CloseRequested += (_, imported) => closed = imported;
        await preview.ImportCommand.ExecuteAsync(null);
        Assert.True(closed, preview.StatusMessage);
        return closed;
    }

    private static async Task<(CatalogViewModel Catalog, CatalogOperations Operations)> CreateAsync(TempStorage storage, FakeDialogs dialogs)
    {
        var desk = new TestDesk(storage);
        var operations = new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true, new RosterFileReader());
        var catalog = new CatalogViewModel(operations, desk.Operations, dialogs);
        await catalog.InitializeAsync();
        return (catalog, operations);
    }

    private static async Task SelectNewTournamentAndTeamAsync(CatalogViewModel catalog, CatalogOperations operations)
    {
        var id = Guid.NewGuid();
        Assert.True((await operations.CreateTournamentAsync(id, "Copa de Nóminas")).IsSuccess);
        await catalog.RefreshCommand.ExecuteAsync(null);
        catalog.SelectedTournament = catalog.Tournaments.Single(t => t.TournamentId == id);
        catalog.SelectedTeam = catalog.Teams.Single(t => t.TeamId == DemoDataSeeder.HomeTeamId);
    }

    private sealed class FakeDialogs : ICatalogDialogs
    {
        public string? File { get; set; }

        public Func<RosterImportViewModel, Task<bool>> OnPreview { get; set; } = _ => Task.FromResult(false);

        public bool ConfirmAnswer { get; set; } = true;

        public int Confirmations { get; private set; }

        public RosterImportViewModel? Shown { get; private set; }

        public string? PickRosterFile() => File;

        public Task<bool> ShowRosterImportAsync(RosterImportViewModel preview)
        {
            Shown = preview;
            return OnPreview(preview);
        }

        public Task<bool> ShowRulesEditorAsync(RulesEditorViewModel editor) => Task.FromResult(false);

        public bool Confirm(string title, string message)
        {
            Confirmations++;
            return ConfirmAnswer;
        }

        public string? PickedColor { get; set; }

        public string? PickColor(string? currentHex) => PickedColor;
    }
}
