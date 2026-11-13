using GeoPoint.Core.Geometry;

namespace GeoPoint.Core.Sheets;

/// <summary>Retângulo no papel (mm, origem no canto inferior esquerdo da folha).</summary>
public readonly record struct PaperRect(double X0, double Y0, double X1, double Y1)
{
    public double Width => X1 - X0;
    public double Height => Y1 - Y0;
    public Vec2 Center => new((X0 + X1) / 2, (Y0 + Y1) / 2);
    public PaperRect Inset(double d) => new(X0 + d, Y0 + d, X1 - d, Y1 - d);
    public bool Intersects(PaperRect o) => X0 < o.X1 && o.X0 < X1 && Y0 < o.Y1 && o.Y0 < Y1;
    public bool Contains(Vec2 p) => p.X >= X0 && p.X <= X1 && p.Y >= Y0 && p.Y <= Y1;
}

public enum SheetOrientation { Retrato = 0, Paisagem = 1 }

/// <summary>Escalas usuais das pranchas (a disposição da folha fica no <see cref="SheetTemplate"/>).</summary>
public static class SheetLayout
{
    /// <summary>Escalas usuais (1:n).</summary>
    public static readonly int[] StandardScales = { 100, 125, 150, 200, 250, 300, 400, 500, 600, 750, 1000, 1250, 1500, 2000, 2500, 3000, 4000, 5000, 7500, 10000, 15000, 20000, 25000, 50000 };

    /// <summary>Menor escala usual em que o desenho (m) cabe na área (mm) com a margem (mm) para as anotações.</summary>
    public static int ChooseScale(double widthM, double heightM, double areaWmm, double areaHmm, double marginMm)
    {
        foreach (int s in StandardScales)
        {
            double w = widthM * 1000 / s + 2 * marginMm, h = heightM * 1000 / s + 2 * marginMm;
            if (w <= areaWmm && h <= areaHmm) return s;
        }
        return StandardScales[^1];
    }
}
