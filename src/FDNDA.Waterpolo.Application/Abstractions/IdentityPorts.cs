using FDNDA.Waterpolo.Application.Identity;
using FDNDA.Waterpolo.Domain.Competitions;

namespace FDNDA.Waterpolo.Application.Abstractions;

/// <summary>
/// Persistencia de la identidad visual: imágenes de equipos y plantilla del evento. Las imágenes
/// nuevas se insertan de forma idempotente por su identificador; las que quedan sin uso se borran
/// en la misma transacción para no hacer crecer la base con cada reemplazo.
/// </summary>
public interface IIdentityRepository
{
    Task<TeamImages> GetTeamImagesAsync(Guid teamId, CancellationToken ct = default);

    /// <summary>Asigna o quita (con <paramref name="image"/> nulo) el escudo o la bandera del equipo.</summary>
    Task SetTeamImageAsync(Guid teamId, AssetKind kind, ImageAsset? image, DateTimeOffset atUtc, CancellationToken ct = default);

    /// <summary>Plantilla del evento con sus imágenes; nula si el evento no tiene identidad propia.</summary>
    Task<EventBranding?> GetEventBrandingAsync(Guid tournamentId, CancellationToken ct = default);

    /// <summary>Guarda la plantilla completa (textos, colores, logos y auspiciadores) en una transacción.</summary>
    Task SaveEventBrandingAsync(EventBranding branding, DateTimeOffset atUtc, CancellationToken ct = default);

    /// <summary>Quita la identidad propia del evento; el marcador vuelve a la presentación predeterminada.</summary>
    Task RemoveEventBrandingAsync(Guid tournamentId, CancellationToken ct = default);

    /// <summary>Identidad del partido: plantilla de su evento e imágenes de sus dos equipos; nula si el partido no existe.</summary>
    Task<ScoreboardIdentity?> GetMatchIdentityAsync(Guid matchId, CancellationToken ct = default);
}

/// <summary>Archivo de imagen leído del disco, aún sin validar.</summary>
public sealed record ImageFile(string FileName, byte[] Content);

/// <summary>El archivo de imagen no se pudo leer (tamaño excesivo, permiso, archivo en uso).</summary>
public sealed class ImageFileException(string message) : Exception(message);

/// <summary>Lee un archivo de imagen elegido por el operador, sin pasar del peso máximo.</summary>
public interface IImageFileReader
{
    /// <exception cref="ImageFileException">El archivo no se pudo leer o pesa demasiado.</exception>
    ImageFile Read(string path, CancellationToken ct = default);
}

/// <summary>Recursos de la plantilla de ejemplo, incluidos en el programa. Son genéricos u originales.</summary>
public interface IExampleIdentitySource
{
    ExampleIdentity Load();
}

/// <summary>Contenido de una plantilla de ejemplo, sin validar.</summary>
public sealed record ExampleIdentity(
    string Title,
    string Subtitle,
    string PrimaryColorHex,
    string AccentColorHex,
    ImageFile Logo,
    ImageFile OrganizerLogo,
    IReadOnlyList<(string Name, ImageFile Image)> Sponsors);
