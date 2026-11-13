using GeoPoint.Core.Geometry;
using GeoPoint.Core.Operations;

namespace GeoPoint.Core.Tests;

public class LotShapesTests
{
    private static Dictionary<string, double> V(params (string k, double v)[] x) => x.ToDictionary(p => p.k, p => p.v);

    [Fact]
    public void AllShapes_DefaultsAreValid()
    {
        foreach (var d in LotShapes.All)
        {
            var r = LotShapes.Build(d.Kind, new Dictionary<string, double>());
            Assert.True(r.Ok, $"{d.Nome}: {string.Join(" ", r.Errors)}");
            Assert.True(r.Area > 0, d.Nome);
            Assert.Equal(r.Edges.Count, r.SideNames.Count);
            // Contorno fechado e começando no canto frente-esquerda, frente ao longo de +X.
            for (int i = 0; i < r.Edges.Count; i++) Assert.True(r.Edges[i].End.DistanceTo(r.Edges[(i + 1) % r.Edges.Count].Start) < 1e-9, d.Nome);
            Assert.Equal("Frente", r.SideNames[0]);
        }
    }

    [Fact]
    public void Rectangle_Triangle_Trapezoid_Areas()
    {
        Assert.Equal(360, LotShapes.Build(LotShapeKind.Retangulo, V(("frente", 12), ("prof", 30))).Area, 9);
        Assert.Equal(144, LotShapes.Build(LotShapeKind.Quadrado, V(("lado", 12))).Area, 9);
        // Triângulo 3-4-5: área 6.
        var t = LotShapes.Build(LotShapeKind.Triangulo, V(("frente", 3), ("dir", 4), ("esq", 5)));
        Assert.Equal(6, t.Area, 9);
        Assert.Equal(4, t.Edges[1].Length, 9);
        Assert.Equal(5, t.Edges[2].Length, 9);
        Assert.Equal((12 + 15) / 2.0 * 30, LotShapes.Build(LotShapeKind.TrapezioRetangulo, V(("frente", 12), ("fundos", 15), ("prof", 30))).Area, 9);
        Assert.Equal((12 + 8) / 2.0 * 30, LotShapes.Build(LotShapeKind.Trapezio, V(("frente", 12), ("fundos", 8), ("prof", 30), ("recuo", 2))).Area, 9);
        Assert.False(LotShapes.Build(LotShapeKind.Triangulo, V(("frente", 10), ("dir", 3), ("esq", 4))).Ok);
    }

    [Fact]
    public void Quadrilaterals_RespectAllSides()
    {
        // 4 lados + ângulo de 90° com lados de retângulo → retângulo 12 x 30.
        var q = LotShapes.Build(LotShapeKind.QuadrilateroAngulo, V(("frente", 12), ("dir", 30), ("fundos", 12), ("esq", 30), ("ang", 90)));
        Assert.True(q.Ok);
        Assert.Equal(360, q.Area, 6);
        // Lados diferentes: cada lado com a medida informada.
        var q2 = LotShapes.Build(LotShapeKind.QuadrilateroAngulo, V(("frente", 12), ("dir", 30), ("fundos", 12.5), ("esq", 31), ("ang", 92)));
        Assert.True(q2.Ok);
        Assert.Equal(new[] { 12, 30, 12.5, 31 }, q2.Edges.Select(e => Math.Round(e.Length, 9)));
        var d = LotShapes.Build(LotShapeKind.QuadrilateroDiagonal, V(("frente", 12), ("dir", 30), ("fundos", 12), ("esq", 30), ("diag", Math.Sqrt(12 * 12 + 30 * 30))));
        Assert.Equal(360, d.Area, 6);
        Assert.Equal(new[] { 12.0, 30, 12, 30 }, d.Edges.Select(e => Math.Round(e.Length, 9)));
    }

    [Fact]
    public void LShape_Corners_Polygon()
    {
        Assert.Equal(20 * 30 - 8 * 12, LotShapes.Build(LotShapeKind.LoteL, V(("frente", 20), ("prof", 30), ("rlarg", 8), ("rprof", 12))).Area, 9);
        Assert.Equal(15 * 30 - 4.5, LotShapes.Build(LotShapeKind.EsquinaChanfro, V(("frente", 15), ("prof", 30), ("cf", 3), ("cl", 3))).Area, 9);
        Assert.Equal(15 * 30 - 4.5, LotShapes.Build(LotShapeKind.EsquinaChanfro, V(("frente", 15), ("prof", 30), ("cf", 3), ("cl", 3), ("lado", 1))).Area, 9);
        // Curva de raio 6: perde r² − πr²/4.
        double lost = 36 - Math.PI * 36 / 4;
        Assert.Equal(15 * 30 - lost, LotShapes.Build(LotShapeKind.EsquinaRaio, V(("frente", 15), ("prof", 30), ("raio", 6))).Area, 6);
        Assert.Equal(15 * 30 - lost, LotShapes.Build(LotShapeKind.EsquinaRaio, V(("frente", 15), ("prof", 30), ("raio", 6), ("lado", 1))).Area, 6);
        // Polígono em L: 12 x 30 com recorte 5 x 4.
        var p = LotShapes.Build(LotShapeKind.Poligono, new Dictionary<string, double>(),
            new Dictionary<string, string> { ["lados"] = "12; 26; 5; 4; 7; 30", ["angs"] = "90; 90; 270; 90; 90; 90" });
        Assert.True(p.Ok, string.Join(" ", p.Errors));
        Assert.Equal(12 * 30 - 5 * 4, p.Area, 6);
        var bad = LotShapes.Build(LotShapeKind.Poligono, new Dictionary<string, double>(),
            new Dictionary<string, string> { ["lados"] = "12; 30; 12; 25", ["angs"] = "90; 90; 90; 90" });
        Assert.False(bad.Ok);
        Assert.Contains("não fecha", bad.Errors[0]);
    }

    [Fact]
    public void Place_RotatesAndTranslates()
    {
        var r = LotShapes.Build(LotShapeKind.Retangulo, V(("frente", 10), ("prof", 20)));
        var placed = LotShapes.Place(r.Edges, new Vec2(100, 50), Math.PI / 2, 10);
        Assert.Equal(new Vec2(100, 60), placed[0].Start);
        Assert.Equal(new Vec2(100, 70).X, placed[0].End.X, 9);
        Assert.Equal(70, placed[0].End.Y, 9);
        Assert.Equal(200, new Ring(placed).Area, 9);
    }
}

public class CollinearMergeTests
{
    [Fact]
    public void Remembramento_ThreeRectangles_KeepsPointsWhereConfrontanteChanges()
    {
        var a = TestProjects.Rect(0, 0, 10, 30, "01");
        var b = TestProjects.Rect(10, 0, 10, 30, "02");
        var c = TestProjects.Rect(20, 0, 10, 30, "03");
        var u = GeoPoint.Core.Operations.ParcelOperations.Union(new[] { a, b, c });
        Assert.True(u.Ok);
        var lot = new GeoPoint.Core.Model.LotRecord { Nome = "R", Outer = u.Outer, Holes = u.Holes };
        Assert.True(lot.Outer.Count > 4);   // a união guarda os pontos dos lotes antigos
        // Fundos com 3 confrontantes diferentes e frente com a mesma rua.
        lot.EdgeInfos.Add(new GeoPoint.Core.Model.EdgeInfo { MX = 5, MY = 30, Confrontante = "Lote 16" });
        lot.EdgeInfos.Add(new GeoPoint.Core.Model.EdgeInfo { MX = 15, MY = 30, Confrontante = "Lote 17" });
        lot.EdgeInfos.Add(new GeoPoint.Core.Model.EdgeInfo { MX = 25, MY = 30, Confrontante = "Lote 18" });
        foreach (var x in new[] { 5.0, 15, 25 }) lot.EdgeInfos.Add(new GeoPoint.Core.Model.EdgeInfo { MX = x, MY = 0, Confrontante = "Rua 1", Testada = true });
        int removed = GeoPoint.Core.Operations.CollinearMerge.Apply(lot);
        // A frente (mesma rua) vira uma divisa só; nos fundos os pontos ficam onde o confrontante muda.
        Assert.Equal(2, removed);
        Assert.Equal(6, lot.Outer.Count);
        Assert.Equal(900, lot.ToRegion().Area, 6);
        foreach (var (x, name) in new[] { (5.0, "Lote 16"), (15.0, "Lote 17"), (25.0, "Lote 18") })
            Assert.Equal(name, GeoPoint.Core.Calc.ProjectContext.FindInfo(lot, new GeoPoint.Core.Geometry.Vec2(x, 30))!.Confrontante);
        var front = GeoPoint.Core.Calc.ProjectContext.FindInfo(lot, new GeoPoint.Core.Geometry.Vec2(15, 0));
        Assert.Equal("Rua 1", front!.Confrontante);
        Assert.True(front.Testada);
    }
}
