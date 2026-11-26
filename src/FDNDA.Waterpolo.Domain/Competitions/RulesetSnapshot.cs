using FDNDA.Waterpolo.Domain.Common;

namespace FDNDA.Waterpolo.Domain.Competitions;

/// <summary>
/// Reglas configurables de un evento. Se copian al partido al iniciarlo para que
/// los cambios posteriores del evento no alteren partidos iniciados o cerrados.
/// Los parámetros de posesión solo alimentan la simulación: nunca cambian el Colorado.
/// Prórroga y tanda son parámetros del evento pendientes de aprobación técnica: el programa
/// no decide cuándo corresponden; solo impide combinaciones incoherentes (por ejemplo, una
/// tanda con el resultado desempatado).
/// </summary>
public sealed record RulesetSnapshot
{
    public RulesetSnapshot(
        string presetName,
        bool isDemoPreset,
        int periodCount,
        int periodDurationSeconds,
        int breakDurationSeconds,
        int halftimeDurationSeconds,
        int timeoutsPerTeam,
        int shotClockSeconds,
        int shotClockAlternateSeconds,
        bool penaltyShootoutEnabled,
        int overtimePeriodCount = 0,
        int overtimePeriodDurationSeconds = DefaultOvertimePeriodDurationSeconds)
    {
        if (string.IsNullOrWhiteSpace(presetName))
        {
            throw new DomainException("reglas.sin_nombre", "Las reglas deben tener un nombre de preset.");
        }

        Require(periodCount is >= 1 and <= PeriodNumber.Max, $"La cantidad de periodos debe estar entre 1 y {PeriodNumber.Max}.");
        Require(periodDurationSeconds is >= 60 and <= 3600, "La duración del periodo debe estar entre 1 y 60 minutos.");
        Require(breakDurationSeconds is >= 0 and <= 3600, "El descanso entre periodos debe estar entre 0 y 60 minutos.");
        Require(halftimeDurationSeconds is >= 0 and <= 3600, "El descanso de medio tiempo debe estar entre 0 y 60 minutos.");
        Require(timeoutsPerTeam is >= 0 and <= 10, "Los tiempos muertos por equipo deben estar entre 0 y 10.");
        Require(shotClockSeconds is >= 5 and <= 120, "La posesión debe estar entre 5 y 120 segundos.");
        Require(
            shotClockAlternateSeconds is >= 5 and <= 120 && shotClockAlternateSeconds <= shotClockSeconds,
            "La posesión reducida debe estar entre 5 y 120 segundos y no superar la posesión completa.");
        Require(
            overtimePeriodCount >= 0 && periodCount + overtimePeriodCount <= PeriodNumber.Max,
            $"Los periodos de prórroga no pueden ser negativos y, sumados a los reglamentarios, no deben pasar de {PeriodNumber.Max}.");
        Require(overtimePeriodDurationSeconds is >= 60 and <= 3600, "La duración de la prórroga debe estar entre 1 y 60 minutos.");

        PresetName = presetName.Trim();
        IsDemoPreset = isDemoPreset;
        PeriodCount = periodCount;
        PeriodDurationSeconds = periodDurationSeconds;
        BreakDurationSeconds = breakDurationSeconds;
        HalftimeDurationSeconds = halftimeDurationSeconds;
        TimeoutsPerTeam = timeoutsPerTeam;
        ShotClockSeconds = shotClockSeconds;
        ShotClockAlternateSeconds = shotClockAlternateSeconds;
        PenaltyShootoutEnabled = penaltyShootoutEnabled;
        OvertimePeriodCount = overtimePeriodCount;
        OvertimePeriodDurationSeconds = overtimePeriodDurationSeconds;
    }

    /// <summary>Duración ilustrativa de cada periodo de prórroga (3 min), no reglamentaria.</summary>
    public const int DefaultOvertimePeriodDurationSeconds = 3 * 60;

    public string PresetName { get; }

    public bool IsDemoPreset { get; }

    public int PeriodCount { get; }

    public int PeriodDurationSeconds { get; }

    public int BreakDurationSeconds { get; }

    public int HalftimeDurationSeconds { get; }

    public int TimeoutsPerTeam { get; }

    public int ShotClockSeconds { get; }

    public int ShotClockAlternateSeconds { get; }

    public bool PenaltyShootoutEnabled { get; }

    /// <summary>Periodos de prórroga que se pueden jugar tras un empate reglamentario; 0 = sin prórroga.</summary>
    public int OvertimePeriodCount { get; }

    public int OvertimePeriodDurationSeconds { get; }

    /// <summary>Periodos reglamentarios más los de prórroga.</summary>
    public int MaxPeriods => PeriodCount + OvertimePeriodCount;

    /// <summary>Verdadero si el periodo indicado es de prórroga.</summary>
    public bool IsOvertime(int period) => period > PeriodCount;

    /// <summary>
    /// Nombre de las reglas que el operador escribe para un evento cuando no coinciden con un preset.
    /// Dice expresamente que no tienen aprobación técnica: el programa no declara homologación.
    /// </summary>
    public const string CustomPresetName = "Personalizadas del evento (sin aprobación técnica)";

    /// <summary>
    /// Reglas escritas por el operador para un evento. Si los valores coinciden con el preset demo
    /// se guardan como el preset demo (sigue identificado como no reglamentario); si no, como
    /// reglas personalizadas sin aprobación técnica.
    /// </summary>
    public static RulesetSnapshot ForEvent(
        int periodCount,
        int periodDurationSeconds,
        int breakDurationSeconds,
        int halftimeDurationSeconds,
        int timeoutsPerTeam,
        int shotClockSeconds,
        int shotClockAlternateSeconds,
        bool penaltyShootoutEnabled,
        int overtimePeriodCount = 0,
        int overtimePeriodDurationSeconds = DefaultOvertimePeriodDurationSeconds)
    {
        var custom = new RulesetSnapshot(
            CustomPresetName,
            isDemoPreset: false,
            periodCount,
            periodDurationSeconds,
            breakDurationSeconds,
            halftimeDurationSeconds,
            timeoutsPerTeam,
            shotClockSeconds,
            shotClockAlternateSeconds,
            penaltyShootoutEnabled,
            overtimePeriodCount,
            overtimePeriodDurationSeconds);
        var demo = DemoPreset();
        return custom.HasSameValues(demo) ? demo : custom;
    }

    /// <summary>
    /// Compara solo los valores de juego, no el nombre ni la marca de demo. Sin prórroga, su
    /// duración no importa.
    /// </summary>
    public bool HasSameValues(RulesetSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return PeriodCount == other.PeriodCount
            && PeriodDurationSeconds == other.PeriodDurationSeconds
            && BreakDurationSeconds == other.BreakDurationSeconds
            && HalftimeDurationSeconds == other.HalftimeDurationSeconds
            && TimeoutsPerTeam == other.TimeoutsPerTeam
            && ShotClockSeconds == other.ShotClockSeconds
            && ShotClockAlternateSeconds == other.ShotClockAlternateSeconds
            && PenaltyShootoutEnabled == other.PenaltyShootoutEnabled
            && OvertimePeriodCount == other.OvertimePeriodCount
            && (OvertimePeriodCount == 0 || OvertimePeriodDurationSeconds == other.OvertimePeriodDurationSeconds);
    }

    /// <summary>
    /// Preset de demostración. Sus valores son ilustrativos y NO representan un reglamento aprobado.
    /// El preset de competencia queda pendiente de aprobación del área técnica.
    /// </summary>
    public static RulesetSnapshot DemoPreset() => new(
        presetName: "DEMO (valores ilustrativos, no reglamentarios)",
        isDemoPreset: true,
        periodCount: 4,
        periodDurationSeconds: 8 * 60,
        breakDurationSeconds: 2 * 60,
        halftimeDurationSeconds: 3 * 60,
        timeoutsPerTeam: 2,
        shotClockSeconds: 30,
        shotClockAlternateSeconds: 20,
        penaltyShootoutEnabled: false,
        overtimePeriodCount: 0,
        overtimePeriodDurationSeconds: DefaultOvertimePeriodDurationSeconds);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new DomainException("reglas.valor_invalido", message);
        }
    }
}
