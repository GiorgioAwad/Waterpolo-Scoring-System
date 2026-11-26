using System.Globalization;
using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Competitions;

namespace FDNDA.Waterpolo.Application.Catalog;

/// <summary>Estado de una fila en la vista previa de la importación.</summary>
public enum RosterRowStatus
{
    /// <summary>Jugador nuevo en la nómina del evento.</summary>
    New,

    /// <summary>La nómina ya tiene ese nombre con ese gorro: no cambia nada.</summary>
    Unchanged,

    /// <summary>Choca con la nómina actual de un modo que el programa no decide solo.</summary>
    Ambiguous,
}

/// <summary>Tipo de conflicto de una fila ambigua.</summary>
public enum RosterAmbiguity
{
    None,

    /// <summary>El gorro ya lo tiene otro nombre en la nómina actual.</summary>
    CapTakenByOtherName,

    /// <summary>El nombre ya está en la nómina actual con otro gorro.</summary>
    NameAtOtherCap,
}

/// <summary>Decisión del operador sobre una fila ambigua.</summary>
public enum RosterResolution
{
    Unresolved,

    /// <summary>No importar la fila; la nómina actual queda como está.</summary>
    Skip,

    /// <summary>Es el mismo jugador: corregir su nombre.</summary>
    RenameExisting,

    /// <summary>Es otro jugador: sale de la nómina el actual y entra el del archivo.</summary>
    ReplaceExisting,

    /// <summary>Es el mismo jugador: cambia de gorro.</summary>
    MoveExistingCap,

    /// <summary>Es otro jugador con el mismo nombre: se agrega como jugador nuevo.</summary>
    AddAsNew,
}

/// <summary>Error localizado en el archivo. <see cref="Column"/> es nulo si afecta a toda la fila o al archivo.</summary>
public sealed record RosterIssue(int Row, string? Column, string Message)
{
    public override string ToString() => Column is null ? $"Fila {Row}: {Message}" : $"Fila {Row}, columna {Column}: {Message}";
}

/// <summary>
/// Fila válida del archivo, con su clasificación frente a la nómina actual. <see cref="Existing"/>
/// es el jugador actual con ese gorro (o con ese nombre, si el gorro está libre);
/// <see cref="SameName"/>, el jugador actual con el mismo nombre en otro gorro cuando el gorro está ocupado.
/// </summary>
public sealed record RosterPreviewRow(
    int Row,
    string TeamCode,
    string TeamName,
    string PlayerName,
    int Cap,
    RosterRowStatus Status,
    RosterAmbiguity Ambiguity,
    Guid TeamId,
    TournamentRosterLine? Existing,
    IReadOnlyList<RosterResolution> Options,
    string Detail)
{
    public TournamentRosterLine? SameName { get; init; }

    /// <summary>Jugador actual que cambia con la decisión dada; nulo si no cambia ninguno.</summary>
    public TournamentRosterLine? Target(RosterResolution choice) => choice switch
    {
        RosterResolution.RenameExisting or RosterResolution.ReplaceExisting => Existing,
        RosterResolution.MoveExistingCap => Ambiguity == RosterAmbiguity.CapTakenByOtherName ? SameName : Existing,
        _ => null,
    };
}

/// <summary>Equipo del archivo que no existe en el catálogo y se creará al confirmar.</summary>
public sealed record RosterNewTeam(Guid TeamId, string Code, string Name);

/// <summary>
/// Vista previa de una importación. No ha escrito nada. Guarda la nómina actual que se usó para
/// clasificar: al confirmar se comprueba que no haya cambiado.
/// </summary>
public sealed record RosterPreview(
    Guid TournamentId,
    string TournamentName,
    string FileName,
    IReadOnlyList<RosterPreviewRow> Rows,
    IReadOnlyList<RosterIssue> Errors,
    IReadOnlyList<RosterNewTeam> NewTeams,
    IReadOnlyDictionary<Guid, IReadOnlyList<TournamentRosterLine>> CurrentRosters,
    IReadOnlyList<string> Notices)
{
    public bool HasErrors => Errors.Count > 0;

    public int Count(RosterRowStatus status) => Rows.Count(r => r.Status == status);
}

/// <summary>Cambio de nombre de un jugador existente.</summary>
public sealed record PlayerRename(Guid PlayerId, string NewName);

/// <summary>Cambios de la nómina de un equipo en el evento.</summary>
public sealed record TeamRosterChanges(
    Guid TeamId,
    IReadOnlyList<TournamentRosterLine> Expected,
    IReadOnlyList<Player> NewPlayers,
    IReadOnlyList<PlayerRename> Renames,
    IReadOnlyList<TournamentRosterEntry> Final)
{
    public bool IsEmpty =>
        NewPlayers.Count == 0 && Renames.Count == 0
        && Expected.Count == Final.Count
        && Final.All(f => Expected.Any(e => e.EntryId == f.Id && e.PlayerId == f.PlayerId && e.Cap == f.Cap.Value));
}

/// <summary>Recuento de lo que hará la importación.</summary>
public sealed record RosterImportSummary(int Added, int Renamed, int Replaced, int Moved, int Unchanged, int Skipped, int NewTeams)
{
    public bool HasChanges => Added + Renamed + Replaced + Moved + NewTeams > 0;

    public override string ToString()
    {
        var parts = new List<string>();
        Add(parts, NewTeams, "equipo nuevo", "equipos nuevos");
        Add(parts, Added, "jugador agregado", "jugadores agregados");
        Add(parts, Renamed, "nombre corregido", "nombres corregidos");
        Add(parts, Replaced, "jugador reemplazado", "jugadores reemplazados");
        Add(parts, Moved, "cambio de gorro", "cambios de gorro");
        Add(parts, Unchanged, "sin cambios", "sin cambios");
        Add(parts, Skipped, "fila omitida", "filas omitidas");
        return parts.Count == 0 ? "Nada que importar" : string.Join(" · ", parts);

        static void Add(List<string> parts, int count, string one, string many)
        {
            if (count > 0)
            {
                parts.Add($"{count} {(count == 1 ? one : many)}");
            }
        }
    }
}

/// <summary>Cambios listos para escribir en una sola transacción.</summary>
public sealed record RosterImportChanges(
    Guid TournamentId,
    IReadOnlyList<Team> NewTeams,
    IReadOnlyList<TeamRosterChanges> Teams,
    RosterImportSummary Summary);

/// <summary>Resultado de aplicar las decisiones del operador: cambios o la lista de lo que falta resolver.</summary>
public sealed record RosterImportPlan(RosterImportChanges? Changes, IReadOnlyList<string> Problems)
{
    public bool IsReady => Changes is not null && Problems.Count == 0;
}

/// <summary>Fila del archivo ya separada en columnas, antes de compararla con la nómina.</summary>
public sealed record RosterFileRow(int Row, string TeamCode, string TeamName, string PlayerName, int Cap);

/// <summary>
/// Interpreta la plantilla <c>equipo_codigo,equipo_nombre,jugador_nombre,numero_gorro</c> y la
/// compara con la nómina actual del evento. Es puro: no lee ni escribe almacenamiento.
/// Nunca deduce que dos jugadores son el mismo por el nombre: cuando el nombre o el gorro
/// coinciden solo en parte, la fila queda ambigua y el operador decide.
/// </summary>
public static class RosterImportPlanner
{
    public const string ColTeamCode = "equipo_codigo";
    public const string ColTeamName = "equipo_nombre";
    public const string ColPlayerName = "jugador_nombre";
    public const string ColCap = "numero_gorro";

    /// <summary>Color y gorro con que se crea un equipo que solo existe en el archivo; se editan después.</summary>
    public const string NewTeamColorHex = "#78909C";
    public const string NewTeamCapColor = "Por definir";

    private static readonly string[] RequiredColumns = [ColTeamCode, ColTeamName, ColPlayerName, ColCap];

    /// <summary>Separa columnas y valida cada fila. No compara con la nómina.</summary>
    public static (IReadOnlyList<RosterFileRow> Rows, IReadOnlyList<RosterIssue> Errors, IReadOnlyList<string> Notices) ReadRows(RosterTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var errors = new List<RosterIssue>();
        var notices = new List<string>();
        var rows = new List<RosterFileRow>();

        var nonEmpty = table.Rows.Where(r => r.Cells.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        if (nonEmpty.Count == 0)
        {
            errors.Add(new RosterIssue(1, null, "El archivo está vacío."));
            return (rows, errors, notices);
        }

        var header = nonEmpty[0];
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        var ignored = new List<string>();
        for (var i = 0; i < header.Cells.Count; i++)
        {
            var name = NormalizeHeader(header.Cells[i]);
            if (name.Length == 0)
            {
                continue;
            }

            if (!RequiredColumns.Contains(name))
            {
                ignored.Add(header.Cells[i].Trim());
            }
            else if (!columns.TryAdd(name, i))
            {
                errors.Add(new RosterIssue(header.Number, Label(name, i), "La columna está repetida en el encabezado."));
            }
        }

        foreach (var required in RequiredColumns.Where(c => !columns.ContainsKey(c)))
        {
            errors.Add(new RosterIssue(header.Number, required,
                $"Falta la columna en el encabezado. La plantilla es {string.Join(",", RequiredColumns)}."));
        }

        if (ignored.Count > 0)
        {
            notices.Add($"Se ignoran las columnas que no son de la plantilla: {string.Join(", ", ignored)}.");
        }

        if (errors.Count > 0)
        {
            return (rows, errors, notices);
        }

        var data = nonEmpty.Skip(1).ToList();
        if (data.Count > RosterImportLimits.MaxDataRows)
        {
            errors.Add(new RosterIssue(data[RosterImportLimits.MaxDataRows].Number, null,
                $"El archivo supera el máximo de {RosterImportLimits.MaxDataRows} filas de datos. Divídalo en varios archivos."));
            return (rows, errors, notices);
        }

        if (data.Count == 0)
        {
            errors.Add(new RosterIssue(header.Number, null, "El archivo solo tiene el encabezado; no hay jugadores."));
            return (rows, errors, notices);
        }

        var teamNames = new Dictionary<string, (string Name, int Row)>(StringComparer.OrdinalIgnoreCase);
        var caps = new Dictionary<(string Team, int Cap), int>();
        var names = new Dictionary<(string Team, string Name), int>();

        foreach (var row in data)
        {
            var before = errors.Count;
            var code = Text(row, columns, ColTeamCode, 10, errors);
            var teamName = Text(row, columns, ColTeamName, 80, errors);
            var player = Text(row, columns, ColPlayerName, 80, errors);
            var cap = Cap(row, columns, errors);
            if (errors.Count > before || code is null || teamName is null || player is null || cap is null)
            {
                continue;
            }

            code = code.ToUpperInvariant();
            if (teamNames.TryGetValue(code, out var seen))
            {
                if (!SameName(seen.Name, teamName))
                {
                    errors.Add(new RosterIssue(row.Number, Label(ColTeamName, columns[ColTeamName]),
                        $"El equipo {code} ya aparece en la fila {seen.Row} con el nombre «{seen.Name}». Use el mismo nombre en todas las filas."));
                    continue;
                }
            }
            else
            {
                teamNames[code] = (teamName, row.Number);
            }

            if (caps.TryGetValue((code, cap.Value), out var capRow))
            {
                errors.Add(new RosterIssue(row.Number, Label(ColCap, columns[ColCap]),
                    $"El gorro {cap} de {code} ya está en la fila {capRow}. El mismo gorro solo se repite en equipos distintos."));
                continue;
            }

            caps[(code, cap.Value)] = row.Number;
            var nameKey = (code, NormalizeName(player));
            if (names.TryGetValue(nameKey, out var nameRow))
            {
                notices.Add($"Las filas {nameRow} y {row.Number} tienen el mismo nombre en {code}; se tratan como jugadores distintos.");
            }
            else
            {
                names[nameKey] = row.Number;
            }

            rows.Add(new RosterFileRow(row.Number, code, teamName, player, cap.Value));
        }

        return (rows, errors, notices);
    }

    /// <summary>
    /// Compara las filas válidas con la nómina actual del evento. <paramref name="currentRosters"/>
    /// tiene la nómina de cada equipo existente que aparece en el archivo.
    /// </summary>
    public static RosterPreview Classify(
        Tournament tournament,
        string fileName,
        IReadOnlyList<RosterFileRow> fileRows,
        IReadOnlyList<RosterIssue> fileErrors,
        IReadOnlyList<string> fileNotices,
        IReadOnlyList<Team> teams,
        IReadOnlyDictionary<Guid, IReadOnlyList<TournamentRosterLine>> currentRosters)
    {
        ArgumentNullException.ThrowIfNull(tournament);
        var rows = new List<RosterPreviewRow>();
        var notices = new List<string>(fileNotices);
        var newTeams = new List<RosterNewTeam>();

        foreach (var group in fileRows.GroupBy(r => r.TeamCode, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var team = teams.FirstOrDefault(t => string.Equals(t.Code, group.Key, StringComparison.OrdinalIgnoreCase));
            if (team is null)
            {
                var created = new RosterNewTeam(Guid.NewGuid(), first.TeamCode, first.TeamName);
                newTeams.Add(created);
                notices.Add($"El equipo {created.Code} («{created.Name}») no está en el catálogo: se creará con color y gorro por definir.");
                rows.AddRange(group.Select(r => Row(r, RosterRowStatus.New, created.TeamId, "Jugador nuevo (equipo nuevo).")));
                continue;
            }

            if (!SameName(team.Name, first.TeamName))
            {
                notices.Add($"El equipo {team.Code} se llama «{team.Name}» en el catálogo y «{first.TeamName}» en el archivo; la importación no cambia el nombre del equipo.");
            }

            var current = currentRosters.TryGetValue(team.Id, out var lines) ? lines : [];
            ClassifyTeam(group.ToList(), team, current, rows, notices);
        }

        rows.Sort((a, b) => a.Row.CompareTo(b.Row));
        return new RosterPreview(
            tournament.Id,
            tournament.Name,
            fileName,
            rows,
            fileErrors,
            newTeams,
            currentRosters,
            notices);
    }

    /// <summary>
    /// Aplica las decisiones del operador (por número de fila) y calcula los cambios. Devuelve
    /// problemas mientras haya errores, filas sin resolver o decisiones incompatibles.
    /// </summary>
    public static RosterImportPlan Plan(RosterPreview preview, IReadOnlyDictionary<int, RosterResolution> choices)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(choices);
        var problems = new List<string>();
        if (preview.HasErrors)
        {
            problems.Add($"El archivo tiene {preview.Errors.Count} {(preview.Errors.Count == 1 ? "error" : "errores")}. Corríjalo y vuelva a abrirlo.");
            return new RosterImportPlan(null, problems);
        }

        var decided = new List<(RosterPreviewRow Row, RosterResolution Choice)>();
        var unresolved = new List<int>();
        foreach (var row in preview.Rows.Where(r => r.Status == RosterRowStatus.Ambiguous))
        {
            var choice = choices.TryGetValue(row.Row, out var c) ? c : RosterResolution.Unresolved;
            if (choice == RosterResolution.Unresolved || !row.Options.Contains(choice))
            {
                unresolved.Add(row.Row);
            }
            else
            {
                decided.Add((row, choice));
            }
        }

        if (unresolved.Count > 0)
        {
            problems.Add($"Falta decidir {(unresolved.Count == 1 ? "la fila" : "las filas")} {string.Join(", ", unresolved)}.");
        }

        foreach (var clash in decided
            .Where(d => d.Row.Target(d.Choice) is not null)
            .GroupBy(d => d.Row.Target(d.Choice)!.EntryId)
            .Where(g => g.Count() > 1))
        {
            var player = clash.First().Row.Target(clash.First().Choice)!;
            problems.Add($"Las filas {string.Join(" y ", clash.Select(c => c.Row.Row))} cambian al mismo jugador («{player.PlayerName}», gorro {player.Cap}). Omita una de ellas.");
        }

        if (problems.Count > 0)
        {
            return new RosterImportPlan(null, problems);
        }

        int added = 0, renamed = 0, replaced = 0, moved = 0, skipped = 0;
        var teamChanges = new List<TeamRosterChanges>();
        var choiceByRow = decided.ToDictionary(d => d.Row.Row, d => d.Choice);

        foreach (var group in preview.Rows.GroupBy(r => r.TeamId))
        {
            var teamId = group.Key;
            var expected = preview.CurrentRosters.TryGetValue(teamId, out var lines) ? lines : [];
            var final = expected.ToDictionary(e => e.EntryId, e => (e.PlayerId, Cap: e.Cap));
            var newPlayers = new List<Player>();
            var renames = new List<PlayerRename>();

            foreach (var row in group)
            {
                var choice = row.Status switch
                {
                    RosterRowStatus.New => RosterResolution.AddAsNew,
                    RosterRowStatus.Ambiguous => choiceByRow[row.Row],
                    _ => RosterResolution.Skip,
                };

                switch (choice)
                {
                    case RosterResolution.AddAsNew:
                        AddNew(row);
                        added++;
                        break;
                    case RosterResolution.RenameExisting:
                        renames.Add(new PlayerRename(row.Existing!.PlayerId, row.PlayerName));
                        renamed++;
                        break;
                    case RosterResolution.ReplaceExisting:
                        final.Remove(row.Existing!.EntryId);
                        AddNew(row);
                        replaced++;
                        break;
                    case RosterResolution.MoveExistingCap:
                        var mover = row.Target(choice)!;
                        final[mover.EntryId] = (mover.PlayerId, row.Cap);
                        moved++;
                        break;
                    case RosterResolution.Skip when row.Status == RosterRowStatus.Ambiguous:
                        skipped++;
                        break;
                }
            }

            var repeated = final.Values.GroupBy(v => v.Cap).FirstOrDefault(g => g.Count() > 1);
            if (repeated is not null)
            {
                problems.Add($"Con esas decisiones el gorro {repeated.Key} quedaría repetido en {group.First().TeamCode}. Revise las filas de ese equipo.");
                continue;
            }

            var entries = final
                .Select(f => new TournamentRosterEntry(f.Key, preview.TournamentId, teamId, f.Value.PlayerId, new CapNumber(f.Value.Cap)))
                .OrderBy(e => e.Cap.Value)
                .ToList();
            var change = new TeamRosterChanges(teamId, expected, newPlayers, renames, entries);
            if (!change.IsEmpty)
            {
                teamChanges.Add(change);
            }

            void AddNew(RosterPreviewRow row)
            {
                var player = new Player(Guid.NewGuid(), teamId, row.PlayerName);
                newPlayers.Add(player);
                final[Guid.NewGuid()] = (player.Id, row.Cap);
            }
        }

        if (problems.Count > 0)
        {
            return new RosterImportPlan(null, problems);
        }

        var newTeams = preview.NewTeams
            .Select(t => new Team(t.TeamId, t.Code, t.Name, NewTeamColorHex, NewTeamCapColor))
            .ToList();
        var summary = new RosterImportSummary(
            added, renamed, replaced, moved, preview.Count(RosterRowStatus.Unchanged), skipped, newTeams.Count);
        return new RosterImportPlan(new RosterImportChanges(preview.TournamentId, newTeams, teamChanges, summary), problems);
    }

    /// <summary>Nombre comparable: sin espacios repetidos ni diferencia de mayúsculas. Los acentos sí cuentan.</summary>
    public static string NormalizeName(string name) =>
        string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static void ClassifyTeam(
        List<RosterFileRow> fileRows, Team team, IReadOnlyList<TournamentRosterLine> current, List<RosterPreviewRow> rows, List<string> notices)
    {
        // exact: coincidencias exactas; touched: jugadores actuales a los que alguna fila se refiere.
        var exact = new HashSet<Guid>();
        var touched = new HashSet<Guid>();
        var pending = new List<RosterFileRow>();

        // Primero las coincidencias exactas (mismo gorro y mismo nombre): no cambian nada.
        foreach (var row in fileRows)
        {
            var match = current.FirstOrDefault(c => c.Cap == row.Cap && SameName(c.PlayerName, row.PlayerName));
            if (match is not null)
            {
                exact.Add(match.EntryId);
                touched.Add(match.EntryId);
                rows.Add(Row(row, RosterRowStatus.Unchanged, team.Id, "Ya está en la nómina con ese gorro.") with { Existing = match });
            }
            else
            {
                pending.Add(row);
            }
        }

        foreach (var row in pending)
        {
            // Solo se descartan las coincidencias exactas: así el orden de las filas no decide
            // en silencio que un nombre repetido sea un jugador nuevo.
            var sameName = current.Where(c => !exact.Contains(c.EntryId) && SameName(c.PlayerName, row.PlayerName)).ToList();
            var atCap = current.FirstOrDefault(c => c.Cap == row.Cap);
            if (atCap is not null)
            {
                touched.Add(atCap.EntryId);
                if (sameName.Count == 1)
                {
                    // Típico de un intercambio de gorros: el nombre está en otro gorro y este gorro es de otro.
                    var mover = sameName[0];
                    touched.Add(mover.EntryId);
                    rows.Add(Row(row, RosterRowStatus.Ambiguous, team.Id,
                        $"El gorro {row.Cap} de {team.Code} es de «{atCap.PlayerName}» y «{mover.PlayerName}» tiene el gorro {mover.Cap} en la nómina actual. ¿«{mover.PlayerName}» cambia al gorro {row.Cap}, se corrige el nombre de «{atCap.PlayerName}», o es otro jugador?") with
                    {
                        Ambiguity = RosterAmbiguity.CapTakenByOtherName,
                        Existing = atCap,
                        SameName = mover,
                        Options = [RosterResolution.MoveExistingCap, RosterResolution.RenameExisting, RosterResolution.ReplaceExisting, RosterResolution.Skip],
                    });
                    continue;
                }

                rows.Add(Row(row, RosterRowStatus.Ambiguous, team.Id,
                    $"El gorro {row.Cap} de {team.Code} es de «{atCap.PlayerName}» en la nómina actual. ¿Es el mismo jugador con el nombre corregido, u otro jugador?") with
                {
                    Ambiguity = RosterAmbiguity.CapTakenByOtherName,
                    Existing = atCap,
                    Options = [RosterResolution.RenameExisting, RosterResolution.ReplaceExisting, RosterResolution.Skip],
                });
                continue;
            }

            if (sameName.Count == 1)
            {
                var other = sameName[0];
                touched.Add(other.EntryId);
                rows.Add(Row(row, RosterRowStatus.Ambiguous, team.Id,
                    $"«{other.PlayerName}» ya está en la nómina de {team.Code} con el gorro {other.Cap}. ¿Es el mismo jugador que cambia de gorro, u otro con el mismo nombre?") with
                {
                    Ambiguity = RosterAmbiguity.NameAtOtherCap,
                    Existing = other,
                    Options = [RosterResolution.MoveExistingCap, RosterResolution.AddAsNew, RosterResolution.Skip],
                });
            }
            else if (sameName.Count > 1)
            {
                rows.Add(Row(row, RosterRowStatus.Ambiguous, team.Id,
                    $"Hay {sameName.Count} jugadores llamados «{row.PlayerName}» en la nómina de {team.Code} (gorros {string.Join(", ", sameName.Select(s => s.Cap))}). Para cambiar el gorro de uno de ellos, edítelo a mano.") with
                {
                    Ambiguity = RosterAmbiguity.NameAtOtherCap,
                    Options = [RosterResolution.AddAsNew, RosterResolution.Skip],
                });
            }
            else
            {
                rows.Add(Row(row, RosterRowStatus.New, team.Id, "Jugador nuevo."));
            }
        }

        var kept = current.Where(c => !touched.Contains(c.EntryId)).ToList();
        if (kept.Count > 0)
        {
            notices.Add($"{kept.Count} {(kept.Count == 1 ? "jugador" : "jugadores")} de {team.Code} no {(kept.Count == 1 ? "está" : "están")} en el archivo y se {(kept.Count == 1 ? "conserva" : "conservan")} (gorros {string.Join(", ", kept.Select(k => k.Cap))}). La importación no quita jugadores; para quitarlos use la edición manual.");
        }
    }

    private static RosterPreviewRow Row(RosterFileRow row, RosterRowStatus status, Guid teamId, string detail) =>
        new(row.Row, row.TeamCode, row.TeamName, row.PlayerName, row.Cap, status, RosterAmbiguity.None, teamId, null, [], detail);

    private static string? Text(RosterTableRow row, Dictionary<string, int> columns, string column, int maxLength, List<RosterIssue> errors)
    {
        var index = columns[column];
        var value = index < row.Cells.Count ? row.Cells[index].Trim() : string.Empty;
        if (value.Length == 0)
        {
            errors.Add(new RosterIssue(row.Number, Label(column, index), "El dato es obligatorio."));
            return null;
        }

        if (value.Length > maxLength)
        {
            errors.Add(new RosterIssue(row.Number, Label(column, index), $"Supera los {maxLength} caracteres permitidos."));
            return null;
        }

        return value;
    }

    private static int? Cap(RosterTableRow row, Dictionary<string, int> columns, List<RosterIssue> errors)
    {
        var index = columns[ColCap];
        var value = index < row.Cells.Count ? row.Cells[index].Trim() : string.Empty;
        if (value.Length == 0)
        {
            errors.Add(new RosterIssue(row.Number, Label(ColCap, index), "El dato es obligatorio."));
            return null;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var cap) || cap is < CapNumber.Min or > CapNumber.Max)
        {
            errors.Add(new RosterIssue(row.Number, Label(ColCap, index),
                $"«{value}» no es un número de gorro válido: debe ser un entero entre {CapNumber.Min} y {CapNumber.Max}."));
            return null;
        }

        return cap;
    }

    private static string NormalizeHeader(string text) => text.Trim().Trim('﻿').Trim().ToLowerInvariant();

    private static bool SameName(string a, string b) => NormalizeName(a) == NormalizeName(b);

    /// <summary>Nombre de columna con su letra de hoja de cálculo, p. ej. «numero_gorro (D)».</summary>
    private static string Label(string column, int index) => $"{column} ({ColumnLetter(index)})";

    private static string ColumnLetter(int index)
    {
        var letters = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            letters = (char)('A' + ((n - 1) % 26)) + letters;
        }

        return letters;
    }
}
