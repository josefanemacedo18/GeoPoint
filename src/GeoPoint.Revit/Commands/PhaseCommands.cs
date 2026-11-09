using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;
using GeoPoint.Revit.Infrastructure;
using GeoPoint.Revit.UI;

namespace GeoPoint.Revit.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class CmdFases : CommandBase
{
    protected override string Title => "Fases do projeto";

    protected override Result Run(GpSession s)
    {
        string ViewName(string uid) => ElementUtil.ByUniqueId(s.Doc, uid) is View v ? v.Name : "";
        var w = new FasesWindow(s.Project, ViewName);
        if (Ui.ShowDialog(w, s.MainWindow) != true || w.Action == FaseAction.None) return Result.Cancelled;
        var sel = w.Selected;
        var doc = s.Doc;
        View? open = null;
        string msg = "";
        using (var t = new Transaction(doc, "GeoPoint: fases"))
        {
            t.Start();
            switch (w.Action)
            {
                case FaseAction.Create:
                {
                    var baseF = w.BaseId.Length > 0 ? s.Project.FindFase(w.BaseId) ?? s.Project.Fase : s.Project.Fase;
                    var source = PlanSource(s, baseF);
                    // A vista de origem passa a ser a vista da fase de partida (se ela ainda não tiver vista).
                    if (source is not null && baseF.ViewIds.Count == 0) baseF.ViewIds.Add(source.UniqueId);
                    var oldAnnotated = s.Project.Lots.Where(l => l.FaseId == baseF.Id && l.Status == LotStatus.Ativo)
                        .GroupBy(l => (l.Nome, l.Quadra))
                        .ToDictionary(g => g.Key, g => (ann: g.Any(l => l.Annotations.Any(a => a.ViewId == source?.UniqueId)), hat: g.Any(l => l.Hatches.Any(h => h.ViewId == source?.UniqueId))));
                    var f = FaseService.Create(s.Project, w.NewNameText, w.NewSituacaoText, w.BaseId, w.BaseId.Length > 0 ? w.LotIds : null);
                    var ctx = s.NewContext();
                    VertexRegistry.EnsureAll(ctx);
                    if (w.CreateViewValue && source is not null)
                    {
                        open = PhaseViews.CreateView(s, f, source);
                        // Anota/hachura na vista nova o que estava anotado/hachurado na vista de origem.
                        var lots = s.Project.ActiveLots.ToList();
                        var ann = lots.Where(l => oldAnnotated.TryGetValue((l.Nome, l.Quadra), out var x) && x.ann).ToList();
                        var hat = ProjectContext.SortLots(lots.Where(l => oldAnnotated.TryGetValue((l.Nome, l.Quadra), out var x) && x.hat)).ToList();
                        ctx = s.NewContext();
                        if (ann.Count > 0) Annotator.Annotate(doc, open, ann, ctx, new ValueFormatter(s.Settings.Format), Annotator.Options.From(s.Settings.Annotation));
                        if (hat.Count > 0) Hatcher.Apply(doc, open, hat, l => hat.IndexOf(l));
                    }
                    if (source is not null) foreach (var uid in baseF.ViewIds) if (ElementUtil.ByUniqueId(doc, uid) is View bv) PhaseViews.Refresh(s, baseF, bv);
                    msg = (w.BaseId.Length == 0 ? $"{f.Label} criada vazia: desenhe, gere ou reconheça os lotes nela." : $"{f.Label} criada com {s.Project.ActiveLots.Count()} lote(s) da {baseF.Label}.") +
                          " A numeração dos vértices começa em P-01." +
                          (open is not null ? $"\nVista de trabalho: \"{open.Name}\"." : "");
                    break;
                }
                case FaseAction.Activate:
                    FaseService.Activate(s.Project, sel!.Id);
                    open = PhaseViews.ActivePhaseView(s);
                    if (open is not null) PhaseViews.Refresh(s, sel, open);
                    msg = $"{sel.Label} ativa." + (open is null ? "\nEsta fase não tem vista de trabalho: use 'Criar vista da fase'." : "");
                    break;
                case FaseAction.CreateView:
                {
                    FaseService.Activate(s.Project, sel!.Id);
                    var source = PlanSource(s, sel);
                    if (source is null) { t.RollBack(); Ui.Warn("Vista inadequada", "Abra uma vista de planta e tente de novo."); return Result.Cancelled; }
                    open = PhaseViews.CreateView(s, sel, source);
                    msg = $"Vista \"{open.Name}\" criada para a {sel.Label}.";
                    break;
                }
                case FaseAction.Refresh:
                    PhaseViews.RefreshAll(s);
                    msg = "Visibilidade das linhas atualizada nas vistas das fases.";
                    break;
                case FaseAction.Delete:
                {
                    var removed = FaseService.Delete(s.Project, sel!.Id);
                    foreach (var l in removed) CmdGerenciar.DeleteGeoPointElements(doc, l, deleteTableView: false);
                    bool views = sel.ViewIds.Count > 0 && Ui.Confirm("Vistas da fase", $"Excluir também as vistas de trabalho da {sel.Label}?", "Excluir as vistas", "Manter as vistas");
                    if (views)
                        foreach (var uid in sel.ViewIds)
                            if (ElementUtil.ByUniqueId(doc, uid) is View v && v.Id != s.ActiveView.Id) { try { doc.Delete(v.Id); } catch { } }
                    msg = $"{sel.Label} excluída ({removed.Count} lote(s)).";
                    break;
                }
                case FaseAction.Save:
                    msg = "";
                    break;
            }
            ProjectStore.Save(doc, s.Project);
            t.Commit();
        }
        if (open is not null && open.Id != s.ActiveView.Id)
        {
            try { s.UiDoc.ActiveView = open; } catch (Exception ex) { Logger.Error("Abrir vista da fase", ex); }
        }
        if (msg.Length > 0) Ui.Info("Fases", msg);
        return Result.Succeeded;
    }

    /// <summary>Vista de planta para copiar: a ativa (se for planta) ou a primeira vista da fase.</summary>
    private static View? PlanSource(GpSession s, FaseRecord f)
    {
        var v = s.ActiveView;
        if (ElementUtil.IsPlanLike(v) && v is not ViewSheet) return v;
        return f.ViewIds.Select(u => ElementUtil.ByUniqueId(s.Doc, u)).OfType<View>().FirstOrDefault(x => ElementUtil.IsPlanLike(x));
    }
}
