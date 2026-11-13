using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;
using GeoPoint.Core.Urban;

namespace GeoPoint.Core.Tests;

public class BuildingTests
{
    private static BuildingRecord Rect(double x0, double y0, double w, double h, string faseId, string lotId)
    {
        var pts = new[] { new Vec2(x0, y0), new Vec2(x0 + w, y0), new Vec2(x0 + w, y0 + h), new Vec2(x0, y0 + h) };
        var b = new BuildingRecord { FaseId = faseId, LotId = lotId, Nome = "Edificação 1", Pavimentos = 2 };
        for (int i = 0; i < 4; i++) b.Outer.Add(EdgeDto.FromCurve(new LineCurve(pts[i], pts[(i + 1) % 4])));
        return b;
    }

    [Fact]
    public void Analyze_AreaOccupationAndSetbacks()
    {
        var lot = TestProjects.Rect(0, 0, 10, 30, "01");
        lot.EdgeInfos.Add(new EdgeInfo { MX = 5, MY = 0, Confrontante = "Rua E", Testada = true });
        var p = TestProjects.Project(lot);
        var ctx = TestProjects.Context(p);
        var b = Rect(2, 5, 6, 10, p.Fase.Id, lot.Id);
        p.Buildings.Add(b);
        var r = BuildingAnalyzer.Analyze(ctx.Report(lot), b);
        Assert.Equal(60, r.Area, 6);
        Assert.Equal(120, r.AreaConstruida, 6);
        Assert.Equal(20, r.TaxaOcupacao, 6);
        Assert.Equal(0, r.AreaFora);
        var d = r.Afastamentos.Select(a => Math.Round(a.Distance, 3)).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 2.0, 2.0, 5.0, 15.0 }, d);
        Assert.Contains(r.Afastamentos, a => a.Confrontante == "Rua E" && Math.Abs(a.Distance - 5) < 1e-6);
        var text = BuildingAnalyzer.MemorialText(new[] { r }, new ValueFormatter(ctx.Settings.Format)).Single();
        Assert.Contains("Distâncias até as divisas", text);
        Assert.Contains("2 pavimentos", text);
        Assert.Contains("Rua E: 5,00 m", text);
        // Memorial do lote traz a seção ÁREA EDIFICADA.
        var gen = new MemorialGenerator(ctx, new MemorialPhrases());
        var xml = string.Concat(gen.LotSection(lot, true).Select(x => x.Value));
        Assert.Contains("ÁREA EDIFICADA", xml);
    }

    [Fact]
    public void Building_FollowsLotAcrossPhasesAndSplits()
    {
        var lot = TestProjects.Rect(0, 0, 20, 30, "01");
        var p = TestProjects.Project(lot);
        var b = Rect(12, 5, 6, 10, p.Fase.Id, lot.Id);
        p.Buildings.Add(b);
        // Nova fase: a edificação é copiada para o lote copiado.
        var f1 = FaseService.Create(p, "Desmembramento", "Desmembramento");
        var copy = p.Buildings.Single(x => x.FaseId == f1.Id);
        var lotCopy = p.Lots.Single(l => l.FaseId == f1.Id);
        Assert.Equal(lotCopy.Id, copy.LotId);
        // Lote copiado desmembrado em dois: a edificação passa para o lote que a contém.
        lotCopy.Status = LotStatus.Arquivado;
        var a = TestProjects.Rect(0, 0, 10, 30, "01A"); a.FaseId = f1.Id;
        var c = TestProjects.Rect(10, 0, 10, 30, "01B"); c.FaseId = f1.Id;
        p.Lots.Add(a); p.Lots.Add(c);
        Assert.Equal(c.Id, BuildingAnalyzer.OwnerLot(p, copy)!.Id);
        Assert.Single(BuildingAnalyzer.Of(p, c));
        Assert.Empty(BuildingAnalyzer.Of(p, a));
        // Excluir a fase remove as edificações dela.
        FaseService.Delete(p, f1.Id);
        Assert.Single(p.Buildings);
    }
}

public class PivotSplitTests
{
    [Fact]
    public void SplitFromPoint_FanCutsHitTargetAreas()
    {
        // Lote 20 × 30 (600 m²), três partes de 200 m² com divisas passando pelo vértice (0,0).
        var lot = TestProjects.Rect(0, 0, 20, 30, "01");
        var region = lot.ToRegion();
        var res = AreaSplitter.SplitFromPoint(region, new Vec2(0, 0), new[] { 200.0, 400.0 });
        Assert.Equal(2, res.Cuts.Count);
        Assert.All(res.Cuts, c => Assert.True(c.Start.DistanceTo(new Vec2(0, 0)) < 1e-6));
        var pieces = SubdivisionPlanner.FromCuts(lot, res.Cuts.Select(c => ((Curve2)c, (string?)null)).ToList());
        Assert.Equal(3, pieces.Count);
        Assert.All(pieces, p => Assert.Equal(200, p.Area, 4));
        // Ponto no meio de uma divisa (fundos): duas partes iguais.
        var res2 = AreaSplitter.SplitFromPoint(region, new Vec2(10, 30), new[] { 300.0 });
        var p2 = SubdivisionPlanner.FromCuts(lot, res2.Cuts.Select(c => ((Curve2)c, (string?)null)).ToList());
        Assert.Equal(2, p2.Count);
        Assert.All(p2, p => Assert.Equal(300, p.Area, 4));
    }
}

public class CoordinateTableTests
{
    [Fact]
    public void Parse_Utm_Geographic_Dms()
    {
        var utm = GeoPoint.Core.Export.CoordinateTableReader.Parse("Vértice;E;N\nP-01;235.142,50;9.299.292,99\nP-02;235113,25;9299307,98\n", 23, true);
        Assert.Equal("UTM", utm.Kind);
        Assert.Equal(2, utm.Points.Count);
        Assert.Equal("P-01", utm.Points[0].Name);
        Assert.InRange(utm.Points[0].Lat, -6.40, -6.30);
        Assert.InRange(utm.Points[0].Lon, -47.45, -47.35);
        var dec = GeoPoint.Core.Export.CoordinateTableReader.Parse("M1\t-6,333656\t-47,394191\nM2\t-6,333520\t-47,394450", 23, true);
        Assert.Equal("Geográficas", dec.Kind);
        Assert.Equal(-6.333656, dec.Points[0].Lat, 6);
        Assert.Equal(-47.394191, dec.Points[0].Lon, 6);
        var dms = GeoPoint.Core.Export.CoordinateTableReader.Parse("V-01; 6°20'01,1641\" S; 47°23'39,0858\" O", 23, true);
        Assert.Single(dms.Points);
        Assert.Equal(-(6 + 20 / 60.0 + 1.1641 / 3600), dms.Points[0].Lat, 7);
        Assert.Equal(-(47 + 23 / 60.0 + 39.0858 / 3600), dms.Points[0].Lon, 7);
        // Nome numérico e N antes de E.
        var num = GeoPoint.Core.Export.CoordinateTableReader.Parse("1 9299292.99 235142.50", 23, true);
        Assert.Equal("1", num.Points.Single().Name);
        Assert.Equal(utm.Points[0].Lat, num.Points[0].Lat, 6);
    }
}

public class ConfrontanteOwnerTests
{
    [Fact]
    public void Confrontante_IsOwnerName_AndChangePointsKept()
    {
        // Lote 01 com dois vizinhos do projeto nos fundos: um com proprietário, outro sem.
        var a = TestProjects.Rect(0, 0, 20, 30, "01");
        var b = TestProjects.Rect(0, 30, 10, 20, "02");
        var c = TestProjects.Rect(10, 30, 10, 20, "03");
        b.Proprietario = "Pedro Alves";
        // Ponto onde o confrontante muda nos fundos (como "Dividir divisa" faz).
        a.ExtraVertices.Add(new PointDto { X = 10, Y = 30 });
        GeoPoint.Core.Topology.EdgeSplitter.Apply(a);
        var p = TestProjects.Project(a, b, c);
        var ctx = TestProjects.Context(p);
        var names = ctx.Report(a).Segments.Select(s => s.Confrontante).ToList();
        Assert.Contains("Pedro Alves", names);                    // só o proprietário, sem lote/quadra
        Assert.Contains(names, n => n.StartsWith("Lote 03"));    // sem proprietário: o nome do lote
        // Os fundos têm dois confrontantes na mesma linha: o ponto (10, 30) é vértice, com distância e azimute de cada trecho.
        var back = ctx.Report(a).Segments.Where(s => Math.Abs(s.Curve.Mid.Y - 30) < 1e-6).ToList();
        Assert.Equal(2, back.Count);
        Assert.All(back, s => Assert.Equal(10, s.LengthHorizontal, 6));
        // Conferência avisa do confrontante sem proprietário.
        var spec = new GeoPoint.Core.Sheets.SheetSpec { LotIds = { a.Id } };
        var content = GeoPoint.Core.Sheets.SheetContent.Build(ctx, new GeoPoint.Core.Memorial.ValueFormatter(ctx.Settings.Format), spec, new[] { a }, GeoPoint.Core.Geodesy.CrsDef.Utm());
        Assert.Contains(content.Pendencias, x => x.Contains("sem proprietário"));
        // Estilo "lote e quadra" volta ao nome do lote.
        var ctx2 = TestProjects.Context(p, st => st.Naming.ConfrontanteNome = GeoPoint.Core.Settings.ConfrontanteNome.Lote);
        Assert.Contains(ctx2.Report(a).Segments, s => s.Confrontante.StartsWith("Lote 02"));
    }
}
