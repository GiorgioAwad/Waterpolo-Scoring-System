using System.Text;
using FDNDA.Waterpolo.Application.Abstractions;
using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Infrastructure.Import;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;

namespace FDNDA.Waterpolo.Infrastructure.Tests;

/// <summary>R3 — importación de nóminas CSV/XLSX con vista previa (RF02), sobre SQLite temporal.</summary>
public class RosterImportTests
{
    private static readonly Dictionary<int, RosterResolution> NoChoices = [];

    // ---------- Importar y reimportar ----------

    [Fact]
    public async Task La_plantilla_CSV_se_importa_en_el_evento_elegido_al_confirmar()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);
        var tournamentId = await NewTournamentAsync(catalog);

        var preview = await PreviewAsync(catalog, tournamentId, RosterFiles.Sample("plantilla-nomina.csv"));

        Assert.False(preview.HasErrors);
        Assert.Equal(6, preview.Count(RosterRowStatus.New));
        Assert.Equal([2, 3, 4, 5, 6, 7], preview.Rows.Select(r => r.Row)); // fila 1 = encabezado
        Assert.Empty(await catalog.GetTournamentRosterAsync(tournamentId, DemoDataSeeder.HomeTeamId)); // la vista previa no escribe

        var result = await catalog.ConfirmRosterImportAsync(preview, NoChoices);

        Assert.Equal(CommandOutcome.Applied, result.Outcome);
        Assert.Contains("6 jugadores agregados", result.Message, StringComparison.Ordinal);
        var del = await catalog.GetTournamentRosterAsync(tournamentId, DemoDataSeeder.HomeTeamId);
        Assert.Equal(["Marco Arrecife", "Luis Marea", "Diego Coral"], del.Select(r => r.PlayerName));
        Assert.Equal([1, 2, 3], del.Select(r => r.Cap));
        var tib = await catalog.GetTournamentRosterAsync(tournamentId, DemoDataSeeder.AwayTeamId);
        Assert.Equal(["Renzo Aleta", "Fabio Cardumen", "Julián Estela"], tib.Select(r => r.PlayerName)); // UTF-8: tilde intacta
        Assert.Equal(13, (await catalog.GetTournamentRosterAsync(DemoDataSeeder.TournamentId, DemoDataSeeder.HomeTeamId)).Count); // otro evento intacto
    }

    [Fact]
    public async Task Reimportar_la_misma_plantilla_no_duplica_jugadores()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);
        var tournamentId = await NewTournamentAsync(catalog);
        var file = RosterFiles.Sample("plantilla-nomina.csv");
        await ConfirmAsync(catalog, await PreviewAsync(catalog, tournamentId, file));
        var players = await CountAsync(storage, "SELECT COUNT(*) FROM player;");
        var before = await catalog.GetTournamentRosterAsync(tournamentId, DemoDataSeeder.HomeTeamId);

        var again = await PreviewAsync(catalog, tournamentId, file);
        Assert.Equal(6, again.Count(RosterRowStatus.Unchanged));
        var result = await catalog.ConfirmRosterImportAsync(again, NoChoices);

        Assert.Equal(CommandOutcome.AlreadyApplied, result.Outcome);
        Assert.Equal(players, await CountAsync(storage, "SELECT COUNT(*) FROM player;"));
        Assert.Equal(before, await catalog.GetTournamentRosterAsync(tournamentId, DemoDataSeeder.HomeTeamId)); // mismos jugadores e inscripciones
    }

    [Fact]
    public async Task La_importacion_no_quita_a_los_jugadores_que_no_estan_en_el_archivo()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);

        // El evento demo ya tiene 13 jugadores por equipo; la plantilla trae los tres primeros de cada uno.
        var preview = await PreviewAsync(catalog, DemoDataSeeder.TournamentId, RosterFiles.Sample("plantilla-nomina.csv"));

        Assert.Equal(6, preview.Count(RosterRowStatus.Unchanged));
        Assert.Contains(preview.Notices, n => n.StartsWith("10 jugadores de DEL no están en el archivo y se conservan", StringComparison.Ordinal));
        Assert.Equal(CommandOutcome.AlreadyApplied, (await catalog.ConfirmRosterImportAsync(preview, NoChoices)).Outcome);
        Assert.Equal(13, (await catalog.GetTournamentRosterAsync(DemoDataSeeder.TournamentId, DemoDataSeeder.HomeTeamId)).Count);
    }

    // ---------- Validación ----------

    [Fact]
    public async Task Los_errores_indican_fila_y_columna_y_bloquean_la_importacion()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);
        var tournamentId = await NewTournamentAsync(catalog);
        var file = RosterFiles.Csv(storage,
            RosterFiles.Header,
            "DEL,Delfines del Pacífico,Marco Arrecife,1",
            "DEL,Delfines del Pacífico,,2",
            "DEL,Delfines del Pacífico,Diego Coral,tres",
            "TIB,Club Deportivo Tiburones del Norte,Renzo Aleta,100",
            ",Sin código,Fabio Cardumen,4",
            "DEL,Delfines del Pacífico,Nombre muy largo " + new string('x', 80) + ",5");

        var preview = await PreviewAsync(catalog, tournamentId, file);

        Assert.True(preview.HasErrors);
        Assert.Collection(
            preview.Errors,
            e => Assert.Equal((3, "jugador_nombre (C)"), (e.Row, e.Column)),
            e => Assert.Equal((4, "numero_gorro (D)"), (e.Row, e.Column)),
            e => Assert.Equal((5, "numero_gorro (D)"), (e.Row, e.Column)),
            e => Assert.Equal((6, "equipo_codigo (A)"), (e.Row, e.Column)),
            e => Assert.Equal((7, "jugador_nombre (C)"), (e.Row, e.Column)));
        Assert.Equal("Fila 4, columna numero_gorro (D): «tres» no es un número de gorro válido: debe ser un entero entre 1 y 99.", preview.Errors[1].ToString());
        Assert.Contains("obligatorio", preview.Errors[0].Message, StringComparison.Ordinal);

        var result = await catalog.ConfirmRosterImportAsync(preview, NoChoices);
        Assert.Equal(CommandOutcome.Rejected, result.Outcome);
        Assert.Empty(await catalog.GetTournamentRosterAsync(tournamentId, DemoDataSeeder.HomeTeamId)); // ni siquiera la fila válida
    }

    [Fact]
    public async Task Falta_una_columna_obligatoria_en_el_encabezado()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);
        var tournamentId = await NewTournamentAsync(catalog);
        var file = RosterFiles.Csv(storage, "equipo_codigo,equipo_nombre,jugador,numero_gorro", "DEL,Delfines,Marco,1");

        var preview = await PreviewAsync(catalog, tournamentId, file);

        var error = Assert.Single(preview.Errors);
        Assert.Equal((1, "jugador_nombre"), (error.Row, error.Column));
        Assert.Contains(preview.Notices, n => n.Contains("jugador", StringComparison.Ordinal)); // columna desconocida ignorada
        Assert.Empty(preview.Rows);
    }

    [Fact]
    public async Task El_gorro_repetido_en_el_mismo_equipo_es_error_y_en_equipos_distintos_es_valido()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);
        var tournamentId = await NewTournamentAsync(catalog);

        var valid = await PreviewAsync(catalog, tournamentId, RosterFiles.Csv(storage,
            RosterFiles.Header,
            "DEL,Delfines del Pacífico,Marco Arrecife,7",
            "TIB,Club Deportivo Tiburones del Norte,Renzo Aleta,7"));
        Assert.False(valid.HasErrors);

        var repeated = await PreviewAsync(catalog, tournamentId, RosterFiles.Csv(storage,
            RosterFiles.Header,
            "DEL,Delfines del Pacífico,Marco Arrecife,7",
            "TIB,Club Deportivo Tiburones del Norte,Renzo Aleta,7",
            "del,Delfines del Pacífico,Luis Marea,7"));
        var error = Assert.Single(repeated.Errors);
        Assert.Equal((4, "numero_gorro (D)"), (error.Row, error.Column));
        Assert.Contains("ya está en la fila 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task El_limite_de_filas_se_valida_antes_de_importar()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);
        var tournamentId = await NewTournamentAsync(catalog);
        var lines = new List<string> { RosterFiles.Header };
        lines.AddRange(Enumerable.Range(1, RosterImportLimits.MaxDataRows + 1).Select(i => $"T{i % 400},Equipo {i % 400},Jugador {i},{(i % 99) + 1}"));

        var preview = await PreviewAsync(catalog, tournamentId, RosterFiles.Csv(storage, [.. lines]));

        var error = Assert.Single(preview.Errors);
        Assert.Contains($"máximo de {RosterImportLimits.MaxDataRows} filas", error.Message, StringComparison.Ordinal);
        Assert.Empty(preview.Rows);
    }

    // ---------- Cancelar y transacción ----------

    [Fact]
    public async Task Cancelar_la_vista_previa_no_escribe_nada()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);
        var tournamentId = await NewTournamentAsync(catalog);
        var tables = await SnapshotAsync(storage);

        var preview = await PreviewAsync(catalog, tournamentId, RosterFiles.Csv(storage,
            RosterFiles.Header,
            "NUE,Equipo Nuevo,Ana Ola,1",
            "DEL,Delfines del Pacífico,Marco Arrecife,1"));
        Assert.Single(preview.NewTeams);

        // Cancelar = descartar la vista previa sin confirmar.
        Assert.Equal(tables, await SnapshotAsync(storage));
    }

    [Fact]
    public async Task Si_la_nomina_cambia_antes_de_confirmar_no_se_importa_nada()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);
        var tournamentId = await NewTournamentAsync(catalog);
        var preview = await PreviewAsync(catalog, tournamentId, RosterFiles.Csv(storage,
            RosterFiles.Header,
            "NUE,Equipo Nuevo,Ana Ola,1",
            "DEL,Delfines del Pacífico,Marco Arrecife,1",
            "TIB,Club Deportivo Tiburones del Norte,Renzo Aleta,1"));

        // Otro cambio llega entre la vista previa y la confirmación: TIB ya no está como se previsualizó.
        await catalog.AddRosterPlayerAsync(tournamentId, DemoDataSeeder.AwayTeamId, Guid.NewGuid(), Guid.NewGuid(), "Jugador Intermedio", 9);
        var tables = await SnapshotAsync(storage);

        var result = await catalog.ConfirmRosterImportAsync(preview, NoChoices);

        // El equipo nuevo y DEL se escribieron antes de detectar el cambio en TIB: la transacción los deshace.
        Assert.Equal(CommandOutcome.Rejected, result.Outcome);
        Assert.Contains("No se importó nada", result.Message, StringComparison.Ordinal);
        Assert.Equal(tables, await SnapshotAsync(storage));
        Assert.DoesNotContain(await catalog.ListTeamsAsync(), t => t.Code == "NUE");
    }

    // ---------- Conflictos: resolución explícita ----------

    [Fact]
    public async Task Un_gorro_ocupado_por_otro_nombre_exige_una_decision_explicita()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);
        var tournamentId = await TournamentWithSampleAsync(catalog);
        var file = RosterFiles.Csv(storage, RosterFiles.Header, "DEL,Delfines del Pacífico,Luis Marea Ríos,2");

        var preview = await PreviewAsync(catalog, tournamentId, file);
        var row = Assert.Single(preview.Rows);
        Assert.Equal(RosterRowStatus.Ambiguous, row.Status);
        Assert.Equal(RosterAmbiguity.CapTakenByOtherName, row.Ambiguity);
        Assert.Equal("Luis Marea", row.Existing!.PlayerName);
        Assert.Equal([RosterResolution.RenameExisting, RosterResolution.ReplaceExisting, RosterResolution.Skip], row.Options);

        var unresolved = await catalog.ConfirmRosterImportAsync(preview, NoChoices);
        Assert.Equal(CommandOutcome.Rejected, unresolved.Outcome);
        Assert.Contains("Falta decidir la fila 2", unresolved.Message, StringComparison.Ordinal);
        Assert.Equal("Luis Marea", (await catalog.GetTournamentRosterAsync(tournamentId, DemoDataSeeder.HomeTeamId))[1].PlayerName);

        var skipped = await catalog.ConfirmRosterImportAsync(preview, new Dictionary<int, RosterResolution> { [2] = RosterResolution.Skip });
        Assert.Equal(CommandOutcome.AlreadyApplied, skipped.Outcome);
    }

    [Fact]
    public async Task Corregir_el_nombre_conserva_al_jugador_y_reemplazar_inscribe_a_otro()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);
        var tournamentId = await TournamentWithSampleAsync(catalog);
        var original = (await catalog.GetTournamentRosterAsync(tournamentId, DemoDataSeeder.HomeTeamId)).ToList();

        var renamePreview = await PreviewAsync(catalog, tournamentId,
            RosterFiles.Csv(storage, RosterFiles.Header, "DEL,Delfines del Pacífico,Luis Marea Ríos,2"));
        var renamed = await catalog.ConfirmRosterImportAsync(renamePreview, new Dictionary<int, RosterResolution> { [2] = RosterResolution.RenameExisting });
        Assert.Equal(CommandOutcome.Applied, renamed.Outcome);
        var afterRename = await catalog.GetTournamentRosterAsync(tournamentId, DemoDataSeeder.HomeTeamId);
        Assert.Equal(original[1].PlayerId, afterRename[1].PlayerId);
        Assert.Equal("Luis Marea Ríos", afterRename[1].PlayerName);

        var replacePreview = await PreviewAsync(catalog, tournamentId,
            RosterFiles.Csv(storage, RosterFiles.Header, "DEL,Delfines del Pacífico,Pablo Oleaje,2"));
        var replaced = await catalog.ConfirmRosterImportAsync(replacePreview, new Dictionary<int, RosterResolution> { [2] = RosterResolution.ReplaceExisting });
        Assert.Equal(CommandOutcome.Applied, replaced.Outcome);
        var afterReplace = await catalog.GetTournamentRosterAsync(tournamentId, DemoDataSeeder.HomeTeamId);
        Assert.Equal(3, afterReplace.Count);
        Assert.Equal("Pablo Oleaje", afterReplace[1].PlayerName);
        Assert.NotEqual(original[1].PlayerId, afterReplace[1].PlayerId);
        Assert.DoesNotContain(afterReplace, r => r.PlayerId == original[1].PlayerId);
    }

    [Fact]
    public async Task El_mismo_nombre_con_otro_gorro_no_se_une_por_el_nombre_sin_decision()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);
        var tournamentId = await TournamentWithSampleAsync(catalog);
        var diego = (await catalog.GetTournamentRosterAsync(tournamentId, DemoDataSeeder.HomeTeamId))[2];
        var file = RosterFiles.Csv(storage, RosterFiles.Header, "DEL,Delfines del Pacífico,diego  coral,9");

        var preview = await PreviewAsync(catalog, tournamentId, file);
        var row = Assert.Single(preview.Rows);
        Assert.Equal(RosterAmbiguity.NameAtOtherCap, row.Ambiguity);
        Assert.Equal(diego.EntryId, row.Existing!.EntryId);
        Assert.Equal(CommandOutcome.Rejected, (await catalog.ConfirmRosterImportAsync(preview, NoChoices)).Outcome);

        // Elegir «es otro jugador» crea un jugador nuevo y deja al actual en su gorro.
        var asNew = await catalog.ConfirmRosterImportAsync(preview, new Dictionary<int, RosterResolution> { [2] = RosterResolution.AddAsNew });
        Assert.Equal(CommandOutcome.Applied, asNew.Outcome);
        var roster = await catalog.GetTournamentRosterAsync(tournamentId, DemoDataSeeder.HomeTeamId);
        Assert.Equal(4, roster.Count);
        Assert.Contains(roster, r => r.PlayerId == diego.PlayerId && r.Cap == 3);
        Assert.Contains(roster, r => r.PlayerId != diego.PlayerId && r.Cap == 9);
    }

    [Fact]
    public async Task Cambiar_de_gorro_conserva_al_jugador_y_un_intercambio_se_resuelve_moviendo_a_ambos()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);
        var tournamentId = await TournamentWithSampleAsync(catalog);
        var before = await catalog.GetTournamentRosterAsync(tournamentId, DemoDataSeeder.HomeTeamId);

        // Marco (1) y Luis (2) intercambian gorros.
        var preview = await PreviewAsync(catalog, tournamentId, RosterFiles.Csv(storage,
            RosterFiles.Header,
            "DEL,Delfines del Pacífico,Marco Arrecife,2",
            "DEL,Delfines del Pacífico,Luis Marea,1"));
        Assert.All(preview.Rows, r => Assert.Contains(RosterResolution.MoveExistingCap, r.Options));
        var result = await catalog.ConfirmRosterImportAsync(preview, new Dictionary<int, RosterResolution>
        {
            [2] = RosterResolution.MoveExistingCap,
            [3] = RosterResolution.MoveExistingCap,
        });

        Assert.Equal(CommandOutcome.Applied, result.Outcome);
        Assert.Contains("2 cambios de gorro", result.Message, StringComparison.Ordinal);
        var after = await catalog.GetTournamentRosterAsync(tournamentId, DemoDataSeeder.HomeTeamId);
        Assert.Equal(before[0].PlayerId, after.Single(r => r.Cap == 2).PlayerId);
        Assert.Equal(before[1].PlayerId, after.Single(r => r.Cap == 1).PlayerId);
        Assert.Equal(before[2], after[2]);

        // Mover solo uno dejaría el gorro repetido: se rechaza sin escribir.
        var half = await PreviewAsync(catalog, tournamentId, RosterFiles.Csv(storage,
            RosterFiles.Header, "DEL,Delfines del Pacífico,Diego Coral,1"));
        var rejected = await catalog.ConfirmRosterImportAsync(half, new Dictionary<int, RosterResolution> { [2] = RosterResolution.MoveExistingCap });
        Assert.Equal(CommandOutcome.Rejected, rejected.Outcome);
        Assert.Contains("gorro 1 quedaría repetido", rejected.Message, StringComparison.Ordinal);
        Assert.Equal(after, await catalog.GetTournamentRosterAsync(tournamentId, DemoDataSeeder.HomeTeamId));
    }

    [Fact]
    public async Task Dos_decisiones_sobre_el_mismo_jugador_se_rechazan()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);
        var tournamentId = await TournamentWithSampleAsync(catalog);
        var preview = await PreviewAsync(catalog, tournamentId, RosterFiles.Csv(storage,
            RosterFiles.Header,
            "DEL,Delfines del Pacífico,Marco Arrecife Soto,1",
            "DEL,Delfines del Pacífico,Marco Arrecife,8"));

        var result = await catalog.ConfirmRosterImportAsync(preview, new Dictionary<int, RosterResolution>
        {
            [2] = RosterResolution.RenameExisting,
            [3] = RosterResolution.MoveExistingCap,
        });

        Assert.Equal(CommandOutcome.Rejected, result.Outcome);
        Assert.Contains("Las filas 2 y 3 cambian al mismo jugador", result.Message, StringComparison.Ordinal);
    }

    // ---------- Equipos y partidos ----------

    [Fact]
    public async Task Un_equipo_que_no_esta_en_el_catalogo_se_crea_al_confirmar()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);
        var tournamentId = await NewTournamentAsync(catalog);
        var preview = await PreviewAsync(catalog, tournamentId, RosterFiles.Csv(storage,
            RosterFiles.Header, "nue,Equipo Nuevo,Ana Ola,4", "NUE,Equipo Nuevo,Rosa Espuma,5"));

        var created = Assert.Single(preview.NewTeams);
        Assert.Equal("NUE", created.Code);
        Assert.Equal(CommandOutcome.Applied, (await catalog.ConfirmRosterImportAsync(preview, NoChoices)).Outcome);

        var team = Assert.Single(await catalog.ListTeamsAsync(), t => t.Code == "NUE");
        Assert.Equal("Equipo Nuevo", team.Name);
        Assert.Equal(RosterImportPlanner.NewTeamCapColor, team.CapColor);
        Assert.Equal(2, (await catalog.GetTournamentRosterAsync(tournamentId, team.Id)).Count);
    }

    [Fact]
    public async Task Los_partidos_ya_programados_conservan_la_nomina_con_la_que_se_programaron()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, desk) = Create(storage);
        var tournamentId = await NewTournamentAsync(catalog);
        var matchId = Guid.NewGuid();
        Assert.True((await desk.Operations.ScheduleMatchAsync(new ScheduleMatch(
            matchId, tournamentId, "Fecha 1", null, DemoDataSeeder.HomeTeamId, DemoDataSeeder.AwayTeamId))).IsSuccess);

        var preview = await PreviewAsync(catalog, tournamentId, RosterFiles.Sample("plantilla-nomina.csv"));
        Assert.Contains(preview.Notices, n => n.StartsWith("Hay 1 partido ya programado", StringComparison.Ordinal));
        Assert.Contains(preview.Notices, n => n.Contains("«Actualizar nómina»", StringComparison.Ordinal));
        await ConfirmAsync(catalog, preview);

        var match = (await new SqliteMatchRepository(storage.OpenDatabase()).GetAsync(matchId))!;
        Assert.Empty(match.Roster);
    }

    // ---------- Lector de archivos ----------

    [Fact]
    public async Task El_XLSX_se_lee_igual_que_el_CSV_con_textos_compartidos_en_linea_y_numeros()
    {
        await using var storage = await TempStorage.CreateAsync();
        var reader = new RosterFileReader();
        var xlsx = RosterFiles.Xlsx(storage,
        [
            ["equipo_codigo", "equipo_nombre", "jugador_nombre", "numero_gorro"],
            ["DEL", "Delfines del Pacífico", "Marco Arrecife", 1],
            ["DEL", "Delfines del Pacífico", "Luis Marea", 2.0],
            ["TIB", "Club Deportivo Tiburones del Norte", "Julián Estela", "3"],
        ]);

        var table = reader.Read(xlsx);

        Assert.Equal([1, 2, 3, 4], table.Rows.Select(r => r.Number));
        Assert.Equal(["DEL", "Delfines del Pacífico", "Marco Arrecife", "1"], table.Rows[1].Cells);
        Assert.Equal("2", table.Rows[2].Cells[3]); // 2.0 guardado como número
        Assert.Equal("Julián Estela", table.Rows[3].Cells[2]);
        Assert.DoesNotContain(table.Rows.SelectMany(r => r.Cells), c => c.Contains("FONETICA", StringComparison.Ordinal));

        var (rows, errors, _) = RosterImportPlanner.ReadRows(table);
        Assert.Empty(errors);
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public async Task La_plantilla_XLSX_de_muestra_se_importa_como_la_CSV()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);
        var tournamentId = await NewTournamentAsync(catalog);
        var reader = new RosterFileReader();

        Assert.Equal(
            reader.Read(RosterFiles.Sample("plantilla-nomina.csv")).Rows.Select(r => string.Join("|", r.Cells)),
            reader.Read(RosterFiles.Sample("plantilla-nomina.xlsx")).Rows.Select(r => string.Join("|", r.Cells)));

        var preview = await PreviewAsync(catalog, tournamentId, RosterFiles.Sample("plantilla-nomina.xlsx"));
        Assert.False(preview.HasErrors);
        Assert.Equal(6, preview.Count(RosterRowStatus.New));
    }

    [Fact]
    public async Task Un_XLSX_guardado_por_Microsoft_Excel_se_importa_como_la_CSV()
    {
        // samples/plantilla-nomina-excel.xlsx lo guardó Excel 16 (cadenas compartidas, tema, estilos y
        // extensiones x14ac/xr), no una librería: es el formato que llegará de la FDNDA.
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);
        var tournamentId = await NewTournamentAsync(catalog);
        var reader = new RosterFileReader();
        var path = RosterFiles.Sample("plantilla-nomina-excel.xlsx");
        using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
        using (var app = new StreamReader(zip.GetEntry("docProps/app.xml")!.Open()))
        {
            Assert.Contains("<Application>Microsoft Excel</Application>", await app.ReadToEndAsync(), StringComparison.Ordinal);
        }

        Assert.Equal(
            reader.Read(RosterFiles.Sample("plantilla-nomina.csv")).Rows.Select(r => string.Join("|", r.Cells)),
            reader.Read(path).Rows.Select(r => string.Join("|", r.Cells)));

        var preview = await PreviewAsync(catalog, tournamentId, path);
        Assert.False(preview.HasErrors);
        Assert.Equal(6, preview.Count(RosterRowStatus.New));
    }

    [Fact]
    public async Task Se_rechazan_los_libros_con_macros_el_formato_xls_y_otras_extensiones()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);
        var tournamentId = await NewTournamentAsync(catalog);
        object[][] rows = [["equipo_codigo", "equipo_nombre", "jugador_nombre", "numero_gorro"], ["DEL", "Delfines", "Marco", 1]];

        var xlsm = await catalog.PreviewRosterImportAsync(tournamentId, RosterFiles.Xlsx(storage, rows, extension: ".xlsm"));
        var disguised = await catalog.PreviewRosterImportAsync(tournamentId, RosterFiles.Xlsx(storage, rows, macroContentType: true));
        var xls = await catalog.PreviewRosterImportAsync(tournamentId, RosterFiles.Xlsx(storage, rows, extension: ".xls"));
        var txt = await catalog.PreviewRosterImportAsync(tournamentId, RosterFiles.Xlsx(storage, rows, extension: ".txt"));

        Assert.Null(xlsm.Preview);
        Assert.Contains("macros", xlsm.Message, StringComparison.Ordinal);
        Assert.Null(disguised.Preview);
        Assert.Contains("macros", disguised.Message, StringComparison.Ordinal);
        Assert.Contains("(.xls)", xls.Message, StringComparison.Ordinal);
        Assert.Contains(".csv (UTF-8) o .xlsx", txt.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_XLSX_danado_o_un_CSV_que_no_es_UTF8_se_informan_sin_importar()
    {
        await using var storage = await TempStorage.CreateAsync();
        var (catalog, _) = Create(storage);
        var tournamentId = await NewTournamentAsync(catalog);
        var damaged = RosterFiles.Csv(storage, "esto no es un zip");
        var damagedXlsx = Path.ChangeExtension(damaged, ".xlsx");
        File.Move(damaged, damagedXlsx);
        var latin1 = RosterFiles.CsvWith(storage, Encoding.Latin1, RosterFiles.Header, "DEL,Delfines del Pacífico,Julián Estela,4");

        var xlsx = await catalog.PreviewRosterImportAsync(tournamentId, damagedXlsx);
        var csv = await catalog.PreviewRosterImportAsync(tournamentId, latin1);
        var missing = await catalog.PreviewRosterImportAsync(tournamentId, Path.Combine(storage.Root, "no-existe.csv"));

        Assert.Null(xlsx.Preview);
        Assert.Contains("no es un libro de Excel (.xlsx) válido", xlsx.Message, StringComparison.Ordinal);
        Assert.Null(csv.Preview);
        Assert.Contains("CSV UTF-8", csv.Message, StringComparison.Ordinal);
        Assert.Contains("no existe", missing.Message, StringComparison.Ordinal);
        Assert.Empty(storage.Log.Errors);
    }

    [Fact]
    public void El_CSV_admite_punto_y_coma_comillas_y_saltos_de_linea_dentro_de_un_campo()
    {
        var table = RosterFileReader.ParseCsv(
            "equipo_codigo;equipo_nombre;jugador_nombre;numero_gorro\r\n" +
            "DEL;\"Delfines; del \"\"Pacífico\"\"\";\"Marco\nArrecife\";1\n" +
            "\n" +
            "TIB;Tiburones;Renzo Aleta;2");

        Assert.Equal(4, table.Rows.Count);
        Assert.Equal(["DEL", "Delfines; del \"Pacífico\"", "Marco\nArrecife", "1"], table.Rows[1].Cells);
        Assert.Equal(4, table.Rows[3].Number); // la fila vacía cuenta, como en Excel
        Assert.Equal("Renzo Aleta", table.Rows[3].Cells[2]);
    }

    [Fact]
    public void Unas_comillas_sin_cerrar_se_informan_con_la_fila()
    {
        var ex = Assert.Throws<RosterFileException>(() => RosterFileReader.ParseCsv(
            RosterFiles.Header + "\nDEL,Delfines,\"Marco,1\nTIB,Tiburones,Renzo,2"));

        Assert.StartsWith("Fila 2:", ex.Message, StringComparison.Ordinal);
    }

    // ---------- Apoyo ----------

    internal static (CatalogOperations Catalog, TestDesk Desk) Create(TempStorage storage)
    {
        var desk = new TestDesk(storage);
        return (new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true, new RosterFileReader()), desk);
    }

    internal static async Task<Guid> NewTournamentAsync(CatalogOperations catalog)
    {
        var id = Guid.NewGuid();
        Assert.True((await catalog.CreateTournamentAsync(id, $"Copa {id:N}")).IsSuccess);
        return id;
    }

    private static async Task<Guid> TournamentWithSampleAsync(CatalogOperations catalog)
    {
        var id = await NewTournamentAsync(catalog);
        await ConfirmAsync(catalog, await PreviewAsync(catalog, id, RosterFiles.Sample("plantilla-nomina.csv")));
        return id;
    }

    private static async Task<RosterPreview> PreviewAsync(CatalogOperations catalog, Guid tournamentId, string path)
    {
        var result = await catalog.PreviewRosterImportAsync(tournamentId, path);
        Assert.True(result.Preview is not null, result.Message);
        return result.Preview!;
    }

    private static async Task ConfirmAsync(CatalogOperations catalog, RosterPreview preview)
    {
        var result = await catalog.ConfirmRosterImportAsync(preview, NoChoices);
        Assert.True(result.Outcome == CommandOutcome.Applied, result.Message);
    }

    private static async Task<long> CountAsync(TempStorage storage, string sql)
    {
        await using var connection = await storage.OpenDatabase().OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Contenido de las tablas del catálogo, para comprobar que nada se escribió.</summary>
    private static async Task<string> SnapshotAsync(TempStorage storage)
    {
        var text = new StringBuilder();
        await using var connection = await storage.OpenDatabase().OpenAsync();
        foreach (var sql in new[]
        {
            "SELECT id, code, name FROM team ORDER BY id;",
            "SELECT id, team_id, full_name FROM player ORDER BY id;",
            "SELECT id, tournament_id, team_id, player_id, cap_number FROM tournament_roster_entry ORDER BY id;",
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    text.Append(reader.GetValue(i)).Append('|');
                }

                text.AppendLine();
            }
        }

        return text.ToString();
    }
}
