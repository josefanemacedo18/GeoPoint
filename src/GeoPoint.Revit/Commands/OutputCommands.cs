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
                    Line = new List<Vec2> { ln.A, ln.B }, StrokeWidth = 0.8, Dashed = ln.Kind == AnnoLineKind.ConfrontanteTick,
                    Stroke = ln.Kind is AnnoLineKind.Leader or AnnoLineKind.RefLeader ? System.Windows.Media.Color.FromRgb(70, 70, 70) : System.Windows.Media.Color.FromRgb(192, 0, 0),
                });
            foreach (var d in plan.Dots) items.Add(new PreviewItem { LabelAt = d.At, DotRadiusM = d.Radius });
            foreach (var t in plan.Texts)
                items.Add(new PreviewItem
                {
                    Label = t.Text, LabelAt = t.At, LabelAngle = t.Rotation, LabelSizeM = t.Kind == AnnoTextKind.LotLabel ? po.TextM * 1.1 : po.TextM,
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
                "1. Desenhe as divisas com linhas (modelo ou detalhe), usando snap nos encontros.\n" +
                "2. Reconhecer Lotes: selecione as linhas e clique dentro de cada lote.\n" +
                "3. Gerenciar Lotes: nomes, quadra, uso, confrontantes e testada.\n" +
                "4. Definir Referência: cole a coordenada de um vértice (Google Maps/Earth, QGIS, GPS ou UTM).\n" +
                "5. Google Earth / QGIS: confira sobre a imagem de satélite.\n" +
                "6. Memorial Descritivo, Planilha, Anotar Lotes, Tabela de Coordenadas.\n\n" +
                "Desmembramento: desenhe as novas divisas (ou use Dividir Lote), reconheça os lotes e registre.\n" +
                "Remembramento: escolha os lotes contíguos e registre.\n\n" +
                "Os dados ficam gravados no próprio arquivo .rvt (Desfazer funciona normalmente).\n" +
                $"Configurações e registros: {SettingsStore.DefaultFolder}",
            CommonButtons = TaskDialogCommonButtons.Close,
        };
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir o manual (MANUAL.md, na pasta do plugin)");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Abrir a pasta de configurações e registros");
        var r = td.Show();
        if (r == TaskDialogResult.CommandLink1)
        {
            var path = Path.Combine(Path.GetDirectoryName(typeof(CmdAjuda).Assembly.Location) ?? "", "MANUAL.md");
            if (File.Exists(path)) Ui.OpenFile(path); else Ui.Info("Manual não encontrado", path);
        }
        else if (r == TaskDialogResult.CommandLink2)
        {
            Directory.CreateDirectory(SettingsStore.DefaultFolder);
            Ui.OpenFile(SettingsStore.DefaultFolder);
        }
        return Result.Succeeded;
    }
}
