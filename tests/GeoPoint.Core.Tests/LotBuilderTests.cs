using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Topology;

namespace GeoPoint.Core.Tests;

public class LotBuilderTests
{
    private static InputCurve L(double x1, double y1, double x2, double y2, int tag) => new(new LineCurve(new Vec2(x1, y1), new Vec2(x2, y2)), tag);

    [Fact]
    public void Face_ToLot_TracksSources_KeepsCollinearByDefault()
    {
        // Frente desenhada em duas linhas colineares (vértice "artificial" em x=10).
        var curves = new[] { L(0, 0, 10, 0, 0), L(10, 0, 20, 0, 1), L(20, 0, 20, 10, 2), L(20, 10, 0, 10, 3), L(0, 10, 0, 0, 4) };
        var net = PlanarNetwork.Build(curves);
        var face = net.Faces.Single();
        var src = new[] { "a", "b", "c", "d", "e" };
        var (outer, _) = LotBuilder.FromFace(net, face, t => new[] { src[t] }, false, mergeCollinear: false);
        Assert.Equal(5, outer.Count);
        var (merged, _) = LotBuilder.FromFace(net, face, t => new[] { src[t] }, false, mergeCollinear: true);
        Assert.Equal(4, merged.Count);
        Assert.Contains(merged, e => e.Src.Contains("a") && e.Src.Contains("b"));
    }

    [Fact]
    public void Collinear_AtTJunction_IsKept()
    {
        // Vértice em x=10 é encontro com outra divisa (grau 3) → mantido.
        var curves = new[] { L(0, 0, 20, 0, 0), L(20, 0, 20, 10, 1), L(20, 10, 0, 10, 2), L(0, 10, 0, 0, 3), L(10, 0, 10, -10, 4), L(10, -10, 0, -10, 5), L(0, -10, 0, 0, 6) };
        var net = PlanarNetwork.Build(curves);
        var face = net.FindFace(new Vec2(10, 5))!;
        var (merged, _) = LotBuilder.FromFace(net, face, t => new[] { t.ToString() }, false, mergeCollinear: true);
        Assert.Equal(5, merged.Count);
    }

    [Fact]
    public void RemapEdgeInfos_FollowsSplitEdges()
    {
        var old = new List<EdgeInfo> { new() { MX = 10, MY = 0, Confrontante = "Rua A", Testada = true } };
        var edges = new List<EdgeDto>
        {
            EdgeDto.FromCurve(new LineCurve(new(0, 0), new(12, 0))),
            EdgeDto.FromCurve(new LineCurve(new(12, 0), new(20, 0))),
        };
        var res = LotBuilder.RemapEdgeInfos(old, edges);
        Assert.Single(res);
        Assert.Equal(6, res[0].MX, 9);
        Assert.Equal("Rua A", res[0].Confrontante);
    }

    [Fact]
    public void TryMerge_Arcs()
    {
        var a1 = CurveFactory.FromCenter(Vec2.Zero, new Vec2(10, 0), new Vec2(0, 10), true);
        var a2 = CurveFactory.FromCenter(Vec2.Zero, new Vec2(0, 10), new Vec2(-10, 0), true);
        var m = LotBuilder.TryMerge(a1, a2) as ArcCurve;
        Assert.NotNull(m);
        Assert.Equal(Math.PI, m!.Sweep, 9);
    }
}
