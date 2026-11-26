using CommunityToolkit.Mvvm.ComponentModel;
using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Desktop.Presentation;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;

namespace FDNDA.Waterpolo.Desktop.ViewModels;

/// <summary>Fila de la lista de partidos.</summary>
public sealed record MatchListItem(
    Guid MatchId, string Tournament, string Label, string Home, string Away, string Status, string Scheduled,
    MatchStatus State = MatchStatus.Scheduled, Guid TournamentId = default, DateTimeOffset? ScheduledAtUtc = null)
{
    /// <summary>Aún no iniciado ni anulado: se puede reprogramar, actualizar su nómina o anular.</summary>
    public bool IsScheduled => State == MatchStatus.Scheduled;

    public bool IsCancelled => State == MatchStatus.Cancelled;

    public static MatchListItem From(MatchSummary summary) => new(
        summary.MatchId,
        summary.TournamentName,
        summary.Label,
        summary.HomeName,
        summary.AwayName,
        MatchTexts.Status(summary.Status),
        summary.ScheduledAtUtc is { } at ? at.ToLocalTime().ToString(CatalogViewModel.DateFormat, MatchTexts.Culture) : "—",
        summary.Status,
        summary.TournamentId,
        summary.ScheduledAtUtc);
}

/// <summary>Jugador de la nómina del partido para elegir en la mesa.</summary>
public sealed record PlayerOption(TeamSide Side, Guid RosterEntryId, int Cap, string PlayerName, string TeamCode)
{
    public string LabelWithTeam => $"{TeamCode} · {Cap} · {PlayerName}";
}

/// <summary>Acción explícita de mesa, incluidos resultado del penal y decisión de exclusión.</summary>
public sealed record IncidentOption(IncidentType Type, PenaltyResult? PenaltyResult = null, ExclusionKind? ExclusionKind = null)
{
    public string Label => MatchTexts.Incident(Type, PenaltyResult, ExclusionKind);

    public static IReadOnlyList<IncidentOption> All { get; } =
    [
        new(IncidentType.Goal),
        new(IncidentType.Exclusion, ExclusionKind: Domain.Matches.ExclusionKind.Exclusion),
        new(IncidentType.Exclusion, ExclusionKind: Domain.Matches.ExclusionKind.Expulsion),
        new(IncidentType.PersonalFoul),
        new(IncidentType.Penalty, Domain.Matches.PenaltyResult.Converted),
        new(IncidentType.Penalty, Domain.Matches.PenaltyResult.NotConverted),
        new(IncidentType.Timeout),
        new(IncidentType.ShootoutAttempt, Domain.Matches.PenaltyResult.Converted),
        new(IncidentType.ShootoutAttempt, Domain.Matches.PenaltyResult.NotConverted),
    ];

    /// <summary>Acciones que la mesa registra con F6/F9 en juego (el gol tiene sus propias teclas).</summary>
    public static IReadOnlyList<IncidentOption> DuringPlay { get; } =
        [.. All.Where(o => o.Type is not (IncidentType.Goal or IncidentType.ShootoutAttempt))];

    /// <summary>Durante la tanda: lanzamientos y, por si el árbitro sanciona, exclusión o expulsión.</summary>
    public static IReadOnlyList<IncidentOption> DuringShootout { get; } =
        [.. All.Where(o => o.Type == IncidentType.ShootoutAttempt), .. All.Where(o => o.Type == IncidentType.Exclusion)];
}

public sealed record TeamOption(TeamSide Side, string Label);

/// <summary>Incidencia en el historial de la mesa, con su auditoría.</summary>
public sealed record IncidentItem(
    Guid IncidentId,
    int Sequence,
    string Period,
    string Clock,
    string Team,
    string Cap,
    string Player,
    string State,
    bool IsVoided,
    IReadOnlyList<string> History,
    IncidentType Type,
    string TypeText,
    PenaltyResult? PenaltyResult,
    ExclusionKind? ExclusionKind,
    TeamSide Side,
    Guid? RosterEntryId)
{
    public static IncidentItem From(MatchSnapshot snapshot, IncidentSnapshot incident)
    {
        var state = incident.IsVoided
            ? "ANULADO"
            : incident.Revisions.Count > 1 ? $"Corregido ({incident.Revisions.Count - 1})" : "Vigente";

        var history = incident.Revisions
            .Select(r =>
            {
                var who = r.PlayerName is null ? MatchTexts.TeamCode(snapshot, r.Side) : $"{MatchTexts.TeamCode(snapshot, r.Side)} · {r.Cap} · {r.PlayerName}";
                var reason = r.Reason is null ? string.Empty : $" · Motivo: {r.Reason}";
                return $"Rev. {r.Number} · {MatchTexts.Revision(r.Kind)} · {MatchTexts.Incident(incident.Type, r.PenaltyResult, r.ExclusionKind)} · {who}{reason} · {r.OperatorName} · {MatchTexts.LocalTime(r.AtUtc)}";
            })
            .ToList();

        return new IncidentItem(
            incident.IncidentId,
            incident.Sequence,
            MatchTexts.IncidentPeriod(snapshot, incident),
            MatchTexts.IncidentClock(incident.Clock),
            MatchTexts.TeamCode(snapshot, incident.Side),
            incident.Cap?.ToString(MatchTexts.Culture) ?? "—",
            incident.PlayerName ?? "—",
            state,
            incident.IsVoided,
            history,
            incident.Type,
            MatchTexts.Incident(incident.Type, incident.PenaltyResult, incident.ExclusionKind),
            incident.PenaltyResult,
            incident.ExclusionKind,
            incident.Side,
            incident.RosterEntryId);
    }
}

/// <summary>Pantalla disponible para el marcador.</summary>
public sealed record MonitorOption(string DeviceName, int X, int Y, int Width, int Height, bool IsPrimary, int Index)
{
    public string Label => $"Pantalla {Index}{(IsPrimary ? " (principal)" : string.Empty)} — {Width}×{Height}";
}

/// <summary>Ajustes del marcador que la mesa controla en vivo.</summary>
public sealed partial class ScoreboardSettings : ObservableObject
{
    /// <summary>Animaciones locales del marcador; la mesa puede desactivarlas.</summary>
    [ObservableProperty]
    public partial bool AnimationsEnabled { get; set; } = true;

    /// <summary>Aviso de gol (también el penal convertido).</summary>
    [ObservableProperty]
    public partial bool GoalAnimationsEnabled { get; set; } = true;

    /// <summary>Aviso de exclusión o expulsión.</summary>
    [ObservableProperty]
    public partial bool ExclusionAnimationsEnabled { get; set; } = true;

    /// <summary>Aviso de tiempo muerto: al pedirlo (mesa) y al terminar (reloj de la consola).</summary>
    [ObservableProperty]
    public partial bool TimeoutAnimationsEnabled { get; set; } = true;

    /// <summary>Aviso de falta personal.</summary>
    [ObservableProperty]
    public partial bool FoulAnimationsEnabled { get; set; } = true;

    /// <summary>Anuncio de inicio de periodo, descanso, tanda y final.</summary>
    [ObservableProperty]
    public partial bool PeriodAnimationsEnabled { get; set; } = true;

    /// <summary>Margen seguro por lado, en porcentaje del área útil (0–10).</summary>
    [ObservableProperty]
    public partial double SafeMarginPercent { get; set; } = 2.5;
}

/// <summary>Datos del diagnóstico local. No incluye nóminas.</summary>
public sealed record AppInfo(
    string Version,
    AppProfile Profile,
    string DataDirectory,
    string DatabasePath,
    int SchemaVersion,
    string LogPath,
    string OperatorName,
    string DataLocationText = "")
{
    public string ProfileText => Profile == AppProfile.Demo ? "DEMO (datos de demostración)" : "PRODUCCIÓN";
}

/// <summary>Dónde está el marcador abierto.</summary>
public sealed record ScoreboardPlacement(MonitorOption Monitor, bool FullScreen);

/// <summary>Ventana del marcador, vista desde la mesa.</summary>
public interface IScoreboardHost
{
    bool IsOpen { get; }

    /// <summary>Pantalla y modo del marcador abierto; nulo si está cerrado.</summary>
    ScoreboardPlacement? Placement => null;

    IReadOnlyList<MonitorOption> GetMonitors();

    /// <summary>Abre o reubica el marcador. Con una sola pantalla se abre en ventana.</summary>
    void Open(MonitorOption? monitor, bool fullScreen);

    void Close();

    /// <summary>Aviso para el operador (p. ej. retorno a ventana por desconexión del monitor).</summary>
    event EventHandler<string>? Notice;

    event EventHandler? StateChanged;
}

/// <summary>Diálogos de la mesa.</summary>
public interface IPromptService
{
    /// <summary>Pide un motivo; nulo si el operador cancela.</summary>
    string? AskReason(string title, string message);

    bool Confirm(string title, string message);

    void OpenFolder(string path);
}
