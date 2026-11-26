using FDNDA.Waterpolo.Application.Abstractions;

namespace FDNDA.Waterpolo.Application.Reports;

public enum ReportFormat
{
    Csv = 1,
    Pdf = 2,
}

/// <summary>Escribe un documento de resumen en un archivo (CSV o PDF).</summary>
public interface IReportWriter
{
    /// <summary>
    /// Escribe el archivo completo o no lo deja: si falla, no queda un archivo a medias en
    /// <paramref name="path"/> (lo que hubiera antes se conserva).
    /// </summary>
    Task WriteAsync(ReportDocument document, ReportFormat format, string path, CancellationToken ct = default);
}

/// <summary>Resultado de exportar.</summary>
public sealed record ExportResult(bool IsSuccess, string Message, string? Path = null);

/// <summary>
/// Resúmenes por partido y por evento (RF09) derivados de los partidos persistidos, y su
/// exportación. Leer y escribir archivos corre fuera del hilo que llama.
/// </summary>
public sealed class ReportOperations
{
    private readonly IMatchRepository _matches;
    private readonly ITournamentRepository _tournaments;
    private readonly IReportWriter _writer;
    private readonly TimeProvider _time;
    private readonly IDiagnosticLog _log;

    public ReportOperations(
        IMatchRepository matches, ITournamentRepository tournaments, IReportWriter writer, TimeProvider time, IDiagnosticLog log)
    {
        _matches = matches;
        _tournaments = tournaments;
        _writer = writer;
        _time = time;
        _log = log;
    }

    public async Task<MatchReport?> GetMatchReportAsync(Guid matchId, CancellationToken ct = default)
    {
        var match = await _matches.GetAsync(matchId, ct).ConfigureAwait(false);
        return match is null ? null : ReportBuilder.ForMatch(match);
    }

    public Task<TournamentReport?> GetTournamentReportAsync(Guid tournamentId, CancellationToken ct = default) =>
        Task.Run(async () =>
        {
            var tournament = await _tournaments.GetAsync(tournamentId, ct).ConfigureAwait(false);
            if (tournament is null)
            {
                return null;
            }

            var reports = new List<MatchReport>();
            foreach (var summary in await _matches.ListAsync(ct).ConfigureAwait(false))
            {
                if (await _matches.GetAsync(summary.MatchId, ct).ConfigureAwait(false) is { } match && match.TournamentId == tournamentId)
                {
                    reports.Add(ReportBuilder.ForMatch(match));
                }
            }

            return (TournamentReport?)ReportBuilder.ForTournament(tournament.Id, tournament.Name, tournament.IsDemo, reports);
        }, ct);

    public async Task<ReportDocument?> MatchDocumentAsync(Guid matchId, CancellationToken ct = default)
    {
        var report = await GetMatchReportAsync(matchId, ct).ConfigureAwait(false);
        if (report is null)
        {
            return null;
        }

        var tournament = await _tournaments.GetAsync(report.TournamentId, ct).ConfigureAwait(false);
        return ReportDocuments.ForMatch(report, tournament?.IsDemo ?? false, _time.GetUtcNow(), _time.LocalTimeZone);
    }

    public async Task<ReportDocument?> TournamentDocumentAsync(Guid tournamentId, CancellationToken ct = default)
    {
        var report = await GetTournamentReportAsync(tournamentId, ct).ConfigureAwait(false);
        return report is null ? null : ReportDocuments.ForTournament(report, _time.GetUtcNow(), _time.LocalTimeZone);
    }

    public async Task<ExportResult> ExportAsync(ReportDocument document, ReportFormat format, string path, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var name = System.IO.Path.GetFileName(path);
        try
        {
            await Task.Run(() => _writer.WriteAsync(document, format, path, ct), ct).ConfigureAwait(false);
            _log.Info($"Resumen exportado ({format}): {name}.");
            return new ExportResult(true, $"Resumen exportado: {name}.", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"No se pudo exportar el resumen: {ex.GetType().Name}.");
            return new ExportResult(
                false,
                $"No se pudo guardar «{name}». Si está abierto en otro programa (Excel o un lector de PDF), ciérrelo; compruebe también el espacio libre y que puede escribir en esa carpeta.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error("No se pudo exportar el resumen.", ex);
            return new ExportResult(false, $"No se pudo exportar «{name}».");
        }
    }
}
