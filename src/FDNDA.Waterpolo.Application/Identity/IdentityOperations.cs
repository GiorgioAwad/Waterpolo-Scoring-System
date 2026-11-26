using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;

namespace FDNDA.Waterpolo.Application.Identity;

/// <summary>Imagen leída y validada, aún sin guardar; o el motivo por el que no se pudo usar.</summary>
public sealed record ImageLoadResult(ImageAsset? Image, string? Message)
{
    public bool IsSuccess => Image is not null;
}

public sealed record SponsorDraft(string Name, ImageAsset Image);

/// <summary>Plantilla del evento tal como la edita el operador, antes de validarla.</summary>
public sealed record EventBrandingDraft(
    string Title,
    string? Subtitle,
    string? PrimaryColorHex,
    string? AccentColorHex,
    ImageAsset? Logo,
    ImageAsset? OrganizerLogo,
    IReadOnlyList<SponsorDraft> Sponsors)
{
    public static EventBrandingDraft From(EventBranding branding) => new(
        branding.DisplayTitle,
        branding.Subtitle,
        branding.PrimaryColorHex,
        branding.AccentColorHex,
        branding.Logo,
        branding.OrganizerLogo,
        [.. branding.Sponsors.Select(s => new SponsorDraft(s.Name, s.Image))]);
}

public sealed record ExampleDraftResult(EventBrandingDraft? Draft, string? Message);

/// <summary>
/// Casos de uso de la identidad visual (RF01, RF07, RF08): escudos y banderas de equipos,
/// plantilla del marcador por evento y plantilla de ejemplo. Publica la identidad del partido
/// abierto cuando cambia de partido o cuando se guarda un cambio de identidad. La identidad es
/// presentación: no toca partidos, incidencias ni resultados.
/// </summary>
public sealed class IdentityOperations : IDisposable
{
    private const string StorageFailureMessage =
        "No se pudo guardar el cambio en el almacenamiento local. Revise el disco y vuelva a intentar.";

    private readonly IIdentityRepository _identity;
    private readonly ICatalogRepository _catalog;
    private readonly IImageFileReader? _reader;
    private readonly IExampleIdentitySource? _examples;
    private readonly IMatchSnapshotFeed _feed;
    private readonly IScoreboardIdentityPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly IDiagnosticLog _log;
    private readonly SemaphoreSlim _writer = new(1, 1);
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private Guid? _loadedMatchId;
    private Task _pending = Task.CompletedTask;

    public IdentityOperations(
        IIdentityRepository identity,
        ICatalogRepository catalog,
        IImageFileReader? reader,
        IExampleIdentitySource? examples,
        IMatchSnapshotFeed feed,
        IScoreboardIdentityPublisher publisher,
        TimeProvider time,
        IDiagnosticLog log)
    {
        _identity = identity;
        _catalog = catalog;
        _reader = reader;
        _examples = examples;
        _feed = feed;
        _publisher = publisher;
        _time = time;
        _log = log;
        _feed.Published += OnSnapshotPublished;
    }

    public void Dispose() => _feed.Published -= OnSnapshotPublished;

    /// <summary>Termina cuando la última recarga de identidad lanzada por un cambio de partido acabó.</summary>
    public Task WhenIdleAsync() => _pending;

    // ---------- Imágenes ----------

    /// <summary>Lee y valida una imagen elegida por el operador. No guarda nada.</summary>
    public async Task<ImageLoadResult> LoadImageAsync(string path, AssetKind kind, CancellationToken ct = default)
    {
        if (_reader is null)
        {
            return new ImageLoadResult(null, "La carga de imágenes no está disponible en esta instalación.");
        }

        var name = Path.GetFileName(path);
        try
        {
            var reader = _reader;
            var file = await Task.Run(() => reader.Read(path, ct), ct).ConfigureAwait(false);
            return new ImageLoadResult(new ImageAsset(Guid.NewGuid(), kind, file.FileName, file.Content), null);
        }
        catch (DomainException ex)
        {
            return new ImageLoadResult(null, $"No se cargó «{name}»: {ex.Message}");
        }
        catch (ImageFileException ex)
        {
            return new ImageLoadResult(null, $"No se cargó «{name}»: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"No se pudo abrir la imagen: {ex.GetType().Name}.");
            return new ImageLoadResult(null, $"No se pudo abrir «{name}». Compruebe que existe y que tiene permiso para leerlo.");
        }
    }

    // ---------- Equipos ----------

    public Task<TeamImages> GetTeamImagesAsync(Guid teamId, CancellationToken ct = default) =>
        _identity.GetTeamImagesAsync(teamId, ct);

    /// <summary>Asigna o quita el escudo o la bandera del equipo. Se ve de inmediato en mesa y marcador.</summary>
    public Task<CatalogResult> SetTeamImageAsync(Guid teamId, AssetKind kind, ImageAsset? image, CancellationToken ct = default) =>
        ExecuteAsync("guardar la imagen del equipo", async () =>
        {
            if (kind is not (AssetKind.Crest or AssetKind.Flag))
            {
                throw new DomainException("imagen.tipo", "A un equipo solo se le asigna escudo o bandera.");
            }

            if (image is not null && image.Kind != kind)
            {
                throw new DomainException("imagen.tipo", $"La imagen no se cargó como {ImageRules.KindText(kind)}.");
            }

            var team = await _catalog.GetTeamAsync(teamId, ct).ConfigureAwait(false)
                ?? throw new DomainException("equipo.no_existe", "Guarde el equipo antes de asignarle imágenes.");
            var current = await _identity.GetTeamImagesAsync(teamId, ct).ConfigureAwait(false);
            var existing = kind == AssetKind.Crest ? current.Crest : current.Flag;
            var what = ImageRules.KindText(kind);
            if (existing?.Id == image?.Id)
            {
                return new CatalogResult(CommandOutcome.AlreadyApplied, image is null ? $"{team.Code} no tenía {what}." : $"El {what} ya estaba asignado.");
            }

            await _identity.SetTeamImageAsync(teamId, kind, image, _time.GetUtcNow(), ct).ConfigureAwait(false);
            return new CatalogResult(
                CommandOutcome.Applied,
                image is null ? $"Se quitó {Article(kind)} {what} de {team.Code}." : $"{Capitalize(what)} de {team.Code} guardad{Ending(kind)}: {image.Description}.");
        }, ct);

    // ---------- Plantilla del evento ----------

    public Task<EventBranding?> GetEventBrandingAsync(Guid tournamentId, CancellationToken ct = default) =>
        _identity.GetEventBrandingAsync(tournamentId, ct);

    /// <summary>Guarda la plantilla del evento. Vale para todos sus partidos, también los ya jugados, al mostrarse.</summary>
    public Task<CatalogResult> SaveEventBrandingAsync(Guid tournamentId, EventBrandingDraft draft, CancellationToken ct = default) =>
        ExecuteAsync("guardar la identidad del evento", async () =>
        {
            ArgumentNullException.ThrowIfNull(draft);
            var tournament = await _catalog.GetAsync(tournamentId, ct).ConfigureAwait(false)
                ?? throw new DomainException("evento.no_existe", "El evento no existe.");
            var branding = new EventBranding(
                tournamentId,
                draft.Title,
                draft.Subtitle,
                draft.PrimaryColorHex,
                draft.AccentColorHex,
                draft.Logo,
                draft.OrganizerLogo,
                [.. draft.Sponsors.Select(s => new SponsorAsset(s.Name, s.Image))]);

            var current = await _identity.GetEventBrandingAsync(tournamentId, ct).ConfigureAwait(false);
            if (current is not null && SameBranding(current, branding))
            {
                return new CatalogResult(CommandOutcome.AlreadyApplied, "La identidad del evento no tenía cambios.");
            }

            await _identity.SaveEventBrandingAsync(branding, _time.GetUtcNow(), ct).ConfigureAwait(false);
            return new CatalogResult(CommandOutcome.Applied, $"Identidad de «{tournament.Name}» guardada: {Summary(branding)}.");
        }, ct);

    public Task<CatalogResult> RemoveEventBrandingAsync(Guid tournamentId, CancellationToken ct = default) =>
        ExecuteAsync("quitar la identidad del evento", async () =>
        {
            var tournament = await _catalog.GetAsync(tournamentId, ct).ConfigureAwait(false)
                ?? throw new DomainException("evento.no_existe", "El evento no existe.");
            if (await _identity.GetEventBrandingAsync(tournamentId, ct).ConfigureAwait(false) is null)
            {
                return new CatalogResult(CommandOutcome.AlreadyApplied, $"«{tournament.Name}» no tenía identidad propia.");
            }

            await _identity.RemoveEventBrandingAsync(tournamentId, ct).ConfigureAwait(false);
            return new CatalogResult(
                CommandOutcome.Applied, $"Se quitó la identidad de «{tournament.Name}»: el marcador usa la presentación predeterminada.");
        }, ct);

    /// <summary>
    /// Plantilla de ejemplo del Panamericano Sub-17 2027 con recursos genéricos y originales (no
    /// oficiales), para editar y guardar. No guarda nada por sí sola.
    /// </summary>
    public async Task<ExampleDraftResult> LoadExampleAsync(CancellationToken ct = default)
    {
        if (_examples is null)
        {
            return new ExampleDraftResult(null, "La plantilla de ejemplo no está disponible en esta instalación.");
        }

        try
        {
            var examples = _examples;
            var example = await Task.Run(examples.Load, ct).ConfigureAwait(false);
            var draft = new EventBrandingDraft(
                example.Title,
                example.Subtitle,
                example.PrimaryColorHex,
                example.AccentColorHex,
                ToAsset(example.Logo, AssetKind.EventLogo),
                ToAsset(example.OrganizerLogo, AssetKind.EventLogo),
                [.. example.Sponsors.Select(s => new SponsorDraft(s.Name, ToAsset(s.Image, AssetKind.Sponsor)))]);
            return new ExampleDraftResult(draft, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error("No se pudo cargar la plantilla de ejemplo.", ex);
            return new ExampleDraftResult(null, "No se pudo cargar la plantilla de ejemplo.");
        }
    }

    // ---------- Publicación ----------

    /// <summary>Vuelve a leer la identidad del partido abierto y la publica.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _refresh.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_feed.Current is not { } snapshot)
            {
                return;
            }

            var identity = await _identity.GetMatchIdentityAsync(snapshot.MatchId, ct).ConfigureAwait(false);

            // Si mientras tanto se abrió otro partido, lo publicará su propia recarga.
            if (identity is not null && _feed.Current?.MatchId == identity.MatchId)
            {
                _loadedMatchId = identity.MatchId;
                _publisher.Publish(identity);
            }
        }
        finally
        {
            _refresh.Release();
        }
    }

    private void OnSnapshotPublished(object? sender, MatchSnapshotPublished e)
    {
        if (e.Change.Kind == MatchChangeKind.Loaded || e.Snapshot.MatchId != _loadedMatchId)
        {
            _pending = RefreshSafeAsync();
        }
    }

    private async Task RefreshSafeAsync()
    {
        try
        {
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sin identidad el marcador sigue mostrando nombres, colores y resultado.
            _log.Error("No se pudo leer la identidad visual del partido.", ex);
        }
    }

    private async Task<CatalogResult> ExecuteAsync(string action, Func<Task<CatalogResult>> apply, CancellationToken ct)
    {
        CatalogResult result;
        await _writer.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            result = await apply().ConfigureAwait(false);
        }
        catch (DomainException ex)
        {
            return new CatalogResult(CommandOutcome.Rejected, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error($"No se pudo {action}.", ex);
            return new CatalogResult(CommandOutcome.Failed, StorageFailureMessage);
        }
        finally
        {
            _writer.Release();
        }

        // Publicar después del commit: la mesa y el marcador ven el cambio sin reabrir el partido.
        if (result.Outcome == CommandOutcome.Applied)
        {
            await RefreshSafeAsync().ConfigureAwait(false);
        }

        return result;
    }

    private static ImageAsset ToAsset(ImageFile file, AssetKind kind) => new(Guid.NewGuid(), kind, file.FileName, file.Content);

    private static bool SameBranding(EventBranding a, EventBranding b) =>
        a.DisplayTitle == b.DisplayTitle
        && a.Subtitle == b.Subtitle
        && a.PrimaryColorHex == b.PrimaryColorHex
        && a.AccentColorHex == b.AccentColorHex
        && a.Logo?.Id == b.Logo?.Id
        && a.OrganizerLogo?.Id == b.OrganizerLogo?.Id
        && a.Sponsors.Select(s => (s.Name, s.Image.Id)).SequenceEqual(b.Sponsors.Select(s => (s.Name, s.Image.Id)));

    private static string Summary(EventBranding branding)
    {
        var parts = new List<string> { $"título «{branding.DisplayTitle}»" };
        if (branding.Logo is not null)
        {
            parts.Add("logo del evento");
        }

        if (branding.OrganizerLogo is not null)
        {
            parts.Add("logo de la organización");
        }

        parts.Add(branding.Sponsors.Count switch
        {
            0 => "sin auspiciadores",
            1 => "1 auspiciador",
            var n => $"{n} auspiciadores",
        });
        return string.Join(", ", parts);
    }

    private static string Article(AssetKind kind) => kind == AssetKind.Flag ? "la" : "el";

    private static string Ending(AssetKind kind) => kind == AssetKind.Flag ? "a" : "o";

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
