using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;
using Microsoft.Data.Sqlite;

namespace FDNDA.Waterpolo.Infrastructure.Persistence;

/// <summary>
/// Repositorio concreto del catálogo (eventos, equipos, jugadores, nóminas de evento y operadores).
/// Las nóminas de evento se pueden reescribir; las de cada partido viven en <c>match_roster_entry</c>
/// y no cambian con ellas.
/// </summary>
public sealed class SqliteCatalogRepository(SqliteDatabase database) : ICatalogRepository, IOperatorRepository
{
    public async Task<IReadOnlyList<TournamentSummary>> ListTournamentsAsync(CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await using var command = Db.Command(
            connection,
            null,
            $"""
            SELECT t.id, t.name, t.is_demo,
                   (SELECT COUNT(*) FROM match m WHERE m.tournament_id = t.id AND m.status <> 'Cancelled'),
                   (SELECT COUNT(*) FROM match m WHERE m.tournament_id = t.id AND m.status = 'Scheduled'),
                   {RulesColumns}
            FROM tournament t
            ORDER BY t.created_at_utc, t.name;
            """);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var result = new List<TournamentSummary>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new TournamentSummary(
                Db.ReadId(reader, 0),
                reader.GetString(1),
                reader.GetInt32(2) == 1,
                ReadRules(reader, 5),
                reader.GetInt32(3),
                reader.GetInt32(4)));
        }

        return result;
    }

    public async Task<bool> AddTournamentAsync(Tournament tournament, DateTimeOffset createdAtUtc, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        return await InsertTournamentAsync(connection, null, tournament, createdAtUtc, ct).ConfigureAwait(false) == 1;
    }

    public async Task UpdateTournamentAsync(Tournament tournament, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        var rules = tournament.Rules;
        await using var command = Db.Command(
            connection,
            null,
            """
            UPDATE tournament SET
                name = $name,
                rules_preset_name = $preset,
                rules_is_demo_preset = $isDemoPreset,
                rules_period_count = $periods,
                rules_period_seconds = $periodSeconds,
                rules_break_seconds = $breakSeconds,
                rules_halftime_seconds = $halftimeSeconds,
                rules_timeouts_per_team = $timeouts,
                rules_shot_clock_seconds = $shot,
                rules_shot_clock_alt_seconds = $shotAlt,
                rules_shootout_enabled = $shootout,
                rules_overtime_period_count = $overtimePeriods,
                rules_overtime_period_seconds = $overtimeSeconds
            WHERE id = $id;
            """)
            .With("$name", tournament.Name)
            .With("$preset", rules.PresetName)
            .With("$isDemoPreset", rules.IsDemoPreset ? 1 : 0)
            .With("$periods", rules.PeriodCount)
            .With("$periodSeconds", rules.PeriodDurationSeconds)
            .With("$breakSeconds", rules.BreakDurationSeconds)
            .With("$halftimeSeconds", rules.HalftimeDurationSeconds)
            .With("$timeouts", rules.TimeoutsPerTeam)
            .With("$shot", rules.ShotClockSeconds)
            .With("$shotAlt", rules.ShotClockAlternateSeconds)
            .With("$shootout", rules.PenaltyShootoutEnabled ? 1 : 0)
            .With("$overtimePeriods", rules.OvertimePeriodCount)
            .With("$overtimeSeconds", rules.OvertimePeriodDurationSeconds)
            .With("$id", Db.Id(tournament.Id));
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("El evento a actualizar no existe.");
        }
    }

    public async Task<IReadOnlyList<Team>> ListTeamsAsync(CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await using var command = Db.Command(
            connection, null, "SELECT id, code, name, color_hex, cap_color FROM team ORDER BY name, code;");
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var result = new List<Team>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(ReadTeam(reader));
        }

        return result;
    }

    public async Task<Team?> GetTeamAsync(Guid teamId, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await using var command = Db.Command(
            connection, null, "SELECT id, code, name, color_hex, cap_color FROM team WHERE id = $id;")
            .With("$id", Db.Id(teamId));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadTeam(reader) : null;
    }

    public async Task<bool> AddTeamAsync(Team team, DateTimeOffset createdAtUtc, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        return await InsertTeamAsync(connection, null, team, createdAtUtc, ct).ConfigureAwait(false) == 1;
    }

    public async Task UpdateTeamAsync(Team team, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await using var command = Db.Command(
            connection,
            null,
            "UPDATE team SET code = $code, name = $name, color_hex = $color, cap_color = $cap WHERE id = $id;")
            .With("$code", team.Code)
            .With("$name", team.Name)
            .With("$color", team.ColorHex)
            .With("$cap", team.CapColor)
            .With("$id", Db.Id(team.Id));
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("El equipo a actualizar no existe.");
        }
    }

    public async Task<IReadOnlyList<TournamentRosterLine>> GetTournamentRosterAsync(
        Guid tournamentId, Guid teamId, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        return await ReadRosterAsync(connection, null, tournamentId, teamId, ct).ConfigureAwait(false);
    }

    public async Task<bool> AddRosterPlayerAsync(
        Player player, TournamentRosterEntry entry, DateTimeOffset createdAtUtc, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var exists = Db.Command(connection, transaction, "SELECT 1 FROM tournament_roster_entry WHERE id = $id;")
            .With("$id", Db.Id(entry.Id)))
        {
            if (await exists.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null)
            {
                return false;
            }
        }

        await InsertPlayerAsync(connection, transaction, player, createdAtUtc, ct).ConfigureAwait(false);
        await InsertRosterEntryAsync(connection, transaction, entry, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task UpdateRosterPlayerAsync(Guid entryId, string playerName, int cap, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var entry = Db.Command(
            connection, transaction, "UPDATE tournament_roster_entry SET cap_number = $cap WHERE id = $id;")
            .With("$cap", cap)
            .With("$id", Db.Id(entryId)))
        {
            if (await entry.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException("La inscripción a actualizar no existe.");
            }
        }

        await using (var player = Db.Command(
            connection,
            transaction,
            "UPDATE player SET full_name = $name WHERE id = (SELECT player_id FROM tournament_roster_entry WHERE id = $id);")
            .With("$name", playerName)
            .With("$id", Db.Id(entryId)))
        {
            await player.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveRosterEntryAsync(Guid entryId, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await using var command = Db.Command(connection, null, "DELETE FROM tournament_roster_entry WHERE id = $id;")
            .With("$id", Db.Id(entryId));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task ApplyRosterImportAsync(RosterImportChanges changes, DateTimeOffset atUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        foreach (var team in changes.NewTeams)
        {
            await using (var taken = Db.Command(connection, transaction, "SELECT 1 FROM team WHERE code = $code OR id = $id;")
                .With("$code", team.Code)
                .With("$id", Db.Id(team.Id)))
            {
                if (await taken.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null)
                {
                    throw RosterChanged();
                }
            }

            await InsertTeamAsync(connection, transaction, team, atUtc, ct).ConfigureAwait(false);
        }

        foreach (var team in changes.Teams)
        {
            // La nómina debe seguir exactamente como en la vista previa.
            var current = await ReadRosterAsync(connection, transaction, changes.TournamentId, team.TeamId, ct).ConfigureAwait(false);
            if (!current.OrderBy(c => c.EntryId).SequenceEqual(team.Expected.OrderBy(e => e.EntryId)))
            {
                throw RosterChanged();
            }

            foreach (var player in team.NewPlayers)
            {
                await InsertPlayerAsync(connection, transaction, player, atUtc, ct).ConfigureAwait(false);
            }

            foreach (var rename in team.Renames)
            {
                await using var command = Db.Command(connection, transaction, "UPDATE player SET full_name = $name WHERE id = $id;")
                    .With("$name", rename.NewName)
                    .With("$id", Db.Id(rename.PlayerId));
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // Se reescribe la nómina del equipo completa: así un intercambio de gorros no choca
            // con la restricción de gorro único a mitad de camino.
            await using (var delete = Db.Command(
                connection, transaction, "DELETE FROM tournament_roster_entry WHERE tournament_id = $tournamentId AND team_id = $teamId;")
                .With("$tournamentId", Db.Id(changes.TournamentId))
                .With("$teamId", Db.Id(team.TeamId)))
            {
                await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            foreach (var entry in team.Final)
            {
                await InsertRosterEntryAsync(connection, transaction, entry, ct).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> CountScheduledMatchesAsync(Guid tournamentId, IReadOnlyCollection<Guid> teamIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(teamIds);
        if (teamIds.Count == 0)
        {
            return 0;
        }

        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        var count = 0;
        await using var command = Db.Command(
            connection,
            null,
            "SELECT home_team_id, away_team_id FROM match WHERE tournament_id = $tournamentId AND status = 'Scheduled';")
            .With("$tournamentId", Db.Id(tournamentId));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (teamIds.Contains(Db.ReadId(reader, 0)) || teamIds.Contains(Db.ReadId(reader, 1)))
            {
                count++;
            }
        }

        return count;
    }

    private static DomainException RosterChanged() => new(
        "nomina.cambio",
        "La nómina o el catálogo cambiaron desde la vista previa. No se importó nada: vuelva a abrir el archivo para revisarlo.");

    private static async Task<IReadOnlyList<TournamentRosterLine>> ReadRosterAsync(
        SqliteConnection connection, SqliteTransaction? transaction, Guid tournamentId, Guid teamId, CancellationToken ct)
    {
        await using var command = Db.Command(
            connection,
            transaction,
            """
            SELECT r.id, p.id, p.full_name, r.cap_number
            FROM tournament_roster_entry r
            JOIN player p ON p.id = r.player_id
            WHERE r.tournament_id = $tournamentId AND r.team_id = $teamId
            ORDER BY r.cap_number;
            """)
            .With("$tournamentId", Db.Id(tournamentId))
            .With("$teamId", Db.Id(teamId));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var result = new List<TournamentRosterLine>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new TournamentRosterLine(Db.ReadId(reader, 0), Db.ReadId(reader, 1), reader.GetString(2), reader.GetInt32(3)));
        }

        return result;
    }

    private static Team ReadTeam(SqliteDataReader reader) =>
        new(Db.ReadId(reader, 0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4));

    public async Task<Tournament?> GetAsync(Guid tournamentId, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await using var command = Db.Command(
            connection,
            null,
            $"""
            SELECT name, is_demo, {RulesColumns}
            FROM tournament WHERE id = $id;
            """).With("$id", Db.Id(tournamentId));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new Tournament(tournamentId, reader.GetString(0), reader.GetInt32(1) == 1, ReadRules(reader, 2));
    }

    /// <summary>Columnas de reglas del evento, en el orden que espera <see cref="ReadRules"/>.</summary>
    private const string RulesColumns =
        "rules_preset_name, rules_is_demo_preset, rules_period_count, rules_period_seconds, rules_break_seconds, " +
        "rules_halftime_seconds, rules_timeouts_per_team, rules_shot_clock_seconds, rules_shot_clock_alt_seconds, rules_shootout_enabled, " +
        "rules_overtime_period_count, rules_overtime_period_seconds";

    private static RulesetSnapshot ReadRules(SqliteDataReader reader, int first) => new(
        reader.GetString(first),
        reader.GetInt32(first + 1) == 1,
        reader.GetInt32(first + 2),
        reader.GetInt32(first + 3),
        reader.GetInt32(first + 4),
        reader.GetInt32(first + 5),
        reader.GetInt32(first + 6),
        reader.GetInt32(first + 7),
        reader.GetInt32(first + 8),
        reader.GetInt32(first + 9) == 1,
        reader.GetInt32(first + 10),
        reader.GetInt32(first + 11));

    public async Task<IReadOnlyList<OperatorProfile>> ListAsync(CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await using var command = Db.Command(connection, null, "SELECT id, display_name FROM operator_profile ORDER BY display_name;");
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var result = new List<OperatorProfile>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new OperatorProfile(Db.ReadId(reader, 0), reader.GetString(1)));
        }

        return result;
    }

    public async Task<OperatorProfile?> FindByNameAsync(string displayName, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await using var command = Db.Command(
            connection,
            null,
            "SELECT id, display_name FROM operator_profile WHERE display_name = $name COLLATE NOCASE;")
            .With("$name", displayName.Trim());
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? new OperatorProfile(Db.ReadId(reader, 0), reader.GetString(1))
            : null;
    }

    public async Task AddAsync(OperatorProfile profile, DateTimeOffset createdAtUtc, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await InsertOperatorAsync(connection, null, profile, createdAtUtc, ct).ConfigureAwait(false);
    }

    internal static async Task InsertOperatorAsync(
        SqliteConnection connection, SqliteTransaction? transaction, OperatorProfile profile, DateTimeOffset createdAtUtc, CancellationToken ct)
    {
        await using var command = Db.Command(
            connection,
            transaction,
            "INSERT INTO operator_profile (id, display_name, created_at_utc) VALUES ($id, $name, $at);")
            .With("$id", Db.Id(profile.Id))
            .With("$name", profile.DisplayName)
            .With("$at", Db.Utc(createdAtUtc));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <returns>Filas insertadas: 0 si ya existía un evento con ese identificador.</returns>
    internal static async Task<int> InsertTournamentAsync(
        SqliteConnection connection, SqliteTransaction? transaction, Tournament tournament, DateTimeOffset createdAtUtc, CancellationToken ct)
    {
        var rules = tournament.Rules;
        await using var command = Db.Command(
            connection,
            transaction,
            """
            INSERT INTO tournament (
                id, name, is_demo, rules_preset_name, rules_is_demo_preset, rules_period_count, rules_period_seconds,
                rules_break_seconds, rules_halftime_seconds, rules_timeouts_per_team, rules_shot_clock_seconds,
                rules_shot_clock_alt_seconds, rules_shootout_enabled, rules_overtime_period_count,
                rules_overtime_period_seconds, created_at_utc)
            VALUES (
                $id, $name, $isDemo, $preset, $isDemoPreset, $periods, $periodSeconds,
                $breakSeconds, $halftimeSeconds, $timeouts, $shot, $shotAlt, $shootout, $overtimePeriods,
                $overtimeSeconds, $at)
            ON CONFLICT (id) DO NOTHING;
            """)
            .With("$id", Db.Id(tournament.Id))
            .With("$name", tournament.Name)
            .With("$isDemo", tournament.IsDemo ? 1 : 0)
            .With("$preset", rules.PresetName)
            .With("$isDemoPreset", rules.IsDemoPreset ? 1 : 0)
            .With("$periods", rules.PeriodCount)
            .With("$periodSeconds", rules.PeriodDurationSeconds)
            .With("$breakSeconds", rules.BreakDurationSeconds)
            .With("$halftimeSeconds", rules.HalftimeDurationSeconds)
            .With("$timeouts", rules.TimeoutsPerTeam)
            .With("$shot", rules.ShotClockSeconds)
            .With("$shotAlt", rules.ShotClockAlternateSeconds)
            .With("$shootout", rules.PenaltyShootoutEnabled ? 1 : 0)
            .With("$overtimePeriods", rules.OvertimePeriodCount)
            .With("$overtimeSeconds", rules.OvertimePeriodDurationSeconds)
            .With("$at", Db.Utc(createdAtUtc));
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <returns>Filas insertadas: 0 si ya existía un equipo con ese identificador.</returns>
    internal static async Task<int> InsertTeamAsync(
        SqliteConnection connection, SqliteTransaction? transaction, Team team, DateTimeOffset createdAtUtc, CancellationToken ct)
    {
        await using var command = Db.Command(
            connection,
            transaction,
            """
            INSERT INTO team (id, code, name, color_hex, cap_color, created_at_utc)
            VALUES ($id, $code, $name, $color, $cap, $at)
            ON CONFLICT (id) DO NOTHING;
            """)
            .With("$id", Db.Id(team.Id))
            .With("$code", team.Code)
            .With("$name", team.Name)
            .With("$color", team.ColorHex)
            .With("$cap", team.CapColor)
            .With("$at", Db.Utc(createdAtUtc));
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    internal static async Task InsertPlayerAsync(
        SqliteConnection connection, SqliteTransaction transaction, Player player, DateTimeOffset createdAtUtc, CancellationToken ct)
    {
        await using var command = Db.Command(
            connection,
            transaction,
            "INSERT INTO player (id, team_id, full_name, created_at_utc) VALUES ($id, $teamId, $name, $at);")
            .With("$id", Db.Id(player.Id))
            .With("$teamId", Db.Id(player.TeamId))
            .With("$name", player.FullName)
            .With("$at", Db.Utc(createdAtUtc));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    internal static async Task InsertRosterEntryAsync(
        SqliteConnection connection, SqliteTransaction transaction, TournamentRosterEntry entry, CancellationToken ct)
    {
        await using var command = Db.Command(
            connection,
            transaction,
            """
            INSERT INTO tournament_roster_entry (id, tournament_id, team_id, player_id, cap_number)
            VALUES ($id, $tournamentId, $teamId, $playerId, $cap);
            """)
            .With("$id", Db.Id(entry.Id))
            .With("$tournamentId", Db.Id(entry.TournamentId))
            .With("$teamId", Db.Id(entry.TeamId))
            .With("$playerId", Db.Id(entry.PlayerId))
            .With("$cap", entry.Cap.Value);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
