using System.Xml.Linq;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Model;
using GeoPoint.Core.Office;
using GeoPoint.Core.Settings;
using GeoPoint.Core.Urban;

namespace GeoPoint.Core.Memorial;

public enum MemorialKind
{
    /// <summary>Memorial de um ou mais lotes.</summary>
    Lotes = 0,
    Desmembramento = 1,
    Remembramento = 2,
    /// <summary>Loteamento/parcelamento: gleba, quadro de áreas e todos os lotes.</summary>
    Loteamento = 3,
}

public sealed class MemorialRequest
{
    public MemorialKind Kind { get; init; }
    public List<LotRecord> Lots { get; init; } = new();
    public OperationRecord? Operation { get; init; }
    public DateTime Date { get; init; } = DateTime.Today;
}

/// <summary>Gera o memorial descritivo (.docx) a partir dos lotes e do modelo.</summary>
public sealed class MemorialGenerator
{
    /// <summary>Memorial do lote unido por remembramento: não cita matrícula (nem histórico).</summary>
    private bool _noMatricula;

    private readonly ProjectContext _ctx;
    private readonly MemorialPhrases _ph;
    private readonly ValueFormatter _fmt;
    private readonly MemorialSettings _ms;
    private readonly List<string> _warnings = new();

    public MemorialGenerator(ProjectContext ctx, MemorialPhrases phrases)
    {
        _ctx = ctx;
        _ph = phrases;
        _fmt = new ValueFormatter(ctx.Settings.Format);
        _ms = ctx.Settings.Memorial;
    }

    public IReadOnlyList<string> Warnings => _warnings;

    private static readonly ParaSpec Body = new() { Align = ParaAlign.Justify, SpaceAfterPt = 6, LineSpacing = 1.5 };
    private static readonly ParaSpec H1 = new() { Align = ParaAlign.Left, SpaceBeforePt = 12, SpaceAfterPt = 6, KeepNext = true };
    private static readonly ParaSpec H2 = new() { Align = ParaAlign.Left, SpaceBeforePt = 8, SpaceAfterPt = 4, KeepNext = true };
    private static readonly ParaSpec Caption = new() { Align = ParaAlign.Center, SpaceBeforePt = 6, SpaceAfterPt = 3, KeepNext = true };

    // ------------------------------------------------------------------ modelo padrão

    /// <summary>Modelo padrão do GeoPoint (pode ser exportado e editado no Word).</summary>
    public static byte[] DefaultTemplate()
    {
        var center = new ParaSpec { Align = ParaAlign.Center, SpaceAfterPt = 0 };
        var label = new ParaSpec { Align = ParaAlign.Left, SpaceAfterPt = 3, LineSpacing = 1.15 };
        XElement Field(string lab, string ph) => Wml.Paragraph(label, new RunSpec(lab + " ", true), new RunSpec(ph));
        var body = new List<XElement>
        {
            Wml.Paragraph(center, new RunSpec("{{TITULO}}", true, SizePt: 14)),
            Wml.Paragraph(new ParaSpec { Align = ParaAlign.Center, SpaceAfterPt = 12 }, new RunSpec("{{SUBTITULO}}", true, SizePt: 12)),
            Field("PROPRIETÁRIO(S):", "{{PROPRIETARIO?}}"),
            Field("CPF/CNPJ:", "{{DOCUMENTO?}}"),
            Field("IMÓVEL:", "{{IMOVEL?}}"),
            Field("ENDEREÇO:", "{{ENDERECO?}}"),
            Field("MATRÍCULA:", "{{MATRICULA?}}"),
            Field("CARTÓRIO:", "{{CARTORIO?}}"),
            Field("INSCRIÇÃO MUNICIPAL:", "{{INSCRICAO?}}"),
            Field("CCIR:", "{{CCIR?}}"),
            Field("CAR:", "{{CAR?}}"),
            Field("MUNICÍPIO:", "{{MUNICIPIO?}}"),
            Field("ESTADO:", "{{UF?}}"),
            Field("COMARCA:", "{{COMARCA?}}"),
            Field("ÁREA:", "{{AREA?}}"),
            Field("PERÍMETRO:", "{{PERIMETRO?}}"),
            Field("FINALIDADE:", "{{FINALIDADE?}}"),
            Wml.Paragraph("{{CORPO}}"),
            Wml.Paragraph(new ParaSpec { Align = ParaAlign.Right, SpaceBeforePt = 18, SpaceAfterPt = 36 }, new RunSpec("{{LOCAL_DATA}}")),
            Wml.Paragraph(center, new RunSpec("_______________________________________________")),
            Wml.Paragraph(center, new RunSpec("{{TECNICO_NOME?}}", true)),
            Wml.Paragraph(center, new RunSpec("{{TECNICO_PROFISSAO?}}")),
            Wml.Paragraph(center, new RunSpec("{{TECNICO_REGISTRO_TEXTO?}}")),
            Wml.Paragraph(center, new RunSpec("{{RRT_ART_TEXTO?}}")),
            Wml.Paragraph(center, new RunSpec("{{TECNICO_EMPRESA?}}")),
        };
        return DocxPackage.Create(body, "Calibri", 11);
    }

    // ------------------------------------------------------------------ geração

    public byte[] Generate(MemorialRequest req, byte[]? template = null)
    {
        _warnings.Clear();
        _noMatricula = false;
        if (req.Lots.Count == 0) throw new InvalidOperationException("Nenhum lote selecionado para o memorial.");
        var info = _ctx.Project.Info;
        var tech = _ctx.Settings.Technician;
        var lots = req.Lots;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var blocks = new Dictionary<string, IReadOnlyList<XElement>>(StringComparer.OrdinalIgnoreCase);

        // Lote principal para o cabeçalho.
        LotRecord main = lots[0];
        double headerArea, headerPerim;
        string subtitle;
        var corpo = new List<XElement>();
        switch (req.Kind)
        {
            case MemorialKind.Desmembramento:
            {
                var parent = OperationParent(req) ?? main;
                main = parent;
                var children = OperationChildren(req, parent);
                subtitle = "DESMEMBRAMENTO";
                var pr = _ctx.Report(parent);
                headerArea = pr.Area(_ms.AreaType);
                headerPerim = pr.Perimeter(_ms.DistanceType);
                corpo.Add(Heading("1. SITUAÇÃO ATUAL – ÁREA A DESMEMBRAR"));
                corpo.AddRange(LotSection(parent, includeTitle: true));
                corpo.Add(Heading("2. SITUAÇÃO PROPOSTA – ÁREAS RESULTANTES"));
                int k = 1;
                foreach (var c in children)
                {
                    corpo.Add(SubHeading($"2.{k++}. {c.DisplayName.ToUpperInvariant()}"));
                    corpo.AddRange(LotSection(c, includeTitle: false));
                }
                corpo.Add(Heading("3. QUADRO RESUMO"));
                corpo.AddRange(SummaryTable(children, parent, "Área original"));
                break;
            }
            case MemorialKind.Remembramento:
            {
                var originals = OperationParents(req);
                var result = OperationResult(req) ?? main;
                main = result;
                subtitle = "REMEMBRAMENTO";
                var rr = _ctx.Report(result);
                headerArea = rr.Area(_ms.AreaType);
                headerPerim = rr.Perimeter(_ms.DistanceType);
                corpo.Add(Heading("1. SITUAÇÃO ATUAL – ÁREAS A REMEMBRAR"));
                corpo.AddRange(SummaryTable(originals, null, null));
                int k = 1;
                foreach (var o in originals)
                {
                    corpo.Add(SubHeading($"1.{k++}. {o.DisplayName.ToUpperInvariant()}"));
                    corpo.AddRange(LotSection(o, includeTitle: false, includeTables: false));
                }
                corpo.Add(Heading("2. SITUAÇÃO PROPOSTA – ÁREA REMEMBRADA"));
                corpo.AddRange(LotSection(result, includeTitle: true));
                corpo.Add(Heading("3. QUADRO RESUMO"));
                corpo.AddRange(SummaryTable(new List<LotRecord> { result }, null, null, originals));
                break;
            }
            case MemorialKind.Loteamento:
            {
                subtitle = "LOTEAMENTO / PARCELAMENTO DO SOLO";
                var gleba = lots.FirstOrDefault(l => l.Uso == LotUse.Gleba);
                var parts = ProjectContext.SortLots(lots.Where(l => l.Uso != LotUse.Gleba)).ToList();
                int sec = 1;
                if (gleba is not null)
                {
                    main = gleba;
                    var gr = _ctx.Report(gleba);
                    headerArea = gr.Area(_ms.AreaType);
                    headerPerim = gr.Perimeter(_ms.DistanceType);
                    corpo.Add(Heading($"{sec++}. GLEBA"));
                    corpo.AddRange(LotSection(gleba, includeTitle: true));
                }
                else
                {
                    headerArea = parts.Sum(l => _ctx.Report(l).Area(_ms.AreaType));
                    headerPerim = double.NaN;
                }
                corpo.Add(Heading($"{sec++}. QUADRO DE ÁREAS"));
                corpo.AddRange(QuadroTable());
                corpo.Add(Heading($"{sec++}. DESCRIÇÃO DAS ÁREAS"));
                string? lastQuadra = null;
                int k = 1;
                foreach (var l in parts)
                {
                    if (!string.IsNullOrWhiteSpace(l.Quadra) && l.Quadra != lastQuadra)
                    {
                        corpo.Add(Wml.Paragraph(H1, new RunSpec($"QUADRA {l.Quadra.ToUpperInvariant()}", true)));
                        lastQuadra = l.Quadra;
                    }
                    corpo.Add(SubHeading($"{sec - 1}.{k++}. {l.DisplayName.ToUpperInvariant()} ({LotUseInfo.Label(l.Uso)})"));
                    corpo.AddRange(LotSection(l, includeTitle: false));
                }
                break;
            }
            default:
            {
                var sit = (_ctx.Project.Situacao ?? "").Trim();
                subtitle = sit.Length > 0 && !sit.Equals("Levantamento planimétrico", StringComparison.OrdinalIgnoreCase)
                    ? sit.ToUpperInvariant() : lots.Count == 1 ? "" : "LOTES";
                if (lots.Count == 1)
                {
                    var r = _ctx.Report(main);
                    headerArea = r.Area(_ms.AreaType);
                    headerPerim = r.Perimeter(_ms.DistanceType);
                    // Lote resultante de operação: o memorial trata só deste lote (sem origem nem os demais lotes);
                    // o subtítulo traz o procedimento.
                    var origin = req.Operation is { } ro && ro.ChildIds.Contains(main.Id) ? ro : _ctx.Project.OperationOf(main);
                    _noMatricula = origin?.Type == OperationType.Remembramento;   // lote unido: sem matrícula no memorial
                    if (origin is not null)
                        subtitle = origin.Type switch
                        {
                            OperationType.Desmembramento => main.IsRemanescente ? "DESMEMBRAMENTO – LOTE REMANESCENTE" : "DESMEMBRAMENTO",
                            OperationType.Remembramento => "REMEMBRAMENTO",
                            _ => "PARCELAMENTO",
                        };
                    corpo.Add(Heading("DESCRIÇÃO DO PERÍMETRO"));
                    corpo.AddRange(LotSection(main, includeTitle: false));
                }
                else
                {
                    headerArea = lots.Sum(l => _ctx.Report(l).Area(_ms.AreaType));
                    headerPerim = double.NaN;
                    int k = 1;
                    foreach (var l in lots)
                    {
                        corpo.Add(Heading($"{k++}. {l.DisplayName.ToUpperInvariant()}"));
                        corpo.AddRange(LotSection(l, includeTitle: false));
                    }
                    corpo.Add(Heading($"{k}. QUADRO RESUMO"));
                    corpo.AddRange(SummaryTable(lots, null, null));
                }
                break;
            }
        }

        // As "notas técnicas" do final do memorial foram retiradas (o sistema de referência consta no quadro de coordenadas).

        blocks["CORPO"] = corpo;

        string ownerName = _ctx.Project.EffectiveOwner(main);
        values["TITULO"] = "MEMORIAL DESCRITIVO";
        values["SUBTITULO"] = subtitle;
        values["PROPRIETARIO"] = ownerName;
        values["DOCUMENTO"] = _ctx.Project.EffectiveDocumento(main);
        values["IMOVEL"] = ImovelText(main, req.Kind);
        values["ENDERECO"] = AddressText(main);
        values["BAIRRO"] = info.Bairro;
        values["MATRICULA"] = req.Kind == MemorialKind.Remembramento
            ? string.Join(", ", OperationParents(req).Select(o => o.Matricula).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct())
            : _noMatricula ? "" : _ctx.Project.EffectiveMatricula(main);
        values["CARTORIO"] = info.Cartorio;
        values["INSCRICAO"] = info.InscricaoMunicipal;
        values["CCIR"] = info.Ccir;
        values["CAR"] = info.Car;
        values["MUNICIPIO"] = info.Municipio;
        values["UF"] = info.UF;
        values["COMARCA"] = info.Comarca;
        values["FINALIDADE"] = info.Finalidade;
        values["AREA"] = _fmt.Area(headerArea);
        values["AREA_HA"] = _fmt.AreaHa(headerArea);
        values["PERIMETRO"] = double.IsNaN(headerPerim) ? "" : _fmt.Distance(headerPerim);
        values["DATA"] = ValueFormatter.DateLong(req.Date);
        values["LOCAL_DATA"] = string.IsNullOrWhiteSpace(info.Municipio)
            ? ValueFormatter.DateLong(req.Date)
            : $"{info.Municipio}{(string.IsNullOrWhiteSpace(info.UF) ? "" : " – " + info.UF)}, {ValueFormatter.DateLong(req.Date)}";
        values["TECNICO_NOME"] = tech.Nome;
        values["TECNICO_PROFISSAO"] = tech.Profissao;
        values["TECNICO_CONSELHO"] = tech.Conselho;
        values["TECNICO_REGISTRO"] = tech.Registro;
        values["TECNICO_REGISTRO_TEXTO"] = string.IsNullOrWhiteSpace(tech.Registro) ? "" : $"{(string.IsNullOrWhiteSpace(tech.Conselho) ? "Registro" : tech.Conselho)}: {tech.Registro}";
        values["TECNICO_EMPRESA"] = tech.Empresa;
        values["RRT_ART"] = info.RrtArt;
        values["RRT_ART_TEXTO"] = string.IsNullOrWhiteSpace(info.RrtArt) ? "" : $"{(tech.Conselho.Equals("CREA", StringComparison.OrdinalIgnoreCase) ? "ART" : "RRT")} nº {info.RrtArt}";
        values["LOTE"] = main.DisplayName;
        values["QUADRA"] = main.Quadra;

        if (string.IsNullOrWhiteSpace(tech.Nome)) _warnings.Add("Responsável técnico não informado (Configurações › Memorial).");
        if (!_ctx.Coords.HasGeoreference) _warnings.Add("Projeto sem ponto de referência: o memorial usa coordenadas locais do projeto.");

        var tpl = template ?? DefaultTemplate();
        return DocxTemplate.Fill(tpl, values, blocks);
    }

    // ------------------------------------------------------------------ partes do texto

    private LotRecord? OperationParent(MemorialRequest req) =>
        req.Operation is { } op && op.ParentIds.Count > 0 ? _ctx.Project.FindLot(op.ParentIds[0]) : null;

    private List<LotRecord> OperationChildren(MemorialRequest req, LotRecord parent)
    {
        if (req.Operation is { } op)
            return ProjectContext.SortLots(op.ChildIds.Select(id => _ctx.Project.FindLot(id)).OfType<LotRecord>()).ToList();
        return req.Lots.Where(l => l.Id != parent.Id).ToList();
    }

    private List<LotRecord> OperationParents(MemorialRequest req)
    {
        if (req.Operation is { } op)
            return ProjectContext.SortLots(op.ParentIds.Select(id => _ctx.Project.FindLot(id)).OfType<LotRecord>()).ToList();
        return req.Lots.Skip(1).ToList();
    }

    private LotRecord? OperationResult(MemorialRequest req) =>
        req.Operation is { } op && op.ChildIds.Count > 0 ? _ctx.Project.FindLot(op.ChildIds[0]) : req.Lots.FirstOrDefault();

    private static string FirstNonEmpty(params string?[] s) => s.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";

    private string ImovelText(LotRecord lot, MemorialKind kind)
    {
        var info = _ctx.Project.Info;
        string den = string.IsNullOrWhiteSpace(info.Denominacao) ? (info.TipoImovel == "Rural" ? "Imóvel Rural" : "Lote Urbano") : info.Denominacao;
        bool named = !string.IsNullOrWhiteSpace(lot.Nome) || !string.IsNullOrWhiteSpace(lot.Quadra);
        if (kind == MemorialKind.Lotes && named && !string.Equals(lot.DisplayName, den, StringComparison.OrdinalIgnoreCase))
            return $"{den} – {lot.DisplayName}";
        return den;
    }

    private string AddressText(LotRecord lot)
    {
        var info = _ctx.Project.Info;
        string addr = FirstNonEmpty(lot.Endereco, info.Endereco);
        if (!string.IsNullOrWhiteSpace(info.Bairro) && !addr.Contains(info.Bairro, StringComparison.OrdinalIgnoreCase))
            addr = string.IsNullOrWhiteSpace(addr) ? info.Bairro : $"{addr}, {info.Bairro}";
        return addr;
    }

    private static XElement Heading(string text) => Wml.Paragraph(H1, new RunSpec(text, true));
    private static XElement SubHeading(string text) => Wml.Paragraph(H2, new RunSpec(text, true));

    private CrsDef PrimaryCrs()
    {
        var def = _ms.CoordinateSystems.FirstOrDefault() ?? CrsDef.Utm();
        if (def.Kind != CrsKind.ProjectLocal && !_ctx.Coords.HasGeoreference) return CrsDef.ProjectLocalDef();
        return def;
    }

    private List<CrsDef> TableCrs()
    {
        var list = _ms.CoordinateSystems.Count > 0 ? _ms.CoordinateSystems : new List<CrsDef> { CrsDef.Utm() };
        if (!_ctx.Coords.HasGeoreference) return new List<CrsDef> { CrsDef.ProjectLocalDef() };
        return list.Take(3).ToList();
    }

    /// <summary>Seção de um lote: dados, descrição perimétrica e quadros.</summary>
    public List<XElement> LotSection(LotRecord lot, bool includeTitle, bool includeTables = true)
    {
        var res = new List<XElement>();
        var r = _ctx.Report(lot);
        if (includeTitle) res.Add(SubHeading(lot.DisplayName.ToUpperInvariant()));

        var facts = new List<string> { $"Área: {AreaText(r)}", $"Perímetro: {_fmt.Distance(r.Perimeter(_ms.DistanceType))}" };
        if (!_noMatricula && _ctx.Project.EffectiveMatricula(lot) is { Length: > 0 } mat) facts.Add($"Matrícula: {mat}");
        if (!string.IsNullOrWhiteSpace(lot.Proprietario) && lot.Proprietario != _ctx.Project.Info.Proprietario) facts.Add($"Proprietário: {lot.Proprietario}");
        if (r.Testada > 0) facts.Add($"Testada: {_fmt.Distance(r.Testada)}");
        if (r.AreaDeducoes > 0) facts.Add($"Área líquida (descontadas {string.Join(", ", r.Deducoes.Select(d => d.Lot.DisplayName))}): {_fmt.Area(r.AreaLiquida)}");
        res.Add(Wml.Paragraph(new ParaSpec { Align = ParaAlign.Left, SpaceAfterPt = 6 }, new RunSpec(string.Join("; ", facts) + ".")));

        res.Add(Wml.Paragraph(Body, new RunSpec(PerimeterDescription(r))));
        if (_ms.IncludeConfrontantesTable)
            foreach (var line in Confrontacoes(r)) res.Add(line);
        foreach (var t in HoleDescriptions(lot)) res.Add(Wml.Paragraph(Body, new RunSpec(t)));

        // Área edificada: projeção, pavimentos, área construída, taxa de ocupação e distância até cada divisa.
        if (_ms.IncludeBuildings)
        {
            var bs = GeoPoint.Core.Urban.BuildingAnalyzer.Of(_ctx.Project, lot);
            if (bs.Count > 0)
            {
                res.Add(Wml.Paragraph(new ParaSpec { Align = ParaAlign.Left, SpaceBeforePt = 6, SpaceAfterPt = 2 }, new RunSpec("ÁREA EDIFICADA", true)));
                var reps = bs.Select(b => GeoPoint.Core.Urban.BuildingAnalyzer.Analyze(r, b)).ToList();
                foreach (var t in GeoPoint.Core.Urban.BuildingAnalyzer.MemorialText(reps, _fmt)) res.Add(Wml.Paragraph(Body, new RunSpec(t)));
                if (reps.Count > 1)
                    res.Add(Wml.Paragraph(Body, new RunSpec($"Total edificado: projeção de {_fmt.Area(reps.Sum(x => x.Area))} (taxa de ocupação de {_fmt.Number(reps.Sum(x => x.TaxaOcupacao), 2)}%) e área construída de {_fmt.Area(reps.Sum(x => x.AreaConstruida))}.")));
            }
        }

        if (includeTables && _ms.IncludeVertexTable)
        {
            res.Add(Wml.Paragraph(Caption, new RunSpec($"Quadro de coordenadas – {lot.DisplayName}", true, SizePt: 9)));
            res.Add(CoordinateTable(r));
            res.Add(Wml.Paragraph(Caption, new RunSpec($"Quadro de segmentos – {lot.DisplayName}", true, SizePt: 9)));
            res.Add(SegmentTable(r));
            res.Add(Wml.EmptyParagraph(6));
        }
        return res;
    }

    /// <summary>Resumo das confrontações por lado ("Frente: Rua E – 50,00 m").</summary>
    private IEnumerable<XElement> Confrontacoes(LotReport r)
    {
        var groups = r.Segments.Where(s => s.Lado != LadoLote.Automatico)
            .GroupBy(s => s.Lado).OrderBy(g => g.Key switch
            {
                LadoLote.Frente => 0, LadoLote.LateralDireita => 1, LadoLote.Fundos => 2, LadoLote.LateralEsquerda => 3, _ => 4,
            }).ToList();
        if (groups.Count == 0) yield break;
        yield return Wml.Paragraph(new ParaSpec { SpaceBeforePt = 4, SpaceAfterPt = 2, KeepNext = true }, new RunSpec("Confrontações:", true));
        foreach (var g in groups)
        {
            var parts = g.Select(s => $"{(string.IsNullOrWhiteSpace(s.Confrontante) ? "(não informado)" : s.Confrontante)} – {_fmt.Distance(s.Length(_ms.DistanceType))} ({s.From.Name}–{s.To.Name})");
            yield return Wml.Paragraph(new ParaSpec { SpaceAfterPt = 1, LeftIndentCm = 0.5 },
                new RunSpec(LadoInfo.Label(g.Key) + ": ", true), new RunSpec(string.Join("; ", parts) + "."));
        }
    }

    private string AreaText(LotReport r) => _ms.AreaType switch
    {
        AreaType.Utm when r.AreaUtm is not null => _fmt.Area(r.AreaUtm.Value) + " (plano UTM)",
        AreaType.Both when r.AreaUtm is not null => $"{_fmt.Area(r.AreaLocal)} no plano topográfico local ({_fmt.Area(r.AreaUtm.Value)} no plano UTM)",
        _ => _fmt.Area(r.AreaLocal),
    };

    private string VertexCoordText(VertexReport v, CrsDef def)
    {
        var c = _ctx.Coords.ToCrs(v.Internal, def);
        if (c is null)
        {
            var s = v.Shared;
            return $"E {_fmt.Plane(s.X)} m e N {_fmt.Plane(s.Y)} m (sistema local)";
        }
        return _fmt.CoordText(c.Value, def);
    }

    /// <summary>Texto corrido da descrição perimétrica.</summary>
    public string PerimeterDescription(LotReport r)
    {
        var def = PrimaryCrs();
        var vals = new Dictionary<string, string>();
        var sb = new System.Text.StringBuilder();
        var first = r.Vertices[0];
        vals["VERTICE"] = first.Name;
        vals["COORDENADAS"] = VertexCoordText(first, def);
        sb.Append(MemorialPhrases.Apply(_ph.Inicio, vals));

        for (int i = 0; i < r.Segments.Count; i++)
        {
            var s = r.Segments[i];
            bool last = i == r.Segments.Count - 1;
            vals = new Dictionary<string, string>
            {
                ["CONFRONTANTE"] = string.IsNullOrWhiteSpace(s.Confrontante) ? _ph.ConfrontanteVazio : s.Confrontante,
                ["LADO"] = _ms.IncludeSide ? LadoInfo.Phrase(s.Lado) : "",
                ["DIRECAO"] = _fmt.DirectionPhrase(s.Azimuth(_ms.AzimuthType)),
                ["AZIMUTE"] = _fmt.Angle(s.Azimuth(_ms.AzimuthType)),
                ["DISTANCIA"] = _fmt.Distance(s.IsArc ? s.Chord(_ms.DistanceType) : s.Length(_ms.DistanceType)),
                ["VERTICE_SEGUINTE"] = s.To.Name,
                ["COORDENADAS_SEGUINTE"] = VertexCoordText(s.To, def),
                ["VERTICE_ATUAL"] = s.From.Name,
            };
            if (s.IsArc)
            {
                vals["RAIO"] = _fmt.Distance(s.Radius ?? 0);
                vals["DESENVOLVIMENTO"] = _fmt.Distance(s.Length(_ms.DistanceType));
                vals["CORDA"] = _fmt.Distance(s.Chord(_ms.DistanceType));
                vals["ANGULO_CENTRAL"] = AngleText.FormatDms(s.CentralAngleDeg ?? 0, _fmt.F.AzimuthSecondsDecimals);
                sb.Append(MemorialPhrases.Apply(last ? _ph.ArcoFinal : _ph.Arco, vals));
            }
            else sb.Append(MemorialPhrases.Apply(last ? _ph.RetaFinal : _ph.Reta, vals));
        }
        var close = new Dictionary<string, string>
        {
            ["AREA"] = AreaText(r),
            ["PERIMETRO"] = _fmt.Distance(r.Perimeter(_ms.DistanceType)),
        };
        sb.Append(MemorialPhrases.Apply(_ph.Fechamento, close));
        // Uma única frase do referencial (as antigas "notas técnicas" foram retiradas).
        if (!string.IsNullOrWhiteSpace(_ph.Referencial) && ReferenceSystemText() is { Length: > 0 } sys)
            sb.Append(MemorialPhrases.Apply(_ph.Referencial, new Dictionary<string, string> { ["SISTEMA"] = sys }));
        return sb.ToString();
    }

    private IEnumerable<string> HoleDescriptions(LotRecord lot)
    {
        if (lot.Holes.Count == 0) yield break;
        int k = 1;
        foreach (var h in lot.Holes)
        {
            if (h.Count == 0) continue;
            var temp = new LotRecord { Id = lot.Id + "-furo" + k, Outer = h, Nome = $"Ilha {k}", Clockwise = lot.Clockwise };
            var r = _ctx.Report(temp);
            var head = MemorialPhrases.Apply(_ph.FuroInicio, new Dictionary<string, string> { ["NOME_FURO"] = $"ilha {k}", ["AREA_FURO"] = _fmt.Area(r.AreaLocal) });
            yield return head + PerimeterDescription(r);
            k++;
        }
    }

    private XElement CoordinateTable(LotReport r)
    {
        var defs = TableCrs();
        bool alt = _ms.IncludeAltitude && r.Vertices.Any(v => v.Elevation is not null);
        int cols = 1 + defs.Count * 2 + (alt ? 1 : 0);
        double total = 16.0;
        double vw = 1.6, aw = alt ? 1.8 : 0;
        double cw = (total - vw - aw) / (defs.Count * 2);
        var widths = new List<double> { vw };
        for (int i = 0; i < defs.Count * 2; i++) widths.Add(cw);
        if (alt) widths.Add(aw);

        var rows = new List<IReadOnlyList<CellSpec>>();
        var head1 = new List<CellSpec> { new("Vértice", true, ParaAlign.Center, "D9E2F3") };
        var head2 = new List<CellSpec> { new("", true, ParaAlign.Center, "D9E2F3") };
        foreach (var d in defs)
        {
            head1.Add(new CellSpec(d.ShortName(_ctx.Coords.AutoZone), true, ParaAlign.Center, "D9E2F3") { GridSpan = 2 });
            var (a, b) = _fmt.ColumnTitles(d);
            head2.Add(new CellSpec(a, true, ParaAlign.Center, "D9E2F3"));
            head2.Add(new CellSpec(b, true, ParaAlign.Center, "D9E2F3"));
        }
        if (alt) { head1.Add(new CellSpec("Altitude (m)", true, ParaAlign.Center, "D9E2F3")); head2.Add(new CellSpec("", true, ParaAlign.Center, "D9E2F3")); }
        rows.Add(head1);
        rows.Add(head2);
        foreach (var v in r.Vertices)
        {
            var row = new List<CellSpec> { new(v.Name, false, ParaAlign.Center) };
            foreach (var d in defs)
            {
                var c = _ctx.Coords.ToCrs(v.Internal, d);
                if (c is null) { row.Add(new CellSpec("–", false, ParaAlign.Center)); row.Add(new CellSpec("–", false, ParaAlign.Center)); continue; }
                var (a, b) = _fmt.Columns(c.Value, d);
                row.Add(new CellSpec(a, false, ParaAlign.Center));
                row.Add(new CellSpec(b, false, ParaAlign.Center));
            }
            if (alt) row.Add(new CellSpec(v.Elevation is double z ? _fmt.Number(z, 3) : "–", false, ParaAlign.Center));
            rows.Add(row);
        }
        _ = cols;
        return Wml.Table(rows, widths, headerRepeat: true, fontPt: 8.5);
    }

    private XElement SegmentTable(LotReport r)
    {
        bool side = r.Segments.Any(x => x.Lado != LadoLote.Automatico);
        var widths = side ? new List<double> { 2.3, 2.4, 2.7, 3.8, 4.8 } : new List<double> { 2.6, 3.0, 4.2, 6.2 };
        string angTitle = _fmt.F.AngleStyle == AngleStyle.Rumo ? "Rumo" : "Azimute";
        var head = new List<CellSpec> { new("Segmento", true, ParaAlign.Center, "D9E2F3") };
        if (side) head.Add(new CellSpec("Lado", true, ParaAlign.Center, "D9E2F3"));
        head.Add(new CellSpec(angTitle, true, ParaAlign.Center, "D9E2F3"));
        head.Add(new CellSpec("Distância (m)", true, ParaAlign.Center, "D9E2F3"));
        head.Add(new CellSpec("Confrontante", true, ParaAlign.Center, "D9E2F3"));
        var rows = new List<IReadOnlyList<CellSpec>> { head };
        foreach (var s in r.Segments)
        {
            string dist = s.IsArc
                ? $"Arco: D = {_fmt.DistanceValue(s.Length(_ms.DistanceType))}; R = {_fmt.DistanceValue(s.Radius ?? 0)}; corda = {_fmt.DistanceValue(s.Chord(_ms.DistanceType))}"
                : _fmt.DistanceValue(s.Length(_ms.DistanceType));
            var row = new List<CellSpec> { new($"{s.From.Name} – {s.To.Name}", false, ParaAlign.Center) };
            if (side) row.Add(new CellSpec(s.Lado == LadoLote.Automatico ? "–" : LadoInfo.Label(s.Lado), false, ParaAlign.Center));
            row.Add(new CellSpec(_fmt.Angle(s.Azimuth(_ms.AzimuthType)), false, ParaAlign.Center));
            row.Add(new CellSpec(dist, false, ParaAlign.Center));
            row.Add(new CellSpec(string.IsNullOrWhiteSpace(s.Confrontante) ? "–" : s.Confrontante, false, ParaAlign.Left));
            rows.Add(row);
        }
        return Wml.Table(rows, widths, headerRepeat: true, fontPt: 8.5);
    }

    private List<XElement> SummaryTable(List<LotRecord> lots, LotRecord? reference, string? referenceLabel, List<LotRecord>? originals = null)
    {
        var widths = new List<double> { 5.6, 3.0, 3.0, 2.4, 2.0 };
        var rows = new List<IReadOnlyList<CellSpec>>
        {
            new List<CellSpec>
            {
                new("Área", true, ParaAlign.Center, "D9E2F3"), new("Matrícula", true, ParaAlign.Center, "D9E2F3"),
                new("Área (m²)", true, ParaAlign.Center, "D9E2F3"), new("Perímetro (m)", true, ParaAlign.Center, "D9E2F3"),
                new("%", true, ParaAlign.Center, "D9E2F3"),
            },
        };
        double total = lots.Sum(l => _ctx.Report(l).Area(_ms.AreaType));
        double baseArea = reference is not null ? _ctx.Report(reference).Area(_ms.AreaType) : total;
        foreach (var l in lots)
        {
            var r = _ctx.Report(l);
            double a = r.Area(_ms.AreaType);
            rows.Add(new List<CellSpec>
            {
                new(l.DisplayName), new(_ctx.Project.EffectiveMatricula(l) is { Length: > 0 } lm ? lm : "–", false, ParaAlign.Center),
                new(_fmt.Number(a, _fmt.F.AreaDecimals), false, ParaAlign.Right),
                new(_fmt.DistanceValue(r.Perimeter(_ms.DistanceType)), false, ParaAlign.Right),
                new(baseArea > 0 ? _fmt.Number(a / baseArea * 100, 2) : "–", false, ParaAlign.Right),
            });
        }
        rows.Add(new List<CellSpec>
        {
            new("TOTAL", true), new(""), new(_fmt.Number(total, _fmt.F.AreaDecimals), true, ParaAlign.Right), new(""),
            new(baseArea > 0 ? _fmt.Number(total / baseArea * 100, 2) : "–", true, ParaAlign.Right),
        });
        var res = new List<XElement> { Wml.Table(rows, widths, fontPt: 9) };
        if (reference is not null)
        {
            double diff = total - baseArea;
            res.Add(Wml.Paragraph(new ParaSpec { SpaceBeforePt = 4 }, new RunSpec(
                $"{referenceLabel ?? "Área de referência"}: {_fmt.Area(baseArea)}. Diferença: {_fmt.Number(diff, _fmt.F.AreaDecimals)} m²" +
                (Math.Abs(diff) <= 0.01 ? " (confere)." : "."), SizePt: 10)));
        }
        if (originals is not null)
        {
            double sum = originals.Sum(o => _ctx.Report(o).Area(_ms.AreaType));
            double diff = total - sum;
            res.Add(Wml.Paragraph(new ParaSpec { SpaceBeforePt = 4 }, new RunSpec(
                $"Soma das áreas originais: {_fmt.Area(sum)}. Diferença: {_fmt.Number(diff, _fmt.F.AreaDecimals)} m²" +
                (Math.Abs(diff) <= 0.01 ? " (confere)." : "."), SizePt: 10)));
        }
        return res;
    }

    private List<XElement> QuadroTable()
    {
        var q = QuadroAreas.Build(_ctx);
        var widths = new List<double> { 7.0, 2.5, 4.0, 2.5 };
        var rows = new List<IReadOnlyList<CellSpec>>
        {
            new List<CellSpec>
            {
                new("Especificação", true, ParaAlign.Center, "D9E2F3"), new("Quantidade", true, ParaAlign.Center, "D9E2F3"),
                new("Área (m²)", true, ParaAlign.Center, "D9E2F3"), new("%", true, ParaAlign.Center, "D9E2F3"),
            },
        };
        foreach (var c in q.Categorias)
            rows.Add(new List<CellSpec>
            {
                new(c.Categoria), new(c.Quantidade.ToString(), false, ParaAlign.Center),
                new(_fmt.Number(c.Area, _fmt.F.AreaDecimals), false, ParaAlign.Right), new(_fmt.Number(c.Percentual, 2), false, ParaAlign.Right),
            });
        double baseArea = q.AreaGleba ?? q.AreaTotal;
        rows.Add(new List<CellSpec>
        {
            new("TOTAL", true), new(q.Categorias.Sum(c => c.Quantidade).ToString(), true, ParaAlign.Center),
            new(_fmt.Number(q.AreaTotal, _fmt.F.AreaDecimals), true, ParaAlign.Right),
            new(baseArea > 0 ? _fmt.Number(q.AreaTotal / baseArea * 100, 2) : "–", true, ParaAlign.Right),
        });
        var res = new List<XElement> { Wml.Table(rows, widths, fontPt: 9) };
        foreach (var p in q.Problemas) res.Add(Wml.Paragraph(new ParaSpec { SpaceBeforePt = 2 }, new RunSpec("Atenção: " + p, Italic: true, SizePt: 9)));
        return res;
    }

    /// <summary>Texto curto do sistema das coordenadas (ex.: "datum SIRGAS 2000, projeção UTM, fuso 23, hemisfério Sul, meridiano central 45° WGr"). Vazio sem georreferência.</summary>
    public string ReferenceSystemText()
    {
        var c = _ctx.Coords;
        if (!c.HasGeoreference) return "";
        var d = TableCrs().FirstOrDefault();
        if (d is null) return "";
        var datum = Datum.Get(d.Datum);
        return d.Kind switch
        {
            CrsKind.Utm => $"datum {datum.Name}, projeção UTM, {_fmt.ZoneText(c.ResolveZone(d), c.Origin.LatDeg)}, meridiano central {ValueFormatter.CentralMeridianText(c.ResolveZone(d).CentralMeridianDeg)}",
            CrsKind.Geographic => $"em coordenadas geográficas, datum {datum.Name}",
            CrsKind.CustomTm => $"datum {datum.Name}, projeção {d.CustomName} (meridiano central {ValueFormatter.CentralMeridianText(d.CustomCentralMeridian)})",
            CrsKind.LocalTopographic => $"datum {datum.Name}, em Plano Topográfico Local (NBR 14166)",
            _ => "",
        };
    }

    /// <summary>Notas técnicas: sistema de referência, convenções e origem das coordenadas.</summary>
    public List<string> SystemDeclaration(LotRecord main)
    {
        var res = new List<string>();
        var c = _ctx.Coords;
        var defs = TableCrs();
        if (!c.HasGeoreference)
        {
            res.Add("As coordenadas deste memorial estão no sistema local do projeto (coordenadas compartilhadas do modelo), sem vinculação ao Sistema Geodésico Brasileiro.");
        }
        else
        {
            foreach (var d in defs)
            {
                var datum = Datum.Get(d.Datum);
                switch (d.Kind)
                {
                    case CrsKind.Utm:
                    {
                        var z = c.ResolveZone(d);
                        res.Add($"As coordenadas planas UTM estão referenciadas ao datum {datum.Name}, projeção Universal Transversa de Mercator, {_fmt.ZoneText(z, c.Origin.LatDeg)}, meridiano central {ValueFormatter.CentralMeridianText(z.CentralMeridianDeg)}.");
                        break;
                    }
                    case CrsKind.Geographic:
                        res.Add($"As coordenadas geográficas (latitude e longitude) estão referenciadas ao datum {datum.Name}.");
                        break;
                    case CrsKind.CustomTm:
                        res.Add($"As coordenadas {d.CustomName} estão referenciadas ao datum {datum.Name}, projeção Transversa de Mercator com meridiano central {ValueFormatter.CentralMeridianText(d.CustomCentralMeridian)}, fator de escala {d.CustomK0.ToString("0.000000", AngleText.PtBr)}, falso este {_fmt.Plane(d.CustomFalseEasting)} m e falso norte {_fmt.Plane(d.CustomFalseNorthing)} m.");
                        break;
                    case CrsKind.LocalTopographic:
                    {
                        var o = d.PtlUseAnchor ? c.Origin : new GeodeticCoord(d.PtlLat, d.PtlLon, d.PtlHeight);
                        res.Add($"As coordenadas X e Y estão no Plano Topográfico Local (conceito da NBR 14166), com origem em latitude {_fmt.Lat(o.LatDeg)} e longitude {_fmt.Lon(o.LonDeg)}, altitude do plano de {_fmt.Number(o.Height, 2)} m, e coordenadas da origem X = {_fmt.Plane(d.PtlFalseEasting)} m e Y = {_fmt.Plane(d.PtlFalseNorthing)} m.");
                        break;
                    }
                    case CrsKind.ProjectLocal:
                        res.Add("As coordenadas locais correspondem às coordenadas compartilhadas do modelo do projeto.");
                        break;
                }
                if (datum.Id is DatumId.Sad69 or DatumId.CorregoAlegre)
                    res.Add($"Coordenadas no datum {datum.Name} obtidas por transformação a partir do SIRGAS 2000 ({datum.Note}).");
            }
        }

        string az = _ms.AzimuthType switch
        {
            AzimuthType.GridUtm => "Os azimutes são planos (de quadrícula), calculados a partir das coordenadas UTM.",
            AzimuthType.Geodesic => "Os azimutes são geodésicos, calculados sobre o elipsoide (problema geodésico inverso).",
            _ => "Os azimutes são verdadeiros, referidos ao norte verdadeiro.",
        };
        if (_fmt.F.AngleStyle == AngleStyle.Rumo) az = az.Replace("Os azimutes são", "Os rumos são");
        res.Add(az);
        res.Add(_ms.DistanceType switch
        {
            DistanceType.GridUtm => "As distâncias são planas, no plano de projeção UTM (incluem o fator de escala e a redução ao elipsoide).",
            DistanceType.Geodesic => "As distâncias são geodésicas, medidas sobre o elipsoide.",
            _ => "As distâncias são horizontais, no plano topográfico local.",
        });
        res.Add(_ms.AreaType switch
        {
            AreaType.Utm => "A área foi calculada no plano de projeção UTM.",
            AreaType.Both => "As áreas são apresentadas no plano topográfico local e no plano de projeção UTM.",
            _ => "A área foi calculada no plano topográfico local.",
        });

        if (c.HasGeoreference)
        {
            var refRec = c.Reference;
            string precision = PrecisionInfo.Label(refRec.Precision);
            double pm = PrecisionInfo.TypicalMeters(refRec.Precision);
            string pmText = pm >= 1 ? $"± {pm:0} m" : $"± {pm * 100:0} cm";
            string anchor = string.IsNullOrWhiteSpace(refRec.AnchorLabel) ? "de um vértice do levantamento" : $"do ponto {refRec.AnchorLabel}";
            string orient = refRec.Mode switch
            {
                OrientationMode.EdgeAzimuth => "a orientação foi definida pelo azimute informado de uma divisa",
                OrientationMode.TwoPoints => "a orientação foi definida por dois pontos de coordenadas conhecidas",
                OrientationMode.ViewUp => "a orientação considera o norte para cima no desenho",
                _ => "a orientação segue o norte verdadeiro definido no projeto",
            };
            res.Add($"O georreferenciamento parte {anchor}, com coordenadas obtidas de {precision} (precisão estimada {pmText}); {orient}. As demais coordenadas foram calculadas a partir das distâncias e azimutes do desenho.");
            if (refRec.Precision is PrecisionClass.GoogleMapsEarth or PrecisionClass.GpsNavegacao or PrecisionClass.Desconhecida)
                _warnings.Add("A coordenada de referência tem precisão de metros (Google/GPS de navegação). Para registro em cartório ou SIGEF é necessário levantamento geodésico.");
        }
        bool cw = main.Clockwise ?? _ctx.Settings.Naming.Clockwise;
        res.Add(cw ? "A descrição perimétrica segue o sentido horário." : "A descrição perimétrica segue o sentido anti-horário.");
        return res;
    }
}
