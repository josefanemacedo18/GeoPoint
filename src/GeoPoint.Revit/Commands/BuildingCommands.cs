using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Settings;
using GeoPoint.Core.Topology;
using GeoPoint.Core.Urban;
using GeoPoint.Revit.Infrastructure;
using GeoPoint.Revit.UI;

namespace GeoPoint.Revit.Commands;

/// <summary>Área edificada: edificações desenhadas com linhas dentro dos lotes, com área, taxa de ocupação e afastamentos.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdAreaEdificada : CommandBase
{
    protected override string Title => "Área edificada";
    protected override string? BackupReason => "area edificada";

    protected override Result Run(GpSession s)
    {
        if (!s.Project.ActiveLots.Any()) { Ui.Info("Nenhum lote ativo", "Use 'Reconhecer Lotes' primeiro."); return Result.Cancelled; }
        // Cópia para desfazer se a janela for cancelada.
        string snapshot = JsonSerializer.Serialize(s.Project.Buildings, JsonOptions.Default);
        var removed = new HashSet<string>();
        string? select = null;
        bool setbacks = true;
        bool startWithPick = !s.Project.PhaseBuildings.Any();
        while (true)
        {
            var ctx = s.NewContext();
            if (startWithPick)
            {
                startWithPick = false;
                select = AddFromLines(s) ?? select;
                if (!s.Project.PhaseBuildings.Any()) return Result.Cancelled;
                continue;
            }
            var w = new BuildingsWindow(ctx, s.Project.PhaseBuildings.ToList(), removed, select, setbacks);
            if (Ui.ShowDialog(w, s.MainWindow) != true || w.Action == BuildingsAction.Cancel)
            {
                s.Project.Buildings = JsonSerializer.Deserialize<List<BuildingRecord>>(snapshot, JsonOptions.Default) ?? new();
                return Result.Cancelled;
            }
            removed = new HashSet<string>(w.Removed);
            select = w.SelectedId;
            setbacks = w.DrawSetbacks;
            if (w.Action == BuildingsAction.Add) { select = AddFromLines(s) ?? select; continue; }
            if (w.Action == BuildingsAction.Update)
            {
                var b = s.Project.Buildings.FirstOrDefault(x => x.Id == select);
                if (b is not null) UpdateFromLines(s, b);
                continue;
            }
            break;
        }

        int drawn = 0;
        var view = s.ActiveView;
        bool canDraw = ElementUtil.SupportsDetails(view) && view is not ViewSheet && ElementUtil.IsPlanLike(view);
        using (var t = new Transaction(s.Doc, "GeoPoint: área edificada"))
        {
            t.Start();
            foreach (var id in removed)
            {
                var b = s.Project.Buildings.FirstOrDefault(x => x.Id == id);
                if (b is null) continue;
                var ids = ElementUtil.ExistingIds(s.Doc, b.Drawings.SelectMany(d => d.ElementIds));
                if (ids.Count > 0) s.Doc.Delete(ids);
                s.Project.Buildings.Remove(b);
            }
            if (canDraw)
            {
                var ctx = s.NewContext();
                var reps = Reports(s, ctx);
                drawn = BuildingDrawer.Draw(s.Doc, view, reps, new ValueFormatter(s.Settings.Format), s.Settings.Annotation.TextSizeMm, s.Settings.Annotation.Font, setbacks);
            }
            ProjectStore.Save(s.Doc, s.Project);
            t.Commit();
        }
        Ui.Info("Área edificada", $"{s.Project.PhaseBuildings.Count()} edificação(ões) na fase.\n" +
            (canDraw ? $"{drawn} desenhada(s) na vista \"{view.Name}\"." : "Abra uma planta para desenhar as edificações (hachura, rótulo e afastamentos).") +
            "\n\nAs edificações também aparecem no memorial (área edificada e distância até as divisas) e nas pranchas.");
        return Result.Succeeded;
    }

    internal static List<BuildingReport> Reports(GpSession s, ProjectContext ctx)
    {
        var res = new List<BuildingReport>();
        foreach (var b in s.Project.PhaseBuildings)
        {
            var lot = BuildingAnalyzer.OwnerLot(s.Project, b);
            if (lot is null) continue;
            try { res.Add(BuildingAnalyzer.Analyze(ctx.Report(lot), b)); } catch (Exception ex) { Logger.Error("Edificação " + b.Nome, ex); }
        }
        return res;
    }

    /// <summary>Selecionar as linhas do contorno das edificações e criar os registros. Devolve o id da última criada.</summary>
    private static string? AddFromLines(GpSession s)
    {
        IList<Reference> refs;
        try
        {
            refs = s.UiDoc.Selection.PickObjects(ObjectType.Element, new LineAndCadFilter(false),
                "GeoPoint: selecione as linhas do contorno da(s) edificação(ões) e clique em Concluir");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return null; }
        var elements = refs.Select(r => s.Doc.GetElement(r)).Where(e => e is not null).ToList();
        if (elements.Count == 0) return null;
        RecognitionResult rec;
        try { rec = LotServices.Recognize(s, elements, null); }
        catch (Exception ex) { Ui.Warn("Área edificada", ex.Message); return null; }
        var lots = s.Project.ActiveLots.ToList();
        string? last = null;
        int created = 0, outside = 0;
        foreach (var face in rec.Network.Faces)
        {
            if (rec.Existing.ContainsKey(face.Id)) continue;   // é um lote, não uma edificação
            var (outer, _) = LotBuilder.FromFace(rec.Network, face, rec.SourcesOfTag, false, true);
            var b = new BuildingRecord { FaseId = s.Project.Fase.Id, Outer = outer, SourceIds = outer.SelectMany(e => e.Src).Distinct().ToList() };
            Vec2 c;
            try { c = b.ToRegion().InteriorPoint(); } catch { continue; }
            var lot = lots.FirstOrDefault(l => { try { return l.ToRegion().Contains(c); } catch { return false; } });
            if (lot is null) { outside++; continue; }
            // Mesmo contorno já cadastrado: atualiza em vez de duplicar.
            var same = s.Project.PhaseBuildings.FirstOrDefault(x => LotBuilder.SameShape(x.ToRegion(), b.ToRegion()));
            if (same is not null) { last = same.Id; continue; }
            b.LotId = lot.Id;
            b.Nome = $"Edificação {BuildingAnalyzer.Of(s.Project, lot).Count + 1}";
            s.Project.Buildings.Add(b);
            last = b.Id;
            created++;
        }
        if (created == 0)
            Ui.Warn("Área edificada", outside > 0
                ? "O contorno selecionado não fica dentro de nenhum lote ativo."
                : "Nenhuma área fechada nova: confira se as linhas da edificação se encontram nos cantos." + (rec.Warnings.Count > 0 ? "\n\n" + string.Join("\n", rec.Warnings.Take(5)) : ""));
        return last;
    }

    private static void UpdateFromLines(GpSession s, BuildingRecord b)
    {
        var elements = b.SourceIds.Select(id => ElementUtil.ByUniqueId(s.Doc, id)).Where(e => e is not null).Select(e => e!).ToList();
        if (elements.Count == 0) { Ui.Warn("Área edificada", "As linhas desta edificação não existem mais. Remova-a e adicione de novo pelas linhas atuais."); return; }
        try
        {
            var rec = LotServices.Recognize(s, elements, null);
            var face = rec.Network.Faces.OrderByDescending(f => Math.Abs(f.Region.Area)).FirstOrDefault();
            if (face is null) { Ui.Warn("Área edificada", "As linhas não formam mais uma área fechada."); return; }
            var (outer, _) = LotBuilder.FromFace(rec.Network, face, rec.SourcesOfTag, false, true);
            b.Outer = outer;
        }
        catch (Exception ex) { Ui.Warn("Área edificada", ex.Message); }
    }
}
