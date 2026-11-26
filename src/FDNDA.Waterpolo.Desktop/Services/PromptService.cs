using System.Diagnostics;
using System.IO;
using System.Windows;
using FDNDA.Waterpolo.Desktop.ViewModels;
using FDNDA.Waterpolo.Desktop.Views;

namespace FDNDA.Waterpolo.Desktop.Services;

/// <summary>Diálogos de la mesa. Siempre se abren sobre la ventana de mesa, nunca sobre el marcador.</summary>
internal sealed class PromptService : IPromptService
{
    public string? AskReason(string title, string message)
    {
        var dialog = new MotivoWindow(title, message) { Owner = System.Windows.Application.Current.MainWindow };
        return dialog.ShowDialog() == true ? dialog.Reason : null;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(
            System.Windows.Application.Current.MainWindow!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
        == MessageBoxResult.Yes;

    public void OpenFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }
}
