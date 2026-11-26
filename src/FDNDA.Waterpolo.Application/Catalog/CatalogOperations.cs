using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;

namespace FDNDA.Waterpolo.Application.Catalog;

/// <summary>Resultado de un comando del catálogo.</summary>
public sealed record CatalogResult(CommandOutcome Outcome, string? Message)
{
    public bool IsSuccess => Outcome is CommandOutcome.Applied or CommandOutcome.AlreadyApplied;
}

/// <summary>Resultado de abrir un archivo de nómina: la vista previa o el motivo por el que no se pudo leer.</summary>
public sealed record RosterPreviewResult(RosterPreview? Preview, string? Message);

/// <summary>
/// Casos de uso del catálogo: eventos, equipos y nóminas de evento. Los partidos ya programados
/// guardan una copia de los nombres, colores y nómina, así que editar el catálogo no los altera.
/// Las altas son idempotentes por el identificador que genera la interfaz.
/// </summary>
public sealed class CatalogOperations
{
    private const string StorageFailureMessage =
        "No se pudo guardar el cambio en el almacenamiento local. Revise el disco y vuelva a intentar.";

    private readonly ICatalogRepository _catalog;
    private readonly TimeProvider _time;
    private readonly IDiagnosticLog _log;
    private readonly bool _demoData;
    private readonly IRosterFileReader? _rosterReader;
    private readonly SemaphoreSlim _writer = new(1, 1);

    /// <param name="demoData">Verdadero en el perfil demo: los eventos nuevos quedan marcados como datos de demostración.</param>
    /// <param name="rosterReader">Lector de archivos de nómina; sin él la importación no está disponible.</param>
    public CatalogOperations(
        ICatalogRepository catalog, TimeProvider time, IDiagnosticLog log, bool demoData, IRosterFileReader? rosterReader = null)
    {
        _catalog = catalog;
        _time = time;
        _log = log;
        _demoData = demoData;
        _rosterReader = rosterReader;
    }

    // ---------- Lecturas ----------

    public Task<IReadOnlyList<TournamentSummary>> ListTournamentsAsync(CancellationToken ct = default) =>
        _catalog.ListTournamentsAsync(ct);

    /// <summary>Evento con sus reglas vigentes; nulo si no existe.</summary>
    public Task<Tournament?> GetTournamentAsync(Guid tournamentId, CancellationToken ct = default) =>
        _catalog.GetAsync(tournamentId, ct);

    public Task<IReadOnlyList<Team>> ListTeamsAsync(CancellationToken ct = default) => _catalog.ListTeamsAsync(ct);

    public Task<IReadOnlyList<TournamentRosterLine>> GetTournamentRosterAsync(
        Guid tournamentId, Guid teamId, CancellationToken ct = default) =>
        _catalog.GetTournamentRosterAsync(tournamentId, teamId, ct);

    // ---------- Eventos ----------

    /// <summary>
    /// Crea un evento. Mientras no exista un preset de competencia aprobado por el área técnica,
    /// el evento nace con el preset demo, identificado como no reglamentario; sus reglas se
    /// editan después con <see cref="UpdateTournamentRulesAsync"/>.
    /// </summary>
    public Task<CatalogResult> CreateTournamentAsync(Guid tournamentId, string name, CancellationToken ct = default) =>
        ExecuteAsync("crear el evento", async () =>
        {
            var tournament = new Tournament(tournamentId, name, _demoData, RulesetSnapshot.DemoPreset());
            await RequireUniqueTournamentNameAsync(tournament.Id, tournament.Name, ct).ConfigureAwait(false);
            var added = await _catalog.AddTournamentAsync(tournament, _time.GetUtcNow(), ct).ConfigureAwait(false);
            return added
                ? new CatalogResult(CommandOutcome.Applied, $"Evento «{tournament.Name}» creado.")
                : new CatalogResult(CommandOutcome.AlreadyApplied, "El evento ya estaba creado.");
        }, ct);

    /// <summary>Cambia el nombre del evento. Los partidos ya programados conservan el nombre anterior.</summary>
    public Task<CatalogResult> RenameTournamentAsync(Guid tournamentId, string name, CancellationToken ct = default) =>
        ExecuteAsync("guardar el evento", async () =>
        {
            var current = await _catalog.GetAsync(tournamentId, ct).ConfigureAwait(false)
                ?? throw new DomainException("evento.no_existe", "El evento no existe.");
            var renamed = new Tournament(current.Id, name, current.IsDemo, current.Rules);
            if (renamed.Name == current.Name)
            {
                return new CatalogResult(CommandOutcome.AlreadyApplied, "El evento no tenía cambios.");
            }

            await RequireUniqueTournamentNameAsync(renamed.Id, renamed.Name, ct).ConfigureAwait(false);
            await _catalog.UpdateTournamentAsync(renamed, ct).ConfigureAwait(false);
            return new CatalogResult(CommandOutcome.Applied, $"Evento «{renamed.Name}» guardado.");
        }, ct);

    /// <summary>
    /// Cambia las reglas del evento. Solo las toman los partidos que se inicien después: los
    /// iniciados o cerrados conservan la copia que recibieron al iniciarse. Si los valores
    /// coinciden con el preset demo se guardan como tal; si no, como reglas personalizadas sin
    /// aprobación técnica. Los parámetros de posesión solo alimentan la simulación: el programa
    /// nunca escribe en la consola.
    /// </summary>
    public Task<CatalogResult> UpdateTournamentRulesAsync(Guid tournamentId, RulesetSnapshot rules, CancellationToken ct = default) =>
        ExecuteAsync("guardar las reglas del evento", async () =>
        {
            ArgumentNullException.ThrowIfNull(rules);
            var current = await _catalog.GetAsync(tournamentId, ct).ConfigureAwait(false)
                ?? throw new DomainException("evento.no_existe", "El evento no existe.");
            var normalized = RulesetSnapshot.ForEvent(
                rules.PeriodCount,
                rules.PeriodDurationSeconds,
                rules.BreakDurationSeconds,
                rules.HalftimeDurationSeconds,
                rules.TimeoutsPerTeam,
                rules.ShotClockSeconds,
                rules.ShotClockAlternateSeconds,
                rules.PenaltyShootoutEnabled,
                rules.OvertimePeriodCount,
                rules.OvertimePeriodDurationSeconds);
            if (normalized == current.Rules)
            {
                return new CatalogResult(CommandOutcome.AlreadyApplied, "Las reglas del evento no tenían cambios.");
            }

            await _catalog.UpdateTournamentAsync(new Tournament(current.Id, current.Name, current.IsDemo, normalized), ct).ConfigureAwait(false);
            var summary = (await _catalog.ListTournamentsAsync(ct).ConfigureAwait(false)).FirstOrDefault(t => t.TournamentId == tournamentId);
            return new CatalogResult(CommandOutcome.Applied, RulesSavedMessage(current.Name, normalized, summary));
        }, ct);

    private static string RulesSavedMessage(string tournamentName, RulesetSnapshot rules, TournamentSummary? summary)
    {
        var scheduled = summary?.ScheduledMatchCount ?? 0;
        var others = (summary?.MatchCount ?? 0) - scheduled;
        var preset = rules.IsDemoPreset ? "preset DEMO (no reglamentario)" : "reglas personalizadas sin aprobación técnica";
        var applies = scheduled switch
        {
            0 => "Las tomarán los partidos que se inicien desde ahora.",
            1 => "El partido programado las tomará al iniciarse.",
            _ => $"Los {scheduled} partidos programados las tomarán al iniciarse.",
        };
        var keeps = others switch
        {
            0 => string.Empty,
            1 => " El partido ya iniciado o cerrado conserva sus reglas.",
            _ => $" Los {others} partidos ya iniciados o cerrados conservan sus reglas.",
        };
        return $"Reglas de «{tournamentName}» guardadas ({preset}). {applies}{keeps}";
    }

    // ---------- Equipos ----------

    public Task<CatalogResult> CreateTeamAsync(
        Guid teamId, string code, string name, string colorHex, string capColor, CancellationToken ct = default) =>
        ExecuteAsync("crear el equipo", async () =>
        {
            var team = new Team(teamId, code, name, colorHex, capColor);
            var existing = await _catalog.GetTeamAsync(team.Id, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                return new CatalogResult(CommandOutcome.AlreadyApplied, "El equipo ya estaba creado.");
            }

            await RequireUniqueTeamCodeAsync(team, ct).ConfigureAwait(false);
            var added = await _catalog.AddTeamAsync(team, _time.GetUtcNow(), ct).ConfigureAwait(false);
            return added
                ? new CatalogResult(CommandOutcome.Applied, $"Equipo «{team.Name}» creado.")
                : new CatalogResult(CommandOutcome.AlreadyApplied, "El equipo ya estaba creado.");
        }, ct);

    /// <summary>Edita un equipo. Los partidos ya programados conservan los datos anteriores.</summary>
    public Task<CatalogResult> UpdateTeamAsync(
        Guid teamId, string code, string name, string colorHex, string capColor, CancellationToken ct = default) =>
        ExecuteAsync("guardar el equipo", async () =>
        {
            var current = await _catalog.GetTeamAsync(teamId, ct).ConfigureAwait(false)
                ?? throw new DomainException("equipo.no_existe", "El equipo no existe.");
            var team = new Team(teamId, code, name, colorHex, capColor);
            if (team == current)
            {
                return new CatalogResult(CommandOutcome.AlreadyApplied, "El equipo no tenía cambios.");
            }

            await RequireUniqueTeamCodeAsync(team, ct).ConfigureAwait(false);
            await _catalog.UpdateTeamAsync(team, ct).ConfigureAwait(false);
            return new CatalogResult(CommandOutcome.Applied, $"Equipo «{team.Name}» guardado.");
        }, ct);

    // ---------- Nóminas: edición manual ----------

    /// <summary>Inscribe un jugador nuevo en la nómina del equipo en el evento. Idempotente por <paramref name="entryId"/>.</summary>
    public Task<CatalogResult> AddRosterPlayerAsync(
        Guid tournamentId, Guid teamId, Guid entryId, Guid playerId, string playerName, int cap, CancellationToken ct = default) =>
        ExecuteAsync("agregar el jugador", async () =>
        {
            var (team, roster) = await RequireRosterAsync(tournamentId, teamId, ct).ConfigureAwait(false);
            if (roster.Any(r => r.EntryId == entryId))
            {
                return new CatalogResult(CommandOutcome.AlreadyApplied, "El jugador ya estaba en la nómina.");
            }

            var player = new Player(playerId, teamId, playerName);
            var entry = new TournamentRosterEntry(entryId, tournamentId, teamId, playerId, new CapNumber(cap));
            RequireFreeCap(roster, team, entry.Cap.Value, exceptEntryId: null);
            var added = await _catalog.AddRosterPlayerAsync(player, entry, _time.GetUtcNow(), ct).ConfigureAwait(false);
            return added
                ? new CatalogResult(CommandOutcome.Applied, $"«{player.FullName}» agregado a {team.Code} con el gorro {cap}.")
                : new CatalogResult(CommandOutcome.AlreadyApplied, "El jugador ya estaba en la nómina.");
        }, ct);

    /// <summary>
    /// Corrige el nombre o el gorro de un jugador de la nómina. El nombre se corrige en el jugador,
    /// así que vale para cualquier evento; los partidos ya programados conservan su copia.
    /// </summary>
    public Task<CatalogResult> UpdateRosterPlayerAsync(
        Guid tournamentId, Guid teamId, Guid entryId, string playerName, int cap, CancellationToken ct = default) =>
        ExecuteAsync("guardar el jugador", async () =>
        {
            var (team, roster) = await RequireRosterAsync(tournamentId, teamId, ct).ConfigureAwait(false);
            var current = roster.FirstOrDefault(r => r.EntryId == entryId)
                ?? throw new DomainException("nomina.no_existe", "El jugador ya no está en la nómina. Actualice la lista.");
            var player = new Player(current.PlayerId, teamId, playerName);
            var capNumber = new CapNumber(cap);
            if (player.FullName == current.PlayerName && capNumber.Value == current.Cap)
            {
                return new CatalogResult(CommandOutcome.AlreadyApplied, "El jugador no tenía cambios.");
            }

            RequireFreeCap(roster, team, capNumber.Value, entryId);
            await _catalog.UpdateRosterPlayerAsync(entryId, player.FullName, capNumber.Value, ct).ConfigureAwait(false);
            return new CatalogResult(CommandOutcome.Applied, $"«{player.FullName}» guardado con el gorro {cap}.");
        }, ct);

    /// <summary>Quita a un jugador de la nómina del evento. Los partidos ya programados no cambian.</summary>
    public Task<CatalogResult> RemoveRosterPlayerAsync(Guid tournamentId, Guid teamId, Guid entryId, CancellationToken ct = default) =>
        ExecuteAsync("quitar el jugador", async () =>
        {
            var (team, roster) = await RequireRosterAsync(tournamentId, teamId, ct).ConfigureAwait(false);
            var current = roster.FirstOrDefault(r => r.EntryId == entryId);
            if (current is null)
            {
                return new CatalogResult(CommandOutcome.AlreadyApplied, "El jugador ya no estaba en la nómina.");
            }

            await _catalog.RemoveRosterEntryAsync(entryId, ct).ConfigureAwait(false);
            return new CatalogResult(CommandOutcome.Applied, $"«{current.PlayerName}» (gorro {current.Cap}) quitado de la nómina de {team.Code}.");
        }, ct);

    // ---------- Nóminas: importación ----------

    /// <summary>
    /// Lee un archivo de nómina y lo compara con la nómina actual del evento. No escribe nada:
    /// cancelar la vista previa no deja rastro. La lectura corre fuera del hilo que llama.
    /// </summary>
    public async Task<RosterPreviewResult> PreviewRosterImportAsync(Guid tournamentId, string path, CancellationToken ct = default)
    {
        if (_rosterReader is null)
        {
            return new RosterPreviewResult(null, "La importación de nóminas no está disponible en esta instalación.");
        }

        try
        {
            var tournament = await _catalog.GetAsync(tournamentId, ct).ConfigureAwait(false);
            if (tournament is null)
            {
                return new RosterPreviewResult(null, "Elija el evento al que pertenece la nómina.");
            }

            var reader = _rosterReader;
            var (rows, errors, notices) = await Task.Run(() => RosterImportPlanner.ReadRows(reader.Read(path, ct)), ct).ConfigureAwait(false);

            var teams = await _catalog.ListTeamsAsync(ct).ConfigureAwait(false);
            var inFile = teams
                .Where(t => rows.Any(r => string.Equals(r.TeamCode, t.Code, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var rosters = new Dictionary<Guid, IReadOnlyList<TournamentRosterLine>>();
            foreach (var team in inFile)
            {
                rosters[team.Id] = await _catalog.GetTournamentRosterAsync(tournamentId, team.Id, ct).ConfigureAwait(false);
            }

            var allNotices = notices.ToList();
            var scheduled = await _catalog.CountScheduledMatchesAsync(tournamentId, [.. inFile.Select(t => t.Id)], ct).ConfigureAwait(false);
            if (scheduled > 0)
            {
                allNotices.Add(scheduled == 1
                    ? "Hay 1 partido ya programado de este evento con estos equipos: conserva la nómina que tenía al programarse. Para pasarle la nueva, use «Actualizar nómina» en la pestaña Partidos."
                    : $"Hay {scheduled} partidos ya programados de este evento con estos equipos: conservan la nómina que tenían al programarse. Para pasarles la nueva, use «Actualizar nómina» en la pestaña Partidos.");
            }

            var preview = RosterImportPlanner.Classify(
                tournament, Path.GetFileName(path), rows, errors, allNotices, teams, rosters);
            return new RosterPreviewResult(preview, null);
        }
        catch (RosterFileException ex)
        {
            return new RosterPreviewResult(null, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"No se pudo abrir el archivo de nómina: {ex.GetType().Name}.");
            return new RosterPreviewResult(null,
                "No se pudo abrir el archivo. Si está abierto en Excel, ciérrelo y vuelva a intentar; compruebe también que tiene permiso para leerlo.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error("No se pudo preparar la vista previa de la nómina.", ex);
            return new RosterPreviewResult(null, "No se pudo leer el catálogo del almacenamiento local.");
        }
    }

    /// <summary>
    /// Escribe la importación con las decisiones del operador, en una sola transacción. Si la
    /// nómina cambió desde la vista previa, no escribe nada y pide revisarla de nuevo.
    /// </summary>
    public Task<CatalogResult> ConfirmRosterImportAsync(
        RosterPreview preview, IReadOnlyDictionary<int, RosterResolution> choices, CancellationToken ct = default) =>
        ExecuteAsync("importar la nómina", async () =>
        {
            var plan = RosterImportPlanner.Plan(preview, choices);
            if (!plan.IsReady)
            {
                return new CatalogResult(CommandOutcome.Rejected, string.Join(" ", plan.Problems));
            }

            var changes = plan.Changes!;
            if (!changes.Summary.HasChanges)
            {
                return new CatalogResult(CommandOutcome.AlreadyApplied, "La nómina ya estaba al día: no hubo cambios.");
            }

            await _catalog.ApplyRosterImportAsync(changes, _time.GetUtcNow(), ct).ConfigureAwait(false);
            return new CatalogResult(CommandOutcome.Applied, $"Nómina importada en «{preview.TournamentName}»: {changes.Summary}.");
        }, ct);

    private async Task<(Team Team, IReadOnlyList<TournamentRosterLine> Roster)> RequireRosterAsync(
        Guid tournamentId, Guid teamId, CancellationToken ct)
    {
        _ = await _catalog.GetAsync(tournamentId, ct).ConfigureAwait(false)
            ?? throw new DomainException("evento.no_existe", "Elija el evento de la nómina.");
        var team = await _catalog.GetTeamAsync(teamId, ct).ConfigureAwait(false)
            ?? throw new DomainException("equipo.no_existe", "Elija el equipo de la nómina.");
        var roster = await _catalog.GetTournamentRosterAsync(tournamentId, teamId, ct).ConfigureAwait(false);
        return (team, roster);
    }

    private static void RequireFreeCap(IReadOnlyList<TournamentRosterLine> roster, Team team, int cap, Guid? exceptEntryId)
    {
        var holder = roster.FirstOrDefault(r => r.Cap == cap && r.EntryId != exceptEntryId);
        if (holder is not null)
        {
            throw new DomainException("nomina.gorro_duplicado", $"El gorro {cap} de {team.Code} ya es de «{holder.PlayerName}».");
        }
    }

    private async Task RequireUniqueTeamCodeAsync(Team team, CancellationToken ct)
    {
        var teams = await _catalog.ListTeamsAsync(ct).ConfigureAwait(false);
        if (teams.Any(t => t.Id != team.Id && string.Equals(t.Code, team.Code, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException("equipo.codigo_duplicado", $"Ya existe otro equipo con el código {team.Code}.");
        }
    }

    private async Task RequireUniqueTournamentNameAsync(Guid tournamentId, string name, CancellationToken ct)
    {
        var tournaments = await _catalog.ListTournamentsAsync(ct).ConfigureAwait(false);
        if (tournaments.Any(t => t.TournamentId != tournamentId && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException("evento.nombre_duplicado", $"Ya existe otro evento llamado «{name}».");
        }
    }

    private async Task<CatalogResult> ExecuteAsync(string action, Func<Task<CatalogResult>> apply, CancellationToken ct)
    {
        await _writer.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await apply().ConfigureAwait(false);
        }
        catch (DomainException ex)
        {
            return new CatalogResult(CommandOutcome.Rejected, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error($"No se pudo {action}.", ex);
            return new CatalogResult(CommandOutcome.Failed, StorageFailureMessage);
        }
        finally
        {
            _writer.Release();
        }
    }
}
