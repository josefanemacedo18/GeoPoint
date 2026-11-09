using System.Text.Json.Serialization;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;

namespace GeoPoint.Core.Model;

public enum LotUse
{
    Lote = 0,
    Gleba = 1,
    SistemaViario = 2,
    AreaVerde = 3,
    AreaInstitucional = 4,
    App = 5,
    Servidao = 6,
    ReservaLegal = 7,
    AreaNaoEdificavel = 8,
    Remanescente = 9,
    Outro = 10,
}

public enum LotStatus { Ativo = 0, Arquivado = 1 }

public enum OrientationMode
{
    /// <summary>Norte verdadeiro definido no projeto Revit (Localização do projeto).</summary>
    ProjectTrueNorth = 0,
    /// <summary>Azimute informado para uma linha que parte do ponto de referência.</summary>
    EdgeAzimuth = 1,
    /// <summary>Dois pontos com coordenadas conhecidas.</summary>
    TwoPoints = 2,
}

public enum PrecisionClass
{
    GoogleMapsEarth = 0,
    GpsNavegacao = 1,
    Qgis = 2,
    GnssRtk = 3,
    MarcoOficial = 4,
    Desconhecida = 5,
}

public enum OperationType { Desmembramento = 0, Remembramento = 1, Parcelamento = 2 }

public static class LotUseInfo
{
    public static string Label(LotUse u) => u switch
    {
        LotUse.Lote => "Lote",
        LotUse.Gleba => "Gleba",
        LotUse.SistemaViario => "Sistema viário",
        LotUse.AreaVerde => "Área verde",
        LotUse.AreaInstitucional => "Área institucional",
        LotUse.App => "APP",
        LotUse.Servidao => "Servidão",
        LotUse.ReservaLegal => "Reserva legal",
        LotUse.AreaNaoEdificavel => "Área não edificável",
        LotUse.Remanescente => "Remanescente",
        _ => "Outro",
    };

    /// <summary>Usos que representam restrições/descontos dentro de outros lotes.</summary>
    public static bool IsDeduction(LotUse u) => u is LotUse.App or LotUse.Servidao or LotUse.ReservaLegal or LotUse.AreaNaoEdificavel;

    /// <summary>Cor padrão (RGB) para mapas e KML.</summary>
    public static (byte R, byte G, byte B) DefaultColor(LotUse u) => u switch
    {
        LotUse.Lote => (255, 214, 102),
        LotUse.Gleba => (200, 200, 200),
        LotUse.SistemaViario => (160, 160, 160),
        LotUse.AreaVerde => (112, 191, 90),
        LotUse.AreaInstitucional => (110, 160, 230),
        LotUse.App => (40, 140, 70),
        LotUse.Servidao => (230, 120, 60),
        LotUse.ReservaLegal => (30, 110, 50),
        LotUse.AreaNaoEdificavel => (230, 90, 90),
        LotUse.Remanescente => (210, 180, 140),
        _ => (180, 140, 210),
    };

    public static IReadOnlyList<LotUse> All { get; } = Enum.GetValues<LotUse>();
}

public static class PrecisionInfo
{
    public static string Label(PrecisionClass p) => p switch
    {
        PrecisionClass.GoogleMapsEarth => "Google Maps / Google Earth",
        PrecisionClass.GpsNavegacao => "GPS de navegação / celular",
        PrecisionClass.Qgis => "QGIS / base cartográfica",
        PrecisionClass.GnssRtk => "GNSS geodésico (RTK/PPP)",
        PrecisionClass.MarcoOficial => "Marco geodésico oficial",
        _ => "Desconhecida",
    };

    /// <summary>Precisão horizontal típica (m) — apenas indicativa.</summary>
    public static double TypicalMeters(PrecisionClass p) => p switch
    {
        PrecisionClass.GoogleMapsEarth => 5.0,
        PrecisionClass.GpsNavegacao => 5.0,
        PrecisionClass.Qgis => 2.0,
        PrecisionClass.GnssRtk => 0.05,
        PrecisionClass.MarcoOficial => 0.05,
        _ => 10.0,
    };
}

/// <summary>Aresta serializável (metros, coordenadas internas do Revit).</summary>
public sealed class EdgeDto
{
    /// <summary>"L" reta, "A" arco.</summary>
    public string K { get; set; } = "L";
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    /// <summary>Ponto intermediário do arco.</summary>
    public double MX { get; set; }
    public double MY { get; set; }
    /// <summary>Elementos de origem (UniqueId do Revit).</summary>
    public List<string> Src { get; set; } = new();

    public Curve2 ToCurve()
    {
        var a = new Vec2(X1, Y1); var b = new Vec2(X2, Y2);
        return K == "A" ? CurveFactory.FromThreePoints(a, new Vec2(MX, MY), b) : new LineCurve(a, b);
    }

    public static EdgeDto FromCurve(Curve2 c, IEnumerable<string>? src = null)
    {
        var d = new EdgeDto { X1 = c.Start.X, Y1 = c.Start.Y, X2 = c.End.X, Y2 = c.End.Y };
        if (c is ArcCurve)
        {
            d.K = "A";
            var m = c.Mid;
            d.MX = m.X; d.MY = m.Y;
        }
        else
        {
            var m = c.Mid;
            d.MX = m.X; d.MY = m.Y;
        }
        if (src is not null) d.Src = src.Distinct().ToList();
        return d;
    }
}

/// <summary>Atributos de uma divisa do lote, identificada pelo ponto médio.</summary>
public sealed class EdgeInfo
{
    public double MX { get; set; }
    public double MY { get; set; }
    public string Confrontante { get; set; } = "";
    public bool Testada { get; set; }
    /// <summary>Recuo/afastamento (m) para cálculo de área edificável.</summary>
    public double? Recuo { get; set; }
}

public sealed class AnnotationRecord
{
    public string ViewId { get; set; } = "";
    public List<string> ElementIds { get; set; } = new();
}

public sealed class HistoryEntry
{
    public DateTime Date { get; set; } = DateTime.Now;
    public string Description { get; set; } = "";
    public double Area { get; set; }
    public double Perimeter { get; set; }
}

public sealed class LotRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Nome { get; set; } = "";
    public string Quadra { get; set; } = "";
    public string Numero { get; set; } = "";
    public LotUse Uso { get; set; } = LotUse.Lote;
    public LotStatus Status { get; set; } = LotStatus.Ativo;

    public string Matricula { get; set; } = "";
    public string Proprietario { get; set; } = "";
    public string Documento { get; set; } = "";
    public string Endereco { get; set; } = "";
    public string Observacoes { get; set; } = "";
    public double? AreaTitulo { get; set; }
    public double? PerimetroTitulo { get; set; }

    public List<EdgeDto> Outer { get; set; } = new();
    public List<List<EdgeDto>> Holes { get; set; } = new();

    /// <summary>Elevação (pés, coordenadas internas) do plano das linhas de origem.</summary>
    public double ZFeet { get; set; }
    /// <summary>"ModelLine", "DetailLine", "Cad" ou "Mixed".</summary>
    public string SourceKind { get; set; } = "ModelLine";
    /// <summary>UniqueId da vista (para linhas de detalhe).</summary>
    public string SourceViewId { get; set; } = "";
    /// <summary>UniqueId do plano de trabalho (linhas de modelo).</summary>
    public string SketchPlaneId { get; set; } = "";

    public List<EdgeInfo> EdgeInfos { get; set; } = new();

    /// <summary>Vértice inicial escolhido para a descrição (coordenadas internas, m).</summary>
    public double? StartX { get; set; }
    public double? StartY { get; set; }
    /// <summary>Sentido da descrição (null = padrão das configurações).</summary>
    public bool? Clockwise { get; set; }

    public double? AreaEdificavel { get; set; }
    public double? AreaSuperficial { get; set; }
    /// <summary>Altitudes dos vértices (chave "x;y" com 3 casas).</summary>
    public Dictionary<string, double> VertexElevations { get; set; } = new();

    public List<AnnotationRecord> Annotations { get; set; } = new();
    public string PropertyLineId { get; set; } = "";
    public List<string> FilledRegionIds { get; set; } = new();
    public List<string> AuxElementIds { get; set; } = new();

    public List<HistoryEntry> History { get; set; } = new();
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Modified { get; set; } = DateTime.Now;

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            string n;
            if (string.IsNullOrWhiteSpace(Nome)) n = Uso == LotUse.Lote ? "Lote" : LotUseInfo.Label(Uso);
            else if (Uso == LotUse.Lote && char.IsDigit(Nome.Trim()[0])) n = "Lote " + Nome.Trim();
            else n = Nome.Trim();
            if (!string.IsNullOrWhiteSpace(Quadra)) n += $" – Quadra {Quadra}";
            return n;
        }
    }

    public IEnumerable<string> AllSources() => Outer.Concat(Holes.SelectMany(h => h)).SelectMany(e => e.Src).Distinct();

    public Region ToRegion(bool includeHoles = true)
    {
        var outer = new Ring(Outer.Select(e => e.ToCurve()).ToList());
        var holes = includeHoles ? Holes.Where(h => h.Count > 0).Select(h => new Ring(h.Select(e => e.ToCurve()).ToList())).ToList() : new List<Ring>();
        return new Region(outer, holes);
    }

    public static string VertexKey(Vec2 p) => FormattableString.Invariant($"{p.X:0.000};{p.Y:0.000}");
}

/// <summary>Quadro de referência das coordenadas compartilhadas do Revit (capturado do documento).</summary>
public sealed class SharedFrame
{
    /// <summary>Coordenadas compartilhadas (m) da origem interna.</summary>
    public double OriginE { get; set; }
    public double OriginN { get; set; }
    /// <summary>Ângulo (rad, anti-horário) do eixo X interno no quadro compartilhado (norte verdadeiro = +Y).</summary>
    public double AngleRad { get; set; }
    /// <summary>Elevação compartilhada (m) da origem interna.</summary>
    public double OriginElevation { get; set; }

    public Vec2 ToShared(Vec2 internalM) => new Vec2(OriginE, OriginN) + internalM.Rotate(AngleRad);
}

public sealed class GeoReferenceRecord
{
    public bool IsSet { get; set; }
    /// <summary>Ponto de referência (coordenadas internas, m).</summary>
    public double AnchorX { get; set; }
    public double AnchorY { get; set; }
    public double AnchorZFeet { get; set; }
    public string AnchorLabel { get; set; } = "";

    /// <summary>Coordenadas geodésicas do ponto (SIRGAS 2000).</summary>
    public double Lat { get; set; }
    public double Lon { get; set; }
    /// <summary>Altitude informada (m) – usada como altura do plano topográfico.</summary>
    public double Height { get; set; }

    /// <summary>Como o usuário informou a coordenada (texto original) – para registro.</summary>
    public string InputText { get; set; } = "";
    public CrsKind InputKind { get; set; } = CrsKind.Geographic;
    public DatumId InputDatum { get; set; } = DatumId.Wgs84;

    public OrientationMode Mode { get; set; } = OrientationMode.ProjectTrueNorth;
    /// <summary>Rotação (rad, anti-horária) das coordenadas internas para o sistema leste-norte verdadeiro.</summary>
    public double RotationRad { get; set; }
    public double? EdgeAzimuthDeg { get; set; }

    // Segundo ponto (modo dois pontos).
    public double? P2X { get; set; }
    public double? P2Y { get; set; }
    public double? P2Lat { get; set; }
    public double? P2Lon { get; set; }
    /// <summary>Diferença entre a distância do desenho e a distância calculada pelas coordenadas (m).</summary>
    public double? TwoPointDistanceDiff { get; set; }
    /// <summary>Diferença entre a rotação calculada e o norte verdadeiro do projeto (graus).</summary>
    public double? TwoPointRotationDiffDeg { get; set; }

    public PrecisionClass Precision { get; set; } = PrecisionClass.GoogleMapsEarth;
    public DateTime SetAt { get; set; } = DateTime.Now;
}

public sealed class ProjectInfo
{
    public string Proprietario { get; set; } = "";
    public string Documento { get; set; } = "";
    public string Denominacao { get; set; } = "Lote Urbano";
    /// <summary>"Urbano" ou "Rural".</summary>
    public string TipoImovel { get; set; } = "Urbano";
    public string Endereco { get; set; } = "";
    public string Bairro { get; set; } = "";
    public string Municipio { get; set; } = "";
    public string UF { get; set; } = "";
    public string Comarca { get; set; } = "";
    public string Cartorio { get; set; } = "";
    public string Matricula { get; set; } = "";
    public string InscricaoMunicipal { get; set; } = "";
    public string Ccir { get; set; } = "";
    public string Car { get; set; } = "";
    public string Finalidade { get; set; } = "";
    public string RrtArt { get; set; } = "";
    public string RuleProfileName { get; set; } = "";
}

public sealed class OperationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public OperationType Type { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    public List<string> ParentIds { get; set; } = new();
    public List<string> ChildIds { get; set; } = new();
    public string Notes { get; set; } = "";

    [JsonIgnore]
    public string Label => $"{(Type == OperationType.Desmembramento ? "Desmembramento" : Type == OperationType.Remembramento ? "Remembramento" : "Parcelamento")} – {Date:dd/MM/yyyy HH:mm}";
}

/// <summary>Dados do GeoPoint gravados no documento do Revit.</summary>
public sealed class ParcelProject
{
    public int SchemaVersion { get; set; } = 1;
    public GeoReferenceRecord Reference { get; set; } = new();
    public SharedFrame Frame { get; set; } = new();
    public ProjectInfo Info { get; set; } = new();
    public List<LotRecord> Lots { get; set; } = new();
    public List<OperationRecord> Operations { get; set; } = new();

    public IEnumerable<LotRecord> ActiveLots => Lots.Where(l => l.Status == LotStatus.Ativo);

    public LotRecord? FindLot(string id) => Lots.FirstOrDefault(l => l.Id == id);

    public string NextLotName(string prefix = "Lote")
    {
        int max = 0;
        foreach (var l in Lots)
        {
            var n = l.Numero;
            if (int.TryParse(n, out int k)) max = Math.Max(max, k);
        }
        return (max + 1).ToString("00");
    }
}
