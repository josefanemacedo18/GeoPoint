using System.Xml.Linq;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Model;
using GeoPoint.Core.Office;

namespace GeoPoint.Core.Memorial;

/// <summary>Divisa comum com um confrontante (um ou mais segmentos).</summary>
public sealed class ConfrontanteGroup
{
    public string Nome { get; init; } = "";
    public ConfrontanteTipo Tipo { get; init; }
    public LotRecord? Lote { get; init; }
    public ViaRecord? Via { get; init; }
    public List<SegmentReport> Segments { get; } = new();
    public double Length => Segments.Sum(s => s.LengthHorizontal);
}

/// <summary>Declaração de anuência (concordância de limites) de cada confrontante.</summary>
public sealed class AnuenciaGenerator
{
    private readonly ProjectContext _ctx;
    private readonly ValueFormatter _fmt;

    public AnuenciaGenerator(ProjectContext ctx)
    {
        _ctx = ctx;
        _fmt = new ValueFormatter(ctx.Settings.Format);
    }

    public static List<ConfrontanteGroup> Groups(ProjectContext ctx, LotRecord lot, bool includeVias)
    {
        var rep = ctx.Report(lot);
        var res = new List<ConfrontanteGroup>();
        foreach (var s in rep.Segments)
        {
            if (string.IsNullOrWhiteSpace(s.Confrontante)) continue;
            if (s.ConfrontanteTipo == ConfrontanteTipo.Via && !includeVias) continue;
            var key = s.ConfrontanteTipo switch
            {
                ConfrontanteTipo.Lote when !string.IsNullOrWhiteSpace(s.ConfrontanteLotId) => "L:" + s.ConfrontanteLotId,
                ConfrontanteTipo.Via when !string.IsNullOrWhiteSpace(s.ConfrontanteViaId) => "V:" + s.ConfrontanteViaId,
                _ => "T:" + s.Confrontante.Trim().ToUpperInvariant(),
            };
            var g = res.FirstOrDefault(x => GroupKey(x) == key);
            if (g is null)
            {
                g = new ConfrontanteGroup
                {
                    Nome = s.Confrontante.Trim(),
                    Tipo = s.ConfrontanteTipo,
                    Lote = ctx.Project.FindLot(s.ConfrontanteLotId),
                    Via = ctx.Project.FindVia(s.ConfrontanteViaId),
                };
                res.Add(g);
            }
            g.Segments.Add(s);
        }
        return res;

        static string GroupKey(ConfrontanteGroup g) => g.Tipo switch
        {
            ConfrontanteTipo.Lote when g.Lote is not null => "L:" + g.Lote.Id,
            ConfrontanteTipo.Via when g.Via is not null => "V:" + g.Via.Id,
            _ => "T:" + g.Nome.ToUpperInvariant(),
        };
    }

    public byte[] Generate(LotRecord lot, IReadOnlyList<ConfrontanteGroup> groups, DateTime? date)
    {
        if (groups.Count == 0) throw new InvalidOperationException("O lote não tem confrontantes informados.");
        var info = _ctx.Project.Info;
        var tech = _ctx.Settings.Technician;
        var body = new List<XElement>();
        var title = new ParaSpec { Align = ParaAlign.Center, SpaceAfterPt = 4 };
        var text = new ParaSpec { Align = ParaAlign.Justify, SpaceAfterPt = 8, LineSpacing = 1.5 };
        var center = new ParaSpec { Align = ParaAlign.Center, SpaceAfterPt = 0 };
        string owner = First(_ctx.Project.EffectiveOwner(lot), "________________________________");
        string ownerDoc = First(_ctx.Project.EffectiveDocumento(lot), "____________________");
        string matricula = First(_ctx.Project.EffectiveMatricula(lot), "________");
        string endereco = First(lot.Endereco, info.Endereco, "________________________________");
        string cidade = string.IsNullOrWhiteSpace(info.Municipio) ? "________________" : info.Municipio + (string.IsNullOrWhiteSpace(info.UF) ? "" : "/" + info.UF);
        string dataTxt = date is DateTime d ? ValueFormatter.DateLong(d) : "____ de ______________ de ______";
        string localData = (string.IsNullOrWhiteSpace(info.Municipio) ? "________________" : info.Municipio + (string.IsNullOrWhiteSpace(info.UF) ? "" : " – " + info.UF)) + ", " + dataTxt;
        var rep = _ctx.Report(lot);

        for (int gi = 0; gi < groups.Count; gi++)
        {
            var g = groups[gi];
            if (gi > 0) body.Add(Wml.PageBreak());
            body.Add(Wml.Paragraph(title, new RunSpec("DECLARAÇÃO DE ANUÊNCIA DE CONFRONTANTE", true, SizePt: 14)));
            body.Add(Wml.Paragraph(new ParaSpec { Align = ParaAlign.Center, SpaceAfterPt = 14 }, new RunSpec("(concordância de limites e confrontações)", Italic: true, SizePt: 10)));

            string confName = g.Lote is { } nl && !string.IsNullOrWhiteSpace(nl.Proprietario) ? nl.Proprietario : (g.Tipo == ConfrontanteTipo.Texto ? g.Nome : "________________________________");
            string confDoc = g.Lote is { } dl && !string.IsNullOrWhiteSpace(dl.Documento) ? dl.Documento : "____________________";
            string confImovel = g.Tipo switch
            {
                ConfrontanteTipo.Lote when g.Lote is not null => g.Lote.DisplayName + (_ctx.Project.EffectiveMatricula(g.Lote) is { Length: > 0 } gm ? $", matrícula nº {gm}" : ""),
                ConfrontanteTipo.Via => $"{g.Nome} (logradouro público)",
                _ => "imóvel confrontante, matrícula nº ________",
            };
            string tecnico = string.IsNullOrWhiteSpace(tech.Nome) ? "responsável técnico" : $"{tech.Nome}{(string.IsNullOrWhiteSpace(tech.Profissao) ? "" : ", " + tech.Profissao)}{(string.IsNullOrWhiteSpace(tech.Registro) ? "" : $", {tech.Conselho} {tech.Registro}")}";

            body.Add(Wml.Paragraph(text,
                new RunSpec("Eu, "), new RunSpec(confName, true),
                new RunSpec($", inscrito(a) no CPF/CNPJ sob o nº {confDoc}, na qualidade de proprietário(a) ou possuidor(a) do {confImovel}, confrontante do imóvel "),
                new RunSpec(lot.DisplayName, true),
                new RunSpec($", situado em {endereco}, {cidade}, matrícula nº {matricula}, de propriedade de "),
                new RunSpec(owner, true),
                new RunSpec($" (CPF/CNPJ {ownerDoc}), DECLARO, para os devidos fins de direito, que concordo com os limites e as confrontações apurados no levantamento e no memorial descritivo elaborados por {tecnico}, especialmente quanto à divisa comum entre os dois imóveis, assim descrita:")));

            var rows = new List<IReadOnlyList<CellSpec>>
            {
                new List<CellSpec>
                {
                    new("Segmento", true, ParaAlign.Center, "D9E2F3"), new("Lado", true, ParaAlign.Center, "D9E2F3"),
                    new(_fmt.F.AngleStyle == Settings.AngleStyle.Rumo ? "Rumo" : "Azimute", true, ParaAlign.Center, "D9E2F3"),
                    new("Distância (m)", true, ParaAlign.Center, "D9E2F3"), new("Coordenadas inicial / final", true, ParaAlign.Center, "D9E2F3"),
                },
            };
            var def = _ctx.Coords.HasGeoreference ? (_ctx.Settings.Memorial.CoordinateSystems.FirstOrDefault() ?? CrsDef.Utm()) : CrsDef.ProjectLocalDef();
            foreach (var s in g.Segments)
            {
                string Coord(VertexReport v)
                {
                    var c = _ctx.Coords.ToCrs(v.Internal, def);
                    if (c is null) return "–";
                    var (a, b) = _fmt.Columns(c.Value, def);
                    return $"{v.Name}: {a}; {b}";
                }
                rows.Add(new List<CellSpec>
                {
                    new($"{s.From.Name} – {s.To.Name}", false, ParaAlign.Center),
                    new(s.Lado == LadoLote.Automatico ? "–" : LadoInfo.Label(s.Lado), false, ParaAlign.Center),
                    new(_fmt.Angle(s.AzimuthTrue), false, ParaAlign.Center),
                    new(s.IsArc ? $"D={_fmt.DistanceValue(s.LengthHorizontal)} R={_fmt.DistanceValue(s.Radius ?? 0)}" : _fmt.DistanceValue(s.LengthHorizontal), false, ParaAlign.Center),
                    new(Coord(s.From) + "\n" + Coord(s.To), false, ParaAlign.Left),
                });
            }
            body.Add(Wml.Table(rows, new List<double> { 2.4, 2.4, 2.6, 2.4, 6.2 }, fontPt: 8.5));
            body.Add(Wml.Paragraph(new ParaSpec { SpaceBeforePt = 6, SpaceAfterPt = 8 },
                new RunSpec($"Extensão total da divisa comum: {_fmt.Distance(g.Length)}. Sistema de coordenadas: {def.ShortName(_ctx.Coords.AutoZone)}.", SizePt: 10)));
            body.Add(Wml.Paragraph(text, new RunSpec("Declaro, ainda, que não existe litígio, contestação ou ação judicial quanto aos limites acima descritos, autorizando a utilização desta declaração perante o Cartório de Registro de Imóveis, a Prefeitura Municipal e demais órgãos competentes.")));
            body.Add(Wml.Paragraph(new ParaSpec { Align = ParaAlign.Right, SpaceBeforePt = 12, SpaceAfterPt = 36 }, new RunSpec(localData)));

            void Sign(string line1, string line2)
            {
                body.Add(Wml.Paragraph(center, new RunSpec("_______________________________________________")));
                body.Add(Wml.Paragraph(center, new RunSpec(line1, true)));
                body.Add(Wml.Paragraph(new ParaSpec { Align = ParaAlign.Center, SpaceAfterPt = 28 }, new RunSpec(line2, SizePt: 10)));
            }
            Sign(confName, $"Confrontante – CPF/CNPJ: {confDoc}");
            body.Add(Wml.Paragraph(new ParaSpec { SpaceAfterPt = 18 }, new RunSpec("Cônjuge (se houver): _____________________________________  CPF: __________________", SizePt: 10)));
            Sign(owner, $"Proprietário(a) do imóvel – CPF/CNPJ: {ownerDoc}");
            body.Add(Wml.Paragraph(new ParaSpec { SpaceBeforePt = 6 }, new RunSpec("Testemunhas:", true, SizePt: 10)));
            body.Add(Wml.Paragraph(new ParaSpec { SpaceAfterPt = 14 }, new RunSpec("1) ______________________________  CPF: ________________      2) ______________________________  CPF: ________________", SizePt: 10)));
            body.Add(Wml.Paragraph(new ParaSpec { SpaceBeforePt = 4 }, new RunSpec("Reconhecer firma das assinaturas, quando exigido pelo cartório. Anexo: planta do imóvel.", Italic: true, SizePt: 9)));
        }
        return DocxPackage.Create(body, "Calibri", 11, "Declaração de anuência de confrontantes");
    }

    private static string First(params string?[] s) => s.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
}
