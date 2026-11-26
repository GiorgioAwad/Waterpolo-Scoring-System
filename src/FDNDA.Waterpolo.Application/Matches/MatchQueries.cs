using FDNDA.Waterpolo.Application.Abstractions;

namespace FDNDA.Waterpolo.Application.Matches;

/// <summary>Lecturas de partidos. No cambian estado.</summary>
public sealed class MatchQueries(IMatchRepository matches)
{
    public Task<IReadOnlyList<MatchSummary>> ListAsync(CancellationToken ct = default) => matches.ListAsync(ct);

    /// <summary>Partido en juego para recuperarlo al arrancar, si existe.</summary>
    public Task<Guid?> FindInProgressMatchIdAsync(CancellationToken ct = default) =>
        matches.FindInProgressMatchIdAsync(ct);

    /// <summary>Snapshot derivado de los datos persistidos, sin publicarlo.</summary>
    public async Task<MatchSnapshot?> GetSnapshotAsync(Guid matchId, CancellationToken ct = default)
    {
        var match = await matches.GetAsync(matchId, ct).ConfigureAwait(false);
        return match is null ? null : MatchSnapshotProjector.Project(match);
    }
}
