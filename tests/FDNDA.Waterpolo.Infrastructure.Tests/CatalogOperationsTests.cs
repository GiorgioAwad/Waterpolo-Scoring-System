using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;

namespace FDNDA.Waterpolo.Infrastructure.Tests;

/// <summary>R2 — eventos, equipos y programación de partidos sobre SQLite temporal (RF01).</summary>
public class CatalogOperationsTests
{
    [Fact]
    public async Task Se_guardan_dos_equipos_distintos_y_se_programa_un_encuentro_en_el_evento_correcto()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, desk) = Create(storage);
        var tournamentId = Guid.NewGuid();
        var homeId = Guid.NewGuid();
        var awayId = Guid.NewGuid();

        Assert.Equal(CommandOutcome.Applied, (await catalog.CreateTournamentAsync(tournamentId, "Copa de Prueba 2027")).Outcome);
        Assert.Equal(CommandOutcome.Applied, (await catalog.CreateTeamAsync(homeId, "aaa", "Equipo Ámbar", "#0b63ce", "Blanco")).Outcome);
        Assert.Equal(CommandOutcome.Applied, (await catalog.CreateTeamAsync(awayId, "BBB", "Equipo Bruma", "#C62828", "Azul")).Outcome);

        var matchId = Guid.NewGuid();
        var scheduledAt = new DateTimeOffset(2027, 3, 14, 20, 30, 0, TimeSpan.Zero);
        var result = await desk.Operations.ScheduleMatchAsync(
            new ScheduleMatch(matchId, tournamentId, "Fecha 1", scheduledAt, homeId, awayId));

        Assert.Equal(CommandOutcome.Applied, result.Outcome);
        var stored = (await new SqliteMatchRepository(storage.OpenDatabase()).GetAsync(matchId))!;
        Assert.Equal(tournamentId, stored.TournamentId);
        Assert.Equal("Copa de Prueba 2027", stored.TournamentName);
        Assert.Equal(MatchStatus.Scheduled, stored.Status);
        Assert.Equal(scheduledAt, stored.ScheduledAtUtc);
        Assert.Equal(homeId, stored.Home.TeamId);
        Assert.Equal("AAA", stored.Home.Code); // el código se normaliza a mayúsculas
        Assert.Equal("#0B63CE", stored.Home.ColorHex);
        Assert.Equal(awayId, stored.Away.TeamId);
        Assert.Empty(stored.Roster); // el evento nuevo aún no tiene nómina (R3)

        var listed = Assert.Single(await desk.Queries.ListAsync(), m => m.MatchId == matchId);
        Assert.Equal("Copa de Prueba 2027", listed.TournamentName);
        Assert.Contains(await catalog.ListTournamentsAsync(), t => t.TournamentId == tournamentId && t.MatchCount == 1);
        Assert.Equal(4, (await catalog.ListTeamsAsync()).Count); // dos demo y dos nuevos
    }

    [Fact]
    public async Task Programar_congela_la_nomina_del_evento_y_los_colores_elegidos_para_el_partido()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, desk) = Create(storage);
        var matchId = Guid.NewGuid();

        var result = await desk.Operations.ScheduleMatchAsync(new ScheduleMatch(
            matchId, DemoDataSeeder.TournamentId, "Partido demo 3", null, DemoDataSeeder.HomeTeamId, DemoDataSeeder.AwayTeamId,
            HomeColorHex: "#FFFFFF", HomeCapColor: "Blanco con números negros", AwayCapColor: "Negro"));

        Assert.Equal(CommandOutcome.Applied, result.Outcome);
        var snapshot = result.Snapshot!;
        Assert.Equal(13, snapshot.Home.Roster.Count);
        Assert.Equal(13, snapshot.Away.Roster.Count);
        Assert.Equal(Enumerable.Range(1, 13), snapshot.Home.Roster.Select(r => r.Cap));
        Assert.Equal("Marco Arrecife", snapshot.Home.Roster[0].PlayerName);
        Assert.Equal("#FFFFFF", snapshot.Home.ColorHex);
        Assert.Equal("Blanco con números negros", snapshot.Home.CapColor);
        Assert.Equal("#C62828", snapshot.Away.ColorHex); // vacío: toma el del catálogo
        Assert.Equal("Negro", snapshot.Away.CapColor);

        // El catálogo no cambia por los colores elegidos para un partido.
        var catalogHome = Assert.Single(await catalog.ListTeamsAsync(), t => t.Id == DemoDataSeeder.HomeTeamId);
        Assert.Equal("#0B63CE", catalogHome.ColorHex);
        Assert.Equal("Blanco", catalogHome.CapColor);
    }

    [Fact]
    public async Task Programar_no_publica_ni_cambia_el_partido_abierto_en_la_mesa()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (_, desk) = Create(storage);
        await desk.SignInAsync(storage);
        await desk.Operations.OpenMatchAsync(TestDesk.MatchId);
        var publications = desk.Publications.Count;

        await desk.Operations.ScheduleMatchAsync(new ScheduleMatch(
            Guid.NewGuid(), DemoDataSeeder.TournamentId, "Otro", null, DemoDataSeeder.HomeTeamId, DemoDataSeeder.AwayTeamId));

        Assert.Equal(publications, desk.Publications.Count);
        Assert.Equal(TestDesk.MatchId, desk.Hub.Current!.MatchId);
    }

    [Fact]
    public async Task Se_rechaza_el_mismo_equipo_como_local_y_visita_sin_escribir()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (_, desk) = Create(storage);

        var result = await desk.Operations.ScheduleMatchAsync(new ScheduleMatch(
            Guid.NewGuid(), DemoDataSeeder.TournamentId, "Inválido", null, DemoDataSeeder.HomeTeamId, DemoDataSeeder.HomeTeamId));

        Assert.Equal(CommandOutcome.Rejected, result.Outcome);
        Assert.Contains("distintos", result.Message, StringComparison.Ordinal);
        Assert.Equal(2, (await desk.Queries.ListAsync()).Count);
    }

    [Theory]
    [InlineData(true, false, "evento")]
    [InlineData(false, true, "equipo")]
    public async Task Se_rechaza_programar_sin_evento_o_equipo_existente(bool missingTournament, bool missingTeam, string expected)
    {
        await using var storage = await TempStorage.CreateAsync();
        var (_, desk) = Create(storage);

        var result = await desk.Operations.ScheduleMatchAsync(new ScheduleMatch(
            Guid.NewGuid(),
            missingTournament ? Guid.NewGuid() : DemoDataSeeder.TournamentId,
            "Inválido",
            null,
            DemoDataSeeder.HomeTeamId,
            missingTeam ? Guid.NewGuid() : DemoDataSeeder.AwayTeamId));

        Assert.Equal(CommandOutcome.Rejected, result.Outcome);
        Assert.Contains(expected, result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, (await desk.Queries.ListAsync()).Count);
    }

    [Fact]
    public async Task Programar_con_descripcion_vacia_o_color_invalido_se_rechaza()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (_, desk) = Create(storage);

        var noLabel = await desk.Operations.ScheduleMatchAsync(new ScheduleMatch(
            Guid.NewGuid(), DemoDataSeeder.TournamentId, "  ", null, DemoDataSeeder.HomeTeamId, DemoDataSeeder.AwayTeamId));
        var badColor = await desk.Operations.ScheduleMatchAsync(new ScheduleMatch(
            Guid.NewGuid(), DemoDataSeeder.TournamentId, "Fecha 2", null, DemoDataSeeder.HomeTeamId, DemoDataSeeder.AwayTeamId,
            HomeColorHex: "azul"));

        Assert.Equal(CommandOutcome.Rejected, noLabel.Outcome);
        Assert.Equal(CommandOutcome.Rejected, badColor.Outcome);
        Assert.Contains("#RRGGBB", badColor.Message, StringComparison.Ordinal);
        Assert.Equal(2, (await desk.Queries.ListAsync()).Count);
    }

    [Fact]
    public async Task Reenviar_el_mismo_alta_no_duplica_eventos_equipos_ni_partidos()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, desk) = Create(storage);
        var tournamentId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var schedule = new ScheduleMatch(
            matchId, DemoDataSeeder.TournamentId, "Fecha 3", null, DemoDataSeeder.HomeTeamId, DemoDataSeeder.AwayTeamId);

        await catalog.CreateTournamentAsync(tournamentId, "Evento único");
        var tournamentRetry = await catalog.CreateTournamentAsync(tournamentId, "Evento único");
        await catalog.CreateTeamAsync(teamId, "UNI", "Equipo Único", "#123456", "Verde");
        var teamRetry = await catalog.CreateTeamAsync(teamId, "UNI", "Equipo Único", "#123456", "Verde");
        await desk.Operations.ScheduleMatchAsync(schedule);
        var matchRetry = await desk.Operations.ScheduleMatchAsync(schedule);

        Assert.Equal(CommandOutcome.AlreadyApplied, tournamentRetry.Outcome);
        Assert.Equal(CommandOutcome.AlreadyApplied, teamRetry.Outcome);
        Assert.Equal(CommandOutcome.AlreadyApplied, matchRetry.Outcome);
        Assert.Equal(matchId, matchRetry.Snapshot!.MatchId);
        Assert.Equal(2, (await catalog.ListTournamentsAsync()).Count);
        Assert.Equal(3, (await catalog.ListTeamsAsync()).Count);
        Assert.Equal(3, (await desk.Queries.ListAsync()).Count);
    }

    [Fact]
    public async Task El_codigo_de_equipo_es_unico_sin_distinguir_mayusculas()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);

        var duplicate = await catalog.CreateTeamAsync(Guid.NewGuid(), "del", "Otro equipo", "#000000", "Negro");
        var renameToDuplicate = await catalog.UpdateTeamAsync(DemoDataSeeder.AwayTeamId, "DEL", "Tiburones", "#C62828", "Azul");

        Assert.Equal(CommandOutcome.Rejected, duplicate.Outcome);
        Assert.Contains("DEL", duplicate.Message, StringComparison.Ordinal);
        Assert.Equal(CommandOutcome.Rejected, renameToDuplicate.Outcome);
        Assert.Equal(2, (await catalog.ListTeamsAsync()).Count);
        Assert.Equal("TIB", Assert.Single(await catalog.ListTeamsAsync(), t => t.Id == DemoDataSeeder.AwayTeamId).Code);
    }

    [Fact]
    public async Task El_nombre_de_evento_no_se_repite()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);

        var duplicate = await catalog.CreateTournamentAsync(Guid.NewGuid(), "torneo demostración fdnda");

        Assert.Equal(CommandOutcome.Rejected, duplicate.Outcome);
        Assert.Single(await catalog.ListTournamentsAsync());
    }

    [Fact]
    public async Task Datos_invalidos_del_equipo_se_rechazan_con_mensaje_en_espanol()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);

        var noName = await catalog.CreateTeamAsync(Guid.NewGuid(), "XYZ", " ", "#000000", "Negro");
        var badColor = await catalog.CreateTeamAsync(Guid.NewGuid(), "XYZ", "Equipo", "rojo", "Negro");
        var longCode = await catalog.CreateTeamAsync(Guid.NewGuid(), "CODIGOLARGO", "Equipo", "#000000", "Negro");

        Assert.Equal("El equipo debe tener nombre.", noName.Message);
        Assert.Equal("Elija un color de la paleta o escríbalo como #RRGGBB (por ejemplo #0B63CE).", badColor.Message);
        Assert.Equal(CommandOutcome.Rejected, longCode.Outcome);
        Assert.Equal(2, (await catalog.ListTeamsAsync()).Count);
    }

    [Fact]
    public async Task Editar_el_catalogo_no_altera_partidos_iniciados_ni_ya_programados()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, desk) = Create(storage);
        await desk.SignInAsync(storage);
        var started = await desk.StartAsync();
        var scheduledBefore = await desk.Queries.GetSnapshotAsync(DemoDataSeeder.SecondMatchId);

        Assert.Equal(CommandOutcome.Applied, (await catalog.UpdateTeamAsync(
            DemoDataSeeder.HomeTeamId, "DPA", "Delfines Renombrados", "#00FF00", "Verde")).Outcome);
        Assert.Equal(CommandOutcome.Applied, (await catalog.RenameTournamentAsync(
            DemoDataSeeder.TournamentId, "Torneo Renombrado")).Outcome);

        var startedAfter = (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!;
        var scheduledAfter = (await desk.Queries.GetSnapshotAsync(DemoDataSeeder.SecondMatchId))!;
        Assert.Equal(started.Home.Name, startedAfter.Home.Name);
        Assert.Equal("DEL", startedAfter.Home.Code);
        Assert.Equal(started.Home.ColorHex, startedAfter.Home.ColorHex);
        Assert.Equal(started.TournamentName, startedAfter.TournamentName);
        Assert.Equal(scheduledBefore!.Away.Name, scheduledAfter.Away.Name);
        Assert.Equal(scheduledBefore.TournamentName, scheduledAfter.TournamentName);

        // Un partido programado después de la edición sí toma los datos nuevos.
        var fresh = await desk.Operations.ScheduleMatchAsync(new ScheduleMatch(
            Guid.NewGuid(), DemoDataSeeder.TournamentId, "Fecha nueva", null, DemoDataSeeder.HomeTeamId, DemoDataSeeder.AwayTeamId));
        Assert.Equal("Delfines Renombrados", fresh.Snapshot!.Home.Name);
        Assert.Equal("Torneo Renombrado", fresh.Snapshot.TournamentName);
    }

    [Fact]
    public async Task Un_evento_nuevo_usa_el_preset_demo_identificado_y_respeta_el_perfil()
    {
        await using var storage = await TempStorage.CreateAsync();
        var database = storage.OpenDatabase();
        var production = new CatalogOperations(new SqliteCatalogRepository(database), storage.Time, storage.Log, demoData: false);
        var id = Guid.NewGuid();

        await production.CreateTournamentAsync(id, "Evento de producción");

        var created = Assert.Single(await production.ListTournamentsAsync(), t => t.TournamentId == id);
        Assert.False(created.IsDemo);
        Assert.True(created.RulesIsDemoPreset);
        Assert.Contains("DEMO", created.RulesPresetName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_cambios_no_se_escribe_y_lo_informa()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);

        var team = await catalog.UpdateTeamAsync(DemoDataSeeder.HomeTeamId, "DEL", "Delfines del Pacífico", "#0B63CE", "Blanco");
        var tournament = await catalog.RenameTournamentAsync(DemoDataSeeder.TournamentId, "Torneo Demostración FDNDA");

        Assert.Equal(CommandOutcome.AlreadyApplied, team.Outcome);
        Assert.Equal(CommandOutcome.AlreadyApplied, tournament.Outcome);
    }

    [Fact]
    public async Task La_nomina_del_evento_se_lee_por_equipo_y_ordenada_por_gorro()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);

        var roster = await catalog.GetTournamentRosterAsync(DemoDataSeeder.TournamentId, DemoDataSeeder.AwayTeamId);
        var empty = await catalog.GetTournamentRosterAsync(Guid.NewGuid(), DemoDataSeeder.AwayTeamId);

        Assert.Equal(13, roster.Count);
        Assert.Equal("Renzo Aleta", roster[0].PlayerName);
        Assert.Equal(Enumerable.Range(1, 13), roster.Select(r => r.Cap));
        Assert.Empty(empty);
    }

    private static (CatalogOperations Catalog, TestDesk Desk) Create(TempStorage storage)
    {
        var desk = new TestDesk(storage);
        return (new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true), desk);
    }
}
