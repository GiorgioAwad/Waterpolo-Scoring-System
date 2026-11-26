using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;

namespace FDNDA.Waterpolo.Application.Matches;

// Comandos que cambian estado. Cada uno lleva un CommandId idempotente: reenviar el mismo
// comando no vuelve a aplicar el cambio. ExpectedVersion es la versión del snapshot que
// vio el operador; si el partido cambió, el comando se rechaza.

/// <summary>
/// Programa un partido. <paramref name="MatchId"/> lo genera la interfaz y sirve también de
/// identificador idempotente. Colores y gorros vacíos toman los del equipo en el catálogo.
/// </summary>
public sealed record ScheduleMatch(
    Guid MatchId,
    Guid TournamentId,
    string Label,
    DateTimeOffset? ScheduledAtUtc,
    Guid HomeTeamId,
    Guid AwayTeamId,
    string? HomeColorHex = null,
    string? HomeCapColor = null,
    string? AwayColorHex = null,
    string? AwayCapColor = null);

/// <summary>
/// Reprograma un partido aún no iniciado: descripción, fecha (nula = sin fecha) y color y gorro
/// de cada equipo. Los equipos no cambian.
/// </summary>
public sealed record RescheduleMatch(
    Guid CommandId,
    Guid MatchId,
    string Label,
    DateTimeOffset? ScheduledAtUtc,
    string HomeColorHex,
    string HomeCapColor,
    string AwayColorHex,
    string AwayCapColor,
    OperatorRef Operator,
    int? ExpectedVersion = null);

/// <summary>Copia al partido aún no iniciado la nómina vigente de sus equipos en el evento.</summary>
public sealed record RefreshMatchRoster(Guid CommandId, Guid MatchId, OperatorRef Operator, int? ExpectedVersion = null);

/// <summary>Anula un partido aún no iniciado; exige motivo. El partido no se borra.</summary>
public sealed record CancelMatch(Guid CommandId, Guid MatchId, string Reason, OperatorRef Operator, int? ExpectedVersion = null);

public sealed record StartMatch(Guid CommandId, Guid MatchId, OperatorRef Operator, int? ExpectedVersion = null);

public sealed record StartBreak(Guid CommandId, Guid MatchId, OperatorRef Operator, int? ExpectedVersion = null);

public sealed record StartNextPeriod(Guid CommandId, Guid MatchId, OperatorRef Operator, int? ExpectedVersion = null);

/// <summary>Inicia la tanda de penales (resultado empatado tras todos los periodos).</summary>
public sealed record StartShootout(Guid CommandId, Guid MatchId, OperatorRef Operator, int? ExpectedVersion = null);

/// <summary>Deshace una tanda iniciada por error; exige motivo y ningún lanzamiento vigente.</summary>
public sealed record CancelShootout(Guid CommandId, Guid MatchId, string Reason, OperatorRef Operator, int? ExpectedVersion = null);

public sealed record FinishMatch(Guid CommandId, Guid MatchId, OperatorRef Operator, int? ExpectedVersion = null);

public sealed record ReopenMatch(Guid CommandId, Guid MatchId, string Reason, OperatorRef Operator, int? ExpectedVersion = null);

public sealed record RegisterGoal(
    Guid CommandId,
    Guid MatchId,
    TeamSide Side,
    Guid RosterEntryId,
    OperatorRef Operator,
    int? ExpectedVersion = null);

public sealed record CorrectGoalScorer(
    Guid CommandId,
    Guid MatchId,
    Guid IncidentId,
    TeamSide NewSide,
    Guid NewRosterEntryId,
    string Reason,
    OperatorRef Operator,
    int? ExpectedVersion = null);

public sealed record VoidIncident(
    Guid CommandId,
    Guid MatchId,
    Guid IncidentId,
    string Reason,
    OperatorRef Operator,
    int? ExpectedVersion = null);

public sealed record RegisterIncident(
    Guid CommandId,
    Guid MatchId,
    IncidentType Type,
    TeamSide Side,
    Guid? RosterEntryId,
    OperatorRef Operator,
    int? ExpectedVersion = null,
    PenaltyResult? PenaltyResult = null,
    ExclusionKind? ExclusionKind = null);

public sealed record CorrectIncident(
    Guid CommandId,
    Guid MatchId,
    Guid IncidentId,
    TeamSide NewSide,
    Guid? NewRosterEntryId,
    string Reason,
    OperatorRef Operator,
    int? ExpectedVersion = null,
    PenaltyResult? PenaltyResult = null,
    ExclusionKind? ExclusionKind = null);

public enum CommandOutcome
{
    /// <summary>El cambio quedó escrito y se publicó el nuevo marcador.</summary>
    Applied = 1,

    /// <summary>El mismo comando ya se había aplicado; no se repitió.</summary>
    AlreadyApplied = 2,

    /// <summary>Una regla lo impide. Nada cambió.</summary>
    Rejected = 3,

    /// <summary>No se pudo escribir. Nada cambió y el marcador anterior se conserva.</summary>
    Failed = 4,
}

public sealed record CommandResult(CommandOutcome Outcome, string? Message, MatchSnapshot? Snapshot)
{
    public bool IsSuccess => Outcome is CommandOutcome.Applied or CommandOutcome.AlreadyApplied;
}
