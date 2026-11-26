using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;
using Microsoft.Data.Sqlite;

namespace FDNDA.Waterpolo.Infrastructure.Tests;

public class IncidentMigrationTests
{
    [Fact]
    public async Task Migrar_de_uno_a_dos_respalda_antes_y_conserva_partidos_goles_y_revisiones()
    {
        await using var storage = await TempStorage.CreateAsync();
        var database = await CreateVersionOneAsync(storage);
        var runner = new MigrationRunner(database, new SqliteBackupService(database), storage.Time);

        var report = await runner.MigrateAsync(storage.Paths.BackupDirectory);

        Assert.Equal(1, report.PreviousVersion);
        Assert.Equal(MigrationRunner.LatestKnownVersion, report.CurrentVersion);
        Assert.NotNull(report.PreMigrationBackupPath);
        // 0003 (R7): bandera del equipo y auspiciadores, sin tocar los datos existentes.
        Assert.Equal(1L, await ScalarAsync(database, "SELECT COUNT(*) FROM pragma_table_info('team') WHERE name = 'flag_asset_id';"));
        Assert.Equal(0L, await ScalarAsync(database, "SELECT COUNT(*) FROM event_sponsor;"));
        Assert.True(File.Exists(report.PreMigrationBackupPath));
        Assert.Null(await SqliteBackupService.CheckIntegrityAsync(report.PreMigrationBackupPath));
        var backup = new SqliteDatabase(report.PreMigrationBackupPath);
        Assert.Equal(1L, await ScalarAsync(backup, "SELECT MAX(version) FROM schema_migrations;"));
        Assert.Equal(0L, await ScalarAsync(backup, "SELECT COUNT(*) FROM pragma_table_info('incident_revision') WHERE name = 'penalty_result';"));
        Assert.Equal(2L, await ScalarAsync(backup, "SELECT COUNT(*) FROM match;"));
        Assert.Equal(1L, await ScalarAsync(backup, "SELECT COUNT(*) FROM match_incident;"));
        Assert.Equal(2L, await ScalarAsync(backup, "SELECT COUNT(*) FROM incident_revision;"));

        var recovered = (await new MatchQueries(new SqliteMatchRepository(database)).GetSnapshotAsync(TestDesk.MatchId))!;
        Assert.Equal(0, recovered.Home.Score);
        Assert.Equal(1, recovered.Away.Score);
        var goal = Assert.Single(recovered.Incidents);
        Assert.Equal(2, goal.Revisions.Count);
        Assert.Equal(TeamSide.Home, goal.Revisions[0].Side);
        Assert.Equal(TeamSide.Away, goal.Revisions[1].Side);
        Assert.Equal("Equipo corregido en v1", goal.Revisions[1].Reason);
        Assert.Null(goal.PenaltyResult);
        Assert.Null(goal.ExclusionKind);
        Assert.False((await runner.MigrateAsync(storage.Paths.BackupDirectory)).Applied);

        await using var connection = await database.OpenAsync();
        await using var query = connection.CreateCommand();
        query.CommandText = "DELETE FROM incident_revision;";
        await Assert.ThrowsAsync<SqliteException>(() => query.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Si_no_se_puede_crear_el_respaldo_no_se_aplica_la_migracion()
    {
        await using var storage = await TempStorage.CreateAsync();
        var database = await CreateVersionOneAsync(storage);
        var blockedDirectory = Path.Combine(storage.Root, "no-es-carpeta");
        await File.WriteAllTextAsync(blockedDirectory, "Impide crear el directorio del respaldo");
        var runner = new MigrationRunner(database, new SqliteBackupService(database), storage.Time);
        await Assert.ThrowsAnyAsync<IOException>(() => runner.MigrateAsync(blockedDirectory));
        Assert.Equal(1, await runner.GetCurrentVersionAsync());
        Assert.Equal(0L, await ScalarAsync(database, "SELECT COUNT(*) FROM pragma_table_info('incident_revision') WHERE name = 'penalty_result';"));
        Assert.Equal(2L, await ScalarAsync(database, "SELECT COUNT(*) FROM incident_revision;"));
    }

    internal static async Task<SqliteDatabase> CreateVersionOneAsync(TempStorage storage)
    {
        var database = new SqliteDatabase(Path.Combine(storage.Root, "version-uno.db"));
        var assembly = typeof(MigrationRunner).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("0001_inicial.sql", StringComparison.Ordinal));
        await using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        var schema = await reader.ReadToEndAsync();
        await using (var connection = await database.OpenAsync())
        await using (var command = connection.CreateCommand())
        {
            // Fixture creado con la migración publicada, sin simular un descenso de versión.
            command.CommandText = schema + """

                CREATE TABLE schema_migrations (version INTEGER PRIMARY KEY, name TEXT NOT NULL, applied_at_utc TEXT NOT NULL);
                INSERT INTO schema_migrations VALUES (1, 'inicial', '2026-11-11T15:00:00Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        // Los datos demo se generan con el código actual en una base aparte (esquema vigente) y se
        // copian solo las columnas que existían en v1: el sembrador escribe columnas de migraciones
        // posteriores (reglas de prórroga, banderas…) que una base v1 no tiene.
        var seedPath = Path.Combine(storage.Root, "semilla-v1.db");
        var seed = new SqliteDatabase(seedPath);
        await new MigrationRunner(seed, new SqliteBackupService(seed), storage.Time).MigrateAsync(storage.Paths.BackupDirectory);
        await new DemoDataSeeder(seed, new SqliteMatchRepository(seed), storage.Time).SeedAsync();
        await using (var connection = await database.OpenAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                ATTACH DATABASE $seed AS semilla;
                BEGIN;
                INSERT INTO asset SELECT id, kind, file_name, media_type, byte_length, sha256, content, created_at_utc FROM semilla.asset;
                INSERT INTO tournament SELECT id, name, is_demo, rules_preset_name, rules_is_demo_preset, rules_period_count,
                    rules_period_seconds, rules_break_seconds, rules_halftime_seconds, rules_timeouts_per_team,
                    rules_shot_clock_seconds, rules_shot_clock_alt_seconds, rules_shootout_enabled, created_at_utc FROM semilla.tournament;
                INSERT INTO team SELECT id, code, name, color_hex, cap_color, crest_asset_id, created_at_utc FROM semilla.team;
                INSERT INTO player SELECT id, team_id, full_name, created_at_utc FROM semilla.player;
                INSERT INTO tournament_roster_entry SELECT id, tournament_id, team_id, player_id, cap_number FROM semilla.tournament_roster_entry;
                INSERT INTO match SELECT id, tournament_id, tournament_name, label, scheduled_at_utc, status, stage, current_period,
                    home_team_id, home_code, home_name, home_color_hex, home_cap_color, away_team_id, away_code, away_name,
                    away_color_hex, away_cap_color, version, created_at_utc, updated_at_utc FROM semilla.match;
                INSERT INTO match_roster_entry SELECT id, match_id, side, player_id, player_name, cap_number FROM semilla.match_roster_entry;
                INSERT INTO processed_command SELECT command_id, match_id, command_type, applied_at_utc, resulting_version FROM semilla.processed_command;
                COMMIT;
                DETACH DATABASE semilla;
                """;
            command.Parameters.AddWithValue("$seed", seedPath);
            await command.ExecuteNonQueryAsync();
        }

        await using (var connection = await database.OpenAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                BEGIN;
                INSERT INTO operator_profile VALUES ($operator, 'Operador ficticio v1', $at);
                UPDATE match SET status = 'InProgress', stage = 'Period', current_period = 1 WHERE id = $match;
                INSERT INTO match_ruleset_snapshot
                    SELECT $match, rules_preset_name, rules_is_demo_preset, rules_period_count, rules_period_seconds,
                        rules_break_seconds, rules_halftime_seconds, rules_timeouts_per_team, rules_shot_clock_seconds,
                        rules_shot_clock_alt_seconds, rules_shootout_enabled, $at FROM tournament;
                INSERT INTO match_incident VALUES ($incident, $match, 1, 'Goal', 1, 'Simulated', 'Live', 400000, 20000, $at);
                INSERT INTO incident_revision
                    SELECT $incident, 1, 'Original', 'Home', id, NULL, $operator, 'Operador ficticio v1', $at
                    FROM match_roster_entry WHERE match_id = $match AND side = 'Home' AND cap_number = 1;
                INSERT INTO incident_revision
                    SELECT $incident, 2, 'Correction', 'Away', id, 'Equipo corregido en v1', $operator, 'Operador ficticio v1', $at
                    FROM match_roster_entry WHERE match_id = $match AND side = 'Away' AND cap_number = 2;
                COMMIT;
                """;
            command.Parameters.AddWithValue("$operator", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$incident", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$match", TestDesk.MatchId.ToString("D"));
            command.Parameters.AddWithValue("$at", storage.Time.GetUtcNow().ToString("O"));
            await command.ExecuteNonQueryAsync();
        }

        return database;
    }

    private static async Task<long> ScalarAsync(SqliteDatabase database, string sql)
    {
        await using var connection = await database.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
