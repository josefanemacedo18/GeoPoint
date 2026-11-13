using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;
using GeoPoint.Core.Sheets;
using GeoPoint.Core.Urban;

namespace GeoPoint.Core.Tests;

/// <summary>
/// Projeto de demonstração de remembramento (dados inventados): lotes 10, 11 e 12 (12 × 30 m cada) unidos em um lote de 36 × 30 m.
/// Só roda com GEOPOINT_REM_OUT definido. Não altera o plugin.
/// </summary>
public class ProjetoRemembramentoTests
{
    [Fact]
    public void ProjetoRemembramentoCompleto()
    {
        var outDir = Environment.GetEnvironmentVariable("GEOPOINT_REM_OUT");
        if (string.IsNullOrEmpty(outDir)) return;
        Directory.CreateDirectory(outDir);
        var date = new DateTime(2026, 11, 13);

        // ---------- 1) Três lotes contíguos, frente para a Rua Projetada B (sul).
        var l10 = TestProjects.Rect(0, 0, 12, 30, "10");
        var l11 = TestProjects.Rect(12, 0, 12, 30, "11");
        var l12 = TestProjects.Rect(24, 0, 12, 30, "12");
        var p = TestProjects.Project(l10, l11, l12);
        var fase = p.Fase;
        var via = new ViaRecord { Nome = "Rua Projetada B", Largura = 7, Calcada = 2 };
        p.Vias.Add(via);
        p.Info.Municipio = "Porto Franco"; p.Info.UF = "MA"; p.Info.Endereco = "Rua Projetada B, s/n";
        p.Info.Bairro = "Jardim São Manoel"; p.Info.Cep = "65970-000"; p.Info.Situacao = "Remembramento";
        p.Info.RrtArt = "";

        void Setup(LotRecord l, string owner, string mat, double aTitulo, params EdgeInfo[] edges)
        {
            l.FaseId = fase.Id; l.Proprietario = owner; l.Matricula = mat; l.AreaTitulo = aTitulo; l.PerimetroTitulo = 84;
            l.Endereco = "Rua Projetada B, s/n";
            foreach (var e in edges) l.EdgeInfos.Add(e);
        }
        Setup(l10, "Francisco Lima", "1001", 360,
            new EdgeInfo { MX = 6, MY = 0, Tipo = ConfrontanteTipo.Via, ViaId = via.Id, Testada = true },
            new EdgeInfo { MX = 6, MY = 30, Confrontante = "Lote 9 – Quadra B" },
            new EdgeInfo { MX = 0, MY = 15, Confrontante = "Antônio Pereira" },
            new EdgeInfo { MX = 12, MY = 15, Confrontante = "Lote 11 – Quadra B", Tipo = ConfrontanteTipo.Lote });
        Setup(l11, "Beatriz Costa", "1002", 360,
            new EdgeInfo { MX = 18, MY = 0, Tipo = ConfrontanteTipo.Via, ViaId = via.Id, Testada = true },
            new EdgeInfo { MX = 18, MY = 30, Confrontante = "Lote 8 – Quadra B" });
        Setup(l12, "Sérgio Nunes", "1003", 360,
            new EdgeInfo { MX = 30, MY = 0, Tipo = ConfrontanteTipo.Via, ViaId = via.Id, Testada = true },
            new EdgeInfo { MX = 30, MY = 30, Confrontante = "Lote 7 – Quadra B" },
            new EdgeInfo { MX = 36, MY = 15, Confrontante = "Dona Rosa Almeida" });

        var ctx0 = TestProjects.Context(p);
        var parents = new List<LotRecord> { l10, l11, l12 };
        var u = ParcelOperations.Union(parents);
        if (!u.Ok) throw new InvalidOperationException("União falhou: " + string.Join("; ", u.Errors));

        // ---------- 2) Lote resultante (como o comando de remembramento faz).
        var result = new LotRecord
        {
            Nome = "13", Numero = "13", Quadra = "B", Uso = LotUse.Lote, FaseId = fase.Id,
            Outer = u.Outer, Holes = u.Holes, ZFeet = l10.ZFeet, SourceKind = l10.SourceKind,
            Proprietario = "Francisco Lima", Endereco = "Rua Projetada B, s/n", Created = DateTime.Now,
        };
        result.EdgeInfos = ConfrontanteInheritance.Inherit(parents, result, Array.Empty<LotRecord>(), ctx0);
        CollinearMerge.Apply(result, ctx0);
        result.History.Add(new HistoryEntry { Description = "Criado por remembramento de 10, 11 e 12", Area = u.Area });
        foreach (var l in parents) l.Status = LotStatus.Arquivado;
        var op = new OperationRecord { Type = OperationType.Remembramento, FaseId = fase.Id, ParentIds = parents.Select(l => l.Id).ToList(), ChildIds = { result.Id } };
        p.Lots.Add(result);
        p.Operations.Add(op);
        var ctx = TestProjects.Context(p);
        var fmt = new ValueFormatter(ctx.Settings.Format);

        // ---------- 3) Pranchas.
        var tpl = SheetTemplate.Default();
        var colors = new Dictionary<string, int> { [l10.Id] = 0, [l11.Id] = 1, [l12.Id] = 2 };
        var sheets = new List<(string File, SheetComposition Comp)>();
        var report = new System.Text.StringBuilder();
        void Add(string file, SheetSpec spec, IReadOnlyList<LotRecord> lots)
        {
            var comp = SheetPreview.Compose(ctx, lots, spec, tpl, 2.0, 0, null);
            sheets.Add((file, comp));
            report.AppendLine($"== {file}  ({spec.Title} – {spec.Subtitle})  escala 1:{comp.Fit.Scale}");
            foreach (var w in comp.Warnings) report.AppendLine($"   aviso do desenho: {w}");
            foreach (var pend in comp.Input.Content.Pendencias) report.AppendLine($"   conferência: {pend}");
            report.AppendLine();
        }
        Add("01-situacao-atual-geral.svg", new SheetSpec
        {
            Kind = SheetKind.Atual, Title = "SITUAÇÃO ATUAL", Subtitle = "REMEMBRAMENTO DOS LOTES 10, 11 E 12",
            LotIds = { l10.Id, l11.Id, l12.Id }, QuadroIds = { l10.Id, l11.Id, l12.Id }, Hatch = true, Colors = colors,
        }, parents);
        for (int i = 0; i < 3; i++)
            Add($"0{2 + i}-situacao-atual-lote-{parents[i].Nome}.svg", new SheetSpec
            {
                Kind = SheetKind.IndividualAtual, Title = $"SITUAÇÃO ATUAL – LOTE {parents[i].Nome}", Subtitle = "REMEMBRAMENTO DOS LOTES 10, 11 E 12",
                LotIds = { parents[i].Id }, CoordTable = true, Hatch = true, Colors = colors,
            }, new[] { parents[i] });
        Add("05-situacao-proposta-lote-13.svg", new SheetSpec
        {
            Kind = SheetKind.Proposta, Title = "SITUAÇÃO PROPOSTA – LOTE 13", Subtitle = "REMEMBRAMENTO DOS LOTES 10, 11 E 12",
            LotIds = { result.Id }, CoordTable = true, Hatch = true, Colors = new() { [result.Id] = 0 }, OperationId = op.Id,
        }, new[] { result });
        foreach (var (file, comp) in sheets)
            File.WriteAllText(Path.Combine(outDir, file), SheetV15PreviewTests.PrimSvg(comp));

        // ---------- 4) Rua.
        var fronts = new List<StreetFrontage>();
        foreach (var s in ctx.Report(result).Segments.Where(s => s.ConfrontanteViaId.Length > 0))
            fronts.Add(new StreetFrontage(s.ConfrontanteViaId, s.Curve, s.OutwardNormal));
        var street = StreetSchematic.Build(p.Vias, new List<CruzamentoRecord>(), fronts, new[] { result.ToRegion() });
        File.WriteAllText(Path.Combine(outDir, "06-rua-rua-projetada-b.svg"), ProjetoDemoTests.StreetSvg(new[] { result }, street));

        // ---------- 5) Memoriais: geral do remembramento e do lote resultante.
        var gen = new MemorialGenerator(ctx, new MemorialPhrases());
        File.WriteAllBytes(Path.Combine(outDir, "07-memorial-remembramento-geral.docx"),
            gen.Generate(new MemorialRequest { Kind = MemorialKind.Remembramento, Lots = new List<LotRecord> { result }, Operation = op, Date = date }));
        File.WriteAllBytes(Path.Combine(outDir, "08-memorial-lote-13.docx"),
            gen.Generate(new MemorialRequest { Kind = MemorialKind.Lotes, Lots = new List<LotRecord> { result }, Date = date }));

        // ---------- 6) Relatório.
        report.AppendLine("== rua");
        report.AppendLine($"   testadas associadas à via \"{via.Nome}\": {fronts.Count}; meio-fio: {street.Curbs.Count}");
        report.AppendLine();
        report.AppendLine("== lote resultante");
        var r = ctx.Report(result);
        report.AppendLine($"   {result.DisplayName}: área {fmt.Area(r.AreaLocal)}, perímetro {fmt.Distance(r.PerimeterLocal)}, testada {fmt.Distance(r.Testada)}, proprietário {result.Proprietario}");
        foreach (var s in r.Segments)
            report.AppendLine($"       {s.From.Name}–{s.To.Name}  {fmt.Distance(s.LengthHorizontal)}  {fmt.Angle(s.AzimuthTrue)}  confrontante: {s.Confrontante}{(s.ConfrontanteViaId.Length > 0 ? " (via)" : "")}");
        report.AppendLine();
        report.AppendLine($"   transcrição (conferir): {GeoPoint.Core.Sheets.SheetContent.Transcricao(p, op, p.Info)}");
        File.WriteAllText(Path.Combine(outDir, "00-relatorio.txt"), report.ToString(), System.Text.Encoding.UTF8);
    }
}
