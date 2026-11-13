using System.Globalization;
using System.Text;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;
using GeoPoint.Core.Sheets;
using GeoPoint.Core.Settings;
using GeoPoint.Core.Urban;

namespace GeoPoint.Core.Tests;

/// <summary>
/// Projeto de demonstração completo (dados inventados) de um desmembramento: lote 01 com rua na frente,
/// três partes (02, 03 e 04 remanescente), pranchas, rua, memoriais e relatório. Só roda com GEOPOINT_DEMO_OUT.
/// </summary>
public class ProjetoDemoTests
{
    [Fact]
    public void ProjetoDesmembramentoCompleto()
    {
        var outDir = Environment.GetEnvironmentVariable("GEOPOINT_DEMO_OUT");
        if (string.IsNullOrEmpty(outDir)) return;
        Directory.CreateDirectory(outDir);
        var date = new DateTime(2026, 11, 13);

        // ---------- 1) Lote original: 24 × 40 m (960 m²), frente para a Rua Projetada A (sul).
        var parent = TestProjects.Rect(0, 0, 24, 40, "01");
        parent.Proprietario = "Lucas Araujo Marinho Brasil";
        parent.Documento = "027.108.961-09";
        parent.Matricula = "7607";
        parent.Endereco = "Rua Projetada A, s/n";
        parent.AreaTitulo = 960;
        parent.PerimetroTitulo = 128;

        var p = TestProjects.Project(parent);
        p.Info.Proprietario = "Lucas Araujo Marinho Brasil";
        p.Info.Documento = "027.108.961-09";
        p.Info.Matricula = "7607";
        p.Info.Municipio = "Porto Franco";
        p.Info.UF = "MA";
        p.Info.Endereco = "Rua Projetada A, s/n";
        p.Info.Bairro = "Jardim São Manoel";
        p.Info.Cep = "65970-000";
        p.Info.RrtArt = "";            // deixado em branco de propósito: deve aparecer na conferência
        p.Info.Situacao = "Desmembramento";
        var fase = p.Fase;
        parent.FaseId = fase.Id;

        var via = new ViaRecord { Nome = "Rua Projetada A", Largura = 7, Calcada = 2 };
        p.Vias.Add(via);

        // Confrontantes do lote original (ponto médio de cada divisa).
        parent.EdgeInfos.Add(new EdgeInfo { MX = 12, MY = 0, Tipo = ConfrontanteTipo.Via, ViaId = via.Id, Testada = true });
        parent.EdgeInfos.Add(new EdgeInfo { MX = 12, MY = 40, Confrontante = "Lote 15 – Quadra A" });
        parent.EdgeInfos.Add(new EdgeInfo { MX = 0, MY = 20, Confrontante = "José Rodrigues Aguiar" });
        parent.EdgeInfos.Add(new EdgeInfo { MX = 24, MY = 20, Confrontante = "Maria Souza" });

        // ---------- 2) Desmembramento: duas linhas verticais (x = 8 e x = 16), que passam além do lote.
        var cuts = new List<(Curve2 Curve, string? Uid)>
        {
            (new LineCurve(new Vec2(8, -2), new Vec2(8, 42)), null),
            (new LineCurve(new Vec2(16, -2), new Vec2(16, 42)), null),
        };
        var pieces = SubdivisionPlanner.OrderReading(SubdivisionPlanner.FromCuts(parent, cuts));

        // ---------- 3) Lotes resultantes.
        var names = new[] { "02", "03", "04" };
        var owners = new[] { "Carlos Mendes Pereira", "Ana Lima Costa", "Lucas Araujo Marinho Brasil" };
        var op = new OperationRecord { Type = OperationType.Desmembramento, FaseId = fase.Id, ParentIds = { parent.Id } };
        var children = new List<LotRecord>();
        for (int i = 0; i < pieces.Count; i++)
        {
            var c = new LotRecord
            {
                Nome = names[i], Numero = names[i], Quadra = "A", Uso = LotUse.Lote, FaseId = fase.Id,
                Outer = pieces[i].Outer, Holes = pieces[i].Holes, ZFeet = parent.ZFeet, SourceKind = parent.SourceKind,
                Proprietario = owners[i], IsRemanescente = i == 2, Created = DateTime.Now.AddMilliseconds(i),
            };
            c.History.Add(new HistoryEntry { Description = "Resultante do desmembramento do lote 01" });
            children.Add(c);
        }
        p.Lots.AddRange(children);
        parent.Status = LotStatus.Arquivado;
        op.ChildIds.AddRange(children.Select(c => c.Id));
        op.RemanescenteId = children[2].Id;
        p.Operations.Add(op);
        var baseCtx = TestProjects.Context(p);
        foreach (var c in children)
            c.EdgeInfos = ConfrontanteInheritance.Inherit(new List<LotRecord> { parent }, c, children, baseCtx);
        var ctx = TestProjects.Context(p);
        var fmt = new ValueFormatter(ctx.Settings.Format);

        // ---------- 4) Pranchas (modelo padrão retrato e um paisagem para o último lote).
        var tpl = SheetTemplate.Default();
        var tplL = SheetTemplate.Default(SheetOrientation.Paisagem);
        var colors = new Dictionary<string, int> { [children[0].Id] = 0, [children[1].Id] = 1, [children[2].Id] = 2 };
        var sheets = new List<(string File, SheetComposition Comp)>();
        var report = new StringBuilder();

        void Add(string file, SheetSpec spec, IReadOnlyList<LotRecord> lots, SheetTemplate t)
        {
            var comp = SheetPreview.Compose(ctx, lots, spec, t, 2.0, 0, null);
            sheets.Add((file, comp));
            report.AppendLine($"== {file}  ({spec.Title} – {spec.Subtitle})  escala 1:{comp.Fit.Scale}");
            foreach (var w in comp.Warnings) report.AppendLine($"   aviso do desenho: {w}");
            foreach (var pend in comp.Input.Content.Pendencias) report.AppendLine($"   conferência: {pend}");
            report.AppendLine();
        }

        var atual = new SheetSpec
        {
            Kind = SheetKind.Atual, Title = "SITUAÇÃO ATUAL", Subtitle = "DESMEMBRAMENTO DO LOTE 01",
            LotIds = { parent.Id }, CoordTable = true, Colors = new() { [parent.Id] = 0 },
        };
        Add("01-situacao-atual-lote-01.svg", atual, new[] { parent }, tpl);

        var geral = new SheetSpec
        {
            Kind = SheetKind.Proposta, Title = "SITUAÇÃO PROPOSTA", Subtitle = "DESMEMBRAMENTO DO LOTE 01",
            LotIds = { children[0].Id, children[1].Id, children[2].Id },
            QuadroIds = { children[0].Id, children[1].Id, children[2].Id },
            Hatch = true, Colors = colors, OperationId = op.Id,
        };
        Add("02-situacao-proposta-geral.svg", geral, children, tpl);

        for (int i = 0; i < 3; i++)
        {
            var ind = new SheetSpec
            {
                Kind = SheetKind.IndividualProposta, Title = $"SITUAÇÃO PROPOSTA – LOTE {names[i]}", Subtitle = "DESMEMBRAMENTO DO LOTE 01",
                LotIds = { children[i].Id }, CoordTable = true, Hatch = true, Colors = colors, OperationId = op.Id,
            };
            Add($"0{3 + i}-situacao-proposta-lote-{names[i]}{(i == 2 ? "-paisagem" : "")}.svg", ind, new[] { children[i] }, i == 2 ? tplL : tpl);
        }

        foreach (var (file, comp) in sheets)
            File.WriteAllText(Path.Combine(outDir, file), SheetV15PreviewTests.PrimSvg(comp));

        // ---------- 5) Rua: vias e testadas (desenho esquemático com meio-fio, calçada e nome).
        var fronts = new List<StreetFrontage>();
        foreach (var l in children)
            foreach (var s in ctx.Report(l).Segments.Where(s => s.ConfrontanteViaId.Length > 0))
                fronts.Add(new StreetFrontage(s.ConfrontanteViaId, s.Curve, s.OutwardNormal));
        var street = StreetSchematic.Build(p.Vias, new List<CruzamentoRecord>(), fronts, children.Select(l => l.ToRegion()).ToList());
        File.WriteAllText(Path.Combine(outDir, "06-rua-rua-projetada-a.svg"), StreetSvg(children, street));
        report.AppendLine($"== rua: {fronts.Count} testada(s) associada(s) à via \"{via.Nome}\"; meio-fio: {street.Curbs.Count}; alinhamentos: {street.Alignments.Count}");
        foreach (var w in street.Warnings) report.AppendLine($"   aviso da rua: {w}");
        report.AppendLine();

        // ---------- 6) Memoriais (geral e um por lote) – arquivos .docx.
        var gen = new MemorialGenerator(ctx, new MemorialPhrases());
        File.WriteAllBytes(Path.Combine(outDir, "07-memorial-desmembramento-geral.docx"),
            gen.Generate(new MemorialRequest { Kind = MemorialKind.Desmembramento, Lots = children, Operation = op, Date = date }));
        var localSt = ctx.Settings.Clone();
        localSt.Naming.Numbering = VertexNumbering.Lote;
        var genLocal = new MemorialGenerator(new ProjectContext(p, localSt), new MemorialPhrases());
        for (int i = 0; i < 3; i++)
            File.WriteAllBytes(Path.Combine(outDir, $"08-memorial-lote-{names[i]}.docx"),
                genLocal.Generate(new MemorialRequest { Kind = MemorialKind.Lotes, Lots = new List<LotRecord> { children[i] }, Operation = op, Date = date }));

        // ---------- 7) Relatório com números, confrontantes e pendências.
        report.AppendLine("== lotes");
        report.AppendLine($"   lote original 01: área {fmt.Area(ctx.Report(parent).AreaLocal)}, perímetro {fmt.Distance(ctx.Report(parent).PerimeterLocal)}");
        foreach (var c in children)
        {
            var r = ctx.Report(c);
            report.AppendLine($"   {c.DisplayName}: área {fmt.Area(r.AreaLocal)}, perímetro {fmt.Distance(r.PerimeterLocal)}, testada {fmt.Distance(r.Testada)}, proprietário {c.Proprietario}");
            foreach (var s in r.Segments)
                report.AppendLine($"       {s.From.Name}–{s.To.Name}  {fmt.Distance(s.LengthHorizontal)}  {fmt.Angle(s.AzimuthTrue)}  confrontante: {s.Confrontante}{(s.ConfrontanteViaId.Length > 0 ? " (via)" : "")}");
        }
        File.WriteAllText(Path.Combine(outDir, "00-relatorio.txt"), report.ToString(), Encoding.UTF8);
    }

    /// <summary>Desenho da rua: lotes, meio-fio, alinhamento e nome da via (SVG, norte para cima).</summary>
    internal static string StreetSvg(IReadOnlyList<LotRecord> lots, StreetDrawing d)
    {
        var all = lots.SelectMany(l => l.ToRegion().Outer.ToPolygon(0.05)).Concat(d.Curbs.SelectMany(c => c)).ToList();
        var bb = BBox.Of(all).Expand(6);
        const double s = 12;
        string X(double x) => ((x - bb.MinX) * s).ToString("0.##", CultureInfo.InvariantCulture);
        string Y(double y) => ((bb.MaxY - y) * s).ToString("0.##", CultureInfo.InvariantCulture);
        var sb = new StringBuilder($"<svg xmlns='http://www.w3.org/2000/svg' width='{(bb.Width * s):0}' height='{(bb.Height * s):0}'><rect width='100%' height='100%' fill='white'/>");
        string[] fills = { "#f8d7b0", "#d7e3fb", "#d6f0de" };
        int i = 0;
        foreach (var l in lots)
        {
            var pts = string.Join(" ", l.ToRegion().Outer.ToPolygon(0.05).Select(p => $"{X(p.X)},{Y(p.Y)}"));
            sb.Append($"<polygon points='{pts}' fill='{fills[i++ % 3]}' stroke='black' stroke-width='1'/>");
        }
        foreach (var c in d.Curbs)
            sb.Append($"<polyline points='{string.Join(" ", c.Select(p => $"{X(p.X)},{Y(p.Y)}"))}' fill='none' stroke='#444' stroke-width='2'/>");
        foreach (var a in d.Alignments)
            sb.Append($"<polyline points='{string.Join(" ", a.Select(p => $"{X(p.X)},{Y(p.Y)}"))}' fill='none' stroke='#999' stroke-width='1' stroke-dasharray='4,3'/>");
        foreach (var lb in d.Labels)
            sb.Append($"<text x='{X(lb.Position.X)}' y='{Y(lb.Position.Y)}' font-family='Arial' font-size='16' text-anchor='middle' transform='rotate({(-lb.AngleRad * 180 / Math.PI).ToString("0.#", CultureInfo.InvariantCulture)} {X(lb.Position.X)} {Y(lb.Position.Y)})'>{System.Security.SecurityElement.Escape(lb.Text.ToUpperInvariant())}</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }
}
