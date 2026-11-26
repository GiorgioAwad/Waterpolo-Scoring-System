using System.ComponentModel;
using System.Windows;
using FDNDA.Waterpolo.Desktop.ViewModels;

namespace FDNDA.Waterpolo.Desktop.Views;

/// <summary>Vista previa de la importación de una nómina. Cerrarla sin importar no guarda nada.</summary>
public partial class ImportarNominaWindow : Window
{
    private readonly RosterImportViewModel _viewModel;

    public ImportarNominaWindow(RosterImportViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, bool imported) => DialogResult = imported;

    protected override void OnClosing(CancelEventArgs e)
    {
        // Mientras se guarda no se cierra: el resultado debe llegar a la mesa.
        e.Cancel = _viewModel.IsBusy;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.CloseRequested -= OnCloseRequested;
        base.OnClosed(e);
    }
}
