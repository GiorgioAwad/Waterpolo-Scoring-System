using FDNDA.Waterpolo.Application.Reports;
using FDNDA.Waterpolo.Desktop.ViewModels;
using Microsoft.Win32;

namespace FDNDA.Waterpolo.Desktop.Services;

/// <summary>«Guardar como» para los resúmenes, siempre sobre la ventana de mesa.</summary>
internal sealed class ReportDialogs : IReportDialogs
{
    public string? PickSavePath(string suggestedFileName, ReportFormat format)
    {
        var pdf = format == ReportFormat.Pdf;
        var dialog = new SaveFileDialog
        {
            Title = pdf ? "Exportar resumen en PDF" : "Exportar resumen en CSV",
            Filter = pdf ? "Documento PDF (*.pdf)|*.pdf" : "CSV UTF-8 (*.csv)|*.csv",
            DefaultExt = pdf ? ".pdf" : ".csv",
            AddExtension = true,
            FileName = suggestedFileName,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FileName : null;
    }
}
