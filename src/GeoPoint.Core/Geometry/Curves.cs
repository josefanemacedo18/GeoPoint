namespace GeoPoint.Core.Geometry;

/// <summary>Curva 2D parametrizada em t ∈ [0,1]: segmento de reta ou arco circular.</summary>
public abstract class Curve2
{
    public abstract Vec2 Start { get; }
    public abstract Vec2 End { get; }
    public abstract double Length { get; }
    public abstract BBox Bounds { get; }
    public abstract bool IsArc { get; }

    public Vec2 Mid => PointAt(0.5);

    public abstract Vec2 PointAt(double t);

    /// <summary>Tangente unitária no sentido do percurso.</summary>
    public abstract Vec2 TangentAt(double t);

    /// <summary>Curvatura com sinal (positiva = curva para a esquerda no sentido do percurso).</summary>
    public abstract double SignedCurvature { get; }

    /// <summary>Parâmetro da projeção do ponto (pode ficar fora de [0,1]).</summary>
    public abstract double ParamOf(Vec2 p);

    public abstract Curve2 SubCurve(double t0, double t1);
    public abstract Curve2 Reversed();

    /// <summary>½∮(x dy − y dx) ao longo da curva, com coordenadas relativas a <paramref name="origin"/>.</summary>
    public abstract double GreenIntegral(Vec2 origin);

    /// <summary>Pontos do início ao fim (inclusive), com erro de corda máximo indicado.</summary>
    public abstract List<Vec2> Densify(double maxChordError);

    public Vec2 ClosestPoint(Vec2 p, out double t)
    {
        t = Math.Clamp(ParamOf(p), 0.0, 1.0);
        var q = PointAt(t);
        // Para arcos o ponto fora do vão é comparado com as extremidades.
        double ds = p.DistanceTo(Start), de = p.DistanceTo(End), dq = p.DistanceTo(q);
        if (ds < dq && ds <= de) { t = 0; return Start; }
        if (de < dq) { t = 1; return End; }
        return q;
    }

    public double DistanceTo(Vec2 p) => p.DistanceTo(ClosestPoint(p, out _));

    /// <summary>Cria uma curva com novas extremidades preservando a forma (arcos passam pelo ponto médio original).</summary>
    public abstract Curve2 WithEndpoints(Vec2 newStart, Vec2 newEnd);
}

public sealed class LineCurve : Curve2
{
    public LineCurve(Vec2 a, Vec2 b) { A = a; B = b; }

    public Vec2 A { get; }
    public Vec2 B { get; }

    public override Vec2 Start => A;
    public override Vec2 End => B;
    public override double Length => A.DistanceTo(B);
    public override bool IsArc => false;
    public override BBox Bounds => BBox.Empty.Include(A).Include(B);
    public Vec2 Direction => (B - A).Normalized();

    public override Vec2 PointAt(double t) => Vec2.Lerp(A, B, t);
    public override Vec2 TangentAt(double t) => Direction;
    public override double SignedCurvature => 0;

    public override double ParamOf(Vec2 p)
    {
        var d = B - A;
        double l2 = d.LengthSquared;
        return l2 > 0 ? (p - A).Dot(d) / l2 : 0;
    }

    public override Curve2 SubCurve(double t0, double t1) => new LineCurve(PointAt(t0), PointAt(t1));
    public override Curve2 Reversed() => new LineCurve(B, A);

    public override double GreenIntegral(Vec2 o)
    {
        var a = A - o; var b = B - o;
        return 0.5 * a.Cross(b);
    }

    public override List<Vec2> Densify(double maxChordError) => new() { A, B };

    public override Curve2 WithEndpoints(Vec2 newStart, Vec2 newEnd) => new LineCurve(newStart, newEnd);

    public override string ToString() => $"Reta {A} → {B}";
}

public sealed class ArcCurve : Curve2
{
    /// <param name="sweep">Ângulo central com sinal (positivo = anti-horário).</param>
    public ArcCurve(Vec2 center, double radius, double startAngle, double sweep)
    {
        if (radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (sweep == 0) throw new ArgumentOutOfRangeException(nameof(sweep));
        Center = center; Radius = radius; StartAngle = startAngle; Sweep = sweep;
    }

    public Vec2 Center { get; }
    public double Radius { get; }
    public double StartAngle { get; }
    public double Sweep { get; }
    public double EndAngle => StartAngle + Sweep;
    public bool IsCounterClockwise => Sweep > 0;

    public override bool IsArc => true;
    public override Vec2 Start => PointAt(0);
    public override Vec2 End => PointAt(1);
    public override double Length => Radius * Math.Abs(Sweep);
    public double Chord => Start.DistanceTo(End);

    public override Vec2 PointAt(double t)
    {
        double a = StartAngle + t * Sweep;
        return new Vec2(Center.X + Radius * Math.Cos(a), Center.Y + Radius * Math.Sin(a));
    }

    public override Vec2 TangentAt(double t)
    {
        double a = StartAngle + t * Sweep;
        double s = Math.Sign(Sweep);
        return new Vec2(-Math.Sin(a) * s, Math.Cos(a) * s);
    }

    public override double SignedCurvature => Math.Sign(Sweep) / Radius;

    public override double ParamOf(Vec2 p)
    {
        var v = p - Center;
        if (v.LengthSquared == 0) return 0;
        double phi = v.Angle;
        double absSweep = Math.Abs(Sweep);
        double delta = Sweep > 0 ? AngleUtil.Norm2Pi(phi - StartAngle) : AngleUtil.Norm2Pi(StartAngle - phi);
        if (delta <= absSweep) return delta / absSweep;
        // Fora do vão: escolhe a extremidade angularmente mais próxima.
        double after = delta - absSweep;
        double before = AngleUtil.TwoPi - delta;
        return after <= before ? delta / absSweep : -before / absSweep;
    }

    public override Curve2 SubCurve(double t0, double t1) =>
        new ArcCurve(Center, Radius, StartAngle + t0 * Sweep, (t1 - t0) * Sweep);

    public override Curve2 Reversed() => new ArcCurve(Center, Radius, StartAngle + Sweep, -Sweep);

    public override BBox Bounds
    {
        get
        {
            var b = BBox.Empty.Include(Start).Include(End);
            // Pontos extremos dos eixos que estejam dentro do vão.
            for (int k = 0; k < 4; k++)
            {
                double ang = k * Math.PI / 2;
                var p = new Vec2(Center.X + Radius * Math.Cos(ang), Center.Y + Radius * Math.Sin(ang));
                double t = ParamOf(p);
                if (t >= 0 && t <= 1) b = b.Include(p);
            }
            return b;
        }
    }

    public override double GreenIntegral(Vec2 o)
    {
        double cx = Center.X - o.X, cy = Center.Y - o.Y, r = Radius;
        double a0 = StartAngle, a1 = StartAngle + Sweep;
        return 0.5 * (r * r * Sweep + cx * r * (Math.Sin(a1) - Math.Sin(a0)) - cy * r * (Math.Cos(a1) - Math.Cos(a0)));
    }

    public int SegmentsFor(double maxChordError)
    {
        double e = Math.Max(maxChordError, 1e-9);
        double absSweep = Math.Abs(Sweep);
        int n;
        if (e >= Radius) n = (int)Math.Ceiling(absSweep / (Math.PI / 2));
        else
        {
            double step = 2 * Math.Acos(1 - e / Radius);
            n = step > 0 ? (int)Math.Ceiling(absSweep / step) : 4096;
        }
        return Math.Clamp(n, 1, 4096);
    }

    public override List<Vec2> Densify(double maxChordError)
    {
        int n = SegmentsFor(maxChordError);
        var pts = new List<Vec2>(n + 1);
        for (int i = 0; i <= n; i++) pts.Add(PointAt((double)i / n));
        return pts;
    }

    public override Curve2 WithEndpoints(Vec2 newStart, Vec2 newEnd)
    {
        return CurveFactory.FromThreePoints(newStart, Mid, newEnd);
    }

    public override string ToString() => $"Arco C{Center} R={Radius:0.###} {Start} → {End}";
}

public static class CurveFactory
{
    /// <summary>
    /// Cria arco passando por início, ponto intermediário e fim. Se os pontos forem colineares, devolve uma reta.
    /// </summary>
    public static Curve2 FromThreePoints(Vec2 s, Vec2 m, Vec2 e)
    {
        var arc = TryArcFromThreePoints(s, m, e);
        return arc is null ? new LineCurve(s, e) : arc;
    }

    public static ArcCurve? TryArcFromThreePoints(Vec2 s, Vec2 m, Vec2 e)
    {
        double scale = Math.Max(Math.Max(s.DistanceTo(m), m.DistanceTo(e)), s.DistanceTo(e));
        if (scale == 0) return null;
        // Teste de colinearidade relativo (flecha muito pequena em relação ao tamanho).
        double cross = (m - s).Cross(e - m);
        if (Math.Abs(cross) <= 1e-12 * scale * scale) return null;
        // Circuncentro calculado com origem deslocada para o ponto inicial (melhor precisão numérica).
        var o = s;
        double a2x = 0, a2y = 0, b2x = m.X - o.X, b2y = m.Y - o.Y, c2x = e.X - o.X, c2y = e.Y - o.Y;
        double d = 2 * (a2x * (b2y - c2y) + b2x * (c2y - a2y) + c2x * (a2y - b2y));
        if (d == 0) return null;
        double b2 = b2x * b2x + b2y * b2y, c2 = c2x * c2x + c2y * c2y;
        double ux = (b2 * (c2y - a2y) + c2 * (a2y - b2y)) / d;
        double uy = (b2 * (a2x - c2x) + c2 * (b2x - a2x)) / d;
        var center = new Vec2(ux + o.X, uy + o.Y);
        double r = center.DistanceTo(s);
        if (!(r > 0) || double.IsInfinity(r) || double.IsNaN(r)) return null;
        // Raios absurdamente grandes equivalem a reta.
        if (r > 1e7 * scale) return null;

        bool ccw = cross > 0;
        double a0 = (s - center).Angle;
        double ae = (e - center).Angle;
        double mag = ccw ? AngleUtil.Norm2Pi(ae - a0) : AngleUtil.Norm2Pi(a0 - ae);
        if (mag <= 0) mag = AngleUtil.TwoPi;
        return new ArcCurve(center, r, a0, ccw ? mag : -mag);
    }

    /// <summary>Arco a partir de centro, raio e extremidades (sentido informado).</summary>
    public static ArcCurve FromCenter(Vec2 center, Vec2 start, Vec2 end, bool counterClockwise)
    {
        double r = center.DistanceTo(start);
        double a0 = (start - center).Angle;
        double ae = (end - center).Angle;
        double mag = counterClockwise ? AngleUtil.Norm2Pi(ae - a0) : AngleUtil.Norm2Pi(a0 - ae);
        if (mag <= 0) mag = AngleUtil.TwoPi;
        return new ArcCurve(center, r, a0, counterClockwise ? mag : -mag);
    }
}
