using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Desktop.ViewModels;
using FDNDA.Waterpolo.Domain.Competitions;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;

namespace FDNDA.Waterpolo.Desktop.Tests;

/// <summary>R4 — diálogo «Reglas del evento» desde la pestaña «Eventos y nóminas» (RF03).</summary>
public class RulesEditorViewModelTests
{
    [Fact]
    public async Task Desde_la_interfaz_se_editan_las_reglas_del_evento_en_minutos_y_segundos()
    {
        await using var storage = await TempStorage.CreateAsync();
        var dialogs = new RulesDialogs();
        var (catalog, operations) = await CreateAsync(storage, dialogs);
        catalog.SelectedTournament = catalog.Tournaments.Single(t => t.TournamentId == DemoDataSeeder.TournamentId);
        Assert.Contains("DEMO", catalog.TournamentRulesText, StringComparison.Ordinal);
        Assert.Contains("4 periodos × 8:00", catalog.TournamentRulesText, StringComparison.Ordinal);

        dialogs.OnRules = async editor =>
        {
            Assert.Equal("4", editor.PeriodCountText);
            Assert.Equal("8:00", editor.PeriodDurationText);
            Assert.Contains("Los 2 partidos programados", editor.ApplicabilityText, StringComparison.Ordinal);
            Assert.Equal("Sin cambios respecto de las reglas actuales.", editor.OutcomeText);

            editor.PeriodDurationText = "7:30";
            editor.BreakDurationText = "1:30";
            editor.TimeoutsText = "3";
            editor.PenaltyShootoutEnabled = true;
            Assert.Contains("sin aprobación técnica", editor.OutcomeText, StringComparison.Ordinal);
            return await SaveAsync(editor);
        };
        await catalog.EditRulesCommand.ExecuteAsync(null);

        Assert.False(catalog.IsStatusError, catalog.StatusMessage);
        Assert.Contains("guardadas", catalog.StatusMessage, StringComparison.Ordinal);
        var stored = (await operations.GetTournamentAsync(DemoDataSeeder.TournamentId))!.Rules;
        Assert.Equal((450, 90, 3, true), (stored.PeriodDurationSeconds, stored.BreakDurationSeconds, stored.TimeoutsPerTeam, stored.PenaltyShootoutEnabled));
        Assert.Equal(RulesetSnapshot.CustomPresetName, stored.PresetName);
        Assert.Contains("4 periodos × 7:30", catalog.TournamentRulesText, StringComparison.Ordinal);
        Assert.Contains("tanda de penales: sí", catalog.TournamentRulesText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("8", 480)]
    [InlineData("7:30", 450)]
    [InlineData(" 0:45 ", 45)]
    [InlineData("10:00", 600)]
    public void La_duracion_se_escribe_en_minutos_o_como_minutos_y_segundos(string written, int expectedSeconds)
    {
        var editor = NewEditor(out _);
        editor.BreakDurationText = written;

        Assert.True(editor.TryBuild(out var rules, out var problem), problem);
        Assert.Equal(expectedSeconds, rules.BreakDurationSeconds);
    }

    [Theory]
    [InlineData("ocho")]
    [InlineData("8:5")]
    [InlineData("8:60")]
    [InlineData("-8")]
    [InlineData("1:2:3")]
    [InlineData("")]
    public void Una_duracion_mal_escrita_no_se_guarda_y_lo_explica(string written)
    {
        var editor = NewEditor(out var saved);
        editor.PeriodDurationText = written;

        editor.SaveCommand.Execute(null);

        Assert.False(saved());
        Assert.Contains("minutos:segundos", editor.StatusMessage, StringComparison.Ordinal);
        Assert.StartsWith("Revise:", editor.OutcomeText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_valor_fuera_de_rango_no_se_guarda_y_el_evento_conserva_sus_reglas()
    {
        await using var storage = await TempStorage.CreateAsync();
        var dialogs = new RulesDialogs();
        var (catalog, operations) = await CreateAsync(storage, dialogs);
        catalog.SelectedTournament = catalog.Tournaments.Single(t => t.TournamentId == DemoDataSeeder.TournamentId);

        dialogs.OnRules = async editor =>
        {
            editor.PeriodCountText = "0";
            await editor.SaveCommand.ExecuteAsync(null);
            Assert.Contains("entre 1 y 20", editor.StatusMessage, StringComparison.Ordinal);
            editor.PeriodCountText = "4";
            editor.ShotClockAlternateText = "35";
            await editor.SaveCommand.ExecuteAsync(null);
            Assert.Contains("no superar la posesión completa", editor.StatusMessage, StringComparison.Ordinal);
            editor.CancelCommand.Execute(null);
            return false;
        };
        await catalog.EditRulesCommand.ExecuteAsync(null);

        Assert.Equal("Edición de reglas cancelada: no se guardó nada.", catalog.StatusMessage);
        Assert.Equal(RulesetSnapshot.DemoPreset(), (await operations.GetTournamentAsync(DemoDataSeeder.TournamentId))!.Rules);
    }

    [Fact]
    public async Task Cargar_DEMO_restituye_los_valores_ilustrativos_y_el_preset_de_competencia_queda_pendiente()
    {
        await using var storage = await TempStorage.CreateAsync();
        var dialogs = new RulesDialogs();
        var (catalog, operations) = await CreateAsync(storage, dialogs);
        var custom = RulesetSnapshot.ForEvent(2, 600, 60, 300, 1, 25, 15, false);
        Assert.True((await operations.UpdateTournamentRulesAsync(DemoDataSeeder.TournamentId, custom)).IsSuccess);
        await catalog.RefreshCommand.ExecuteAsync(null);
        catalog.SelectedTournament = catalog.Tournaments.Single(t => t.TournamentId == DemoDataSeeder.TournamentId);

        dialogs.OnRules = async editor =>
        {
            Assert.Contains("sin aprobación técnica", editor.CurrentPresetText, StringComparison.Ordinal);
            editor.LoadDemoCommand.Execute(null);
            Assert.Equal(("4", "8:00", "30", "20"), (editor.PeriodCountText, editor.PeriodDurationText, editor.ShotClockText, editor.ShotClockAlternateText));
            Assert.Contains("preset DEMO", editor.OutcomeText, StringComparison.Ordinal);
            return await SaveAsync(editor);
        };
        await catalog.EditRulesCommand.ExecuteAsync(null);

        Assert.Equal(RulesetSnapshot.DemoPreset(), (await operations.GetTournamentAsync(DemoDataSeeder.TournamentId))!.Rules);
        Assert.Contains("pendiente de aprobación", RulesEditorViewModel.CompetitionPresetNote, StringComparison.Ordinal);
        Assert.Contains("nunca escribe en la consola", RulesEditorViewModel.ShotClockNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task El_editor_cuenta_los_partidos_al_abrirse_aunque_la_lista_se_haya_cargado_antes()
    {
        await using var storage = await TempStorage.CreateAsync();
        var dialogs = new RulesDialogs();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var operations = new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true);
        var catalog = new CatalogViewModel(operations, desk.Operations, dialogs);
        await catalog.InitializeAsync();
        catalog.SelectedTournament = catalog.Tournaments.Single(t => t.TournamentId == DemoDataSeeder.TournamentId);
        await desk.StartAsync(); // la lista de la pestaña no se actualizó

        string? applicability = null;
        dialogs.OnRules = editor =>
        {
            applicability = editor.ApplicabilityText;
            return Task.FromResult(false);
        };
        await catalog.EditRulesCommand.ExecuteAsync(null);

        Assert.Equal(
            "El partido programado de este evento tomará estas reglas al iniciarse. El partido ya iniciado o cerrado conserva las reglas con las que empezó.",
            applicability);
    }

    [Fact]
    public void Al_corregir_un_valor_se_borra_el_error_anterior()
    {
        var editor = NewEditor(out _);
        editor.PeriodCountText = "0";
        editor.SaveCommand.Execute(null);
        Assert.NotEmpty(editor.StatusMessage);

        editor.PeriodCountText = "4";

        Assert.Empty(editor.StatusMessage);
        Assert.Equal("Sin cambios respecto de las reglas actuales.", editor.OutcomeText);
    }

    [Fact]
    public async Task Sin_evento_elegido_no_se_pueden_editar_reglas()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = await CreateAsync(storage, new RulesDialogs());

        Assert.False(catalog.EditRulesCommand.CanExecute(null));
        catalog.SelectedTournament = catalog.Tournaments[0];
        Assert.True(catalog.EditRulesCommand.CanExecute(null));
    }

    // ---------- Apoyo ----------

    private static async Task<bool> SaveAsync(RulesEditorViewModel editor)
    {
        var saved = false;
        editor.CloseRequested += (_, ok) => saved = ok;
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.True(saved, editor.StatusMessage);
        return saved;
    }

    /// <summary>Editor suelto sobre reglas demo; el catálogo nunca se llama si los textos no son válidos.</summary>
    private static RulesEditorViewModel NewEditor(out Func<bool> saved)
    {
        var editor = new RulesEditorViewModel(null!, Guid.NewGuid(), "Evento", RulesetSnapshot.DemoPreset(), 0, 0);
        var closed = false;
        editor.CloseRequested += (_, ok) => closed = ok;
        saved = () => closed;
        return editor;
    }

    private static async Task<(CatalogViewModel Catalog, CatalogOperations Operations)> CreateAsync(TempStorage storage, RulesDialogs dialogs)
    {
        var desk = new TestDesk(storage);
        var operations = new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true);
        var catalog = new CatalogViewModel(operations, desk.Operations, dialogs);
        await catalog.InitializeAsync();
        return (catalog, operations);
    }

    private sealed class RulesDialogs : ICatalogDialogs
    {
        public Func<RulesEditorViewModel, Task<bool>> OnRules { get; set; } = _ => Task.FromResult(false);

        public Task<bool> ShowRulesEditorAsync(RulesEditorViewModel editor) => OnRules(editor);

        public string? PickRosterFile() => null;

        public Task<bool> ShowRosterImportAsync(RosterImportViewModel preview) => Task.FromResult(false);

        public bool Confirm(string title, string message) => true;

        public string? PickColor(string? currentHex) => null;
    }
}
