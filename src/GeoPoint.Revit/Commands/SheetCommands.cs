using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Sheets;
using GeoPoint.Revit.Infrastructure;
using GeoPoint.Revit.UI;

namespace GeoPoint.Revit.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class CmdPranchas : CommandBase
{
    protected override string Title => "Pranchas A4";

    protected override Result Run(GpSession s)
    {
        if (!s.Project.Lots.Any()) { Ui.Info("Nenhum lote cadastrado", "Use 'Reconhecer Lotes' primeiro."); return Result.Cancelled; }
        return GenerateForOperation(s, s.Project.Operations.OrderByDescending(o => o.Date).FirstOrDefault(), ask: true) ? Result.Succeeded : Result.Cancelled;
    }

    /// <summary>Abre a janela de pranchas (pré-selecionando a operação) e gera as pranchas.</summary>
    internal static bool GenerateForOperation(GpSession s, OperationRecord? op, bool ask)
    {
        var source = SourceView(s);
        if (source is null)
        {
            Ui.Warn("Vista inadequada", "Abra a vista de planta onde os lotes estão desenhados e use 'Pranchas A4'.");
            return false;
        }
        var rows = PickerRows.Build(s, false, l => l.Status == LotStatus.Ativo || s.Project.Operations.Any(o => o.ParentIds.Contains(l.Id) || o.ChildIds.Contains(l.Id)));
        var w = new PranchasWindow(s.Project, s.Settings.Sheets, rows, op);
        if (Ui.ShowDialog(w, s.MainWindow) != true) return false;
        w.Apply(s.Settings.Sheets, s.Project.Info);
        try { s.SaveSettings(); } catch { }
        var st = s.Settings.Sheets;
        var specs = w.Specs;
        var chosenOp = w.Operation;

        // Pranchas anteriores da mesma operação.
        var old = chosenOp?.GeneratedIds.Select(u => ElementUtil.ByUniqueId(s.Doc, u)).Where(e => e is not null).Select(e => e!).ToList() ?? new List<Element>();
        bool replace = old.Count > 0 && Ui.Confirm("Pranchas existentes",
            $"Esta operação já tem {old.OfType<ViewSheet>().Count()} prancha(s) gerada(s) pelo GeoPoint. Substituir (apagar as anteriores e as vistas copiadas)?", "Substituir", "Manter e criar novas");

        var builder = new SheetBuilder(s, source, new SheetBuildOptions
        {
            Orientation = st.Orientation == 1 ? SheetOrientation.Paisagem : SheetOrientation.Retrato,
            Scale = st.AutoScale ? 0 : st.Scale,
            Anotacoes = st.Anotacoes, Vias = st.Vias, NorteParaCima = st.NorteParaCima,
            LocationImage = s.Project.Info.ImagemLocalizacao, NumberPrefix = st.NumberPrefix, Font = s.Settings.Annotation.Font,
        });
        List<ViewSheet> sheets;
        using (var t = new Transaction(s.Doc, "GeoPoint: pranchas A4"))
        {
            t.Start();
            if (replace)
            {
                foreach (var e in old.OfType<ViewSheet>().Cast<Element>().Concat(old.Where(e => e is not ViewSheet)))
                {
                    try { if (e.IsValidObject) s.Doc.Delete(e.Id); } catch { }
                }
                chosenOp!.GeneratedIds.Clear();
            }
            sheets = builder.Build(specs);
            if (chosenOp is not null) chosenOp.GeneratedIds.AddRange(builder.Created);
            ProjectStore.Save(s.Doc, s.Project);
            t.Commit();
        }
        if (sheets.Count > 0)
        {
            try { s.UiDoc.ActiveView = sheets[0]; } catch { }
        }
        string msg = $"{sheets.Count} prancha(s) criada(s): {string.Join(", ", sheets.Select(x => x.SheetNumber))}.";
        if (builder.Warnings.Count > 0) Ui.Warn("Pranchas geradas com avisos", msg + "\n\n" + string.Join("\n", builder.Warnings.Distinct().Take(20)));
        else Ui.Info("Pranchas geradas", msg + "\n\nPara imprimir/exportar em PDF use o recurso nativo do Revit (Arquivo › Exportar › PDF), em folha A4.");
        return sheets.Count > 0;
    }

    /// <summary>Vista de planta com os lotes: a vista ativa ou, se for prancha, a primeira planta usada pelos lotes.</summary>
    internal static View? SourceView(GpSession s)
    {
        var v = s.ActiveView;
        if (ElementUtil.IsPlanLike(v) && v is not ViewSheet) return v;
        foreach (var lot in s.Project.Lots)
            if (ElementUtil.ByUniqueId(s.Doc, lot.SourceViewId) is View sv && ElementUtil.IsPlanLike(sv)) return sv;
        return null;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdHachuras : CommandBase
{
    protected override string Title => "Hachuras";

    protected override Result Run(GpSession s)
    {
        var view = s.ActiveView;
        if (!ElementUtil.SupportsDetails(view) || view is ViewSheet) { Ui.Warn("Vista inadequada", "Abra uma vista de planta para hachurar os lotes."); return Result.Cancelled; }
        if (!s.Project.Lots.Any()) { Ui.Info("Nenhum lote cadastrado", "Use 'Reconhecer Lotes' primeiro."); return Result.Cancelled; }
        var opts = new List<OptionItem>
        {
            new("clear", "Somente remover as hachuras do GeoPoint dos lotes marcados", false),
            new("space", "Espaçamento da hachura no papel (mm)", 1.6),
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
                n = Hatcher.Apply(s.Doc, view, order, l => order.IndexOf(l), Math.Clamp(w.Number("space"), 0.5, 10));
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

    protected override Result Run(GpSession s)
    {
        if (!s.Project.ActiveLots.Any()) { Ui.Info("Nenhum lote ativo", "Use 'Reconhecer Lotes' primeiro."); return Result.Cancelled; }
        var ctx = s.NewContext();
        var w = new ViasWindow(s.Project, ctx, s.Settings.Streets);
        if (Ui.ShowDialog(w, s.MainWindow) != true) return Result.Cancelled;
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
