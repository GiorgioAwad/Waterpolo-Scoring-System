using System.Diagnostics;
using System.IO;
using System.Windows;
using FDNDA.Waterpolo.Desktop.ViewModels;
using Microsoft.Win32;

namespace FDNDA.Waterpolo.Desktop.Services;

/// <summary>Diálogos de la sección de respaldos, siempre sobre la ventana de mesa.</summary>
internal sealed class BackupDialogs : IBackupDialogs
{
    public string? PickBackupFile(string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Restaurar respaldo",
            Filter = "Respaldo del marcador (*.db)|*.db|Todos los archivos (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : string.Empty,
        };
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(
            System.Windows.Application.Current.MainWindow!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
        == MessageBoxResult.Yes;

    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
