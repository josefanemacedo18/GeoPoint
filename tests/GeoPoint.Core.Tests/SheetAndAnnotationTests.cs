using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Sheets;

namespace GeoPoint.Core.Tests;

public class SheetAndAnnotationTests
{
    private static AnnotationPlanOptions Opts() => new() { TextM = 1.0, OffsetM = 0.75, DotRadiusM = 0.3 };

    [Fact]
    public void Annotation_EdgeTextsOutsideLot()
    {
        var lot = TestProjects.Rect(0, 0, 10, 30, "01");
        var ctx = TestProjects.Context(TestProjects.Project(lot));
        var rep = ctx.Report(lot);
        var plan = AnnotationPlanner.Plan(new[] { rep }, new ValueFormatter(ctx.Settings.Format), Opts());
        var edgeTexts = plan.Texts.Where(t => t.Kind is AnnoTextKind.Distance or AnnoTextKind.Azimuth).ToList();
        Assert.Equal(8, edgeTexts.Count);
        Assert.All(edgeTexts, t => Assert.False(rep.Region.Contains(t.At)));
        // A distância fica mais perto da linha que o azimute.
        var d = edgeTexts.First(t => t.Kind == AnnoTextKind.Distance);
        var a = edgeTexts.First(t => t.Kind == AnnoTextKind.Azimuth && t.Key.StartsWith(d.Key));
        Assert.True(rep.Region.Outer.Edges.Min(e => e.DistanceTo(d.At)) < rep.Region.Outer.Edges.Min(e => e.DistanceTo(a.At)));
        Assert.Equal(4, plan.Dots.Count);
        Assert.All(plan.Texts.Where(t => t.Kind == AnnoTextKind.Vertex), t => Assert.False(rep.Region.Contains(t.At)));
    }

    [Fact]
    public void Annotation_SharedEdgesAndVerticesOnce()
    {
        var a = TestProjects.Rect(0, 0, 10, 30, "01");
        var b = TestProjects.Rect(10, 0, 10, 30, "02");
        var ctx = TestProjects.Context(TestProjects.Project(a, b));
        var reps = new[] { ctx.Report(a), ctx.Report(b) };
        var plan = AnnotationPlanner.Plan(reps, new ValueFormatter(ctx.Settings.Format), Opts());
        // 7 divisas distintas (a comum uma vez só) e 6 vértices distintos.
        Assert.Equal(7, plan.Texts.Count(t => t.Kind == AnnoTextKind.Distance));
        Assert.Equal(6, plan.Dots.Count);
        Assert.Equal(6, plan.Texts.Count(t => t.Kind == AnnoTextKind.Vertex));
        // Nomes dos vértices fora dos dois lotes.
        Assert.All(plan.Texts.Where(t => t.Kind == AnnoTextKind.Vertex), t => Assert.DoesNotContain(reps, r => r.Region.Contains(t.At)));
        // Anotando de novo só o lote 02, o que o 01 já anotou não se repete.
        var existing = plan.KeysOf(a.Id).ToHashSet();
        var plan2 = AnnotationPlanner.Plan(new[] { ctx.Report(b) }, new ValueFormatter(ctx.Settings.Format), Opts(), existing);
        Assert.Equal(3, plan2.Texts.Count(t => t.Kind == AnnoTextKind.Distance));
        Assert.Equal(2, plan2.Dots.Count);
    }

    [Fact]
    public void Annotation_ConfrontanteExtentsAlongOneSide()
    {
        // Lado de fundos (y = 30) dividido em 3 confrontantes: 4 + 3 + 3 m.
        var lot = TestProjects.Rect(0, 0, 10, 30, "01");
        lot.ExtraVertices.Add(new PointDto { X = 6, Y = 30 });
        lot.ExtraVertices.Add(new PointDto { X = 3, Y = 30 });
        GeoPoint.Core.Topology.EdgeSplitter.Apply(lot);
        lot.EdgeInfos.Add(new EdgeInfo { MX = 8, MY = 30, Confrontante = "Lote 16" });
        lot.EdgeInfos.Add(new EdgeInfo { MX = 4.5, MY = 30, Confrontante = "Lote 17" });
        lot.EdgeInfos.Add(new EdgeInfo { MX = 1.5, MY = 30, Confrontante = "Lote 18" });
        var ctx = TestProjects.Context(TestProjects.Project(lot));
        var rep = ctx.Report(lot);
        Assert.Equal(6, rep.Segments.Count);
        var plan = AnnotationPlanner.Plan(new[] { rep }, new ValueFormatter(ctx.Settings.Format), Opts());
        var names = plan.Texts.Where(t => t.Kind == AnnoTextKind.Confrontante).Select(t => t.Text).ToList();
        Assert.Contains("Lote 16", names);
        Assert.Contains("Lote 17", names);
        Assert.Contains("Lote 18", names);
        // Marcas tracejadas nas 4 pontas dos trechos (2 cantos + 2 divisões) e 3 cotas.
        Assert.Equal(4, plan.Lines.Count(l => l.Kind == AnnoLineKind.ConfrontanteTick));
        Assert.Equal(3, plan.Lines.Count(l => l.Kind == AnnoLineKind.ConfrontanteDimension));
        Assert.All(plan.Lines, l => Assert.True(l.A.Y >= 30 - 1e-9 && l.B.Y > 30));
        // Nomes fora do lote, além das distâncias.
        Assert.All(plan.Texts.Where(t => t.Kind == AnnoTextKind.Confrontante && t.Text.StartsWith("Lote 1")), t => Assert.True(t.At.Y > 30 + 2));
    }

    [Theory]
    [InlineData(SheetOrientation.Retrato, 210, 297)]
    [InlineData(SheetOrientation.Paisagem, 297, 210)]
    public void SheetLayout_A4CellsInsideFrame(SheetOrientation o, double w, double h)
    {
        var l = SheetLayout.A4(o);
        Assert.Equal(w, l.Width);
        Assert.Equal(h, l.Height);
        Assert.Equal(25, l.Frame.X0);
        Assert.Equal(7, l.Frame.Y0);
        foreach (var key in new[] { "titulo", "proprietario", "imovel", "municipio", "matricula", "area", "perimetro", "coordenada", "localizacao", "resp", "assprop", "escala", "data", "folha", "prancha" })
        {
            var c = l.Cell(key).Rect;
            Assert.True(c.X0 >= l.TitleBlock.X0 - 1e-9 && c.X1 <= l.TitleBlock.X1 + 1e-9 && c.Y0 >= l.TitleBlock.Y0 - 1e-9 && c.Y1 <= l.TitleBlock.Y1 + 1e-9, key);
            Assert.True(c.Width > 10 && c.Height >= 6, key);
        }
        Assert.True(l.Drawing.Width > 150 && l.Drawing.Height > 150);
    }

    [Fact]
    public void SheetLayout_ChooseScale()
    {
        // 20 x 30 m em 178 x 200 mm com 16 mm de margem → 1:200.
        Assert.Equal(200, SheetLayout.ChooseScale(20, 30, 172, 200, 16));
        Assert.Equal(1000, SheetLayout.ChooseScale(120, 80, 172, 150, 16));
    }

    [Fact]
    public void SheetPlanner_Desmembramento()
    {
        var parent = TestProjects.Rect(0, 0, 20, 30, "03");
        parent.Status = LotStatus.Arquivado;
        var a = TestProjects.Rect(0, 0, 10, 30, "03-A");
        var b = TestProjects.Rect(10, 0, 10, 30, "03-B");
        b.IsRemanescente = true;
        var p = TestProjects.Project(parent, a, b);
        var op = new OperationRecord { Type = OperationType.Desmembramento, ParentIds = { parent.Id }, ChildIds = { a.Id, b.Id } };
        var specs = SheetPlanner.ForOperation(p, op, new SheetRequest());
        Assert.Equal(new[] { SheetKind.Atual, SheetKind.Proposta, SheetKind.IndividualProposta, SheetKind.IndividualProposta }, specs.Select(x => x.Kind));
        Assert.Single(specs[0].LotIds);
        Assert.Empty(specs[0].QuadroIds);
        Assert.Equal(2, specs[1].QuadroIds.Count);
        Assert.True(specs[1].Hatch);
        // Mesma cor do lote em todas as pranchas.
        Assert.Equal(specs[1].Colors[b.Id], specs[3].Colors[b.Id]);
        var only = SheetPlanner.ForOperation(p, op, new SheetRequest { Atual = true, Proposta = false, IndividualProposta = false });
        Assert.Single(only);
    }
}

public class EdgeSplitTests
{
    [Fact]
    public void SplitEdge_CopiesInfoAndMergeRestores()
    {
        var lot = TestProjects.Rect(0, 0, 10, 30, "01");
        // Fundos (y = 30), percorrido de (10,30) para (0,30) no anel do retângulo.
        lot.EdgeInfos.Add(new EdgeInfo { MX = 5, MY = 30, Confrontante = "Fulano" });
        var edge = lot.Outer.Select(e => e.ToCurve()).First(c => Math.Abs(c.Mid.Y - 30) < 1e-9);
        var pts = GeoPoint.Core.Topology.EdgeSplitter.SplitEdge(lot, edge, new[] { 4.0, 3.0 });
        Assert.Equal(2, pts.Count);
        Assert.Equal(6, lot.Outer.Count);
        Assert.Equal(3, lot.EdgeInfos.Count(i => Math.Abs(i.MY - 30) < 1e-9 && i.Confrontante == "Fulano"));
        Assert.Equal(300, lot.ToRegion().Area, 6);
        // Juntar um trecho remove o vértice inserido.
        Assert.True(GeoPoint.Core.Topology.EdgeSplitter.Merge(lot, pts[0]));
        Assert.Equal(5, lot.Outer.Count);
        Assert.Single(lot.ExtraVertices);
        Assert.Equal(300, lot.ToRegion().Area, 6);
        // Um canto do desenho não é removido.
        Assert.False(GeoPoint.Core.Topology.EdgeSplitter.Merge(lot, new Vec2(0, 0)));
    }
}
