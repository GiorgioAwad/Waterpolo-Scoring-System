using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Backups;
using FDNDA.Waterpolo.Application.Identity;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Domain.Competitions;
using FDNDA.Waterpolo.Infrastructure.Identity;
using FDNDA.Waterpolo.Infrastructure.Import;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;
using Microsoft.Data.Sqlite;

namespace FDNDA.Waterpolo.Infrastructure.Tests;

/// <summary>R7 — imágenes de equipos, plantilla del evento y su publicación, sobre SQLite temporal.</summary>
public class IdentityOperationsTests
{
    [Fact]
    public async Task La_demo_trae_escudos_genericos_para_sus_dos_clubes()
    {
        await using var storage = await TempStorage.CreateAsync();
        var repository = new SqliteIdentityRepository(storage.OpenDatabase());

        var home = await repository.GetTeamImagesAsync(DemoDataSeeder.HomeTeamId);
        var away = await repository.GetTeamImagesAsync(DemoDataSeeder.AwayTeamId);

        Assert.Equal("escudo-delfines-ejemplo.png", home.Crest!.FileName);
        Assert.Equal("escudo-tiburones-ejemplo.png", away.Crest!.FileName);
        Assert.Null(home.Flag);
        Assert.Equal(2, await CountAssetsAsync(storage));
    }

    [Fact]
    public async Task Escudo_y_bandera_se_guardan_y_reemplazar_borra_la_imagen_que_ya_nadie_usa()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (identity, _, _) = await CreateAsync(storage);
        var teamId = DemoDataSeeder.HomeTeamId;
        var oldCrest = (await identity.GetTeamImagesAsync(teamId)).Crest!;
        var crest = Image(AssetKind.Crest, 1);
        var flag = new ImageAsset(Guid.NewGuid(), AssetKind.Flag, "bandera.jpg", TestImages.Jpeg(360, 240));

        var saved = await identity.SetTeamImageAsync(teamId, AssetKind.Crest, crest);
        var again = await identity.SetTeamImageAsync(teamId, AssetKind.Crest, crest);
        var flagged = await identity.SetTeamImageAsync(teamId, AssetKind.Flag, flag);

        Assert.Equal(CommandOutcome.Applied, saved.Outcome);
        Assert.StartsWith("Escudo de DEL guardado", saved.Message, StringComparison.Ordinal);
        Assert.Equal(CommandOutcome.AlreadyApplied, again.Outcome);
        Assert.StartsWith("Bandera de DEL guardada: bandera.jpg · 360×240 px · ", flagged.Message, StringComparison.Ordinal);
        var images = await identity.GetTeamImagesAsync(teamId);
        Assert.Equal((crest.Id, crest.Sha256), (images.Crest!.Id, images.Crest.Sha256));
        Assert.Equal(("image/jpeg", 360), (images.Flag!.MediaType, images.Flag.Width));
        Assert.False(await AssetExistsAsync(storage, oldCrest.Id)); // reemplazada: no queda huérfana
        Assert.Equal(3, await CountAssetsAsync(storage)); // escudo nuevo, bandera y el escudo de TIB

        var removed = await identity.SetTeamImageAsync(teamId, AssetKind.Flag, null);
        Assert.Equal("Se quitó la bandera de DEL.", removed.Message);
        Assert.Null((await identity.GetTeamImagesAsync(teamId)).Flag);
        Assert.Equal(2, await CountAssetsAsync(storage));
    }

    [Fact]
    public async Task Una_imagen_de_otro_tipo_o_un_equipo_inexistente_se_rechazan_sin_escribir()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (identity, _, _) = await CreateAsync(storage);

        var wrongKind = await identity.SetTeamImageAsync(DemoDataSeeder.HomeTeamId, AssetKind.Flag, Image(AssetKind.Crest, 1));
        var noTeam = await identity.SetTeamImageAsync(Guid.NewGuid(), AssetKind.Crest, Image(AssetKind.Crest, 2));

        Assert.Equal(CommandOutcome.Rejected, wrongKind.Outcome);
        Assert.Equal(CommandOutcome.Rejected, noTeam.Outcome);
        Assert.Contains("Guarde el equipo", noTeam.Message, StringComparison.Ordinal);
        Assert.Equal(2, await CountAssetsAsync(storage));
    }

    [Fact]
    public async Task La_plantilla_del_evento_se_guarda_con_auspiciadores_en_orden_y_se_puede_quitar()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (identity, _, _) = await CreateAsync(storage);
        var tournamentId = DemoDataSeeder.TournamentId;
        var logo = Image(AssetKind.EventLogo, 1);
        var sponsors = new[] { Image(AssetKind.Sponsor, 2), Image(AssetKind.Sponsor, 3), Image(AssetKind.Sponsor, 4) };
        var draft = new EventBrandingDraft(
            "Copa de Prueba", "Fecha 1", "#0B2A4A", "FFC83D", logo, null,
            [new("Marca Uno", sponsors[0]), new("Marca Dos", sponsors[1]), new("Marca Tres", sponsors[2])]);

        var saved = await identity.SaveEventBrandingAsync(tournamentId, draft);
        var repeated = await identity.SaveEventBrandingAsync(tournamentId, draft);

        Assert.Equal(CommandOutcome.Applied, saved.Outcome);
        Assert.Equal(
            "Identidad de «Torneo Demostración FDNDA» guardada: título «Copa de Prueba», logo del evento, 3 auspiciadores.",
            saved.Message);
        Assert.Equal(CommandOutcome.AlreadyApplied, repeated.Outcome);
        var stored = (await identity.GetEventBrandingAsync(tournamentId))!;
        Assert.Equal(("Copa de Prueba", "Fecha 1", "#FFC83D"), (stored.DisplayTitle, stored.Subtitle, stored.AccentColorHex));
        Assert.Equal(logo.Sha256, stored.Logo!.Sha256);
        Assert.Equal(["Marca Uno", "Marca Dos", "Marca Tres"], stored.Sponsors.Select(s => s.Name));

        // Reordenar y quitar un auspiciador: el que sale se borra de la base.
        var reordered = draft with { Sponsors = [new("Marca Tres", sponsors[2]), new("Marca Uno", sponsors[0])] };
        Assert.Equal(CommandOutcome.Applied, (await identity.SaveEventBrandingAsync(tournamentId, reordered)).Outcome);
        Assert.Equal(["Marca Tres", "Marca Uno"], (await identity.GetEventBrandingAsync(tournamentId))!.Sponsors.Select(s => s.Name));
        Assert.False(await AssetExistsAsync(storage, sponsors[1].Id));

        var removed = await identity.RemoveEventBrandingAsync(tournamentId);
        Assert.Equal(CommandOutcome.Applied, removed.Outcome);
        Assert.Null(await identity.GetEventBrandingAsync(tournamentId));
        Assert.Equal(2, await CountAssetsAsync(storage)); // solo los escudos de la demo
        Assert.Equal(CommandOutcome.AlreadyApplied, (await identity.RemoveEventBrandingAsync(tournamentId)).Outcome);
    }

    [Fact]
    public async Task Una_plantilla_invalida_se_rechaza_con_el_motivo()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (identity, _, _) = await CreateAsync(storage);
        var draft = new EventBrandingDraft(" ", null, null, null, null, null, []);

        var result = await identity.SaveEventBrandingAsync(DemoDataSeeder.TournamentId, draft);

        Assert.Equal(CommandOutcome.Rejected, result.Outcome);
        Assert.Equal("Indique el título que verá el público.", result.Message);
        Assert.Null(await identity.GetEventBrandingAsync(DemoDataSeeder.TournamentId));
    }

    [Fact]
    public async Task Al_abrir_un_partido_se_publica_su_identidad_y_cada_cambio_la_actualiza()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (identity, desk, hub) = await CreateAsync(storage);
        var published = new List<ScoreboardIdentity>();
        hub.Changed += (_, e) => published.Add(e);

        await desk.Operations.OpenMatchAsync(TestDesk.MatchId);
        await identity.WhenIdleAsync();

        var opened = Assert.Single(published);
        Assert.Equal(TestDesk.MatchId, opened.MatchId);
        Assert.Null(opened.Title); // sin plantilla: el marcador usa el nombre del evento
        Assert.NotNull(opened.Home.Crest);
        Assert.Null(opened.Away.Flag);

        var flag = new ImageAsset(Guid.NewGuid(), AssetKind.Flag, "bandera.png", TestImages.Png(360, 240));
        await identity.SetTeamImageAsync(DemoDataSeeder.AwayTeamId, AssetKind.Flag, flag);
        await identity.SaveEventBrandingAsync(DemoDataSeeder.TournamentId, new EventBrandingDraft(
            "Copa de Prueba", null, null, null, null, Image(AssetKind.EventLogo, 5), [new("Marca", Image(AssetKind.Sponsor, 6))]));

        Assert.Equal(3, published.Count);
        Assert.Equal(flag.Id, published[1].Away.Flag!.AssetId);
        Assert.Equal("Copa de Prueba", hub.Current!.Title);
        Assert.NotNull(hub.Current.OrganizerLogo);
        Assert.Equal("Marca", Assert.Single(hub.Current.Sponsors).Name);

        // Una incidencia no vuelve a leer la identidad: solo el cambio de partido o de identidad.
        await desk.StartAsync();
        await identity.WhenIdleAsync();
        Assert.Equal(3, published.Count);
    }

    [Fact]
    public async Task Las_imagenes_viajan_en_el_respaldo_y_se_restauran_identicas()
    {
        await using var origin = await TempStorage.CreateAsync();
        var (identity, desk, _) = await CreateAsync(origin);
        var flag = Image(AssetKind.Flag, 7);
        await identity.SetTeamImageAsync(DemoDataSeeder.HomeTeamId, AssetKind.Flag, flag);
        var logo = Image(AssetKind.EventLogo, 8);
        await identity.SaveEventBrandingAsync(DemoDataSeeder.TournamentId, new EventBrandingDraft(
            "Copa de Prueba", null, "#102A43", null, logo, null, [new("Marca", Image(AssetKind.Sponsor, 9))]));
        var backup = await new BackupOperations(
            new SqliteBackupStore(origin.OpenDatabase(), origin.Paths.BackupDirectory, origin.Time), desk.Repository, origin.Log,
            AppProfile.Demo).CreateAsync(BackupReason.Manual);
        Assert.True(backup.IsSuccess, backup.Message);

        await using var target = await TempStorage.CreateAsync();
        var targetDesk = new TestDesk(target);
        var restored = await new BackupOperations(
            new SqliteBackupStore(target.OpenDatabase(), target.Paths.BackupDirectory, target.Time), targetDesk.Repository, target.Log,
            AppProfile.Demo).RestoreAsync(backup.Path!);
        Assert.Equal(CommandOutcome.Applied, restored.Outcome);

        var repository = new SqliteIdentityRepository(target.OpenDatabase());
        Assert.Equal(flag.Sha256, (await repository.GetTeamImagesAsync(DemoDataSeeder.HomeTeamId)).Flag!.Sha256);
        var branding = (await repository.GetEventBrandingAsync(DemoDataSeeder.TournamentId))!;
        Assert.Equal((logo.Id, logo.Sha256), (branding.Logo!.Id, branding.Logo.Sha256));
        Assert.Equal("Marca", Assert.Single(branding.Sponsors).Name);
        Assert.Equal(5, await CountAssetsAsync(target));
    }

    [Fact]
    public async Task Al_cargar_un_archivo_se_valida_antes_de_guardar_y_el_error_nombra_el_archivo()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (identity, _, _) = await CreateAsync(storage);
        var folder = Path.Combine(storage.Root, "imagenes");
        Directory.CreateDirectory(folder);
        var good = Path.Combine(folder, "escudo.png");
        var text = Path.Combine(folder, "notas.png");
        var huge = Path.Combine(folder, "enorme.png");
        await File.WriteAllBytesAsync(good, TestImages.Png(200, 240));
        await File.WriteAllTextAsync(text, "no es una imagen");
        await File.WriteAllBytesAsync(huge, TestImages.Png(800, 800, padding: ImageRules.MaxBytes + 10));

        var loaded = await identity.LoadImageAsync(good, AssetKind.Crest);
        var notImage = await identity.LoadImageAsync(text, AssetKind.Crest);
        var tooBig = await identity.LoadImageAsync(huge, AssetKind.Crest);
        var missing = await identity.LoadImageAsync(Path.Combine(folder, "no-existe.png"), AssetKind.Crest);

        Assert.True(loaded.IsSuccess);
        Assert.Equal(("escudo.png", AssetKind.Crest), (loaded.Image!.FileName, loaded.Image.Kind));
        Assert.Equal("No se cargó «notas.png»: El archivo no es una imagen reconocible. Use una imagen PNG o JPEG.", notImage.Message);
        Assert.StartsWith("No se cargó «enorme.png»: La imagen pesa 2.0 MB", tooBig.Message, StringComparison.Ordinal);
        Assert.Equal("No se cargó «no-existe.png»: El archivo no existe.", missing.Message);
        Assert.Equal(2, await CountAssetsAsync(storage)); // cargar no guarda nada
    }

    [Fact]
    public async Task La_plantilla_de_ejemplo_del_Panamericano_carga_recursos_genericos_validos_y_se_puede_guardar()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (identity, _, _) = await CreateAsync(storage);

        var example = await identity.LoadExampleAsync();

        var draft = example.Draft!;
        Assert.Equal("Panamericano de Waterpolo Sub-17 2027", draft.Title);
        Assert.Contains("no oficiales", draft.Subtitle, StringComparison.Ordinal);
        Assert.Equal(("image/png", AssetKind.EventLogo), (draft.Logo!.MediaType, draft.Logo.Kind));
        Assert.Equal(AssetKind.EventLogo, draft.OrganizerLogo!.Kind);
        Assert.Equal(3, draft.Sponsors.Count);
        Assert.All(draft.Sponsors, s => Assert.Contains("(ejemplo)", s.Name, StringComparison.Ordinal));
        Assert.Equal(2, await CountAssetsAsync(storage)); // cargarla no guarda nada

        var saved = await identity.SaveEventBrandingAsync(DemoDataSeeder.TournamentId, draft);
        Assert.Equal(CommandOutcome.Applied, saved.Outcome);
        Assert.Equal(7, await CountAssetsAsync(storage));
    }

    private static async Task<(IdentityOperations Identity, TestDesk Desk, ScoreboardIdentityHub Hub)> CreateAsync(TempStorage storage)
    {
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var hub = new ScoreboardIdentityHub();
        var identity = new IdentityOperations(
            new SqliteIdentityRepository(storage.OpenDatabase()),
            desk.Catalog,
            new ImageFileReader(),
            new ExampleIdentitySource(),
            desk.Hub,
            hub,
            storage.Time,
            storage.Log);
        return (identity, desk, hub);
    }

    private static ImageAsset Image(AssetKind kind, int n) =>
        new(Guid.NewGuid(), kind, $"imagen-{n}.png", TestImages.Png(200 + n, 120 + n, fill: (byte)n));

    private static async Task<long> CountAssetsAsync(TempStorage storage)
    {
        await using var connection = await storage.OpenDatabase().OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM asset;";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> AssetExistsAsync(TempStorage storage, Guid id)
    {
        await using var connection = await storage.OpenDatabase().OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM asset WHERE id = $id;";
        command.Parameters.Add(new SqliteParameter("$id", id.ToString("D")));
        return await command.ExecuteScalarAsync() is not null;
    }
}
