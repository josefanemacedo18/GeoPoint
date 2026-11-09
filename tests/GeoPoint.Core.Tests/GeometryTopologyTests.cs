using GeoPoint.Core.Geometry;
using GeoPoint.Core.Topology;

namespace GeoPoint.Core.Tests;

public class GeometryTopologyTests
{
    private static InputCurve L(double x1, double y1, double x2, double y2, int tag = 0) => new(new LineCurve(new Vec2(x1, y1), new Vec2(x2, y2)), tag);

    private static PlanarNetwork Net(params InputCurve[] c) => PlanarNetwork.Build(c);

    [Fact]
    public void ArcFromThreePoints_SemicircleAreaAndLength()
    {
        var arc = (ArcCurve)CurveFactory.FromThreePoints(new Vec2(10, 0), new Vec2(0, 10), new Vec2(-10, 0));
        Assert.Equal(10, arc.Radius, 9);
        Assert.True(arc.IsCounterClockwise);
        Assert.Equal(Math.PI * 10, arc.Length, 9);
        var ring = new Ring(new Curve2[] { arc, new LineCurve(new Vec2(-10, 0), new Vec2(10, 0)) });
        Assert.Equal(Math.PI * 100 / 2, ring.SignedArea, 9);
    }

    [Fact]
    public void ArcParamOf_HandlesWrapAround()
    {
        var arc = CurveFactory.FromCenter(Vec2.Zero, new Vec2(1, 0), new Vec2(0, 1), counterClockwise: true);
        Assert.Equal(0.5, arc.ParamOf(new Vec2(Math.Cos(Math.PI / 4), Math.Sin(Math.PI / 4))), 9);
        Assert.True(arc.ParamOf(new Vec2(0.9999, -0.01)) < 0);
        Assert.True(arc.ParamOf(new Vec2(-0.01, 0.9999)) > 1);
    }

    [Fact]
    public void Square_OneFace()
    {
        var net = Net(L(0, 0, 10, 0), L(10, 0, 10, 10), L(10, 10, 0, 10), L(0, 10, 0, 0));
        Assert.Single(net.Faces);
        Assert.Equal(100, net.Faces[0].Area, 9);
        Assert.Equal(4, net.Faces[0].OuterEdges.Count);
    }

    [Fact]
    public void UrbanBlock_TJunctions_ThreeLots()
    {
        // Frente e fundos em uma linha só; divisas em "T".
        var net = Net(
            L(0, 0, 36, 0, 1), L(0, 30, 36, 30, 2), L(0, 0, 0, 30, 3), L(36, 0, 36, 30, 4),
            L(12, 0, 12, 30, 5), L(24, 30, 24, 0, 6));
        Assert.Equal(3, net.Faces.Count);
        Assert.All(net.Faces, f => Assert.Equal(360, f.Area, 6));
        var mid = net.FindFace(new Vec2(18, 15));
        Assert.NotNull(mid);
        Assert.Equal(4, mid!.OuterEdges.Count);
        Assert.Contains(1, mid.OuterEdges.SelectMany(e => e.Tags));
    }

    [Fact]
    public void CrossingLines_AreNoded()
    {
        var net = Net(L(0, 0, 20, 0), L(20, 0, 20, 20), L(20, 20, 0, 20), L(0, 20, 0, 0), L(10, -5, 10, 25), L(-5, 10, 25, 10));
        Assert.Equal(4, net.Faces.Count);
        Assert.All(net.Faces, f => Assert.Equal(100, f.Area, 6));
        Assert.Contains(net.Diagnostics, d => d.Kind == DiagnosticKind.Crossing);
        Assert.Contains(net.Diagnostics, d => d.Kind == DiagnosticKind.DanglingLine);
    }

    [Fact]
    public void SmallGap_WithinTolerance_IsClosed()
    {
        var net = Net(L(0, 0, 10, 0), L(10, 0, 10, 10), L(10, 10, 0, 10), L(0, 10, 0, 0.0005));
        Assert.Single(net.Faces);
        Assert.Contains(net.Diagnostics, d => d.Kind == DiagnosticKind.SnappedEndpoints);
    }

    [Fact]
    public void Gap_AboveTolerance_ReportsPossibleGap()
    {
        var net = Net(L(0, 0, 10, 0), L(10, 0, 10, 10), L(10, 10, 0, 10), L(0, 10, 0, 0.005));
        Assert.Empty(net.Faces);
        var gap = net.Diagnostics.First(d => d.Kind == DiagnosticKind.PossibleGap);
        Assert.Equal(0.005, gap.Value, 6);
    }

    [Fact]
    public void Undershoot_TJunction_Reported()
    {
        // Divisa interna que termina 3 mm antes da frente.
        var net = Net(L(0, 0, 20, 0), L(20, 0, 20, 10), L(20, 10, 0, 10), L(0, 10, 0, 0), L(10, 10, 10, 0.003));
        Assert.Single(net.Faces);
        Assert.Contains(net.Diagnostics, d => d.Kind == DiagnosticKind.PossibleGap);
    }

    [Fact]
    public void DuplicateLines_Merged()
    {
        var net = Net(L(0, 0, 10, 0), L(0, 0, 10, 0, 9), L(10, 0, 10, 10), L(10, 10, 0, 10), L(0, 10, 0, 0));
        Assert.Single(net.Faces);
        Assert.Contains(net.Diagnostics, d => d.Kind == DiagnosticKind.Duplicate);
    }

    [Fact]
    public void PartialOverlap_IsSplit()
    {
        var net = Net(L(0, 0, 10, 0), L(5, 0, 20, 0), L(20, 0, 20, 10), L(20, 10, 0, 10), L(0, 10, 0, 0));
        Assert.Single(net.Faces);
        Assert.Equal(200, net.Faces[0].Area, 6);
    }

    [Fact]
    public void ArcBoundary_ExactArea()
    {
        // Retângulo 20x10 com o lado superior em arco (flecha 2 m).
        var arc = CurveFactory.FromThreePoints(new Vec2(20, 10), new Vec2(10, 12), new Vec2(0, 10));
        var net = Net(L(0, 0, 20, 0), L(20, 0, 20, 10), new InputCurve(arc, 7), L(0, 10, 0, 0));
        Assert.Single(net.Faces);
        var a = (ArcCurve)arc;
        double theta = Math.Abs(a.Sweep);
        double segment = a.Radius * a.Radius / 2 * (theta - Math.Sin(theta));
        Assert.Equal(200 + segment, net.Faces[0].Area, 6);
        Assert.Contains(net.Faces[0].OuterEdges, e => e.Curve.IsArc);
    }

    [Fact]
    public void FullCircle_IsFace()
    {
        var a1 = CurveFactory.FromCenter(Vec2.Zero, new Vec2(5, 0), new Vec2(-5, 0), true);
        var a2 = CurveFactory.FromCenter(Vec2.Zero, new Vec2(-5, 0), new Vec2(5, 0), true);
        var net = Net(new InputCurve(a1, 0), new InputCurve(a2, 1));
        Assert.Single(net.Faces);
        Assert.Equal(Math.PI * 25, net.Faces[0].Area, 6);
    }

    [Fact]
    public void Island_BecomesHole_AndBridgeRemoved()
    {
        var curves = new List<InputCurve>
        {
            L(0, 0, 30, 0), L(30, 0, 30, 30), L(30, 30, 0, 30), L(0, 30, 0, 0),
            L(10, 10, 20, 10), L(20, 10, 20, 20), L(20, 20, 10, 20), L(10, 20, 10, 10),
        };
        var net = PlanarNetwork.Build(curves);
        Assert.Equal(2, net.Faces.Count);
        var outer = net.FindFace(new Vec2(5, 5))!;
        Assert.Equal(900 - 100, outer.Area, 6);
        Assert.Single(outer.Region.Holes);
        Assert.Equal(100, net.FindFace(new Vec2(15, 15))!.Area, 6);

        curves.Add(L(0, 15, 10, 15, 99));
        var net2 = PlanarNetwork.Build(curves);
        Assert.Equal(2, net2.Faces.Count);
        Assert.Contains(net2.Diagnostics, d => d.Kind == DiagnosticKind.Bridge);
    }

    [Fact]
    public void Lens_TwoArcsBetweenSameNodes()
    {
        var up = CurveFactory.FromThreePoints(new Vec2(0, 0), new Vec2(5, 2), new Vec2(10, 0));
        var down = CurveFactory.FromThreePoints(new Vec2(0, 0), new Vec2(5, -2), new Vec2(10, 0));
        var net = Net(new InputCurve(up, 0), new InputCurve(down, 1), L(0, 0, 10, 0, 2));
        Assert.Equal(2, net.Faces.Count);
    }

    [Fact]
    public void UnionBoundary_TwoAdjacentFaces()
    {
        var net = Net(L(0, 0, 20, 0), L(20, 0, 20, 10), L(20, 10, 0, 10), L(0, 10, 0, 0), L(10, 0, 10, 10));
        var ids = net.Faces.Select(f => f.Id).ToHashSet();
        var rings = net.UnionBoundary(ids);
        Assert.Single(rings);
        var ring = new Ring(rings[0].Select(e => e.Curve).ToList());
        Assert.Equal(200, ring.SignedArea, 6);
    }

    [Fact]
    public void TouchingAtCorner_TwoFaces()
    {
        var net = Net(L(0, 0, 10, 0), L(10, 0, 10, 10), L(10, 10, 0, 10), L(0, 10, 0, 0),
                      L(10, 10, 20, 10), L(20, 10, 20, 20), L(20, 20, 10, 20), L(10, 20, 10, 10));
        Assert.Equal(2, net.Faces.Count);
        Assert.All(net.Faces, f => Assert.Equal(100, f.Area, 6));
    }

    [Fact]
    public void PoleOfInaccessibility_IsInside()
    {
        // Lote em "L".
        var ring = new Ring(new Curve2[]
        {
            new LineCurve(new(0,0), new(30,0)), new LineCurve(new(30,0), new(30,5)), new LineCurve(new(30,5), new(5,5)),
            new LineCurve(new(5,5), new(5,30)), new LineCurve(new(5,30), new(0,30)), new LineCurve(new(0,30), new(0,0)),
        });
        var region = new Region(ring);
        var p = region.InteriorPoint();
        Assert.True(region.Contains(p));
    }

    [Fact]
    public void ClipByConvex_TriangleArea()
    {
        var square = new List<Vec2> { new(0, 0), new(10, 0), new(10, 10), new(0, 10) };
        var tri = new List<Vec2> { new(5, 5), new(15, 5), new(5, 15) };
        var clip = PolygonUtil.ClipByConvex(square, tri);
        // Interseção = quadrado [5,10]x[5,10] (x+y ≤ 20 sempre vale ali).
        Assert.Equal(25, Math.Abs(PolygonUtil.SignedArea(clip)), 9);
    }
}
