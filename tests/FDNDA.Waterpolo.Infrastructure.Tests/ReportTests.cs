using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FDNDA.Waterpolo.Application.Catalog;
using FDNDA.Waterpolo.Application.Matches;
using FDNDA.Waterpolo.Application.Reports;
using FDNDA.Waterpolo.Domain.Common;
using FDNDA.Waterpolo.Domain.Matches;
using FDNDA.Waterpolo.Infrastructure.Export;
using FDNDA.Waterpolo.Infrastructure.Persistence;
using FDNDA.Waterpolo.Infrastructure.Tests.Support;

namespace FDNDA.Waterpolo.Infrastructure.Tests;

/// <summary>R6 — resúmenes por partido y evento y exportación CSV/PDF (RF09).</summary>
public class ReportTests
{
    [Fact]
    public async Task Los_totales_del_resumen_coinciden_con_el_marcador_tras_corregir_y_anular()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        var home = started.Home.Roster;
        var away = started.Away.Roster;
        await desk.GoalAsync(TeamSide.Home, home[0].RosterEntryId);
        await desk.GoalAsync(TeamSide.Home, home[1].RosterEntryId);
        var toCorrect = (await desk.GoalAsync(TeamSide.Home, home[1].RosterEntryId)).Snapshot!.Incidents[^1].IncidentId;
        var toVoid = (await desk.GoalAsync(TeamSide.Away, away[0].RosterEntryId)).Snapshot!.Incidents[^1].IncidentId;
        await Register(desk, IncidentType.Penalty, TeamSide.Away, away[1].RosterEntryId, penalty: PenaltyResult.Converted);
        await Register(desk, IncidentType.Penalty, TeamSide.Home, home[2].RosterEntryId, penalty: PenaltyResult.NotConverted);
        await Register(desk, IncidentType.Exclusion, TeamSide.Home, home[3].RosterEntryId, exclusion: ExclusionKind.Exclusion);
        await Register(desk, IncidentType.Exclusion, TeamSide.Away, away[2].RosterEntryId, exclusion: ExclusionKind.Expulsion);
        await Register(desk, IncidentType.PersonalFoul, TeamSide.Away, away[3].RosterEntryId);
        await Register(desk, IncidentType.Timeout, TeamSide.Home, null);
        Assert.True((await desk.Operations.CorrectIncidentAsync(new CorrectIncident(
            Guid.NewGuid(), TestDesk.MatchId, toCorrect, TeamSide.Home, home[4].RosterEntryId, "Goleador corregido", desk.Operator))).IsSuccess);
        Assert.True((await desk.Operations.VoidIncidentAsync(new VoidIncident(
            Guid.NewGuid(), TestDesk.MatchId, toVoid, "Gol anulado por el árbitro", desk.Operator))).IsSuccess);

        var snapshot = (await desk.Queries.GetSnapshotAsync(TestDesk.MatchId))!;
        var report = (await Reports(storage, desk).GetMatchReportAsync(TestDesk.MatchId))!;

        Assert.Equal((3, 1), (snapshot.Home.Score, snapshot.Away.Score));
        Assert.Equal((snapshot.Home.Score, snapshot.Away.Score), (report.Home.Totals.Goals, report.Away.Totals.Goals));
        Assert.Equal(
            snapshot.Scorers.Select(s => (s.Cap, s.PlayerName, s.Goals)),
            report.Scorers.Select(s => (s.Cap, s.PlayerName, s.Goals)));
        Assert.Equal((1, 0, 0, 0, 1, 1), (report.Home.Totals.Exclusions, report.Home.Totals.Expulsions, report.Home.Totals.PersonalFouls,
            report.Home.Totals.PenaltiesConverted, report.Home.Totals.PenaltiesMissed, report.Home.Totals.Timeouts));
        Assert.Equal((0, 1, 1, 1, 0, 0), (report.Away.Totals.Exclusions, report.Away.Totals.Expulsions, report.Away.Totals.PersonalFouls,
            report.Away.Totals.PenaltiesConverted, report.Away.Totals.PenaltiesMissed, report.Away.Totals.Timeouts));
        Assert.Equal(10, report.Incidents.Count); // el historial completo, con la anulada marcada
        Assert.True(report.Incidents.Single(i => i.Sequence == 4).IsVoided);
        Assert.True(report.Incidents.Single(i => i.Sequence == 3).IsCorrected);
        Assert.Equal(home[4].PlayerName, report.Incidents.Single(i => i.Sequence == 3).PlayerName);
    }

    [Fact]
    public async Task El_resumen_del_evento_suma_solo_partidos_iniciados_y_agrupa_goleadores_por_jugador()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var first = await desk.StartAsync();
        var scorer = first.Home.Roster[0]; // Delfines es local en el partido 1 y visita en el 2
        await desk.GoalAsync(TeamSide.Home, scorer.RosterEntryId);
        await desk.GoalAsync(TeamSide.Away, first.Away.Roster[0].RosterEntryId);
        await desk.Operations.FinishMatchAsync(new FinishMatch(Guid.NewGuid(), TestDesk.MatchId, desk.Operator));
        var second = (await desk.Operations.StartMatchAsync(new StartMatch(Guid.NewGuid(), DemoDataSeeder.SecondMatchId, desk.Operator))).Snapshot!;
        var sameScorer = second.Away.Roster.Single(r => r.PlayerName == scorer.PlayerName);
        await desk.Operations.RegisterGoalAsync(new RegisterGoal(Guid.NewGuid(), DemoDataSeeder.SecondMatchId, TeamSide.Away, sameScorer.RosterEntryId, desk.Operator));
        await desk.Operations.RegisterGoalAsync(new RegisterGoal(Guid.NewGuid(), DemoDataSeeder.SecondMatchId, TeamSide.Away, sameScorer.RosterEntryId, desk.Operator));
        var scheduled = await desk.Operations.ScheduleMatchAsync(new ScheduleMatch(
            Guid.NewGuid(), DemoDataSeeder.TournamentId, "Partido sin jugar", null, DemoDataSeeder.HomeTeamId, DemoDataSeeder.AwayTeamId));
        Assert.True(scheduled.IsSuccess);
        var catalog = new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true);
        var otherEvent = Guid.NewGuid();
        await catalog.CreateTournamentAsync(otherEvent, "Otro evento");
        await desk.Operations.ScheduleMatchAsync(new ScheduleMatch(
            Guid.NewGuid(), otherEvent, "De otro evento", null, DemoDataSeeder.HomeTeamId, DemoDataSeeder.AwayTeamId));

        var report = (await Reports(storage, desk).GetTournamentReportAsync(DemoDataSeeder.TournamentId))!;

        Assert.Equal(3, report.Matches.Count);
        Assert.DoesNotContain(report.Matches, m => m.Label == "De otro evento");
        var top = report.Scorers[0];
        Assert.Equal((scorer.PlayerName, 3), (top.PlayerName, top.Goals));
        Assert.Equal(2, report.Scorers.Count);
        var dolphins = report.Teams.Single(t => t.TeamId == DemoDataSeeder.HomeTeamId);
        var sharks = report.Teams.Single(t => t.TeamId == DemoDataSeeder.AwayTeamId);
        Assert.Equal((2, 3, 1), (dolphins.Played, dolphins.GoalsFor, dolphins.GoalsAgainst));
        Assert.Equal((2, 1, 3), (sharks.Played, sharks.GoalsFor, sharks.GoalsAgainst));

        var document = ReportDocuments.ForTournament(report, storage.Time.GetUtcNow(), TimeZoneInfo.Utc);
        Assert.Contains("3 partidos: 1 finalizado, 1 en juego y 1 programado.", document.Lines);
        Assert.Contains(document.Lines, l => l.StartsWith("DATOS DE DEMOSTRACIÓN", StringComparison.Ordinal));
        Assert.Equal("vs", document.Sections[0].Rows.Single(r => r[1] == "Partido sin jugar")[3]);
        Assert.Contains("No es una tabla de posiciones", document.Sections[2].Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=SUMA(A1:A2)", "'=SUMA(A1:A2)")]
    [InlineData("+51 999", "'+51 999")]
    [InlineData("-1", "'-1")]
    [InlineData("@usuario", "'@usuario")]
    [InlineData("Ana, la goleadora", "\"Ana, la goleadora\"")]
    [InlineData("El \"Tiburón\"", "\"El \"\"Tiburón\"\"\"")]
    [InlineData("=HYPERLINK(\"x\",\"y\")", "\"'=HYPERLINK(\"\"x\"\",\"\"y\"\")\"")]
    [InlineData("Normal", "Normal")]
    public void Un_campo_CSV_neutraliza_formulas_y_respeta_comillas(string value, string expected)
    {
        Assert.Equal(expected, CsvReportWriter.Field(value));
    }

    [Fact]
    public async Task El_CSV_exportado_es_UTF8_con_BOM_y_ningun_texto_del_catalogo_queda_como_formula()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var catalog = new CatalogOperations(desk.Catalog, storage.Time, storage.Log, demoData: true);
        Assert.True((await catalog.UpdateTeamAsync(DemoDataSeeder.HomeTeamId, "DEL", "=HYPERLINK(\"http://x\")", "#0B63CE", "Blanco")).IsSuccess);
        var matchId = Guid.NewGuid();
        await desk.Operations.ScheduleMatchAsync(new ScheduleMatch(
            matchId, DemoDataSeeder.TournamentId, "@Fecha peligrosa", null, DemoDataSeeder.HomeTeamId, DemoDataSeeder.AwayTeamId));
        var operations = Reports(storage, desk);
        var path = Path.Combine(storage.Root, "exportes", "partido.csv");

        var result = await operations.ExportAsync((await operations.MatchDocumentAsync(matchId))!, ReportFormat.Csv, path);

        Assert.True(result.IsSuccess, result.Message);
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.Contains("\r\n", text, StringComparison.Ordinal);
        var cells = text.Split("\r\n").SelectMany(SplitCsv).ToList();
        Assert.Contains(cells, c => c.StartsWith("'=HYPERLINK", StringComparison.Ordinal));
        Assert.Contains(cells, c => c.StartsWith("'@Fecha peligrosa", StringComparison.Ordinal) || c.Contains("@Fecha peligrosa", StringComparison.Ordinal));
        Assert.DoesNotContain(cells, c => c.Length > 0 && "=+-@".Contains(c[0], StringComparison.Ordinal));
        Assert.Contains("Resumen por equipo", text, StringComparison.Ordinal);
        Assert.Contains("Torneo Demostración FDNDA", text, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public async Task El_PDF_exportado_tiene_estructura_valida_y_el_texto_del_resumen()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var started = await desk.StartAsync();
        await desk.GoalAsync(TeamSide.Home, started.Home.Roster[0].RosterEntryId);
        var operations = Reports(storage, desk);
        var path = Path.Combine(storage.Root, "partido.pdf");

        var result = await operations.ExportAsync((await operations.MatchDocumentAsync(TestDesk.MatchId))!, ReportFormat.Pdf, path);

        Assert.True(result.IsSuccess, result.Message);
        var pdf = await File.ReadAllBytesAsync(path);
        var pages = AssertValidPdf(pdf);
        Assert.Equal(1, pages);
        var text = Encoding.Latin1.GetString(pdf);
        Assert.Contains("(Goleadores) Tj", text, StringComparison.Ordinal);
        Assert.Contains("Pac\\355fico", text, StringComparison.Ordinal); // «í» en WinAnsi
        Assert.Contains("P\\341gina 1 de 1", text, StringComparison.Ordinal);
        Assert.Contains("/BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_resumen_largo_ocupa_varias_paginas_y_corta_lo_que_no_cabe()
    {
        var rows = Enumerable.Range(1, 150)
            .Select(i => (IReadOnlyList<string>)[i.ToString(CultureInfo.InvariantCulture), new string('M', 80), "(paréntesis) y \\barra"])
            .ToList();
        var document = new ReportDocument(
            "Prueba de páginas",
            ["Línea de contexto"],
            [new ReportSection("Tabla larga", [new("N.º", 10, ColumnAlign.Right), new("Nombre", 50), new("Texto", 40)], rows, "Vacía")],
            "Pie de prueba",
            "prueba");
        using var stream = new MemoryStream();

        PdfReportWriter.Write(document, stream, new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero));

        var pdf = stream.ToArray();
        var pages = AssertValidPdf(pdf);
        Assert.True(pages >= 3, $"páginas: {pages}");
        var text = Encoding.Latin1.GetString(pdf);
        Assert.Contains("(Tabla larga \\(continuaci\\363n\\)) Tj", text, StringComparison.Ordinal);
        Assert.Contains("\\205) Tj", text, StringComparison.Ordinal); // «…» al cortar
        Assert.Contains("\\(par\\351ntesis\\) y \\\\barra", text, StringComparison.Ordinal);
        Assert.Contains($"de {pages}) Tj", text, StringComparison.Ordinal);
        Assert.Contains("/CreationDate (D:20261123120000Z)", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Si_el_destino_esta_bloqueado_no_se_reporta_exito_y_el_archivo_anterior_queda()
    {
        await using var storage = await TempStorage.CreateAsync();
        var desk = await new TestDesk(storage).SignInAsync(storage);
        var operations = Reports(storage, desk);
        var path = Path.Combine(storage.Root, "abierto-en-excel.csv");
        await File.WriteAllTextAsync(path, "contenido anterior");

        ExportResult result;
        await using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = await operations.ExportAsync((await operations.TournamentDocumentAsync(DemoDataSeeder.TournamentId))!, ReportFormat.Csv, path);
        }

        Assert.False(result.IsSuccess);
        Assert.Contains("abierto en otro programa", result.Message, StringComparison.Ordinal);
        Assert.Equal("contenido anterior", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(storage.Root, "*.tmp"));
    }

    [Theory]
    [InlineData("Copa Perú: Fecha 1/2", "copa-peru-fecha-1-2")]
    [InlineData("  ¡Niñas «Sub-17»!  ", "ninas-sub-17")]
    [InlineData("***", "sin-nombre")]
    public void El_nombre_sugerido_no_lleva_acentos_ni_caracteres_reservados(string text, string expected)
    {
        Assert.Equal(expected, ReportDocuments.Slug(text));
    }

    // ---------- Apoyo ----------

    private static ReportOperations Reports(TempStorage storage, TestDesk desk) =>
        new(desk.Repository, desk.Catalog, new ReportFileWriter(storage.Time), storage.Time, storage.Log);

    private static Task<CommandResult> Register(
        TestDesk desk, IncidentType type, TeamSide side, Guid? player, PenaltyResult? penalty = null, ExclusionKind? exclusion = null) =>
        desk.Operations.RegisterIncidentAsync(new RegisterIncident(
            Guid.NewGuid(), TestDesk.MatchId, type, side, player, desk.Operator, PenaltyResult: penalty, ExclusionKind: exclusion));

    /// <summary>Comprueba cabecera, fin, tabla xref (cada desplazamiento apunta a su objeto) y devuelve las páginas.</summary>
    private static int AssertValidPdf(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-1.4\n", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
        var startxref = int.Parse(Regex.Match(text, @"startxref\n(\d+)\n%%EOF\n$").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.StartsWith("xref\n0 ", text[startxref..], StringComparison.Ordinal);
        var count = int.Parse(Regex.Match(text[startxref..], @"^xref\n0 (\d+)\n").Groups[1].Value, CultureInfo.InvariantCulture);
        var entries = Regex.Matches(text[startxref..], @"(\d{10}) 00000 n \n");
        Assert.Equal(count - 1, entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var offset = int.Parse(entries[i].Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.StartsWith($"{i + 1} 0 obj\n", text[offset..], StringComparison.Ordinal);
        }

        foreach (System.Text.RegularExpressions.Match stream in Regex.Matches(text, @"/Length (\d+) >>\nstream\n"))
        {
            var length = int.Parse(stream.Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.StartsWith("endstream", text[(stream.Index + stream.Length + length)..], StringComparison.Ordinal);
        }

        var pages = Regex.Matches(text, @"/Type /Page /").Count;
        Assert.Contains($"/Count {pages} >>", text, StringComparison.Ordinal);
        return pages;
    }

    private static IEnumerable<string> SplitCsv(string line)
    {
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                yield return cell.ToString();
                cell.Clear();
            }
            else
            {
                cell.Append(c);
            }
        }

        yield return cell.ToString();
    }
}
