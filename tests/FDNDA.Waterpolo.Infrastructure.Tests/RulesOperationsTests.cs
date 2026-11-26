using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Domain.Competitions;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;

namespace FDNDA.Waterpolo.Infrastructure.Tests;

/// <summary>R4 — reglas por evento sobre SQLite temporal (RF03).</summary>
public class RulesOperationsTests
{
    private static readonly RulesetSnapshot Custom =
        RulesetSnapshot.ForEvent(4, (7 * 60) + 30, 90, 4 * 60, 3, 28, 18, penaltyShootoutEnabled: true);

    [Fact]
    public async Task Editar_las_reglas_del_evento_no_altera_el_partido_en_juego_y_las_toma_el_que_se_inicia_despues()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, desk) = await CreateAsync(storage);
        var started = await desk.StartAsync();

        var result = await catalog.UpdateTournamentRulesAsync(DemoDataSeeder.TournamentId, Custom);

        Assert.Equal(CommandOutcome.Applied, result.Outcome);
        Assert.Contains("El partido programado las tomará al iniciarse", result.Message, StringComparison.Ordinal);
        Assert.Contains("El partido ya iniciado o cerrado conserva sus reglas", result.Message, StringComparison.Ordinal);

        var stored = (await catalog.GetTournamentAsync(DemoDataSeeder.TournamentId))!.Rules;
        Assert.Equal(Custom, stored);
        var listed = Assert.Single(await catalog.ListTournamentsAsync(), t => t.TournamentId == DemoDataSeeder.TournamentId);
        Assert.Equal(Custom, listed.Rules);
        Assert.Equal((2, 1), (listed.MatchCount, listed.ScheduledMatchCount));

        var live = (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!;
        Assert.Equal(started.Rules, live.Rules);
        Assert.True(live.Rules!.IsDemoPreset);

        await desk.Operations.FinishMatchAsync(new FinishMatch(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        var second = (await desk.Operations.StartMatchAsync(
            new StartMatch(Guid.NewGuid(), DemoDataSeeder.SecondMatchId, desk.Operator))).Snapshot!;
        Assert.False(second.Rules!.IsDemoPreset);
        Assert.Equal(RulesetSnapshot.CustomPresetName, second.Rules.PresetName);
        Assert.Equal(
            (4, 450, 28, 18),
            (second.Rules.PeriodCount, second.Rules.PeriodDurationSeconds, second.Rules.ShotClockSeconds, second.Rules.ShotClockAlternateSeconds));
    }

    [Fact]
    public async Task Un_partido_cerrado_y_reabierto_conserva_sus_reglas_tras_editar_el_evento()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, desk) = await CreateAsync(storage);
        var started = await desk.StartAsync();
        await desk.Operations.FinishMatchAsync(new FinishMatch(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));

        Assert.True((await catalog.UpdateTournamentRulesAsync(DemoDataSeeder.TournamentId, Custom)).IsSuccess);

        var closed = (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!;
        Assert.Equal(started.Rules, closed.Rules);
        var reopened = await desk.Operations.ReopenMatchAsync(
            new ReopenMatch(Guid.NewGuid(), TestDesk.MatchId, "Corrección de acta", desk.Operator));
        Assert.Equal(CommandOutcome.Applied, reopened.Outcome);
        Assert.Equal(started.Rules, reopened.Snapshot!.Rules);

        // Tras reiniciar el programa sigue igual: la copia vive con el partido.
        var afterRestart = (await new TestDesk(storage).Queries.GetSnapshotAsync(TestDesk.MatchId))!;
        Assert.Equal(started.Rules, afterRestart.Rules);
    }

    [Fact]
    public async Task Volver_a_los_valores_demo_restituye_el_preset_demo_y_repetir_no_escribe()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = await CreateAsync(storage);
        Assert.True((await catalog.UpdateTournamentRulesAsync(DemoDataSeeder.TournamentId, Custom)).IsSuccess);

        var same = await catalog.UpdateTournamentRulesAsync(DemoDataSeeder.TournamentId, Custom);
        var back = await catalog.UpdateTournamentRulesAsync(DemoDataSeeder.TournamentId, RulesetSnapshot.DemoPreset());

        Assert.Equal(CommandOutcome.AlreadyApplied, same.Outcome);
        Assert.Equal(CommandOutcome.Applied, back.Outcome);
        Assert.Contains("preset DEMO", back.Message, StringComparison.Ordinal);
        Assert.Contains("Los 2 partidos programados las tomarán al iniciarse", back.Message, StringComparison.Ordinal);
        Assert.Equal(RulesetSnapshot.DemoPreset(), (await catalog.GetTournamentAsync(DemoDataSeeder.TournamentId))!.Rules);
    }

    [Fact]
    public async Task Renombrar_el_evento_conserva_sus_reglas_y_un_evento_inexistente_se_rechaza()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = await CreateAsync(storage);
        Assert.True((await catalog.UpdateTournamentRulesAsync(DemoDataSeeder.TournamentId, Custom)).IsSuccess);

        Assert.True((await catalog.RenameTournamentAsync(DemoDataSeeder.TournamentId, "Torneo con reglas propias")).IsSuccess);
        var missing = await catalog.UpdateTournamentRulesAsync(Guid.NewGuid(), Custom);

        var tournament = (await catalog.GetTournamentAsync(DemoDataSeeder.TournamentId))!;
        Assert.Equal("Torneo con reglas propias", tournament.Name);
        Assert.Equal(Custom, tournament.Rules);
        Assert.Equal(CommandOutcome.Rejected, missing.Outcome);
    }

    private static async Task<(CatalogOperations Catalog, TestDesk Desk)> CreateAsync(TempStorage storage)
    {
        var desk = await new TestDesk(storage).SignInAsync(storage);
        return (new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true), desk);
    }
}
