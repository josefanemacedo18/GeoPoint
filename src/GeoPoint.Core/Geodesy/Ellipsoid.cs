namespace GeoPoint.Core.Geodesy;

/// <summary>Elipsoide de referência.</summary>
public sealed class Ellipsoid
{
    public Ellipsoid(string name, double a, double inverseFlattening)
    {
        Name = name;
        A = a;
        InverseFlattening = inverseFlattening;
        F = 1.0 / inverseFlattening;
        B = A * (1 - F);
        E2 = F * (2 - F);
        Ep2 = E2 / (1 - E2);
    }

    public string Name { get; }
    /// <summary>Semieixo maior (m).</summary>
    public double A { get; }
    /// <summary>Semieixo menor (m).</summary>
    public double B { get; }
    public double F { get; }
    public double InverseFlattening { get; }
    /// <summary>Primeira excentricidade ao quadrado.</summary>
    public double E2 { get; }
    /// <summary>Segunda excentricidade ao quadrado.</summary>
    public double Ep2 { get; }

    /// <summary>Raio de curvatura do primeiro vertical (N).</summary>
    public double PrimeVerticalRadius(double latRad)
    {
        double s = Math.Sin(latRad);
        return A / Math.Sqrt(1 - E2 * s * s);
    }

    /// <summary>Raio de curvatura do meridiano (M).</summary>
    public double MeridianRadius(double latRad)
    {
        double s = Math.Sin(latRad);
        double w = 1 - E2 * s * s;
        return A * (1 - E2) / (w * Math.Sqrt(w));
    }

    /// <summary>Raio médio gaussiano √(M·N).</summary>
    public double GaussianRadius(double latRad) => Math.Sqrt(MeridianRadius(latRad) * PrimeVerticalRadius(latRad));

    public static readonly Ellipsoid GRS80 = new("GRS 1980", 6378137.0, 298.257222101);
    public static readonly Ellipsoid WGS84 = new("WGS 84", 6378137.0, 298.257223563);
    /// <summary>Elipsoide de Referência 1967 / "GRS 1967 Modificado" (SAD 69).</summary>
    public static readonly Ellipsoid SAD69 = new("Referência 1967 (SAD 69)", 6378160.0, 298.25);
    /// <summary>Internacional de Hayford 1924 (Córrego Alegre).</summary>
    public static readonly Ellipsoid Hayford = new("Internacional 1924 (Hayford)", 6378388.0, 297.0);
}

/// <summary>Coordenadas geodésicas (graus e metros).</summary>
public readonly record struct GeodeticCoord(double LatDeg, double LonDeg, double Height = 0)
{
    public double LatRad => LatDeg * Math.PI / 180.0;
    public double LonRad => LonDeg * Math.PI / 180.0;
}

/// <summary>Coordenadas cartesianas geocêntricas (m).</summary>
public readonly record struct Ecef(double X, double Y, double Z);

public static class Geocentric
{
    public static Ecef ToEcef(GeodeticCoord g, Ellipsoid e)
    {
        double lat = g.LatRad, lon = g.LonRad;
        double n = e.PrimeVerticalRadius(lat);
        double cl = Math.Cos(lat);
        return new Ecef(
            (n + g.Height) * cl * Math.Cos(lon),
            (n + g.Height) * cl * Math.Sin(lon),
            (n * (1 - e.E2) + g.Height) * Math.Sin(lat));
    }

    /// <summary>Conversão geocêntrica → geodésica (iterativa, precisão sub-milimétrica).</summary>
    public static GeodeticCoord FromEcef(Ecef p, Ellipsoid e)
    {
        double x = p.X, y = p.Y, z = p.Z;
        double lon = Math.Atan2(y, x);
        double r = Math.Sqrt(x * x + y * y);
        if (r < 1e-9)
        {
            double latPole = z >= 0 ? Math.PI / 2 : -Math.PI / 2;
            return new GeodeticCoord(latPole * 180 / Math.PI, 0, Math.Abs(z) - e.B);
        }
        // Valor inicial de Bowring.
        double beta = Math.Atan2(z * e.A, r * e.B);
        double lat = Math.Atan2(z + e.Ep2 * e.B * Math.Pow(Math.Sin(beta), 3), r - e.E2 * e.A * Math.Pow(Math.Cos(beta), 3));
        for (int i = 0; i < 10; i++)
        {
            double n = e.PrimeVerticalRadius(lat);
            double h0 = r / Math.Cos(lat) - n;
            double newLat = Math.Atan2(z, r * (1 - e.E2 * n / (n + h0)));
            if (Math.Abs(newLat - lat) < 1e-14) { lat = newLat; break; }
            lat = newLat;
        }
        double nn = e.PrimeVerticalRadius(lat);
        double h;
        double cl = Math.Cos(lat);
        if (Math.Abs(cl) > 1e-10) h = r / cl - nn;
        else h = Math.Abs(z) / Math.Abs(Math.Sin(lat)) - nn * (1 - e.E2);
        return new GeodeticCoord(lat * 180 / Math.PI, lon * 180 / Math.PI, h);
    }

    /// <summary>ENU (leste, norte, cima) de um ponto em relação a uma origem.</summary>
    public static (double E, double N, double U) ToEnu(GeodeticCoord origin, GeodeticCoord p, Ellipsoid e)
    {
        var o = ToEcef(origin, e);
        var q = ToEcef(p, e);
        double dx = q.X - o.X, dy = q.Y - o.Y, dz = q.Z - o.Z;
        double lat = origin.LatRad, lon = origin.LonRad;
        double sl = Math.Sin(lat), cl = Math.Cos(lat), so = Math.Sin(lon), co = Math.Cos(lon);
        double east = -so * dx + co * dy;
        double north = -sl * co * dx - sl * so * dy + cl * dz;
        double up = cl * co * dx + cl * so * dy + sl * dz;
        return (east, north, up);
    }

    public static GeodeticCoord FromEnu(GeodeticCoord origin, double east, double north, double up, Ellipsoid e)
    {
        var o = ToEcef(origin, e);
        double lat = origin.LatRad, lon = origin.LonRad;
        double sl = Math.Sin(lat), cl = Math.Cos(lat), so = Math.Sin(lon), co = Math.Cos(lon);
        double dx = -so * east - sl * co * north + cl * co * up;
        double dy = co * east - sl * so * north + cl * so * up;
        double dz = cl * north + sl * up;
        return FromEcef(new Ecef(o.X + dx, o.Y + dy, o.Z + dz), e);
    }
}
