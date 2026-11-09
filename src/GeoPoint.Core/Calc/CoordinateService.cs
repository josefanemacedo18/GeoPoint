using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;

namespace GeoPoint.Core.Calc;

/// <summary>
/// Converte pontos do desenho (coordenadas internas do Revit, em metros) para o plano topográfico local
/// orientado ao norte verdadeiro, coordenadas geodésicas (SIRGAS 2000) e sistemas de saída.
/// O desenho é tratado como um plano topográfico local (distâncias horizontais verdadeiras).
/// </summary>
public sealed class CoordinateService
{
    private readonly GeoReferenceRecord _ref;
    private readonly SharedFrame _frame;
    private readonly GeodeticCoord _origin;
    private readonly Dictionary<(int, int, int), TransverseMercator> _tmCache = new();

    public CoordinateService(GeoReferenceRecord reference, SharedFrame frame)
    {
        _ref = reference;
        _frame = frame;
        _origin = new GeodeticCoord(reference.Lat, reference.Lon, reference.Height);
    }

    public bool HasGeoreference => _ref.IsSet;
    public GeoReferenceRecord Reference => _ref;
    public SharedFrame Frame => _frame;

    /// <summary>Rotação interna → leste/norte verdadeiro (rad, anti-horária).</summary>
    public double Rotation =>
        _ref.IsSet && _ref.Mode != OrientationMode.ProjectTrueNorth ? _ref.RotationRad : _frame.AngleRad;

    public Vec2 Anchor => _ref.IsSet ? new Vec2(_ref.AnchorX, _ref.AnchorY) : Vec2.Zero;

    public GeodeticCoord Origin => _origin;

    public UtmZone? AutoZone => _ref.IsSet ? UtmZone.FromLonLat(_ref.Lon, _ref.Lat) : null;

    /// <summary>Plano local orientado (leste, norte) relativo ao ponto de referência.</summary>
    public Vec2 ToEnu(Vec2 internalM) => (internalM - Anchor).Rotate(Rotation);

    public Vec2 FromEnu(Vec2 enu) => Anchor + enu.Rotate(-Rotation);

    /// <summary>Converte um vetor do desenho para o plano local (sem translação).</summary>
    public Vec2 VectorToEnu(Vec2 v) => v.Rotate(Rotation);

    public Vec2 VectorFromEnu(Vec2 v) => v.Rotate(-Rotation);

    public Vec2 ToShared(Vec2 internalM) => _frame.ToShared(internalM);

    /// <summary>Coordenadas geodésicas SIRGAS 2000 (null se o projeto não tiver referência).</summary>
    public GeodeticCoord? ToGeodetic(Vec2 internalM)
    {
        if (!_ref.IsSet) return null;
        var enu = ToEnu(internalM);
        return Geocentric.FromEnu(_origin, enu.X, enu.Y, 0, Ellipsoid.GRS80);
    }

    /// <summary>Ponto do desenho a partir de coordenadas geodésicas SIRGAS 2000.</summary>
    public Vec2? FromGeodetic(GeodeticCoord g)
    {
        if (!_ref.IsSet) return null;
        var (e, n, _) = Geocentric.ToEnu(_origin, new GeodeticCoord(g.LatDeg, g.LonDeg, _origin.Height), Ellipsoid.GRS80);
        return FromEnu(new Vec2(e, n));
    }

    /// <summary>Azimute verdadeiro (graus, 0–360) de um vetor do desenho.</summary>
    public double TrueAzimuth(Vec2 from, Vec2 to) => AzimuthOfEnuVector(VectorToEnu(to - from));

    public static double AzimuthOfEnuVector(Vec2 enu)
    {
        double az = Math.Atan2(enu.X, enu.Y) * 180.0 / Math.PI;
        if (az < 0) az += 360.0;
        if (az >= 360.0) az -= 360.0;
        return az;
    }

    /// <summary>Vetor unitário do desenho correspondente a um azimute verdadeiro.</summary>
    public Vec2 DirectionFromAzimuth(double azDeg)
    {
        double a = azDeg * Math.PI / 180.0;
        return VectorFromEnu(new Vec2(Math.Sin(a), Math.Cos(a)));
    }

    public TransverseMercator UtmProjection(UtmZone zone, DatumId datum)
    {
        var key = (zone.Number, zone.South ? 1 : 0, (int)datum);
        if (!_tmCache.TryGetValue(key, out var tm))
        {
            tm = zone.Projection(Datum.Get(datum).Ellipsoid);
            _tmCache[key] = tm;
        }
        return tm;
    }

    public UtmZone ResolveZone(CrsDef def)
    {
        var auto = AutoZone ?? new UtmZone(23, true);
        int number = def.UtmZone > 0 ? def.UtmZone : auto.Number;
        bool south = def.UtmSouth ?? auto.South;
        return new UtmZone(number, south);
    }

    /// <summary>Coordenadas do ponto no sistema de saída. Null se o sistema exigir georreferência inexistente.</summary>
    public CrsCoord? ToCrs(Vec2 internalM, CrsDef def)
    {
        if (def.Kind == CrsKind.ProjectLocal)
        {
            var s = ToShared(internalM);
            return new CrsCoord(s.X, s.Y, false);
        }
        var g = ToGeodetic(internalM);
        if (g is null) return null;
        return GeodeticToCrs(g.Value, def);
    }

    public CrsCoord GeodeticToCrs(GeodeticCoord sirgas, CrsDef def)
    {
        var datum = Datum.Get(def.Datum);
        var g = datum.FromSirgas(sirgas);
        switch (def.Kind)
        {
            case CrsKind.Geographic:
                return new CrsCoord(g.LatDeg, g.LonDeg, true);
            case CrsKind.Utm:
            {
                var zone = ResolveZone(def);
                var tm = UtmProjection(zone, def.Datum);
                var p = tm.Forward(g.LatDeg, g.LonDeg);
                return new CrsCoord(p.E, p.N, false, zone.ToString(), p.ConvergenceDeg, p.Scale);
            }
            case CrsKind.CustomTm:
            {
                var tm = new TransverseMercator(datum.Ellipsoid, def.CustomCentralMeridian, def.CustomK0, def.CustomFalseEasting, def.CustomFalseNorthing, def.CustomLatOrigin);
                var p = tm.Forward(g.LatDeg, g.LonDeg);
                return new CrsCoord(p.E, p.N, false, def.CustomName, p.ConvergenceDeg, p.Scale);
            }
            case CrsKind.LocalTopographic:
            {
                GeodeticCoord o = def.PtlUseAnchor ? _origin : new GeodeticCoord(def.PtlLat, def.PtlLon, def.PtlHeight);
                var (e, n, _) = Geocentric.ToEnu(o, new GeodeticCoord(sirgas.LatDeg, sirgas.LonDeg, o.Height), Ellipsoid.GRS80);
                return new CrsCoord(def.PtlFalseEasting + e, def.PtlFalseNorthing + n, false, "PTL");
            }
            default:
                return new CrsCoord(g.LatDeg, g.LonDeg, true);
        }
    }

    /// <summary>Coordenadas SIRGAS 2000 a partir de coordenada num sistema de entrada (para o ponto de referência).</summary>
    public static GeodeticCoord CrsToGeodetic(CrsCoord c, CrsDef def, double height)
    {
        var datum = Datum.Get(def.Datum);
        GeodeticCoord g;
        switch (def.Kind)
        {
            case CrsKind.Geographic:
                g = new GeodeticCoord(c.A, c.B, height);
                break;
            case CrsKind.Utm:
            {
                var zone = new UtmZone(def.UtmZone > 0 ? def.UtmZone : 23, def.UtmSouth ?? true);
                var tm = zone.Projection(datum.Ellipsoid);
                var (lat, lon) = tm.Inverse(c.A, c.B);
                g = new GeodeticCoord(lat, lon, height);
                break;
            }
            case CrsKind.CustomTm:
            {
                var tm = new TransverseMercator(datum.Ellipsoid, def.CustomCentralMeridian, def.CustomK0, def.CustomFalseEasting, def.CustomFalseNorthing, def.CustomLatOrigin);
                var (lat, lon) = tm.Inverse(c.A, c.B);
                g = new GeodeticCoord(lat, lon, height);
                break;
            }
            default:
                throw new NotSupportedException("Sistema de entrada não suportado para o ponto de referência.");
        }
        return datum.ToSirgas(g);
    }
}
