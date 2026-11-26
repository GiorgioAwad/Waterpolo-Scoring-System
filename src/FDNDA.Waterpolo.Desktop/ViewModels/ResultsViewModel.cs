using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Application.Reports;
using FDNDA.Waterpolo.Desktop.Presentation;

namespace FDNDA.Waterpolo.Desktop.ViewModels;

/// <summary>Diálogo para elegir dónde guardar un resumen.</summary>
public interface IReportDialogs
{
    /// <summary>Ruta elegida, o nula si el operador cancela.</summary>
    string? PickSavePath(string suggestedFileName, ReportFormat format);
}

public sealed record TournamentOption(Guid TournamentId, string Name);

/// <summary>Fila de resultados del evento.</summary>
public sealed record MatchResultItem(
    Guid MatchId, string DateText, string Label, string Home, string ScoreText, string Away, string StatusText, string ShootoutText = "")
{
    public static MatchResultItem From(MatchReport m) => new(
        m.MatchId,
        m.ScheduledAtUtc is { } at ? at.ToLocalTime().ToString("dd/MM/yyyy HH:mm", MatchTexts.Culture) : "Sin fecha",
        m.Label,
        m.Home.Name,
        ReportDocuments.ResultText(m),
        m.Away.Name,
        ReportDocuments.Status(m.Status),
        ReportDocuments.ShootoutText(m));
}

public sealed record ScorerItem(string PlayerName, string TeamCode, int Goals);

/// <summary>Totales de un equipo en el partido elegido.</summary>
public sealed record TeamStatsItem(string Team, int Goals, int Exclusions, int Expulsions, int PersonalFouls, string Penalties, int Timeouts)
{
    public static TeamStatsItem From(TeamReport t) => new(
        $"{t.Code} · {t.Name}",
        t.Totals.Goals,
        t.Totals.Exclusions,
        t.Totals.Expulsions,
        t.Totals.PersonalFouls,
        $"{t.Totals.PenaltiesConverted} / {t.Totals.PenaltiesMissed}",
        t.Totals.Timeouts);
}

/// <summary>
/// Sección «Resultados» de la pestaña «Resultados y respaldos» (RF09): resultados, goleadores y
/// totales derivados de las incidencias vigentes, y exportación a PDF y CSV del evento o del partido.
/// </summary>
public sealed partial class ResultsViewModel : ObservableObject
{
    private readonly ReportOperations _reports;
    private readonly CatalogOperations _catalog;
    private readonly IReportDialogs? _dialogs;
    private bool _selectingInCode;

    public ResultsViewModel(ReportOperations reports, CatalogOperations catalog, IReportDialogs? dialogs = null)
    {
        _reports = reports;
        _catalog = catalog;
        _dialogs = dialogs;
    }

    public ObservableCollection<TournamentOption> Tournaments { get; } = [];

    public ObservableCollection<MatchResultItem> Matches { get; } = [];

    public ObservableCollection<ScorerItem> Scorers { get; } = [];

    public ObservableCollection<TeamStatsItem> MatchTeams { get; } = [];

    [ObservableProperty]
    public partial TournamentOption? SelectedTournament { get; set; }

    [ObservableProperty]
    public partial MatchResultItem? SelectedMatch { get; set; }

    [ObservableProperty]
    public partial string TournamentSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MatchTitle { get; set; } = "Elija un partido de la lista para ver su resumen.";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsStatusError { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public Task InitializeAsync() => RefreshAsync();

    /// <summary>Relee eventos, resultados y el partido elegido. La mesa lo llama al abrir la pestaña.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        var tournamentId = SelectedTournament?.TournamentId;
        var matchId = SelectedMatch?.MatchId;
        try
        {
            var tournaments = await _catalog.ListTournamentsAsync();
            Tournaments.Clear();
            foreach (var t in tournaments)
            {
                Tournaments.Add(new TournamentOption(t.TournamentId, t.Name));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus("No se pudieron leer los eventos del almacenamiento local.", isError: true);
            return;
        }

        // Se selecciona sin disparar cargas sueltas y se carga en orden: evento, luego partido.
        SelectInCode(() =>
        {
            SelectedTournament = Tournaments.FirstOrDefault(t => t.TournamentId == tournamentId) ?? Tournaments.FirstOrDefault();
            SelectedMatch = null;
        });
        await LoadTournamentAsync();
        SelectInCode(() => SelectedMatch = Matches.FirstOrDefault(m => m.MatchId == matchId));
        await LoadMatchAsync();
    }

    private void SelectInCode(Action select)
    {
        _selectingInCode = true;
        try
        {
            select();
        }
        finally
        {
            _selectingInCode = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportTournament))]
    private Task ExportTournamentPdfAsync() => ExportTournamentAsync(ReportFormat.Pdf);

    [RelayCommand(CanExecute = nameof(CanExportTournament))]
    private Task ExportTournamentCsvAsync() => ExportTournamentAsync(ReportFormat.Csv);

    [RelayCommand(CanExecute = nameof(CanExportMatch))]
    private Task ExportMatchPdfAsync() => ExportMatchAsync(ReportFormat.Pdf);

    [RelayCommand(CanExecute = nameof(CanExportMatch))]
    private Task ExportMatchCsvAsync() => ExportMatchAsync(ReportFormat.Csv);

    private bool CanExportTournament() => !IsBusy && SelectedTournament is not null && _dialogs is not null;

    private bool CanExportMatch() => !IsBusy && SelectedMatch is not null && _dialogs is not null;

    private async Task ExportTournamentAsync(ReportFormat format)
    {
        if (SelectedTournament is { } tournament)
        {
            await ExportAsync(() => _reports.TournamentDocumentAsync(tournament.TournamentId), format);
        }
    }

    private async Task ExportMatchAsync(ReportFormat format)
    {
        if (SelectedMatch is { } match)
        {
            await ExportAsync(() => _reports.MatchDocumentAsync(match.MatchId), format);
        }
    }

    private async Task ExportAsync(Func<Task<ReportDocument?>> build, ReportFormat format)
    {
        if (IsBusy || _dialogs is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            // El documento se arma con los datos de este momento, antes de elegir el archivo.
            var document = await build();
            if (document is null)
            {
                SetStatus("Ya no existe en los datos. Pulse «Actualizar».", isError: true);
                return;
            }

            var extension = format == ReportFormat.Pdf ? ".pdf" : ".csv";
            var path = _dialogs.PickSavePath(document.SuggestedFileName + extension, format);
            if (path is null)
            {
                SetStatus("Exportación cancelada.", isError: false);
                return;
            }

            SetStatus("Exportando…", isError: false);
            var result = await _reports.ExportAsync(document, format, path);
            SetStatus(result.IsSuccess ? $"{result.Message} Carpeta: {System.IO.Path.GetDirectoryName(path)}" : result.Message, !result.IsSuccess);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus($"Error inesperado: {ex.Message}", isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadTournamentAsync()
    {
        Matches.Clear();
        Scorers.Clear();
        TournamentSummary = string.Empty;
        if (SelectedTournament is not { } selected)
        {
            await LoadMatchAsync();
            return;
        }

        try
        {
            var report = await _reports.GetTournamentReportAsync(selected.TournamentId);
            if (report is null || SelectedTournament != selected)
            {
                return;
            }

            Matches.Clear();
            Scorers.Clear();

            foreach (var match in report.Matches)
            {
                Matches.Add(MatchResultItem.From(match));
            }

            foreach (var scorer in report.Scorers)
            {
                Scorers.Add(new ScorerItem(scorer.PlayerName, scorer.TeamCode, scorer.Goals));
            }

            var played = report.Matches.Count(m => m.HasBeenPlayed);
            TournamentSummary = $"{report.Matches.Count} partidos ({played} iniciados o finalizados) · " +
                $"{report.Scorers.Sum(s => s.Goals)} goles con goleador registrado.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus("No se pudieron leer los resultados del almacenamiento local.", isError: true);
        }
    }

    private async Task LoadMatchAsync()
    {
        MatchTeams.Clear();
        if (SelectedMatch is not { } selected)
        {
            MatchTitle = "Elija un partido de la lista para ver su resumen.";
            return;
        }

        try
        {
            var report = await _reports.GetMatchReportAsync(selected.MatchId);
            if (report is null || SelectedMatch != selected)
            {
                return;
            }

            var shootout = report.Shootout is { } s ? $" · penales {s.HomeGoals} – {s.AwayGoals}" : string.Empty;
            MatchTitle = $"{report.Label}: {report.Home.Name} {ReportDocuments.ResultText(report)} {report.Away.Name}{shootout} · {ReportDocuments.Status(report.Status)}";
            MatchTeams.Add(TeamStatsItem.From(report.Home));
            MatchTeams.Add(TeamStatsItem.From(report.Away));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus("No se pudo leer el partido del almacenamiento local.", isError: true);
        }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
    }

    partial void OnSelectedTournamentChanged(TournamentOption? value)
    {
        ExportTournamentPdfCommand.NotifyCanExecuteChanged();
        ExportTournamentCsvCommand.NotifyCanExecuteChanged();
        if (!_selectingInCode)
        {
            SelectedMatch = null;
            _ = LoadTournamentAsync();
        }
    }

    partial void OnSelectedMatchChanged(MatchResultItem? value)
    {
        ExportMatchPdfCommand.NotifyCanExecuteChanged();
        ExportMatchCsvCommand.NotifyCanExecuteChanged();
        if (!_selectingInCode)
        {
            _ = LoadMatchAsync();
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        ExportTournamentPdfCommand.NotifyCanExecuteChanged();
        ExportTournamentCsvCommand.NotifyCanExecuteChanged();
        ExportMatchPdfCommand.NotifyCanExecuteChanged();
        ExportMatchCsvCommand.NotifyCanExecuteChanged();
    }
}
