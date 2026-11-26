using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Infrastructure.Identity;
using Microsoft.Data.Sqlite;

namespace FDNDA.Waterpolo.Infrastructure.Persistence;

/// <summary>
/// Datos de demostración: un evento, dos equipos con nóminas ficticias y dos partidos.
/// Los nombres son inventados; no corresponden a personas ni clubes reales. Solo se cargan
/// en la base del perfil demo, separada de producción. Volver a ejecutarlo no duplica.
/// </summary>
public sealed class DemoDataSeeder(SqliteDatabase database, IMatchRepository matches, TimeProvider time)
{
    public static readonly Guid TournamentId = Id(1, 1);
    public static readonly Guid HomeTeamId = Id(2, 1);
    public static readonly Guid AwayTeamId = Id(2, 2);
    public static readonly Guid FirstMatchId = Id(5, 1);
    public static readonly Guid SecondMatchId = Id(5, 2);

    private static readonly string[] HomePlayers =
    [
        "Marco Arrecife", "Luis Marea", "Diego Coral", "Bruno Oleaje", "Tomás Brisa", "Iván Caleta", "Nico Bahía",
        "Samuel Rompiente", "Joel Corriente", "Adrián Espuma", "Mateo Faro", "Gael Muelle", "Thiago Atolón",
    ];

    private static readonly string[] AwayPlayers =
    [
        "Renzo Aleta", "Fabio Cardumen", "Julián Estela", "Óscar Bajamar", "Elías Remolino", "Dante Malecón", "Hugo Ensenada",
        "Simón Pleamar", "Lucas Barlovento", "Emilio Sotavento", "Andrés Maximiliano Contracorriente", "Pablo Ancla", "César Timón",
    ];

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var tournament = new Tournament(TournamentId, "Torneo Demostración FDNDA", isDemo: true, RulesetSnapshot.DemoPreset());
        var home = new Team(HomeTeamId, "DEL", "Delfines del Pacífico", "#0B63CE", "Blanco");
        var away = new Team(AwayTeamId, "TIB", "Club Deportivo Tiburones del Norte", "#C62828", "Azul");
        var homePlayers = BuildPlayers(home, HomePlayers, group: 3);
        var awayPlayers = BuildPlayers(away, AwayPlayers, group: 4);

        await using (var connection = await database.OpenAsync(ct).ConfigureAwait(false))
        {
            if (!await ExistsAsync(connection, "SELECT 1 FROM tournament WHERE id = $id;", TournamentId, ct).ConfigureAwait(false))
            {
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
                await SqliteCatalogRepository.InsertTournamentAsync(connection, transaction, tournament, now, ct).ConfigureAwait(false);

                foreach (var (team, players) in new[] { (home, homePlayers), (away, awayPlayers) })
                {
                    await SqliteCatalogRepository.InsertTeamAsync(connection, transaction, team, now, ct).ConfigureAwait(false);
                    for (var index = 0; index < players.Count; index++)
                    {
                        var player = players[index];
                        await SqliteCatalogRepository.InsertPlayerAsync(connection, transaction, player, now, ct).ConfigureAwait(false);
                        var entry = new TournamentRosterEntry(
                            Guid.NewGuid(), tournament.Id, team.Id, player.Id, new CapNumber(index + 1));
                        await SqliteCatalogRepository.InsertRosterEntryAsync(connection, transaction, entry, ct).ConfigureAwait(false);
                    }
                }

                // Escudos genéricos de los clubes ficticios (R7); la plantilla del evento se elige desde la mesa.
                foreach (var (team, file, assetId) in new[]
                {
                    (home, "escudo-delfines-ejemplo.png", Id(6, 1)),
                    (away, "escudo-tiburones-ejemplo.png", Id(6, 2)),
                })
                {
                    var image = ExampleIdentitySource.Resource(file);
                    var crest = new ImageAsset(assetId, AssetKind.Crest, image.FileName, image.Content);
                    await SqliteIdentityRepository.SetTeamCrestAsync(connection, transaction, team.Id, crest, now, ct).ConfigureAwait(false);
                }

                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
        }

        await ScheduleIfMissingAsync(FirstMatchId, tournament, "Partido demo 1", now.AddMinutes(15), home, homePlayers, away, awayPlayers, ct)
            .ConfigureAwait(false);
        await ScheduleIfMissingAsync(SecondMatchId, tournament, "Partido demo 2 (vuelta)", now.AddHours(2), away, awayPlayers, home, homePlayers, ct)
            .ConfigureAwait(false);
    }

    private async Task ScheduleIfMissingAsync(
        Guid matchId,
        Tournament tournament,
        string label,
        DateTimeOffset scheduledAtUtc,
        Team home,
        IReadOnlyList<Player> homePlayers,
        Team away,
        IReadOnlyList<Player> awayPlayers,
        CancellationToken ct)
    {
        if (await matches.GetAsync(matchId, ct).ConfigureAwait(false) is not null)
        {
            return;
        }

        var roster = homePlayers
            .Select((p, i) => new MatchRosterEntry(Guid.NewGuid(), TeamSide.Home, p.Id, p.FullName, new CapNumber(i + 1)))
            .Concat(awayPlayers.Select((p, i) => new MatchRosterEntry(Guid.NewGuid(), TeamSide.Away, p.Id, p.FullName, new CapNumber(i + 1))));

        var match = Match.Schedule(
            matchId, tournament.Id, tournament.Name, label, scheduledAtUtc, MatchTeam.From(home), MatchTeam.From(away), roster);

        // El identificador del comando es el del partido: la carga demo es idempotente.
        await matches.SaveAsync(match, new CommandStamp(matchId, "SeedDemoMatch", time.GetUtcNow()), ct).ConfigureAwait(false);
    }

    private static List<Player> BuildPlayers(Team team, string[] names, int group) =>
        names.Select((name, index) => new Player(Id(group, index + 1), team.Id, name)).ToList();

    private static async Task<bool> ExistsAsync(SqliteConnection connection, string sql, Guid id, CancellationToken ct)
    {
        await using var command = Db.Command(connection, null, sql).With("$id", Db.Id(id));
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    /// <summary>Identificadores fijos para que los datos demo sean reproducibles.</summary>
    private static Guid Id(int group, int number) =>
        new(0xD3300000u + (uint)group, 0, 0x4000, 0x80, 0, 0, 0, 0, 0, 0, (byte)number);
}
