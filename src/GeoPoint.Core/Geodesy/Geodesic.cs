namespace GeoPoint.Core.Geodesy;

/// <summary>Problema geodésico inverso (Vincenty) — distância e azimutes sobre o elipsoide.</summary>
public static class Geodesic
{
    public readonly record struct InverseResult(double Distance, double Azimuth1Deg, double Azimuth2Deg, bool Converged);

    public static InverseResult Inverse(GeodeticCoord p1, GeodeticCoord p2, Ellipsoid e)
    {
        double a = e.A, b = e.B, f = e.F;
        double phi1 = p1.LatRad, phi2 = p2.LatRad;
        double L = (p2.LonDeg - p1.LonDeg) * Math.PI / 180.0;
        L = Math.IEEERemainder(L, 2 * Math.PI);

        double tanU1 = (1 - f) * Math.Tan(phi1), cosU1 = 1 / Math.Sqrt(1 + tanU1 * tanU1), sinU1 = tanU1 * cosU1;
        double tanU2 = (1 - f) * Math.Tan(phi2), cosU2 = 1 / Math.Sqrt(1 + tanU2 * tanU2), sinU2 = tanU2 * cosU2;

        double lambda = L, lambdaP;
        double sinLambda, cosLambda, sinSigma = 0, cosSigma = 0, sigma = 0, cosSqAlpha = 0, cos2SigmaM = 0, sinAlpha;
        int iter = 0;
        bool converged = false;
        do
        {
            sinLambda = Math.Sin(lambda); cosLambda = Math.Cos(lambda);
            double t1 = cosU2 * sinLambda;
            double t2 = cosU1 * sinU2 - sinU1 * cosU2 * cosLambda;
            sinSigma = Math.Sqrt(t1 * t1 + t2 * t2);
            if (sinSigma == 0) return new InverseResult(0, 0, 0, true); // pontos coincidentes
            cosSigma = sinU1 * sinU2 + cosU1 * cosU2 * cosLambda;
            sigma = Math.Atan2(sinSigma, cosSigma);
            sinAlpha = cosU1 * cosU2 * sinLambda / sinSigma;
            cosSqAlpha = 1 - sinAlpha * sinAlpha;
            cos2SigmaM = cosSqAlpha != 0 ? cosSigma - 2 * sinU1 * sinU2 / cosSqAlpha : 0;
            double C = f / 16 * cosSqAlpha * (4 + f * (4 - 3 * cosSqAlpha));
            lambdaP = lambda;
            lambda = L + (1 - C) * f * sinAlpha * (sigma + C * sinSigma * (cos2SigmaM + C * cosSigma * (-1 + 2 * cos2SigmaM * cos2SigmaM)));
            if (Math.Abs(lambda - lambdaP) < 1e-13) { converged = true; break; }
        } while (++iter < 200);

        double uSq = cosSqAlpha * (a * a - b * b) / (b * b);
        double A = 1 + uSq / 16384 * (4096 + uSq * (-768 + uSq * (320 - 175 * uSq)));
        double B = uSq / 1024 * (256 + uSq * (-128 + uSq * (74 - 47 * uSq)));
        double deltaSigma = B * sinSigma * (cos2SigmaM + B / 4 * (cosSigma * (-1 + 2 * cos2SigmaM * cos2SigmaM) -
            B / 6 * cos2SigmaM * (-3 + 4 * sinSigma * sinSigma) * (-3 + 4 * cos2SigmaM * cos2SigmaM)));
        double s = b * A * (sigma - deltaSigma);

        sinLambda = Math.Sin(lambda); cosLambda = Math.Cos(lambda);
        double az1 = Math.Atan2(cosU2 * sinLambda, cosU1 * sinU2 - sinU1 * cosU2 * cosLambda);
        double az2 = Math.Atan2(cosU1 * sinLambda, -sinU1 * cosU2 + cosU1 * sinU2 * cosLambda);
        return new InverseResult(s, Norm(az1 * 180 / Math.PI), Norm(az2 * 180 / Math.PI), converged);
    }

    private static double Norm(double d)
    {
        d %= 360;
        if (d < 0) d += 360;
        return d;
    }
}
