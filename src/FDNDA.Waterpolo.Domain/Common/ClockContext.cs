namespace FDNDA.Waterpolo.Domain.Common;

/// <summary>Origen del tiempo asociado a una incidencia.</summary>
public enum ClockSource
{
    Unknown = 0,
    Simulated = 1,
    Replay = 2,
    ColoradoSystem6 = 3,
}

/// <summary>Calidad del contexto de reloj capturado al registrar una incidencia.</summary>
public enum ClockQuality
{
    /// <summary>No se informó contexto de reloj.</summary>
    Unknown = 0,

    /// <summary>Lectura vigente de la fuente en el momento del registro.</summary>
    Live = 1,

    /// <summary>Había fuente configurada, pero sin señal vigente. No hay tiempos.</summary>
    Unavailable = 2,
}

/// <summary>
/// Contexto de reloj de una incidencia. Los tiempos son opcionales y por separado;
/// solo existen cuando la lectura estaba vigente. Nunca se inventa una hora de juego.
/// </summary>
public sealed record ClockContext
{
    public ClockContext(ClockSource source, ClockQuality quality, int? gameClockRemainingMs, int? shotClockRemainingMs)
    {
        if (quality != ClockQuality.Live && (gameClockRemainingMs is not null || shotClockRemainingMs is not null))
        {
            throw new DomainException(
                "reloj.contexto_invalido",
                "Solo una lectura vigente puede llevar tiempos de reloj.");
        }

        if (gameClockRemainingMs is < 0 || shotClockRemainingMs is < 0)
        {
            throw new DomainException("reloj.tiempo_negativo", "Los tiempos de reloj no pueden ser negativos.");
        }

        Source = source;
        Quality = quality;
        GameClockRemainingMs = gameClockRemainingMs;
        ShotClockRemainingMs = shotClockRemainingMs;
    }

    public ClockSource Source { get; }

    public ClockQuality Quality { get; }

    public int? GameClockRemainingMs { get; }

    public int? ShotClockRemainingMs { get; }

    public static ClockContext Unknown { get; } = new(ClockSource.Unknown, ClockQuality.Unknown, null, null);

    public static ClockContext Unavailable(ClockSource source) => new(source, ClockQuality.Unavailable, null, null);
}
