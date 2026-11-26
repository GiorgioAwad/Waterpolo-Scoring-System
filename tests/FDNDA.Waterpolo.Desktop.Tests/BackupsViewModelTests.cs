using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Backups;
using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Desktop.ViewModels;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;

namespace FDNDA.Waterpolo.Desktop.Tests;

/// <summary>R5 — pestaña «Resultados y respaldos» y respaldos automáticos de la mesa (RF10).</summary>
public class BackupsViewModelTests
{
    [Fact]
    public async Task Iniciar_y_finalizar_desde_la_mesa_crean_respaldos_automaticos()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (_, mesa, backups, _) = await CreateAsync(storage);

        await mesa.StartMatchCommand.ExecuteAsync(null);
        Assert.Equal("Partido iniciado: periodo 1. Respaldo previo creado.", mesa.StatusMessage);
        await mesa.FinishMatchCommand.ExecuteAsync(null);

        Assert.Equal("Partido finalizado. Respaldo de cierre creado.", mesa.StatusMessage);
        Assert.Equal(["Al finalizar el partido", "Antes del partido"], backups.Items.Select(i => i.ReasonText));
        Assert.Equal("2 respaldos en la carpeta", backups.ListTitle);
    }

    [Fact]
    public async Task Con_los_respaldos_automaticos_desactivados_no_se_crea_ninguno()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (_, mesa, backups, _) = await CreateAsync(storage);
        backups.AutoBackupEnabled = false;

        await mesa.StartMatchCommand.ExecuteAsync(null);
        await mesa.FinishMatchCommand.ExecuteAsync(null);

        Assert.Equal("Partido finalizado.", mesa.StatusMessage);
        Assert.Empty(backups.Items);
    }

    [Fact]
    public async Task Si_el_respaldo_previo_falla_se_pregunta_y_al_decir_no_el_partido_no_se_inicia()
    {
        await using var storage = await TempStorage.CreateAsync();
        var prompts = new Prompts { Answer = false };
        var (desk, mesa, _, _) = await CreateAsync(storage, prompts: prompts, failingStore: true);

        await mesa.StartMatchCommand.ExecuteAsync(null);

        Assert.Equal(1, prompts.Confirmations);
        Assert.True(mesa.IsStatusError);
        Assert.Contains("no se pudo crear el respaldo previo", mesa.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(MatchStatus.Scheduled, (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!.Status);

        prompts.Answer = true; // el operador decide iniciar sin respaldo
        await mesa.StartMatchCommand.ExecuteAsync(null);
        Assert.Equal("Partido iniciado: periodo 1. Sin respaldo previo (falló).", mesa.StatusMessage);
    }

    [Fact]
    public async Task Restaurar_desde_la_pestaña_recarga_la_mesa_y_el_catalogo()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (desk, mesa, backups, dialogs) = await CreateAsync(storage);
        await backups.CreateBackupCommand.ExecuteAsync(null);
        Assert.StartsWith("Respaldo creado: respaldo-manual-", backups.StatusMessage, StringComparison.Ordinal);
        var catalogOps = new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true);
        Assert.True((await catalogOps.RenameTournamentAsync(DemoDataSeeder.TournamentId, "Nombre posterior al respaldo")).IsSuccess);
        await mesa.StartMatchCommand.ExecuteAsync(null);
        mesa.SelectedHomePlayer = mesa.HomeRoster[0];
        await mesa.GoalHomeCommand.ExecuteAsync(null);
        await mesa.FinishMatchCommand.ExecuteAsync(null);
        Assert.Equal(1, mesa.HomeScore);

        backups.SelectedBackup = backups.Items.Single(i => i.ReasonText == "Manual");
        await backups.RestoreSelectedCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogs.Confirmations);
        Assert.Contains("2 partidos y 0 incidencias", dialogs.LastQuestion, StringComparison.Ordinal);
        Assert.False(backups.IsStatusError, backups.StatusMessage);
        Assert.StartsWith("Respaldo restaurado.", backups.StatusMessage, StringComparison.Ordinal);
        Assert.StartsWith("Respaldo restaurado: se recargaron partidos, eventos y nóminas.", mesa.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(0, mesa.HomeScore);
        Assert.Equal("PROGRAMADO", mesa.StageText.ToUpperInvariant());
        Assert.True(mesa.StartMatchCommand.CanExecute(null));
        Assert.Empty(mesa.Incidents);
        Assert.Equal("Torneo Demostración FDNDA", mesa.Catalog!.Tournaments.Single().Name);
        Assert.Contains(backups.Items, i => i.ReasonText == "Base anterior a una restauración");
    }

    [Fact]
    public async Task Con_un_partido_en_juego_no_se_restaura_ni_se_pregunta()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (desk, mesa, backups, dialogs) = await CreateAsync(storage);
        await backups.CreateBackupCommand.ExecuteAsync(null);
        await mesa.StartMatchCommand.ExecuteAsync(null);

        backups.SelectedBackup = backups.Items.Single(i => i.ReasonText == "Manual");
        await backups.RestoreSelectedCommand.ExecuteAsync(null);

        Assert.Equal(0, dialogs.Confirmations);
        Assert.True(backups.IsStatusError);
        Assert.Contains("Hay un partido en juego", backups.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(MatchStatus.InProgress, (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!.Status);
    }

    [Fact]
    public async Task Cancelar_la_confirmacion_no_restaura_y_un_respaldo_de_otra_instalacion_se_elige_por_archivo()
    {
        await using var other = await TempStorage.CreateAsync();
        var otherDesk = await new TestDesk(other).SignInAsync(other);
        var otherCatalog = new CatalogOperations(otherDesk.Catalog, other.Time, other.Log, demoData: true);
        Assert.True((await otherCatalog.RenameTournamentAsync(DemoDataSeeder.TournamentId, "Copa de la otra laptop")).IsSuccess);
        var file = (await new BackupOperations(
            new SqliteBackupStore(other.OpenDatabase(), other.Paths.BackupDirectory, other.Time),
            otherDesk.Repository, other.Log, AppProfile.Demo).CreateAsync(BackupReason.Manual)).Path!;

        await using var storage = await TempStorage.CreateAsync();
        var (_, mesa, backups, dialogs) = await CreateAsync(storage);
        dialogs.File = file;
        dialogs.Answer = false;
        await backups.RestoreFromFileCommand.ExecuteAsync(null);
        Assert.Equal("Restauración cancelada: no se cambió nada.", backups.StatusMessage);
        Assert.Empty(backups.Items);

        dialogs.Answer = true;
        await backups.RestoreFromFileCommand.ExecuteAsync(null);

        Assert.Equal(storage.Paths.BackupDirectory, dialogs.InitialDirectory);
        Assert.StartsWith("Respaldo restaurado.", backups.StatusMessage, StringComparison.Ordinal);
        Assert.Equal("Copa de la otra laptop", mesa.Catalog!.Tournaments.Single().Name);
    }

    // ---------- Apoyo ----------

    private static async Task<(TestDesk Desk, MesaViewModel Mesa, BackupsViewModel Backups, BackupDialogs Dialogs)> CreateAsync(
        TempStorage storage, Prompts? prompts = null, bool failingStore = false)
    {
        var desk = await new TestDesk(storage).SignInAsync(storage);
        IBackupStore store = new SqliteBackupStore(storage.OpenDatabase(), storage.Paths.BackupDirectory, storage.Time);
        if (failingStore)
        {
            store = new FailingCreateStore(store);
        }

        var dialogs = new BackupDialogs();
        var backups = new BackupsViewModel(new BackupOperations(store, desk.Repository, storage.Log, AppProfile.Demo), dialogs);
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new ImmediateContext());
        try
        {
            var catalog = new CatalogViewModel(new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true), desk.Operations);
            var mesa = new MesaViewModel(desk.Operations, desk.Queries, desk.Hub, new FixedTiming(), null, desk.Operator,
                new ScoreboardHost(), prompts ?? new Prompts(), new ScoreboardSettings(),
                new AppInfo("Prueba", AppProfile.Demo, storage.Root, storage.Paths.DatabasePath, 2, "", desk.Operator.Name))
            {
                Catalog = catalog,
                Backups = backups,
            };
            await mesa.InitializeAsync();
            await catalog.InitializeAsync();
            await backups.InitializeAsync();
            return (desk, mesa, backups, dialogs);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class FailingCreateStore(IBackupStore inner) : IBackupStore
    {
        public string BackupDirectory => inner.BackupDirectory;

        public Task<BackupFile> CreateAsync(BackupReason reason, CancellationToken ct = default) =>
            throw new IOException("No hay espacio en el disco (simulado).");

        public Task<IReadOnlyList<BackupFile>> ListAsync(CancellationToken ct = default) => inner.ListAsync(ct);

        public Task<BackupInspection> InspectAsync(string path, CancellationToken ct = default) => inner.InspectAsync(path, ct);

        public Task<RestoreOutcome> RestoreAsync(string path, CancellationToken ct = default) => inner.RestoreAsync(path, ct);
    }

    private sealed class BackupDialogs : IBackupDialogs
    {
        public string? File { get; set; }

        public bool Answer { get; set; } = true;

        public int Confirmations { get; private set; }

        public string LastQuestion { get; private set; } = string.Empty;

        public string? InitialDirectory { get; private set; }

        public string? PickBackupFile(string initialDirectory)
        {
            InitialDirectory = initialDirectory;
            return File;
        }

        public bool Confirm(string title, string message)
        {
            Confirmations++;
            LastQuestion = message;
            return Answer;
        }

        public void OpenFolder(string path)
        {
        }
    }

    private sealed class ImmediateContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }

    private sealed class FixedTiming : ITimingFeed
    {
        public TimingView Current => TimingView.Initial(ClockSource.Simulated, "DEMO / SIMULACIÓN");

        public event EventHandler<TimingView>? Changed { add { } remove { } }
    }

    private sealed class ScoreboardHost : IScoreboardHost
    {
        public bool IsOpen => false;

        public IReadOnlyList<MonitorOption> GetMonitors() => [];

        public void Open(MonitorOption? monitor, bool fullScreen)
        {
        }

        public void Close()
        {
        }

        public event EventHandler<string>? Notice { add { } remove { } }

        public event EventHandler? StateChanged { add { } remove { } }
    }

    private sealed class Prompts : IPromptService
    {
        public bool Answer { get; set; } = true;

        public int Confirmations { get; private set; }

        public string? AskReason(string title, string message) => "Motivo ficticio";

        public bool Confirm(string title, string message)
        {
            Confirmations++;
            return title == "Finalizar partido" || Answer;
        }

        public void OpenFolder(string path)
        {
        }
    }
}
