using GeoPoint.Core.Operations;
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

    /// <summary>Lote da imagem enviada pelo usuário (736,92 m²), desmembrado em dois por uma divisa interna.</summary>
    private static (LotRecord a, LotRecord b) UserLotSplit()
    {
        var p1 = new Vec2(47.651, 22.458); var p2 = new Vec2(18.643, 0); var p3 = new Vec2(0, 24.473);
        var p4 = new Vec2(23.897, 25.553); var p5 = new Vec2(39.733, 33.831);
        // Divisa interna de um ponto da divisa P-01/P-02 até um ponto da divisa P-02/P-03.
        var c1 = Vec2.Lerp(p1, p2, 0.45); var c2 = Vec2.Lerp(p2, p3, 0.52);
        var a = TestProjects.Poly("01", c2, p3, p4, p5, p1, c1);
        var b = TestProjects.Poly("02", c1, p2, c2);
        a.EdgeInfos.Add(new EdgeInfo { MX = (p3.X + p4.X) / 2, MY = (p3.Y + p4.Y) / 2, Confrontante = "Rua 1" });
        a.EdgeInfos.Add(new EdgeInfo { MX = (p4.X + p5.X) / 2, MY = (p4.Y + p5.Y) / 2, Confrontante = "Bemeval" });
        a.EdgeInfos.Add(new EdgeInfo { MX = (p5.X + p1.X) / 2, MY = (p5.Y + p1.Y) / 2, Confrontante = "Coité" });
        return (a, b);
    }

    [Fact]
    public void Annotation_NoOverlapOnUserLot()
    {
        var (a, b) = UserLotSplit();
        var ctx = TestProjects.Context(TestProjects.Project(a, b));
        var reps = new[] { ctx.Report(a), ctx.Report(b) };
        // Escala 1:250, texto de 2 mm.
        var o = new AnnotationPlanOptions { TextM = 0.5, OffsetM = 0.375, DotRadiusM = 0.15, PadM = 0.1 };
        var plan = AnnotationPlanner.Plan(reps, new ValueFormatter(ctx.Settings.Format), o);
        Assert.Equal(0, plan.Conflicts);
        var boxes = plan.Texts.Select(t => t.Box).ToList();
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
                Assert.False(boxes[i].Intersects(boxes[j]), $"\"{plan.Texts[i].Text}\" sobre \"{plan.Texts[j].Text}\"");
        var edges = reps.SelectMany(r => r.Segments.Select(s => s.Curve)).ToList();
        foreach (var t in plan.Texts)
            Assert.DoesNotContain(edges, e => t.Box.Intersects(e.Start, e.End));
        // Rotação sempre legível e textos das divisas alinhados com a divisa.
        Assert.All(plan.Texts, t => Assert.InRange(t.Rotation, -Math.PI / 2 - 1e-9, Math.PI / 2 + 1e-9));
        Assert.Contains(plan.Texts, t => t.Kind == AnnoTextKind.Confrontante && t.Text == "Coité");
    }

    [Fact]
    public void Obb_IntersectionTests()
    {
        var a = Obb.Of(new Vec2(0, 0), 0, 2, 1);
        Assert.True(a.Intersects(Obb.Of(new Vec2(3, 0), 0, 1.5, 1)));
        Assert.False(a.Intersects(Obb.Of(new Vec2(4, 0), 0, 1.5, 1)));
        Assert.True(a.Intersects(Obb.Of(new Vec2(2.5, 2.5), Math.PI / 4, 2, 0.5)));
        Assert.True(a.Intersects(new Vec2(-5, 0.5), new Vec2(5, 0.5)));
        Assert.False(a.Intersects(new Vec2(-5, 1.5), new Vec2(5, 1.5)));
        Assert.False(a.Intersects(new Vec2(2.5, -5), new Vec2(2.5, 5)));
        Assert.Equal(-Math.PI / 4, AnnotationPlanner.Readable(3 * Math.PI / 4), 9);
        Assert.Equal(Math.PI / 2, AnnotationPlanner.Readable(-Math.PI / 2), 9);
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

public class DesmembramentoFlowTests
{
    [Fact]
    public void Split_InheritsAllConfrontantesAndSnapsToVertex()
    {
        var p1 = new Vec2(47.651, 22.458); var p2 = new Vec2(18.643, 0); var p3 = new Vec2(0, 24.473);
        var p4 = new Vec2(23.897, 25.553); var p5 = new Vec2(39.733, 33.831);
        var parent = TestProjects.Poly("01", p1, p2, p3, p4, p5);
        var via = new ViaRecord { Nome = "Rua 1" };
        void Info(Vec2 a, Vec2 b, EdgeInfo i) { var m = Vec2.Lerp(a, b, 0.5); i.MX = m.X; i.MY = m.Y; parent.EdgeInfos.Add(i); }
        Info(p1, p2, new EdgeInfo { Confrontante = "Juca" });
        Info(p2, p3, new EdgeInfo { Confrontante = "Aaaa" });
        Info(p3, p4, new EdgeInfo { Tipo = ConfrontanteTipo.Via, ViaId = via.Id, Testada = true });
        Info(p4, p5, new EdgeInfo { Confrontante = "Bemeval" });
        Info(p5, p1, new EdgeInfo { Confrontante = "Coité" });
        var project = TestProjects.Project(parent);
        project.Vias.Add(via);
        var ctx = TestProjects.Context(project);

        // Divisa que termina a 12 cm do vértice P-04 (deve encaixar nele).
        var nearP4 = p4 + (p3 - p4).Normalized() * 0.12;
        var cuts = new List<LineCurve> { new(Vec2.Lerp(p1, p2, 0.45), nearP4) };
        Assert.Equal(1, SubdivisionPlanner.SnapCuts(parent, cuts, 0.30));
        Assert.True(cuts[0].End.DistanceTo(p4) < 1e-9);

        var pieces = SubdivisionPlanner.FromCuts(parent, cuts.Select(c => ((Curve2)c, (string?)null)).ToList());
        Assert.Equal(2, pieces.Count);
        Assert.Equal(parent.ToRegion().Area, pieces.Sum(p => p.Area), 6);
        var kids = pieces.Select((p, i) => new LotRecord { Nome = $"0{i + 1}", Numero = $"0{i + 1}", Quadra = "A", Outer = p.Outer, Holes = p.Holes }).ToList();
        foreach (var k in kids) k.EdgeInfos = ConfrontanteInheritance.Inherit(new[] { parent }, k, kids, ctx);
        parent.Status = LotStatus.Arquivado;
        project.Lots.AddRange(kids);
        ctx.Invalidate();
        foreach (var k in kids)
        {
            var rep = ctx.Report(k);
            Assert.All(rep.Segments, s => Assert.False(string.IsNullOrWhiteSpace(s.Confrontante), $"{k.DisplayName} {s.From.Name}-{s.To.Name} sem confrontante"));
            Assert.Contains(rep.Segments, s => s.Confrontante.StartsWith("Lote 0"));
            Assert.DoesNotContain(rep.Segments, s => s.Curve.Length < 0.5);
        }
        Assert.Contains(ctx.Report(kids.First(k => ctx.Report(k).Segments.Any(s => s.ConfrontanteViaId == via.Id))).Segments, s => s.Confrontante == "Rua 1" && s.Lado == LadoLote.Frente);
    }
}

public class GeneralSheetAnnotationTests
{
    [Fact]
    public void GeneralSheet_NoVerticesNoAzimuths_WithOwner_AvoidsObstacle()
    {
        var a = TestProjects.Rect(0, 0, 10, 30, "01");
        var b = TestProjects.Rect(10, 0, 10, 30, "02");
        b.Proprietario = "Maria";
        var ctx = TestProjects.Context(TestProjects.Project(a, b));
        var reps = new[] { ctx.Report(a), ctx.Report(b) };
        var marker = Obb.Of(new Vec2(0, 0), 0, 1.2, 1.2);
        var o = new AnnotationPlanOptions
        {
            TextM = 0.5, OffsetM = 0.375, DotRadiusM = 0.15, PadM = 0.1, Vertices = false, EdgeAngle = false, Dots = false,
            OwnerOf = l => string.IsNullOrWhiteSpace(l.Proprietario) ? "Fulano de Tal" : l.Proprietario, Obstacles = { marker },
        };
        var plan = AnnotationPlanner.Plan(reps, new GeoPoint.Core.Memorial.ValueFormatter(ctx.Settings.Format), o);
        Assert.DoesNotContain(plan.Texts, t => t.Kind is AnnoTextKind.Vertex or AnnoTextKind.Azimuth);
        Assert.Empty(plan.Dots);
        Assert.Equal(7, plan.Texts.Count(t => t.Kind == AnnoTextKind.Distance));
        Assert.Contains(plan.Texts, t => t.Kind == AnnoTextKind.LotLabel && t.Text.Contains("Prop.: Maria"));
        Assert.Contains(plan.Texts, t => t.Kind == AnnoTextKind.LotLabel && t.Text.Contains("Prop.: Fulano de Tal"));
        Assert.DoesNotContain(plan.Texts, t => t.Box.Intersects(marker));
    }
}
