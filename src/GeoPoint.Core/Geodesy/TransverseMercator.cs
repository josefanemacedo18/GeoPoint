namespace GeoPoint.Core.Geodesy;

/// <summary>Resultado de projeção TM: coordenadas planas, convergência meridiana e fator de escala.</summary>
public readonly record struct TmPoint(double E, double N, double ConvergenceDeg, double Scale);

/// <summary>
/// Projeção Transversa de Mercator (série de Krüger até n⁶, conforme Karney 2011).
/// Precisão melhor que 1 mm dentro de milhares de km do meridiano central.
/// </summary>
public sealed class TransverseMercator
{
    private readonly double[] _alpha = new double[7];
    private readonly double[] _beta = new double[7];
    private readonly double _a1; // A (raio retificante)
    private readonly double _e;
    private readonly double _e2;
    private readonly double _n;

    public TransverseMercator(Ellipsoid ellipsoid, double centralMeridianDeg, double k0, double falseEasting, double falseNorthing, double latOriginDeg = 0)
    {
        Ellipsoid = ellipsoid;
        CentralMeridianDeg = centralMeridianDeg;
        K0 = k0;
        FalseEasting = falseEasting;
        FalseNorthing = falseNorthing;
        LatOriginDeg = latOriginDeg;

        double f = ellipsoid.F;
        double n = f / (2 - f);
        _n = n;
        _e2 = f * (2 - f);
        _e = Math.Sqrt(_e2);
        double n2 = n * n, n3 = n2 * n, n4 = n3 * n, n5 = n4 * n, n6 = n5 * n;
        _a1 = ellipsoid.A / (1 + n) * (1 + n2 / 4 + n4 / 64 + n6 / 256);

        _alpha[1] = n / 2 - 2 * n2 / 3 + 5 * n3 / 16 + 41 * n4 / 180 - 127 * n5 / 288 + 7891 * n6 / 37800;
        _alpha[2] = 13 * n2 / 48 - 3 * n3 / 5 + 557 * n4 / 1440 + 281 * n5 / 630 - 1983433 * n6 / 1935360;
        _alpha[3] = 61 * n3 / 240 - 103 * n4 / 140 + 15061 * n5 / 26880 + 167603 * n6 / 181440;
        _alpha[4] = 49561 * n4 / 161280 - 179 * n5 / 168 + 6601661 * n6 / 7257600;
        _alpha[5] = 34729 * n5 / 80640 - 3418889 * n6 / 1995840;
        _alpha[6] = 212378941 * n6 / 319334400;

        _beta[1] = n / 2 - 2 * n2 / 3 + 37 * n3 / 96 - n4 / 360 - 81 * n5 / 512 + 96199 * n6 / 604800;
        _beta[2] = n2 / 48 + n3 / 15 - 437 * n4 / 1440 + 46 * n5 / 105 - 1118711 * n6 / 3870720;
        _beta[3] = 17 * n3 / 480 - 37 * n4 / 840 - 209 * n5 / 4480 + 5569 * n6 / 90720;
        _beta[4] = 4397 * n4 / 161280 - 11 * n5 / 504 - 830251 * n6 / 7257600;
        _beta[5] = 4583 * n5 / 161280 - 108847 * n6 / 3991680;
        _beta[6] = 20648693 * n6 / 638668800;

        _northingOrigin = latOriginDeg == 0 ? 0 : ForwardRaw(latOriginDeg * Math.PI / 180, 0).xi * _a1 * k0;
    }

    private readonly double _northingOrigin;

    public Ellipsoid Ellipsoid { get; }
    public double CentralMeridianDeg { get; }
    public double K0 { get; }
    public double FalseEasting { get; }
    public double FalseNorthing { get; }
    public double LatOriginDeg { get; }

    private static double Asinh(double x) => Math.Asinh(x);
    private static double Atanh(double x) => Math.Atanh(x);

    /// <summary>tan da latitude conforme a partir de tan φ.</summary>
    private double TauPrime(double tau)
    {
        double sig = Math.Sinh(_e * Atanh(_e * tau / Math.Sqrt(1 + tau * tau)));
        return tau * Math.Sqrt(1 + sig * sig) - sig * Math.Sqrt(1 + tau * tau);
    }

    /// <summary>Inverso de TauPrime (Newton).</summary>
    private double TauFromTauPrime(double taup)
    {
        double tau = taup / (1 - _e2);
        double stol = 1e-15 * Math.Max(1.0, Math.Abs(taup));
        for (int i = 0; i < 10; i++)
        {
            double taupa = TauPrime(tau);
            double dtau = (taup - taupa) * (1 + (1 - _e2) * tau * tau) /
                          ((1 - _e2) * Math.Sqrt(1 + taupa * taupa) * Math.Sqrt(1 + tau * tau));
            tau += dtau;
            if (Math.Abs(dtau) <= stol) break;
        }
        return tau;
    }

    private (double xi, double eta, double xip, double etap, double tau, double taup) ForwardRaw(double lat, double dlon)
    {
        double tau = Math.Tan(lat);
        double taup = TauPrime(tau);
        double xip = Math.Atan2(taup, Math.Cos(dlon));
        double etap = Asinh(Math.Sin(dlon) / Math.Sqrt(taup * taup + Math.Cos(dlon) * Math.Cos(dlon)));
        double xi = xip, eta = etap;
        for (int j = 1; j <= 6; j++)
        {
            xi += _alpha[j] * Math.Sin(2 * j * xip) * Math.Cosh(2 * j * etap);
            eta += _alpha[j] * Math.Cos(2 * j * xip) * Math.Sinh(2 * j * etap);
        }
        return (xi, eta, xip, etap, tau, taup);
    }

    public TmPoint Forward(double latDeg, double lonDeg)
    {
        double lat = latDeg * Math.PI / 180.0;
        double dlon = NormalizeLon(lonDeg - CentralMeridianDeg) * Math.PI / 180.0;
        var (xi, eta, xip, etap, tau, taup) = ForwardRaw(lat, dlon);

        double e = FalseEasting + K0 * _a1 * eta;
        double nCoord = FalseNorthing + K0 * _a1 * xi - _northingOrigin;

        // Escala e convergência.
        double sigma = 1, tauS = 0;
        for (int j = 1; j <= 6; j++)
        {
            sigma += 2 * j * _alpha[j] * Math.Cos(2 * j * xip) * Math.Cosh(2 * j * etap);
            tauS += 2 * j * _alpha[j] * Math.Sin(2 * j * xip) * Math.Sinh(2 * j * etap);
        }
        double cosl = Math.Cos(dlon), sinl = Math.Sin(dlon);
        double gammaP = Math.Atan2(taup * sinl, Math.Sqrt(1 + taup * taup) * cosl);
        double gammaPP = Math.Atan2(tauS, sigma);
        double gamma = gammaP + gammaPP;

        double kP = Math.Sqrt(1 - _e2 * Math.Sin(lat) * Math.Sin(lat)) * Math.Sqrt(1 + tau * tau) /
                    Math.Sqrt(taup * taup + cosl * cosl);
        double kPP = _a1 / Ellipsoid.A * Math.Sqrt(sigma * sigma + tauS * tauS);
        double k = K0 * kP * kPP;

        return new TmPoint(e, nCoord, gamma * 180 / Math.PI, k);
    }

    public (double LatDeg, double LonDeg) Inverse(double easting, double northing)
    {
        double xi = (northing - FalseNorthing + _northingOrigin) / (K0 * _a1);
        double eta = (easting - FalseEasting) / (K0 * _a1);
        double xip = xi, etap = eta;
        for (int j = 1; j <= 6; j++)
        {
            xip -= _beta[j] * Math.Sin(2 * j * xi) * Math.Cosh(2 * j * eta);
            etap -= _beta[j] * Math.Cos(2 * j * xi) * Math.Sinh(2 * j * eta);
        }
        double sinhEtap = Math.Sinh(etap);
        double sinXip = Math.Sin(xip), cosXip = Math.Cos(xip);
        double r = Math.Sqrt(sinhEtap * sinhEtap + cosXip * cosXip);
        double taup = sinXip / r;
        double lat;
        double lon;
        if (r != 0)
        {
            double tau = TauFromTauPrime(taup);
            lat = Math.Atan(tau);
            lon = Math.Atan2(sinhEtap, cosXip);
        }
        else
        {
            lat = Math.PI / 2 * Math.Sign(xip);
            lon = 0;
        }
        return (lat * 180 / Math.PI, NormalizeLon(CentralMeridianDeg + lon * 180 / Math.PI));
    }

    /// <summary>Convergência e fator de escala num ponto (graus, adimensional).</summary>
    public (double ConvergenceDeg, double Scale) Factors(double latDeg, double lonDeg)
    {
        var p = Forward(latDeg, lonDeg);
        return (p.ConvergenceDeg, p.Scale);
    }

    private static double NormalizeLon(double d)
    {
        d %= 360.0;
        if (d > 180) d -= 360;
        if (d <= -180) d += 360;
        return d;
    }
}

/// <summary>Zona UTM.</summary>
public readonly record struct UtmZone(int Number, bool South)
{
    public double CentralMeridianDeg => -183 + 6 * Number;

    public static UtmZone FromLonLat(double lonDeg, double latDeg)
    {
        double lon = lonDeg;
        while (lon < -180) lon += 360;
        while (lon >= 180) lon -= 360;
        int zone = (int)Math.Floor((lon + 180) / 6) + 1;
        zone = Math.Clamp(zone, 1, 60);
        return new UtmZone(zone, latDeg < 0);
    }

    /// <summary>Letra da faixa de latitude (padrão MGRS), ex.: 23M.</summary>
    public static char LatitudeBand(double latDeg)
    {
        const string bands = "CDEFGHJKLMNPQRSTUVWX";
        if (latDeg < -80 || latDeg > 84) return 'Z';
        int idx = (int)Math.Floor((latDeg + 80) / 8);
        idx = Math.Clamp(idx, 0, bands.Length - 1);
        return bands[idx];
    }

    public TransverseMercator Projection(Ellipsoid ellipsoid) =>
        new(ellipsoid, CentralMeridianDeg, 0.9996, 500000, South ? 10000000 : 0);

    public override string ToString() => $"{Number}{(South ? "S" : "N")}";
}
