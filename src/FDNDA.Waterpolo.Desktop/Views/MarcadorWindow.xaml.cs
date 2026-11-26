using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using FDNDA.Waterpolo.Desktop.Services;
using FDNDA.Waterpolo.Desktop.ViewModels;

namespace FDNDA.Waterpolo.Desktop.Views;

/// <summary>
/// Ventana del marcador de audiencia. A pantalla completa no tiene bordes y ocupa la
/// pantalla elegida; Esc la devuelve a modo ventana.
/// </summary>
public partial class MarcadorWindow : Window
{
    private ScoreboardSettings? _settings;
    private MarcadorViewModel? _viewModel;

    public MarcadorWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateSafeArea();
        DataContextChanged += OnDataContextChanged;
        Closed += (_, _) => Detach();
    }

    /// <summary>El operador pidió salir de pantalla completa (Esc).</summary>
    public event EventHandler? WindowedRequested;

    /// <summary>Ubica la ventana en la pantalla indicada, a pantalla completa o en ventana 16:9.</summary>
    internal void ApplyPlacement(MonitorOption monitor, bool fullScreen)
    {
        WindowState = WindowState.Normal;
        WindowStyle = fullScreen ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        ResizeMode = fullScreen ? ResizeMode.NoResize : ResizeMode.CanResize;
        Topmost = fullScreen;

        var handle = new WindowInteropHelper(this).EnsureHandle();
        Place(handle, monitor, fullScreen);
        if (!IsVisible)
        {
            Show();
            Place(handle, monitor, fullScreen); // WPF puede reubicar al mostrar por primera vez
        }

        UpdateSafeArea();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            WindowedRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private static void Place(IntPtr handle, MonitorOption monitor, bool fullScreen)
    {
        if (fullScreen)
        {
            NativeMonitors.SetBounds(handle, monitor.X, monitor.Y, monitor.Width, monitor.Height);
            return;
        }

        // Ventana 16:9 centrada, de hasta 1280 px de ancho o el 70 % de la pantalla.
        var width = (int)Math.Min(1280, monitor.Width * 0.7);
        var height = (width * 9 / 16) + 40;
        NativeMonitors.SetBounds(
            handle, monitor.X + ((monitor.Width - width) / 2), monitor.Y + ((monitor.Height - height) / 2), width, height);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();
        if (e.NewValue is MarcadorViewModel viewModel)
        {
            _viewModel = viewModel;
            _viewModel.PropertyChanged += OnViewModelChanged;
            _settings = viewModel.Settings;
            _settings.PropertyChanged += OnSettingsChanged;
        }

        UpdateSafeArea();
    }

    private void Detach()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel = null;
        }

        if (_settings is not null)
        {
            _settings.PropertyChanged -= OnSettingsChanged;
            _settings = null;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MarcadorViewModel.BannerGeneration) && _viewModel?.Banner is { } banner)
        {
            Dispatcher.BeginInvoke(() => PlayTakeover(banner));
        }
        else if (e.PropertyName == nameof(MarcadorViewModel.AnnouncementGeneration) && _viewModel is { } viewModel)
        {
            var announcement = viewModel.Announcement;
            Dispatcher.BeginInvoke(() => PlayAnnouncement(announcement));
        }
    }

    // ---------- Anuncio del centro (inicio de periodo, descanso, tanda, final) ----------

    /// <summary>
    /// El texto del periodo (y el titular, si lo hay) aparece con escala y desenfoque, y un brillo suave
    /// cruza la pantalla. Nada opaco tapa los relojes. En el final, el número del ganador late en dorado.
    /// </summary>
    private void PlayAnnouncement(ScoreboardAnnouncement announcement)
    {
        if (announcement == ScoreboardAnnouncement.None)
        {
            return;
        }

        Pop(PeriodLine, PeriodLineScale, PeriodLineBlur, start: 0);
        if (announcement != ScoreboardAnnouncement.PeriodStarted)
        {
            Pop(CenterHeadlineText, HeadlineScale, HeadlineBlur, start: 0.25);
        }

        AnnounceSweep.BeginAnimation(OpacityProperty, Frames((0, 0, null), (0.15, 1, null), (1.3, 1, null), (1.5, 0, null)));
        AnnounceSweepShift.BeginAnimation(TranslateTransform.XProperty, Frames((0, -700, null), (1.5, 2300, EaseOut)));

        if (announcement == ScoreboardAnnouncement.Finished && _viewModel is { } viewModel && viewModel.HomeDimmed != viewModel.AwayDimmed)
        {
            var home = viewModel.AwayDimmed;
            Beat(home ? HomeScoreScale : AwayScoreScale, home ? HomeScoreGlow : AwayScoreGlow, start: 0.6);
        }
    }

    private static void Pop(UIElement element, ScaleTransform scale, BlurEffect blur, double start)
    {
        var grow = Frames((0, 1.8, null), (start, 1.8, null), (start + 0.55, 0.96, EaseOut), (start + 0.75, 1.03, null), (start + 0.9, 1, null));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        blur.BeginAnimation(BlurEffect.RadiusProperty, Frames((0, 22, null), (start, 22, null), (start + 0.55, 0, null)));
        element.BeginAnimation(OpacityProperty, Frames((0, 0, null), (start, 0, null), (start + 0.45, 1, null)));
    }

    /// <summary>El número del equipo late en dorado (escala 1,12 y resplandor), ~1 s desde <paramref name="start"/>.</summary>
    private static void Beat(ScaleTransform scale, DropShadowEffect glow, double start)
    {
        var beat = Frames((0, 1, null), (start, 1, null), (start + 0.35, 1.12, EaseOut), (start + 1.0, 1, null));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, beat);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, beat);
        glow.BeginAnimation(DropShadowEffect.OpacityProperty, Frames((0, 0, null), (start, 0, null), (start + 0.35, 0.95, null), (start + 1.0, 0, null)));
    }

    // ---------- Aviso «toma del lado» (spec §4.2) ----------

    private const double PanelWidth = 691;
    private const double PanelHeight = 946;

    /// <summary>Corte inclinado del 14 % del ancho, siempre del lado que mira al centro.</summary>
    private static readonly Geometry HomeClip = Polygon(new(0, 0), new(PanelWidth, 0), new(PanelWidth * 0.86, PanelHeight), new(0, PanelHeight));
    private static readonly Geometry AwayClip = Polygon(new(PanelWidth * 0.14, 0), new(PanelWidth, 0), new(PanelWidth, PanelHeight), new(0, PanelHeight));

    private static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction EaseIn = Frozen(new CubicEase { EasingMode = EasingMode.EaseIn });

    /// <summary>
    /// Anima el aviso: entra desde el borde de su lado, «aparece» el titular o el gorro, sube la línea
    /// del jugador, ondas (solo gol), brillo, salida y, en gol, el número del equipo late en dorado.
    /// Un aviso nuevo reemplaza al anterior (BeginAnimation reemplaza las animaciones en curso).
    /// </summary>
    private void PlayTakeover(ActionBanner banner)
    {
        var home = banner.IsHome;
        Takeover.HorizontalAlignment = home ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        Takeover.Clip = home ? HomeClip : AwayClip;
        TakeoverBrush.StartPoint = home ? new Point(0, 0) : new Point(1, 0);
        TakeoverBrush.EndPoint = home ? new Point(1, 1) : new Point(0, 1);
        TakeoverContent.Margin = home ? new Thickness(46, 0, 115, 0) : new Thickness(115, 0, 46, 0);
        var offscreen = home ? -PanelWidth - 30 : PanelWidth + 30;

        Takeover.BeginAnimation(VisibilityProperty, VisibleDuring(4.3));
        TakeoverShift.BeginAnimation(TranslateTransform.XProperty,
            Frames((0, offscreen, null), (0.5, 0, EaseOut), (3.7, 0, null), (4.3, offscreen, EaseIn)));

        var pop = Frames((0, 2.2, null), (0.4, 2.2, null), (0.85, 0.96, EaseOut), (1.1, 1.06, null), (1.3, 1, null));
        PopScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        PopScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        PopPart.BeginAnimation(OpacityProperty, Frames((0, 0, null), (0.4, 0, null), (0.85, 1, null)));
        PopBlur.BeginAnimation(BlurEffect.RadiusProperty, Frames((0, 24, null), (0.4, 24, null), (0.85, 0, null)));

        RiseShift.BeginAnimation(TranslateTransform.YProperty, Frames((0, 46, null), (0.8, 46, null), (1.4, 0, EaseOut)));
        RisePart.BeginAnimation(OpacityProperty, Frames((0, 0, null), (0.8, 0, null), (1.4, 1, null)));

        ShineShift.BeginAnimation(TranslateTransform.XProperty, Frames((0, -400, null), (1.1, -400, null), (2.0, 900, null)));

        if (banner.IsGoal)
        {
            Ripple(Ripple1, Ripple1Scale, start: 0.5);
            Ripple(Ripple2, Ripple2Scale, start: 1.0);

            Beat(home ? HomeScoreScale : AwayScoreScale, home ? HomeScoreGlow : AwayScoreGlow, start: 4.3);
        }
        else
        {
            Ripple1.BeginAnimation(OpacityProperty, null);
            Ripple2.BeginAnimation(OpacityProperty, null);
        }
    }

    private static void Ripple(UIElement ring, ScaleTransform scale, double start)
    {
        var grow = Frames((0, 0, null), (start, 0, null), (start + 1.9, 1, EaseOut));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        ring.BeginAnimation(OpacityProperty, Frames((0, 0, null), (start, 0.9, null), (start + 1.9, 0, null)));
    }

    private static DoubleAnimationUsingKeyFrames Frames(params (double Seconds, double Value, IEasingFunction? Ease)[] frames)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        foreach (var (seconds, value, ease) in frames)
        {
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds)), ease));
        }

        animation.Freeze();
        return animation;
    }

    private static ObjectAnimationUsingKeyFrames VisibleDuring(double seconds)
    {
        var animation = new ObjectAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new DiscreteObjectKeyFrame(Visibility.Visible, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new DiscreteObjectKeyFrame(Visibility.Hidden, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds))));
        animation.Freeze();
        return animation;
    }

    private static Geometry Polygon(params Point[] points)
    {
        var figure = new PathFigure { StartPoint = points[0], IsClosed = true };
        foreach (var point in points.Skip(1))
        {
            figure.Segments.Add(new LineSegment(point, isStroked: false));
        }

        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    private static IEasingFunction Frozen(EasingFunctionBase ease)
    {
        ease.Freeze();
        return ease;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScoreboardSettings.SafeMarginPercent))
        {
            Dispatcher.BeginInvoke(UpdateSafeArea);
        }
    }

    /// <summary>Aplica el margen seguro como porcentaje del área útil de la ventana.</summary>
    private void UpdateSafeArea()
    {
        var percent = Math.Clamp(_settings?.SafeMarginPercent ?? 0, 0, 10) / 100.0;
        var horizontal = ActualWidth * percent;
        var vertical = ActualHeight * percent;
        SafeArea.Margin = new Thickness(horizontal, vertical, horizontal, vertical);
    }
}
