using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Identity;
using FDNDA.Waterpolo.Domain.Competitions;
using Microsoft.Data.Sqlite;

namespace FDNDA.Waterpolo.Infrastructure.Persistence;

/// <summary>
/// Repositorio concreto de la identidad visual: imágenes en <c>asset</c>, escudo y bandera en
/// <c>team</c>, plantilla en <c>event_branding</c> y auspiciadores en <c>event_sponsor</c>.
/// Cada escritura borra, en su transacción, las imágenes que ya nadie usa.
/// </summary>
public sealed class SqliteIdentityRepository(SqliteDatabase database) : IIdentityRepository
{
    public async Task<TeamImages> GetTeamImagesAsync(Guid teamId, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        return await ReadTeamImagesAsync(connection, teamId, ct).ConfigureAwait(false);
    }

    public async Task SetTeamImageAsync(Guid teamId, AssetKind kind, ImageAsset? image, DateTimeOffset atUtc, CancellationToken ct = default)
    {
        // El nombre de la columna sale de esta lista fija, nunca de un texto del operador.
        var column = kind switch
        {
            AssetKind.Crest => "crest_asset_id",
            AssetKind.Flag => "flag_asset_id",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Solo escudo o bandera."),
        };

        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (image is not null)
        {
            await InsertAssetAsync(connection, transaction, image, atUtc, ct).ConfigureAwait(false);
        }

        await using (var update = Db.Command(connection, transaction, $"UPDATE team SET {column} = $asset WHERE id = $id;")
            .With("$asset", image is null ? null : Db.Id(image.Id))
            .With("$id", Db.Id(teamId)))
        {
            if (await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException("El equipo no existe.");
            }
        }

        await DeleteUnusedAssetsAsync(connection, transaction, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<EventBranding?> GetEventBrandingAsync(Guid tournamentId, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        return await ReadBrandingAsync(connection, tournamentId, ct).ConfigureAwait(false);
    }

    public async Task SaveEventBrandingAsync(EventBranding branding, DateTimeOffset atUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(branding);
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await WriteBrandingAsync(connection, transaction, branding, atUtc, ct).ConfigureAwait(false);
        await DeleteUnusedAssetsAsync(connection, transaction, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveEventBrandingAsync(Guid tournamentId, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var sql in new[]
        {
            "DELETE FROM event_sponsor WHERE tournament_id = $id;",
            "DELETE FROM event_branding WHERE tournament_id = $id;",
        })
        {
            await using var command = Db.Command(connection, transaction, sql).With("$id", Db.Id(tournamentId));
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await DeleteUnusedAssetsAsync(connection, transaction, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<ScoreboardIdentity?> GetMatchIdentityAsync(Guid matchId, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        Guid tournamentId, homeId, awayId;
        await using (var command = Db.Command(
            connection, null, "SELECT tournament_id, home_team_id, away_team_id FROM match WHERE id = $id;")
            .With("$id", Db.Id(matchId)))
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return null;
            }

            (tournamentId, homeId, awayId) = (Db.ReadId(reader, 0), Db.ReadId(reader, 1), Db.ReadId(reader, 2));
        }

        var branding = await ReadBrandingAsync(connection, tournamentId, ct).ConfigureAwait(false);
        var home = await ReadTeamImagesAsync(connection, homeId, ct).ConfigureAwait(false);
        var away = await ReadTeamImagesAsync(connection, awayId, ct).ConfigureAwait(false);
        return new ScoreboardIdentity(
            matchId,
            tournamentId,
            branding?.DisplayTitle,
            branding?.Subtitle,
            branding?.PrimaryColorHex,
            branding?.AccentColorHex,
            ToData(branding?.Logo),
            ToData(branding?.OrganizerLogo),
            branding is null ? [] : [.. branding.Sponsors.Select(s => new SponsorIdentity(s.Name, ToData(s.Image)!))],
            new TeamIdentity(homeId, ToData(home.Crest), ToData(home.Flag)),
            new TeamIdentity(awayId, ToData(away.Crest), ToData(away.Flag)));
    }

    /// <summary>Inserta la imagen si no existía (idempotente por identificador).</summary>
    internal static async Task InsertAssetAsync(
        SqliteConnection connection, SqliteTransaction transaction, ImageAsset image, DateTimeOffset atUtc, CancellationToken ct)
    {
        await using var command = Db.Command(
            connection,
            transaction,
            """
            INSERT INTO asset (id, kind, file_name, media_type, byte_length, sha256, content, created_at_utc)
            VALUES ($id, $kind, $fileName, $mediaType, $length, $sha, $content, $at)
            ON CONFLICT (id) DO NOTHING;
            """)
            .With("$id", Db.Id(image.Id))
            .With("$kind", KindName(image.Kind))
            .With("$fileName", image.FileName)
            .With("$mediaType", image.MediaType)
            .With("$length", image.ByteLength)
            .With("$sha", image.Sha256)
            .With("$content", image.Content)
            .With("$at", Db.Utc(atUtc));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    internal static async Task WriteBrandingAsync(
        SqliteConnection connection, SqliteTransaction transaction, EventBranding branding, DateTimeOffset atUtc, CancellationToken ct)
    {
        foreach (var image in branding.Images)
        {
            await InsertAssetAsync(connection, transaction, image, atUtc, ct).ConfigureAwait(false);
        }

        await using (var upsert = Db.Command(
            connection,
            transaction,
            """
            INSERT INTO event_branding (
                tournament_id, display_title, subtitle, primary_color_hex, accent_color_hex, logo_asset_id, organizer_logo_asset_id)
            VALUES ($id, $title, $subtitle, $primary, $accent, $logo, $organizer)
            ON CONFLICT (tournament_id) DO UPDATE SET
                display_title = excluded.display_title,
                subtitle = excluded.subtitle,
                primary_color_hex = excluded.primary_color_hex,
                accent_color_hex = excluded.accent_color_hex,
                logo_asset_id = excluded.logo_asset_id,
                organizer_logo_asset_id = excluded.organizer_logo_asset_id;
            """)
            .With("$id", Db.Id(branding.TournamentId))
            .With("$title", branding.DisplayTitle)
            .With("$subtitle", branding.Subtitle)
            .With("$primary", branding.PrimaryColorHex)
            .With("$accent", branding.AccentColorHex)
            .With("$logo", branding.Logo is null ? null : Db.Id(branding.Logo.Id))
            .With("$organizer", branding.OrganizerLogo is null ? null : Db.Id(branding.OrganizerLogo.Id)))
        {
            await upsert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var clear = Db.Command(connection, transaction, "DELETE FROM event_sponsor WHERE tournament_id = $id;")
            .With("$id", Db.Id(branding.TournamentId)))
        {
            await clear.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        for (var index = 0; index < branding.Sponsors.Count; index++)
        {
            var sponsor = branding.Sponsors[index];
            await using var insert = Db.Command(
                connection,
                transaction,
                "INSERT INTO event_sponsor (tournament_id, position, name, asset_id) VALUES ($id, $position, $name, $asset);")
                .With("$id", Db.Id(branding.TournamentId))
                .With("$position", index + 1)
                .With("$name", sponsor.Name)
                .With("$asset", Db.Id(sponsor.Image.Id));
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    internal static async Task SetTeamCrestAsync(
        SqliteConnection connection, SqliteTransaction transaction, Guid teamId, ImageAsset crest, DateTimeOffset atUtc, CancellationToken ct)
    {
        await InsertAssetAsync(connection, transaction, crest, atUtc, ct).ConfigureAwait(false);
        await using var update = Db.Command(connection, transaction, "UPDATE team SET crest_asset_id = $asset WHERE id = $id;")
            .With("$asset", Db.Id(crest.Id))
            .With("$id", Db.Id(teamId));
        await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task DeleteUnusedAssetsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var command = Db.Command(
            connection,
            transaction,
            """
            DELETE FROM asset
            WHERE id NOT IN (SELECT crest_asset_id FROM team WHERE crest_asset_id IS NOT NULL)
              AND id NOT IN (SELECT flag_asset_id FROM team WHERE flag_asset_id IS NOT NULL)
              AND id NOT IN (SELECT logo_asset_id FROM event_branding WHERE logo_asset_id IS NOT NULL)
              AND id NOT IN (SELECT organizer_logo_asset_id FROM event_branding WHERE organizer_logo_asset_id IS NOT NULL)
              AND id NOT IN (SELECT asset_id FROM event_sponsor);
            """);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<TeamImages> ReadTeamImagesAsync(SqliteConnection connection, Guid teamId, CancellationToken ct)
    {
        Guid? crestId, flagId;
        await using (var command = Db.Command(connection, null, "SELECT crest_asset_id, flag_asset_id FROM team WHERE id = $id;")
            .With("$id", Db.Id(teamId)))
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return TeamImages.None;
            }

            (crestId, flagId) = (Db.ReadNullableId(reader, 0), Db.ReadNullableId(reader, 1));
        }

        return new TeamImages(
            await ReadAssetAsync(connection, crestId, ct).ConfigureAwait(false),
            await ReadAssetAsync(connection, flagId, ct).ConfigureAwait(false));
    }

    private static async Task<EventBranding?> ReadBrandingAsync(SqliteConnection connection, Guid tournamentId, CancellationToken ct)
    {
        string title;
        string? subtitle, primary, accent;
        Guid? logoId, organizerId;
        await using (var command = Db.Command(
            connection,
            null,
            """
            SELECT display_title, subtitle, primary_color_hex, accent_color_hex, logo_asset_id, organizer_logo_asset_id
            FROM event_branding WHERE tournament_id = $id;
            """).With("$id", Db.Id(tournamentId)))
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return null;
            }

            title = reader.GetString(0);
            subtitle = Db.ReadNullableString(reader, 1);
            primary = Db.ReadNullableString(reader, 2);
            accent = Db.ReadNullableString(reader, 3);
            logoId = Db.ReadNullableId(reader, 4);
            organizerId = Db.ReadNullableId(reader, 5);
        }

        var sponsorRows = new List<(string Name, Guid AssetId)>();
        await using (var command = Db.Command(
            connection, null, "SELECT name, asset_id FROM event_sponsor WHERE tournament_id = $id ORDER BY position;")
            .With("$id", Db.Id(tournamentId)))
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                sponsorRows.Add((reader.GetString(0), Db.ReadId(reader, 1)));
            }
        }

        var sponsors = new List<SponsorAsset>();
        foreach (var (name, assetId) in sponsorRows)
        {
            sponsors.Add(new SponsorAsset(name, (await ReadAssetAsync(connection, assetId, ct).ConfigureAwait(false))!));
        }

        return new EventBranding(
            tournamentId,
            title,
            subtitle,
            primary,
            accent,
            await ReadAssetAsync(connection, logoId, ct).ConfigureAwait(false),
            await ReadAssetAsync(connection, organizerId, ct).ConfigureAwait(false),
            sponsors);
    }

    private static async Task<ImageAsset?> ReadAssetAsync(SqliteConnection connection, Guid? assetId, CancellationToken ct)
    {
        if (assetId is not { } id)
        {
            return null;
        }

        await using var command = Db.Command(connection, null, "SELECT kind, file_name, content FROM asset WHERE id = $id;")
            .With("$id", Db.Id(id));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new ImageAsset(id, ParseKind(reader.GetString(0)), reader.GetString(1), (byte[])reader.GetValue(2));
    }

    private static ImageData? ToData(ImageAsset? image) => image is null ? null : new ImageData(image.Id, image.Content);

    internal static string KindName(AssetKind kind) => kind switch
    {
        AssetKind.Crest => "Escudo",
        AssetKind.Flag => "Bandera",
        AssetKind.Sponsor => "Auspiciador",
        AssetKind.EventLogo => "LogoEvento",
        _ => "Otro",
    };

    private static AssetKind ParseKind(string name) => name switch
    {
        "Escudo" => AssetKind.Crest,
        "Bandera" => AssetKind.Flag,
        "Auspiciador" => AssetKind.Sponsor,
        "LogoEvento" => AssetKind.EventLogo,
        _ => AssetKind.Other,
    };
}
