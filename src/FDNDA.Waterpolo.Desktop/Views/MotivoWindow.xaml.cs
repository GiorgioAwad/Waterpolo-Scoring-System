using System.Windows;
using System.Windows.Controls;

namespace FDNDA.Waterpolo.Desktop.Views;

/// <summary>Pide el motivo de una acción auditada (p. ej. reabrir un partido).</summary>
public partial class MotivoWindow : Window
{
    public MotivoWindow(string title, string message)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
    }

    public string Reason => ReasonBox.Text.Trim();

    private void OnReasonChanged(object sender, TextChangedEventArgs e) =>
        AcceptButton.IsEnabled = !string.IsNullOrWhiteSpace(ReasonBox.Text);

    private void OnAccept(object sender, RoutedEventArgs e) => DialogResult = true;
}
