namespace FDNDA.Waterpolo.Application.Matches;

/// <summary>Motivo de una publicación. Permite animar solo acciones nuevas, una sola vez.</summary>
public enum MatchChangeKind
{
    /// <summary>Se abrió o recuperó un partido desde los datos persistidos. No dispara animaciones.</summary>
    Loaded = 0,
    Started = 1,
    StageChanged = 2,
    Finished = 3,
    Reopened = 4,
    GoalRegistered = 5,
    IncidentCorrected = 6,
    IncidentVoided = 7,
    IncidentRegistered = 8,

    /// <summary>
    /// Se reprogramó, se actualizó la nómina o se anuló un partido aún no iniciado. No dispara
    /// animaciones; solo se publica si ese partido es el abierto en la mesa.
    /// </summary>
    PreMatchChanged = 9,
}

public sealed record MatchChange(MatchChangeKind Kind, Guid? IncidentId = null);

public sealed record MatchSnapshotPublished(MatchSnapshot Snapshot, MatchChange Change);

/// <summary>Lectura del estado confirmado del partido abierto en mesa.</summary>
public interface IMatchSnapshotFeed
{
    /// <summary>Último snapshot confirmado, o nulo si no hay partido abierto.</summary>
    MatchSnapshot? Current { get; }

    /// <summary>
    /// Se dispara una vez por cada escritura confirmada. Puede llegar desde cualquier hilo:
    /// la interfaz debe despachar al hilo de UI.
    /// </summary>
    event EventHandler<MatchSnapshotPublished>? Published;
}

/// <summary>Publicación de snapshots; solo se invoca después del commit.</summary>
public interface IMatchSnapshotPublisher
{
    /// <summary>Partido abierto en la mesa (el último publicado), o nulo.</summary>
    Guid? CurrentMatchId { get; }

    void Publish(MatchSnapshot snapshot, MatchChange change);
}

/// <summary>
/// Estado compartido en memoria entre la mesa y el marcador, dentro del mismo proceso.
/// </summary>
public sealed class MatchSnapshotHub : IMatchSnapshotFeed, IMatchSnapshotPublisher
{
    private volatile MatchSnapshot? _current;

    public MatchSnapshot? Current => _current;

    public Guid? CurrentMatchId => _current?.MatchId;

    public event EventHandler<MatchSnapshotPublished>? Published;

    public void Publish(MatchSnapshot snapshot, MatchChange change)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(change);
        _current = snapshot;
        Published?.Invoke(this, new MatchSnapshotPublished(snapshot, change));
    }
}
