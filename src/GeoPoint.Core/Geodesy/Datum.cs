namespace GeoPoint.Core.Geodesy;

public enum DatumId
{
    Sirgas2000 = 0,
    Wgs84 = 1,
    Sad69 = 2,
    CorregoAlegre = 3,
}

/// <summary>
/// Datum geodésico com transformação de 3 parâmetros (translação geocêntrica) para SIRGAS 2000.
/// Parâmetros oficiais IBGE/EPSG.
/// </summary>
public sealed class Datum
{
    private Datum(DatumId id, string name, Ellipsoid ellipsoid, double dx, double dy, double dz, string note)
    {
        Id = id; Name = name; Ellipsoid = ellipsoid; Dx = dx; Dy = dy; Dz = dz; Note = note;
    }

    public DatumId Id { get; }
    public string Name { get; }
    public Ellipsoid Ellipsoid { get; }
    /// <summary>Translação deste datum para SIRGAS 2000 (m).</summary>
    public double Dx { get; }
    public double Dy { get; }
    public double Dz { get; }
    public string Note { get; }

    public bool IsSirgasEquivalent => Dx == 0 && Dy == 0 && Dz == 0;

    public static readonly Datum Sirgas2000 = new(DatumId.Sirgas2000, "SIRGAS 2000", Ellipsoid.GRS80, 0, 0, 0,
        "Sistema Geodésico de Referência oficial do Brasil.");

    public static readonly Datum Wgs84 = new(DatumId.Wgs84, "WGS 84", Ellipsoid.WGS84, 0, 0, 0,
        "Usado por GPS, Google Earth e Google Maps. Considerado equivalente ao SIRGAS 2000 (diferença de poucos decímetros, inferior à precisão dessas fontes).");

    public static readonly Datum Sad69 = new(DatumId.Sad69, "SAD 69", Ellipsoid.SAD69, -67.35, 3.88, -38.22,
        "Parâmetros oficiais do IBGE (R.PR 1/2005), os mesmos do registro EPSG \"SAD69 to SIRGAS 2000 (1)\". Precisão da transformação ≈ 5 m.");

    public static readonly Datum CorregoAlegre = new(DatumId.CorregoAlegre, "Córrego Alegre 1970-72", Ellipsoid.Hayford, -206.05, 168.28, -3.82,
        "Parâmetros do registro EPSG \"Corrego Alegre 1970-72 to SIRGAS 2000 (2)\". Precisão da transformação ≈ 5 m.");

    public static IReadOnlyList<Datum> All { get; } = new[] { Sirgas2000, Wgs84, Sad69, CorregoAlegre };

    public static Datum Get(DatumId id) => All.First(d => d.Id == id);

    /// <summary>Converte coordenada deste datum para SIRGAS 2000.</summary>
    public GeodeticCoord ToSirgas(GeodeticCoord g)
    {
        if (IsSirgasEquivalent) return g;
        var p = Geocentric.ToEcef(g, Ellipsoid);
        var q = new Ecef(p.X + Dx, p.Y + Dy, p.Z + Dz);
        return Geocentric.FromEcef(q, Ellipsoid.GRS80);
    }

    /// <summary>Converte coordenada SIRGAS 2000 para este datum.</summary>
    public GeodeticCoord FromSirgas(GeodeticCoord g)
    {
        if (IsSirgasEquivalent) return g;
        var p = Geocentric.ToEcef(g, Ellipsoid.GRS80);
        var q = new Ecef(p.X - Dx, p.Y - Dy, p.Z - Dz);
        return Geocentric.FromEcef(q, Ellipsoid);
    }

    public override string ToString() => Name;
}
