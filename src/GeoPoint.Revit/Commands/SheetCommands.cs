using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Sheets;
using GeoPoint.Core.Urban;
using GeoPoint.Revit.Infrastructure;
using GeoPoint.Revit.UI;

namespace GeoPoint.Revit.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class CmdPranchas : CommandBase
{
    protected override string Title => "Pranchas";
    protected override string? BackupReason => "pranchas";

    protected override Result Run(GpSession s)
    {
        if (!s.Project.Lots.Any()) { Ui.Info("Nenhum lote cadastrado", "Use 'Reconhecer Lotes' primeiro."); return Result.Cancelled; }
        return GenerateForOperation(s, s.Project.PhaseOperations.OrderByDescending(o => o.Date).FirstOrDefault(), ask: true) ? Result.Succeeded : Result.Cancelled;
    }

    /// <summary>Abre a janela de pranchas (pré-selecionando a operação) e gera as pranchas.</summary>
    internal static bool GenerateForOperation(GpSession s, OperationRecord? op, bool ask)
    {
        if (!NorthGuard.Ensure(s)) return false;
        var source = SourceView(s);
        if (source is null)
        {
            Ui.Warn("Vista inadequada", "Abra a vista de planta onde os lotes estão desenhados e use 'Pranchas'.");
            return false;
        }
        var state = new PranchasState { OpId = op?.Id, UseOp = op is not null };
        // Opções de ponto de referência por prancha (chave → ponto do desenho).
        var points = new Dictionary<string, Vec2>();
        var localSettings = s.Settings.Clone();
        localSettings.Naming.Numbering = VertexNumbering.Lote;
        var local = new ProjectContext(s.Project, localSettings);
        List<IdText> RefOptions(SheetSpec sp)
        {
            // Prancha geral: cada lote mostra a coordenada do seu próprio ponto de referência.
            if (!sp.Detailed) return new List<IdText> { new("", "Cada lote: o seu ponto") };
            var lot0 = s.Project.FindLot(sp.LotIds[0]);
            string current = "P-01";
            if (lot0 is { RefX: double rx, RefY: double ry })
                try { current = local.Report(lot0).Vertices.OrderBy(v => v.Internal.DistanceTo(new Vec2(rx, ry))).First().Name; } catch { }
            var list = new List<IdText> { new("", $"Guardado no lote ({current})") };
            var seen = new HashSet<string>();
            foreach (var id in sp.LotIds)
            {
                var lot = s.Project.FindLot(id);
                if (lot is null) continue;
                try
                {
                    foreach (var v in local.Report(lot).Vertices)
                    {
                        string k = AnnotationPlanner.VertexKey(v.Internal);
                        points[k] = v.Internal;
                        if (seen.Add(k)) list.Add(new IdText(k, sp.Detailed ? v.Name : $"{lot.DisplayName} – {v.Name}"));
                    }
                }
                catch { }
            }
            return list;
        }
        (bool, bool) Existing(SheetSpec sp)
        {
            var g = SheetRegistry.Find(s, sp.Key);
            return g is null ? (false, false) : (true, SheetRegistry.IsEdited(s, g));
        }

        PranchasWindow w;
        while (true)
        {
            var rows = PickerRows.Build(s, false, l => l.Status == LotStatus.Ativo || s.Project.PhaseOperations.Any(o => o.ParentIds.Contains(l.Id) || o.ChildIds.Contains(l.Id)));
            var pctx = s.NewContext();
            SheetComposition? Preview(SheetSpec sp, SheetTemplate tpl, int scale)
            {
                var ls = sp.LotIds.Select(s.Project.FindLot).Where(l => l is not null).Select(l => l!).ToList();
                if (ls.Count == 0) return null;
                double mm = tpl.Styles.DesenhoMm > 0 ? tpl.Styles.DesenhoMm : s.Settings.Annotation.TextSizeMm;
                return SheetPreview.Compose(pctx, ls, sp, tpl, mm, scale, Hatcher.Palette, r => Annotator.LotLabel(r, pctx, new ValueFormatter(s.Settings.Format), Annotator.Options.From(s.Settings.Annotation)));
            }
            w = new PranchasWindow(s.Project, s.Settings.Sheets, rows, op, state, RefOptions, Existing, Preview);
            if (Ui.ShowDialog(w, s.MainWindow) != true || w.Action == PranchasAction.Cancel) return false;
            w.Apply(s.Settings.Sheets, s.Project.Info);
            if (w.Action == PranchasAction.Generate) break;
            // Escolher o ponto de referência no desenho.
            var spec = w.Specs.FirstOrDefault(x => x.Key == state.PickKey);
            if (spec is null) continue;
            if (!spec.Detailed)
            {
                Ui.Info("Ponto de referência", "Nas pranchas gerais cada lote mostra a coordenada do seu próprio ponto. Escolha o ponto numa prancha de um lote (ele fica guardado no lote).");
                continue;
            }
            try
            {
                var pick = RvtUnits.ToVec(s.UiDoc.Selection.PickPoint(Autodesk.Revit.UI.Selection.ObjectSnapTypes.Endpoints | Autodesk.Revit.UI.Selection.ObjectSnapTypes.Intersections,
                    $"GeoPoint: clique no vértice cuja coordenada aparece no mapa de \"{spec.Title}\""));
                var opts = RefOptions(spec).Where(o => o.Id.Length > 0 && points.ContainsKey(o.Id)).ToList();
                var best = opts.OrderBy(o => points[o.Id].DistanceTo(pick)).FirstOrDefault();
                if (best is null || points[best.Id].DistanceTo(pick) > 1.0)
                    Ui.Warn("Ponto de referência", "O ponto clicado não é um vértice dos lotes desta prancha (tolerância de 1 m).");
                else state.RefByKey[spec.Key] = best.Id;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException) { Ui.Warn("Vista sem plano de trabalho", "Abra uma vista de planta para clicar no ponto."); }
        }
        try { s.SaveSettings(); } catch { }
        var st = s.Settings.Sheets;
        var specs = w.Specs;
        // Ponto de referência escolhido numa prancha de um lote: fica guardado no lote (vale para todas as pranchas dele).
        foreach (var sp in specs.Where(x => x.Detailed))
            if (state.RefByKey.TryGetValue(sp.Key, out var k) && k.Length > 0 && points.TryGetValue(k, out var pt) && s.Project.FindLot(sp.LotIds[0]) is { } lot)
            {
                lot.RefX = pt.X;
                lot.RefY = pt.Y;
            }
        var template = w.SelectedTemplate ?? SheetTemplateLibrary.Find(st.Template);

        // Conferência antes de emitir: dados que faltam ou diferem da matrícula.
        var pend = new List<string>();
        var pendLots = new HashSet<string>();
        var fmtC = new ValueFormatter(s.Settings.Format);
        var defC = s.Settings.Memorial.CoordinateSystems.FirstOrDefault() ?? GeoPoint.Core.Geodesy.CrsDef.Utm();
        foreach (var sp in specs)
        {
            var ls = sp.LotIds.Select(s.Project.FindLot).Where(l => l is not null).Select(l => l!).ToList();
            try
            {
                var cc = SheetContent.Build(s.NewContext(), fmtC, sp, ls, defC);
                pend.AddRange(cc.Pendencias);
                pendLots.UnionWith(cc.PendenciaLotIds);
            }
            catch { }
        }
        foreach (var warn in template.Validate()) pend.Add("Modelo de prancha: " + warn);
        pend = pend.Distinct().ToList();
        if (pend.Count > 0)
        {
            var td = new TaskDialog("GeoPoint – Conferência antes de emitir")
            {
                MainInstruction = $"{pend.Count} pendência(s) encontrada(s)",
                MainContent = "• " + string.Join("\n• ", pend.Take(15)) + (pend.Count > 15 ? $"\n… e mais {pend.Count - 15}." : ""),
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Gerar as pranchas mesmo assim", "Os campos vazios ficam com \"—\" no carimbo; você pode completar depois e gerar de novo.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Voltar e corrigir", "Abra Dados do Imóvel, Gerenciar Lotes ou Configurações.");
            if (pendLots.Count > 0)
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Selecionar no desenho os lotes com pendência", $"{pendLots.Count} lote(s) ficam selecionados e enquadrados na vista.");
            var res = td.Show();
            if (res == TaskDialogResult.CommandLink3) LotServices.SelectSources(s, pendLots.Select(s.Project.FindLot).Where(l => l is not null).Select(l => l!));
            if (res != TaskDialogResult.CommandLink1) return false;
        }
        if (s.Project.Fases.Count > 1)
            foreach (var sp in specs) sp.Subtitle = string.Join(" – ", new[] { sp.Subtitle, s.Project.Fase.Label.ToUpperInvariant() }.Where(x => x.Length > 0));
        var chosenOp = w.Operation;

        var builder = new SheetBuilder(s, source, new SheetBuildOptions
        {
            Template = template,
            Scale = st.AutoScale ? 0 : st.Scale,
            Croqui = st.CroquiAuto && CroquiService.EnsureTerms(s) ? CroquiService.ForSheets(s, template) : null,
            Anotacoes = st.Anotacoes, Vias = st.Vias, NorteParaCima = st.NorteParaCima,
            LocationImage = s.Project.Info.ImagemLocalizacao, NumberPrefix = st.NumberPrefix, Font = s.Settings.Annotation.Font,
        });
        List<ViewSheet> sheets;
        using (var t = new Transaction(s.Doc, "GeoPoint: pranchas"))
        {
            t.Start();
            sheets = builder.Build(specs);
            if (chosenOp is not null) chosenOp.GeneratedIds.AddRange(builder.Created);
            ProjectStore.Save(s.Doc, s.Project);
            t.Commit();
        }
        if (sheets.Count > 0)
        {
            try { s.UiDoc.ActiveView = sheets[0]; } catch { }
        }
        string msg = $"{sheets.Count} prancha(s) criada(s): {string.Join(", ", sheets.Select(x => x.SheetNumber))}." +
                     (builder.Kept > 0 ? $"\n{builder.Kept} prancha(s) existente(s) mantida(s) como estavam." : "");
        if (builder.Warnings.Count > 0) Ui.Warn("Pranchas geradas com avisos", msg + "\n\n" + string.Join("\n", builder.Warnings.Distinct().Take(20)));
        else Ui.Info("Pranchas geradas", msg + "\n\nAs pranchas ficam editáveis no Revit. Depois de revisar, use 'Exportar PDF' (ou Arquivo › Exportar › PDF do Revit).");
        return sheets.Count > 0 || builder.Kept > 0;
    }

    /// <summary>Vista de planta com os lotes: a vista ativa ou, se for prancha, a primeira planta usada pelos lotes.</summary>
    internal static View? SourceView(GpSession s)
    {
        var v = s.ActiveView;
        if (ElementUtil.IsPlanLike(v) && v is not ViewSheet) return v;
        if (PhaseViews.ActivePhaseView(s) is View pv && ElementUtil.IsPlanLike(pv)) return pv;
        foreach (var lot in s.Project.PhaseLots)
            if (ElementUtil.ByUniqueId(s.Doc, lot.SourceViewId) is View sv && ElementUtil.IsPlanLike(sv)) return sv;
        return null;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdHachuras : CommandBase
{
    protected override string Title => "Hachuras";
    protected override string? BackupReason => "hachuras";

    protected override Result Run(GpSession s)
    {
        var view = s.ActiveView;
        if (!ElementUtil.SupportsDetails(view) || view is ViewSheet) { Ui.Warn("Vista inadequada", "Abra uma vista de planta para hachurar os lotes."); return Result.Cancelled; }
        if (!s.Project.Lots.Any()) { Ui.Info("Nenhum lote cadastrado", "Use 'Reconhecer Lotes' primeiro."); return Result.Cancelled; }
        var opts = new List<OptionItem>
        {
            new("clear", "Somente remover as hachuras do GeoPoint dos lotes marcados", false),
            new("space", "Espaçamento da hachura no papel (mm)", 1.6),
            new("par", "Preencher parâmetros GP_* (lote, quadra, uso, matrícula, área, perímetro…) nas hachuras, para tabelas e etiquetas do Revit", true),
        };
        var w = new LotPickerWindow("Hachuras dos lotes", "Cada lote recebe uma cor; o lote remanescente recebe hachura cruzada. As bordas da hachura são invisíveis (as divisas continuam sendo as suas linhas).",
            PickerRows.Build(s, false), opts, "Aplicar");
        if (Ui.ShowDialog(w, s.MainWindow) != true) return Result.Cancelled;
        var lots = w.SelectedLots;
        int n = 0;
        using (var t = new Transaction(s.Doc, "GeoPoint: hachuras"))
        {
            t.Start();
            if (w.Option("clear")) foreach (var l in lots) Hatcher.Clear(s.Doc, l, view);
            else
            {
                var order = ProjectContext.SortLots(lots).ToList();
                Action<FilledRegion, LotRecord>? fill = null;
                if (w.Option("par"))
                {
                    // Parâmetros compartilhados GP_* na categoria Componentes de detalhe (regiões preenchidas).
                    foreach (var warn in NativeElements.EnsureParameters(s.Doc, new[] { BuiltInCategory.OST_DetailComponents })) Logger.Info("Parâmetros GP_*: " + warn);
                    var ctx = s.NewContext();
                    fill = (fr, l) => NativeElements.SetParameters(fr, l, ctx.Report(l));
                }
                n = Hatcher.Apply(s.Doc, view, order, l => order.IndexOf(l), Math.Clamp(w.Number("space"), 0.5, 10), after: fill);
            }
            ProjectStore.Save(s.Doc, s.Project);
            t.Commit();
        }
        if (n > 0) Ui.Info("Hachuras", $"{n} lote(s) hachurado(s) na vista \"{view.Name}\".");
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdAnuencia : CommandBase
{
    protected override string Title => "Anuência de confrontantes";

    protected override Result Run(GpSession s)
    {
        if (!s.Project.ActiveLots.Any()) { Ui.Info("Nenhum lote ativo", "Use 'Reconhecer Lotes' primeiro."); return Result.Cancelled; }
        if (!NorthGuard.Ensure(s)) return Result.Cancelled;
        var opts = new List<OptionItem>
        {
            new("vias", "Incluir as vias públicas (declaração do poder público)", false),
            new("one", "Um arquivo por lote", true),
        };
        var w = new LotPickerWindow("Declaração de anuência de confrontantes",
            "Gera, para cada lote marcado, uma declaração por confrontante com a descrição da divisa comum (vértices, azimutes e distâncias), para assinatura do confrontante e de testemunhas.",
            PickerRows.Build(s, true), opts, "Gerar documento");
        if (Ui.ShowDialog(w, s.MainWindow) != true) return Result.Cancelled;
        var ctx = s.NewContext();
        var gen = new AnuenciaGenerator(ctx);
        bool vias = w.Option("vias");
        var lots = w.SelectedLots;
        string folder = Folders.Default(s, s.Settings.Memorial.OutputFolder);
        var files = new List<string>();
        var empty = new List<string>();
        if (lots.Count == 1 || !w.Option("one"))
        {
            foreach (var lot in lots)
            {
                var groups = AnuenciaGenerator.Groups(ctx, lot, vias);
                if (groups.Count == 0) { empty.Add(lot.DisplayName); continue; }
                var path = Ui.SaveFile("Salvar declaração de anuência", "Documento do Word (*.docx)|*.docx", $"Anuência de confrontantes - {lot.DisplayName}.docx", folder);
                if (path is null) return Result.Cancelled;
                if (!Ui.TryWrite(path, gen.Generate(lot, groups, DateTime.Today))) return Result.Failed;
                files.Add(path);
            }
        }
        else
        {
            var dir = Ui.PickFolder("Pasta para as declarações (um arquivo por lote)", folder);
            if (dir is null) return Result.Cancelled;
            foreach (var lot in lots)
            {
                var groups = AnuenciaGenerator.Groups(ctx, lot, vias);
                if (groups.Count == 0) { empty.Add(lot.DisplayName); continue; }
                var path = Path.Combine(dir, Ui.SafeFileName($"Anuência de confrontantes - {lot.DisplayName}") + ".docx");
                if (!Ui.TryWrite(path, gen.Generate(lot, groups, DateTime.Today))) return Result.Failed;
                files.Add(path);
            }
        }
        if (empty.Count > 0)
            Ui.Warn("Lotes sem confrontantes", "Sem confrontantes informados (Gerenciar Lotes › Divisas): " + string.Join(", ", empty));
        if (files.Count == 1) Ui.OpenFile(files[0]);
        else if (files.Count > 1) Ui.OpenFolder(Path.GetDirectoryName(files[0])!);
        return files.Count > 0 ? Result.Succeeded : Result.Cancelled;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdVias : CommandBase
{
    protected override string Title => "Vias";
    protected override string? BackupReason => "vias";

    /// <summary>Seleção no desenho: linhas das testadas e linha(s) do meio-fio do lado dos lotes.</summary>
    private static void PickStreet(GpSession s, ProjectContext ctx, ViaRecord via)
    {
        List<Curve2> Pick(string prompt)
        {
            try
            {
                var refs = s.UiDoc.Selection.PickObjects(Autodesk.Revit.UI.Selection.ObjectType.Element, new LineAndCadFilter(false), prompt);
                var els = refs.Select(r => s.Doc.GetElement(r)).Where(e => e is not null).Select(e => e!).ToList();
                return GeometryReader.Read(s.Doc, s.ActiveView, els, null, new List<string>()).Select(c => c.Curve).ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return new List<Curve2>(); }
        }
        // 1) Testadas: divisas de lotes cobertas pelas linhas selecionadas.
        var testadas = Pick($"GeoPoint – {via.Nome}: selecione as linhas das TESTADAS (divisas de frente para a rua) e clique em Concluir");
        int marked = 0;
        var frontCurves = new List<Curve2>();
        if (testadas.Count > 0)
            foreach (var lot in s.Project.ActiveLots)
            {
                LotReport rep;
                try { rep = ctx.Report(lot); } catch { continue; }
                foreach (var sg in rep.Segments)
                {
                    bool covered = new[] { 0.1, 0.5, 0.9 }.All(t => { var p = sg.Curve.PointAt(t); return testadas.Any(c => c.DistanceTo(p) <= 0.02); });
                    if (!covered) continue;
                    var info = ProjectContext.FindInfo(lot, sg.Curve.Mid);
                    if (info is null) { info = new EdgeInfo { MX = sg.Curve.Mid.X, MY = sg.Curve.Mid.Y }; lot.EdgeInfos.Add(info); }
                    info.Testada = true;
                    info.Tipo = ConfrontanteTipo.Via;
                    info.ViaId = via.Id;
                    info.LotId = "";
                    frontCurves.Add(sg.Curve);
                    marked++;
                }
            }
        if (testadas.Count > 0 && marked == 0)
            Ui.Warn("Testadas", "Nenhuma divisa de lote coincide com as linhas selecionadas. Selecione as linhas que formam a frente dos lotes.");
        // 2) Meio-fio (lado dos lotes).
        var curbs = Pick($"GeoPoint – {via.Nome}: selecione a(s) linha(s) do MEIO-FIO do lado dos lotes e clique em Concluir (Esc = manter as atuais)");
        if (curbs.Count > 0)
        {
            via.MeioFio = curbs.Select(c => c.Densify(0.02).Select(p => new PointDto { X = p.X, Y = p.Y }).ToList()).ToList();
            via.Prolongamento = 0;
        }
        // Calçada medida entre testadas e meio-fio.
        if (frontCurves.Count == 0)
            frontCurves = s.Project.ActiveLots.SelectMany(l => { try { return ctx.Report(l).Segments.Where(x => x.ConfrontanteViaId == via.Id).Select(x => x.Curve); } catch { return Enumerable.Empty<Curve2>(); } }).ToList();
        if (via.MeioFio.Count > 0 && frontCurves.Count > 0)
        {
            double c = StreetSchematic.MeasureCalcada(frontCurves, via.MeioFio.Select(m => m.Select(p => p.ToVec()).ToList()));
            if (!double.IsNaN(c) && c > 0.05) via.Calcada = Math.Round(c, 3);
        }
        string msg = $"Via \"{via.Nome}\": {marked} testada(s) marcada(s)" + (via.MeioFio.Count > 0 ? $", meio-fio com {via.MeioFio.Count} linha(s), calçada medida de {Fmt.N(via.Calcada, 2)} m." : ".");
        Logger.Info(msg);
        Ui.Info("Vias", msg + "\n\nConfira na janela e clique em Salvar para desenhar.");
    }

    protected override Result Run(GpSession s)
    {
        if (!s.Project.ActiveLots.Any()) { Ui.Info("Nenhum lote ativo", "Use 'Reconhecer Lotes' primeiro."); return Result.Cancelled; }
        ViasWindow w;
        var ctx = s.NewContext();
        while (true)
        {
            ctx = s.NewContext();
            w = new ViasWindow(s.Project, ctx, s.Settings.Streets);
            if (Ui.ShowDialog(w, s.MainWindow) != true) return Result.Cancelled;
            if (w.PickVia is null) break;
            PickStreet(s, ctx, w.PickVia);
        }
        var view = s.ActiveView;
        bool draw = w.DrawNow;
        if (draw && (!ElementUtil.SupportsDetails(view) || view is ViewSheet))
        {
            Ui.Warn("Vista inadequada", "As vias foram salvas, mas a vista atual não aceita linhas de detalhe. Abra uma planta e use 'Vias' novamente para desenhar.");
            draw = false;
        }
        List<string> warnings = new();
        using (var t = new Transaction(s.Doc, "GeoPoint: vias"))
        {
            t.Start();
            if (draw)
            {
                ctx = s.NewContext();
                warnings = StreetDrawer.Draw(s.Doc, view, ctx, s.Project.ActiveLots.ToList(), s.Settings.Streets, s.Settings.Annotation.Font);
            }
            ProjectStore.Save(s.Doc, s.Project);
            t.Commit();
        }
        if (warnings.Count > 0) Ui.Warn("Vias", string.Join("\n", warnings));
        return Result.Succeeded;
    }
}
