using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;

namespace FDNDA.Waterpolo.Domain.Matches;

/// <summary>
/// Estado administrativo del partido: programado → en juego → finalizado. Un partido aún
/// programado puede quedar anulado (con motivo); no se borra.
/// </summary>
public enum MatchStatus
{
    Scheduled = 1,
    InProgress = 2,
    Finished = 3,
    Cancelled = 4,
}

/// <summary>
/// Etapa del partido. Es distinta de la pausa del reloj: la pausa se toma de la fuente
/// de cronometraje cuando está disponible y no se modela aquí.
/// </summary>
public enum MatchStage
{
    PreMatch = 1,
    Period = 2,
    Break = 3,
    Ended = 4,

    /// <summary>Tanda de penales: solo lanzamientos; no cambia el resultado reglamentario.</summary>
    Shootout = 5,
}

/// <summary>
/// Incidencias del partido. <see cref="ShootoutAttempt"/> es un lanzamiento de la tanda de penales:
/// se guarda y se corrige como las demás, pero nunca suma al resultado reglamentario.
/// </summary>
public enum IncidentType
{
    Goal = 1,
    Exclusion = 2,
    PersonalFoul = 3,
    Penalty = 4,
    Timeout = 5,
    ShootoutAttempt = 6,
}

public enum RevisionKind
{
    Original = 1,
    Correction = 2,
    Void = 3,
}

public enum PenaltyResult
{
    Converted = 1,
    NotConverted = 2,
}

/// <summary>Decisión registrada por la mesa, sin aplicar sanciones automáticas.</summary>
public enum ExclusionKind
{
    Exclusion = 1,
    Expulsion = 2,
}

public enum MatchStatusChangeKind
{
    Started = 1,
    BreakStarted = 2,
    PeriodStarted = 3,
    Finished = 4,
    Reopened = 5,
    ShootoutStarted = 6,

    /// <summary>La mesa deshace una tanda iniciada por error (sin lanzamientos vigentes).</summary>
    ShootoutCancelled = 7,

    /// <summary>Se cambió la descripción, la fecha, los colores o los gorros de un partido programado.</summary>
    Rescheduled = 8,

    /// <summary>Se reemplazó la nómina de un partido programado por la vigente del evento.</summary>
    RosterReplaced = 9,

    /// <summary>Se anuló un partido programado; exige motivo.</summary>
    Cancelled = 10,
}

/// <summary>Datos del equipo congelados en el partido para preservar el historial.</summary>
public sealed record MatchTeam
{
    public MatchTeam(Guid teamId, string code, string name, string colorHex, string capColor)
    {
        TeamId = Guard.NotEmpty(teamId, "equipo");
        Code = Guard.Text(code, "equipo.sin_codigo", "El equipo debe tener código.", 10);
        Name = Guard.Text(name, "equipo.sin_nombre", "El equipo debe tener nombre.", 80);
        ColorHex = Guard.ColorHex(colorHex);
        CapColor = Guard.Text(capColor, "equipo.sin_gorro", "Indique el color de gorro del equipo.", 30);
    }

    public Guid TeamId { get; }

    public string Code { get; }

    public string Name { get; }

    public string ColorHex { get; }

    public string CapColor { get; }

    public static MatchTeam From(Team team, string? capColorOverride = null) =>
        new(team.Id, team.Code, team.Name, team.ColorHex, capColorOverride ?? team.CapColor);
}

/// <summary>Jugador en la nómina del partido, con nombre y gorro congelados.</summary>
public sealed record MatchRosterEntry
{
    public MatchRosterEntry(Guid id, TeamSide side, Guid playerId, string playerName, CapNumber cap)
    {
        Id = Guard.NotEmpty(id, "nómina del partido");
        Side = side;
        PlayerId = Guard.NotEmpty(playerId, "jugador");
        PlayerName = Guard.Text(playerName, "jugador.sin_nombre", "El jugador debe tener nombre.", 80);
        Cap = cap;
    }

    public Guid Id { get; }

    public TeamSide Side { get; }

    public Guid PlayerId { get; }

    public string PlayerName { get; }

    public CapNumber Cap { get; }
}

/// <summary>
/// Revisión de una incidencia. La número 1 es el registro original; las siguientes son
/// correcciones o la anulación. El valor anterior de una revisión es la revisión previa.
/// </summary>
public sealed record IncidentRevision(
    int Number,
    RevisionKind Kind,
    TeamSide Side,
    Guid? RosterEntryId,
    string? Reason,
    OperatorRef Operator,
    DateTimeOffset AtUtc,
    PenaltyResult? PenaltyResult = null,
    ExclusionKind? ExclusionKind = null);

/// <summary>Cambio de estado o de etapa del partido, atribuido a un operador.</summary>
public sealed record MatchStatusChange(
    int Sequence,
    MatchStatusChangeKind Kind,
    MatchStatus FromStatus,
    MatchStatus ToStatus,
    MatchStage FromStage,
    MatchStage ToStage,
    int Period,
    string? Reason,
    OperatorRef Operator,
    DateTimeOffset AtUtc);

/// <summary>Resultado reglamentario derivado de las incidencias vigentes.</summary>
public readonly record struct MatchScore(int Home, int Away);
