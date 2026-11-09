using GeoPoint.Core.Geometry;
using GeoPoint.ThirdParty.Clipper2;

namespace GeoPoint.Core.Operations;

/// <summary>Operações booleanas aproximadas (Clipper2, precisão de 0,1 mm, arcos densificados a 1 mm).</summary>
public static class Overlay
{
    public const int Precision = 4;
    public const double ChordError = 0.001;

    public static PathsD ToPaths(Region r, double chordError = ChordError)
    {
        var paths = new PathsD();
        foreach (var ring in r.AllRings())
        {
            var p = new PathD();
            foreach (var v in ring.ToPolygon(chordError)) p.Add(new PointD(v.X, v.Y));
            paths.Add(p);
        }
        return paths;
    }

    public static PathD ToPath(IEnumerable<Vec2> pts)
    {
        var p = new PathD();
        foreach (var v in pts) p.Add(new PointD(v.X, v.Y));
        return p;
    }

    public static List<Vec2> ToVecs(PathD p) => p.Select(q => new Vec2(q.x, q.y)).ToList();

    public static PathsD Union(PathsD subject, FillRule fill) => Clipper.BooleanOp(ClipType.Union, subject, null, fill, Precision);
    public static PathsD Intersect(PathsD a, PathsD b, FillRule fill) => Clipper.BooleanOp(ClipType.Intersection, a, b, fill, Precision);
    public static PathsD Difference(PathsD a, PathsD b, FillRule fill) => Clipper.BooleanOp(ClipType.Difference, a, b, fill, Precision);

    public static double IntersectionArea(Region a, Region b)
    {
        if (!a.Bounds.Intersects(b.Bounds)) return 0;
        var res = Intersect(ToPaths(a), ToPaths(b), FillRule.EvenOdd);
        return Math.Abs(Clipper.Area(res));
    }

    public static double Area(PathsD paths) => Math.Abs(Clipper.Area(paths));

    /// <summary>Área da união de várias regiões.</summary>
    public static double UnionArea(IEnumerable<Region> regions)
    {
        // Cada região é normalizada (EvenOdd, saída com orientação consistente) e a união usa NonZero.
        var normalized = new PathsD();
        foreach (var r in regions) normalized.AddRange(Union(ToPaths(r), FillRule.EvenOdd));
        var res = Union(normalized, FillRule.NonZero);
        return Area(res);
    }

    /// <summary>Converte resultado do Clipper em anéis (externos anti-horários, furos horários).</summary>
    public static List<List<Vec2>> ToRings(PathsD paths) => paths.Select(ToVecs).Where(r => r.Count >= 3).ToList();
}
