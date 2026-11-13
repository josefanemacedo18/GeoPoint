using System.Text.Json;
using System.Text.Json.Serialization;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Model;
using GeoPoint.Core.Urban;

namespace GeoPoint.Core.Settings;

public enum AzimuthType
{
    /// <summary>Azimute verdadeiro no plano topográfico local (como desenhado no Revit).</summary>
    TrueLocal = 0,
    /// <summary>Azimute plano de quadrícula (UTM).</summary>
    GridUtm = 1,
    /// <summary>Azimute geodésico (elipsoide).</summary>
    Geodesic = 2,
}

public enum DistanceType
{
    /// <summary>Distância horizontal (plano topográfico local – a do desenho).</summary>
    Horizontal = 0,
    /// <summary>Distância plana UTM (com fator de escala).</summary>
    GridUtm = 1,
    /// <summary>Distância geodésica (elipsoide).</summary>
    Geodesic = 2,
}

public enum AreaType
{
    /// <summary>Área no plano topográfico local (a do desenho).</summary>
    Local = 0,
    /// <summary>Área no plano de projeção UTM.</summary>
    Utm = 1,
    /// <summary>As duas.</summary>
    Both = 2,
}

public enum AreaUnit { SquareMeters = 0, Hectares = 1, Both = 2 }

public enum AngleStyle { Azimute = 0, Rumo = 1 }

public enum ZoneLabelStyle
{
    /// <summary>"Fuso 23, hemisfério Sul".</summary>
    FusoHemisferio = 0,
    /// <summary>"Zona 23M".</summary>
    ZonaLetra = 1,
}

public enum StartVertexRule
{
    /// <summary>Início da testada (frente); se não houver, vértice mais ao norte.</summary>
    Testada = 0,
    /// <summary>Vértice mais ao norte (empate: mais a oeste).</summary>
    MaisAoNorte = 1,
    /// <summary>Vértice mais a noroeste.</summary>
    MaisANoroeste = 2,
}

public sealed class RecognitionSettings
{
    public double ToleranceMm { get; set; } = 1.0;
    public double GapSearchMm { get; set; } = 100;
    public double TinyFaceM2 { get; set; } = 0.01;
    /// <summary>Remove vértices intermediários colineares sem encontro de outras linhas.</summary>
    public bool MergeCollinear { get; set; }
    /// <summary>Considerar ilhas internas como furos do lote.</summary>
    public bool IncludeHoles { get; set; }
    public string DefaultQuadra { get; set; } = "";
    public string NamePrefix { get; set; } = "Lote";
}

public sealed class NamingSettings
{
    public string VertexPrefix { get; set; } = "P-";
    public int VertexDigits { get; set; } = 2;
    /// <summary>Numeração dos vértices: projeto inteiro (registro único), por quadra ou por lote.</summary>
    public VertexNumbering Numbering { get; set; } = VertexNumbering.Lote;
    /// <summary>Descrição no sentido horário.</summary>
    public bool Clockwise { get; set; } = true;
    public StartVertexRule StartRule { get; set; } = StartVertexRule.Testada;
}

public sealed class FormatSettings
{
    public int DistanceDecimals { get; set; } = 2;
    public int AreaDecimals { get; set; } = 2;
    public int PlaneCoordDecimals { get; set; } = 3;
    public int GeoSecondsDecimals { get; set; } = 4;
    public int AzimuthSecondsDecimals { get; set; }
    public bool ThousandsSeparator { get; set; } = true;
    public AreaUnit AreaUnit { get; set; } = AreaUnit.SquareMeters;
    public int HectareDecimals { get; set; } = 4;
    public AngleStyle AngleStyle { get; set; } = AngleStyle.Azimute;
    /// <summary>Usar "O" (oeste) em vez de "W".</summary>
    public bool WestAsO { get; set; } = true;
    public ZoneLabelStyle ZoneStyle { get; set; } = ZoneLabelStyle.FusoHemisferio;
    /// <summary>Latitude/longitude em graus decimais em vez de GMS.</summary>
    public bool GeoDecimalDegrees { get; set; }
}

public sealed class MemorialSettings
{
    /// <summary>Sistemas de coordenadas mostrados no memorial (o primeiro é o principal).</summary>
    public List<CrsDef> CoordinateSystems { get; set; } = new() { CrsDef.Utm(), CrsDef.Geographic() };
    public AzimuthType AzimuthType { get; set; } = AzimuthType.TrueLocal;
    public DistanceType DistanceType { get; set; } = DistanceType.Horizontal;
    public AreaType AreaType { get; set; } = AreaType.Local;
    public bool IncludeVertexTable { get; set; } = true;
    /// <summary>Sem uso (as notas técnicas foram retiradas do memorial). Mantido para ler configurações antigas.</summary>
    public bool IncludeSystemDeclaration { get; set; }
    public bool IncludeAltitude { get; set; }
    /// <summary>Inclui o lado do lote na descrição ("pela frente", "pela lateral direita"...).</summary>
    public bool IncludeSide { get; set; } = true;
    /// <summary>Quadro de confrontantes por lado.</summary>
    public bool IncludeConfrontantesTable { get; set; } = true;
    public string TemplatePath { get; set; } = "";
    public string PhrasesPath { get; set; } = "";
    public bool OneFilePerLot { get; set; }
    public bool OpenAfterGenerate { get; set; } = true;
    public string OutputFolder { get; set; } = "";
    /// <summary>Transversa de Mercator personalizada (RTM/LTM municipal) disponível para o memorial.</summary>
    public CrsDef CustomTm { get; set; } = CrsDef.Rtm(-45);
    /// <summary>Plano Topográfico Local (origem no ponto de referência ou definida pelo município).</summary>
    public CrsDef Ptl { get; set; } = CrsDef.Ptl();
}

public sealed class TechnicianSettings
{
    public string Nome { get; set; } = "";
    public string Profissao { get; set; } = "Arquiteto e Urbanista";
    public string Conselho { get; set; } = "CAU";
    public string Registro { get; set; } = "";
    public string Empresa { get; set; } = "";
}

public sealed class AnnotationSettings
{
    public double TextSizeMm { get; set; } = 2.0;
    public double OffsetMm { get; set; } = 1.5;
    public bool Vertices { get; set; } = true;
    public bool Edges { get; set; } = true;
    public bool AreaLabel { get; set; } = true;
    public bool EdgeAzimuth { get; set; } = true;
    public string Font { get; set; } = "Arial";
    /// <summary>Textos das divisas do lado de fora do lote.</summary>
    public bool TextOutside { get; set; } = true;
    /// <summary>Ponto (círculo preenchido) em cada vértice.</summary>
    public bool VertexDots { get; set; } = true;
    /// <summary>Diâmetro do ponto no papel (mm).</summary>
    public double DotMm { get; set; } = 2.2;
    /// <summary>Fundo opaco nos textos (legíveis sobre hachuras).</summary>
    public bool OpaqueText { get; set; } = true;
    /// <summary>Nome do confrontante e extensão de cada confrontante ao longo da divisa.</summary>
    public bool Confrontantes { get; set; } = true;
    /// <summary>Rótulo do lote com proprietário, matrícula e perímetro.</summary>
    public bool LabelOwner { get; set; } = true;
    public bool LabelMatricula { get; set; } = true;
    public bool LabelPerimeter { get; set; } = true;
}

public sealed class StreetSettings
{
    /// <summary>Pista (leito) de meio-fio a meio-fio (m).</summary>
    public double Largura { get; set; } = 7;
    public double Calcada { get; set; } = 2;
    public double RaioEsquina { get; set; } = 2;
    /// <summary>Altura do texto do nome da via no papel (mm).</summary>
    public double NomeMm { get; set; } = 3.0;
}

public sealed class SheetSettings
{
    /// <summary>Carimbo próprio ("Família: Tipo"); vazio = moldura e carimbo A4 desenhados pelo GeoPoint.</summary>
    public string TitleBlock { get; set; } = "";
    public int Scale { get; set; } = 500;
    public string NumberPrefix { get; set; } = "GP";
    public bool SituacaoAtual { get; set; } = true;
    public bool Proposta { get; set; } = true;
    public bool Quadro { get; set; } = true;
    public bool Hachuras { get; set; } = true;
    public bool Anotacoes { get; set; } = true;
    public bool Vias { get; set; } = true;
    public bool IndividualAtual { get; set; }
    public bool IndividualProposta { get; set; } = true;
    /// <summary>Quadro de coordenadas nas pranchas de um lote só.</summary>
    public bool Coordenadas { get; set; } = true;
    /// <summary>0 = retrato, 1 = paisagem (folha A4).</summary>
    public int Orientation { get; set; }
    /// <summary>Escala automática (a menor escala usual em que o desenho cabe na folha).</summary>
    public bool AutoScale { get; set; } = true;
    /// <summary>Vista com o norte verdadeiro para cima.</summary>
    public bool NorteParaCima { get; set; } = true;
}

public sealed class KmlSettings
{
    public double FillOpacity { get; set; } = 0.35;
    public double LineWidth { get; set; } = 2;
    public bool IncludeVertices { get; set; } = true;
    public bool IncludeEdges { get; set; }
    public bool IncludeArchived { get; set; }
}

public sealed class GeoPointSettings
{
    public int Version { get; set; } = 3;
    public RecognitionSettings Recognition { get; set; } = new();
    public NamingSettings Naming { get; set; } = new();
    public FormatSettings Format { get; set; } = new();
    public MemorialSettings Memorial { get; set; } = new();
    public TechnicianSettings Technician { get; set; } = new();
    public AnnotationSettings Annotation { get; set; } = new();
    public KmlSettings Kml { get; set; } = new();
    public StreetSettings Streets { get; set; } = new();
    public SheetSettings Sheets { get; set; } = new();
    public List<RuleProfile> RuleProfiles { get; set; } = RuleProfile.Defaults();
    public string DefaultRuleProfile { get; set; } = "Lei 6.766/79 (mínimos federais)";
    public string LastExportFolder { get; set; } = "";

    public RuleProfile? FindProfile(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) name = DefaultRuleProfile;
        return RuleProfiles.FirstOrDefault(p => p.Name == name) ?? RuleProfiles.FirstOrDefault();
    }

    public GeoPointSettings Clone() => SettingsStore.Deserialize(SettingsStore.Serialize(this)) ?? new GeoPointSettings();
}

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    /// <summary>JSON compacto (para armazenamento no modelo).</summary>
    public static readonly JsonSerializerOptions Compact = new(Default) { WriteIndented = false };
}

public static class SettingsStore
{
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GeoPoint");

    public static string DefaultPath => Path.Combine(DefaultFolder, "configuracoes.json");

    public static string Serialize(GeoPointSettings s) => JsonSerializer.Serialize(s, JsonOptions.Default);

    public static GeoPointSettings? Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<GeoPointSettings>(json, JsonOptions.Default); }
        catch { return null; }
    }

    /// <summary>Carrega configurações; se o arquivo estiver corrompido, guarda uma cópia e usa os padrões.</summary>
    public static GeoPointSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return Normalize(new GeoPointSettings());
            var json = File.ReadAllText(path);
            var s = Deserialize(json);
            if (s is null)
            {
                try { File.Copy(path, path + ".corrompido-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true); } catch { }
                return Normalize(new GeoPointSettings());
            }
            return Normalize(s);
        }
        catch
        {
            return Normalize(new GeoPointSettings());
        }
    }

    public static void Save(GeoPointSettings s, string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Serialize(s));
        File.Move(tmp, path, true);
    }

    private static GeoPointSettings Normalize(GeoPointSettings s)
    {
        s.Recognition ??= new();
        s.Naming ??= new();
        s.Format ??= new();
        s.Memorial ??= new();
        s.Technician ??= new();
        s.Annotation ??= new();
        s.Kml ??= new();
        s.Streets ??= new();
        s.Sheets ??= new();
        s.Streets.Largura = Math.Clamp(s.Streets.Largura, 1, 200);
        s.Streets.Calcada = Math.Clamp(s.Streets.Calcada, 0, 50);
        s.Streets.RaioEsquina = Math.Clamp(s.Streets.RaioEsquina, 0, 50);
        s.Streets.NomeMm = Math.Clamp(s.Streets.NomeMm, 0.5, 20);
        s.Sheets.Scale = Math.Clamp(s.Sheets.Scale, 1, 100000);
        // Versão 2: pontos dos vértices maiores (1,2 → 2,2 mm), mais visíveis.
        if (s.Version < 2)
        {
            if (s.Annotation.DotMm <= 1.21) s.Annotation.DotMm = 2.2;
            s.Version = 2;
        }
        // Versão 3: numeração própria de cada lote, vias 2 m + 7 m com esquina de 2 m.
        if (s.Version < 3)
        {
            s.Naming.Numbering = VertexNumbering.Lote;
            if (Math.Abs(s.Streets.Largura - 12) < 1e-9) s.Streets.Largura = 7;
            if (Math.Abs(s.Streets.Calcada - 2.5) < 1e-9) s.Streets.Calcada = 2;
            if (Math.Abs(s.Streets.RaioEsquina - 3) < 1e-9) s.Streets.RaioEsquina = 2;
            s.Version = 3;
        }
        s.Annotation.DotMm = Math.Clamp(s.Annotation.DotMm, 0.2, 10);
        s.RuleProfiles ??= RuleProfile.Defaults();
        if (s.RuleProfiles.Count == 0) s.RuleProfiles = RuleProfile.Defaults();
        s.Memorial.CoordinateSystems ??= new();
        s.Memorial.CustomTm ??= CrsDef.Rtm(-45);
        s.Memorial.CustomTm.Kind = CrsKind.CustomTm;
        s.Memorial.Ptl ??= CrsDef.Ptl();
        s.Memorial.Ptl.Kind = CrsKind.LocalTopographic;
        if (s.Memorial.CustomTm.CustomK0 <= 0 || s.Memorial.CustomTm.CustomK0 > 2) s.Memorial.CustomTm.CustomK0 = 0.999995;
        if (s.Memorial.CoordinateSystems.Count == 0) s.Memorial.CoordinateSystems.Add(CrsDef.Utm());
        s.Recognition.ToleranceMm = Math.Clamp(s.Recognition.ToleranceMm, 0.01, 100);
        s.Recognition.GapSearchMm = Math.Clamp(s.Recognition.GapSearchMm, 0, 10000);
        s.Naming.VertexDigits = Math.Clamp(s.Naming.VertexDigits, 1, 5);
        s.Format.DistanceDecimals = Math.Clamp(s.Format.DistanceDecimals, 0, 6);
        s.Format.AreaDecimals = Math.Clamp(s.Format.AreaDecimals, 0, 6);
        s.Format.PlaneCoordDecimals = Math.Clamp(s.Format.PlaneCoordDecimals, 0, 6);
        s.Format.GeoSecondsDecimals = Math.Clamp(s.Format.GeoSecondsDecimals, 0, 6);
        s.Format.AzimuthSecondsDecimals = Math.Clamp(s.Format.AzimuthSecondsDecimals, 0, 4);
        s.Format.HectareDecimals = Math.Clamp(s.Format.HectareDecimals, 0, 8);
        s.Annotation.TextSizeMm = Math.Clamp(s.Annotation.TextSizeMm, 0.5, 20);
        s.Annotation.OffsetMm = Math.Clamp(s.Annotation.OffsetMm, 0, 50);
        s.Kml.FillOpacity = Math.Clamp(s.Kml.FillOpacity, 0, 1);
        s.Kml.LineWidth = Math.Clamp(s.Kml.LineWidth, 0.5, 20);
        return s;
    }
}
