using System.Windows;

namespace FDNDA.Waterpolo.Desktop.Views;

/// <summary>Identificación local del operador al abrir la sesión.</summary>
public partial class OperadorWindow : Window
{
    public OperadorWindow(IEnumerable<string> knownOperators)
    {
        InitializeComponent();
        foreach (var name in knownOperators)
        {
            NameBox.Items.Add(name);
        }

        if (NameBox.Items.Count > 0)
        {
            NameBox.SelectedIndex = 0;
        }
    }

    public string OperatorName => (NameBox.Text ?? string.Empty).Trim();

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (OperatorName.Length is 0 or > 60)
        {
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
