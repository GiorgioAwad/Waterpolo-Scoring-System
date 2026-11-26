using FDNDA.Waterpolo.Domain.Common;

namespace FDNDA.Waterpolo.Domain.Matches;

/// <summary>
/// Incidencia confirmada de un partido. Nunca se borra: se corrige o anula con una
/// nueva revisión y el marcador se deriva de la última revisión válida.
/// </summary>
public sealed class MatchIncident
{
    private readonly List<IncidentRevision> _revisions;

    internal MatchIncident(
        Guid id,
        int sequence,
        IncidentType type,
        PeriodNumber period,
        ClockContext clock,
        DateTimeOffset registeredAtUtc,
        IEnumerable<IncidentRevision> revisions)
    {
        Id = id;
        Sequence = sequence;
        Type = type;
        Period = period;
        Clock = clock;
        RegisteredAtUtc = registeredAtUtc;
        _revisions = revisions.OrderBy(r => r.Number).ToList();

        if (_revisions.Count == 0 || _revisions[0].Kind != RevisionKind.Original || _revisions[0].Number != 1)
        {
            throw new DomainException("incidencia.sin_original", "La incidencia no tiene su registro original.");
        }
    }

    public Guid Id { get; }

    /// <summary>Secuencia dentro del partido (1 en adelante).</summary>
    public int Sequence { get; }

    public IncidentType Type { get; }

    public PeriodNumber Period { get; }

    public ClockContext Clock { get; }

    public DateTimeOffset RegisteredAtUtc { get; }

    public IReadOnlyList<IncidentRevision> Revisions => _revisions;

    public IncidentRevision Original => _revisions[0];

    /// <summary>Última revisión: define el valor vigente de la incidencia.</summary>
    public IncidentRevision Effective => _revisions[^1];

    public bool IsVoided => Effective.Kind == RevisionKind.Void;

    /// <summary>Un penal convertido es una sola incidencia que aporta un solo gol.</summary>
    public bool CountsAsGoal => !IsVoided &&
        (Type == IncidentType.Goal || Type == IncidentType.Penalty && Effective.PenaltyResult == PenaltyResult.Converted);

    internal IncidentRevision AddRevision(
        RevisionKind kind,
        TeamSide side,
        Guid? rosterEntryId,
        string reason,
        OperatorRef @operator,
        DateTimeOffset atUtc,
        PenaltyResult? penaltyResult = null,
        ExclusionKind? exclusionKind = null)
    {
        var revision = new IncidentRevision(_revisions.Count + 1, kind, side, rosterEntryId, reason, @operator, atUtc,
            penaltyResult, exclusionKind);
        _revisions.Add(revision);
        return revision;
    }
}
