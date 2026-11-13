using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GeoPoint.Core.Calc;
using Autodesk.Revit.UI.Selection;
using GeoPoint.Core.Operations;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Topology;
using GeoPoint.Revit.Infrastructure;
using GeoPoint.Revit.UI;

namespace GeoPoint.Revit.Commands;

internal static class OpHelpers
{
    public static List<LotRow> ActiveRows(ParcelProject p) =>
        ProjectContext.SortLots(p.ActiveLots).Select(l =>
        {
            double a = 0;
            try { a = l.ToRegion().Area; } catch { }
            return new LotRow(l, a);
        }).ToList();

    /// <summary>Anota e hachura os lotes novos nas vistas em que os lotes originais estavam anotados/hachurados. Dentro de transação.</summary>
    public static void Redraw(GpSession s, ProjectContext ctx, IEnumerable<string> annotViews, IEnumerable<string> hatchViews, List<LotRecord> lots)
    {
        if (lots.Count == 0) return;
        var fmt = new ValueFormatter(s.Settings.Format);
        foreach (var uid in annotViews.Distinct())
        {
            if (ElementUtil.ByUniqueId(s.Doc, uid) is not View v || v is ViewSheet) continue;
            try { Annotator.Annotate(s.Doc, v, lots, ctx, fmt, Annotator.Options.From(s.Settings.Annotation)); }
            catch (Exception ex) { Logger.Error("Anotar lotes novos", ex); }
        }
        var order = ProjectContext.SortLots(lots).ToList();
        foreach (var uid in hatchViews.Distinct())
        {
            if (ElementUtil.ByUniqueId(s.Doc, uid) is not View v || v is ViewSheet) continue;
            try { Hatcher.Apply(s.Doc, v, order, l => order.IndexOf(l)); }
            catch (Exception ex) { Logger.Error("Hachurar lotes novos", ex); }
        }
    }

    public static void InheritOwner(LotRecord target, LotRecord from)
    {
        if (string.IsNullOrWhiteSpace(target.Proprietario)) target.Proprietario = from.Proprietario;
        if (string.IsNullOrWhiteSpace(target.Documento)) target.Documento = from.Documento;
        if (string.IsNullOrWhiteSpace(target.Endereco)) target.Endereco = from.Endereco;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdDesmembramento : CommandBase
{
    protected override string Title => "Desmembramento";

    protected override Result Run(GpSession s)
    {
        if (!s.Project.ActiveLots.Any()) { Ui.Info("Nenhum lote ativo", "Use 'Reconhecer Lotes' primeiro."); return Result.Cancelled; }
        if (!NorthGuard.Ensure(s)) return Result.Cancelled;
        var state = new DesmembramentoState { ParentId = LotServices.LotsFromSelection(s).FirstOrDefault() };
        return Flow(s, state);
    }

    /// <summary>Janela do desmembramento e registro (também chamada ao concluir o desenho com a ferramenta Linha).</summary>
    internal static Result Flow(GpSession s, DesmembramentoState state)
    {
        DesmembramentoWindow w;
        while (true)
        {
            var ctx = s.NewContext();
            w = new DesmembramentoWindow(s.Project, ctx, state);
            if (Ui.ShowDialog(w, s.MainWindow) != true || w.Action == DesmAction.Cancel) return Result.Cancelled;
            if (w.Action == DesmAction.DrawLines)
            {
                // Desenho ao vivo: a prévia acompanha cada linha; ao concluir, esta janela volta com as divisas.
                if (w.ParentLot is not { } parentLot) continue;
                var view = s.ActiveView;
                if (!ElementUtil.IsPlanLike(view) || view is ViewSheet)
                {
                    Ui.Warn("Vista inadequada", "Abra a planta onde o lote está desenhado para desenhar as divisas.");
                    continue;
                }
                LiveSplitSession.Start(s, parentLot, state, Flow);
                return Result.Succeeded;
            }
            if (w.Action != DesmAction.PickLines) break;
            // Seleção das divisas desenhadas pelo usuário.
            try
            {
                var refs = s.UiDoc.Selection.PickObjects(ObjectType.Element, new LineAndCadFilter(false),
                    "GeoPoint: selecione as linhas das novas divisas e clique em Concluir");
                var els = refs.Select(r => s.Doc.GetElement(r)).Where(e => e is not null).Select(e => e!).ToList();
                var warnings = new List<string>();
                var curves = GeometryReader.Read(s.Doc, s.ActiveView, els, null, warnings);
                state.LineCuts = curves.Select(c => (c.Curve, c.UniqueId)).ToList();
                state.Modo = DesmModo.Linhas;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
        }
        var op = Register(s, state, w);
        if (op is null) return Result.Failed;
        if (state.Sheets) CmdPranchas.GenerateForOperation(s, op, ask: true);
        if (Ui.Confirm("Desmembramento registrado", "Deseja gerar o memorial descritivo do desmembramento agora?", "Gerar memorial", "Agora não"))
            return CmdMemorial.Open(s, MemorialKind.Desmembramento, op);
        return Result.Succeeded;
    }

    /// <summary>Cria os lotes resultantes, aplica remanescente, proprietários, confrontantes e anexações.</summary>
    internal static OperationRecord? Register(GpSession s, DesmembramentoState st, DesmembramentoWindow w)
    {
        var doc = s.Doc;
        var parent = w.ParentLot!;
        var rows = w.Rows;
        using var tg = new TransactionGroup(doc, "GeoPoint: desmembramento");
        tg.Start();

        // 1) Geometria das partes (com origem rastreada).
        List<PlannedPiece> pieces;
        List<LotRecord?> existing = rows.Select(r => r.ExistingLot).ToList();
        if (w.Modo == DesmModo.Automatico && w.AutoResult is { } res)
        {
            var cuts = res.Cuts.Where(c => c.Length >= 0.001).ToList();
            List<CurveElement> created;
            using (var t = new Transaction(doc, "GeoPoint: desenhar divisas"))
            {
                t.Start();
                var target = CurveTarget.ForLot(doc, s.ActiveView, parent);
                created = target.Create(doc, cuts, null);
                t.Commit();
            }
            if (created.Count != cuts.Count) { tg.RollBack(); Ui.Error("Desmembramento", "Não foi possível desenhar todas as divisas."); return null; }
            var tagged = cuts.Select((c, i) => ((Curve2)c, (string?)created[i].UniqueId)).ToList();
            var fresh = SubdivisionPlanner.FromCuts(parent, tagged, LotServices.NetworkOptions(s.Settings).Tolerance);
            // Mesma ordem da prévia: casa cada parte pela posição.
            pieces = rows.Select(r => fresh.FirstOrDefault(f => f.Region.Contains(r.Piece.Region.InteriorPoint())) ?? r.Piece).ToList();
        }
        else pieces = rows.Select(r => r.Piece).ToList();

        // 2) Lotes resultantes.
        var children = new List<LotRecord>();
        string parentOwner = string.IsNullOrWhiteSpace(parent.Proprietario) ? s.Project.Info.Proprietario : parent.Proprietario;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var lot = existing[i] ?? new LotRecord
            {
                Outer = pieces[i].Outer, Holes = pieces[i].Holes,
                ZFeet = parent.ZFeet, SourceKind = parent.SourceKind, SourceViewId = parent.SourceViewId, SketchPlaneId = parent.SketchPlaneId,
                Uso = LotUse.Lote, Created = DateTime.Now.AddMilliseconds(i),
            };
            lot.Nome = row.Nome.Trim();
            lot.Numero = lot.Nome;
            lot.Quadra = string.IsNullOrWhiteSpace(st.Quadra) ? parent.Quadra : st.Quadra!;
            lot.IsRemanescente = row.Remanescente;
            lot.Proprietario = row.Proprietario.Trim();
            if (lot.Proprietario == parentOwner || string.IsNullOrWhiteSpace(lot.Proprietario))
            {
                lot.Proprietario = parent.Proprietario;
                if (string.IsNullOrWhiteSpace(lot.Documento)) lot.Documento = parent.Documento;
            }
            if (string.IsNullOrWhiteSpace(lot.Endereco)) lot.Endereco = parent.Endereco;
            // Vértices inseridos no original que caem sobre o novo lote.
            var extras = parent.ExtraVertices.Where(p => lot.Outer.Any(e => e.ToCurve().DistanceTo(p.ToVec()) <= EdgeSplitter.Tolerance)).ToList();
            foreach (var x in extras) if (!lot.ExtraVertices.Any(v => v.ToVec().DistanceTo(x.ToVec()) < 1e-6)) lot.ExtraVertices.Add(x);
            EdgeSplitter.Apply(lot);
            lot.History.Add(new HistoryEntry { Description = $"Resultante do desmembramento de {parent.DisplayName}" + (lot.IsRemanescente ? " (remanescente)" : ""), Area = lot.ToRegion().Area });
            children.Add(lot);
        }
        var ctx = s.NewContext();
        foreach (var c in children) c.EdgeInfos = ConfrontanteInheritance.Inherit(new[] { parent }, c, children, ctx);

        var op = new OperationRecord
        {
            Type = OperationType.Desmembramento, ParentIds = { parent.Id }, ChildIds = children.Select(c => c.Id).ToList(), Notes = st.Notes,
            RemanescenteId = children.FirstOrDefault(c => c.IsRemanescente)?.Id ?? "",
        };

        // 3) Anexações (remembramento automático da parte com o lote escolhido).
        var merges = new List<(LotRecord piece, LotRecord target, LotRecord result, OperationRecord op)>();
        for (int i = 0; i < rows.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(rows[i].AnexarA)) continue;
            var target = s.Project.FindLot(rows[i].AnexarA);
            if (target is null) continue;
            var piece = children[i];
            var u = ParcelOperations.Union(new[] { piece, target });
            if (!u.Ok) { Ui.Warn("Anexação não realizada", $"{piece.DisplayName} não pôde ser anexada a {target.DisplayName}: {string.Join(" ", u.Errors)}"); continue; }
            var result = new LotRecord
            {
                Nome = target.Nome, Numero = target.Numero, Quadra = target.Quadra, Uso = target.Uso,
                Proprietario = target.Proprietario, Documento = target.Documento, Endereco = target.Endereco, Matricula = target.Matricula,
                Outer = u.Outer, Holes = u.Holes,
                ZFeet = target.ZFeet, SourceKind = target.SourceKind, SourceViewId = target.SourceViewId, SketchPlaneId = target.SketchPlaneId,
                Created = DateTime.Now.AddSeconds(1),
            };
            result.EdgeInfos = ConfrontanteInheritance.Inherit(new[] { piece, target }, result, Array.Empty<LotRecord>(), ctx);
            CollinearMerge.Apply(result, ctx);
            result.History.Add(new HistoryEntry { Description = $"Remembramento de {target.DisplayName} com parte de {parent.DisplayName}", Area = u.Area });
            var mop = new OperationRecord { Type = OperationType.Remembramento, ParentIds = { piece.Id, target.Id }, ChildIds = { result.Id }, Notes = $"Anexação de parte do desmembramento de {parent.DisplayName}" };
            merges.Add((piece, target, result, mop));
        }

        using (var t = new Transaction(doc, "GeoPoint: registrar desmembramento"))
        {
            t.Start();
            foreach (var c in children) if (!s.Project.Lots.Contains(c)) s.Project.Lots.Add(c);
            parent.Status = LotStatus.Arquivado;
            parent.History.Add(new HistoryEntry { Description = "Desmembrado (arquivado)", Area = parent.ToRegion().Area });
            // As anotações do original saem da planta (a situação atual fica nas pranchas, em cópias da vista);
            // os lotes novos são anotados/hachurados nas mesmas vistas.
            var annotViews = parent.Annotations.Select(a => a.ViewId).Concat(merges.SelectMany(m => m.target.Annotations.Select(a => a.ViewId))).Distinct().ToList();
            var hatchViews = parent.Hatches.Select(a => a.ViewId).Distinct().ToList();
            Annotator.ClearEverywhere(doc, parent);
            s.Project.Operations.Add(op);
            var archived = new List<string> { parent.Id };
            var newLots = new List<LotRecord>(children);
            foreach (var (piece, target, result, mop) in merges)
            {
                piece.Status = LotStatus.Arquivado;
                target.Status = LotStatus.Arquivado;
                Annotator.ClearEverywhere(doc, piece);
                Annotator.ClearEverywhere(doc, target);
                piece.History.Add(new HistoryEntry { Description = $"Anexado a {target.DisplayName}", Area = piece.ToRegion().Area });
                target.History.Add(new HistoryEntry { Description = "Remembrado (arquivado)", Area = target.ToRegion().Area });
                s.Project.Lots.Add(result);
                s.Project.Operations.Add(mop);
                archived.Add(piece.Id); archived.Add(target.Id);
                newLots.Add(result);
            }
            ConfrontanteInheritance.Retarget(s.Project, archived, newLots.Where(l => l.Status == LotStatus.Ativo).ToList());
            var ctx2 = s.NewContext();
            VertexRegistry.Register(ctx2, children.Concat(merges.Select(m => m.result)));
            ctx2.Invalidate();
            var shown = newLots.Where(l => l.Status == LotStatus.Ativo).ToList();
            OpHelpers.Redraw(s, ctx2, annotViews, hatchViews, shown);
            PhaseViews.RefreshAll(s);
            ProjectStore.Save(doc, s.Project);
            t.Commit();
        }
        tg.Assimilate();
        string msg = $"{parent.DisplayName} → {children.Count} lote(s)" + (op.RemanescenteId.Length > 0 ? $"; remanescente: {s.Project.FindLot(op.RemanescenteId)?.DisplayName}" : "") +
                     (merges.Count > 0 ? $"; {merges.Count} parte(s) anexada(s) a lote(s) vizinho(s)." : ".");
        Logger.Info("Desmembramento: " + msg);
        return op;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdRemembramento : CommandBase
{
    protected override string Title => "Remembramento";

    protected override Result Run(GpSession s)
    {
        if (s.Project.ActiveLots.Count() < 2) { Ui.Info("Lotes insuficientes", "É preciso ter pelo menos dois lotes ativos."); return Result.Cancelled; }
        var ctx = s.NewContext();
        var w = new SubdivisionWindow(s.Project, merge: true, LotServices.LotsFromSelection(s), ctx.Coords.Rotation);
        if (Ui.ShowDialog(w, s.MainWindow) != true || w.Union is null || !w.Union.Ok) return Result.Cancelled;
        var lots = w.SelectedLots;
        var u = w.Union;
        var first = lots[0];
        var result = new LotRecord
        {
            Nome = w.ResultNameText, Numero = w.ResultNameText, Quadra = w.ResultQuadraText, Uso = w.ResultUseValue, Matricula = w.ResultMatriculaText,
            Outer = u.Outer, Holes = u.Holes,
            ZFeet = first.ZFeet, SourceKind = first.SourceKind, SourceViewId = first.SourceViewId, SketchPlaneId = first.SketchPlaneId,
        };
        if (w.ResultOwnerText.Length > 0) result.Proprietario = w.ResultOwnerText;
        foreach (var l in lots) OpHelpers.InheritOwner(result, l);
        result.EdgeInfos = ConfrontanteInheritance.Inherit(lots, result, Array.Empty<LotRecord>(), ctx);
        // O lote unido tem os seus próprios vértices: os pontos dos lotes antigos no meio das divisas saem.
        CollinearMerge.Apply(result, ctx);
        result.History.Add(new HistoryEntry { Description = "Criado por remembramento de " + string.Join(", ", lots.Select(l => l.DisplayName)), Area = u.Area, Perimeter = result.ToRegion().Outer.Perimeter });
        foreach (var l in lots)
        {
            l.Status = LotStatus.Arquivado;
            l.History.Add(new HistoryEntry { Description = $"Remembrado em {result.DisplayName} (arquivado)", Area = l.ToRegion().Area });
        }
        var op = new OperationRecord { Type = OperationType.Remembramento, ParentIds = lots.Select(l => l.Id).ToList(), ChildIds = { result.Id }, Notes = w.NotesText };
        s.Project.Lots.Add(result);
        s.Project.Operations.Add(op);
        ConfrontanteInheritance.Retarget(s.Project, lots.Select(l => l.Id).ToList(), new[] { result });
        VertexRegistry.Register(s.NewContext(), new[] { result });
        using (var t = new Transaction(s.Doc, "GeoPoint: registrar remembramento"))
        {
            t.Start();
            var annotViews = lots.SelectMany(l => l.Annotations.Select(a => a.ViewId)).Distinct().ToList();
            var hatchViews = lots.SelectMany(l => l.Hatches.Select(a => a.ViewId)).Distinct().ToList();
            foreach (var l in lots) Annotator.ClearEverywhere(s.Doc, l);
            OpHelpers.Redraw(s, s.NewContext(), annotViews, hatchViews, new List<LotRecord> { result });
            PhaseViews.RefreshAll(s);
            ProjectStore.Save(s.Doc, s.Project);
            t.Commit();
        }
        if (w.SheetsValue) CmdPranchas.GenerateForOperation(s, op, ask: true);
        if (Ui.Confirm("Remembramento registrado",
                $"{string.Join(", ", lots.Select(l => l.DisplayName))} → {result.DisplayName} ({Fmt.N(u.Area, 2)} m²).\n\n" +
                "As linhas das divisas internas continuam no desenho (nas pranchas da proposta elas ficam ocultas).\n\nDeseja gerar o memorial descritivo do remembramento agora?", "Gerar memorial", "Agora não"))
            return CmdMemorial.Open(s, MemorialKind.Remembramento, op);
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdAreaEdificavel : CommandBase
{
    protected override string Title => "Área edificável";

    protected override Result Run(GpSession s)
    {
        var rows = OpHelpers.ActiveRows(s.Project).Where(r => !LotUseInfo.IsDeduction(r.Lot.Uso)).ToList();
        if (rows.Count == 0) { Ui.Info("Nenhum lote ativo", "Use 'Reconhecer Lotes' primeiro."); return Result.Cancelled; }
        var ctx = s.NewContext();
        var w = new SetbackWindow(ctx, rows, LotServices.LotsFromSelection(s).FirstOrDefault());
        if (Ui.ShowDialog(w, s.MainWindow) != true || w.Lot is null || w.ResultData is null) return Result.Cancelled;
        var lot = w.Lot;
        foreach (var (mid, recuo) in w.Recuos)
        {
            var info = ProjectContext.FindInfo(lot, mid);
            if (recuo is double r && r > 0)
            {
                if (info is null) { info = new EdgeInfo { MX = mid.X, MY = mid.Y }; lot.EdgeInfos.Add(info); }
                info.Recuo = r;
            }
            else if (info is not null) info.Recuo = null;
        }
        lot.EdgeInfos.RemoveAll(e => string.IsNullOrWhiteSpace(e.Confrontante) && !e.Testada && e.Recuo is null);
        lot.AreaEdificavel = w.ResultData.Area;

        using var t = new Transaction(s.Doc, "GeoPoint: área edificável");
        t.Start();
        var old = ElementUtil.ExistingIds(s.Doc, lot.AuxElementIds);
        if (old.Count > 0) s.Doc.Delete(old);
        lot.AuxElementIds.Clear();
        if (w.DrawOutline)
        {
            var target = CurveTarget.ForLot(s.Doc, s.ActiveView, lot);
            var style = RevitStyles.LineStyle(s.Doc, RevitStyles.LineAreaEdificavel, RevitStyles.Rgb(0, 150, 60), 3);
            var curves = new List<Curve2>();
            foreach (var ring in w.ResultData.Rings)
                for (int i = 0; i < ring.Count; i++)
                {
                    var a = ring[i]; var b = ring[(i + 1) % ring.Count];
                    if (a.DistanceTo(b) >= 0.002) curves.Add(new LineCurve(a, b));
                }
            var created = target.Create(s.Doc, curves, style);
            lot.AuxElementIds.AddRange(created.Select(c => c.UniqueId));
        }
        ProjectStore.Save(s.Doc, s.Project);
        t.Commit();
        Ui.Info("Área edificável", $"{lot.DisplayName}: {Fmt.N(w.ResultData.Area, 2)} m² de área edificável.");
        return Result.Succeeded;
    }
}
