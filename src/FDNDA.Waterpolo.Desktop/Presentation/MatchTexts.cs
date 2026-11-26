using System.Globalization;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;

namespace FDNDA.Waterpolo.Desktop.Presentation;

/// <summary>Textos en español compartidos por la mesa y el marcador. Presentación es-PE.</summary>
internal static class MatchTexts
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-PE");

    public static string Status(MatchStatus status) => status switch
    {
        MatchStatus.Scheduled => "Programado",
        MatchStatus.InProgress => "En juego",
        MatchStatus.Finished => "Finalizado",
        MatchStatus.Cancelled => "Anulado",
        _ => status.ToString(),
    };

    /// <summary>Etapa para la mesa.</summary>
    public static string DeskStage(MatchSnapshot snapshot) => snapshot.Status switch
    {
        MatchStatus.Scheduled => "PROGRAMADO",
        MatchStatus.Finished => "FINALIZADO",
        MatchStatus.Cancelled => "ANULADO",
        _ when snapshot.Stage == MatchStage.Shootout => "TANDA DE PENALES",
        _ when snapshot.Stage == MatchStage.Break => $"DESCANSO · tras {Period(snapshot).ToLower(Culture)}",
        _ => Period(snapshot),
    };

    /// <summary>Etapa para el público.</summary>
    public static string PublicStage(MatchSnapshot snapshot) => snapshot.Status switch
    {
        MatchStatus.Scheduled => "PRÓXIMO PARTIDO",
        MatchStatus.Finished => "RESULTADO FINAL",
        MatchStatus.Cancelled => "PARTIDO ANULADO",
        _ when snapshot.Stage == MatchStage.Shootout => "PENALES",
        _ when snapshot.Stage == MatchStage.Break => "DESCANSO",
        _ => Period(snapshot),
    };

    /// <summary>«PERIODO 3» o, pasados los reglamentarios, «PRÓRROGA 1».</summary>
    public static string Period(MatchSnapshot snapshot) => snapshot.IsOvertime
        ? $"PRÓRROGA {snapshot.OvertimeNumber}"
        : $"PERIODO {snapshot.CurrentPeriod}";

    /// <summary>Periodo de una incidencia para el historial: «P3», «Pr1» o «Tanda».</summary>
    public static string IncidentPeriod(MatchSnapshot snapshot, IncidentSnapshot incident) =>
        incident.Type == IncidentType.ShootoutAttempt ? "Tanda"
        : snapshot.Rules is { } rules && incident.Period > rules.PeriodCount ? $"Pr{incident.Period - rules.PeriodCount}"
        : $"P{incident.Period}";

    /// <summary>Resultado de la tanda para la mesa, o vacío si no la hay.</summary>
    public static string Shootout(MatchSnapshot snapshot) => snapshot.Shootout is { } s
        ? $"Penales: {snapshot.Home.Code} {s.HomeScore} – {s.AwayScore} {snapshot.Away.Code}"
        : string.Empty;

    public static string Signal(TimingView view) => view.Signal switch
    {
        SignalState.Live => "Señal de tiempo vigente",
        SignalState.Lost => "SIN SEÑAL DE TIEMPO",
        SignalState.Waiting => "Esperando datos de la fuente de tiempo",
        SignalState.Unavailable => "FUENTE DE TIEMPO NO DISPONIBLE",
        SignalState.Intermission => "Descanso: la consola no envía tiempos",
        _ => view.Signal.ToString(),
    };

    public static string Running(TimingView view) => view.IsRunning switch
    {
        true => "Reloj en marcha",
        false => "Reloj detenido",
        null => string.Empty,
    };

    public static string Revision(RevisionKind kind) => kind switch
    {
        RevisionKind.Original => "Registro",
        RevisionKind.Correction => "Corrección",
        RevisionKind.Void => "Anulación",
        _ => kind.ToString(),
    };

    public static string Incident(IncidentType type, PenaltyResult? penaltyResult = null, ExclusionKind? exclusionKind = null) => type switch
    {
        IncidentType.Goal => "Gol",
        IncidentType.Exclusion => exclusionKind == ExclusionKind.Expulsion ? "Expulsión" : "Exclusión",
        IncidentType.PersonalFoul => "Falta personal",
        IncidentType.Penalty => penaltyResult == PenaltyResult.Converted ? "Penal convertido" : "Penal no convertido",
        IncidentType.Timeout => "Tiempo muerto",
        IncidentType.ShootoutAttempt => penaltyResult == PenaltyResult.Converted ? "Tanda: convertido" : "Tanda: no convertido",
        _ => "Incidencia",
    };

    public static string LocalTime(DateTimeOffset utc) => utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", Culture);

    public static string IncidentClock(ClockContext clock) => clock.Quality == ClockQuality.Live
        ? ClockFormatter.FormatIncidentClock(clock.GameClockRemainingMs)
        : "sin reloj";

    public static string TeamCode(MatchSnapshot snapshot, TeamSide side) =>
        side == TeamSide.Home ? snapshot.Home.Code : snapshot.Away.Code;
}
