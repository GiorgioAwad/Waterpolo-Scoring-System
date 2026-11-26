using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using FDNDA.Waterpolo.Desktop.ViewModels;
using FDNDA.Waterpolo.Desktop.Views;
using Microsoft.Win32;

namespace FDNDA.Waterpolo.Desktop.Services;

/// <summary>Diálogos de la pestaña «Eventos y nóminas», siempre sobre la ventana de mesa.</summary>
internal sealed class CatalogDialogs : ICatalogDialogs
{
    public string? PickRosterFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importar nómina",
            Filter = "Nómina (*.csv; *.xlsx)|*.csv;*.xlsx|CSV UTF-8 (*.csv)|*.csv|Libro de Excel (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            Multiselect = false,
        };
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    public Task<bool> ShowRosterImportAsync(RosterImportViewModel preview)
    {
        var window = new ImportarNominaWindow(preview) { Owner = System.Windows.Application.Current.MainWindow };
        return Task.FromResult(window.ShowDialog() == true);
    }

    public string? PickImageFile(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Imagen PNG o JPEG (*.png; *.jpg; *.jpeg)|*.png;*.jpg;*.jpeg",
            CheckFileExists = true,
            Multiselect = false,
        };
        return dialog.ShowDialog(Owner()) == true ? dialog.FileName : null;
    }

    public Task<bool> ShowEventIdentityAsync(EventIdentityViewModel editor)
    {
        var window = new IdentidadEventoWindow(editor) { Owner = System.Windows.Application.Current.MainWindow };
        return Task.FromResult(window.ShowDialog() == true);
    }

    public Task<bool> ShowRulesEditorAsync(RulesEditorViewModel editor)
    {
        var window = new ReglasEventoWindow(editor) { Owner = System.Windows.Application.Current.MainWindow };
        return Task.FromResult(window.ShowDialog() == true);
    }

    /// <summary>Colores personalizados del selector, conservados mientras el programa esté abierto.</summary>
    private readonly int[] _customColors = new int[16];

    public string? PickColor(string? currentHex)
    {
        var owner = Owner() is { } window ? new WindowInteropHelper(window).Handle : IntPtr.Zero;
        var custom = GCHandle.Alloc(_customColors, GCHandleType.Pinned);
        try
        {
            var choose = new ChooseColorInfo
            {
                StructSize = Marshal.SizeOf<ChooseColorInfo>(),
                Owner = owner,
                Result = ToColorRef(currentHex),
                CustomColors = custom.AddrOfPinnedObject(),
                Flags = CcRgbInit | CcFullOpen,
            };
            if (!ChooseColor(ref choose))
            {
                return null;
            }

            var r = choose.Result & 0xFF;
            var g = (choose.Result >> 8) & 0xFF;
            var b = (choose.Result >> 16) & 0xFF;
            return string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}");
        }
        finally
        {
            custom.Free();
        }
    }

    public string? AskReason(string title, string message)
    {
        var dialog = new MotivoWindow(title, message) { Owner = Owner() };
        return dialog.ShowDialog() == true ? dialog.Reason : null;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(
            Owner()!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
        == MessageBoxResult.Yes;

    /// <summary>Ventana activa (p. ej. el editor de identidad) o, si no hay, la mesa.</summary>
    private static Window? Owner() =>
        System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? System.Windows.Application.Current.MainWindow;

    private const int CcRgbInit = 0x1;
    private const int CcFullOpen = 0x2;

    /// <summary>COLORREF (0x00BBGGRR) a partir de <c>#RRGGBB</c>; negro si no es válido.</summary>
    private static int ToColorRef(string? hex)
    {
        var text = (hex ?? string.Empty).Trim().TrimStart('#');
        if (text.Length != 6 || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return 0;
        }

        return ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ChooseColorInfo
    {
        public int StructSize;
        public IntPtr Owner;
        public IntPtr Instance;
        public int Result;
        public IntPtr CustomColors;
        public int Flags;
        public IntPtr CustData;
        public IntPtr Hook;
        public IntPtr TemplateName;
    }

    [DllImport("comdlg32.dll", EntryPoint = "ChooseColorW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChooseColor(ref ChooseColorInfo info);
}
