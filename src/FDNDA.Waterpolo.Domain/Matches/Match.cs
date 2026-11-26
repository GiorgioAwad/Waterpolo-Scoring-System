using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;

namespace FDNDA.Waterpolo.Domain.Matches;

/// <summary>
/// Agregado del partido: estado, etapa, nómina congelada, reglas copiadas e incidencias.
/// Todas las reglas de operación de mesa viven aquí; no conoce UI, SQLite ni hardware.
/// </summary>
public sealed class Match
{
    private readonly List<MatchRosterEntry> _roster;
    private readonly List<MatchIncident> _incidents;
    private readonly List<MatchStatusChange> _statusChanges;
    private readonly MatchPendingChanges _pending = new();

    private Match(MatchState state, bool isNew)
    {
        Id = Guard.NotEmpty(state.Id, "partido");
        TournamentId = Guard.NotEmpty(state.TournamentId, "evento");
        TournamentName = Guard.Text(state.TournamentName, "evento.sin_nombre", "El evento debe tener nombre.", 120);
        Label = CleanLabel(state.Label);
        ScheduledAtUtc = state.ScheduledAtUtc;
        Home = state.Home;
        Away = state.Away;
        Status = state.Status;
        Stage = state.Stage;
        CurrentPeriod = state.CurrentPeriod;
        Ruleset = state.Ruleset;
        Version = state.Version;
        _roster = state.Roster.ToList();
        _incidents = state.Incidents.OrderBy(i => i.Sequence).ToList();
        _statusChanges = state.StatusChanges.OrderBy(c => c.Sequence).ToList();
        _pending.IsNew = isNew;

        ValidateTeamsAndRoster();
    }

    public Guid Id { get; }

    public Guid TournamentId { get; }

    /// <summary>Nombre del evento congelado al programar el partido.</summary>
    public string TournamentName { get; }

    /// <summary>Descripción del partido; cambia solo al reprogramarlo antes de iniciarse.</summary>
    public string Label { get; private set; }

    public DateTimeOffset? ScheduledAtUtc { get; private set; }

    /// <summary>Equipo local congelado; su color y gorro cambian solo al reprogramar.</summary>
    public MatchTeam Home { get; private set; }

    public MatchTeam Away { get; private set; }

    public MatchStatus Status { get; private set; }

    public MatchStage Stage { get; private set; }

    /// <summary>Periodo actual; 0 mientras el partido no ha iniciado.</summary>
    public int CurrentPeriod { get; private set; }

    /// <summary>Reglas copiadas del evento al iniciar; nulo mientras está programado.</summary>
    public RulesetSnapshot? Ruleset { get; private set; }

    /// <summary>Versión persistida para control de concurrencia optimista.</summary>
    public int Version { get; private set; }

    public IReadOnlyList<MatchRosterEntry> Roster => _roster;

    public IReadOnlyList<MatchIncident> Incidents => _incidents;

    public IReadOnlyList<MatchStatusChange> StatusChanges => _statusChanges;

    /// <summary>Cambios aún no persistidos, para que el repositorio los escriba en una transacción.</summary>
    public MatchPendingChanges PendingChanges => _pending;

    /// <summary>Resultado reglamentario: goles vigentes (no anulados) por lado.</summary>
    public MatchScore Score
    {
        get
        {
            var home = 0;
            var away = 0;
            foreach (var incident in _incidents)
            {
                if (!incident.CountsAsGoal)
                {
                    continue;
                }

                if (incident.Effective.Side == TeamSide.Home)
                {
                    home++;
                }
                else
                {
                    away++;
                }
            }

            return new MatchScore(home, away);
        }
    }

    /// <summary>
    /// Verdadero si el partido entró en la tanda de penales y no se anuló: en la etapa de tanda
    /// o finalizado desde ella.
    /// </summary>
    public bool HasShootout =>
        _statusChanges.LastOrDefault(c => c.Kind is MatchStatusChangeKind.ShootoutStarted or MatchStatusChangeKind.ShootoutCancelled)
            ?.Kind == MatchStatusChangeKind.ShootoutStarted;

    /// <summary>
    /// Resultado de la tanda: lanzamientos convertidos vigentes por lado. Es independiente del
    /// resultado reglamentario (<see cref="Score"/>), que no cambia durante la tanda.
    /// </summary>
    public MatchScore ShootoutScore
    {
        get
        {
            var home = 0;
            var away = 0;
            foreach (var attempt in ShootoutAttempts)
            {
                if (attempt.IsVoided || attempt.Effective.PenaltyResult != PenaltyResult.Converted)
                {
                    continue;
                }

                if (attempt.Effective.Side == TeamSide.Home)
                {
                    home++;
                }
                else
                {
                    away++;
                }
            }

            return new MatchScore(home, away);
        }
    }

    /// <summary>Lanzamientos de la tanda en orden de registro, incluidos los anulados.</summary>
    public IReadOnlyList<MatchIncident> ShootoutAttempts =>
        _incidents.Where(i => i.Type == IncidentType.ShootoutAttempt).ToList();

    /// <summary>Verdadero si el periodo en curso es de prórroga.</summary>
    public bool IsInOvertime => Ruleset is { } rules && CurrentPeriod > 0 && rules.IsOvertime(CurrentPeriod);

    /// <summary>Nulo si la tanda se puede iniciar ahora; si no, el motivo para mostrar a la mesa.</summary>
    public string? ShootoutBlocker
    {
        get
        {
            if (Status != MatchStatus.InProgress)
            {
                return "La tanda solo se inicia con el partido en juego.";
            }

            if (Stage is not (MatchStage.Period or MatchStage.Break))
            {
                return Stage == MatchStage.Shootout ? "La tanda ya está en curso." : "La tanda se inicia al terminar el último periodo.";
            }

            var rules = Ruleset!;
            if (!rules.PenaltyShootoutEnabled)
            {
                return "Las reglas de este partido no tienen tanda de penales.";
            }

            if (CurrentPeriod < rules.MaxPeriods)
            {
                return rules.OvertimePeriodCount > 0
                    ? "La tanda se inicia después de jugar los periodos reglamentarios y la prórroga."
                    : "La tanda se inicia después de jugar todos los periodos reglamentarios.";
            }

            var score = Score;
            return score.Home != score.Away ? "La tanda solo se juega con el resultado empatado." : null;
        }
    }

    public bool CanStartShootout => ShootoutBlocker is null;

    /// <summary>Programa un partido nuevo con equipos y nómina congelados.</summary>
    public static Match Schedule(
        Guid id,
        Guid tournamentId,
        string tournamentName,
        string label,
        DateTimeOffset? scheduledAtUtc,
        MatchTeam home,
        MatchTeam away,
        IEnumerable<MatchRosterEntry> roster)
    {
        var state = new MatchState(
            id,
            tournamentId,
            tournamentName,
            label,
            scheduledAtUtc,
            home ?? throw new ArgumentNullException(nameof(home)),
            away ?? throw new ArgumentNullException(nameof(away)),
            MatchStatus.Scheduled,
            MatchStage.PreMatch,
            CurrentPeriod: 0,
            Ruleset: null,
            Version: 0,
            (roster ?? throw new ArgumentNullException(nameof(roster))).ToList(),
            [],
            []);

        return new Match(state, isNew: true);
    }

    /// <summary>Reconstruye el agregado desde datos persistidos. Uso exclusivo de repositorios.</summary>
    public static Match Rehydrate(MatchState state) => new(state, isNew: false);

    /// <summary>Crea una incidencia desde datos persistidos. Uso exclusivo de repositorios.</summary>
    public static MatchIncident RehydrateIncident(
        Guid id,
        int sequence,
        IncidentType type,
        PeriodNumber period,
        ClockContext clock,
        DateTimeOffset registeredAtUtc,
        IEnumerable<IncidentRevision> revisions) =>
        new(id, sequence, type, period, clock, registeredAtUtc, revisions);

    public MatchRosterEntry? FindRosterEntry(Guid rosterEntryId) =>
        _roster.FirstOrDefault(r => r.Id == rosterEntryId);

    public MatchIncident? FindIncident(Guid incidentId) =>
        _incidents.FirstOrDefault(i => i.Id == incidentId);

    /// <summary>Inicia el partido y copia las reglas del evento.</summary>
    public void Start(RulesetSnapshot rules, OperatorRef @operator, DateTimeOffset atUtc)
    {
        ArgumentNullException.ThrowIfNull(rules);
        RequireNotStarted();
        Ruleset = rules;
        RecordChange(MatchStatusChangeKind.Started, MatchStatus.InProgress, MatchStage.Period, 1, null, @operator, atUtc);
    }

    /// <summary>
    /// Reprograma un partido aún no iniciado: descripción, fecha (puede quedar vacía) y color y
    /// gorro de cada equipo. Los equipos no cambian: para eso se anula y se programa otro.
    /// </summary>
    public void Reschedule(
        string label,
        DateTimeOffset? scheduledAtUtc,
        string homeColorHex,
        string homeCapColor,
        string awayColorHex,
        string awayCapColor,
        OperatorRef @operator,
        DateTimeOffset atUtc)
    {
        ArgumentNullException.ThrowIfNull(@operator);
        RequireNotStarted();
        var newLabel = CleanLabel(label);
        var home = new MatchTeam(Home.TeamId, Home.Code, Home.Name, homeColorHex, homeCapColor);
        var away = new MatchTeam(Away.TeamId, Away.Code, Away.Name, awayColorHex, awayCapColor);
        if (newLabel == Label && scheduledAtUtc == ScheduledAtUtc && home == Home && away == Away)
        {
            throw new DomainException("partido.sin_cambio", "La reprogramación no cambia ningún dato del partido.");
        }

        Label = newLabel;
        ScheduledAtUtc = scheduledAtUtc;
        Home = home;
        Away = away;
        _pending.HeaderChanged = true;
        RecordChange(MatchStatusChangeKind.Rescheduled, Status, Stage, CurrentPeriod, null, @operator, atUtc);
    }

    /// <summary>
    /// Reemplaza la nómina congelada de un partido aún no iniciado (por ejemplo, con la nómina
    /// vigente del evento). Sin incidencias, ninguna referencia a la nómina anterior se pierde.
    /// </summary>
    public void ReplaceRoster(IEnumerable<MatchRosterEntry> roster, OperatorRef @operator, DateTimeOffset atUtc)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(@operator);
        RequireNotStarted();
        var replacement = roster.ToList();
        if (RosterKey(replacement).SetEquals(RosterKey(_roster)))
        {
            throw new DomainException("partido.sin_cambio", "La nómina del partido ya coincide con la del evento.");
        }

        var previous = _roster.ToList();
        _roster.Clear();
        _roster.AddRange(replacement);
        try
        {
            ValidateTeamsAndRoster();
        }
        catch (DomainException)
        {
            _roster.Clear();
            _roster.AddRange(previous);
            throw;
        }

        _pending.RosterReplaced = true;
        RecordChange(MatchStatusChangeKind.RosterReplaced, Status, Stage, CurrentPeriod, null, @operator, atUtc);
    }

    /// <summary>
    /// Anula un partido aún no iniciado. Exige motivo; el partido y su historial se conservan,
    /// pero ya no se puede iniciar ni cuenta en los resultados.
    /// </summary>
    public void Cancel(string reason, OperatorRef @operator, DateTimeOffset atUtc)
    {
        ArgumentNullException.ThrowIfNull(@operator);
        RequireNotStarted();
        var cleanReason = RequireReason(reason);
        RecordChange(MatchStatusChangeKind.Cancelled, MatchStatus.Cancelled, Stage, CurrentPeriod, cleanReason, @operator, atUtc);
    }

    /// <summary>Pasa del periodo en curso al descanso.</summary>
    public void StartBreak(OperatorRef @operator, DateTimeOffset atUtc)
    {
        RequireInProgress();
        if (Stage != MatchStage.Period)
        {
            throw new DomainException("partido.etapa_invalida", "Solo se puede iniciar el descanso durante un periodo.");
        }

        RecordChange(MatchStatusChangeKind.BreakStarted, Status, MatchStage.Break, CurrentPeriod, null, @operator, atUtc);
    }

    /// <summary>Pasa del descanso al siguiente periodo reglamentario.</summary>
    public void StartNextPeriod(OperatorRef @operator, DateTimeOffset atUtc)
    {
        RequireInProgress();
        if (Stage != MatchStage.Break)
        {
            throw new DomainException("partido.etapa_invalida", "El siguiente periodo se inicia desde el descanso.");
        }

        var rules = Ruleset!;
        if (CurrentPeriod >= rules.MaxPeriods)
        {
            throw new DomainException(
                "partido.sin_mas_periodos",
                rules.OvertimePeriodCount > 0
                    ? "Ya se jugaron los periodos reglamentarios y la prórroga."
                    : "No quedan periodos reglamentarios y las reglas de este partido no tienen prórroga.");
        }

        // Se entra en la prórroga solo con empate; sus periodos se juegan completos.
        var score = Score;
        if (CurrentPeriod == rules.PeriodCount && score.Home != score.Away)
        {
            throw new DomainException(
                "partido.prorroga_requiere_empate",
                "No quedan periodos reglamentarios. La prórroga solo se juega con el resultado empatado.");
        }

        RecordChange(MatchStatusChangeKind.PeriodStarted, Status, MatchStage.Period, CurrentPeriod + 1, null, @operator, atUtc);
    }

    /// <summary>
    /// Inicia la tanda de penales: con tanda en las reglas, todos los periodos (incluida la
    /// prórroga) jugados y el resultado empatado. No decide quién lanza ni cuándo termina.
    /// </summary>
    public void StartShootout(OperatorRef @operator, DateTimeOffset atUtc)
    {
        RequireInProgress();
        if (ShootoutBlocker is { } blocker)
        {
            throw new DomainException("partido.tanda_no_disponible", blocker);
        }

        RecordChange(MatchStatusChangeKind.ShootoutStarted, Status, MatchStage.Shootout, CurrentPeriod, null, @operator, atUtc);
    }

    /// <summary>
    /// Deshace una tanda iniciada por error y vuelve al descanso. Exige motivo y que no quede
    /// ningún lanzamiento vigente (se anulan antes, cada uno con su motivo).
    /// </summary>
    public void CancelShootout(string reason, OperatorRef @operator, DateTimeOffset atUtc)
    {
        RequireInProgress();
        if (Stage != MatchStage.Shootout)
        {
            throw new DomainException("partido.sin_tanda", "No hay una tanda en curso.");
        }

        var cleanReason = RequireReason(reason);
        if (ShootoutAttempts.Any(a => !a.IsVoided))
        {
            throw new DomainException(
                "partido.tanda_con_lanzamientos",
                "La tanda tiene lanzamientos vigentes. Anúlelos uno por uno antes de anular la tanda.");
        }

        RecordChange(MatchStatusChangeKind.ShootoutCancelled, Status, MatchStage.Break, CurrentPeriod, cleanReason, @operator, atUtc);
    }

    public void Finish(OperatorRef @operator, DateTimeOffset atUtc)
    {
        RequireInProgress();
        var shootout = ShootoutScore;
        if (Stage == MatchStage.Shootout && shootout.Home == shootout.Away)
        {
            throw new DomainException(
                "partido.tanda_empatada",
                "La tanda está empatada. Registre los lanzamientos que faltan antes de finalizar.");
        }

        RecordChange(MatchStatusChangeKind.Finished, MatchStatus.Finished, MatchStage.Ended, CurrentPeriod, null, @operator, atUtc);
    }

    /// <summary>Reabre un partido finalizado. Exige motivo y vuelve a la etapa previa al cierre.</summary>
    public void Reopen(string reason, OperatorRef @operator, DateTimeOffset atUtc)
    {
        if (Status != MatchStatus.Finished)
        {
            throw new DomainException("partido.no_finalizado", "Solo se puede reabrir un partido finalizado.");
        }

        var cleanReason = RequireReason(reason);
        var closing = _statusChanges.LastOrDefault(c => c.Kind == MatchStatusChangeKind.Finished);
        var stage = closing?.FromStage is MatchStage.Period or MatchStage.Break or MatchStage.Shootout ? closing.FromStage : MatchStage.Break;
        RecordChange(MatchStatusChangeKind.Reopened, MatchStatus.InProgress, stage, CurrentPeriod, cleanReason, @operator, atUtc);
    }

    /// <summary>
    /// Registra un gol de un jugador de la nómina del partido. El periodo es el actual
    /// (durante un descanso, el periodo recién jugado).
    /// </summary>
    public MatchIncident RegisterGoal(
        Guid incidentId,
        TeamSide side,
        Guid rosterEntryId,
        ClockContext clock,
        OperatorRef @operator,
        DateTimeOffset atUtc) =>
        RegisterIncident(incidentId, IncidentType.Goal, side, rosterEntryId, clock, @operator, atUtc);

    /// <summary>Registra una decisión de mesa; no aplica sanciones ni modifica relojes.</summary>
    public MatchIncident RegisterIncident(
        Guid incidentId,
        IncidentType type,
        TeamSide side,
        Guid? rosterEntryId,
        ClockContext clock,
        OperatorRef @operator,
        DateTimeOffset atUtc,
        PenaltyResult? penaltyResult = null,
        ExclusionKind? exclusionKind = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(@operator);
        RequireInProgress();
        Guard.NotEmpty(incidentId, "incidencia");

        if (_incidents.Any(i => i.Id == incidentId))
        {
            throw new DomainException("incidencia.duplicada", "Esta incidencia ya fue registrada.");
        }

        ValidateIncidentData(type, side, rosterEntryId, penaltyResult, exclusionKind);
        if (type == IncidentType.ShootoutAttempt && Stage != MatchStage.Shootout)
        {
            throw new DomainException(
                "incidencia.fuera_de_tanda",
                "Los lanzamientos de la tanda se registran después de iniciar la tanda de penales.");
        }

        if (Stage == MatchStage.Shootout && type is IncidentType.Goal or IncidentType.Penalty)
        {
            throw new DomainException(
                "incidencia.gol_en_tanda",
                "Durante la tanda no se registran goles ni penales del juego: registre lanzamientos de la tanda.");
        }

        var original = new IncidentRevision(1, RevisionKind.Original, side, rosterEntryId, null, @operator, atUtc,
            penaltyResult, exclusionKind);
        var incident = new MatchIncident(
            incidentId,
            _incidents.Count + 1,
            type,
            new PeriodNumber(CurrentPeriod),
            clock,
            atUtc,
            [original]);

        _incidents.Add(incident);
        _pending.NewIncidents.Add(incident);
        return incident;
    }

    /// <summary>Corrige el equipo o goleador de un gol. Conserva la revisión anterior.</summary>
    public IncidentRevision CorrectGoalScorer(
        Guid incidentId,
        TeamSide newSide,
        Guid newRosterEntryId,
        string reason,
        OperatorRef @operator,
        DateTimeOffset atUtc)
    {
        ArgumentNullException.ThrowIfNull(@operator);
        RequireInProgress();
        var incident = RequireActiveIncident(incidentId);
        if (incident.Type != IncidentType.Goal)
        {
            throw new DomainException("incidencia.tipo_invalido", "Solo se puede corregir el goleador de un gol.");
        }

        return CorrectIncident(incidentId, newSide, newRosterEntryId, reason, @operator, atUtc);
    }

    /// <summary>Corrige equipo, jugador o detalle del tipo original mediante una revisión nueva.</summary>
    public IncidentRevision CorrectIncident(
        Guid incidentId,
        TeamSide newSide,
        Guid? newRosterEntryId,
        string reason,
        OperatorRef @operator,
        DateTimeOffset atUtc,
        PenaltyResult? penaltyResult = null,
        ExclusionKind? exclusionKind = null)
    {
        ArgumentNullException.ThrowIfNull(@operator);
        RequireInProgress();
        var incident = RequireActiveIncident(incidentId);
        var cleanReason = RequireReason(reason);
        ValidateIncidentData(incident.Type, newSide, newRosterEntryId, penaltyResult, exclusionKind);

        var current = incident.Effective;
        if (current.Side == newSide && current.RosterEntryId == newRosterEntryId &&
            current.PenaltyResult == penaltyResult && current.ExclusionKind == exclusionKind)
        {
            throw new DomainException("incidencia.sin_cambio", "La corrección no cambia los datos registrados.");
        }

        RequireTiedWithShootout(incident, newSide, CountsAsGoal(incident.Type, penaltyResult));
        var revision = incident.AddRevision(RevisionKind.Correction, newSide, newRosterEntryId, cleanReason, @operator, atUtc,
            penaltyResult, exclusionKind);
        _pending.NewRevisions.Add(new PendingRevision(incident.Id, revision));
        return revision;
    }

    /// <summary>Anula una incidencia. No hay borrado físico: queda una revisión de anulación.</summary>
    public IncidentRevision VoidIncident(Guid incidentId, string reason, OperatorRef @operator, DateTimeOffset atUtc)
    {
        ArgumentNullException.ThrowIfNull(@operator);
        RequireInProgress();
        var incident = RequireActiveIncident(incidentId);
        var cleanReason = RequireReason(reason);

        var current = incident.Effective;
        RequireTiedWithShootout(incident, current.Side, countsAsGoal: false);
        var revision = incident.AddRevision(RevisionKind.Void, current.Side, current.RosterEntryId, cleanReason, @operator, atUtc,
            current.PenaltyResult, current.ExclusionKind);
        _pending.NewRevisions.Add(new PendingRevision(incident.Id, revision));
        return revision;
    }

    /// <summary>El repositorio confirma que los cambios pendientes quedaron escritos.</summary>
    public void MarkPersisted(int newVersion)
    {
        Version = newVersion;
        _pending.Clear();
    }

    private void RecordChange(
        MatchStatusChangeKind kind,
        MatchStatus toStatus,
        MatchStage toStage,
        int period,
        string? reason,
        OperatorRef @operator,
        DateTimeOffset atUtc)
    {
        ArgumentNullException.ThrowIfNull(@operator);
        var change = new MatchStatusChange(
            _statusChanges.Count + 1, kind, Status, toStatus, Stage, toStage, period, reason, @operator, atUtc);

        Status = toStatus;
        Stage = toStage;
        CurrentPeriod = period;
        _statusChanges.Add(change);
        _pending.NewStatusChanges.Add(change);
    }

    /// <summary>Solo un partido programado (ni iniciado ni anulado) se inicia, reprograma o anula.</summary>
    private void RequireNotStarted()
    {
        switch (Status)
        {
            case MatchStatus.Scheduled:
                return;
            case MatchStatus.Cancelled:
                throw CancelledError();
            default:
                throw new DomainException("partido.ya_iniciado", "El partido ya fue iniciado: solo se cambia antes de iniciarse.");
        }
    }

    private static DomainException CancelledError() =>
        new("partido.anulado", "El partido está anulado: no se puede iniciar ni modificar.");

    private static string CleanLabel(string label) =>
        Guard.Text(label, "partido.sin_nombre", "El partido debe tener una descripción.", 80);

    private static HashSet<(TeamSide, Guid, string, int)> RosterKey(IEnumerable<MatchRosterEntry> roster) =>
        roster.Select(r => (r.Side, r.PlayerId, r.PlayerName, r.Cap.Value)).ToHashSet();

    private void RequireInProgress()
    {
        switch (Status)
        {
            case MatchStatus.Cancelled:
                throw CancelledError();
            case MatchStatus.Scheduled:
                throw new DomainException("partido.no_iniciado", "El partido aún no ha iniciado.");
            case MatchStatus.Finished:
                throw new DomainException(
                    "partido.finalizado",
                    "El partido está finalizado. Reábralo con un motivo para registrar cambios.");
        }
    }

    private void ValidateIncidentData(IncidentType type, TeamSide side, Guid? rosterEntryId,
        PenaltyResult? penaltyResult, ExclusionKind? exclusionKind)
    {
        if (!Enum.IsDefined(type) || !Enum.IsDefined(side))
        {
            throw new DomainException("incidencia.tipo_invalido", "Indique un tipo de incidencia y un equipo válidos.");
        }

        if (type == IncidentType.Timeout)
        {
            if (rosterEntryId is not null)
            {
                throw new DomainException("incidencia.sin_jugador", "El tiempo muerto se registra por equipo, sin jugador.");
            }
        }
        else
        {
            if (rosterEntryId is null)
            {
                throw new DomainException("incidencia.jugador_requerido", "Seleccione un jugador de la nómina del equipo.");
            }

            RequireRosterEntry(side, rosterEntryId.Value);
        }

        if (type is IncidentType.Penalty or IncidentType.ShootoutAttempt
            ? penaltyResult is null || !Enum.IsDefined(penaltyResult.Value)
            : penaltyResult is not null)
        {
            throw new DomainException(
                "incidencia.resultado_penal_invalido",
                "Solo el penal y el lanzamiento de la tanda indican resultado: convertido o no convertido.");
        }

        if (type == IncidentType.Exclusion
            ? exclusionKind is null || !Enum.IsDefined(exclusionKind.Value)
            : exclusionKind is not null)
        {
            throw new DomainException("incidencia.exclusion_invalida", "Solo la exclusión debe indicar si es exclusión o expulsión.");
        }
    }

    private static bool CountsAsGoal(IncidentType type, PenaltyResult? penaltyResult) =>
        type == IncidentType.Goal || type == IncidentType.Penalty && penaltyResult == PenaltyResult.Converted;

    /// <summary>
    /// Con una tanda en curso o jugada, el resultado reglamentario tiene que seguir empatado: si
    /// una corrección o anulación lo desempatara, la tanda dejaría de tener sentido. La mesa debe
    /// anular antes los lanzamientos y la tanda.
    /// </summary>
    private void RequireTiedWithShootout(MatchIncident incident, TeamSide newSide, bool countsAsGoal)
    {
        if (!HasShootout || incident.Type == IncidentType.ShootoutAttempt)
        {
            return;
        }

        var score = Score;
        var home = score.Home;
        var away = score.Away;
        if (incident.CountsAsGoal)
        {
            if (incident.Effective.Side == TeamSide.Home)
            {
                home--;
            }
            else
            {
                away--;
            }
        }

        if (countsAsGoal)
        {
            if (newSide == TeamSide.Home)
            {
                home++;
            }
            else
            {
                away++;
            }
        }

        if (home != away)
        {
            throw new DomainException(
                "partido.tanda_requiere_empate",
                "Hay una tanda de penales: este cambio desempataría el resultado reglamentario. Anule antes los lanzamientos y la tanda.");
        }
    }

    private MatchRosterEntry RequireRosterEntry(TeamSide side, Guid rosterEntryId)
    {
        var entry = FindRosterEntry(rosterEntryId);
        if (entry is null)
        {
            throw new DomainException("jugador.ajeno_a_nomina", "El jugador no pertenece a la nómina de este partido.");
        }

        if (entry.Side != side)
        {
            throw new DomainException("jugador.ajeno_al_equipo", "El jugador no pertenece al equipo indicado.");
        }

        return entry;
    }

    private MatchIncident RequireActiveIncident(Guid incidentId)
    {
        var incident = FindIncident(incidentId)
            ?? throw new DomainException("incidencia.no_existe", "La incidencia no existe en este partido.");

        if (incident.IsVoided)
        {
            throw new DomainException("incidencia.anulada", "La incidencia ya está anulada.");
        }

        return incident;
    }

    private static string RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("motivo.requerido", "Indique el motivo del cambio.");
        }

        var trimmed = reason.Trim();
        if (trimmed.Length > 300)
        {
            throw new DomainException("motivo.demasiado_largo", "El motivo supera los 300 caracteres.");
        }

        return trimmed;
    }

    private void ValidateTeamsAndRoster()
    {
        if (Home.TeamId == Away.TeamId)
        {
            throw new DomainException("partido.mismo_equipo", "Local y visita deben ser equipos distintos.");
        }

        var duplicatedCap = _roster.GroupBy(r => (r.Side, r.Cap)).FirstOrDefault(g => g.Count() > 1);
        if (duplicatedCap is not null)
        {
            throw new DomainException(
                "nomina.gorro_duplicado",
                $"El gorro {duplicatedCap.Key.Cap} está repetido en el mismo equipo.");
        }

        if (_roster.GroupBy(r => r.Id).Any(g => g.Count() > 1) || _roster.GroupBy(r => r.PlayerId).Any(g => g.Count() > 1))
        {
            throw new DomainException("nomina.jugador_duplicado", "Un jugador aparece más de una vez en la nómina del partido.");
        }
    }
}

/// <summary>Estado completo del partido para programarlo o reconstruirlo.</summary>
public sealed record MatchState(
    Guid Id,
    Guid TournamentId,
    string TournamentName,
    string Label,
    DateTimeOffset? ScheduledAtUtc,
    MatchTeam Home,
    MatchTeam Away,
    MatchStatus Status,
    MatchStage Stage,
    int CurrentPeriod,
    RulesetSnapshot? Ruleset,
    int Version,
    IReadOnlyList<MatchRosterEntry> Roster,
    IReadOnlyList<MatchIncident> Incidents,
    IReadOnlyList<MatchStatusChange> StatusChanges);

public sealed record PendingRevision(Guid IncidentId, IncidentRevision Revision);

/// <summary>Cambios del agregado pendientes de escritura.</summary>
public sealed class MatchPendingChanges
{
    /// <summary>Verdadero si el partido aún no existe en el almacenamiento.</summary>
    public bool IsNew { get; internal set; }

    public List<MatchIncident> NewIncidents { get; } = [];

    public List<PendingRevision> NewRevisions { get; } = [];

    public List<MatchStatusChange> NewStatusChanges { get; } = [];

    /// <summary>Cambió la descripción, la fecha o los colores y gorros (reprogramación).</summary>
    public bool HeaderChanged { get; internal set; }

    /// <summary>La nómina congelada se reemplazó entera (solo antes de iniciar).</summary>
    public bool RosterReplaced { get; internal set; }

    public bool HasChanges => IsNew || HeaderChanged || RosterReplaced
        || NewIncidents.Count > 0 || NewRevisions.Count > 0 || NewStatusChanges.Count > 0;

    internal void Clear()
    {
        IsNew = false;
        HeaderChanged = false;
        RosterReplaced = false;
        NewIncidents.Clear();
        NewRevisions.Clear();
        NewStatusChanges.Clear();
    }
}
