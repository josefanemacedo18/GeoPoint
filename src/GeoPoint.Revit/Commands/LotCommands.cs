using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;
using GeoPoint.Core.Topology;
using GeoPoint.Revit.Infrastructure;
using GeoPoint.Revit.UI;

namespace GeoPoint.Revit.Commands;

/// <summary>Gera números sequenciais preservando prefixo e zeros à esquerda ("01" → 01, 02...; "L-5" → L-5, L-6...).</summary>
internal sealed class Numberer
{
    private readonly string _prefix;
    private readonly int _width;
    private int _next;

    public Numberer(string start)
    {
        var m = Regex.Match(start ?? "", @"^(.*?)(\d+)\s*$");
        if (m.Success)
        {
            _prefix = m.Groups[1].Value;
            _width = m.Groups[2].Value.Length;
            _next = int.Parse(m.Groups[2].Value);
        }
        else
        {
            _prefix = string.IsNullOrWhiteSpace(start) ? "" : start.Trim() + " ";
            _width = 2;
            _next = 1;
        }
    }

    public string Next() => _prefix + (_next++).ToString(new string('0', Math.Max(1, _width)));

    public static string Suggest(ParcelProject p)
    {
        int max = 0;
        foreach (var l in p.PhaseLots)
        {
            var m = Regex.Match(l.Nome ?? "", @"(\d+)\s*$");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int k)) max = Math.Max(max, k);
        }
        return (max + 1).ToString("00");
    }
}

/// <summary>Numeração acessível às janelas.</summary>
public sealed class NumbererPublic
{
    private readonly string _start;
    public NumbererPublic(string start) { _start = start; }
    /// <summary>Nome da i-ésima parte (0 = primeira) sem alterar o estado.</summary>
    public string Peek(int i)
    {
        var n = new Numberer(_start);
        string r = n.Next();
        for (int k = 0; k < i; k++) r = n.Next();
        return r;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdReconhecer : CommandBase
{
    protected override string Title => "Reconhecer lotes";
    protected override string? BackupReason => "reconhecer lotes";

    protected override Result Run(GpSession s)
    {
        var doc = s.Doc;
        var uidoc = s.UiDoc;
        var elements = uidoc.Selection.GetElementIds().Select(id => doc.GetElement(id))
            .Where(e => e is not null && (GeometryReader.IsLine(e) || GeometryReader.IsCad(e))).Select(e => e!).ToList();
        if (elements.Count == 0)
        {
            var refs = uidoc.Selection.PickObjects(ObjectType.Element, new LineAndCadFilter(),
                "GeoPoint: selecione as linhas (ou o CAD) que formam os lotes e clique em Concluir");
            elements = refs.Select(r => doc.GetElement(r)).Where(e => e is not null).Select(e => e!).ToList();
        }
        if (elements.Count == 0) return Result.Cancelled;

        ISet<string>? layers = null;
        var cads = elements.OfType<ImportInstance>().ToList();
        if (cads.Count > 0)
        {
            var all = GeometryReader.CadLayers(doc, s.ActiveView, cads);
            if (all.Count > 1)
            {
                var w = new CheckListWindow("Layers do CAD", "Marque os layers que contêm as divisas dos lotes (desmarque cotas, textos, eixos etc.).", all);
                if (Ui.ShowDialog(w, s.MainWindow) != true) return Result.Cancelled;
                layers = new HashSet<string>(w.Checked);
            }
        }

        var rec = LotServices.Recognize(s, elements, layers);
        var net = rec.Network;
        int newFaces = net.Faces.Count(f => !rec.Existing.ContainsKey(f.Id));

        var rows = new List<ReportRow>();
        rows.AddRange(rec.Warnings.Select(w => new ReportRow { Nivel = "Aviso", Mensagem = w }));
        foreach (var group in net.Diagnostics.GroupBy(d => d.Kind))
        {
            var items = group.ToList();
            if (items.Count > 25 && items[0].Severity == DiagnosticSeverity.Info)
            {
                rows.Add(new ReportRow { Nivel = "Info", Mensagem = $"{items[0].Message} ({items.Count} ocorrências)", Tags = items.SelectMany(i => i.Tags).Distinct().ToList() });
                continue;
            }
            foreach (var d in items)
                rows.Add(new ReportRow
                {
                    Nivel = d.Severity switch { DiagnosticSeverity.Error => "Erro", DiagnosticSeverity.Warning => "Atenção", _ => "Info" },
                    Mensagem = d.Message,
                    Lote = $"{Fmt.Raw(d.Location.X, 3)}; {Fmt.Raw(d.Location.Y, 3)}",
                    Tags = d.Tags,
                });
        }
        rows = rows.OrderByDescending(r => r.Nivel == "Erro" ? 3 : r.Nivel == "Atenção" ? 2 : r.Nivel == "Aviso" ? 1 : 0).ToList();
        if (rows.Count == 0) rows.Add(new ReportRow { Nivel = "OK", Mensagem = "Nenhum problema encontrado nas linhas selecionadas." });

        string summary = $"{elements.Count} elemento(s) selecionado(s), {rec.Sources.Count} linha(s) lida(s).\n" +
                         $"Áreas fechadas encontradas: {net.Faces.Count} ({rec.Existing.Count} já cadastrada(s), {newFaces} nova(s)).";
        if (net.Faces.Count == 0) summary += "\nNenhuma área fechada: verifique as falhas de conexão listadas abaixo.";

        var win = new RecognitionWindow(summary, rows, newFaces > 0, Numberer.Suggest(s.Project), s.Settings.Recognition.DefaultQuadra,
            s.Settings.Recognition.IncludeHoles, s.Settings.Recognition.MergeCollinear);
        if (Ui.ShowDialog(win, s.MainWindow) != true || win.Action == RecognitionAction.Cancel) return Result.Cancelled;

        if (win.Action == RecognitionAction.ShowIssues)
        {
            var ids = rec.ElementsOfTags(win.SelectedTags).ToList();
            if (ids.Count > 0)
            {
                uidoc.Selection.SetElementIds(ids);
                try { uidoc.ShowElements(ids); } catch { }
            }
            return Result.Succeeded;
        }

        s.Settings.Recognition.IncludeHoles = win.IncludeHolesValue;
        s.Settings.Recognition.MergeCollinear = win.MergeCollinearValue;
        s.Settings.Recognition.DefaultQuadra = win.QuadraText;
        try { s.SaveSettings(); } catch (Exception ex) { Logger.Error("Falha ao salvar configurações", ex); }

        var numberer = new Numberer(win.StartNumberText);
        string NextName() => win.SelectedUse == LotUse.Lote ? numberer.Next() : $"{LotUseInfo.Label(win.SelectedUse)} {numberer.Next()}";
        var created = new List<LotRecord>();
        // Confrontantes guardados pelo Desenhar Perímetro (certidão): vão para as divisas dos lotes criados agora.
        int confApplied = 0;
        void ApplyPending()
        {
            GeoPoint.Core.Operations.PendingConfrontantes.Prune(s.Project, uid => ElementUtil.ByUniqueId(doc, uid) is not null);
            confApplied = GeoPoint.Core.Operations.PendingConfrontantes.Apply(s.Project, created);
        }

        if (!win.ClickMode)
        {
            var faces = net.Faces.Where(f => !rec.Existing.ContainsKey(f.Id))
                .OrderByDescending(f => Math.Round(f.Region.InteriorPoint().Y, 1)).ThenBy(f => f.Region.InteriorPoint().X).ToList();
            foreach (var f in faces)
            {
                string n = NextName();
                created.Add(LotServices.CreateLot(rec, f, s.Settings, n, n, win.QuadraText, win.SelectedUse));
            }
            using var t = new Transaction(doc, "GeoPoint: reconhecer lotes");
            t.Start();
            s.Project.Lots.AddRange(created);
            ApplyPending();
            ProjectStore.Save(doc, s.Project);
            t.Commit();
        }
        else
        {
            using var tg = new TransactionGroup(doc, "GeoPoint: reconhecer lotes");
            tg.Start();
            var view = s.ActiveView;
            bool canHighlight = ElementUtil.IsPlanLike(view);
            var highlights = new List<ElementId>();
            ElementId? tempType = null;
            bool typeExisted = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Any(x => x.Name == "GeoPoint - Seleção temporária");
            var used = new HashSet<int>();
            while (true)
            {
                XYZ pt;
                try
                {
                    pt = uidoc.Selection.PickPoint(ObjectSnapTypes.None, $"GeoPoint: clique dentro de um lote ({created.Count} criado(s)). Esc para terminar.");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { break; }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    Ui.Warn("Vista sem plano de trabalho", "Abra uma vista de planta para clicar dentro dos lotes, ou use a opção 'Criar todos'.");
                    break;
                }
                var face = net.FindFace(RvtUnits.ToVec(pt));
                if (face is null) { Ui.Info("Nenhuma área fechada neste ponto", "Clique dentro de um contorno fechado pelas linhas selecionadas."); continue; }
                if (rec.Existing.TryGetValue(face.Id, out var ex)) { Ui.Info("Área já cadastrada", $"Esta área já é {ex.DisplayName}."); continue; }
                if (!used.Add(face.Id)) continue;
                string n = NextName();
                var lot = LotServices.CreateLot(rec, face, s.Settings, n, n, win.QuadraText, win.SelectedUse);
                created.Add(lot);
                if (canHighlight)
                {
                    using var th = new Transaction(doc, "GeoPoint: destacar lote");
                    th.Start();
                    try
                    {
                        tempType ??= RevitStyles.FillType(doc, LotUse.Lote, "GeoPoint - Seleção temporária", (180, 215, 255));
                        double z = ElementUtil.ViewPlaneZ(view);
                        var loops = new List<CurveLoop> { RvtUnits.ToCurveLoop(face.Region.Outer, z) };
                        var fr = FilledRegion.Create(doc, tempType, view.Id, loops);
                        highlights.Add(fr.Id);
                        th.Commit();
                    }
                    catch (Exception hex)
                    {
                        Logger.Error("Destaque temporário falhou", hex);
                        th.RollBack();
                        canHighlight = false;
                    }
                }
            }
            if (created.Count == 0)
            {
                tg.RollBack();
                return Result.Cancelled;
            }
            using (var t = new Transaction(doc, "GeoPoint: gravar lotes"))
            {
                t.Start();
                var del = highlights.Where(id => doc.GetElement(id) is not null).ToList();
                if (del.Count > 0) doc.Delete(del);
                if (!typeExisted && tempType is not null && doc.GetElement(tempType) is not null)
                {
                    try { doc.Delete(tempType); } catch { }
                }
                s.Project.Lots.AddRange(created);
                ApplyPending();
                ProjectStore.Save(doc, s.Project);
                t.Commit();
            }
            tg.Assimilate();
        }

        if (created.Count == 0)
        {
            Ui.Info("Nenhum lote criado", "Não havia áreas novas para cadastrar.");
            return Result.Succeeded;
        }
        string confNote = confApplied > 0
            ? $"{confApplied} divisa(s) já receberam o confrontante informado no Desenhar Perímetro (certidão).\n\n"
            : "";
        if (Ui.Confirm($"{created.Count} lote(s) criado(s)", confNote + "Deseja abrir o Gerenciador de Lotes agora para informar nomes, confrontantes e testadas?",
                "Abrir o Gerenciador de Lotes", "Agora não"))
            CmdGerenciar.OpenManager(s, created.Select(c => c.Id));
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdGerenciar : CommandBase
{
    protected override string Title => "Gerenciar lotes";
    protected override string? BackupReason => "gerenciar lotes";

    protected override Result Run(GpSession s)
    {
        if (s.Project.Lots.Count == 0)
        {
            Ui.Info("Nenhum lote cadastrado", "Use 'Reconhecer Lotes' para criar lotes a partir das linhas do desenho.");
            return Result.Cancelled;
        }
        var focus = LotServices.LotsFromSelection(s);
        return OpenManager(s, focus) ? Result.Succeeded : Result.Cancelled;
    }

    /// <summary>Remove do modelo os elementos criados pelo GeoPoint para o lote (não apaga as linhas de origem).</summary>
    internal static void DeleteGeoPointElements(Document doc, LotRecord lot, bool deleteTableView = true)
    {
        var uids = lot.Annotations.SelectMany(a => a.ElementIds)
            .Concat(lot.FilledRegionIds).Concat(lot.AuxElementIds)
            .Append(lot.PropertyLineId).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
        var ids = ElementUtil.ExistingIds(doc, uids);
        foreach (var id in ids)
        {
            try { if (doc.GetElement(id) is not null) doc.Delete(id); }
            catch (Exception ex) { Logger.Error("Falha ao remover elemento do lote excluído", ex); }
        }
        // Vista de desenho da tabela de coordenadas.
        if (!deleteTableView) return;
        string viewName = Ui.SafeFileName($"GeoPoint - Coordenadas - {lot.DisplayName}").Replace("{", "(").Replace("}", ")");
        var v = new FilteredElementCollector(doc).OfClass(typeof(ViewDrafting)).Cast<ViewDrafting>().FirstOrDefault(x => x.Name == viewName);
        if (v is not null)
        {
            try { doc.Delete(v.Id); } catch { }
        }
    }

    internal static bool OpenManager(GpSession s, IEnumerable<string> focusIds)
    {
        var ctx = s.NewContext();
        var w = new LotManagerWindow(s.Project, ctx, focusIds);
        if (Ui.ShowDialog(w, s.MainWindow) != true) return false;
        using (var t = new Transaction(s.Doc, "GeoPoint: editar lotes"))
        {
            t.Start();
            foreach (var lot in w.DeletedLots) DeleteGeoPointElements(s.Doc, lot);
            ProjectStore.Save(s.Doc, s.Project);
            t.Commit();
        }
        if (w.ShowInRevit && w.CurrentLot is not null) LotServices.SelectSources(s, new[] { w.CurrentLot });
        return true;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdAtualizar : CommandBase
{
    protected override string Title => "Atualizar lotes";
    protected override string? BackupReason => "atualizar lotes";

    protected override Result Run(GpSession s)
    {
        var lots = s.Project.ActiveLots.ToList();
        if (lots.Count == 0) { Ui.Info("Nenhum lote ativo", "Não há lotes para atualizar."); return Result.Cancelled; }
        var items = LotServices.ComputeSync(s, lots);
        var changed = items.Where(i => i.Changed).ToList();
        var rows = items.Select(i => new ReportRow
        {
            Nivel = i.Failed ? "Atenção" : i.Changed ? "Alterado" : "OK",
            Categoria = i.Changed ? $"{Fmt.N(i.OldArea, 2)} → {Fmt.N(i.NewArea, 2)} m²" : "",
            Lote = i.Lot.DisplayName,
            Mensagem = i.Status,
            LotId = i.Lot.Id,
        }).OrderByDescending(r => r.Nivel == "Atenção" ? 2 : r.Nivel == "Alterado" ? 1 : 0).ToList();
        string summary = $"{items.Count} lote(s) verificado(s): {changed.Count} com geometria alterada, {items.Count(i => i.Failed)} com problema.";
        var w = new ReportWindow("Atualizar lotes a partir do desenho", summary, rows, changed.Count > 0 ? $"Atualizar {changed.Count} lote(s)" : null);
        if (Ui.ShowDialog(w, s.MainWindow) != true) return Result.Cancelled;
        if (w.Action == ReportAction.Select)
        {
            LotServices.SelectSources(s, w.SelectedRows.Where(r => r.LotId is not null).Select(r => s.Project.FindLot(r.LotId!)).OfType<LotRecord>());
            return Result.Succeeded;
        }
        if (w.Action != ReportAction.Accept) return Result.Cancelled;
        foreach (var i in changed) LotServices.ApplySync(i);
        s.SaveProject("GeoPoint: atualizar lotes");
        Ui.Info("Lotes atualizados", $"{changed.Count} lote(s) atualizado(s). Confrontantes e testadas foram mantidos nas divisas correspondentes.");
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdDadosImovel : CommandBase
{
    protected override string Title => "Dados do imóvel";
    protected override string? BackupReason => "dados do imovel";

    protected override Result Run(GpSession s)
    {
        if (!string.IsNullOrWhiteSpace(s.Project.Fase.Situacao)) s.Project.Info.Situacao = s.Project.Fase.Situacao;
        var w = new ProjectInfoWindow(s.Project.Info, s.Settings);
        if (Ui.ShowDialog(w, s.MainWindow) != true) return Result.Cancelled;
        s.Project.Fase.Situacao = s.Project.Info.Situacao;
        s.SaveProject("GeoPoint: dados do imóvel");
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdGerarLote : CommandBase
{
    protected override string Title => "Gerar lote";
    protected override string? BackupReason => "gerar lote";

    protected override Result Run(GpSession s)
    {
        var view = s.ActiveView;
        if (view is ViewSheet || !ElementUtil.IsPlanLike(view))
        {
            Ui.Warn("Vista inadequada", "Abra uma vista de planta para gerar o lote.");
            return Result.Cancelled;
        }
        var w = new LotGeneratorWindow(Numberer.Suggest(s.Project), s.Settings.Recognition.DefaultQuadra);
        if (Ui.ShowDialog(w, s.MainWindow) != true || w.Result is not { Ok: true } shape || w.Shape is null) return Result.Cancelled;
        if (w.Detail && !ElementUtil.SupportsDetails(view))
        {
            Ui.Warn("Vista inadequada", "Esta vista não aceita linhas de detalhe. Use linhas de modelo ou abra uma planta.");
            return Result.Cancelled;
        }
        var ctx = s.NewContext();

        // 1º clique: canto frente / lateral esquerda.
        XYZ p0;
        try
        {
            p0 = s.UiDoc.Selection.PickPoint(ObjectSnapTypes.Endpoints | ObjectSnapTypes.Intersections | ObjectSnapTypes.Midpoints | ObjectSnapTypes.Nearest,
                "GeoPoint: clique no canto FRENTE / LATERAL ESQUERDA do lote");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException) { Ui.Warn("Vista sem plano de trabalho", "Abra uma vista de planta."); return Result.Cancelled; }
        var origin = RvtUnits.ToVec(p0);
        double angle;
        switch (w.Direction)
        {
            case GenDirection.Pick:
                try
                {
                    var p1 = RvtUnits.ToVec(s.UiDoc.Selection.PickPoint(ObjectSnapTypes.Endpoints | ObjectSnapTypes.Intersections | ObjectSnapTypes.Nearest | ObjectSnapTypes.Perpendicular,
                        "GeoPoint: clique um ponto na direção da FRENTE (para o lado direito do lote)"));
                    if (p1.DistanceTo(origin) < 0.01) { Ui.Warn("Direção", "O segundo ponto coincide com o primeiro."); return Result.Cancelled; }
                    angle = Math.Atan2(p1.Y - origin.Y, p1.X - origin.X);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
                break;
            case GenDirection.Azimuth:
            {
                var d = ctx.Coords.DirectionFromAzimuth(w.AzimuthDeg);
                angle = Math.Atan2(d.Y, d.X);
                break;
            }
            default:
                angle = Math.Atan2(view.RightDirection.Y, view.RightDirection.X);
                break;
        }

        var lotsCurves = Enumerable.Range(0, w.CountValue).Select(k => LotShapes.Place(shape.Edges, origin, angle, k * shape.FrontWidth)).ToList();
        string Key(Curve2 c)
        {
            string P(Vec2 v) => $"{Math.Round(v.X, 3)};{Math.Round(v.Y, 3)}";
            string a = P(c.Start), b = P(c.End);
            return (string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a) + "|" + P(c.Mid);
        }
        var unique = new Dictionary<string, Curve2>();
        foreach (var c in lotsCurves.SelectMany(x => x)) unique.TryAdd(Key(c), c);

        var created = new List<LotRecord>();
        using (var t = new Transaction(s.Doc, "GeoPoint: gerar lote"))
        {
            t.Start();
            var target = CurveTarget.ForActiveView(s.Doc, view, w.Detail);
            var keys = unique.Keys.ToList();
            var els = target.Create(s.Doc, keys.Select(k => unique[k]), null);
            if (els.Count != keys.Count) { t.RollBack(); Ui.Error("Gerar lote", "Não foi possível desenhar todas as linhas."); return Result.Failed; }
            var uidByKey = keys.Select((k, i) => (k, els[i].UniqueId)).ToDictionary(x => x.k, x => x.UniqueId);
            if (w.RegisterLots)
            {
                var numberer = new Numberer(w.StartName);
                var now = DateTime.Now;
                for (int k = 0; k < lotsCurves.Count; k++)
                {
                    string name = w.UseValue == LotUse.Lote ? numberer.Next() : $"{LotUseInfo.Label(w.UseValue)} {numberer.Next()}";
                    var curves = lotsCurves[k];
                    var lot = new LotRecord
                    {
                        Nome = name, Numero = name, Quadra = w.QuadraText, Uso = w.UseValue,
                        Outer = curves.Select(c => EdgeDto.FromCurve(c, new[] { uidByKey[Key(c)] })).ToList(),
                        ZFeet = target.ZFeet,
                        SourceKind = w.Detail ? "DetailLine" : "ModelLine",
                        SourceViewId = w.Detail ? view.UniqueId : "",
                        SketchPlaneId = target.Sketch?.UniqueId ?? "",
                        Created = now.AddMilliseconds(k),
                    };
                    if (w.MarkTestada)
                    {
                        var m = curves[0].Mid;
                        lot.EdgeInfos.Add(new EdgeInfo { MX = m.X, MY = m.Y, Testada = true });
                    }
                    var reg = lot.ToRegion();
                    lot.History.Add(new HistoryEntry { Description = $"Gerado pelo formato \"{w.Shape.Nome}\"", Area = reg.Area, Perimeter = reg.Outer.Perimeter });
                    created.Add(lot);
                    s.Project.Lots.Add(lot);
                }
                ctx = s.NewContext();
                VertexRegistry.Register(ctx, created);
                if (w.AnnotateNow && ElementUtil.SupportsDetails(view))
                {
                    try { Annotator.Annotate(s.Doc, view, created, ctx, new ValueFormatter(s.Settings.Format), Annotator.Options.From(s.Settings.Annotation)); }
                    catch (Exception ex) { Logger.Error("Anotar lote gerado", ex); }
                }
                ProjectStore.Save(s.Doc, s.Project);
            }
            t.Commit();
        }
        string msg = w.RegisterLots
            ? $"{created.Count} lote(s) gerado(s) e cadastrado(s) na {s.Project.Fase.Label}: {string.Join(", ", created.Select(l => l.DisplayName))}.\nÁrea de cada lote: {Fmt.N(shape.Area, 2)} m²."
            : $"Linhas do formato \"{w.Shape.Nome}\" desenhadas ({unique.Count} linha(s)). Use 'Reconhecer Lotes' para cadastrar.";
        Ui.Info("Gerar lote", msg + "\n\nAs linhas são nativas do Revit: podem ser editadas depois (use 'Atualizar Lotes').");
        return Result.Succeeded;
    }
}
