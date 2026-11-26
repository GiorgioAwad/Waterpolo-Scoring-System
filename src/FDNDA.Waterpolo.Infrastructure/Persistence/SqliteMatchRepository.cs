using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;
using FDNDA.Waterpolo.Domain.Matches;
using Microsoft.Data.Sqlite;

namespace FDNDA.Waterpolo.Infrastructure.Persistence;

/// <summary>
/// Repositorio concreto del agregado <see cref="Match"/> sobre SQLite. La escritura es
/// una sola transacción: versión esperada, cambios pendientes, auditoría e identificador
/// del comando. Si algo falla se revierte todo y no se reporta éxito.
/// </summary>
public sealed class SqliteMatchRepository(SqliteDatabase database) : IMatchRepository
{
    public async Task<Match?> GetAsync(Guid matchId, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);

        // Lectura consistente de todas las tablas del agregado (transacción diferida: no toma bloqueo de escritura).
        await using var transaction = connection.BeginTransaction(deferred: true);

        MatchHeader? header = null;
        await using (var command = Db.Command(
            connection,
            transaction,
            """
            SELECT tournament_id, tournament_name, label, scheduled_at_utc, status, stage, current_period,
                   home_team_id, home_code, home_name, home_color_hex, home_cap_color,
                   away_team_id, away_code, away_name, away_color_hex, away_cap_color, version
            FROM match WHERE id = $id;
            """).With("$id", Db.Id(matchId)))
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                header = new MatchHeader(
                    Db.ReadId(reader, 0),
                    reader.GetString(1),
                    reader.GetString(2),
                    Db.ReadNullableUtc(reader, 3),
                    Db.ReadEnum<MatchStatus>(reader, 4),
                    Db.ReadEnum<MatchStage>(reader, 5),
                    reader.GetInt32(6),
                    new MatchTeam(Db.ReadId(reader, 7), reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11)),
                    new MatchTeam(Db.ReadId(reader, 12), reader.GetString(13), reader.GetString(14), reader.GetString(15), reader.GetString(16)),
                    reader.GetInt32(17));
            }
        }

        if (header is null)
        {
            return null;
        }

        var ruleset = await ReadRulesetAsync(connection, transaction, matchId, ct).ConfigureAwait(false);
        var roster = await ReadRosterAsync(connection, transaction, matchId, ct).ConfigureAwait(false);
        var revisions = await ReadRevisionsAsync(connection, transaction, matchId, ct).ConfigureAwait(false);
        var incidents = await ReadIncidentsAsync(connection, transaction, matchId, revisions, ct).ConfigureAwait(false);
        var statusChanges = await ReadStatusChangesAsync(connection, transaction, matchId, ct).ConfigureAwait(false);

        return Match.Rehydrate(new MatchState(
            matchId,
            header.TournamentId,
            header.TournamentName,
            header.Label,
            header.ScheduledAtUtc,
            header.Home,
            header.Away,
            header.Status,
            header.Stage,
            header.CurrentPeriod,
            ruleset,
            header.Version,
            roster,
            incidents,
            statusChanges));
    }

    public async Task<IReadOnlyList<MatchSummary>> ListAsync(CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await using var command = Db.Command(
            connection,
            null,
            """
            SELECT id, tournament_name, label, home_name, away_name, status, scheduled_at_utc, tournament_id
            FROM match
            ORDER BY scheduled_at_utc IS NULL, scheduled_at_utc, created_at_utc, id;
            """);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var result = new List<MatchSummary>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new MatchSummary(
                Db.ReadId(reader, 0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                Db.ReadEnum<MatchStatus>(reader, 5),
                Db.ReadNullableUtc(reader, 6),
                Db.ReadId(reader, 7)));
        }

        return result;
    }

    public async Task<Guid?> FindInProgressMatchIdAsync(CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await using var command = Db.Command(connection, null, "SELECT id FROM match WHERE status = 'InProgress' LIMIT 1;");
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is string id ? Guid.Parse(id) : null;
    }

    public async Task<bool> IsCommandProcessedAsync(Guid commandId, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await using var command = Db.Command(connection, null, "SELECT 1 FROM processed_command WHERE command_id = $id;")
            .With("$id", Db.Id(commandId));
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    public async Task SaveAsync(Match match, CommandStamp stamp, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(stamp);

        var pending = match.PendingChanges;
        var newVersion = match.Version + 1;
        var at = Db.Utc(stamp.AtUtc);

        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        if (pending.IsNew)
        {
            await InsertMatchAsync(connection, transaction, match, newVersion, at, ct).ConfigureAwait(false);
        }
        else
        {
            await using var update = Db.Command(
                connection,
                transaction,
                """
                UPDATE match
                SET status = $status, stage = $stage, current_period = $period, version = $newVersion, updated_at_utc = $at,
                    label = $label, scheduled_at_utc = $scheduledAt,
                    home_color_hex = $homeColor, home_cap_color = $homeCap, away_color_hex = $awayColor, away_cap_color = $awayCap
                WHERE id = $id AND version = $expectedVersion;
                """)
                .With("$label", match.Label)
                .With("$scheduledAt", match.ScheduledAtUtc is { } scheduled ? Db.Utc(scheduled) : null)
                .With("$homeColor", match.Home.ColorHex)
                .With("$homeCap", match.Home.CapColor)
                .With("$awayColor", match.Away.ColorHex)
                .With("$awayCap", match.Away.CapColor)
                .With("$status", match.Status.ToString())
                .With("$stage", match.Stage.ToString())
                .With("$period", match.CurrentPeriod)
                .With("$newVersion", newVersion)
                .With("$at", at)
                .With("$id", Db.Id(match.Id))
                .With("$expectedVersion", match.Version);

            if (await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            {
                throw new ConcurrencyException("El partido fue modificado por otra operación.");
            }

            // Solo antes de iniciar (lo garantizan el dominio y un disparador): no hay incidencias que la nombren.
            if (pending.RosterReplaced)
            {
                await using (var delete = Db.Command(connection, transaction, "DELETE FROM match_roster_entry WHERE match_id = $id;")
                    .With("$id", Db.Id(match.Id)))
                {
                    await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await InsertRosterAsync(connection, transaction, match, ct).ConfigureAwait(false);
            }
        }

        // Las reglas se copian una sola vez, cuando el partido inicia.
        if (match.Ruleset is { } rules && pending.NewStatusChanges.Any(c => c.Kind == MatchStatusChangeKind.Started))
        {
            await InsertRulesetAsync(connection, transaction, match.Id, rules, at, ct).ConfigureAwait(false);
        }

        foreach (var change in pending.NewStatusChanges)
        {
            await InsertStatusChangeAsync(connection, transaction, match.Id, change, ct).ConfigureAwait(false);
        }

        foreach (var incident in pending.NewIncidents)
        {
            await InsertIncidentAsync(connection, transaction, match.Id, incident, ct).ConfigureAwait(false);
            foreach (var revision in incident.Revisions)
            {
                await InsertRevisionAsync(connection, transaction, incident.Id, revision, ct).ConfigureAwait(false);
            }
        }

        var newIncidentIds = pending.NewIncidents.Select(i => i.Id).ToHashSet();
        foreach (var pendingRevision in pending.NewRevisions.Where(r => !newIncidentIds.Contains(r.IncidentId)))
        {
            await InsertRevisionAsync(connection, transaction, pendingRevision.IncidentId, pendingRevision.Revision, ct).ConfigureAwait(false);
        }

        await using (var command = Db.Command(
            connection,
            transaction,
            """
            INSERT INTO processed_command (command_id, match_id, command_type, applied_at_utc, resulting_version)
            VALUES ($commandId, $matchId, $type, $at, $version);
            """)
            .With("$commandId", Db.Id(stamp.CommandId))
            .With("$matchId", Db.Id(match.Id))
            .With("$type", stamp.CommandType)
            .With("$at", at)
            .With("$version", newVersion))
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        match.MarkPersisted(newVersion);
    }

    private static async Task InsertMatchAsync(
        SqliteConnection connection, SqliteTransaction transaction, Match match, int version, string at, CancellationToken ct)
    {
        await using (var insert = Db.Command(
            connection,
            transaction,
            """
            INSERT INTO match (
                id, tournament_id, tournament_name, label, scheduled_at_utc, status, stage, current_period,
                home_team_id, home_code, home_name, home_color_hex, home_cap_color,
                away_team_id, away_code, away_name, away_color_hex, away_cap_color,
                version, created_at_utc, updated_at_utc)
            VALUES (
                $id, $tournamentId, $tournamentName, $label, $scheduledAt, $status, $stage, $period,
                $homeId, $homeCode, $homeName, $homeColor, $homeCap,
                $awayId, $awayCode, $awayName, $awayColor, $awayCap,
                $version, $at, $at);
            """)
            .With("$id", Db.Id(match.Id))
            .With("$tournamentId", Db.Id(match.TournamentId))
            .With("$tournamentName", match.TournamentName)
            .With("$label", match.Label)
            .With("$scheduledAt", match.ScheduledAtUtc is { } scheduled ? Db.Utc(scheduled) : null)
            .With("$status", match.Status.ToString())
            .With("$stage", match.Stage.ToString())
            .With("$period", match.CurrentPeriod)
            .With("$homeId", Db.Id(match.Home.TeamId))
            .With("$homeCode", match.Home.Code)
            .With("$homeName", match.Home.Name)
            .With("$homeColor", match.Home.ColorHex)
            .With("$homeCap", match.Home.CapColor)
            .With("$awayId", Db.Id(match.Away.TeamId))
            .With("$awayCode", match.Away.Code)
            .With("$awayName", match.Away.Name)
            .With("$awayColor", match.Away.ColorHex)
            .With("$awayCap", match.Away.CapColor)
            .With("$version", version)
            .With("$at", at))
        {
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await InsertRosterAsync(connection, transaction, match, ct).ConfigureAwait(false);
    }

    private static async Task InsertRosterAsync(
        SqliteConnection connection, SqliteTransaction transaction, Match match, CancellationToken ct)
    {
        foreach (var entry in match.Roster)
        {
            await using var insertEntry = Db.Command(
                connection,
                transaction,
                """
                INSERT INTO match_roster_entry (id, match_id, side, player_id, player_name, cap_number)
                VALUES ($id, $matchId, $side, $playerId, $playerName, $cap);
                """)
                .With("$id", Db.Id(entry.Id))
                .With("$matchId", Db.Id(match.Id))
                .With("$side", entry.Side.ToString())
                .With("$playerId", Db.Id(entry.PlayerId))
                .With("$playerName", entry.PlayerName)
                .With("$cap", entry.Cap.Value);
            await insertEntry.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task InsertRulesetAsync(
        SqliteConnection connection, SqliteTransaction transaction, Guid matchId, RulesetSnapshot rules, string at, CancellationToken ct)
    {
        await using var command = Db.Command(
            connection,
            transaction,
            """
            INSERT INTO match_ruleset_snapshot (
                match_id, preset_name, is_demo_preset, period_count, period_seconds, break_seconds, halftime_seconds,
                timeouts_per_team, shot_clock_seconds, shot_clock_alt_seconds, shootout_enabled,
                overtime_period_count, overtime_period_seconds, copied_at_utc)
            VALUES (
                $matchId, $preset, $isDemo, $periods, $periodSeconds, $breakSeconds, $halftimeSeconds,
                $timeouts, $shot, $shotAlt, $shootout, $overtimePeriods, $overtimeSeconds, $at);
            """)
            .With("$matchId", Db.Id(matchId))
            .With("$preset", rules.PresetName)
            .With("$isDemo", rules.IsDemoPreset ? 1 : 0)
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
            .With("$at", at);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertStatusChangeAsync(
        SqliteConnection connection, SqliteTransaction transaction, Guid matchId, MatchStatusChange change, CancellationToken ct)
    {
        await using var command = Db.Command(
            connection,
            transaction,
            """
            INSERT INTO match_status_change (
                match_id, sequence, kind, from_status, to_status, from_stage, to_stage, period,
                reason, operator_id, operator_name, at_utc)
            VALUES (
                $matchId, $sequence, $kind, $fromStatus, $toStatus, $fromStage, $toStage, $period,
                $reason, $operatorId, $operatorName, $at);
            """)
            .With("$matchId", Db.Id(matchId))
            .With("$sequence", change.Sequence)
            .With("$kind", change.Kind.ToString())
            .With("$fromStatus", change.FromStatus.ToString())
            .With("$toStatus", change.ToStatus.ToString())
            .With("$fromStage", change.FromStage.ToString())
            .With("$toStage", change.ToStage.ToString())
            .With("$period", change.Period)
            .With("$reason", change.Reason)
            .With("$operatorId", Db.Id(change.Operator.Id))
            .With("$operatorName", change.Operator.Name)
            .With("$at", Db.Utc(change.AtUtc));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertIncidentAsync(
        SqliteConnection connection, SqliteTransaction transaction, Guid matchId, MatchIncident incident, CancellationToken ct)
    {
        await using var command = Db.Command(
            connection,
            transaction,
            """
            INSERT INTO match_incident (
                id, match_id, sequence, type, period, clock_source, clock_quality, game_clock_ms, shot_clock_ms, registered_at_utc)
            VALUES (
                $id, $matchId, $sequence, $type, $period, $clockSource, $clockQuality, $gameMs, $shotMs, $at);
            """)
            .With("$id", Db.Id(incident.Id))
            .With("$matchId", Db.Id(matchId))
            .With("$sequence", incident.Sequence)
            .With("$type", incident.Type.ToString())
            .With("$period", incident.Period.Value)
            .With("$clockSource", incident.Clock.Source.ToString())
            .With("$clockQuality", incident.Clock.Quality.ToString())
            .With("$gameMs", incident.Clock.GameClockRemainingMs)
            .With("$shotMs", incident.Clock.ShotClockRemainingMs)
            .With("$at", Db.Utc(incident.RegisteredAtUtc));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertRevisionAsync(
        SqliteConnection connection, SqliteTransaction transaction, Guid incidentId, IncidentRevision revision, CancellationToken ct)
    {
        await using var command = Db.Command(
            connection,
            transaction,
            """
            INSERT INTO incident_revision (
                incident_id, revision_number, kind, side, roster_entry_id, reason, operator_id, operator_name, at_utc,
                penalty_result, exclusion_kind)
            VALUES (
                $incidentId, $number, $kind, $side, $rosterEntryId, $reason, $operatorId, $operatorName, $at,
                $penaltyResult, $exclusionKind);
            """)
            .With("$incidentId", Db.Id(incidentId))
            .With("$penaltyResult", revision.PenaltyResult?.ToString())
            .With("$exclusionKind", revision.ExclusionKind?.ToString())
            .With("$number", revision.Number)
            .With("$kind", revision.Kind.ToString())
            .With("$side", revision.Side.ToString())
            .With("$rosterEntryId", revision.RosterEntryId is { } rosterEntryId ? Db.Id(rosterEntryId) : null)
            .With("$reason", revision.Reason)
            .With("$operatorId", Db.Id(revision.Operator.Id))
            .With("$operatorName", revision.Operator.Name)
            .With("$at", Db.Utc(revision.AtUtc));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<RulesetSnapshot?> ReadRulesetAsync(
        SqliteConnection connection, SqliteTransaction transaction, Guid matchId, CancellationToken ct)
    {
        await using var command = Db.Command(
            connection,
            transaction,
            """
            SELECT preset_name, is_demo_preset, period_count, period_seconds, break_seconds, halftime_seconds,
                   timeouts_per_team, shot_clock_seconds, shot_clock_alt_seconds, shootout_enabled,
                   overtime_period_count, overtime_period_seconds
            FROM match_ruleset_snapshot WHERE match_id = $id;
            """).With("$id", Db.Id(matchId));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new RulesetSnapshot(
            reader.GetString(0),
            reader.GetInt32(1) == 1,
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9) == 1,
            reader.GetInt32(10),
            reader.GetInt32(11));
    }

    private static async Task<List<MatchRosterEntry>> ReadRosterAsync(
        SqliteConnection connection, SqliteTransaction transaction, Guid matchId, CancellationToken ct)
    {
        await using var command = Db.Command(
            connection,
            transaction,
            "SELECT id, side, player_id, player_name, cap_number FROM match_roster_entry WHERE match_id = $id;")
            .With("$id", Db.Id(matchId));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var roster = new List<MatchRosterEntry>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            roster.Add(new MatchRosterEntry(
                Db.ReadId(reader, 0),
                Db.ReadEnum<TeamSide>(reader, 1),
                Db.ReadId(reader, 2),
                reader.GetString(3),
                new CapNumber(reader.GetInt32(4))));
        }

        return roster;
    }

    private static async Task<Dictionary<Guid, List<IncidentRevision>>> ReadRevisionsAsync(
        SqliteConnection connection, SqliteTransaction transaction, Guid matchId, CancellationToken ct)
    {
        await using var command = Db.Command(
            connection,
            transaction,
            """
            SELECT r.incident_id, r.revision_number, r.kind, r.side, r.roster_entry_id, r.reason,
                   r.operator_id, r.operator_name, r.at_utc, r.penalty_result, r.exclusion_kind
            FROM incident_revision r
            JOIN match_incident i ON i.id = r.incident_id
            WHERE i.match_id = $id
            ORDER BY r.incident_id, r.revision_number;
            """).With("$id", Db.Id(matchId));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var revisions = new Dictionary<Guid, List<IncidentRevision>>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var incidentId = Db.ReadId(reader, 0);
            if (!revisions.TryGetValue(incidentId, out var list))
            {
                list = [];
                revisions[incidentId] = list;
            }

            list.Add(new IncidentRevision(
                reader.GetInt32(1),
                Db.ReadEnum<RevisionKind>(reader, 2),
                Db.ReadEnum<TeamSide>(reader, 3),
                Db.ReadNullableId(reader, 4),
                Db.ReadNullableString(reader, 5),
                new OperatorRef(Db.ReadId(reader, 6), reader.GetString(7)),
                Db.ReadUtc(reader, 8),
                reader.IsDBNull(9) ? null : Db.ReadEnum<PenaltyResult>(reader, 9),
                reader.IsDBNull(10) ? null : Db.ReadEnum<ExclusionKind>(reader, 10)));
        }

        return revisions;
    }

    private static async Task<List<MatchIncident>> ReadIncidentsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid matchId,
        Dictionary<Guid, List<IncidentRevision>> revisions,
        CancellationToken ct)
    {
        await using var command = Db.Command(
            connection,
            transaction,
            """
            SELECT id, sequence, type, period, clock_source, clock_quality, game_clock_ms, shot_clock_ms, registered_at_utc
            FROM match_incident WHERE match_id = $id ORDER BY sequence;
            """).With("$id", Db.Id(matchId));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var incidents = new List<MatchIncident>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var id = Db.ReadId(reader, 0);
            incidents.Add(Match.RehydrateIncident(
                id,
                reader.GetInt32(1),
                Db.ReadEnum<IncidentType>(reader, 2),
                new PeriodNumber(reader.GetInt32(3)),
                new ClockContext(
                    Db.ReadEnum<ClockSource>(reader, 4),
                    Db.ReadEnum<ClockQuality>(reader, 5),
                    Db.ReadNullableInt(reader, 6),
                    Db.ReadNullableInt(reader, 7)),
                Db.ReadUtc(reader, 8),
                revisions.TryGetValue(id, out var list) ? list : []));
        }

        return incidents;
    }

    private static async Task<List<MatchStatusChange>> ReadStatusChangesAsync(
        SqliteConnection connection, SqliteTransaction transaction, Guid matchId, CancellationToken ct)
    {
        await using var command = Db.Command(
            connection,
            transaction,
            """
            SELECT sequence, kind, from_status, to_status, from_stage, to_stage, period, reason,
                   operator_id, operator_name, at_utc
            FROM match_status_change WHERE match_id = $id ORDER BY sequence;
            """).With("$id", Db.Id(matchId));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var changes = new List<MatchStatusChange>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            changes.Add(new MatchStatusChange(
                reader.GetInt32(0),
                Db.ReadEnum<MatchStatusChangeKind>(reader, 1),
                Db.ReadEnum<MatchStatus>(reader, 2),
                Db.ReadEnum<MatchStatus>(reader, 3),
                Db.ReadEnum<MatchStage>(reader, 4),
                Db.ReadEnum<MatchStage>(reader, 5),
                reader.GetInt32(6),
                Db.ReadNullableString(reader, 7),
                new OperatorRef(Db.ReadId(reader, 8), reader.GetString(9)),
                Db.ReadUtc(reader, 10)));
        }

        return changes;
    }

    private sealed record MatchHeader(
        Guid TournamentId,
        string TournamentName,
        string Label,
        DateTimeOffset? ScheduledAtUtc,
        MatchStatus Status,
        MatchStage Stage,
        int CurrentPeriod,
        MatchTeam Home,
        MatchTeam Away,
        int Version);
}
