using GeoPoint.Core.Geometry;
using GeoPoint.ThirdParty.Clipper2;

namespace GeoPoint.Core.Operations;

public sealed class SetbackResult
{
    public double Area { get; init; }
    /// <summary>Anéis da área edificável (externos anti-horários, furos horários).</summary>
    public List<List<Vec2>> Rings { get; init; } = new();
    public List<string> Warnings { get; } = new();
}

/// <summary>
/// Área edificável: pontos do lote cuja distância a cada divisa é maior ou igual ao respectivo recuo.
/// Calculada como Lote − ⋃ faixa(divisa, recuo). Cantos reentrantes ficam arredondados, como manda a definição.
/// </summary>
public static class Setbacks
{
    public static SetbackResult Compute(Region lot, IReadOnlyList<(Curve2 Edge, double Setback)> edges)
    {
        var strips = new PathsD();
        foreach (var (edge, d) in edges)
        {
            if (d <= 0) continue;
            var open = new PathsD { Overlay.ToPath(edge.Densify(Overlay.ChordError)) };
            var buf = Clipper.InflatePaths(open, d, JoinType.Round, EndType.Round, 2.0, Overlay.Precision, 0.0005);
            strips.AddRange(buf);
        }
        var lotPaths = Overlay.ToPaths(lot);
        PathsD result;
        if (strips.Count == 0) result = Overlay.Union(lotPaths, FillRule.EvenOdd);
        else
        {
            var stripUnion = Overlay.Union(strips, FillRule.NonZero);
            result = Overlay.Difference(lotPaths, stripUnion, FillRule.EvenOdd);
        }
        result = Clipper.SimplifyPaths(result, 0.0002, true);
        var res = new SetbackResult { Area = Overlay.Area(result), Rings = Overlay.ToRings(result) };
        if (res.Rings.Count == 0) res.Warnings.Add("Os recuos informados eliminam toda a área edificável.");
        else if (res.Rings.Count(r => PolygonUtil.SignedArea(r) > 0) > 1) res.Warnings.Add("A área edificável ficou dividida em mais de uma parte.");
        return res;
    }
}
