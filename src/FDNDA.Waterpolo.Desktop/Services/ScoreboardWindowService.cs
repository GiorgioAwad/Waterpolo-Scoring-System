using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Desktop.ViewModels;
using FDNDA.Waterpolo.Desktop.Views;
using Microsoft.Win32;

namespace FDNDA.Waterpolo.Desktop.Services;

/// <summary>
/// Administra la ventana del marcador: pantalla elegida, pantalla completa sin bordes y
/// retorno seguro a ventana si el monitor se desconecta. Abrir o reabrir el marcador no
/// toca el partido: la ventana solo observa el estado compartido.
/// </summary>
internal sealed class ScoreboardWindowService : IScoreboardHost, IDisposable
{
    private readonly Func<MarcadorViewModel> _viewModelFactory;
    private readonly IDiagnosticLog _log;

    private MarcadorWindow? _window;
    private MarcadorViewModel? _viewModel;
    private MonitorOption? _target;
    private bool _fullScreen;

    public ScoreboardWindowService(Func<MarcadorViewModel> viewModelFactory, IDiagnosticLog log)
    {
        _viewModelFactory = viewModelFactory;
        _log = log;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public event EventHandler<string>? Notice;

    public event EventHandler? StateChanged;

    public bool IsOpen => _window is not null;

    public ScoreboardPlacement? Placement => _window is not null && _target is not null ? new(_target, _fullScreen) : null;

    public IReadOnlyList<MonitorOption> GetMonitors() => NativeMonitors.GetMonitors();

    public void Open(MonitorOption? monitor, bool fullScreen)
    {
        var monitors = GetMonitors();
        var target = monitors.FirstOrDefault(m => m.DeviceName == monitor?.DeviceName)
            ?? monitors.FirstOrDefault(m => !m.IsPrimary)
            ?? monitors[0];

        // Con una sola pantalla el marcador a pantalla completa taparía la mesa.
        var useFullScreen = fullScreen && monitors.Count > 1;
        if (fullScreen && !useFullScreen)
        {
            Notice?.Invoke(this, "Solo hay una pantalla conectada: el marcador se abrió en ventana.");
        }

        if (_window is null)
        {
            _viewModel = _viewModelFactory();
            _window = new MarcadorWindow { DataContext = _viewModel };
            _window.Closed += OnWindowClosed;
            _window.WindowedRequested += OnWindowedRequested;
        }

        _target = target;
        _fullScreen = useFullScreen;
        _window.ApplyPlacement(target, useFullScreen);
        _log.Info($"Marcador abierto en pantalla {target.Index} ({target.Width}x{target.Height}), {(useFullScreen ? "pantalla completa" : "ventana")}.");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Close() => _window?.Close();

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Close();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_window is not null)
        {
            _window.Closed -= OnWindowClosed;
            _window.WindowedRequested -= OnWindowedRequested;
        }

        _viewModel?.Dispose();
        _viewModel = null;
        _window = null;
        _target = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnWindowedRequested(object? sender, EventArgs e)
    {
        if (_window is null || !_fullScreen)
        {
            return;
        }

        var monitors = GetMonitors();
        var target = monitors.FirstOrDefault(m => m.DeviceName == _target?.DeviceName) ?? monitors[0];
        _fullScreen = false;
        _target = target;
        _window.ApplyPlacement(target, fullScreen: false);
        Notice?.Invoke(this, "El marcador pasó a modo ventana (tecla Esc).");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        dispatcher?.BeginInvoke(() =>
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
            if (_window is null || _target is null)
            {
                return;
            }

            var monitors = GetMonitors();
            var current = monitors.FirstOrDefault(m => m.DeviceName == _target.DeviceName);
            if (current is null || (_fullScreen && monitors.Count < 2))
            {
                // Retorno seguro: el monitor del marcador ya no está.
                var fallback = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
                _target = fallback;
                _fullScreen = false;
                _window.ApplyPlacement(fallback, fullScreen: false);
                _log.Warn("La pantalla del marcador se desconectó; el marcador pasó a modo ventana.");
                Notice?.Invoke(this, "Se desconectó la pantalla del marcador: se pasó a modo ventana en la pantalla principal.");
            }
            else if (_fullScreen && current != _target)
            {
                // Cambió la resolución o la posición de la pantalla: se reajusta.
                _target = current;
                _window.ApplyPlacement(current, fullScreen: true);
            }
        });
    }
}
