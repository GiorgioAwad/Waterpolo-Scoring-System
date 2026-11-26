using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Domain.Competitions;

namespace FDNDA.Waterpolo.Application.Abstractions;

/// <summary>
/// Fila de la lista de eventos (lectura), con las reglas vigentes del evento.
/// <see cref="ScheduledMatchCount"/> cuenta los partidos aún no iniciados: son los únicos que
/// tomarán las reglas del evento al iniciarse. <see cref="MatchCount"/> no cuenta los anulados.
/// </summary>
public sealed record TournamentSummary(
    Guid TournamentId,
    string Name,
    bool IsDemo,
    RulesetSnapshot Rules,
    int MatchCount,
    int ScheduledMatchCount)
{
    public string RulesPresetName => Rules.PresetName;

    public bool RulesIsDemoPreset => Rules.IsDemoPreset;
}

/// <summary>Jugador inscrito en la nómina de un evento (lectura). <see cref="EntryId"/> identifica la inscripción.</summary>
public sealed record TournamentRosterLine(Guid EntryId, Guid PlayerId, string PlayerName, int Cap);

/// <summary>
/// Persistencia del catálogo: eventos, equipos y nóminas de evento. Las altas son
/// idempotentes por identificador: si el registro ya existe devuelven falso y no escriben.
/// </summary>
public interface ICatalogRepository : ITournamentRepository
{
    Task<IReadOnlyList<TournamentSummary>> ListTournamentsAsync(CancellationToken ct = default);

    /// <returns>Falso si ya existía un evento con ese identificador.</returns>
    Task<bool> AddTournamentAsync(Tournament tournament, DateTimeOffset createdAtUtc, CancellationToken ct = default);

    /// <summary>
    /// Actualiza el nombre y las reglas del evento. Los partidos iniciados o cerrados no cambian:
    /// guardan su propia copia de las reglas desde que se iniciaron.
    /// </summary>
    Task UpdateTournamentAsync(Tournament tournament, CancellationToken ct = default);

    Task<IReadOnlyList<Team>> ListTeamsAsync(CancellationToken ct = default);

    Task<Team?> GetTeamAsync(Guid teamId, CancellationToken ct = default);

    /// <returns>Falso si ya existía un equipo con ese identificador.</returns>
    Task<bool> AddTeamAsync(Team team, DateTimeOffset createdAtUtc, CancellationToken ct = default);

    Task UpdateTeamAsync(Team team, CancellationToken ct = default);

    /// <summary>Nómina vigente de un equipo en un evento, ordenada por gorro.</summary>
    Task<IReadOnlyList<TournamentRosterLine>> GetTournamentRosterAsync(Guid tournamentId, Guid teamId, CancellationToken ct = default);

    /// <summary>Inscribe un jugador nuevo en la nómina del evento (jugador e inscripción en una transacción).</summary>
    /// <returns>Falso si ya existía una inscripción con ese identificador.</returns>
    Task<bool> AddRosterPlayerAsync(Player player, TournamentRosterEntry entry, DateTimeOffset createdAtUtc, CancellationToken ct = default);

    /// <summary>Corrige el nombre del jugador y el gorro de su inscripción.</summary>
    Task UpdateRosterPlayerAsync(Guid entryId, string playerName, int cap, CancellationToken ct = default);

    /// <summary>Quita la inscripción de la nómina del evento. El jugador se conserva: los partidos lo nombran.</summary>
    Task RemoveRosterEntryAsync(Guid entryId, CancellationToken ct = default);

    /// <summary>
    /// Escribe una importación en una sola transacción. Antes comprueba que la nómina de cada
    /// equipo siga como en la vista previa; si cambió lanza <see cref="Domain.Common.DomainException"/> y no escribe nada.
    /// </summary>
    Task ApplyRosterImportAsync(RosterImportChanges changes, DateTimeOffset atUtc, CancellationToken ct = default);

    /// <summary>Partidos aún programados del evento en los que juega alguno de los equipos.</summary>
    Task<int> CountScheduledMatchesAsync(Guid tournamentId, IReadOnlyCollection<Guid> teamIds, CancellationToken ct = default);
}
