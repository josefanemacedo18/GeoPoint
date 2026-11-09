namespace GeoPoint.Core.Geometry;

/// <summary>Par de parâmetros de interseção (t na curva 1, u na curva 2).</summary>
public readonly record struct ParamPair(double T1, double T2);

public static class Intersections
{
    /// <summary>
    /// Interseções entre duas curvas com tolerância de distância (metros).
    /// Para trechos sobrepostos (colineares/cocirculares) devolve as extremidades da sobreposição.
    /// </summary>
    public static List<ParamPair> Intersect(Curve2 c1, Curve2 c2, double tol)
    {
        var result = new List<ParamPair>();
        if (!c1.Bounds.Expand(tol).Intersects(c2.Bounds.Expand(tol))) return result;

        switch (c1, c2)
        {
            case (LineCurve l1, LineCurve l2): LineLine(l1, l2, tol, result); break;
            case (LineCurve l, ArcCurve a): LineArc(l, a, tol, result, swap: false); break;
            case (ArcCurve a, LineCurve l): LineArc(l, a, tol, result, swap: true); break;
            case (ArcCurve a1, ArcCurve a2): ArcArc(a1, a2, tol, result); break;
        }
        return result;
    }

    private static double ParamTol(Curve2 c, double tol) => c.Length > 0 ? tol / c.Length : 0;

    private static bool InRange(double t, double ptol) => t >= -ptol && t <= 1 + ptol;

    private static void LineLine(LineCurve l1, LineCurve l2, double tol, List<ParamPair> res)
    {
        var d1 = l1.B - l1.A;
        var d2 = l2.B - l2.A;
        double len1 = d1.Length, len2 = d2.Length;
        if (len1 == 0 || len2 == 0) return;
        double denom = d1.Cross(d2);
        double p1 = tol / len1, p2 = tol / len2;

        if (Math.Abs(denom) <= 1e-12 * len1 * len2)
        {
            // Paralelas: verifica colinearidade.
            double dist = Math.Abs((l2.A - l1.A).Cross(d1)) / len1;
            if (dist > tol) return;
            AddOverlapEndpoints(l1, l2, p1, p2, res);
            return;
        }

        var w = l2.A - l1.A;
        double t = w.Cross(d2) / denom;
        double u = w.Cross(d1) / denom;
        if (InRange(t, p1) && InRange(u, p2))
        {
            // Confirma a distância real entre os pontos (evita falsos positivos em ângulos muito fechados).
            var q1 = l1.PointAt(Math.Clamp(t, 0, 1));
            var q2 = l2.PointAt(Math.Clamp(u, 0, 1));
            if (q1.DistanceTo(q2) <= tol * 2)
                res.Add(new ParamPair(Math.Clamp(t, 0, 1), Math.Clamp(u, 0, 1)));
        }
    }

    /// <summary>Extremidades de uma curva que estão sobre a outra (curvas colineares ou cocirculares).</summary>
    private static void AddOverlapEndpoints(Curve2 c1, Curve2 c2, double p1, double p2, List<ParamPair> res)
    {
        foreach (var (pt, isStart) in new[] { (c2.Start, true), (c2.End, false) })
        {
            double t = c1.ParamOf(pt);
            if (InRange(t, p1)) res.Add(new ParamPair(Math.Clamp(t, 0, 1), isStart ? 0 : 1));
        }
        foreach (var (pt, isStart) in new[] { (c1.Start, true), (c1.End, false) })
        {
            double u = c2.ParamOf(pt);
            if (InRange(u, p2)) res.Add(new ParamPair(isStart ? 0 : 1, Math.Clamp(u, 0, 1)));
        }
    }

    private static void LineArc(LineCurve l, ArcCurve arc, double tol, List<ParamPair> res, bool swap)
    {
        var d = l.B - l.A;
        double len = d.Length;
        if (len == 0) return;
        var f = l.A - arc.Center;
        double a = d.Dot(d);
        double b = 2 * f.Dot(d);
        double c = f.Dot(f) - arc.Radius * arc.Radius;
        double disc = b * b - 4 * a * c;

        var ts = new List<double>(2);
        double distCenterLine = Math.Abs(f.Cross(d)) / len;
        if (disc < 0)
        {
            if (Math.Abs(distCenterLine - arc.Radius) <= tol) ts.Add(-b / (2 * a));
        }
        else
        {
            double sq = Math.Sqrt(disc);
            ts.Add((-b - sq) / (2 * a));
            if (sq > 0) ts.Add((-b + sq) / (2 * a));
        }

        double pl = ParamTol(l, tol), pa = ParamTol(arc, tol);
        foreach (var t in ts)
        {
            if (!InRange(t, pl)) continue;
            var q = l.PointAt(t);
            double u = arc.ParamOf(q);
            if (!InRange(u, pa)) continue;
            var qa = arc.PointAt(Math.Clamp(u, 0, 1));
            if (qa.DistanceTo(l.PointAt(Math.Clamp(t, 0, 1))) > tol * 2) continue;
            double tc = Math.Clamp(t, 0, 1), uc = Math.Clamp(u, 0, 1);
            res.Add(swap ? new ParamPair(uc, tc) : new ParamPair(tc, uc));
        }
    }

    private static void ArcArc(ArcCurve a1, ArcCurve a2, double tol, List<ParamPair> res)
    {
        var dv = a2.Center - a1.Center;
        double d = dv.Length;
        double r1 = a1.Radius, r2 = a2.Radius;
        double p1 = ParamTol(a1, tol), p2 = ParamTol(a2, tol);

        if (d <= tol && Math.Abs(r1 - r2) <= tol)
        {
            AddOverlapEndpoints(a1, a2, p1, p2, res);
            return;
        }
        if (d > r1 + r2 + tol) return;
        if (d < Math.Abs(r1 - r2) - tol) return;
        if (d == 0) return;

        double aa = (r1 * r1 - r2 * r2 + d * d) / (2 * d);
        double h2 = r1 * r1 - aa * aa;
        double h = h2 > 0 ? Math.Sqrt(h2) : 0;
        var ex = dv / d;
        var basePt = a1.Center + ex * aa;
        var pts = h > 0 ? new[] { basePt + ex.PerpLeft * h, basePt - ex.PerpLeft * h } : new[] { basePt };
        foreach (var q in pts)
        {
            double t = a1.ParamOf(q), u = a2.ParamOf(q);
            if (!InRange(t, p1) || !InRange(u, p2)) continue;
            double tc = Math.Clamp(t, 0, 1), uc = Math.Clamp(u, 0, 1);
            if (a1.PointAt(tc).DistanceTo(a2.PointAt(uc)) > tol * 2) continue;
            res.Add(new ParamPair(tc, uc));
        }
    }
}
