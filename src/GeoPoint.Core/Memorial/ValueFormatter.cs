using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Settings;

namespace GeoPoint.Core.Memorial;

/// <summary>Formatação de valores para memorial, tabelas e anotações.</summary>
public sealed class ValueFormatter
{
    public ValueFormatter(FormatSettings f) { F = f; }

    public FormatSettings F { get; }

    public string Number(double v, int decimals) => AngleText.FormatNumber(v, decimals, F.ThousandsSeparator);

    public string Distance(double m) => AngleText.FormatNumber(m, F.DistanceDecimals, F.ThousandsSeparator) + " m";
    public string DistanceValue(double m) => AngleText.FormatNumber(m, F.DistanceDecimals, F.ThousandsSeparator);

    public string Area(double m2)
    {
        string sq = AngleText.FormatNumber(m2, F.AreaDecimals, true) + " m²";
        string ha = AngleText.FormatNumber(m2 / 10000.0, F.HectareDecimals, true) + " ha";
        return F.AreaUnit switch
        {
            AreaUnit.Hectares => ha,
            AreaUnit.Both => $"{sq} ({ha})",
            _ => sq,
        };
    }

    public string AreaM2(double m2) => AngleText.FormatNumber(m2, F.AreaDecimals, true) + " m²";
    public string AreaHa(double m2) => AngleText.FormatNumber(m2 / 10000.0, F.HectareDecimals, true) + " ha";

    public string Angle(double azDeg) => F.AngleStyle == AngleStyle.Rumo
        ? AngleText.FormatBearing(azDeg, F.AzimuthSecondsDecimals, F.WestAsO)
        : AngleText.FormatDms(azDeg, F.AzimuthSecondsDecimals, AngleKind.Azimuth);

    public string AngleWord => F.AngleStyle == AngleStyle.Rumo ? "rumo" : "azimute";

    public string DirectionPhrase(double azDeg) => $"{AngleWord} de {Angle(azDeg)}";

    public string Lat(double deg) => F.GeoDecimalDegrees
        ? AngleText.FormatNumber(deg, 8, false) + "°"
        : AngleText.FormatDms(deg, F.GeoSecondsDecimals, AngleKind.Latitude, F.WestAsO);

    public string Lon(double deg) => F.GeoDecimalDegrees
        ? AngleText.FormatNumber(deg, 8, false) + "°"
        : AngleText.FormatDms(deg, F.GeoSecondsDecimals, AngleKind.Longitude, F.WestAsO);

    public string Plane(double v) => AngleText.FormatNumber(v, F.PlaneCoordDecimals, F.ThousandsSeparator);

    /// <summary>Títulos das duas colunas de coordenadas.</summary>
    public (string A, string B) ColumnTitles(CrsDef def) => def.Kind switch
    {
        CrsKind.Geographic => ("Latitude", "Longitude"),
        CrsKind.LocalTopographic => ("X (m)", "Y (m)"),
        _ => ("E (m)", "N (m)"),
    };

    public (string A, string B) Columns(CrsCoord c, CrsDef def) => def.Kind switch
    {
        CrsKind.Geographic => (Lat(c.Lat), Lon(c.Lon)),
        _ => (Plane(c.East), Plane(c.North)),
    };

    /// <summary>Texto da coordenada para a descrição perimétrica.</summary>
    public string CoordText(CrsCoord c, CrsDef def) => def.Kind switch
    {
        CrsKind.Geographic => $"latitude {Lat(c.Lat)} e longitude {Lon(c.Lon)}",
        CrsKind.LocalTopographic => $"X {Plane(c.East)} m e Y {Plane(c.North)} m",
        _ => $"E {Plane(c.East)} m e N {Plane(c.North)} m",
    };

    public string ZoneText(UtmZone z, double latDeg) => F.ZoneStyle == ZoneLabelStyle.ZonaLetra
        ? $"zona {z.Number}{UtmZone.LatitudeBand(latDeg)}"
        : $"fuso {z.Number}, hemisfério {(z.South ? "Sul" : "Norte")}";

    public static string CentralMeridianText(double cmDeg) =>
        $"{Math.Abs(cmDeg).ToString("0.##", AngleText.PtBr)}° {(cmDeg < 0 ? "WGr" : "EGr")}";

    public static string DateLong(DateTime d) =>
        $"{d.Day:00} de {AngleText.PtBr.DateTimeFormat.GetMonthName(d.Month).ToLowerInvariant()} de {d.Year}";
}
