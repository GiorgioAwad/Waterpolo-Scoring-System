using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Storage;

namespace FDNDA.Waterpolo.Infrastructure.Tests.Support;

/// <summary>Reloj controlable: la hora UTC y el reloj monotónico solo avanzan cuando la prueba lo indica.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow = new(2026, 11, 11, 15, 0, 0, TimeSpan.Zero);
    private long _timestamp = 1_000_000;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override long GetTimestamp() => _timestamp;

    public void Advance(TimeSpan elapsed)
    {
        _utcNow += elapsed;
        _timestamp += elapsed.Ticks;
    }

    public void AdvanceMs(int milliseconds) => Advance(TimeSpan.FromMilliseconds(milliseconds));
}

internal sealed class NullLog : IDiagnosticLog
{
    public List<string> Errors { get; } = [];

    public void Info(string message)
    {
    }

    public void Warn(string message)
    {
    }

    public void Error(string message, Exception? exception = null) => Errors.Add(message);
}

/// <summary>Contexto de reloj fijo para las pruebas de registro de incidencias.</summary>
internal sealed class StubClockContext : IClockContextProvider
{
    public ClockContext Context { get; set; } = new(ClockSource.Simulated, ClockQuality.Live, 412_300, 18_700);

    public ClockContext Capture() => Context;
}

/// <summary>Repositorio que falla al escribir, para simular disco lleno o archivo dañado.</summary>
internal sealed class FailingSaveRepository(IMatchRepository inner) : IMatchRepository
{
    public bool FailSaves { get; set; } = true;

    public Task<Match?> GetAsync(Guid matchId, CancellationToken ct = default) => inner.GetAsync(matchId, ct);

    public Task<IReadOnlyList<MatchSummary>> ListAsync(CancellationToken ct = default) => inner.ListAsync(ct);

    public Task<Guid?> FindInProgressMatchIdAsync(CancellationToken ct = default) => inner.FindInProgressMatchIdAsync(ct);

    public Task<bool> IsCommandProcessedAsync(Guid commandId, CancellationToken ct = default) =>
        inner.IsCommandProcessedAsync(commandId, ct);

    public Task SaveAsync(Match match, CommandStamp stamp, CancellationToken ct = default) =>
        FailSaves ? throw new IOException("No hay espacio en el disco (simulado).") : inner.SaveAsync(match, stamp, ct);
}

/// <summary>
/// Almacenamiento SQLite temporal con el esquema migrado y los datos demo. Cada prueba usa
/// su propia carpeta; se elimina al terminar.
/// </summary>
internal sealed class TempStorage : IAsyncDisposable
{
    private TempStorage(string root, AppPaths paths)
    {
        Root = root;
        Paths = paths;
    }

    public string Root { get; }

    public AppPaths Paths { get; }

    public ManualTimeProvider Time { get; } = new();

    public NullLog Log { get; } = new();

    public static async Task<TempStorage> CreateAsync(AppProfile profile = AppProfile.Demo)
    {
        var root = Path.Combine(Path.GetTempPath(), "fdnda-waterpolo-tests", Guid.NewGuid().ToString("N"));
        var storage = new TempStorage(root, AppPaths.ForProfile(profile, root));
        await new StorageInitializer(storage.Paths, storage.Time, storage.Log).InitializeAsync();
        return storage;
    }

    /// <summary>Nueva instancia de acceso a la base, como tras reiniciar el programa.</summary>
    public SqliteDatabase OpenDatabase() => new(Paths.DatabasePath);

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // carpeta temporal: se limpia con el sistema
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>Mesa de prueba: casos de uso reales sobre SQLite temporal.</summary>
internal sealed class TestDesk
{
    public TestDesk(TempStorage storage, IMatchRepository? repositoryOverride = null)
    {
        var database = storage.OpenDatabase();
        var catalog = new SqliteCatalogRepository(database);
        Repository = repositoryOverride ?? new SqliteMatchRepository(database);
        Catalog = catalog;
        Hub = new MatchSnapshotHub();
        Hub.Published += (_, published) => Publications.Add(published);
        Operations = new MatchOperations(Repository, catalog, Hub, Clock, storage.Time, storage.Log);
        Queries = new MatchQueries(Repository);
    }

    public static Guid MatchId => DemoDataSeeder.FirstMatchId;

    public IMatchRepository Repository { get; }

    public SqliteCatalogRepository Catalog { get; }

    public MatchSnapshotHub Hub { get; }

    public List<MatchSnapshotPublished> Publications { get; } = [];

    public StubClockContext Clock { get; } = new();

    public MatchOperations Operations { get; }

    public MatchQueries Queries { get; }

    public OperatorRef Operator { get; private set; } = null!;

    public async Task<TestDesk> SignInAsync(TempStorage storage, string name = "Operadora de prueba")
    {
        var service = new Application.Operators.OperatorService(Catalog, storage.Time);
        Operator = (await service.SignInAsync(name)).ToRef();
        return this;
    }

    public async Task<MatchSnapshot> StartAsync()
    {
        var result = await Operations.StartMatchAsync(new StartMatch(Guid.NewGuid(), MatchId, Operator));
        Assert.Equal(CommandOutcome.Applied, result.Outcome);
        return result.Snapshot!;
    }

    public Task<CommandResult> GoalAsync(TeamSide side, Guid rosterEntryId, Guid? commandId = null) =>
        Operations.RegisterGoalAsync(new RegisterGoal(commandId ?? Guid.NewGuid(), MatchId, side, rosterEntryId, Operator));
}
