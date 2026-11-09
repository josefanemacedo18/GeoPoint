using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;

namespace GeoPoint.Core.Topology;

/// <summary>Insere vértices sobre as divisas (ex.: onde muda o confrontante ao longo de uma divisa).</summary>
public static class EdgeSplitter
{
    public const double Tolerance = 0.002;

    /// <summary>Divide as arestas nos pontos que estão sobre elas (fora das extremidades). Preserva a origem (Src).</summary>
    public static List<EdgeDto> SplitAt(IReadOnlyList<EdgeDto> edges, IEnumerable<Vec2> points)
    {
        var pts = points.ToList();
        var res = new List<EdgeDto>();
        foreach (var e in edges)
        {
            var c = e.ToCurve();
            double len = c.Length;
            var ts = new List<double>();
            foreach (var p in pts)
            {
                var q = c.ClosestPoint(p, out double t);
                if (q.DistanceTo(p) > Tolerance) continue;
                if (t * len <= Tolerance || (1 - t) * len <= Tolerance) continue;
                if (ts.Any(x => Math.Abs(x - t) * len <= Tolerance)) continue;
                ts.Add(t);
            }
            if (ts.Count == 0) { res.Add(e); continue; }
            ts.Sort();
            ts.Insert(0, 0);
            ts.Add(1);
            for (int i = 0; i < ts.Count - 1; i++)
            {
                var piece = c.SubCurve(ts[i], ts[i + 1]);
                // Extremidades exatas nos pontos informados (evita deriva numérica).
                var a = i == 0 ? c.Start : c.PointAt(ts[i]);
                var b = i == ts.Count - 2 ? c.End : c.PointAt(ts[i + 1]);
                res.Add(EdgeDto.FromCurve(piece.WithEndpoints(a, b), e.Src));
            }
        }
        return res;
    }

    /// <summary>Pontos ao longo de uma curva, medidos a partir do início, para as distâncias acumuladas informadas.</summary>
    public static List<Vec2> PointsAlong(Curve2 c, IEnumerable<double> lengths)
    {
        var res = new List<Vec2>();
        double acc = 0, total = c.Length;
        foreach (var l in lengths)
        {
            acc += l;
            if (acc <= Tolerance || acc >= total - Tolerance) continue;
            res.Add(c.PointAt(acc / total));
        }
        return res;
    }

    /// <summary>Reaplica os vértices extras do lote à sua geometria atual.</summary>
    public static void Apply(LotRecord lot)
    {
        if (lot.ExtraVertices.Count == 0) return;
        var pts = lot.ExtraVertices.Select(p => p.ToVec()).ToList();
        lot.Outer = SplitAt(lot.Outer, pts);
        // Mantém só os vértices que continuam sobre o contorno.
        lot.ExtraVertices = lot.ExtraVertices
            .Where(p => lot.Outer.Any(e => new Vec2(e.X1, e.Y1).DistanceTo(p.ToVec()) <= Tolerance)).ToList();
    }
}
