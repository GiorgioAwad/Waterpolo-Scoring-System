using FDNDA.Waterpolo.Domain.Competitions;
using FDNDA.Waterpolo.Domain.Matches;

namespace FDNDA.Waterpolo.Application.Abstractions;

/// <summary>Perfil de datos. Demo y producción usan bases de datos separadas.</summary>
public enum AppProfile
{
    Demo = 1,
    Produccion = 2,
}

/// <summary>Identificador idempotente del comando que origina una escritura.</summary>
public sealed record CommandStamp(Guid CommandId, string CommandType, DateTimeOffset AtUtc);

/// <summary>Fila de la lista de partidos (lectura).</summary>
public sealed record MatchSummary(
    Guid MatchId,
    string TournamentName,
    string Label,
    string HomeName,
    string AwayName,
    MatchStatus Status,
    DateTimeOffset? ScheduledAtUtc,
    Guid TournamentId);

/// <summary>El partido cambió desde que se leyó (versión esperada distinta).</summary>
public sealed class ConcurrencyException(string message) : Exception(message);

/// <summary>Persistencia del agregado <see cref="Match"/>.</summary>
public interface IMatchRepository
{
    /// <summary>Carga el partido completo desde los datos persistidos, o nulo si no existe.</summary>
    Task<Match?> GetAsync(Guid matchId, CancellationToken ct = default);

    Task<IReadOnlyList<MatchSummary>> ListAsync(CancellationToken ct = default);

    /// <summary>Partido en juego, si hay uno. Solo puede haber un partido activo a la vez.</summary>
    Task<Guid?> FindInProgressMatchIdAsync(CancellationToken ct = default);

    Task<bool> IsCommandProcessedAsync(Guid commandId, CancellationToken ct = default);

    /// <summary>
    /// Escribe en una sola transacción los cambios pendientes, la auditoría y el identificador
    /// del comando, validando la versión esperada. Si falla no queda nada escrito.
    /// </summary>
    /// <exception cref="ConcurrencyException">La versión persistida no es la que cargó el agregado.</exception>
    Task SaveAsync(Match match, CommandStamp stamp, CancellationToken ct = default);
}

/// <summary>Lecturas del catálogo de eventos necesarias para operar partidos.</summary>
public interface ITournamentRepository
{
    Task<Tournament?> GetAsync(Guid tournamentId, CancellationToken ct = default);
}

/// <summary>Perfiles locales de operador.</summary>
public interface IOperatorRepository
{
    Task<IReadOnlyList<OperatorProfile>> ListAsync(CancellationToken ct = default);

    Task<OperatorProfile?> FindByNameAsync(string displayName, CancellationToken ct = default);

    Task AddAsync(OperatorProfile profile, DateTimeOffset createdAtUtc, CancellationToken ct = default);
}

/// <summary>Registro de diagnóstico local, acotado y sin nóminas ni datos personales.</summary>
public interface IDiagnosticLog
{
    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? exception = null);
}
