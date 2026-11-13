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
