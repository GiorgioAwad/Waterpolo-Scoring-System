namespace FDNDA.Waterpolo.Application.Identity;

/// <summary>Imagen lista para mostrar: su identificador (para no decodificar dos veces) y sus bytes.</summary>
public sealed record ImageData(Guid AssetId, byte[] Content);

public sealed record TeamIdentity(Guid TeamId, ImageData? Crest, ImageData? Flag)
{
    public static TeamIdentity Empty(Guid teamId) => new(teamId, null, null);
}

public sealed record SponsorIdentity(string Name, ImageData Image);

/// <summary>
/// Identidad visual del partido abierto: plantilla de su evento e imágenes de sus equipos.
/// Se resuelve desde el catálogo vigente al mostrarse; no forma parte del registro del partido.
/// Sin plantilla, <see cref="Title"/> es nulo y el marcador usa el nombre del evento del partido.
/// </summary>
public sealed record ScoreboardIdentity(
    Guid MatchId,
    Guid TournamentId,
    string? Title,
    string? Subtitle,
    string? PrimaryColorHex,
    string? AccentColorHex,
    ImageData? Logo,
    ImageData? OrganizerLogo,
    IReadOnlyList<SponsorIdentity> Sponsors,
    TeamIdentity Home,
    TeamIdentity Away);

/// <summary>Lectura de la identidad del partido abierto, para la mesa y el marcador.</summary>
public interface IScoreboardIdentityFeed
{
    ScoreboardIdentity? Current { get; }

    /// <summary>Se dispara al cambiar de partido o de identidad. Puede llegar desde cualquier hilo.</summary>
    event EventHandler<ScoreboardIdentity>? Changed;
}

public interface IScoreboardIdentityPublisher
{
    void Publish(ScoreboardIdentity identity);
}

/// <summary>Estado compartido en memoria entre la mesa y el marcador.</summary>
public sealed class ScoreboardIdentityHub : IScoreboardIdentityFeed, IScoreboardIdentityPublisher
{
    private volatile ScoreboardIdentity? _current;

    public ScoreboardIdentity? Current => _current;

    public event EventHandler<ScoreboardIdentity>? Changed;

    public void Publish(ScoreboardIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        _current = identity;
        Changed?.Invoke(this, identity);
    }
}
