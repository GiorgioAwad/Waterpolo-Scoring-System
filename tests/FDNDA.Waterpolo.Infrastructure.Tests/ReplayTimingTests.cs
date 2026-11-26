using System.Text;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;
using FDNDA.Waterpolo.Infrastructure.Timing;

namespace FDNDA.Waterpolo.Infrastructure.Tests;

/// <summary>R9 — reproducción determinista de grabaciones normalizadas (RF11, preparación del hito B).</summary>
public class ReplayTimingTests
{
    private static readonly TimingOptions Options = new() { StaleAfter = TimeSpan.FromMilliseconds(1500) };

    /// <summary>El bucle interno no despierta durante la prueba: la prueba decide cuándo entregar.</summary>
    private static readonly TimeSpan NoLoop = TimeSpan.FromHours(1);

    private const string Valid = """
        # comentario
        formato,fdnda-cronometraje-normalizado,1
        t_ms,sesion,partido_ms,posesion_ms,en_marcha,periodo,precision
        0,1,480000,30000,0,1,decimas
        1000,1,480000,30000,1,1,decimas
        2000,1,479000,29000,1,,decimas
        6000,2,475000,25000,1,1,segundos
        7000,2,474000,,1,1,segundos
        fin,10000
        """;

    [Fact]
    public void La_grabacion_de_ejemplo_se_lee_completa()
    {
        var recording = ReplayTimingSource.Load(SamplePath());

        Assert.Equal("guion-sintetico.csv", recording.Name);
        Assert.Equal(TimeSpan.FromSeconds(38), recording.Duration);
        Assert.Equal([1, 2], recording.Samples.Select(s => s.Session).Distinct());
        Assert.Equal((480_000, 30_000), (recording.Samples[0].GameClockRemainingMs, recording.Samples[0].ShotClockRemainingMs));
        Assert.DoesNotContain(recording.Samples, s => s.At >= TimeSpan.FromSeconds(20) && s.At < TimeSpan.FromSeconds(24));
        Assert.All(recording.Samples.Where(s => s.At >= TimeSpan.FromSeconds(30)), s => Assert.Null(s.ShotClockRemainingMs));
    }

    [Theory]
    [InlineData("", "vacío")]
    [InlineData("t_ms,sesion\n0,1", "Línea 1: se esperaba «formato,fdnda-cronometraje-normalizado,1»")]
    [InlineData("formato,fdnda-cronometraje-normalizado,1\nt,s\n", "Línea 2: se esperaba el encabezado")]
    [InlineData("formato,fdnda-cronometraje-normalizado,1\nt_ms,sesion,partido_ms,posesion_ms,en_marcha,periodo,precision\n", "no tiene ninguna muestra")]
    [InlineData("formato,fdnda-cronometraje-normalizado,1\nt_ms,sesion,partido_ms,posesion_ms,en_marcha,periodo,precision\n0,1,480000,30000,0,1\n", "Línea 3: tiene 6 columnas")]
    [InlineData("formato,fdnda-cronometraje-normalizado,1\nt_ms,sesion,partido_ms,posesion_ms,en_marcha,periodo,precision\n500,1,1,1,0,1,decimas\n400,1,1,1,0,1,decimas\n", "Línea 4: t_ms es menor")]
    [InlineData("formato,fdnda-cronometraje-normalizado,1\nt_ms,sesion,partido_ms,posesion_ms,en_marcha,periodo,precision\n0,2,1,1,0,1,decimas\n1,1,1,1,0,1,decimas\n", "Línea 4: sesion no puede volver")]
    [InlineData("formato,fdnda-cronometraje-normalizado,1\nt_ms,sesion,partido_ms,posesion_ms,en_marcha,periodo,precision\n0,1,-5,1,0,1,decimas\n", "Línea 3: partido_ms")]
    [InlineData("formato,fdnda-cronometraje-normalizado,1\nt_ms,sesion,partido_ms,posesion_ms,en_marcha,periodo,precision\n0,1,7200001,,0,1,decimas\n", "Línea 3: partido_ms")]
    [InlineData("formato,fdnda-cronometraje-normalizado,1\nt_ms,sesion,partido_ms,posesion_ms,en_marcha,periodo,precision\n0,1,,,0,1,decimas\n", "no trae ningún reloj")]
    [InlineData("formato,fdnda-cronometraje-normalizado,1\nt_ms,sesion,partido_ms,posesion_ms,en_marcha,periodo,precision\n0,1,1,1,si,1,decimas\n", "en_marcha debe ser 1, 0 o vacío")]
    [InlineData("formato,fdnda-cronometraje-normalizado,1\nt_ms,sesion,partido_ms,posesion_ms,en_marcha,periodo,precision\n0,1,1,1,0,21,decimas\n", "periodo debe estar vacío o entre 1 y 20")]
    [InlineData("formato,fdnda-cronometraje-normalizado,1\nt_ms,sesion,partido_ms,posesion_ms,en_marcha,periodo,precision\n0,1,1,1,0,1,tenths\n", "precision «tenths» no es válida")]
    [InlineData("formato,fdnda-cronometraje-normalizado,1\nt_ms,sesion,partido_ms,posesion_ms,en_marcha,periodo,precision\n900,1,1,1,0,1,decimas\nfin,800\n", "«fin» no puede ser anterior")]
    [InlineData("formato,fdnda-cronometraje-normalizado,1\nt_ms,sesion,partido_ms,posesion_ms,en_marcha,periodo,precision\n0,1,1,1,0,1,decimas\nfin,800\n900,1,1,1,0,1,decimas\n", "Línea 5: no puede haber datos después de «fin»")]
    public void Un_archivo_invalido_se_rechaza_con_la_linea_y_el_motivo(string text, string expected)
    {
        var ex = Assert.Throws<TimingRecordingException>(() => TimingRecordingParser.Parse(text, "prueba.csv"));
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dos_reproducciones_de_la_misma_grabacion_entregan_lo_mismo_en_el_mismo_instante()
    {
        var recording = TimingRecordingParser.Parse(Valid, "a.csv");

        var first = Play(recording);
        var second = Play(recording);

        Assert.Equal(first, second);
        Assert.Equal(5, first.Count);
        Assert.Equal([0L, 1000, 2000, 6000, 7000], first.Select(s => s.OffsetMs));
    }

    [Fact]
    public async Task La_senal_se_pierde_en_el_hueco_y_vuelve_con_una_sesion_nueva_sin_heredar_valores()
    {
        var time = new ManualTimeProvider();
        await using var source = ReplayTimingSource.FromRecording(TimingRecordingParser.Parse(Valid, "a.csv"), time, NoLoop);
        var tracker = new TimingStateTracker(source.Kind, source.DisplayLabel, time, Options);
        Assert.True((await source.StartAsync()).IsAvailable);

        AdvanceTo(time, source, tracker, 2000);
        var playing = tracker.Evaluate();
        AdvanceTo(time, source, tracker, 4000);
        var lost = tracker.Evaluate();
        AdvanceTo(time, source, tracker, 6000);
        var back = tracker.Evaluate();
        AdvanceTo(time, source, tracker, 7000);
        var shotOff = tracker.Evaluate();
        AdvanceTo(time, source, tracker, 7600); // posesión: 1,6 s sin datos; partido: 0,6 s
        var gameOnly = tracker.Evaluate();
        AdvanceTo(time, source, tracker, 10_000);
        var ended = tracker.Evaluate();

        Assert.Equal((SignalState.Live, 479_000, 29_000, true, ClockSource.Replay, "REPRODUCCIÓN"),
            (playing.Signal, playing.GameClock.RemainingMs, playing.ShotClock.RemainingMs, playing.IsRunning, playing.Source, playing.SourceLabel));
        Assert.True(playing.IsSimulation);
        Assert.Equal((SignalState.Lost, null, null), (lost.Signal, lost.GameClock.RemainingMs, lost.ShotClock.RemainingMs));
        Assert.Equal((SignalState.Live, 475_000, 25_000, TimingPrecision.Seconds),
            (back.Signal, back.GameClock.RemainingMs, back.ShotClock.RemainingMs, back.Precision));
        Assert.Equal((474_000, 25_000), (shotOff.GameClock.RemainingMs, shotOff.ShotClock.RemainingMs)); // la posesión sigue vigente 1,5 s
        Assert.Equal((474_000, null), (gameOnly.GameClock.RemainingMs, gameOnly.ShotClock.RemainingMs));
        Assert.Equal(SignalState.Lost, ended.Signal);
        Assert.True(source.Progress.IsFinished);
        Assert.Equal(TimeSpan.FromSeconds(10), source.Progress.Position);
    }

    [Fact]
    public async Task El_avance_se_avisa_una_vez_por_segundo_tambien_sin_muestras_y_al_terminar()
    {
        var time = new ManualTimeProvider();
        await using var source = ReplayTimingSource.FromRecording(TimingRecordingParser.Parse(Valid, "a.csv"), time, NoLoop);
        var notices = new List<TimeSpan>();
        source.ProgressChanged += (_, _) => notices.Add(source.Progress.Position);
        await source.StartAsync();

        for (var step = 0; step < 120; step++) // 12 s en pasos de 100 ms: pasa por el hueco 2-6 s y por el final (10 s)
        {
            source.Pump();
            time.AdvanceMs(100);
        }

        Assert.Equal(Enumerable.Range(0, 11).Select(s => TimeSpan.FromSeconds(s)), notices.Select(n => TimeSpan.FromSeconds((int)n.TotalSeconds)));
        Assert.Equal(TimeSpan.FromSeconds(10), notices[^1]);
        source.Restart();
        Assert.Equal(TimeSpan.Zero, notices[^1]);
    }

    [Fact]
    public async Task Reiniciar_vuelve_al_comienzo_con_sesiones_nuevas()
    {
        var time = new ManualTimeProvider();
        await using var source = ReplayTimingSource.FromRecording(TimingRecordingParser.Parse(Valid, "a.csv"), time, NoLoop);
        var tracker = new TimingStateTracker(source.Kind, source.DisplayLabel, time, Options);
        await source.StartAsync();
        AdvanceTo(time, source, tracker, 7000);
        var nothingNew = source.Pump();

        time.AdvanceMs(100);
        source.Restart();
        var again = source.Pump();
        time.AdvanceMs(1000);
        var afterRestart = source.Pump();
        foreach (var snapshot in afterRestart)
        {
            Assert.True(tracker.Accept(snapshot));
        }

        Assert.Empty(nothingNew);
        Assert.Empty(again); // Restart ya entregó la muestra del instante 0
        var view = tracker.Evaluate();
        Assert.Equal((SignalState.Live, 480_000, 30_000), (view.Signal, view.GameClock.RemainingMs, view.ShotClock.RemainingMs));
        Assert.Equal(TimeSpan.FromSeconds(1), source.Progress.Position);
    }

    [Fact]
    public async Task Un_archivo_invalido_deja_la_fuente_no_disponible_con_el_motivo_y_sin_relojes()
    {
        await using var storage = await TempStorage.CreateAsync();
        var path = Path.Combine(storage.Root, "roto.csv");
        await File.WriteAllTextAsync(path, "formato,fdnda-cronometraje-normalizado,1\nhola\n", new UTF8Encoding(false));
        await using var monitor = new TimingMonitor(new ReplayTimingSource(path, storage.Time, NoLoop), storage.Time, Options, storage.Log);
        var source = new ReplayTimingSource(path, storage.Time, NoLoop);

        await monitor.StartAsync();
        var result = await source.StartAsync();

        Assert.False(result.IsAvailable);
        Assert.Equal("No se pudo cargar la reproducción «roto.csv». Línea 2: se esperaba el encabezado «t_ms,sesion,partido_ms,posesion_ms,en_marcha,periodo,precision».", result.Reason);
        Assert.Equal((SignalState.Unavailable, ClockSource.Replay), (monitor.Current.Signal, monitor.Current.Source));
        Assert.Equal(result.Reason, monitor.Current.Notice);
        Assert.Null(monitor.Current.GameClock.RemainingMs);
        Assert.False(source.Progress.IsLoaded);
        Assert.Equal(ClockQuality.Unavailable, monitor.Capture().Quality);
        await source.DisposeAsync();
    }

    [Theory]
    [InlineData("no-existe.csv", "No existe el archivo «no-existe.csv».")]
    [InlineData("latin1.csv", "«latin1.csv» no está en UTF-8.")]
    public async Task Un_archivo_ausente_o_que_no_es_UTF8_no_se_reproduce(string file, string expected)
    {
        await using var storage = await TempStorage.CreateAsync();
        var path = Path.Combine(storage.Root, file);
        if (file == "latin1.csv")
        {
            await File.WriteAllBytesAsync(path, [.. Encoding.UTF8.GetBytes("# Grabaci"), 0xF3, (byte)'n', (byte)'\n']);
        }

        await using var source = new ReplayTimingSource(path, storage.Time, NoLoop);
        var result = await source.StartAsync();

        Assert.False(result.IsAvailable);
        Assert.EndsWith(expected, result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Una_incidencia_registrada_durante_la_reproduccion_guarda_el_reloj_con_origen_reproduccion()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var source = ReplayTimingSource.FromRecording(TimingRecordingParser.Parse(Valid, "a.csv"), storage.Time, NoLoop);
        await using var monitor = new TimingMonitor(source, storage.Time, Options, storage.Log);
        var live = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Changed += (_, view) =>
        {
            if (view.Signal == SignalState.Live && view.GameClock.RemainingMs == 479_000)
            {
                live.TrySetResult();
            }
        };
        var operations = new MatchOperations(desk.Repository, desk.Catalog, desk.Hub, monitor, storage.Time, storage.Log);
        var started = (await operations.StartMatchAsync(new StartMatch(Guid.NewGuid(), TestDesk.MatchId, desk.Operator))).Snapshot!;
        await monitor.StartAsync();

        storage.Time.AdvanceMs(2000);
        source.Pump();
        await live.Task.WaitAsync(TimeSpan.FromSeconds(10)); // la lectura ocurre en segundo plano; no avanza el reloj de la prueba
        var goal = await operations.RegisterGoalAsync(new RegisterGoal(
            Guid.NewGuid(), TestDesk.MatchId, TeamSide.Home, started.Home.Roster[0].RosterEntryId, desk.Operator));

        Assert.True(goal.IsSuccess, goal.Message);
        var stored = (await new TestDesk(storage).Queries.GetSnapshotAsync(TestDesk.MatchId))!.Incidents.Single();
        Assert.Equal((ClockSource.Replay, ClockQuality.Live, 479_000, 29_000),
            (stored.Clock.Source, stored.Clock.Quality, stored.Clock.GameClockRemainingMs, stored.Clock.ShotClockRemainingMs));
    }

    // ---------- Apoyo ----------

    private static string SamplePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FDNDA.Waterpolo.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "samples", "cronometraje", "guion-sintetico.csv");
    }

    /// <summary>Avanza en pasos de 100 ms entregando lo que corresponda al tracker.</summary>
    private static void AdvanceTo(ManualTimeProvider time, ReplayTimingSource source, TimingStateTracker tracker, int ms)
    {
        while (source.Progress.Position < TimeSpan.FromMilliseconds(ms))
        {
            time.AdvanceMs(100);
            foreach (var snapshot in source.Pump())
            {
                tracker.Accept(snapshot);
            }
        }
    }

    private sealed record Delivered(long OffsetMs, long AtStepMs, int? Game, int? Shot, bool? Running, int? Period, TimingPrecision Precision, int SessionIndex);

    /// <summary>Reproduce en pasos de 70 ms (no coinciden con las muestras) y anota qué se entregó y cuándo.</summary>
    private static List<Delivered> Play(TimingRecording recording)
    {
        var time = new ManualTimeProvider();
        var source = ReplayTimingSource.FromRecording(recording, time, NoLoop);
        source.StartAsync().GetAwaiter().GetResult();
        var start = time.GetTimestamp();
        var sessions = new List<Guid>();
        var delivered = new List<Delivered>();
        for (var step = 0; step <= 160; step++)
        {
            foreach (var s in source.Pump())
            {
                if (!sessions.Contains(s.SessionId))
                {
                    sessions.Add(s.SessionId);
                }

                delivered.Add(new Delivered(
                    (s.ReceivedTimestamp - start) * 1000 / time.TimestampFrequency, step * 70L, s.GameClockRemainingMs,
                    s.ShotClockRemainingMs, s.IsRunning, s.Period, s.Precision, sessions.IndexOf(s.SessionId)));
            }

            time.AdvanceMs(70);
        }

        source.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return delivered;
    }
}
