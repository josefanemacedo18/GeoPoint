using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Export;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Office;
using GeoPoint.Core.Settings;
using GeoPoint.Core.Urban;

namespace GeoPoint.Core.Tests;

public class ReportAndDocumentTests
{
    [Fact]
    public void Report_SquareClockwiseFromNorthWest()
    {
        var lot = TestProjects.Rect(0, 0, 10, 10, "01");
        var ctx = TestProjects.Context(TestProjects.Project(lot));
        var r = ctx.Report(lot);
        Assert.Equal(4, r.Vertices.Count);
        Assert.Equal(new Vec2(0, 10), r.Vertices[0].Internal);
        Assert.Equal("P-01", r.Vertices[0].Name);
        Assert.Equal(new[] { 90.0, 180, 270, 0 }, r.Segments.Select(s => Math.Round(s.AzimuthTrue, 9)).ToArray());
        Assert.Equal(100, r.AreaLocal, 9);
        Assert.Equal(40, r.PerimeterLocal, 9);
        Assert.NotNull(r.AreaUtm);
        // Distância UTM = horizontal × k × R/(R+h)
        var k = r.Vertices[0].Utm!.Value.Scale;
        double R = Ellipsoid.GRS80.GaussianRadius(-6.3375 * Math.PI / 180);
        Assert.Equal(10 * k * R / (R + 150), r.Segments[0].LengthUtm!.Value, 4);
        Assert.Equal(100 * Math.Pow(k * R / (R + 150), 2), r.AreaUtm!.Value, 2);
    }

    [Fact]
    public void Report_CounterClockwiseOption_AndStartVertex()
    {
        var lot = TestProjects.Rect(0, 0, 10, 10, "01");
        lot.Clockwise = false;
        lot.StartX = 10; lot.StartY = 0;
        var ctx = TestProjects.Context(TestProjects.Project(lot));
        var r = ctx.Report(lot);
        Assert.Equal(new Vec2(10, 0), r.Vertices[0].Internal);
        Assert.Equal(0, r.Segments[0].AzimuthTrue, 9);
    }

    [Fact]
    public void Report_RotationFromProjectTrueNorth()
    {
        var lot = TestProjects.Rect(0, 0, 10, 10, "01");
        var p = TestProjects.Project(lot);
        p.Frame.AngleRad = 30 * Math.PI / 180; // eixo X interno a 30° anti-horário do leste
        var ctx = TestProjects.Context(p);
        Assert.Equal(60, ctx.Coords.TrueAzimuth(new Vec2(0, 0), new Vec2(1, 0)), 9);
        Assert.Equal(330, ctx.Coords.TrueAzimuth(new Vec2(0, 0), new Vec2(0, 1)), 9);
    }

    [Fact]
    public void Report_UtmMatchesDirectConversion()
    {
        var lot = TestProjects.Rect(0, 0, 10, 10, "01");
        var ctx = TestProjects.Context(TestProjects.Project(lot));
        var r = ctx.Report(lot);
        var anchor = r.Vertices.First(v => v.Internal == Vec2.Zero);
        var tm = new UtmZone(23, true).Projection(Ellipsoid.GRS80);
        var direct = tm.Forward(-6.3375, -47.39);
        Assert.Equal(direct.E, anchor.Utm!.Value.East, 6);
        Assert.Equal(direct.N, anchor.Utm!.Value.North, 6);
    }

    [Fact]
    public void Neighbors_AutoConfrontante_AndGlobalNames()
    {
        var a = TestProjects.Rect(0, 0, 10, 30, "01");
        var b = TestProjects.Rect(10, 0, 10, 30, "02");
        var ctx = TestProjects.Context(TestProjects.Project(a, b), s => s.Naming.Numbering = VertexNumbering.Projeto);
        var ra = ctx.Report(a);
        var shared = ra.Segments.Single(s => s.ConfrontanteAutomatico);
        Assert.Contains("02", shared.Confrontante);
        var rb = ctx.Report(b);
        var namesA = ra.Vertices.Where(v => v.Internal.X == 10).Select(v => v.Name).OrderBy(n => n);
        var namesB = rb.Vertices.Where(v => v.Internal.X == 10).Select(v => v.Name).OrderBy(n => n);
        Assert.Equal(namesA, namesB);
        Assert.Equal(6, ra.Vertices.Concat(rb.Vertices).Select(v => v.Name).Distinct().Count());
    }

    [Fact]
    public void Neighbors_IgnoreOverlappingAndRespectStatus()
    {
        var parent = TestProjects.Rect(0, 0, 20, 30, "Mae");
        parent.Status = LotStatus.Arquivado;
        var a = TestProjects.Rect(0, 0, 10, 30, "01");
        var b = TestProjects.Rect(10, 0, 10, 30, "02");
        var oldA = TestProjects.Rect(30, 0, 10, 30, "A");
        var oldB = TestProjects.Rect(40, 0, 10, 30, "B");
        oldA.Status = oldB.Status = LotStatus.Arquivado;
        var ctx = TestProjects.Context(TestProjects.Project(parent, a, b, oldA, oldB));
        Assert.DoesNotContain(ctx.Report(parent).Segments, s => s.ConfrontanteAutomatico);
        Assert.Contains(ctx.Report(oldA).Segments, s => s.ConfrontanteAutomatico && s.Confrontante.Contains("B"));
        Assert.Contains(ctx.Report(a).Segments, s => s.ConfrontanteAutomatico && s.Confrontante.Contains("02"));
    }

    [Fact]
    public void Deductions_AppInsideLot()
    {
        var lot = TestProjects.Rect(0, 0, 20, 50, "01");
        var app = TestProjects.Rect(0, 40, 20, 10, "APP", uso: LotUse.App);
        var ctx = TestProjects.Context(TestProjects.Project(lot, app));
        var r = ctx.Report(lot);
        Assert.Equal(200, r.AreaDeducoes, 2);
        Assert.Equal(800, r.AreaLiquida, 2);
        var q = QuadroAreas.Build(ctx);
        Assert.Equal(1000, q.AreaTotal, 6); // APP dentro do lote não é somada de novo
    }

    [Fact]
    public void Quadro_RulesProfile()
    {
        var a = TestProjects.Rect(0, 0, 4, 25, "01");   // 100 m², testada 4
        a.EdgeInfos.Add(new EdgeInfo { MX = 2, MY = 0, Testada = true });
        var b = TestProjects.Rect(4, 0, 10, 25, "02");  // 250 m², testada 10
        b.EdgeInfos.Add(new EdgeInfo { MX = 9, MY = 0, Testada = true });
        var ctx = TestProjects.Context(TestProjects.Project(a, b));
        var q = QuadroAreas.Build(ctx);
        var la = q.Lotes.Single(l => l.Lot == a);
        Assert.Equal(2, la.Problemas.Count);
        Assert.Empty(q.Lotes.Single(l => l.Lot == b).Problemas);
        // Testada define o vértice inicial: no sentido horário a frente (y = 0) é percorrida de (14,0) para (4,0).
        var r = ctx.Report(b);
        Assert.Equal(new Vec2(14, 0), r.Vertices[0].Internal);
        Assert.True(r.Segments[0].Testada);
    }

    [Fact]
    public void Memorial_GeneratesValidDocx()
    {
        var a = TestProjects.Rect(0, 0, 10, 30, "01");
        a.EdgeInfos.Add(new EdgeInfo { MX = 5, MY = 0, Confrontante = "Rua E", Testada = true });
        var b = TestProjects.Rect(10, 0, 10, 30, "02");
        var ctx = TestProjects.Context(TestProjects.Project(a, b));
        var gen = new MemorialGenerator(ctx, new MemorialPhrases());
        var bytes = gen.Generate(new MemorialRequest { Kind = MemorialKind.Lotes, Lots = new() { a }, Date = new DateTime(2026, 7, 7) });
        var text = DocText(bytes, out var xml);
        Assert.Contains("MEMORIAL DESCRITIVO", text);
        Assert.Contains("Inicia-se a descrição deste perímetro no vértice P-01", text);
        Assert.Contains("confrontando com Rua E", text);
        Assert.Contains("Lote 02", text.Replace("02 – Quadra A", "Lote 02"));
        Assert.Contains("azimute de 90°00'00\"", text);
        Assert.Contains("Porto Franco – MA, 07 de julho de 2026", text);
        Assert.Contains("fuso 23, hemisfério Sul", text);
        Assert.DoesNotContain("{{", text);
        Assert.DoesNotContain("MATRÍCULA:", text); // campo vazio removido
        Assert.Contains("PROPRIETÁRIO(S):", text);
        Assert.NotNull(xml);
    }

    [Fact]
    public void Memorial_OperationsAndLoteamento()
    {
        var parent = TestProjects.Rect(0, 0, 20, 30, "Mae");
        parent.Status = LotStatus.Arquivado;
        var a = TestProjects.Rect(0, 0, 10, 30, "01");
        var b = TestProjects.Rect(10, 0, 10, 30, "02");
        var p = TestProjects.Project(parent, a, b);
        var op = new OperationRecord { Type = OperationType.Desmembramento, ParentIds = { parent.Id }, ChildIds = { a.Id, b.Id } };
        p.Operations.Add(op);
        var ctx = TestProjects.Context(p, s => { s.Format.AngleStyle = AngleStyle.Rumo; s.Memorial.AreaType = AreaType.Both; });
        var gen = new MemorialGenerator(ctx, new MemorialPhrases());
        var t1 = DocText(gen.Generate(new MemorialRequest { Kind = MemorialKind.Desmembramento, Lots = new() { parent, a, b }, Operation = op }), out _);
        Assert.Contains("DESMEMBRAMENTO", t1);
        Assert.Contains("SITUAÇÃO PROPOSTA", t1);
        Assert.Contains("(confere)", t1);
        Assert.Contains("rumo de", t1);

        var merged = TestProjects.Rect(0, 0, 20, 30, "Unif");
        var op2 = new OperationRecord { Type = OperationType.Remembramento, ParentIds = { a.Id, b.Id }, ChildIds = { merged.Id } };
        p.Lots.Add(merged); p.Operations.Add(op2);
        ctx.Invalidate();
        var t2 = DocText(gen.Generate(new MemorialRequest { Kind = MemorialKind.Remembramento, Lots = new() { merged, a, b }, Operation = op2 }), out _);
        Assert.Contains("REMEMBRAMENTO", t2);
        Assert.Contains("ÁREA REMEMBRADA", t2);

        var t3 = DocText(gen.Generate(new MemorialRequest { Kind = MemorialKind.Loteamento, Lots = p.ActiveLots.ToList() }), out _);
        Assert.Contains("QUADRO DE ÁREAS", t3);
    }

    [Fact]
    public void Memorial_ArcAndGeographicCoordinates()
    {
        var arc = CurveFactory.FromThreePoints(new Vec2(20, 10), new Vec2(10, 12), new Vec2(0, 10));
        var lot = TestProjects.FromCurves(new Curve2[] { new LineCurve(new(0, 0), new(20, 0)), new LineCurve(new(20, 0), new(20, 10)), arc, new LineCurve(new(0, 10), new(0, 0)) }, "01");
        var ctx = TestProjects.Context(TestProjects.Project(lot), s => s.Memorial.CoordinateSystems = new() { CrsDef.Geographic(), CrsDef.Utm(), CrsDef.Ptl() });
        var gen = new MemorialGenerator(ctx, new MemorialPhrases());
        var text = DocText(gen.Generate(new MemorialRequest { Kind = MemorialKind.Lotes, Lots = new() { lot } }), out _);
        Assert.Contains("curva circular", text);
        Assert.Contains("latitude 6°", text);
        Assert.Contains(" S e longitude 47°", text);
        Assert.Contains("Plano Topográfico Local", text);
    }

    [Fact]
    public void Template_PlaceholderSplitAcrossRuns()
    {
        var W = Wml.W;
        var p = new XElement(W + "p",
            new XElement(W + "r", new XElement(W + "rPr", new XElement(W + "b")), new XElement(W + "t", "Nome: {{PRO")),
            new XElement(W + "r", new XElement(W + "t", "PRIETARIO}} fim")));
        var opt = new XElement(W + "p", new XElement(W + "r", new XElement(W + "t", "Matrícula {{MATRICULA?}}")));
        var tpl = DocxPackage.Create(new[] { p, opt, Wml.Paragraph("{{CORPO}}") });
        var outBytes = DocxTemplate.Fill(tpl, new Dictionary<string, string> { ["PROPRIETARIO"] = "Maria & José <Ltda>" },
            new Dictionary<string, IReadOnlyList<XElement>> { ["CORPO"] = new[] { Wml.Paragraph("Corpo gerado") } });
        var text = DocText(outBytes, out _);
        Assert.Contains("Nome: Maria & José <Ltda> fim", text);
        Assert.DoesNotContain("Matrícula", text);
        Assert.Contains("Corpo gerado", text);
        Assert.Equal(new[] { "CORPO", "MATRICULA", "PROPRIETARIO" }, DocxTemplate.ListPlaceholders(tpl));
    }

    [Fact]
    public void Exports_KmlKmzGeoJsonXlsx()
    {
        var a = TestProjects.Rect(0, 0, 10, 30, "01");
        var app = TestProjects.Rect(0, 25, 10, 5, "APP", uso: LotUse.App);
        var ctx = TestProjects.Context(TestProjects.Project(a, app), s => { s.Kml.IncludeEdges = true; });
        var lots = ctx.Project.ActiveLots.ToList();
        var kml = GisExport.BuildKml(ctx, lots, "Teste");
        var x = XDocument.Parse(kml);
        XNamespace k = "http://www.opengis.net/kml/2.2";
        Assert.Equal(2, x.Descendants(k + "Polygon").Count());
        var coords = x.Descendants(k + "Polygon").First().Descendants(k + "coordinates").First().Value.Split(' ');
        Assert.Equal(5, coords.Length);
        Assert.StartsWith("-47.39", coords.First(c => c.StartsWith("-47.39,")));

        var kmz = GisExport.BuildKmz(kml);
        using (var z = new ZipArchive(new MemoryStream(kmz))) Assert.NotNull(z.GetEntry("doc.kml"));

        var gj = JsonDocument.Parse(GisExport.BuildGeoJson(ctx, lots, true));
        var feats = gj.RootElement.GetProperty("features");
        // 2 polígonos + 6 vértices únicos (a APP compartilha 2 vértices com o lote: mesmo ponto no registro).
        Assert.Equal(2 + 6, feats.GetArrayLength());
        var ring = feats[0].GetProperty("geometry").GetProperty("coordinates")[0];
        Assert.Equal(5, ring.GetArrayLength());

        var xlsx = ExcelReport.Build(ctx, lots);
        using var zx = new ZipArchive(new MemoryStream(xlsx));
        Assert.NotNull(zx.GetEntry("xl/worksheets/sheet4.xml"));
        foreach (var e in zx.Entries.Where(e => e.FullName.EndsWith(".xml")))
        {
            using var s = e.Open();
            XDocument.Load(s); // XML bem formado
        }
    }

    [Fact]
    public void Validator_FindsOverlapAndMissingConfrontantes()
    {
        var a = TestProjects.Rect(0, 0, 10, 10, "01");
        var b = TestProjects.Rect(5, 0, 10, 10, "02");
        var ctx = TestProjects.Context(TestProjects.Project(a, b));
        var issues = ProjectValidator.Validate(ctx);
        Assert.Contains(issues, i => i.Category == "Sobreposição");
        Assert.Contains(issues, i => i.Category == "Confrontantes");
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        var s = new GeoPointSettings();
        s.Memorial.CoordinateSystems.Add(CrsDef.Rtm(-45));
        s.Technician.Nome = "Teste";
        var path = Path.Combine(Path.GetTempPath(), "gp-test-" + Guid.NewGuid().ToString("N") + ".json");
        SettingsStore.Save(s, path);
        var l = SettingsStore.Load(path);
        Assert.Equal("Teste", l.Technician.Nome);
        Assert.Equal(3, l.Memorial.CoordinateSystems.Count);
        Assert.Equal(CrsKind.CustomTm, l.Memorial.CoordinateSystems[2].Kind);
        File.WriteAllText(path, "{ corrompido");
        var d = SettingsStore.Load(path);
        Assert.NotNull(d);
        File.Delete(path);
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + ".corrompido-*")) File.Delete(f);
    }

    [Fact]
    public void ProjectJson_RoundTrip()
    {
        var a = TestProjects.Rect(0, 0, 10, 10, "01");
        var p = TestProjects.Project(a);
        var json = JsonSerializer.Serialize(p, JsonOptions.Compact);
        var back = JsonSerializer.Deserialize<ParcelProject>(json, JsonOptions.Default)!;
        Assert.Single(back.Lots);
        Assert.Equal(100, back.Lots[0].ToRegion().Area, 9);
        Assert.True(back.Reference.IsSet);
    }

    internal static string DocText(byte[] docx, out XDocument xml)
    {
        using var z = new ZipArchive(new MemoryStream(docx));
        using var s = z.GetEntry("word/document.xml")!.Open();
        xml = XDocument.Load(s);
        var W = Wml.W;
        return string.Join("\n", xml.Descendants(W + "p").Select(p => string.Concat(p.Descendants(W + "t").Select(t => t.Value))));
    }
}
