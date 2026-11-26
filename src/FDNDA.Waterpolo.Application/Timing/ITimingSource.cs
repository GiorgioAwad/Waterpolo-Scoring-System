using FDNDA.Waterpolo.Domain.Common;

namespace FDNDA.Waterpolo.Application.Timing;

/// <summary>Precisión con la que la fuente entrega los tiempos.</summary>
public enum TimingPrecision
{
    Unknown = 0,
    Seconds = 1,
    Tenths = 2,
    Hundredths = 3,
    Milliseconds = 4,
}

/// <summary>Calidad de una muestra según el decodificador de la fuente.</summary>
public enum SampleQuality
{
    Valid = 1,
    Suspect = 2,
}

/// <summary>
/// Lectura de cronometraje. Los dos relojes van por separado y son opcionales: no se asume
/// que la fuente entregue ambos ni que lleguen en la misma trama. <c>null</c> significa
/// «esta muestra no trae ese reloj», no «el reloj vale cero».
/// </summary>
/// <param name="Source">Fuente que produjo la muestra.</param>
/// <param name="SessionId">Sesión de conexión; cambia en cada reconexión.</param>
/// <param name="GameClockRemainingMs">Tiempo restante de partido en milisegundos, si viene en la muestra.</param>
/// <param name="ShotClockRemainingMs">Tiempo restante de posesión en milisegundos, si viene en la muestra.</param>
/// <param name="Precision">Precisión disponible.</param>
/// <param name="IsRunning">Estado de marcha solo si hay evidencia; nulo si no se sabe.</param>
/// <param name="Period">Periodo informado por la fuente, si está disponible.</param>
/// <param name="Sequence">Número de secuencia interno, creciente durante la vida de la fuente.</param>
/// <param name="ReceivedTimestamp">Marca monotónica de recepción (<see cref="TimeProvider.GetTimestamp"/>).</param>
/// <param name="ReceivedAtUtc">Hora UTC de recepción, solo para auditoría.</param>
/// <param name="Quality">Calidad de la muestra.</param>
/// <param name="TimeoutClockRemainingMs">
/// Reloj de tiempo muerto en milisegundos. Nulo: no hay tiempo muerto en curso o la fuente no lo informa.
/// </param>
public sealed record TimingSnapshot(
    ClockSource Source,
    Guid SessionId,
    int? GameClockRemainingMs,
    int? ShotClockRemainingMs,
    TimingPrecision Precision,
    bool? IsRunning,
    int? Period,
    long Sequence,
    long ReceivedTimestamp,
    DateTimeOffset ReceivedAtUtc,
    SampleQuality Quality,
    int? TimeoutClockRemainingMs = null);

/// <summary>Resultado explícito del arranque de una fuente. Nunca se simula éxito.</summary>
public sealed record TimingStartResult(bool IsAvailable, string? Reason)
{
    public static TimingStartResult Started { get; } = new(true, null);

    public static TimingStartResult Unavailable(string reason) => new(false, reason);
}

/// <summary>
/// Puerto de cronometraje, de solo lectura, independiente de WPF y de SQLite.
/// </summary>
public interface ITimingSource : IAsyncDisposable
{
    ClockSource Kind { get; }

    /// <summary>Etiqueta visible de la fuente (p. ej. «DEMO / SIMULACIÓN»).</summary>
    string DisplayLabel { get; }

    Task<TimingStartResult> StartAsync(CancellationToken ct = default);

    Task StopAsync();

    /// <summary>Flujo cancelable de muestras válidas. Un único consumidor.</summary>
    IAsyncEnumerable<TimingSnapshot> ReadSnapshotsAsync(CancellationToken ct = default);
}

/// <summary>Avance de una reproducción, para mostrarlo en la mesa.</summary>
/// <param name="Position">Tiempo transcurrido de la grabación (no pasa de la duración).</param>
/// <param name="Duration">Duración total de la grabación.</param>
/// <param name="IsLoaded">Falso si la grabación no se pudo cargar.</param>
public readonly record struct ReplayProgress(TimeSpan Position, TimeSpan Duration, bool IsLoaded)
{
    public bool IsFinished => IsLoaded && Position >= Duration;
}

/// <summary>
/// Control de la fuente de reproducción (R9). Solo existe en el perfil demo con una grabación
/// elegida al arrancar; no controla ninguna consola.
/// </summary>
public interface IReplayControl
{
    /// <summary>Nombre del archivo reproducido.</summary>
    string RecordingName { get; }

    ReplayProgress Progress { get; }

    /// <summary>Vuelve al comienzo de la grabación con sesiones nuevas (como una reconexión).</summary>
    void Restart();

    /// <summary>
    /// El avance cambió de segundo, terminó o se reinició. Llega aunque no lleguen muestras (en un
    /// corte o al final), desde cualquier hilo: despachar al hilo de UI.
    /// </summary>
    event EventHandler? ProgressChanged;
}

/// <summary>
/// Controles del simulador. Solo existen en modo demo; imitan lo que en operación real
/// hace la consola, que este programa nunca controla.
/// </summary>
public interface ISimulationControl
{
    void StartClock();

    void StopClock();

    void ResetShotClock();

    /// <summary>Carga el reloj de partido y el de posesión con los valores indicados y los detiene.</summary>
    void LoadPeriod(int gameClockSeconds, int shotClockSeconds);

    /// <summary>Simula la pérdida de la señal: la «consola» sigue contando pero no llegan datos.</summary>
    void DropSignal();

    /// <summary>Simula la reconexión con una sesión nueva.</summary>
    void RestoreSignal();

    /// <summary>Reproduce el guion demo estándar de juego/pausa/reset/pérdida/reconexión.</summary>
    void RunDemoScript();

    /// <summary>Inicia un tiempo muerto de la duración indicada y detiene el reloj de partido.</summary>
    void StartTimeout(int seconds);

    /// <summary>Termina el tiempo muerto en curso (como «Clear Timeout» en la consola).</summary>
    void ClearTimeout();
}
