using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Backups;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Application.Identity;
using FDNDA.Waterpolo.Application.Operators;
using FDNDA.Waterpolo.Application.Reports;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Desktop.Services;
using FDNDA.Waterpolo.Desktop.ViewModels;
using FDNDA.Waterpolo.Desktop.Views;
using FDNDA.Waterpolo.Infrastructure.Diagnostics;
using FDNDA.Waterpolo.Infrastructure.Export;
using FDNDA.Waterpolo.Infrastructure.Identity;
using FDNDA.Waterpolo.Infrastructure.Import;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Storage;
using FDNDA.Waterpolo.Infrastructure.Timing;
using FDNDA.Waterpolo.Infrastructure.Timing.Colorado;

namespace FDNDA.Waterpolo.Desktop;

/// <summary>
/// Punto de arranque y de composición: el único lugar de la interfaz que conoce la
/// infraestructura. Un proceso, un escritor de partido y una base local; sin red.
/// </summary>
public partial class App : System.Windows.Application
{
    private const string AppTitle = "Marcador de Waterpolo FDNDA";

    private InstanceLock? _instanceLock;
    private TimingMonitor? _timing;
    private ScoreboardWindowService? _scoreboard;
    private MesaViewModel? _mesa;
    private IdentityOperations? _identity;
    private IDiagnosticLog? _log;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Hasta mostrar la mesa, cerrar un diálogo no debe terminar el programa.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnUnhandledException;

        var culture = CultureInfo.GetCultureInfo("es-PE");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        // WPF dispara las teclas de acceso con solo la letra cuando el foco no está en un
        // cuadro de texto. En una mesa de control eso permitiría cambiar el partido con una
        // pulsación accidental, así que las letras solo actúan junto con Alt.
        EventManager.RegisterClassHandler(
            typeof(UIElement), AccessKeyManager.AccessKeyPressedEvent, new AccessKeyPressedEventHandler(RequireAltForAccessKeys));

        try
        {
            if (!await StartAsync(e.Args))
            {
                Shutdown(1);
            }
        }
        catch (Exception ex)
        {
            _log?.Error("El programa no pudo iniciar.", ex);
            ShowError($"El programa no pudo iniciar.\n\n{ex.Message}");
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mesa?.Dispose();
        _scoreboard?.Dispose();
        _identity?.Dispose();

        try
        {
            // Detiene el bucle de lectura y libera la fuente de tiempo.
            _timing?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException ex)
        {
            _log?.Error("Error al cerrar la fuente de tiempo.", ex.InnerException ?? ex);
        }

        _log?.Info("Programa cerrado.");
        _instanceLock?.Dispose();
        base.OnExit(e);
    }

    private async Task<bool> StartAsync(string[] args)
    {
        StartupOptions options;
        try
        {
            options = StartupOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            ShowError(ex.Message);
            return false;
        }

        var time = TimeProvider.System;
        var paths = AppPaths.ForProfile(options.Profile, options.DataRoot);

        // Una sola instancia operadora por carpeta de datos.
        _instanceLock = InstanceLock.TryAcquire(paths.LockFilePath);
        if (_instanceLock is null)
        {
            ShowError("El Marcador de Waterpolo ya está abierto con estos datos. Use la ventana que ya está en ejecución.");
            return false;
        }

        var log = new RotatingFileLog(paths.LogDirectory, time);
        _log = log;
        log.Info($"Inicio. Versión {GetVersion()}, perfil {options.Profile}.");

        // Almacenamiento: proveedor, migraciones y datos demo, una vez y antes de habilitar la mesa.
        StorageReport storage;
        try
        {
            storage = await Task.Run(() => new StorageInitializer(paths, time, log).InitializeAsync());
        }
        catch (Exception ex)
        {
            log.Error("No se pudo preparar el almacenamiento local.", ex);
            ShowError(
                "No se pudo preparar el almacenamiento local. El programa no se abrirá para no arriesgar los datos.\n\n" +
                $"Carpeta: {paths.DataDirectory}\nDetalle: {ex.Message}");
            return false;
        }

        var database = new SqliteDatabase(paths.DatabasePath);
        var matchRepository = new SqliteMatchRepository(database);
        var catalog = new SqliteCatalogRepository(database);

        // Identificación local del operador.
        var operators = new OperatorService(catalog, time);
        var operatorName = options.OperatorName;
        if (string.IsNullOrWhiteSpace(operatorName))
        {
            var known = await operators.ListAsync();
            var dialog = new OperadorWindow(known.Select(o => o.DisplayName));
            if (dialog.ShowDialog() != true)
            {
                return false;
            }

            operatorName = dialog.OperatorName;
        }

        var operatorProfile = await operators.SignInAsync(operatorName);

        // Fuente de tiempo configurada por perfil. En competencia nunca se cambia sola a demo.
        // En demo, una grabación elegida al arrancar reemplaza al simulador (R9).
        ITimingSource source = options.Profile != AppProfile.Demo
            ? options.ConsolePort is { } port
                ? new ColoradoSystem6TimingSource(() => new SerialColoradoTransport(port), time, log)
                : new ColoradoSystem6TimingSource()
            : options.ReplayPath is { } replayPath
                ? new ReplayTimingSource(System.IO.Path.GetFullPath(replayPath), time)
                : new SimulatedTimingSource(time);
        _timing = new TimingMonitor(source, time, new TimingOptions(), log);
        await _timing.StartAsync();

        // Casos de uso y estado compartido entre mesa y marcador.
        var hub = new MatchSnapshotHub();
        _timing.FollowMatch(hub); // en el descanso de la mesa, el silencio de la consola no es pérdida
        var operations = new MatchOperations(matchRepository, catalog, hub, _timing, time, log);
        var queries = new MatchQueries(matchRepository);
        var catalogOperations = new CatalogOperations(
            catalog, time, log, demoData: options.Profile == AppProfile.Demo, new RosterFileReader());
        var identityHub = new ScoreboardIdentityHub();
        _identity = new IdentityOperations(
            new SqliteIdentityRepository(database), catalog, new ImageFileReader(), new ExampleIdentitySource(), hub, identityHub, time, log);
        var catalogViewModel = new CatalogViewModel(catalogOperations, operations, new CatalogDialogs(), _identity)
        {
            Operator = operatorProfile.ToRef(),
            Queries = queries,
        };
        var backups = new BackupOperations(
            new SqliteBackupStore(database, paths.BackupDirectory, time), matchRepository, log, options.Profile);
        var backupsViewModel = new BackupsViewModel(backups, new BackupDialogs());
        var reports = new ReportOperations(matchRepository, catalog, new ReportFileWriter(time), time, log);
        var resultsViewModel = new ResultsViewModel(reports, catalogOperations, new ReportDialogs());
        var settings = new ScoreboardSettings();
        var timing = _timing;
        _scoreboard = new ScoreboardWindowService(() => new MarcadorViewModel(hub, timing, settings, identityHub), log);

        var info = new AppInfo(
            GetVersion(),
            options.Profile,
            paths.DataDirectory,
            paths.DatabasePath,
            storage.SchemaVersion,
            log.CurrentFilePath,
            operatorProfile.DisplayName,
            paths.OriginText);

        _mesa = new MesaViewModel(
            operations,
            queries,
            hub,
            _timing,
            source as ISimulationControl,
            operatorProfile.ToRef(),
            _scoreboard,
            new PromptService(),
            settings,
            info)
        {
            Catalog = catalogViewModel,
            Backups = backupsViewModel,
            Results = resultsViewModel,
            Connections = new ConnectionStatusViewModel(timing, _scoreboard, new StorageProbe(paths.DatabasePath), time),
            Identity = identityHub,
            Replay = source as IReplayControl,
        };

        var window = new MesaWindow { DataContext = _mesa };
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();

        await _mesa.InitializeAsync();
        await catalogViewModel.InitializeAsync();
        await backupsViewModel.InitializeAsync();
        return true;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Error("Error no controlado en la interfaz.", e.Exception);
        ShowError($"Ocurrió un error inesperado. El último estado confirmado del partido está guardado.\n\n{e.Exception.Message}");
        e.Handled = true;
    }

    private static void RequireAltForAccessKeys(object sender, AccessKeyPressedEventArgs e)
    {
        // Enter y Esc (botones predeterminado y de cancelar) también pasan por aquí: no se tocan.
        var isLetterOrDigit = e.Key is { Length: 1 } key && char.IsLetterOrDigit(key[0]);
        if (!e.Handled && e.Scope is null && isLetterOrDigit && (Keyboard.Modifiers & ModifierKeys.Alt) != ModifierKeys.Alt)
        {
            e.Target = null;
            e.Handled = true;
        }
    }

    private static string GetVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "desconocida";

    private static void ShowError(string message) =>
        MessageBox.Show(message, AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
}
