using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GeoPoint.Core.Calc;
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
        if (s.Project.ActiveLots.Count() < 3)
        {
            Ui.Info("Lotes insuficientes", "Para registrar um desmembramento é preciso ter o lote original e pelo menos dois lotes resultantes reconhecidos.\n\n" +
                                          "Dica: desenhe as novas divisas, use 'Reconhecer Lotes' para os lotes resultantes, ou use 'Dividir Lote' para simular e desenhar a divisão automaticamente.");
            return Result.Cancelled;
        }
        var w = new SubdivisionWindow(s.Project, merge: false, LotServices.LotsFromSelection(s));
        if (Ui.ShowDialog(w, s.MainWindow) != true || w.ParentLot is null) return Result.Cancelled;
        var parent = w.ParentLot;
        var children = w.SelectedLots;
        var op = new OperationRecord { Type = OperationType.Desmembramento, ParentIds = { parent.Id }, ChildIds = children.Select(c => c.Id).ToList(), Notes = w.NotesText };
        foreach (var c in children)
        {
            OpHelpers.InheritOwner(c, parent);
            c.History.Add(new HistoryEntry { Description = $"Resultante do desmembramento de {parent.DisplayName}", Area = c.ToRegion().Area });
        }
        if (w.ArchiveOriginals)
        {
            parent.Status = LotStatus.Arquivado;
            parent.History.Add(new HistoryEntry { Description = "Desmembrado (arquivado)", Area = parent.ToRegion().Area });
        }
        s.Project.Operations.Add(op);
        s.SaveProject("GeoPoint: registrar desmembramento");
        if (Ui.Confirm("Desmembramento registrado", $"{parent.DisplayName} → {children.Count} lote(s).\n\nDeseja gerar o memorial descritivo do desmembramento agora?", "Gerar memorial", "Agora não"))
            return CmdMemorial.Open(s, MemorialKind.Desmembramento, op);
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdRemembramento : CommandBase
{
    protected override string Title => "Remembramento";

    protected override Result Run(GpSession s)
    {
        if (s.Project.ActiveLots.Count() < 2) { Ui.Info("Lotes insuficientes", "É preciso ter pelo menos dois lotes ativos."); return Result.Cancelled; }
        var w = new SubdivisionWindow(s.Project, merge: true, LotServices.LotsFromSelection(s));
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
        foreach (var l in lots) OpHelpers.InheritOwner(result, l);
        result.EdgeInfos = LotBuilder.RemapEdgeInfos(lots.SelectMany(l => l.EdgeInfos).ToList(), result.Outer);
        result.History.Add(new HistoryEntry { Description = "Criado por remembramento de " + string.Join(", ", lots.Select(l => l.DisplayName)), Area = u.Area, Perimeter = result.ToRegion().Outer.Perimeter });
        if (w.ArchiveOriginals)
            foreach (var l in lots)
            {
                l.Status = LotStatus.Arquivado;
                l.History.Add(new HistoryEntry { Description = $"Remembrado em {result.DisplayName} (arquivado)", Area = l.ToRegion().Area });
            }
        var op = new OperationRecord { Type = OperationType.Remembramento, ParentIds = lots.Select(l => l.Id).ToList(), ChildIds = { result.Id }, Notes = w.NotesText };
        s.Project.Lots.Add(result);
        s.Project.Operations.Add(op);
        s.SaveProject("GeoPoint: registrar remembramento");
        if (Ui.Confirm("Remembramento registrado",
                $"{string.Join(", ", lots.Select(l => l.DisplayName))} → {result.DisplayName} ({Fmt.N(u.Area, 2)} m²).\n\n" +
                "As linhas das divisas internas continuam no desenho; apague-as se desejar.\n\nDeseja gerar o memorial descritivo do remembramento agora?", "Gerar memorial", "Agora não"))
            return CmdMemorial.Open(s, MemorialKind.Remembramento, op);
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdDividir : CommandBase
{
    protected override string Title => "Dividir lote";

    protected override Result Run(GpSession s)
    {
        var rows = OpHelpers.ActiveRows(s.Project);
        if (rows.Count == 0) { Ui.Info("Nenhum lote ativo", "Use 'Reconhecer Lotes' primeiro."); return Result.Cancelled; }
        var ctx = s.NewContext();
        var w = new SplitWindow(ctx, rows, LotServices.LotsFromSelection(s).FirstOrDefault());
        if (Ui.ShowDialog(w, s.MainWindow) != true || w.Lot is null || w.Result is null) return Result.Cancelled;
        var lot = w.Lot;
        var res = w.Result;
        var doc = s.Doc;
        var cuts = res.Cuts.Where(c => c.Length >= 0.001).ToList();

        using var tg = new TransactionGroup(doc, "GeoPoint: dividir lote");
        tg.Start();
        List<CurveElement> created;
        using (var t = new Transaction(doc, "GeoPoint: desenhar divisas"))
        {
            t.Start();
            var target = CurveTarget.ForLot(doc, s.ActiveView, lot);
            var style = RevitStyles.LineStyle(doc, RevitStyles.LineDivisaProposta, RevitStyles.Rgb(230, 90, 0), 3);
            created = target.Create(doc, cuts, style);
            t.Commit();
        }

        var children = new List<LotRecord>();
        if (w.CreateLotsValue && created.Count == cuts.Count)
        {
            var inputs = new List<InputCurve>();
            var srcs = new List<IEnumerable<string>>();
            foreach (var dto in lot.Outer.Concat(lot.Holes.SelectMany(h => h)))
            {
                inputs.Add(new InputCurve(dto.ToCurve(), srcs.Count));
                srcs.Add(dto.Src);
            }
            for (int i = 0; i < cuts.Count; i++)
            {
                inputs.Add(new InputCurve(cuts[i], srcs.Count));
                srcs.Add(new[] { created[i].UniqueId });
            }
            var net = PlanarNetwork.Build(inputs, LotServices.NetworkOptions(s.Settings));
            var parentRegion = lot.ToRegion();
            var faces = net.Faces.Where(f => parentRegion.Contains(f.Region.InteriorPoint())).ToList();
            // Ordena no sentido da divisão (da frente escolhida).
            var order = res.Positions.Count > 0 ? faces.OrderBy(f => PieceIndex(res, f.Region)).ToList() : faces;
            var numberer = new Numberer(w.StartNumberText);
            foreach (var f in order)
            {
                var (outer, holes) = LotBuilder.FromFace(net, f, t => srcs[t], false, false);
                string n = numberer.Next();
                var child = new LotRecord
                {
                    Nome = n, Numero = n, Quadra = w.QuadraText, Uso = LotUse.Lote,
                    Outer = outer, Holes = holes,
                    ZFeet = lot.ZFeet, SourceKind = lot.SourceKind, SourceViewId = lot.SourceViewId, SketchPlaneId = lot.SketchPlaneId,
                };
                OpHelpers.InheritOwner(child, lot);
                child.EdgeInfos = LotBuilder.RemapEdgeInfos(lot.EdgeInfos, outer);
                child.History.Add(new HistoryEntry { Description = $"Criado pela divisão de {lot.DisplayName}", Area = f.Region.Area, Perimeter = f.Region.Outer.Perimeter });
                children.Add(child);
            }
        }

        OperationRecord? op = null;
        using (var t = new Transaction(doc, "GeoPoint: gravar divisão"))
        {
            t.Start();
            s.Project.Lots.AddRange(children);
            if (w.RegisterValue && children.Count >= 2)
            {
                op = new OperationRecord { Type = OperationType.Desmembramento, ParentIds = { lot.Id }, ChildIds = children.Select(c => c.Id).ToList(), Notes = "Divisão simulada pelo GeoPoint" };
                s.Project.Operations.Add(op);
                lot.Status = LotStatus.Arquivado;
                lot.History.Add(new HistoryEntry { Description = "Dividido (arquivado)", Area = lot.ToRegion().Area });
            }
            ProjectStore.Save(doc, s.Project);
            t.Commit();
        }
        tg.Assimilate();

        string msg = $"{created.Count} divisa(s) desenhada(s)" + (children.Count > 0 ? $" e {children.Count} lote(s) criado(s)." : ".");
        if (op is not null && Ui.Confirm("Divisão concluída", msg + "\n\nDeseja gerar o memorial do desmembramento agora?", "Gerar memorial", "Agora não"))
            return CmdMemorial.Open(s, MemorialKind.Desmembramento, op);
        if (op is null) Ui.Info("Divisão concluída", msg);
        return Result.Succeeded;
    }

    private static int PieceIndex(Core.Operations.SplitResult res, Region r)
    {
        var p = r.InteriorPoint();
        for (int i = 0; i < res.PieceRegions.Count; i++)
            if (res.PieceRegions[i].Contains(p)) return i;
        return int.MaxValue;
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
