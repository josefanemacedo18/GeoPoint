using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;

namespace GeoPoint.Core.Tests;

public class V11Tests
{
    private static LotRecord Rect(double x0, double y0, double w, double h, string name, DateTime created, string quadra = "A")
    {
        var l = TestProjects.Rect(x0, y0, w, h, name, quadra);
        l.Created = created;
        return l;
    }

    [Fact]
    public void Registry_KeepsNamesAcrossSubdivision()
    {
        var t0 = new DateTime(2026, 1, 1);
        var parent = Rect(0, 0, 20, 30, "Mae", t0);
        var a = Rect(0, 0, 10, 30, "01", t0.AddDays(1));
        var b = Rect(10, 0, 10, 30, "02", t0.AddDays(1));
        var p = TestProjects.Project(parent);
        var ctx = TestProjects.Context(p);
        var namesParent = ctx.Report(parent).Vertices.ToDictionary(v => (v.Internal.X, v.Internal.Y), v => v.Name);
        Assert.Equal(new[] { "P-01", "P-02", "P-03", "P-04" }, namesParent.Values.OrderBy(n => n));

        parent.Status = LotStatus.Arquivado;
        p.Lots.Add(a); p.Lots.Add(b);
        ctx.Invalidate();
        foreach (var lot in new[] { a, b })
            foreach (var v in ctx.Report(lot).Vertices)
                if (namesParent.TryGetValue((v.Internal.X, v.Internal.Y), out var n)) Assert.Equal(n, v.Name);
        var newNames = ctx.Report(a).Vertices.Concat(ctx.Report(b).Vertices).Select(v => v.Name).Distinct().Except(namesParent.Values).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "P-05", "P-06" }, newNames);
        // O original continua com os mesmos nomes.
        Assert.Equal(namesParent.Values.OrderBy(x => x), ctx.Report(parent).Vertices.Select(v => v.Name).OrderBy(x => x));
    }

    [Fact]
    public void Registry_QuadraNumbering()
    {
        var t0 = new DateTime(2026, 1, 1);
        var a = Rect(0, 0, 10, 10, "01", t0, "A");
        var b = Rect(30, 0, 10, 10, "01", t0.AddHours(1), "B");
        var ctx = TestProjects.Context(TestProjects.Project(a, b), s => s.Naming.Numbering = VertexNumbering.Quadra);
        Assert.Equal(new[] { "P-01", "P-02", "P-03", "P-04" }, ctx.Report(a).Vertices.Select(v => v.Name).OrderBy(x => x));
        Assert.Equal(new[] { "P-01", "P-02", "P-03", "P-04" }, ctx.Report(b).Vertices.Select(v => v.Name).OrderBy(x => x));
        ctx.Settings.Naming.Numbering = VertexNumbering.Projeto;
        ctx.Invalidate();
        Assert.Equal(new[] { "P-05", "P-06", "P-07", "P-08" }, ctx.Report(b).Vertices.Select(v => v.Name).OrderBy(x => x));
    }

    [Fact]
    public void Sides_FromTestada()
    {
        var lot = TestProjects.Rect(0, 0, 10, 30, "01");
        lot.EdgeInfos.Add(new EdgeInfo { MX = 5, MY = 0, Testada = true });
        var ctx = TestProjects.Context(TestProjects.Project(lot));
        var r = ctx.Report(lot);
        LadoLote SideAt(double x, double y) => r.Segments.Single(s => s.Curve.Mid.DistanceTo(new Vec2(x, y)) < 1e-6).Lado;
        Assert.Equal(LadoLote.Frente, SideAt(5, 0));
        Assert.Equal(LadoLote.Fundos, SideAt(5, 30));
        // Da rua (ao sul), olhando para o lote (norte): a direita é o leste.
        Assert.Equal(LadoLote.LateralDireita, SideAt(10, 15));
        Assert.Equal(LadoLote.LateralEsquerda, SideAt(0, 15));
        // Lado definido pelo usuário prevalece.
        lot.EdgeInfos.Add(new EdgeInfo { MX = 5, MY = 30, Lado = LadoLote.Outro, Confrontante = "Córrego" });
        ctx.Invalidate();
        Assert.Equal(LadoLote.Outro, ctx.Report(lot).Segments.Single(s => s.Curve.Mid.DistanceTo(new Vec2(5, 30)) < 1e-6).Lado);
    }

    [Fact]
    public void Confrontante_ViaAndLotTypes_InMemorial()
    {
        var a = TestProjects.Rect(0, 0, 10, 30, "01");
        var b = TestProjects.Rect(10, 0, 10, 30, "02");
        var p = TestProjects.Project(a, b);
        var via = new ViaRecord { Nome = "Rua E" };
        p.Vias.Add(via);
        a.EdgeInfos.Add(new EdgeInfo { MX = 5, MY = 0, Testada = true, Tipo = ConfrontanteTipo.Via, ViaId = via.Id });
        a.EdgeInfos.Add(new EdgeInfo { MX = 5, MY = 30, Tipo = ConfrontanteTipo.Texto, Confrontante = "Maria da Silva" });
        var ctx = TestProjects.Context(p);
        var r = ctx.Report(a);
        Assert.Equal("Rua E", r.Segments.Single(s => s.Testada).Confrontante);
        Assert.Contains(r.Segments, s => s.ConfrontanteAutomatico && s.ConfrontanteLotId == b.Id);
        via.Nome = "Avenida Brasil";
        ctx.Invalidate();
        Assert.Equal("Avenida Brasil", ctx.Report(a).Segments.Single(s => s.Testada).Confrontante);

        var text = ReportAndDocumentTests.DocText(new MemorialGenerator(ctx, new MemorialPhrases())
            .Generate(new MemorialRequest { Kind = MemorialKind.Lotes, Lots = new() { a } }), out _);
        Assert.Contains("pela frente, confrontando com Avenida Brasil", text);
        Assert.Contains("pelos fundos, confrontando com Maria da Silva", text);
        Assert.Contains("pela lateral direita, confrontando com Lote 02", text);
        Assert.Contains("Confrontações:", text);
    }

    [Fact]
    public void Inheritance_ParentToChildren_AndSiblings()
    {
        var t0 = new DateTime(2026, 1, 1);
        var parent = Rect(0, 0, 20, 30, "Mae", t0);
        var p = TestProjects.Project(parent);
        var via = new ViaRecord { Nome = "Rua A" };
        p.Vias.Add(via);
        parent.EdgeInfos.Add(new EdgeInfo { MX = 10, MY = 0, Testada = true, Tipo = ConfrontanteTipo.Via, ViaId = via.Id });
        parent.EdgeInfos.Add(new EdgeInfo { MX = 10, MY = 30, Confrontante = "João" });
        var neighbor = Rect(20, 0, 10, 30, "Viz", t0);
        neighbor.EdgeInfos.Add(new EdgeInfo { MX = 20, MY = 10, Tipo = ConfrontanteTipo.Lote, LotId = parent.Id });
        p.Lots.Add(neighbor);
        var a = Rect(0, 0, 10, 30, "01", t0.AddDays(1));
        var b = Rect(10, 0, 10, 30, "02", t0.AddDays(1));
        var ctx = TestProjects.Context(p);

        a.EdgeInfos = ConfrontanteInheritance.Inherit(new[] { parent }, a, new[] { a, b }, ctx);
        b.EdgeInfos = ConfrontanteInheritance.Inherit(new[] { parent }, b, new[] { a, b }, ctx);
        Assert.Contains(a.EdgeInfos, i => i.Tipo == ConfrontanteTipo.Via && i.ViaId == via.Id && i.Testada && Math.Abs(i.MX - 5) < 1e-9);
        Assert.Contains(b.EdgeInfos, i => i.Tipo == ConfrontanteTipo.Via && Math.Abs(i.MX - 15) < 1e-9);
        Assert.Contains(a.EdgeInfos, i => i.Confrontante == "João");
        Assert.Contains(a.EdgeInfos, i => i.Tipo == ConfrontanteTipo.Lote && i.LotId == b.Id);
        Assert.Contains(b.EdgeInfos, i => i.Tipo == ConfrontanteTipo.Lote && i.LotId == a.Id);
        Assert.Contains(b.EdgeInfos, i => i.Tipo == ConfrontanteTipo.Lote && i.LotId == neighbor.Id);

        parent.Status = LotStatus.Arquivado;
        p.Lots.Add(a); p.Lots.Add(b);
        Assert.Equal(1, ConfrontanteInheritance.Retarget(p, new[] { parent.Id }, new[] { a, b }));
        Assert.Equal(b.Id, neighbor.EdgeInfos[0].LotId);
    }

    [Fact]
    public void Inheritance_Merge()
    {
        var a = TestProjects.Rect(0, 0, 10, 30, "01");
        var b = TestProjects.Rect(10, 0, 10, 30, "02");
        a.EdgeInfos.Add(new EdgeInfo { MX = 5, MY = 0, Confrontante = "Rua X", Testada = true });
        b.EdgeInfos.Add(new EdgeInfo { MX = 15, MY = 0, Confrontante = "Rua X", Testada = true });
        a.EdgeInfos.Add(new EdgeInfo { MX = 10, MY = 15, Confrontante = "não herdar" });
        var u = ParcelOperations.Union(new[] { a, b });
        var result = new LotRecord { Nome = "R", Outer = u.Outer };
        var infos = ConfrontanteInheritance.Inherit(new[] { a, b }, result, Array.Empty<LotRecord>(), null);
        Assert.Equal(2, infos.Count(i => i.Confrontante == "Rua X"));
        Assert.DoesNotContain(infos, i => i.Confrontante == "não herdar");
    }

    [Fact]
    public void Remanescente_DisplayName()
    {
        var l = new LotRecord { IsRemanescente = true, Quadra = "B" };
        Assert.Equal("Lote Remanescente – Quadra B", l.DisplayName);
        l.Nome = "03";
        Assert.Equal("Lote 03 (Remanescente) – Quadra B", l.DisplayName);
    }
}

public class EdgeSplitterTests
{
    [Fact]
    public void SplitTopEdge_ByConfrontanteExtents()
    {
        // Como no exemplo: divisa de 30 m dividida em 12 + 12 + 6 (Lotes 16, 17 e 18).
        var lot = TestProjects.Rect(0, 0, 30, 12, "15");
        var top = lot.Outer.Select(e => e.ToCurve()).Single(c => Math.Abs(c.Mid.Y - 12) < 1e-9);
        var start = top.Start.X < top.End.X ? top : top.Reversed();
        var pts = Topology.EdgeSplitter.PointsAlong(start, new[] { 12.0, 12.0, 6.0 });
        Assert.Equal(2, pts.Count);
        lot.ExtraVertices.AddRange(pts.Select(p => new PointDto { X = p.X, Y = p.Y }));
        Topology.EdgeSplitter.Apply(lot);
        Assert.Equal(6, lot.Outer.Count);
        Assert.Equal(360, lot.ToRegion().Area, 9);
        var ctx = TestProjects.Context(TestProjects.Project(lot));
        Assert.Equal(6, ctx.Report(lot).Vertices.Count);
        // Reaplicar não duplica.
        Topology.EdgeSplitter.Apply(lot);
        Assert.Equal(6, lot.Outer.Count);
    }
}

public class AnuenciaTests
{
    [Fact]
    public void GeneratesOnePagePerConfrontante()
    {
        var a = TestProjects.Rect(0, 0, 10, 30, "01");
        var b = TestProjects.Rect(10, 0, 10, 30, "02");
        b.Proprietario = "Valtenes dos Santos Almeida";
        var p = TestProjects.Project(a, b);
        var via = new ViaRecord { Nome = "Rua E" };
        p.Vias.Add(via);
        a.EdgeInfos.Add(new EdgeInfo { MX = 5, MY = 0, Testada = true, Tipo = ConfrontanteTipo.Via, ViaId = via.Id });
        a.EdgeInfos.Add(new EdgeInfo { MX = 5, MY = 30, Confrontante = "Jacy Rodrigues Aguiar" });
        var ctx = TestProjects.Context(p);
        var groups = Memorial.AnuenciaGenerator.Groups(ctx, a, includeVias: false);
        Assert.Equal(2, groups.Count);
        var bytes = new Memorial.AnuenciaGenerator(ctx).Generate(a, groups, new DateTime(2026, 7, 7));
        var text = ReportAndDocumentTests.DocText(bytes, out var xml);
        Assert.Contains("DECLARAÇÃO DE ANUÊNCIA DE CONFRONTANTE", text);
        Assert.Contains("Valtenes dos Santos Almeida", text);
        Assert.Contains("Jacy Rodrigues Aguiar", text);
        Assert.Contains("Fulano de Tal", text);
        Assert.DoesNotContain("Rua E (logradouro", text);
        Assert.Single(xml.Descendants(Office.Wml.W + "br"), b => (string?)b.Attribute(Office.Wml.W + "type") == "page");
    }
}

public class SubdivisionPlannerTests
{
    [Fact]
    public void CustomAreas_And_Frontages()
    {
        var lot = TestProjects.Rect(0, 0, 20, 50, "G").ToRegion();
        var f = new LineCurve(new Vec2(0, 0), new Vec2(20, 0));
        var r = Operations.AreaSplitter.Split(new Operations.SplitRequest { Region = lot, CutDirection = new Vec2(0, 1), Frontage = f, Mode = Operations.SplitMode.CustomAreas, Values = new[] { 300.0, 200 } });
        Assert.Equal(new[] { 300.0, 200, 500 }, r.Pieces.Select(p => Math.Round(p.Area, 6)).ToArray());
        var r2 = Operations.AreaSplitter.Split(new Operations.SplitRequest { Region = lot, CutDirection = new Vec2(0, 1), Frontage = f, Mode = Operations.SplitMode.CustomFrontages, Values = new[] { 12.0, 5 } });
        Assert.Equal(new[] { 12.0, 5, 3 }, r2.Pieces.Select(p => Math.Round(p.Frontage, 6)).ToArray());
    }

    [Fact]
    public void FromDrawnLines_TracksSourcesAndFrontage()
    {
        var parent = TestProjects.Rect(0, 0, 20, 30, "Mae");
        parent.EdgeInfos.Add(new EdgeInfo { MX = 10, MY = 0, Testada = true });
        // Linha desenhada que passa um pouco além do lote (como no Revit).
        var cut = new LineCurve(new Vec2(8, -1), new Vec2(8, 31));
        var pieces = Operations.SubdivisionPlanner.FromCuts(parent, new[] { ((Curve2)cut, (string?)"linha-1") });
        Assert.Equal(2, pieces.Count);
        Assert.Equal(new[] { 240.0, 360 }, pieces.Select(p => Math.Round(p.Area, 6)).OrderBy(x => x).ToArray());
        Assert.Equal(new[] { 8.0, 12 }, pieces.Select(p => Math.Round(p.Frontage, 6)).OrderBy(x => x).ToArray());
        Assert.All(pieces, p => Assert.Contains(p.Outer, e => e.Src.Contains("linha-1")));
        Assert.All(pieces, p => Assert.Contains(p.Outer, e => e.Src.Contains("src-Mae-0")));
    }
}
