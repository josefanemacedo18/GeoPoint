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
    /// <summary>O topo da vista em que o ponto foi definido é o norte (desenho feito com o norte para cima).</summary>
    ViewUp = 3,
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

/// <summary>Tipo de confrontante de uma divisa.</summary>
public enum ConfrontanteTipo
{
    /// <summary>Texto livre (proprietário, área etc.) ou sugestão automática quando vazio.</summary>
    Texto = 0,
    /// <summary>Outro lote do projeto (o nome acompanha o lote).</summary>
    Lote = 1,
    /// <summary>Via cadastrada no projeto.</summary>
    Via = 2,
}

/// <summary>Lado do lote em que fica a divisa (visto da via, de frente para o lote).</summary>
public enum LadoLote
{
    Automatico = 0,
    Frente = 1,
    Fundos = 2,
    LateralDireita = 3,
    LateralEsquerda = 4,
    Outro = 5,
}

/// <summary>Numeração dos vértices.</summary>
public enum VertexNumbering
{
    /// <summary>Numeração única no projeto inteiro (registro de vértices).</summary>
    Projeto = 0,
    /// <summary>Numeração única por quadra.</summary>
    Quadra = 1,
    /// <summary>Cada lote começa em P-01.</summary>
    Lote = 2,
}

public static class LadoInfo
{
    public static string Label(LadoLote l) => l switch
    {
        LadoLote.Frente => "Frente",
        LadoLote.Fundos => "Fundos",
        LadoLote.LateralDireita => "Lateral direita",
        LadoLote.LateralEsquerda => "Lateral esquerda",
        LadoLote.Outro => "Outro",
        _ => "Automático",
    };

    /// <summary>Expressão usada no memorial ("pela frente", "pelos fundos"...).</summary>
    public static string Phrase(LadoLote l) => l switch
    {
        LadoLote.Frente => "pela frente",
        LadoLote.Fundos => "pelos fundos",
        LadoLote.LateralDireita => "pela lateral direita",
        LadoLote.LateralEsquerda => "pela lateral esquerda",
        _ => "",
    };
}

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

public sealed class PointDto
{
    public double X { get; set; }
    public double Y { get; set; }
    public Vec2 ToVec() => new(X, Y);
}

/// <summary>Atributos de uma divisa do lote, identificada pelo ponto médio.</summary>
public sealed class EdgeInfo
{
    public double MX { get; set; }
    public double MY { get; set; }
    /// <summary>Texto do confrontante (usado quando Tipo = Texto).</summary>
    public string Confrontante { get; set; } = "";
    public ConfrontanteTipo Tipo { get; set; } = ConfrontanteTipo.Texto;
    /// <summary>Lote confrontante (Tipo = Lote).</summary>
    public string LotId { get; set; } = "";
    /// <summary>Via confrontante (Tipo = Via).</summary>
    public string ViaId { get; set; } = "";
    /// <summary>Lado definido pelo usuário (Automático = calculado pela testada).</summary>
    public LadoLote Lado { get; set; } = LadoLote.Automatico;
    public bool Testada { get; set; }
    /// <summary>Recuo/afastamento (m) para cálculo de área edificável.</summary>
    public double? Recuo { get; set; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Confrontante) && Tipo == ConfrontanteTipo.Texto && !Testada && Recuo is null && Lado == LadoLote.Automatico;

    public EdgeInfo CloneAt(Vec2 mid) => new()
    {
        MX = mid.X, MY = mid.Y, Confrontante = Confrontante, Tipo = Tipo, LotId = LotId, ViaId = ViaId,
        Lado = Lado, Testada = Testada, Recuo = Recuo,
    };
}

/// <summary>Vértice do registro único do projeto (numeração estável).</summary>
public sealed class VertexRecord
{
    /// <summary>Número no projeto (P-01 = 1).</summary>
    public int Number { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Número do vértice em cada quadra em que aparece.</summary>
    public Dictionary<string, int> QuadraNumbers { get; set; } = new();
    public DateTime Created { get; set; } = DateTime.Now;
}

/// <summary>Comportamento da ponta de uma via no desenho esquemático.</summary>
public enum ViaPonta { Termina = 0, Prolonga = 1 }

/// <summary>Via (rua, avenida, estrada) usada como confrontante e no desenho esquemático.</summary>
public sealed class ViaRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Nome { get; set; } = "Rua";
    /// <summary>Pista: largura do leito de meio-fio a meio-fio (m).</summary>
    public double Largura { get; set; } = 7;
    /// <summary>Largura de cada calçada (m).</summary>
    public double Calcada { get; set; } = 2;
    /// <summary>Prolongamento das pontas além da última testada (m), quando não há cruzamento.</summary>
    public double Prolongamento { get; set; }
    /// <summary>
    /// Linhas do meio-fio do lado dos lotes, selecionadas no desenho (coordenadas internas, m). Quando informadas,
    /// a rua é desenhada a partir delas: a pista (Largura) vai do meio-fio até o meio-fio oposto.
    /// </summary>
    public List<List<PointDto>> MeioFio { get; set; } = new();
    public override string ToString() => Nome;
}

/// <summary>Cruzamento de 2, 3 ou 4 vias (desenho esquemático).</summary>
public sealed class CruzamentoRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public List<string> ViaIds { get; set; } = new();
    /// <summary>Vias que atravessam o cruzamento (seguem do outro lado).</summary>
    public List<string> Atravessam { get; set; } = new();
    /// <summary>Raio de concordância do meio-fio nas esquinas (m).</summary>
    public double RaioEsquina { get; set; } = 2;
    /// <summary>Comprimento desenhado além do cruzamento para as vias que atravessam (m).</summary>
    public double Continuacao { get; set; } = 15;
}

public sealed class AnnotationRecord
{
    public string ViewId { get; set; } = "";
    public List<string> ElementIds { get; set; } = new();
    /// <summary>Chaves das anotações (divisas/vértices) para não repetir entre lotes vizinhos.</summary>
    public List<string> Keys { get; set; } = new();
    /// <summary>Escala da vista quando a anotação foi feita (para refazer se a escala mudar).</summary>
    public int Scale { get; set; }
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
    /// <summary>Fase do projeto a que o lote pertence.</summary>
    public string FaseId { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Quadra { get; set; } = "";
    public string Numero { get; set; } = "";
    public LotUse Uso { get; set; } = LotUse.Lote;
    public LotStatus Status { get; set; } = LotStatus.Ativo;
    /// <summary>Lote remanescente de um desmembramento.</summary>
    public bool IsRemanescente { get; set; }

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
    /// <summary>
    /// Vértices inseridos pelo usuário sobre as divisas (mudança de confrontante ao longo de uma divisa).
    /// São reaplicados quando a geometria do lote é atualizada.
    /// </summary>
    public List<PointDto> ExtraVertices { get; set; } = new();
    /// <summary>Hachuras do lote (por vista).</summary>
    public List<AnnotationRecord> Hatches { get; set; } = new();

    public List<HistoryEntry> History { get; set; } = new();
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Modified { get; set; } = DateTime.Now;

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            string n;
            if (string.IsNullOrWhiteSpace(Nome)) n = IsRemanescente ? "Lote Remanescente" : Uso == LotUse.Lote ? "Lote" : LotUseInfo.Label(Uso);
            else if ((Uso is LotUse.Lote or LotUse.Remanescente) && char.IsDigit(Nome.Trim()[0])) n = "Lote " + Nome.Trim();
            else n = Nome.Trim();
            if (IsRemanescente && !string.IsNullOrWhiteSpace(Nome) && !n.Contains("emanescente", StringComparison.OrdinalIgnoreCase)) n += " (Remanescente)";
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
    /// <summary>Diagnóstico: ângulo informado em ProjectPosition.Angle (rad).</summary>
    public double ReportedAngleRad { get; set; }
    /// <summary>Diagnóstico: como o ângulo foi obtido.</summary>
    public string Method { get; set; } = "";

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
    /// <summary>O usuário já confirmou qual é o norte do desenho (tela x norte do Revit).</summary>
    public bool NorthConfirmed { get; set; }
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
    /// <summary>Situação do processo (Regularização, Desmembramento, Remembramento...).</summary>
    public string Situacao { get; set; } = "Regularização";
    public string Cep { get; set; } = "";
    /// <summary>Imagem de localização usada no carimbo das pranchas.</summary>
    public string ImagemLocalizacao { get; set; } = "";

    public static IReadOnlyList<string> Situacoes { get; } = new[]
    {
        "Regularização", "Desmembramento", "Remembramento", "Unificação", "Desdobro", "Retificação de área",
        "Usucapião", "Loteamento", "Condomínio", "Levantamento planimétrico",
    };
}

public sealed class OperationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Fase em que a operação foi feita.</summary>
    public string FaseId { get; set; } = "";
    public OperationType Type { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    public List<string> ParentIds { get; set; } = new();
    public List<string> ChildIds { get; set; } = new();
    public string Notes { get; set; } = "";
    /// <summary>Lote remanescente (desmembramento).</summary>
    public string RemanescenteId { get; set; } = "";
    /// <summary>Vistas e pranchas geradas para a operação (UniqueId).</summary>
    public List<string> GeneratedIds { get; set; } = new();

    [JsonIgnore]
    public string Label => $"{(Type == OperationType.Desmembramento ? "Desmembramento" : Type == OperationType.Remembramento ? "Remembramento" : "Parcelamento")} – {Date:dd/MM/yyyy HH:mm}";
}

/// <summary>
/// Fase do projeto (ex.: 0 – Situação atual, 1 – Remembramento, 2 – Desmembramento). Cada fase parte dos lotes ativos
/// da fase anterior e tem seus próprios lotes, operações, numeração de vértices (começa do zero), vistas e pranchas.
/// </summary>
public sealed class FaseRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Numero { get; set; }
    public string Nome { get; set; } = "Situação atual";
    /// <summary>Situação do processo nesta fase (Regularização, Desmembramento...).</summary>
    public string Situacao { get; set; } = "";
    public string BaseFaseId { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
    public string Notes { get; set; } = "";
    /// <summary>Vistas de trabalho da fase (UniqueId).</summary>
    public List<string> ViewIds { get; set; } = new();
    /// <summary>Linhas ocultadas pelo GeoPoint em cada vista da fase (vista → elementos).</summary>
    public Dictionary<string, List<string>> HiddenByView { get; set; } = new();
    /// <summary>Registro de vértices da fase.</summary>
    public List<VertexRecord> Vertices { get; set; } = new();
    public Dictionary<string, int> QuadraVertexCounters { get; set; } = new();

    [JsonIgnore]
    public string Label => $"Fase {Numero} – {Nome}";
}

/// <summary>Ponto de controle importado (KML/KMZ, GPS): coordenadas geodésicas SIRGAS 2000/WGS 84.</summary>
public sealed class ControlPoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Nome { get; set; } = "";
    public string Descricao { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double? Altitude { get; set; }
    /// <summary>Arquivo de origem.</summary>
    public string Origem { get; set; } = "";
    /// <summary>Elementos desenhados (por vista).</summary>
    public List<AnnotationRecord> Drawings { get; set; } = new();
}

/// <summary>Prancha gerada pelo GeoPoint (para avisar antes de substituir uma prancha editada).</summary>
public sealed class GeneratedSheet
{
    public string Key { get; set; } = "";
    public string SheetId { get; set; } = "";
    public List<string> ViewIds { get; set; } = new();
    /// <summary>Impressão digital do conteúdo logo após a geração.</summary>
    public string Fingerprint { get; set; } = "";
    public string Title { get; set; } = "";
    public string FaseId { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
}

/// <summary>Dados do GeoPoint gravados no documento do Revit.</summary>
public sealed class ParcelProject
{
    public int SchemaVersion { get; set; } = 1;
    public GeoReferenceRecord Reference { get; set; } = new();
    public SharedFrame Frame { get; set; } = new();
    public ProjectInfo Info { get; set; } = new();
    /// <summary>Lotes de todas as fases (use <see cref="PhaseLots"/> para os da fase ativa).</summary>
    public List<LotRecord> Lots { get; set; } = new();
    public List<OperationRecord> Operations { get; set; } = new();
    public List<FaseRecord> Fases { get; set; } = new();
    public string ActiveFaseId { get; set; } = "";
    /// <summary>Registro de vértices de versões anteriores (migrado para a Fase 0).</summary>
    [JsonPropertyName("Vertices")]
    public List<VertexRecord>? LegacyVertices { get; set; }
    [JsonPropertyName("QuadraVertexCounters")]
    public Dictionary<string, int>? LegacyQuadraCounters { get; set; }
    /// <summary>Desenhos esquemáticos de vias por vista (elementos criados).</summary>
    public List<AnnotationRecord> StreetDrawings { get; set; } = new();
    public List<ViaRecord> Vias { get; set; } = new();
    public List<CruzamentoRecord> Cruzamentos { get; set; } = new();
    public List<ControlPoint> ControlPoints { get; set; } = new();
    public List<GeneratedSheet> Sheets { get; set; } = new();

    public ViaRecord? FindVia(string? id) => string.IsNullOrWhiteSpace(id) ? null : Vias.FirstOrDefault(v => v.Id == id);

    /// <summary>Fase ativa (cria a Fase 0 e migra dados antigos quando necessário).</summary>
    [JsonIgnore]
    public FaseRecord Fase
    {
        get
        {
            if (Fases.Count == 0)
            {
                var f0 = new FaseRecord { Numero = 0, Nome = "Situação atual", Situacao = Info?.Situacao ?? "" };
                if (LegacyVertices is { Count: > 0 }) f0.Vertices = LegacyVertices;
                if (LegacyQuadraCounters is { Count: > 0 }) f0.QuadraVertexCounters = LegacyQuadraCounters;
                Fases.Add(f0);
                ActiveFaseId = f0.Id;
            }
            LegacyVertices = null;
            LegacyQuadraCounters = null;
            var f = Fases.FirstOrDefault(x => x.Id == ActiveFaseId);
            if (f is null)
            {
                f = Fases.OrderBy(x => x.Numero).First();
                ActiveFaseId = f.Id;
            }
            // Lotes e operações sem fase (dados antigos ou recém-criados) pertencem à fase ativa.
            foreach (var l in Lots) if (string.IsNullOrEmpty(l.FaseId)) l.FaseId = f.Id;
            foreach (var o in Operations) if (string.IsNullOrEmpty(o.FaseId)) o.FaseId = f.Id;
            return f;
        }
    }

    public FaseRecord? FindFase(string? id) => string.IsNullOrEmpty(id) ? null : Fases.FirstOrDefault(f => f.Id == id);

    /// <summary>Registro de vértices da fase ativa.</summary>
    [JsonIgnore]
    public List<VertexRecord> Vertices => Fase.Vertices;

    /// <summary>Próximo número de vértice em cada quadra (fase ativa).</summary>
    [JsonIgnore]
    public Dictionary<string, int> QuadraVertexCounters => Fase.QuadraVertexCounters;

    /// <summary>Lotes da fase ativa (ativos e arquivados).</summary>
    [JsonIgnore]
    public IEnumerable<LotRecord> PhaseLots { get { var id = Fase.Id; return Lots.Where(l => l.FaseId == id); } }

    /// <summary>Lotes ativos da fase ativa.</summary>
    [JsonIgnore]
    public IEnumerable<LotRecord> ActiveLots => PhaseLots.Where(l => l.Status == LotStatus.Ativo);

    /// <summary>Operações da fase ativa.</summary>
    [JsonIgnore]
    public IEnumerable<OperationRecord> PhaseOperations { get { var id = Fase.Id; return Operations.Where(o => o.FaseId == id); } }

    /// <summary>Situação do processo na fase ativa (ou a dos dados do imóvel).</summary>
    [JsonIgnore]
    public string Situacao => string.IsNullOrWhiteSpace(Fase.Situacao) ? Info?.Situacao ?? "" : Fase.Situacao;

    public LotRecord? FindLot(string id) => Lots.FirstOrDefault(l => l.Id == id);

    public string NextLotName(string prefix = "Lote")
    {
        int max = 0;
        foreach (var l in PhaseLots)
        {
            var n = l.Numero;
            if (int.TryParse(n, out int k)) max = Math.Max(max, k);
        }
        return (max + 1).ToString("00");
    }
}
