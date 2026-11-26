using CommunityToolkit.Mvvm.ComponentModel;
using FDNDA.Waterpolo.Application.Identity;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Application.Timing;
using FDNDA.Waterpolo.Desktop.Presentation;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;

namespace FDNDA.Waterpolo.Desktop.ViewModels;

/// <summary>Tipo de aviso animado del marcador; define su color y su forma.</summary>
public enum ActionBannerKind
{
    None = 0,
    Goal = 1,
    Exclusion = 2,
    Timeout = 3,
    Expulsion = 4,
    Foul = 5,

    /// <summary>Fin del tiempo muerto, visto en el reloj de tiempo muerto de la consola.</summary>
    TimeoutEnd = 6,
}

/// <summary>Anuncio del centro al cambiar el momento del partido.</summary>
public enum ScoreboardAnnouncement
{
    None = 0,
    PeriodStarted = 1,
    BreakStarted = 2,
    ShootoutStarted = 3,
    Finished = 4,
}

/// <summary>
/// Aviso animado de una acción recién confirmada. Cubre el lado del equipo («toma del lado») y
/// nunca la columna central de los relojes. El gol usa el color del equipo; exclusión, expulsión y
/// tiempo muerto, colores fijos que dicen el tipo a distancia.
/// </summary>
public sealed record ActionBanner(
    ActionBannerKind Kind,
    TeamSide Side,
    string Headline,
    int? Cap,
    string? PlayerName,
    string TeamName,
    string Background,
    string BackgroundDark,
    string Foreground,
    string FoulLine = "")
{
    public const string ExclusionColor = "#E53935";
    public const string ExclusionDarkColor = "#6A100E";
    public const string ExpulsionColor = "#9E1220";
    public const string ExpulsionDarkColor = "#2E0308";
    public const string TimeoutColor = "#2F80ED";
    public const string TimeoutDarkColor = "#0B2A63";
    public const string FoulColor = "#F2994A";
    public const string FoulDarkColor = "#5C2A06";
    private const string White = "#FFFFFF";

    public bool IsHome => Side == TeamSide.Home;

    public bool IsGoal => Kind == ActionBannerKind.Goal;

    /// <summary>Inicio o fin del tiempo muerto: cronómetro en el círculo y sin jugador.</summary>
    public bool IsTimeout => Kind is ActionBannerKind.Timeout or ActionBannerKind.TimeoutEnd;

    public bool IsExpulsion => Kind == ActionBannerKind.Expulsion;

    /// <summary>Exclusión y expulsión muestran el número de gorro en grande: es lo que el público busca en el agua.</summary>
    public bool ShowsCap => Kind is ActionBannerKind.Exclusion or ActionBannerKind.Expulsion or ActionBannerKind.Foul;

    /// <summary>«2.ª FALTA» en falta personal, exclusión y expulsión.</summary>
    public bool HasFoulLine => FoulLine.Length > 0;

    public string CapText => Cap?.ToString(MatchTexts.Culture) ?? string.Empty;

    /// <summary>El aviso en una línea, para accesibilidad y registros.</summary>
    public string Text
    {
        get
        {
            var player = $"{Cap}  {PlayerName}";
            var fouls = HasFoulLine ? $"  ·  {FoulLine}" : string.Empty;
            var headline = Headline.Replace('\n', ' ');
            return Kind switch
            {
                ActionBannerKind.Goal => $"{Headline}  {TeamName}  ·  {player}",
                ActionBannerKind.Exclusion or ActionBannerKind.Expulsion or ActionBannerKind.Foul =>
                    $"{headline}  ·  {TeamName}  ·  {player}{fouls}",
                ActionBannerKind.Timeout => $"TIEMPO MUERTO  ·  {TeamName}",
                ActionBannerKind.TimeoutEnd => $"FIN DEL TIEMPO MUERTO  ·  {TeamName}",
                _ => string.Empty,
            };
        }
    }

    /// <summary>
    /// Aviso para una publicación, o nulo si no corresponde. Solo anima registros nuevos (gol, penal
    /// convertido, exclusión o expulsión, tiempo muerto) con su tipo activado en la mesa. Corregir,
    /// anular, reabrir, cambiar de periodo o abrir la pantalla nunca animan.
    /// </summary>
    public static ActionBanner? For(MatchSnapshotPublished published, ScoreboardSettings settings)
    {
        ArgumentNullException.ThrowIfNull(published);
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.AnimationsEnabled
            || published.Change.Kind is not (MatchChangeKind.GoalRegistered or MatchChangeKind.IncidentRegistered)
            || published.Change.IncidentId is not { } incidentId)
        {
            return null;
        }

        var snapshot = published.Snapshot;
        var incident = snapshot.Incidents.FirstOrDefault(i => i.IncidentId == incidentId);
        if (incident is null || incident.IsVoided)
        {
            return null;
        }

        var team = incident.Side == TeamSide.Home ? snapshot.Home : snapshot.Away;
        ActionBanner Goal(string headline) => new(
            ActionBannerKind.Goal, incident.Side, headline, incident.Cap, incident.PlayerName, team.Name,
            team.ColorHex, ColorTools.Darken(team.ColorHex, 0.55), ColorTools.TextOn(team.ColorHex));

        // Faltas del jugador ya contando esta: el snapshot se publica después del commit.
        var foulLine = incident.RosterEntryId is { } player ? ScoreboardTexts.FoulLine(snapshot.FoulsOf(player)) : string.Empty;

        return incident.Type switch
        {
            IncidentType.Goal when settings.GoalAnimationsEnabled => Goal("¡GOL!"),
            IncidentType.Penalty when incident.PenaltyResult == PenaltyResult.Converted && settings.GoalAnimationsEnabled =>
                Goal("¡GOL DE PENAL!"),
            IncidentType.Exclusion when settings.ExclusionAnimationsEnabled && incident.ExclusionKind == ExclusionKind.Expulsion =>
                new ActionBanner(ActionBannerKind.Expulsion, incident.Side, "EXPULSIÓN", incident.Cap, incident.PlayerName, team.Name,
                    ExpulsionColor, ExpulsionDarkColor, White, foulLine),
            IncidentType.Exclusion when settings.ExclusionAnimationsEnabled =>
                new ActionBanner(ActionBannerKind.Exclusion, incident.Side, "EXCLUSIÓN", incident.Cap, incident.PlayerName, team.Name,
                    ExclusionColor, ExclusionDarkColor, White, foulLine),
            IncidentType.PersonalFoul when settings.FoulAnimationsEnabled =>
                new ActionBanner(ActionBannerKind.Foul, incident.Side, "FALTA\nPERSONAL", incident.Cap, incident.PlayerName, team.Name,
                    FoulColor, FoulDarkColor, White, foulLine),
            IncidentType.Timeout when settings.TimeoutAnimationsEnabled =>
                new ActionBanner(ActionBannerKind.Timeout, incident.Side, "TIEMPO\nMUERTO", null, null, team.Name,
                    TimeoutColor, TimeoutDarkColor, White),
            _ => null,
        };
    }

    /// <summary>
    /// Aviso de fin de tiempo muerto: el equipo es el del último tiempo muerto vigente que la mesa registró
    /// en el periodo en curso. Si la mesa no lo registró no hay equipo y no hay aviso (no se adivina).
    /// </summary>
    public static ActionBanner? TimeoutEnded(MatchSnapshot? snapshot, ScoreboardSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.AnimationsEnabled || !settings.TimeoutAnimationsEnabled
            || snapshot is not { Status: MatchStatus.InProgress, Stage: MatchStage.Period })
        {
            return null;
        }

        var timeout = snapshot.Incidents
            .Where(i => i.Type == IncidentType.Timeout && !i.IsVoided && i.Period == snapshot.CurrentPeriod)
            .MaxBy(i => i.Sequence);
        if (timeout is null)
        {
            return null;
        }

        var team = timeout.Side == TeamSide.Home ? snapshot.Home : snapshot.Away;
        return new ActionBanner(ActionBannerKind.TimeoutEnd, timeout.Side, "FIN DEL\nTIEMPO\nMUERTO", null, null, team.Name,
            TimeoutColor, TimeoutDarkColor, White);
    }
}

/// <summary>
/// Gorro con faltas bajo el resultado: tres puntos (llenos los usados). No es una sanción: con tres o
/// más se llenan los tres y, si pasa de tres, se muestra el número.
/// </summary>
public sealed record FoulChip(Guid RosterEntryId, int Cap, string PlayerName, int Fouls, bool IsNew)
{
    public const int Slots = 3;

    public string CapText => Cap.ToString(MatchTexts.Culture);

    public IReadOnlyList<bool> Dots => [.. Enumerable.Range(1, Slots).Select(n => n <= Fouls)];

    public bool HasExtra => Fouls > Slots;

    public string ExtraText => HasExtra ? Fouls.ToString(MatchTexts.Culture) : string.Empty;

    public string Description => $"Gorro {CapText}, {PlayerName}: {Fouls.ToString(MatchTexts.Culture)} {(Fouls == 1 ? "falta" : "faltas")}";
}

/// <summary>Qué muestra la columna central del marcador.</summary>
public enum ScoreboardCenter
{
    /// <summary>Partido programado: «VS», sin resultado ni relojes.</summary>
    Preview = 0,

    /// <summary>En juego: periodo, reloj y anillo de posesión.</summary>
    Clocks = 1,

    /// <summary>Descanso: la consola no emite; «DESCANSO» en lugar de los relojes.</summary>
    Break = 2,

    /// <summary>Tanda de penales, sin relojes.</summary>
    Shootout = 3,

    /// <summary>Partido finalizado.</summary>
    Final = 4,
}

/// <summary>Un periodo reglamentario en la fila de puntos; jugado incluye el actual.</summary>
public sealed record PeriodPip(bool Played);

/// <summary>Un lanzamiento de la tanda en el marcador: círculo lleno si se convirtió, vacío si no.</summary>
public sealed record ShootoutMark(bool Converted)
{
    public string Description => Converted ? "Convertido" : "No convertido";
}

/// <summary>Auspiciador visible en la franja inferior del marcador.</summary>
public sealed record SponsorView(string Name, ImageData Image);

/// <summary>
/// Marcador de audiencia, de solo lectura. Muestra el último snapshot confirmado, los relojes
/// vigentes y la identidad visual del evento. Al abrirse toma el estado actual sin reproducir
/// animaciones pasadas: solo anima acciones nuevas que llegan mientras está abierto, una vez cada una.
/// </summary>
public sealed partial class MarcadorViewModel : ObservableObject, IDisposable
{
    public const string DefaultHeaderColor = "#0D2238";

    public const string DefaultAccentColor = "#FFD54F";

    /// <summary>Lo que dura la animación «toma del lado» (entrada, permanencia y salida).</summary>
    public static readonly TimeSpan BannerDuration = TimeSpan.FromSeconds(4.3);

    /// <summary>Opacidad del brillo de cada equipo en el fondo.</summary>
    private const double GlowOpacity = 0.38;

    private readonly IMatchSnapshotFeed _feed;
    private readonly ITimingFeed _timing;
    private readonly ScoreboardSettings _settings;
    private readonly IScoreboardIdentityFeed? _identityFeed;
    private readonly TimeProvider _time;
    private readonly SynchronizationContext _ui;
    private MatchSnapshot? _snapshot;
    private TimingView? _timingView;
    private ScoreboardIdentity? _identity;
    private int _bannerGeneration;
    private int _announcementGeneration;
    private int? _timeoutsEnded;
    private int _timeoutPeakMs;

    public MarcadorViewModel(
        IMatchSnapshotFeed feed,
        ITimingFeed timing,
        ScoreboardSettings settings,
        IScoreboardIdentityFeed? identity = null,
        TimeProvider? time = null)
    {
        _feed = feed;
        _timing = timing;
        _settings = settings;
        _identityFeed = identity;
        _time = time ?? TimeProvider.System;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();

        _feed.Published += OnSnapshotPublished;
        _timing.Changed += OnTimingChanged;
        if (_identityFeed is not null)
        {
            _identityFeed.Changed += OnIdentityChanged;
            _identity = _identityFeed.Current;
        }

        if (_feed.Current is { } current)
        {
            ApplySnapshot(current);
        }

        ApplyTiming(_timing.Current);
    }

    public ScoreboardSettings Settings => _settings;

    [ObservableProperty]
    public partial bool HasMatch { get; set; }

    /// <summary>Título de la plantilla del evento o, sin plantilla, el nombre del evento del partido.</summary>
    [ObservableProperty]
    public partial string TournamentName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Subtitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasSubtitle { get; set; }

    [ObservableProperty]
    public partial string HeaderColor { get; set; } = DefaultHeaderColor;

    [ObservableProperty]
    public partial string AccentColor { get; set; } = DefaultAccentColor;

    [ObservableProperty]
    public partial ImageData? EventLogo { get; set; }

    [ObservableProperty]
    public partial ImageData? OrganizerLogo { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<SponsorView> Sponsors { get; set; } = [];

    [ObservableProperty]
    public partial bool HasSponsors { get; set; }

    [ObservableProperty]
    public partial ImageData? HomeCrest { get; set; }

    [ObservableProperty]
    public partial ImageData? HomeFlag { get; set; }

    [ObservableProperty]
    public partial ImageData? AwayCrest { get; set; }

    [ObservableProperty]
    public partial ImageData? AwayFlag { get; set; }

    [ObservableProperty]
    public partial string MatchLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HomeName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AwayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HomeCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AwayCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HomeColor { get; set; } = "#37474F";

    [ObservableProperty]
    public partial string AwayColor { get; set; } = "#37474F";

    [ObservableProperty]
    public partial string HomeScoreText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AwayScoreText { get; set; } = string.Empty;

    /// <summary>Falso en la presentación previa: aún no hay resultado que mostrar.</summary>
    [ObservableProperty]
    public partial bool ShowScores { get; set; }

    [ObservableProperty]
    public partial string StageText { get; set; } = string.Empty;

    /// <summary>Verdadero mientras se juega un periodo: se muestran los relojes (no en descanso ni tanda).</summary>
    [ObservableProperty]
    public partial bool ShowClocks { get; set; }

    [ObservableProperty]
    public partial ScoreboardCenter CenterMode { get; set; }

    /// <summary>Línea sobre el centro: «3.er PERIODO», «FIN DEL 2.º PERIODO», «TANDA DE PENALES»…</summary>
    [ObservableProperty]
    public partial string PeriodText { get; set; } = string.Empty;

    /// <summary>Texto grande del centro cuando no hay relojes: «VS», «DESCANSO», «PENALES», «FINAL».</summary>
    [ObservableProperty]
    public partial string CenterHeadline { get; set; } = string.Empty;

    /// <summary>Nota bajo el texto grande: «Reglamentario 9 – 9», «Penales 4 – 3», «Tras prórroga».</summary>
    [ObservableProperty]
    public partial string CenterNote { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasCenterNote { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<PeriodPip> PeriodPips { get; set; } = [];

    /// <summary>Puntos de periodo: en juego o descanso del tiempo reglamentario (no en prórroga).</summary>
    [ObservableProperty]
    public partial bool ShowPeriodPips { get; set; }

    /// <summary>
    /// Fracción del anillo de posesión: lectura vigente ÷ posesión completa de las reglas del partido,
    /// en [0, 1]. Sin lectura vigente o sin reglas es 0: nunca se estima un valor.
    /// </summary>
    [ObservableProperty]
    public partial double ShotClockFraction { get; set; }

    /// <summary>Sin lectura vigente del reloj de partido: reloj y anillo se atenúan.</summary>
    [ObservableProperty]
    public partial bool ClocksDimmed { get; set; } = true;

    /// <summary>Solo en el resultado final: el número del que perdió se atenúa.</summary>
    [ObservableProperty]
    public partial bool HomeDimmed { get; set; }

    [ObservableProperty]
    public partial bool AwayDimmed { get; set; }

    /// <summary>Brillo de fondo del local: su color con opacidad (#AARRGGBB).</summary>
    [ObservableProperty]
    public partial string HomeGlowColor { get; set; } = "#00000000";

    [ObservableProperty]
    public partial string AwayGlowColor { get; set; } = "#00000000";

    [ObservableProperty]
    public partial string GameClockText { get; set; } = ClockFormatter.GameClockUnavailable;

    [ObservableProperty]
    public partial string ShotClockText { get; set; } = ClockFormatter.ShotClockUnavailable;

    /// <summary>«DEMO / SIMULACIÓN» o «REPRODUCCIÓN»; vacío con la fuente oficial.</summary>
    [ObservableProperty]
    public partial string ModeBadge { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowModeBadge { get; set; }

    [ObservableProperty]
    public partial bool ShowSignalNotice { get; set; }

    /// <summary>Aviso animado de la franja inferior (gol, exclusión o tiempo muerto).</summary>
    [ObservableProperty]
    public partial string BannerText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ActionBannerKind BannerKind { get; set; }

    [ObservableProperty]
    public partial bool IsBannerVisible { get; set; }

    /// <summary>Último aviso mostrado (lado, colores, titular, gorro y jugador).</summary>
    [ObservableProperty]
    public partial ActionBanner? Banner { get; set; }

    /// <summary>Sube con cada aviso nuevo: la ventana arranca la animación al cambiar.</summary>
    [ObservableProperty]
    public partial int BannerGeneration { get; set; }

    /// <summary>Bandera (o, si no hay, escudo) del equipo del aviso.</summary>
    [ObservableProperty]
    public partial ImageData? BannerTeamImage { get; set; }

    /// <summary>Hay tanda de penales: se muestra bajo el resultado de cada equipo, aparte de él.</summary>
    [ObservableProperty]
    public partial bool HasShootout { get; set; }

    [ObservableProperty]
    public partial string HomeShootoutText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AwayShootoutText { get; set; } = string.Empty;

    /// <summary>Lanzamientos vigentes del local, en orden (los últimos <see cref="MaxShootoutMarks"/>).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ShootoutMark> HomeShootoutMarks { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ShootoutMark> AwayShootoutMarks { get; set; } = [];

    /// <summary>Marcas visibles por equipo; una muerte súbita larga muestra las últimas.</summary>
    public const int MaxShootoutMarks = 10;

    /// <summary>Lo que dura el anuncio del centro (inicio de periodo, descanso, tanda, final).</summary>
    public static readonly TimeSpan AnnouncementDuration = TimeSpan.FromSeconds(2.2);

    /// <summary>Jugadores del local con faltas (personales, exclusiones y expulsiones), por gorro.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<FoulChip> HomeFouls { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<FoulChip> AwayFouls { get; set; } = [];

    /// <summary>Faltas visibles: con resultado, salvo en la previa y en la tanda (los lanzamientos usan ese lugar).</summary>
    [ObservableProperty]
    public partial bool ShowFouls { get; set; }

    /// <summary>Último anuncio del centro.</summary>
    [ObservableProperty]
    public partial ScoreboardAnnouncement Announcement { get; set; }

    /// <summary>Sube con cada anuncio nuevo del centro: la ventana lo anima al cambiar.</summary>
    [ObservableProperty]
    public partial int AnnouncementGeneration { get; set; }

    /// <summary>Hay un tiempo muerto en curso según la consola: el anillo muestra su cuenta.</summary>
    [ObservableProperty]
    public partial bool TimeoutActive { get; set; }

    [ObservableProperty]
    public partial string TimeoutClockText { get; set; } = ClockFormatter.ShotClockUnavailable;

    /// <summary>Lectura ÷ el mayor valor leído en este tiempo muerto: no se supone su duración.</summary>
    [ObservableProperty]
    public partial double TimeoutFraction { get; set; }

    public void Dispose()
    {
        _feed.Published -= OnSnapshotPublished;
        _timing.Changed -= OnTimingChanged;
        if (_identityFeed is not null)
        {
            _identityFeed.Changed -= OnIdentityChanged;
        }
    }

    private void OnSnapshotPublished(object? sender, MatchSnapshotPublished e) => _ui.Post(_ =>
    {
        var banner = ActionBanner.For(e, _settings);
        ApplySnapshot(e.Snapshot, banner?.ShowsCap == true ? e.Change.IncidentId : null);

        // Solo una acción recién confirmada dispara el aviso; corregir, anular o reabrir no celebran.
        if (banner is not null)
        {
            ShowBanner(banner);
        }

        if (AnnouncementFor(e) is var announcement and not ScoreboardAnnouncement.None)
        {
            ShowAnnouncement(announcement);
        }
    }, null);

    /// <summary>
    /// Anuncio del centro según el último cambio de estado confirmado: inicio de partido o de periodo,
    /// descanso, tanda y final. Reabrir, deshacer la tanda o abrir el partido no anuncian.
    /// </summary>
    private ScoreboardAnnouncement AnnouncementFor(MatchSnapshotPublished published)
    {
        if (!_settings.AnimationsEnabled || !_settings.PeriodAnimationsEnabled
            || published.Change.Kind is not (MatchChangeKind.Started or MatchChangeKind.StageChanged or MatchChangeKind.Finished))
        {
            return ScoreboardAnnouncement.None;
        }

        return published.Snapshot.History.Count == 0
            ? ScoreboardAnnouncement.None
            : published.Snapshot.History[^1].Kind switch
            {
                MatchStatusChangeKind.Started or MatchStatusChangeKind.PeriodStarted => ScoreboardAnnouncement.PeriodStarted,
                MatchStatusChangeKind.BreakStarted => ScoreboardAnnouncement.BreakStarted,
                MatchStatusChangeKind.ShootoutStarted => ScoreboardAnnouncement.ShootoutStarted,
                MatchStatusChangeKind.Finished => ScoreboardAnnouncement.Finished,
                _ => ScoreboardAnnouncement.None,
            };
    }

    private void OnTimingChanged(object? sender, TimingView view) => _ui.Post(_ => ApplyTiming(view), null);

    private void OnIdentityChanged(object? sender, ScoreboardIdentity identity) => _ui.Post(_ =>
    {
        _identity = identity;
        ApplyIdentity();
    }, null);

    private void ApplySnapshot(MatchSnapshot snapshot, Guid? newFoulIncident = null)
    {
        _snapshot = snapshot;
        var started = snapshot.Status is MatchStatus.InProgress or MatchStatus.Finished;
        HasMatch = true;
        MatchLabel = snapshot.Label;
        HomeName = snapshot.Home.Name;
        AwayName = snapshot.Away.Name;
        HomeCode = snapshot.Home.Code;
        AwayCode = snapshot.Away.Code;
        HomeColor = snapshot.Home.ColorHex;
        AwayColor = snapshot.Away.ColorHex;
        HomeScoreText = snapshot.Home.Score.ToString(MatchTexts.Culture);
        AwayScoreText = snapshot.Away.Score.ToString(MatchTexts.Culture);
        ShowScores = started;
        StageText = MatchTexts.PublicStage(snapshot);
        HomeGlowColor = ColorTools.WithAlpha(snapshot.Home.ColorHex, GlowOpacity);
        AwayGlowColor = ColorTools.WithAlpha(snapshot.Away.ColorHex, GlowOpacity);
        ApplyCenter(snapshot);
        ApplyShootout(snapshot.Shootout);
        ApplyFouls(snapshot, newFoulIncident);
        ApplyIdentity();
        if (_timingView is { } timing)
        {
            ApplyTiming(timing);
        }
    }

    private void ApplyCenter(MatchSnapshot snapshot)
    {
        var mode = snapshot.Status switch
        {
            MatchStatus.Scheduled or MatchStatus.Cancelled => ScoreboardCenter.Preview,
            MatchStatus.Finished => ScoreboardCenter.Final,
            _ when snapshot.Stage == MatchStage.Shootout => ScoreboardCenter.Shootout,
            _ when snapshot.Stage == MatchStage.Break => ScoreboardCenter.Break,
            _ => ScoreboardCenter.Clocks,
        };

        var shootout = snapshot.Shootout;
        (PeriodText, CenterHeadline, CenterNote) = mode switch
        {
            ScoreboardCenter.Preview when snapshot.Status == MatchStatus.Cancelled => ("PARTIDO ANULADO", "VS", string.Empty),
            ScoreboardCenter.Preview => ("PRÓXIMO PARTIDO", "VS", string.Empty),
            ScoreboardCenter.Break => (ScoreboardTexts.BreakLine(snapshot), "DESCANSO", string.Empty),
            ScoreboardCenter.Shootout => ("TANDA DE PENALES", "PENALES", $"Reglamentario {Pair(snapshot.Home.Score, snapshot.Away.Score)}"),
            ScoreboardCenter.Final => ("RESULTADO FINAL", "FINAL",
                shootout is not null ? $"Penales {Pair(shootout.HomeScore, shootout.AwayScore)}"
                : snapshot.IsOvertime ? "Tras prórroga"
                : string.Empty),
            _ => (ScoreboardTexts.PeriodLine(snapshot), string.Empty, string.Empty),
        };

        CenterMode = mode;
        HasCenterNote = CenterNote.Length > 0;
        ShowClocks = mode == ScoreboardCenter.Clocks;

        var periods = snapshot.Rules?.PeriodCount ?? 0;
        PeriodPips = [.. Enumerable.Range(1, periods).Select(n => new PeriodPip(n <= snapshot.CurrentPeriod))];
        ShowPeriodPips = periods > 0 && !snapshot.IsOvertime && mode is ScoreboardCenter.Clocks or ScoreboardCenter.Break;

        // El perdedor se deriva del resultado (o de la tanda, si la hubo); es presentación, no una decisión arbitral.
        var (home, away) = shootout is { } s && s.HomeScore != s.AwayScore
            ? (s.HomeScore, s.AwayScore)
            : (snapshot.Home.Score, snapshot.Away.Score);
        HomeDimmed = mode == ScoreboardCenter.Final && home < away;
        AwayDimmed = mode == ScoreboardCenter.Final && away < home;
    }

    private static string Pair(int home, int away) =>
        $"{home.ToString(MatchTexts.Culture)} – {away.ToString(MatchTexts.Culture)}";

    private void ApplyShootout(ShootoutSnapshot? shootout)
    {
        HasShootout = shootout is not null;
        HomeShootoutText = shootout?.HomeScore.ToString(MatchTexts.Culture) ?? string.Empty;
        AwayShootoutText = shootout?.AwayScore.ToString(MatchTexts.Culture) ?? string.Empty;
        HomeShootoutMarks = Marks(shootout, TeamSide.Home);
        AwayShootoutMarks = Marks(shootout, TeamSide.Away);
    }

    /// <summary>Gorros con faltas de cada equipo; el de la falta recién registrada late una vez.</summary>
    private void ApplyFouls(MatchSnapshot snapshot, Guid? newFoulIncident)
    {
        var fresh = newFoulIncident is { } id ? snapshot.Incidents.FirstOrDefault(i => i.IncidentId == id)?.RosterEntryId : null;
        IReadOnlyList<FoulChip> Chips(TeamSide side) =>
        [
            .. snapshot.PlayerFouls
                .Where(f => f.Side == side)
                .OrderBy(f => f.Cap)
                .Select(f => new FoulChip(f.RosterEntryId, f.Cap, f.PlayerName, f.Fouls, f.RosterEntryId == fresh)),
        ];

        HomeFouls = Chips(TeamSide.Home);
        AwayFouls = Chips(TeamSide.Away);
        ShowFouls = ShowScores && CenterMode is not (ScoreboardCenter.Preview or ScoreboardCenter.Shootout) && !HasShootout;
    }

    private static IReadOnlyList<ShootoutMark> Marks(ShootoutSnapshot? shootout, TeamSide side) =>
        shootout is null ? [] : [.. shootout.For(side).TakeLast(MaxShootoutMarks).Select(a => new ShootoutMark(a.Converted))];

    /// <summary>Aplica la identidad solo si es la del partido mostrado; si no, la presentación predeterminada.</summary>
    private void ApplyIdentity()
    {
        var identity = _snapshot is { } snapshot && _identity?.MatchId == snapshot.MatchId ? _identity : null;
        TournamentName = identity?.Title ?? _snapshot?.TournamentName ?? string.Empty;
        Subtitle = identity?.Subtitle ?? string.Empty;
        HasSubtitle = Subtitle.Length > 0;
        HeaderColor = identity?.PrimaryColorHex ?? DefaultHeaderColor;
        AccentColor = identity?.AccentColorHex ?? DefaultAccentColor;
        EventLogo = identity?.Logo;
        OrganizerLogo = identity?.OrganizerLogo;
        Sponsors = identity is null ? [] : [.. identity.Sponsors.Select(s => new SponsorView(s.Name, s.Image))];
        HasSponsors = Sponsors.Count > 0;
        HomeCrest = identity?.Home.Crest;
        HomeFlag = identity?.Home.Flag;
        AwayCrest = identity?.Away.Crest;
        AwayFlag = identity?.Away.Flag;
    }

    private void ApplyTiming(TimingView view)
    {
        _timingView = view;
        GameClockText = ClockFormatter.FormatGameClock(view.GameClock, view.Precision);
        ShotClockText = ClockFormatter.FormatShotClock(view.ShotClock);
        ClocksDimmed = !view.GameClock.IsCurrent;
        var fullMs = (_snapshot?.Rules?.ShotClockSeconds ?? 0) * 1000.0;
        ShotClockFraction = view.ShotClock.RemainingMs is { } shotMs && fullMs > 0
            ? Math.Clamp(shotMs / fullMs, 0, 1)
            : 0;
        ShowModeBadge = view.IsSimulation;
        ModeBadge = view.IsSimulation ? view.SourceLabel : string.Empty;
        ShowSignalNotice = view.Signal is SignalState.Lost or SignalState.Unavailable;
        ApplyTimeout(view);
    }

    /// <summary>
    /// Cuenta del tiempo muerto mientras la consola la envía y aviso al terminar. El fin lo detecta el
    /// seguimiento de cronometraje (sube <see cref="TimingView.TimeoutsEnded"/>); al abrir la pantalla
    /// solo se toma el valor, sin animar.
    /// </summary>
    private void ApplyTimeout(TimingView view)
    {
        if (view.TimeoutClock.RemainingMs is { } ms && ms > 0)
        {
            _timeoutPeakMs = TimeoutActive ? Math.Max(_timeoutPeakMs, ms) : ms;
            TimeoutActive = true;
            TimeoutClockText = ClockFormatter.FormatTimeoutClock(view.TimeoutClock);
            TimeoutFraction = Math.Clamp(ms / (double)_timeoutPeakMs, 0, 1);
        }
        else
        {
            TimeoutActive = false;
            TimeoutClockText = ClockFormatter.ShotClockUnavailable;
            TimeoutFraction = 0;
        }

        var ended = _timeoutsEnded is { } seen && view.TimeoutsEnded > seen;
        _timeoutsEnded = view.TimeoutsEnded;
        if (ended && ActionBanner.TimeoutEnded(_snapshot, _settings) is { } banner)
        {
            ShowBanner(banner);
        }
    }

    private void ShowAnnouncement(ScoreboardAnnouncement announcement)
    {
        Announcement = announcement;
        AnnouncementGeneration = ++_announcementGeneration;
    }

    private async void ShowBanner(ActionBanner banner)
    {
        Banner = banner;
        BannerTeamImage = banner.IsHome ? HomeFlag ?? HomeCrest : AwayFlag ?? AwayCrest;
        BannerKind = banner.Kind;
        BannerText = banner.Text;
        IsBannerVisible = true;

        var generation = ++_bannerGeneration;
        BannerGeneration = generation;
        await Task.Delay(BannerDuration, _time);
        if (generation == _bannerGeneration)
        {
            IsBannerVisible = false;
        }
    }
}
