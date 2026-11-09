namespace GeoPoint.Core.Geodesy;

public enum CrsKind
{
    /// <summary>Coordenadas geográficas (latitude/longitude).</summary>
    Geographic = 0,
    /// <summary>UTM (fuso automático ou fixo).</summary>
    Utm = 1,
    /// <summary>Transversa de Mercator personalizada (RTM, LTM, etc.).</summary>
    CustomTm = 2,
    /// <summary>Plano Topográfico Local (conceito NBR 14166).</summary>
    LocalTopographic = 3,
    /// <summary>Coordenadas compartilhadas do projeto Revit (sistema local).</summary>
    ProjectLocal = 4,
}

/// <summary>Definição de sistema de coordenadas de saída (serializável).</summary>
public sealed class CrsDef
{
    public CrsKind Kind { get; set; } = CrsKind.Utm;
    public DatumId Datum { get; set; } = DatumId.Sirgas2000;

    /// <summary>Fuso UTM (0 = automático pelo ponto de referência).</summary>
    public int UtmZone { get; set; }
    /// <summary>Hemisfério sul (null = automático).</summary>
    public bool? UtmSouth { get; set; }

    public string CustomName { get; set; } = "RTM";
    public double CustomCentralMeridian { get; set; } = -45;
    public double CustomK0 { get; set; } = 0.999995;
    public double CustomFalseEasting { get; set; } = 400000;
    public double CustomFalseNorthing { get; set; } = 5000000;
    public double CustomLatOrigin { get; set; }

    /// <summary>PTL: usar o ponto de referência do projeto como origem.</summary>
    public bool PtlUseAnchor { get; set; } = true;
    public double PtlLat { get; set; }
    public double PtlLon { get; set; }
    public double PtlHeight { get; set; }
    public double PtlFalseEasting { get; set; } = 150000;
    public double PtlFalseNorthing { get; set; } = 250000;

    public CrsDef Clone() => (CrsDef)MemberwiseClone();

    public string ShortName(UtmZone? autoZone = null)
    {
        string datum = GeoPoint.Core.Geodesy.Datum.Get(Datum).Name;
        return Kind switch
        {
            CrsKind.Geographic => $"Geográficas – {datum}",
            CrsKind.Utm => UtmZone > 0 ? $"UTM fuso {UtmZone} – {datum}" : autoZone is { } z ? $"UTM fuso {z.Number} – {datum}" : $"UTM – {datum}",
            CrsKind.CustomTm => $"{CustomName} (MC {CustomCentralMeridian:0.##}°) – {datum}",
            CrsKind.LocalTopographic => "Plano Topográfico Local",
            CrsKind.ProjectLocal => "Coordenadas locais do projeto (Revit)",
            _ => Kind.ToString(),
        };
    }

    public static CrsDef Geographic(DatumId d = DatumId.Sirgas2000) => new() { Kind = CrsKind.Geographic, Datum = d };
    public static CrsDef Utm(DatumId d = DatumId.Sirgas2000, int zone = 0) => new() { Kind = CrsKind.Utm, Datum = d, UtmZone = zone };
    public static CrsDef Ptl() => new() { Kind = CrsKind.LocalTopographic };
    public static CrsDef ProjectLocalDef() => new() { Kind = CrsKind.ProjectLocal };

    /// <summary>Presets de TM regional/local (parâmetros devem ser conferidos com a prefeitura).</summary>
    public static CrsDef Rtm(double centralMeridian) => new()
    {
        Kind = CrsKind.CustomTm, CustomName = "RTM", CustomCentralMeridian = centralMeridian, CustomK0 = 0.999995,
        CustomFalseEasting = 400000, CustomFalseNorthing = 5000000,
    };

    public static CrsDef Ltm(double centralMeridian) => new()
    {
        Kind = CrsKind.CustomTm, CustomName = "LTM", CustomCentralMeridian = centralMeridian, CustomK0 = 0.999995,
        CustomFalseEasting = 200000, CustomFalseNorthing = 5000000,
    };
}

/// <summary>Coordenada num sistema de saída.</summary>
public readonly record struct CrsCoord(double A, double B, bool IsGeographic, string ZoneLabel = "", double ConvergenceDeg = 0, double Scale = 1)
{
    /// <summary>Para sistemas planos: Este (E/X). Para geográficos: latitude.</summary>
    public double East => A;
    public double North => B;
    public double Lat => A;
    public double Lon => B;
}
