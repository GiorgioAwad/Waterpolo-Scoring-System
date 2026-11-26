using System.ComponentModel;
using System.Windows;
using FDNDA.Waterpolo.Desktop.ViewModels;

namespace FDNDA.Waterpolo.Desktop.Views;

/// <summary>Editor de las reglas de un evento. Cerrarlo sin guardar no cambia nada.</summary>
public partial class ReglasEventoWindow : Window
{
    private readonly RulesEditorViewModel _viewModel;

    public ReglasEventoWindow(RulesEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, bool saved) => DialogResult = saved;

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
