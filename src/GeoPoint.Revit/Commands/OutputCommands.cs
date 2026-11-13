using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Settings;
using GeoPoint.Core.Topology;
using GeoPoint.Core.Urban;
using GeoPoint.Revit.Infrastructure;
using GeoPoint.Revit.UI;

namespace GeoPoint.Revit.Commands;

internal static class PickerRows
{
    public static List<LotRow> Build(GpSession s, bool activeOnly, Func<LotRecord, bool>? filter = null)
    {
        var pre = LotServices.LotsFromSelection(s);
        return ProjectContext.SortLots(s.Project.PhaseLots.Where(l => !activeOnly || l.Status == LotStatus.Ativo).Where(l => filter?.Invoke(l) ?? true))
            .Select(l =>
            {
                double a = 0;
                try { a = l.ToRegion().Area; } catch { }
                return new LotRow(l, a) { Selected = pre.Count == 0 ? l.Status == LotStatus.Ativo : pre.Contains(l.Id) };
            }).ToList();
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdAnotar : CommandBase
{
    protected override string Title => "Anotar lotes";
    protected override string? BackupReason => "anotar";

    protected override Result Run(GpSession s)
    {
        var view = s.ActiveView;
        if (!ElementUtil.IsPlanLike(view))
        {
            Ui.Warn("Vista inadequada", "Abra uma vista de planta (ou planta de situação) para anotar os lotes.");
            return Result.Cancelled;
        }
        if (!s.Project.ActiveLots.Any()) { Ui.Info("Nenhum lote ativo", "Use 'Reconhecer Lotes' primeiro."); return Result.Cancelled; }
        if (!NorthGuard.Ensure(s)) return Result.Cancelled;
        var a = s.Settings.Annotation;
        var opts = new List<OptionItem>
        {
            new("edges", "Divisas: distância", a.Edges),
            new("angle", "Divisas: azimute/rumo (abaixo da distância)", a.EdgeAzimuth),
            new("out", "Textos das divisas do lado de fora do lote", a.TextOutside),
            new("vert", "Nomes dos vértices", a.Vertices),
            new("dots", "Ponto preenchido em cada vértice", a.VertexDots),
            new("conf", "Confrontantes: nome acima da linha \"distância  AZ= azimute\" e extensão de cada confrontante", a.Confrontantes),
            new("lowner", "Rótulo do lote: proprietário", a.LabelOwner),
            new("lmat", "Rótulo do lote: matrícula", a.LabelMatricula),
            new("lper", "Rótulo do lote: perímetro", a.LabelPerimeter),
            new("refc", "Coordenada do ponto de referência de cada lote (ponto, linha de chamada, nome e coordenadas no sistema das opções)", a.RefCallout),
            new("area", "Nome e área no centro do lote", a.AreaLabel),
            new("opaque", "Fundo opaco nos textos (legíveis sobre hachuras)", a.OpaqueText),
            new("size", "Altura do texto no papel (mm)", a.TextSizeMm),
            new("offset", "Afastamento das linhas no papel (mm)", a.OffsetMm),
            new("dot", "Diâmetro do ponto no papel (mm)", a.DotMm),
            new("clear", "Somente remover as anotações do GeoPoint dos lotes marcados nesta vista", false),
        };
        var changed = Annotator.ScaleChanged(s, view);
        var rowsA = PickerRows.Build(s, true);
        string scaleNote = "";
        if (changed.Count > 0)
        {
            foreach (var r in rowsA) r.Selected = changed.Any(c => c.Id == r.Lot.Id);
            int old = changed.SelectMany(c => c.Annotations).Where(x => x.ViewId == view.UniqueId).Select(x => x.Scale).FirstOrDefault();
            scaleNote = $" A escala desta vista mudou (de 1:{old} para 1:{view.Scale}): os {changed.Count} lote(s) marcados serão anotados de novo para a nova escala.";
        }
        var w = new LotPickerWindow("Anotar lotes na vista atual", $"Vista: {view.Name} (escala 1:{view.Scale}). Anotações anteriores do GeoPoint nesta vista são substituídas. O tamanho dos textos e os afastamentos são calculados para esta escala." + scaleNote,
            rowsA, opts, "Anotar");
        // Prévia: o mesmo planejamento de textos que vai para a vista (escala e orientação da vista).
        var pctx = s.NewContext();
        var pfmt = new ValueFormatter(s.Settings.Format);
        double vAng = Annotator.ViewAngle(view);
        double vScale = Math.Max(1, view.Scale);
        var pdef = pctx.Coords.HasGeoreference ? (s.Settings.Memorial.CoordinateSystems.FirstOrDefault() ?? GeoPoint.Core.Geodesy.CrsDef.Utm()) : GeoPoint.Core.Geodesy.CrsDef.ProjectLocalDef();
        w.EnablePreview((lots, B, N) =>
        {
            var items = new List<PreviewItem>();
            var reps = new List<LotReport>();
            foreach (var l in lots) { try { reps.Add(pctx.Report(l)); } catch { } }
            if (reps.Count == 0) return (items, -vAng, "Marque os lotes.");
            var lo = Annotator.Options.From(s.Settings.Annotation);
            lo.LabelOwner = B("lowner"); lo.LabelMatricula = B("lmat"); lo.LabelPerimeter = B("lper");
            double textMm = Math.Clamp(N("size"), 0.5, 20);
            var po = new AnnotationPlanOptions
            {
                Edges = B("edges"), EdgeAngle = B("angle"), Vertices = B("vert"), Dots = B("dots"), Confrontantes = B("conf"), Area = B("area"), Outside = B("out"),
                TextM = textMm / 1000 * vScale, OffsetM = N("offset") / 1000 * vScale, DotRadiusM = N("dot") / 2000 * vScale,
                PadM = RevitStyles.TextPadMm / 1000 * vScale, LineFactor = RevitStyles.LineFactor, ViewAngle = vAng,
                LabelOf = r => Annotator.LotLabel(r, pctx, pfmt, lo),
                PerLotVertexNames = pctx.Settings.Naming.Numbering == VertexNumbering.Lote && reps.Count > 1,
                RefCallout = B("refc") ? r => SheetBuilder.Callout(r, pctx, pfmt, pdef) : null,
            };
            var plan = AnnotationPlanner.Plan(reps, pfmt, po);
            int i = 0;
            foreach (var r in reps)
            {
                var col = PreviewCanvas.Palette[i++ % PreviewCanvas.Palette.Length];
                items.Add(PreviewItem.FromRegion(r.Region, col, null, true, System.Windows.Media.Color.FromRgb(30, 30, 30), 1.4));
            }
            foreach (var ln in plan.Lines)
                items.Add(new PreviewItem
                {
                    Line = new List<Vec2> { ln.A, ln.B }, StrokeWidth = ln.Kind == AnnoLineKind.ConfrontanteTick ? 2 : 0.8,
                    Stroke = ln.Kind is AnnoLineKind.Leader or AnnoLineKind.RefLeader ? System.Windows.Media.Color.FromRgb(70, 70, 70) : System.Windows.Media.Color.FromRgb(192, 0, 0),
                });
            foreach (var d in plan.Dots) items.Add(new PreviewItem { LabelAt = d.At, DotRadiusM = d.Radius });
            foreach (var t in plan.Texts)
                items.Add(new PreviewItem
                {
                    Label = t.Text, LabelAt = t.At, LabelAngle = t.Rotation, LabelSizeM = t.Kind == AnnoTextKind.LotLabel ? po.TextM * 1.1 * t.SizeFactor : po.TextM,
                    LabelBoldFirst = t.BoldFirstLine,
                    LabelColor = t.Kind is AnnoTextKind.Confrontante or AnnoTextKind.Measure ? System.Windows.Media.Color.FromRgb(192, 0, 0) : System.Windows.Media.Colors.Black,
                });
            string info = $"Escala 1:{view.Scale}, textos de {Fmt.N(textMm, 1)} mm no papel." + (plan.Conflicts > 0 ? $"  ⚠ {plan.Conflicts} texto(s) sem espaço livre: aumente a escala da vista ou reduza o texto." : "  Sem sobreposições.");
            return (items, -vAng, info);
        });
        if (Ui.ShowDialog(w, s.MainWindow) != true) return Result.Cancelled;
        a.Edges = w.Option("edges"); a.EdgeAzimuth = w.Option("angle"); a.TextOutside = w.Option("out"); a.Vertices = w.Option("vert");
        a.VertexDots = w.Option("dots"); a.Confrontantes = w.Option("conf"); a.AreaLabel = w.Option("area"); a.OpaqueText = w.Option("opaque");
        a.LabelOwner = w.Option("lowner"); a.LabelMatricula = w.Option("lmat"); a.LabelPerimeter = w.Option("lper"); a.RefCallout = w.Option("refc");
        a.TextSizeMm = Math.Clamp(w.Number("size"), 0.5, 20);
        a.OffsetMm = Math.Clamp(w.Number("offset"), 0, 50);
        a.DotMm = Math.Clamp(w.Number("dot"), 0.2, 10);
        try { s.SaveSettings(); } catch { }

        var ctx = s.NewContext();
        var fmt = new ValueFormatter(s.Settings.Format);
        var o = Annotator.Options.From(a);
        int total;
        var failed = new List<string>();
        using (var t = new Transaction(s.Doc, "GeoPoint: anotar lotes"))
        {
            t.Start();
            // Anotações de lotes arquivados (ex.: lote original de um desmembramento) saem desta vista.
            foreach (var arch in s.Project.Lots.Where(l => l.Status == LotStatus.Arquivado || l.FaseId != s.Project.Fase.Id))
            {
                Annotator.Clear(s.Doc, arch, view);
                Hatcher.Clear(s.Doc, arch, view);
            }
            if (w.Option("clear"))
            {
                foreach (var l in w.SelectedLots) Annotator.Clear(s.Doc, l, view);
                total = 0;
            }
            else
            {
                var defR = ctx.Coords.HasGeoreference ? (s.Settings.Memorial.CoordinateSystems.FirstOrDefault() ?? GeoPoint.Core.Geodesy.CrsDef.Utm()) : GeoPoint.Core.Geodesy.CrsDef.ProjectLocalDef();
                Func<LotReport, RefCallout?>? refc = a.RefCallout ? r => SheetBuilder.Callout(r, ctx, fmt, defR) : null;
                total = Annotator.Annotate(s.Doc, view, w.SelectedLots, ctx, fmt, o, failed, refCallout: refc);
            }
            ProjectStore.Save(s.Doc, s.Project);
            t.Commit();
        }
        if (failed.Count > 0) Ui.Warn("Anotação concluída com falhas", $"{total} texto(s) criado(s).\n\n" + string.Join("\n", failed));
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdTabelaCoordenadas : CommandBase
{
    protected override string Title => "Tabela de coordenadas";

    protected override Result Run(GpSession s)
    {
        if (!s.Project.Lots.Any()) { Ui.Info("Nenhum lote cadastrado", "Use 'Reconhecer Lotes' primeiro."); return Result.Cancelled; }
        if (!NorthGuard.Ensure(s)) return Result.Cancelled;
        var opts = new List<OptionItem> { new("size", "Altura do texto (mm)", s.Settings.Annotation.TextSizeMm) };
        var w = new LotPickerWindow("Tabela de coordenadas", "Cria (ou atualiza) uma vista de desenho por lote com o quadro de coordenadas, azimutes, distâncias e confrontantes. Arraste a vista para a prancha.",
            PickerRows.Build(s, false), opts, "Criar tabelas");
        if (Ui.ShowDialog(w, s.MainWindow) != true) return Result.Cancelled;
        double size = Math.Clamp(w.Number("size"), 0.5, 20);
        var ctx = s.NewContext();
        View? last = null;
        using (var t = new Transaction(s.Doc, "GeoPoint: tabela de coordenadas"))
        {
            t.Start();
            foreach (var lot in w.SelectedLots)
                last = CoordinateTableBuilder.Build(s.Doc, lot, ctx.Report(lot), ctx, size);
            t.Commit();
        }
        if (last is not null)
        {
            try { s.UiDoc.ActiveView = last; } catch { }
        }
        Ui.Info("Tabelas criadas", $"{w.SelectedLots.Count} vista(s) de desenho \"GeoPoint - Coordenadas - …\" criada(s)/atualizada(s). Coloque-as na prancha arrastando do Navegador de projeto.");
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdVerificar : CommandBase
{
    protected override string Title => "Verificar projeto";

    protected override Result Run(GpSession s)
    {
        var ctx = s.NewContext();
        var issues = ProjectValidator.Validate(ctx);
        var rows = issues.Select(i => new ReportRow
        {
            Nivel = i.Severity switch { DiagnosticSeverity.Error => "Erro", DiagnosticSeverity.Warning => "Atenção", _ => "Info" },
            Categoria = i.Category, Lote = i.LotName, Mensagem = i.Message, LotId = i.LotId,
        }).ToList();

        // Orientação: para onde o topo da vista atual aponta pelo norte usado pelo GeoPoint.
        var av = s.ActiveView;
        if (s.Project.Reference.IsSet && av is not ViewSheet && ElementUtil.IsPlanLike(av) && Math.Abs(av.UpDirection.Z) < 0.5)
        {
            var up = av.UpDirection;
            double azTop = ctx.Coords.TrueAzimuth(Vec2.Zero, new Vec2(up.X, up.Y));
            string mode = s.Project.Reference.Mode switch
            {
                OrientationMode.ViewUp => "topo da tela",
                OrientationMode.EdgeAzimuth => "azimute de uma divisa",
                OrientationMode.TwoPoints => "dois pontos",
                _ => "norte verdadeiro do Revit",
            };
            bool ok = Math.Min(azTop, 360 - azTop) < 0.5;
            rows.Add(new ReportRow
            {
                Nivel = ok ? "Info" : "Atenção", Categoria = "Norte",
                Mensagem = ok
                    ? $"O norte usado nos azimutes, coordenadas e no Google Earth aponta para o topo da vista \"{av.Name}\" (orientação: {mode})."
                    : $"Na vista \"{av.Name}\" o topo da tela aponta para {AngleText.FormatDms(azTop, 0, AngleKind.Azimuth)} ({NorthGuard.DirectionName(azTop)}) pelo norte usado pelo GeoPoint (orientação: {mode}). " +
                      "Se você desenhou com o norte para cima, use Definir Referência › 'O topo da tela é o norte'.",
            });
        }

        // Pontos importados (KML/KMZ): distância até o vértice mais próximo.
        foreach (var r in ControlPointCheck.Compare(ctx))
        {
            if (r.Distance is not double d) continue;
            rows.Add(new ReportRow
            {
                Nivel = d > 1.0 ? "Atenção" : "Info", Categoria = "Pontos KML", Lote = r.Lot?.DisplayName ?? "", LotId = r.Lot?.Id,
                Mensagem = $"\"{r.Point.Nome}\" ({r.Point.Origem}): vértice mais próximo {r.VertexName} a {Fmt.N(d, 2)} m." +
                           (d > 1.0 ? " Diferença grande: confira o vértice ou o ponto (coordenadas do Google têm erro de alguns metros)." : ""),
            });
        }

        // Verificações que dependem do desenho do Revit.
        var sync = LotServices.ComputeSync(s, s.Project.ActiveLots);
        foreach (var it in sync)
        {
            if (it.Failed) rows.Add(new ReportRow { Nivel = "Erro", Categoria = "Desenho", Lote = it.Lot.DisplayName, Mensagem = it.Status, LotId = it.Lot.Id });
            else if (it.Changed) rows.Add(new ReportRow { Nivel = "Atenção", Categoria = "Desenho", Lote = it.Lot.DisplayName, Mensagem = "As linhas foram alteradas depois do reconhecimento: use 'Atualizar Lotes'. " + it.Status, LotId = it.Lot.Id });
        }
        rows = rows.OrderByDescending(r => r.Nivel == "Erro" ? 3 : r.Nivel == "Atenção" ? 2 : 1).ToList();
        if (rows.Count == 0) rows.Add(new ReportRow { Nivel = "OK", Mensagem = "Nenhum problema encontrado." });
        int err = rows.Count(r => r.Nivel == "Erro"), warn = rows.Count(r => r.Nivel == "Atenção");
        var w = new ReportWindow("Verificação do projeto", $"{s.Project.ActiveLots.Count()} lote(s) ativo(s) – {err} erro(s), {warn} atenção(ões).", rows);
        if (Ui.ShowDialog(w, s.MainWindow) == true && w.Action == ReportAction.Select)
            LotServices.SelectSources(s, w.SelectedRows.Where(r => r.LotId is not null).Select(r => s.Project.FindLot(r.LotId!)).OfType<LotRecord>());
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdConfiguracoes : CommandBase
{
    protected override string Title => "Configurações";

    protected override Result Run(GpSession s)
    {
        var w = new SettingsWindow(s.Settings);
        if (Ui.ShowDialog(w, s.MainWindow) != true) return Result.Cancelled;
        SettingsStore.Save(w.Result);
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdAjuda : CommandBase
{
    protected override string Title => "Ajuda";

    protected override Result Run(GpSession s)
    {
        var version = typeof(CmdAjuda).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        var td = new TaskDialog("GeoPoint")
        {
            MainInstruction = $"GeoPoint {version} – georreferenciamento de lotes no Revit",
            MainContent =
                "Fluxo recomendado:\n" +
                "1. Desenhe as divisas com linhas (modelo ou detalhe), com snap nos encontros – ou use Importar Pontos / Gerar Lote.\n" +
                "2. Reconhecer Lotes. 3. Gerenciar Lotes (nomes, confrontantes, testada). 4. Definir Referência.\n" +
                "5. Anotar Lotes, Área Edificada, Vias. 6. Memorial, Pranchas e Exportar PDF.\n\n" +
                "Atalhos de teclado: os comandos do GeoPoint aparecem em Vista › Interface do usuário › Atalhos de teclado (filtro \"Ferramentas externas\"/\"GeoPoint\"), onde você define as teclas.\n\n" +
                "Os dados ficam no próprio .rvt (Desfazer funciona). Antes de cada operação o GeoPoint guarda uma cópia dos seus dados.\n" +
                $"Configurações, modelos e registros: {SettingsStore.DefaultFolder}",
            CommonButtons = TaskDialogCommonButtons.Close,
        };
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir o manual", "MANUAL.md, na pasta do plugin.");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Restaurar uma cópia de segurança dos dados do GeoPoint", "Volta lotes, fases e operações para um momento anterior (o desenho do Revit não muda).");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Gerar arquivo de diagnóstico", "Um .zip na Área de Trabalho com registros, configurações e dados do projeto, para enviar ao suporte.");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Criar um projeto de exemplo nesta vista", "Três lotes, uma via, uma edificação e a referência, para experimentar as ferramentas.");
        var r = td.Show();
        switch (r)
        {
            case TaskDialogResult.CommandLink1:
            {
                var path = Path.Combine(Path.GetDirectoryName(typeof(CmdAjuda).Assembly.Location) ?? "", "MANUAL.md");
                if (File.Exists(path)) Ui.OpenFile(path); else Ui.Info("Manual não encontrado", path);
                break;
            }
            case TaskDialogResult.CommandLink2: return Restore(s);
            case TaskDialogResult.CommandLink3:
            {
                try
                {
                    var zip = Backups.Diagnostic(s);
                    Ui.Info("Arquivo de diagnóstico", $"Gerado em:\n{zip}\n\nEle contém os dados do projeto (nomes, matrículas, coordenadas). Envie só para quem for analisar o problema.");
                    Ui.OpenFile(Path.GetDirectoryName(zip)!);
                }
                catch (Exception ex) { Ui.Error("Diagnóstico", ex.Message, ex); }
                break;
            }
            case TaskDialogResult.CommandLink4: return Example(s);
        }
        return Result.Succeeded;
    }

    private static Result Restore(GpSession s)
    {
        var list = Backups.List(s.Doc);
        if (list.Count == 0) { Ui.Info("Cópias de segurança", "Ainda não há cópias deste projeto.\nPasta: " + Backups.Folder(s.Doc)); return Result.Cancelled; }
        var rows = list.Select(b => new SheetPickRow { Number = b.When.ToString("dd/MM/yyyy HH:mm:ss"), Name = $"antes de: {b.Reason}", PaperText = $"{b.Lots} lote(s)", IsGeoPoint = true, Tag = b }).ToList();
        var w = new SheetExportWindow(rows, Backups.Folder(s.Doc), "") { Title = "GeoPoint – Restaurar cópia de segurança" };
        w.ConfigureAsPicker("Restaurar cópia de segurança", "Marque UMA cópia. Os dados atuais do GeoPoint também são guardados antes (dá para voltar).", "Restaurar");
        if (Ui.ShowDialog(w, s.MainWindow) != true) return Result.Cancelled;
        var chosen = w.Selected.FirstOrDefault()?.Tag as Backups.Entry;
        if (chosen is null) return Result.Cancelled;
        if (w.Selected.Count > 1) { Ui.Warn("Restaurar", "Marque só uma cópia."); return Result.Cancelled; }
        var data = Backups.Load(chosen.Path);
        Backups.Save(s, "antes de restaurar");
        using (var t = new Transaction(s.Doc, "GeoPoint: restaurar cópia de segurança"))
        {
            t.Start();
            ProjectStore.Save(s.Doc, data);
            t.Commit();
        }
        Ui.Info("Cópia restaurada", $"Dados do GeoPoint de {chosen.When:dd/MM/yyyy HH:mm} restaurados ({chosen.Lots} lote(s)).\nUse 'Atualizar Lotes' se o desenho mudou depois disso. Para desfazer, use Desfazer do Revit.");
        return Result.Succeeded;
    }

    /// <summary>Projeto de exemplo: três lotes 12 × 30 m, uma via, uma edificação e a referência (Porto Franco – MA).</summary>
    private static Result Example(GpSession s)
    {
        var view = s.ActiveView;
        if (!ElementUtil.IsPlanLike(view) || view is ViewSheet) { Ui.Warn("Vista inadequada", "Abra uma planta (de preferência num projeto vazio) para criar o exemplo."); return Result.Cancelled; }
        if (s.Project.ActiveLots.Any()) { Ui.Warn("Projeto com lotes", "O exemplo é para um projeto vazio: este já tem lotes do GeoPoint. Crie um projeto novo no Revit e use de novo."); return Result.Cancelled; }
        using var tg = new TransactionGroup(s.Doc, "GeoPoint: projeto de exemplo");
        tg.Start();
        List<CurveElement> lotLines, bLines;
        using (var t = new Transaction(s.Doc, "GeoPoint: linhas do exemplo"))
        {
            t.Start();
            var target = CurveTarget.ForActiveView(s.Doc, view, false);
            var c = new List<Curve2>();
            Vec2 P(double x, double y) => new(x, y);
            c.Add(new LineCurve(P(0, 0), P(36, 0))); c.Add(new LineCurve(P(36, 0), P(36, 30)));
            c.Add(new LineCurve(P(36, 30), P(0, 30))); c.Add(new LineCurve(P(0, 30), P(0, 0)));
            c.Add(new LineCurve(P(12, 0), P(12, 30))); c.Add(new LineCurve(P(24, 0), P(24, 30)));
            lotLines = target.Create(s.Doc, c, null);
            var b = new List<Curve2> { new LineCurve(P(15, 6), P(21, 6)), new LineCurve(P(21, 6), P(21, 16)), new LineCurve(P(21, 16), P(15, 16)), new LineCurve(P(15, 16), P(15, 6)) };
            bLines = target.Create(s.Doc, b, null);
            t.Commit();
        }
        var rec = LotServices.Recognize(s, lotLines.Cast<Element>().ToList(), null);
        var via = new ViaRecord { Nome = "Rua Exemplo" };
        s.Project.Vias.Add(via);
        var names = new Dictionary<int, string>();
        foreach (var face in rec.Network.Faces.OrderBy(f => f.Region.InteriorPoint().X))
        {
            string n = (names.Count + 1).ToString("00");
            names[face.Id] = n;
            var lot = LotServices.CreateLot(rec, face, s.Settings, n, n, "A", LotUse.Lote);
            var reg = lot.ToRegion();
            var bb = reg.Bounds;
            lot.EdgeInfos.Add(new EdgeInfo { MX = bb.Center.X, MY = 0, Tipo = ConfrontanteTipo.Via, ViaId = via.Id, Testada = true });
            lot.EdgeInfos.Add(new EdgeInfo { MX = bb.Center.X, MY = 30, Confrontante = "Lote " + (10 + names.Count) + " – Quadra A" });
            if (bb.MinX < 0.01) lot.EdgeInfos.Add(new EdgeInfo { MX = 0, MY = 15, Confrontante = "José da Silva" });
            if (bb.MaxX > 35.99) lot.EdgeInfos.Add(new EdgeInfo { MX = 36, MY = 15, Confrontante = "Maria Souza" });
            lot.AreaTitulo = 360;
            s.Project.Lots.Add(lot);
        }
        // Confrontantes entre os lotes do exemplo.
        var lots = s.Project.Lots.Where(l => l.Status == LotStatus.Ativo).OrderBy(l => l.Nome).ToList();
        for (int i = 0; i + 1 < lots.Count; i++)
        {
            double x = 12 * (i + 1);
            lots[i].EdgeInfos.Add(new EdgeInfo { MX = x, MY = 15, Tipo = ConfrontanteTipo.Lote, LotId = lots[i + 1].Id });
            lots[i + 1].EdgeInfos.Add(new EdgeInfo { MX = x, MY = 15, Tipo = ConfrontanteTipo.Lote, LotId = lots[i].Id });
        }
        var brec = LotServices.Recognize(s, bLines.Cast<Element>().ToList(), null);
        var bface = brec.Network.Faces.FirstOrDefault();
        if (bface is not null && lots.Count >= 2)
        {
            var (outer, _) = LotBuilder.FromFace(brec.Network, bface, brec.SourcesOfTag, false, true);
            s.Project.Buildings.Add(new BuildingRecord { FaseId = s.Project.Fase.Id, LotId = lots[1].Id, Nome = "Edificação 1", Pavimentos = 1, Outer = outer, SourceIds = outer.SelectMany(e => e.Src).Distinct().ToList() });
        }
        var info = s.Project.Info;
        info.Proprietario = "Proprietário Exemplo"; info.Documento = "000.000.000-00"; info.Matricula = "0000";
        info.Municipio = "Porto Franco"; info.UF = "MA"; info.Endereco = "Rua Exemplo, s/n"; info.Bairro = "Centro"; info.Cep = "65970-000";
        info.Situacao = "Regularização";
        s.Project.Reference = new GeoReferenceRecord
        {
            IsSet = true, AnchorX = 0, AnchorY = 0, AnchorLabel = "P-01 (exemplo)", Lat = -6.338400, Lon = -47.398700,
            InputText = "Exemplo: -6.338400, -47.398700", InputKind = CrsKind.Geographic, InputDatum = DatumId.Wgs84,
            Precision = PrecisionClass.GoogleMapsEarth, Mode = OrientationMode.ViewUp, RotationRad = NorthGuard.ViewRotation(view), NorthConfirmed = true, SetAt = DateTime.Now,
        };
        using (var t = new Transaction(s.Doc, "GeoPoint: dados do exemplo"))
        {
            t.Start();
            ProjectStore.Save(s.Doc, s.Project);
            t.Commit();
        }
        tg.Assimilate();
        try { s.UiDoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view.Id)?.ZoomToFit(); } catch { }
        Ui.Info("Projeto de exemplo criado", "Três lotes (01, 02 e 03 – Quadra A), a Rua Exemplo na frente e uma edificação no lote 02, com a referência em Porto Franco – MA.\n\nExperimente: Anotar Lotes, Área Edificada, Vias, Memorial, Pranchas e Desmembramento (desenhando com a ferramenta Linha).");
        return Result.Succeeded;
    }
}
