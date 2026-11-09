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

    /// <summary>
    /// Divide uma divisa do lote em trechos (comprimentos a partir do início da divisa no sentido da descrição).
    /// Os dados da divisa (confrontante, testada, lado) são copiados para cada trecho. Devolve os pontos inseridos.
    /// </summary>
    public static List<Vec2> SplitEdge(LotRecord lot, Curve2 edge, IEnumerable<double> lengths)
    {
        var pts = PointsAlong(edge, lengths);
        if (pts.Count == 0) return pts;
        var oldInfo = lot.EdgeInfos.Where(i => edge.DistanceTo(new Vec2(i.MX, i.MY)) <= 0.01).ToList();
        foreach (var p in pts)
            if (!lot.ExtraVertices.Any(v => v.ToVec().DistanceTo(p) <= Tolerance)) lot.ExtraVertices.Add(new PointDto { X = p.X, Y = p.Y });
        Apply(lot);
        if (oldInfo.Count > 0)
        {
            var template = oldInfo[0];
            foreach (var i in oldInfo) lot.EdgeInfos.Remove(i);
            foreach (var e in lot.Outer.Select(x => x.ToCurve()).Where(c => edge.DistanceTo(c.Mid) <= 0.01))
                lot.EdgeInfos.Add(template.CloneAt(e.Mid));
        }
        return pts;
    }

    /// <summary>Remove um vértice inserido sobre uma divisa, juntando os dois trechos. Devolve false se não for um vértice inserido.</summary>
    public static bool Merge(LotRecord lot, Vec2 p)
    {
        int idx = lot.ExtraVertices.FindIndex(v => v.ToVec().DistanceTo(p) <= Tolerance);
        if (idx < 0) return false;
        int n = lot.Outer.Count;
        int iIn = -1;
        for (int i = 0; i < n; i++)
            if (new Vec2(lot.Outer[i].X2, lot.Outer[i].Y2).DistanceTo(p) <= Tolerance && new Vec2(lot.Outer[(i + 1) % n].X1, lot.Outer[(i + 1) % n].Y1).DistanceTo(p) <= Tolerance) { iIn = i; break; }
        lot.ExtraVertices.RemoveAt(idx);
        if (iIn < 0) return true;
        int iOut = (iIn + 1) % n;
        var a = lot.Outer[iIn].ToCurve();
        var b = lot.Outer[iOut].ToCurve();
        Curve2 joined;
        if (!a.IsArc && !b.IsArc) joined = new LineCurve(a.Start, b.End);
        else joined = CurveFactory.FromThreePoints(a.Start, p, b.End);
        var infoA = ProjectInfoAt(lot, a.Mid);
        var infoB = ProjectInfoAt(lot, b.Mid);
        if (infoA is not null) lot.EdgeInfos.Remove(infoA);
        if (infoB is not null) lot.EdgeInfos.Remove(infoB);
        var keep = infoA ?? infoB;
        if (keep is not null) lot.EdgeInfos.Add(keep.CloneAt(joined.Mid));
        var src = lot.Outer[iIn].Src.Union(lot.Outer[iOut].Src).ToList();
        var dto = EdgeDto.FromCurve(joined, src);
        if (iOut == 0)
        {
            lot.Outer[iIn] = dto;
            lot.Outer.RemoveAt(0);
        }
        else
        {
            lot.Outer[iIn] = dto;
            lot.Outer.RemoveAt(iOut);
        }
        return true;
    }

    private static EdgeInfo? ProjectInfoAt(LotRecord lot, Vec2 mid) =>
        lot.EdgeInfos.Where(i => new Vec2(i.MX, i.MY).DistanceTo(mid) <= 0.01).OrderBy(i => new Vec2(i.MX, i.MY).DistanceTo(mid)).FirstOrDefault();
}
