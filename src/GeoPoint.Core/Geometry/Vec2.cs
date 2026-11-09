using System.Globalization;

namespace GeoPoint.Core.Geometry;

/// <summary>Vetor/ponto 2D em metros (plano horizontal).</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    public double Dot(Vec2 o) => X * o.X + Y * o.Y;

    /// <summary>Produto vetorial (componente z): this × o.</summary>
    public double Cross(Vec2 o) => X * o.Y - Y * o.X;

    /// <summary>Perpendicular à esquerda (rotação de +90°).</summary>
    public Vec2 PerpLeft => new(-Y, X);

    /// <summary>Perpendicular à direita (rotação de -90°).</summary>
    public Vec2 PerpRight => new(Y, -X);

    public Vec2 Normalized()
    {
        double l = Length;
        return l > 0 ? new Vec2(X / l, Y / l) : Zero;
    }

    /// <summary>Ângulo matemático (a partir do eixo +X, anti-horário), em radianos, intervalo (-π, π].</summary>
    public double Angle => Math.Atan2(Y, X);

    public double DistanceTo(Vec2 o) => (this - o).Length;

    public Vec2 Rotate(double radians)
    {
        double c = Math.Cos(radians), s = Math.Sin(radians);
        return new Vec2(X * c - Y * s, X * s + Y * c);
    }

    public static Vec2 FromAngle(double radians) => new(Math.Cos(radians), Math.Sin(radians));

    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    public bool AlmostEquals(Vec2 o, double tol) => (this - o).LengthSquared <= tol * tol;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X:0.######}, {Y:0.######})");
}

/// <summary>Retângulo envolvente alinhado aos eixos.</summary>
public readonly record struct BBox(double MinX, double MinY, double MaxX, double MaxY)
{
    public static readonly BBox Empty = new(double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);

    public bool IsEmpty => MinX > MaxX || MinY > MaxY;
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public Vec2 Center => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);

    public BBox Include(Vec2 p) => new(Math.Min(MinX, p.X), Math.Min(MinY, p.Y), Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));
    public BBox Union(BBox b) => IsEmpty ? b : b.IsEmpty ? this : new(Math.Min(MinX, b.MinX), Math.Min(MinY, b.MinY), Math.Max(MaxX, b.MaxX), Math.Max(MaxY, b.MaxY));
    public BBox Expand(double d) => new(MinX - d, MinY - d, MaxX + d, MaxY + d);

    public bool Intersects(BBox b) => !(b.MinX > MaxX || b.MaxX < MinX || b.MinY > MaxY || b.MaxY < MinY);
    public bool Contains(Vec2 p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;

    public static BBox Of(IEnumerable<Vec2> pts)
    {
        var b = Empty;
        foreach (var p in pts) b = b.Include(p);
        return b;
    }
}

public static class AngleUtil
{
    public const double TwoPi = 2 * Math.PI;

    /// <summary>Normaliza para [0, 2π).</summary>
    public static double Norm2Pi(double a)
    {
        a %= TwoPi;
        if (a < 0) a += TwoPi;
        if (a >= TwoPi) a -= TwoPi;
        return a;
    }

    /// <summary>Normaliza para (-π, π].</summary>
    public static double NormPi(double a)
    {
        a = Norm2Pi(a);
        if (a > Math.PI) a -= TwoPi;
        return a;
    }

    public static double ToRad(double deg) => deg * Math.PI / 180.0;
    public static double ToDeg(double rad) => rad * 180.0 / Math.PI;
}
