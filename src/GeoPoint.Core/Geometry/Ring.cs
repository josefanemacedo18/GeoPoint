namespace GeoPoint.Core.Geometry;

/// <summary>Anel fechado formado por curvas contíguas (retas e arcos).</summary>
public sealed class Ring
{
    private List<Vec2>? _poly;
    private const double DensifyError = 1e-4;

    public Ring(IReadOnlyList<Curve2> edges)
    {
        if (edges.Count == 0) throw new ArgumentException("Anel vazio.", nameof(edges));
        Edges = edges;
    }

    public IReadOnlyList<Curve2> Edges { get; }

    public int Count => Edges.Count;

    public IEnumerable<Vec2> Vertices => Edges.Select(e => e.Start);

    public double SignedArea
    {
        get
        {
            var o = Edges[0].Start;
            double s = 0;
            foreach (var e in Edges) s += e.GreenIntegral(o);
            return s;
        }
    }

    public double Area => Math.Abs(SignedArea);
    public bool IsCounterClockwise => SignedArea > 0;
    public double Perimeter => Edges.Sum(e => e.Length);

    public BBox Bounds
    {
        get
        {
            var b = BBox.Empty;
            foreach (var e in Edges) b = b.Union(e.Bounds);
            return b;
        }
    }

    public Ring Reversed()
    {
        var list = new List<Curve2>(Edges.Count);
        for (int i = Edges.Count - 1; i >= 0; i--) list.Add(Edges[i].Reversed());
        return new Ring(list);
    }

    public Ring EnsureOrientation(bool counterClockwise) => IsCounterClockwise == counterClockwise ? this : Reversed();

    public Ring RotateStart(int index)
    {
        int n = Edges.Count;
        index = ((index % n) + n) % n;
        var list = new List<Curve2>(n);
        for (int i = 0; i < n; i++) list.Add(Edges[(index + i) % n]);
        return new Ring(list);
    }

    /// <summary>Polígono aproximado (sem repetir o primeiro ponto no final).</summary>
    public List<Vec2> ToPolygon(double maxChordError = DensifyError)
    {
        if (maxChordError == DensifyError && _poly is not null) return _poly;
        var pts = new List<Vec2>();
        foreach (var e in Edges)
        {
            var d = e.Densify(maxChordError);
            for (int i = 0; i < d.Count - 1; i++) pts.Add(d[i]);
        }
        if (maxChordError == DensifyError) _poly = pts;
        return pts;
    }

    public bool Contains(Vec2 p) => PolygonUtil.PointInPolygon(ToPolygon(), p);

    /// <summary>Verifica se as extremidades são contíguas (dentro da tolerância).</summary>
    public bool IsClosed(double tol)
    {
        for (int i = 0; i < Edges.Count; i++)
        {
            var a = Edges[i].End;
            var b = Edges[(i + 1) % Edges.Count].Start;
            if (!a.AlmostEquals(b, tol)) return false;
        }
        return true;
    }
}

/// <summary>Região: contorno externo (anti-horário) e furos (horários).</summary>
public sealed class Region
{
    public Region(Ring outer, IReadOnlyList<Ring>? holes = null)
    {
        Outer = outer.EnsureOrientation(true);
        Holes = (holes ?? Array.Empty<Ring>()).Select(h => h.EnsureOrientation(false)).ToList();
    }

    public Ring Outer { get; }
    public IReadOnlyList<Ring> Holes { get; }

    public double Area => Outer.Area - Holes.Sum(h => h.Area);
    public double Perimeter => Outer.Perimeter + Holes.Sum(h => h.Perimeter);
    public BBox Bounds => Outer.Bounds;

    public bool Contains(Vec2 p) => Outer.Contains(p) && !Holes.Any(h => h.Contains(p));

    public IEnumerable<Ring> AllRings()
    {
        yield return Outer;
        foreach (var h in Holes) yield return h;
    }

    /// <summary>Ponto interno bem afastado das bordas (polo de inacessibilidade).</summary>
    public Vec2 InteriorPoint()
    {
        var rings = new List<List<Vec2>> { Outer.ToPolygon() };
        rings.AddRange(Holes.Select(h => h.ToPolygon()));
        return PolygonUtil.PoleOfInaccessibility(rings, out _);
    }
}

public static class PolygonUtil
{
    public static bool PointInPolygon(IReadOnlyList<Vec2> poly, Vec2 p)
    {
        bool inside = false;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var a = poly[i]; var b = poly[j];
            if ((a.Y > p.Y) != (b.Y > p.Y))
            {
                double x = (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X;
                if (p.X < x) inside = !inside;
            }
        }
        return inside;
    }

    public static double SignedArea(IReadOnlyList<Vec2> poly)
    {
        if (poly.Count < 3) return 0;
        var o = poly[0];
        double s = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i] - o; var b = poly[(i + 1) % poly.Count] - o;
            s += a.Cross(b);
        }
        return s / 2;
    }

    public static double SegmentDistance(Vec2 p, Vec2 a, Vec2 b)
    {
        var d = b - a;
        double l2 = d.LengthSquared;
        double t = l2 > 0 ? Math.Clamp((p - a).Dot(d) / l2, 0, 1) : 0;
        return p.DistanceTo(a + d * t);
    }

    private static double SignedDistance(List<List<Vec2>> rings, Vec2 p)
    {
        bool inside = false;
        double min = double.PositiveInfinity;
        foreach (var r in rings)
        {
            int n = r.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = r[i]; var b = r[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
                min = Math.Min(min, SegmentDistance(p, a, b));
            }
        }
        return inside ? min : -min;
    }

    /// <summary>Algoritmo "polylabel": ponto interno mais distante das bordas.</summary>
    public static Vec2 PoleOfInaccessibility(List<List<Vec2>> rings, out double distance, double precision = 0)
    {
        var bb = BBox.Of(rings[0]);
        double w = bb.Width, h = bb.Height;
        double cellSize = Math.Min(w, h);
        if (cellSize <= 0)
        {
            distance = 0;
            return rings[0].Count > 0 ? rings[0][0] : Vec2.Zero;
        }
        if (precision <= 0) precision = Math.Max(Math.Max(w, h) / 2000.0, 1e-4);

        var queue = new PriorityQueue<(Vec2 c, double h, double d, double max), double>();
        void Push(Vec2 c, double half)
        {
            double d = SignedDistance(rings, c);
            double max = d + half * Math.Sqrt(2);
            queue.Enqueue((c, half, d, max), -max);
        }

        double halfSize = cellSize / 2;
        for (double x = bb.MinX; x < bb.MaxX; x += cellSize)
            for (double y = bb.MinY; y < bb.MaxY; y += cellSize)
                Push(new Vec2(x + halfSize, y + halfSize), halfSize);

        // Melhor candidato inicial: centroide do polígono externo.
        var best = Centroid(rings[0]);
        double bestD = SignedDistance(rings, best);
        var bc = bb.Center;
        double bcd = SignedDistance(rings, bc);
        if (bcd > bestD) { best = bc; bestD = bcd; }

        int guard = 0;
        while (queue.Count > 0 && guard++ < 200000)
        {
            var cell = queue.Dequeue();
            if (cell.d > bestD) { best = cell.c; bestD = cell.d; }
            if (cell.max - bestD <= precision) continue;
            double hh = cell.h / 2;
            Push(new Vec2(cell.c.X - hh, cell.c.Y - hh), hh);
            Push(new Vec2(cell.c.X + hh, cell.c.Y - hh), hh);
            Push(new Vec2(cell.c.X - hh, cell.c.Y + hh), hh);
            Push(new Vec2(cell.c.X + hh, cell.c.Y + hh), hh);
        }
        distance = bestD;
        return best;
    }

    public static Vec2 Centroid(IReadOnlyList<Vec2> poly)
    {
        if (poly.Count == 0) return Vec2.Zero;
        var o = poly[0];
        double a = 0, cx = 0, cy = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i] - o; var q = poly[(i + 1) % poly.Count] - o;
            double f = p.Cross(q);
            a += f;
            cx += (p.X + q.X) * f;
            cy += (p.Y + q.Y) * f;
        }
        if (Math.Abs(a) < 1e-15)
        {
            double sx = 0, sy = 0;
            foreach (var p in poly) { sx += p.X; sy += p.Y; }
            return new Vec2(sx / poly.Count, sy / poly.Count);
        }
        return new Vec2(cx / (3 * a) + o.X, cy / (3 * a) + o.Y);
    }

    /// <summary>Recorta um polígono qualquer por um polígono convexo (Sutherland–Hodgman). A área resultante é exata.</summary>
    public static List<Vec2> ClipByConvex(IReadOnlyList<Vec2> subject, IReadOnlyList<Vec2> convexClip)
    {
        var output = new List<Vec2>(subject);
        bool ccw = SignedArea(convexClip) > 0;
        for (int i = 0; i < convexClip.Count && output.Count > 0; i++)
        {
            var a = convexClip[i];
            var b = convexClip[(i + 1) % convexClip.Count];
            var input = output;
            output = new List<Vec2>(input.Count + 4);
            bool Inside(Vec2 p) => ccw ? (b - a).Cross(p - a) >= 0 : (b - a).Cross(p - a) <= 0;
            for (int j = 0; j < input.Count; j++)
            {
                var cur = input[j];
                var prev = input[(j + input.Count - 1) % input.Count];
                bool ci = Inside(cur), pi = Inside(prev);
                if (ci)
                {
                    if (!pi) output.Add(LineIntersect(prev, cur, a, b));
                    output.Add(cur);
                }
                else if (pi) output.Add(LineIntersect(prev, cur, a, b));
            }
        }
        return output;
    }

    private static Vec2 LineIntersect(Vec2 p1, Vec2 p2, Vec2 q1, Vec2 q2)
    {
        var r = p2 - p1; var s = q2 - q1;
        double den = r.Cross(s);
        if (Math.Abs(den) < 1e-300) return p1;
        double t = (q1 - p1).Cross(s) / den;
        return p1 + r * t;
    }
}
