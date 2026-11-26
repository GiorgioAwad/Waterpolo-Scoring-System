using System.Globalization;
using System.Text;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Domain.Matches;

namespace FDNDA.Waterpolo.Application.Reports;

public enum ColumnAlign
{
    Left = 0,
    Right = 1,
    Center = 2,
}

/// <summary>Columna de una tabla; el ancho es relativo a las demás columnas de la tabla.</summary>
public sealed record ReportColumn(string Header, double Width, ColumnAlign Align = ColumnAlign.Left);

/// <summary>Una tabla con título. <see cref="EmptyText"/> se muestra si no tiene filas.</summary>
public sealed record ReportSection(
    string Heading,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    string EmptyText,
    string? Note = null);

/// <summary>
/// Documento de un resumen, independiente del formato: los escritores de CSV y PDF lo vuelcan tal
/// cual, así que ambos archivos dicen lo mismo. Todo el texto es de presentación (es-PE).
/// </summary>
public sealed record ReportDocument(
    string Title,
    IReadOnlyList<string> Lines,
    IReadOnlyList<ReportSection> Sections,
    string Footer,
    string SuggestedFileName);

/// <summary>Convierte los resúmenes en documentos en español.</summary>
public static class ReportDocuments
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-PE");

    private const string Derivation =
        "El resultado y las estadísticas se derivan de las incidencias vigentes: las anuladas no cuentan y las corregidas cuentan con su último valor.";

    private const string DemoWarning = "DATOS DE DEMOSTRACIÓN: no son resultados oficiales.";

    public static ReportDocument ForMatch(MatchReport report, bool demoData, DateTimeOffset generatedAtUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(report);
        var lines = new List<string>
        {
            $"{report.TournamentName} · {report.Label}",
            report.ScheduledAtUtc is { } at ? $"Fecha programada: {Local(at, zone)}" : "Sin fecha programada",
            $"Estado: {Status(report.Status)}",
            report.RulesPresetName is { } preset ? $"Reglas del partido: {preset}" : "Reglas: se copian del evento al iniciar el partido.",
        };
        if (demoData)
        {
            lines.Add(DemoWarning);
        }

        if (report.Status == MatchStatus.InProgress)
        {
            lines.Add("Partido en juego: este resumen es provisional.");
        }

        if (report.WentToOvertime)
        {
            lines.Add("El partido tuvo prórroga: sus goles forman parte del resultado.");
        }

        if (report.Shootout is { } shootoutTotals)
        {
            lines.Add($"Tanda de penales: {report.Home.Code} {Number(shootoutTotals.HomeGoals)} – {Number(shootoutTotals.AwayGoals)} {report.Away.Code}. " +
                "No suma al resultado ni a los goleadores.");
        }

        lines.Add(Derivation);

        var summary = new ReportSection(
            "Resumen por equipo",
            [
                new("Equipo", 30), new("Goles", 9, ColumnAlign.Right), new("Exclusiones", 12, ColumnAlign.Right),
                new("Expulsiones", 12, ColumnAlign.Right), new("Faltas personales", 13, ColumnAlign.Right),
                new("Penales conv./no conv.", 15, ColumnAlign.Right), new("Tiempos muertos", 12, ColumnAlign.Right),
            ],
            [TeamRow(report.Home), TeamRow(report.Away)],
            string.Empty);

        var scorers = new ReportSection(
            "Goleadores",
            [new("Gorro", 8, ColumnAlign.Right), new("Jugador", 52), new("Equipo", 14), new("Goles", 10, ColumnAlign.Right)],
            [.. report.Scorers.Select(s => (IReadOnlyList<string>)[Number(s.Cap), s.PlayerName, s.TeamCode, Number(s.Goals)])],
            "Sin goles registrados.");

        var incidents = new ReportSection(
            "Incidencias",
            [
                new("N.º", 6, ColumnAlign.Right), new("Per.", 6, ColumnAlign.Right), new("Reloj", 9, ColumnAlign.Right),
                new("Equipo", 9), new("Gorro", 7, ColumnAlign.Right), new("Jugador", 30), new("Incidencia", 20), new("Estado", 13),
            ],
            [.. report.Incidents.Select(i => (IReadOnlyList<string>)
            [
                Number(i.Sequence),
                i.OvertimeNumber is { } overtime ? $"Pr. {Number(overtime)}" : Number(i.Period),
                ClockFormatter.FormatIncidentClock(i.GameClockRemainingMs),
                i.TeamCode,
                i.Cap is { } cap ? Number(cap) : string.Empty,
                i.PlayerName ?? string.Empty,
                Incident(i.Type, i.PenaltyResult, i.ExclusionKind),
                i.IsVoided ? "Anulada" : i.IsCorrected ? "Corregida" : "Vigente",
            ])],
            "Sin incidencias registradas.");

        List<ReportSection> sections = [summary, scorers, incidents];
        if (report.Shootout is { } shootout)
        {
            sections.Add(new ReportSection(
                "Tanda de penales",
                [
                    new("N.º", 6, ColumnAlign.Right), new("Equipo", 9), new("Gorro", 7, ColumnAlign.Right), new("Jugador", 36),
                    new("Lanzamiento", 18), new("Estado", 13),
                ],
                [.. shootout.Attempts.Select(a => (IReadOnlyList<string>)
                [
                    Number(a.Number),
                    a.TeamCode,
                    a.Cap is { } cap ? Number(cap) : string.Empty,
                    a.PlayerName ?? string.Empty,
                    a.Converted ? "Convertido" : "No convertido",
                    a.IsVoided ? "Anulado" : a.IsCorrected ? "Corregido" : "Vigente",
                ])],
                "Tanda iniciada, sin lanzamientos registrados.",
                $"Resultado de la tanda: {report.Home.Code} {Number(shootout.HomeGoals)} – {Number(shootout.AwayGoals)} {report.Away.Code}. " +
                "Se muestra aparte del resultado reglamentario."));
        }

        var title = $"{report.Home.Name} {report.Home.Totals.Goals} – {report.Away.Totals.Goals} {report.Away.Name}";
        return new ReportDocument(
            title,
            lines,
            sections,
            Footer(generatedAtUtc, zone),
            $"resumen-partido-{Slug(report.Label)}-{Stamp(generatedAtUtc, zone)}");
    }

    public static ReportDocument ForTournament(TournamentReport report, DateTimeOffset generatedAtUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(report);
        var finished = report.Matches.Count(m => m.Status == MatchStatus.Finished);
        var live = report.Matches.Count(m => m.Status == MatchStatus.InProgress);
        var scheduled = report.Matches.Count(m => m.Status == MatchStatus.Scheduled);
        var cancelled = report.Matches.Count(m => m.Status == MatchStatus.Cancelled);
        var lines = new List<string>
        {
            $"{Plural(report.Matches.Count, "partido", "partidos")}: {Plural(finished, "finalizado", "finalizados")}, {Plural(live, "en juego", "en juego")} y {Plural(scheduled, "programado", "programados")}"
                + (cancelled > 0 ? $"; {Plural(cancelled, "anulado", "anulados")} (no cuenta en los totales)." : "."),
        };
        if (report.IsDemo)
        {
            lines.Add(DemoWarning);
        }

        lines.Add(Derivation);

        var results = new ReportSection(
            "Resultados",
            [
                new("Fecha", 16), new("Partido", 18), new("Local", 21), new("Resultado", 10, ColumnAlign.Center),
                new("Tanda", 8, ColumnAlign.Center), new("Visita", 21), new("Estado", 11),
            ],
            [.. report.Matches.Select(m => (IReadOnlyList<string>)
            [
                m.ScheduledAtUtc is { } at ? Local(at, zone) : "Sin fecha",
                m.Label,
                m.Home.Name,
                ResultText(m),
                ShootoutText(m),
                m.Away.Name,
                Status(m.Status),
            ])],
            "El evento no tiene partidos.",
            "La tanda de penales se muestra aparte: no suma al resultado ni a los goles.");

        var scorers = new ReportSection(
            "Goleadores del evento",
            [new("Jugador", 52), new("Equipo", 14), new("Goles", 10, ColumnAlign.Right)],
            [.. report.Scorers.Select(s => (IReadOnlyList<string>)[s.PlayerName, s.TeamCode, Number(s.Goals)])],
            "Sin goles registrados.");

        var teams = new ReportSection(
            "Totales por equipo",
            [
                new("Equipo", 26), new("Jugados", 8, ColumnAlign.Right), new("Goles a favor", 8, ColumnAlign.Right),
                new("Goles en contra", 8, ColumnAlign.Right), new("Exclusiones", 11, ColumnAlign.Right), new("Expulsiones", 11, ColumnAlign.Right),
                new("Faltas personales", 10, ColumnAlign.Right), new("Penales conv.", 9, ColumnAlign.Right), new("Tiempos muertos", 9, ColumnAlign.Right),
            ],
            [.. report.Teams.Select(t => (IReadOnlyList<string>)
            [
                $"{t.Code} · {t.Name}", Number(t.Played), Number(t.GoalsFor), Number(t.GoalsAgainst), Number(t.Exclusions),
                Number(t.Expulsions), Number(t.PersonalFouls), Number(t.PenaltiesConverted), Number(t.Timeouts),
            ])],
            "Ningún partido iniciado todavía.",
            "Suma los partidos iniciados y finalizados. No es una tabla de posiciones: el sistema de puntos del evento no está definido.");

        return new ReportDocument(
            $"Resumen del evento: {report.Name}",
            lines,
            [results, scorers, teams],
            Footer(generatedAtUtc, zone),
            $"resumen-evento-{Slug(report.Name)}-{Stamp(generatedAtUtc, zone)}");
    }

    /// <summary>Resultado reglamentario (incluye la prórroga) o «vs» si el partido no empezó.</summary>
    public static string ResultText(MatchReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!report.HasBeenPlayed)
        {
            return "vs";
        }

        var text = $"{Number(report.Home.Totals.Goals)} – {Number(report.Away.Totals.Goals)}";
        return report.WentToOvertime ? text + " (pr.)" : text;
    }

    /// <summary>Resultado de la tanda, o vacío si no la hubo.</summary>
    public static string ShootoutText(MatchReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return report.Shootout is { } s ? $"{Number(s.HomeGoals)} – {Number(s.AwayGoals)}" : string.Empty;
    }

    public static string Status(MatchStatus status) => status switch
    {
        MatchStatus.Scheduled => "Programado",
        MatchStatus.InProgress => "En juego",
        MatchStatus.Finished => "Finalizado",
        MatchStatus.Cancelled => "Anulado",
        _ => status.ToString(),
    };

    public static string Incident(IncidentType type, PenaltyResult? penalty, ExclusionKind? exclusion) => type switch
    {
        IncidentType.Goal => "Gol",
        IncidentType.Exclusion => exclusion == ExclusionKind.Expulsion ? "Expulsión" : "Exclusión",
        IncidentType.PersonalFoul => "Falta personal",
        IncidentType.Penalty => penalty == PenaltyResult.Converted ? "Penal convertido" : "Penal no convertido",
        IncidentType.Timeout => "Tiempo muerto",
        IncidentType.ShootoutAttempt => penalty == PenaltyResult.Converted ? "Tanda: convertido" : "Tanda: no convertido",
        _ => type.ToString(),
    };

    /// <summary>Nombre de archivo sin acentos, espacios ni caracteres reservados.</summary>
    public static string Slug(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in (text ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > 60)
        {
            slug = slug[..60].TrimEnd('-');
        }

        return slug.Length == 0 ? "sin-nombre" : slug;
    }

    private static IReadOnlyList<string> TeamRow(TeamReport team) =>
    [
        $"{team.Code} · {team.Name}",
        Number(team.Totals.Goals),
        Number(team.Totals.Exclusions),
        Number(team.Totals.Expulsions),
        Number(team.Totals.PersonalFouls),
        $"{Number(team.Totals.PenaltiesConverted)} / {Number(team.Totals.PenaltiesMissed)}",
        Number(team.Totals.Timeouts),
    ];

    private static string Footer(DateTimeOffset generatedAtUtc, TimeZoneInfo zone) =>
        $"Marcador de Waterpolo FDNDA · Generado el {Local(generatedAtUtc, zone)}";

    private static string Local(DateTimeOffset utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(utc, zone).ToString("dd/MM/yyyy HH:mm", Culture);

    private static string Stamp(DateTimeOffset utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(utc, zone).ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Plural(int value, string one, string many) => value == 1 ? $"1 {one}" : $"{value} {many}";
}
