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
            new("conf", "Confrontantes: nome e extensão (cota vermelha com marcas tracejadas)", a.Confrontantes),
            new("area", "Nome e área no centro do lote", a.AreaLabel),
            new("opaque", "Fundo opaco nos textos (legíveis sobre hachuras)", a.OpaqueText),
            new("size", "Altura do texto no papel (mm)", a.TextSizeMm),
            new("offset", "Afastamento das linhas no papel (mm)", a.OffsetMm),
            new("dot", "Diâmetro do ponto no papel (mm)", a.DotMm),
            new("clear", "Somente remover as anotações do GeoPoint dos lotes marcados nesta vista", false),
        };
        var w = new LotPickerWindow("Anotar lotes na vista atual", $"Vista: {view.Name} (escala 1:{view.Scale}). Anotações anteriores do GeoPoint nesta vista são substituídas. Divisas e vértices compartilhados entre os lotes marcados são anotados uma vez só.",
            PickerRows.Build(s, true), opts, "Anotar");
        if (Ui.ShowDialog(w, s.MainWindow) != true) return Result.Cancelled;
        a.Edges = w.Option("edges"); a.EdgeAzimuth = w.Option("angle"); a.TextOutside = w.Option("out"); a.Vertices = w.Option("vert");
        a.VertexDots = w.Option("dots"); a.Confrontantes = w.Option("conf"); a.AreaLabel = w.Option("area"); a.OpaqueText = w.Option("opaque");
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
            else total = Annotator.Annotate(s.Doc, view, w.SelectedLots, ctx, fmt, o, failed);
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
public sealed class CmdElementosNativos : CommandBase
{
    protected override string Title => "Elementos nativos";

    protected override Result Run(GpSession s)
    {
        if (!s.Project.ActiveLots.Any()) { Ui.Info("Nenhum lote ativo", "Use 'Reconhecer Lotes' primeiro."); return Result.Cancelled; }
        bool viewOk = ElementUtil.IsPlanLike(s.ActiveView);
        var opts = new List<OptionItem>
        {
            new("pl", "Linha de propriedade (Property Line) nativa – área calculada pelo Revit, etiquetas e tabelas nativas", true),
            new("fr", "Região preenchida colorida por uso na vista atual" + (viewOk ? "" : " (abra uma planta)"), false),
            new("par", "Preencher parâmetros GP_* (lote, quadra, uso, área, perímetro…) para tabelas do Revit", true),
        };
        var w = new LotPickerWindow("Elementos nativos do Revit", "Cria elementos do próprio Revit a partir dos lotes, para usar etiquetas, tabelas e filtros nativos. A área calculada pelo Revit é comparada com a do GeoPoint.",
            PickerRows.Build(s, true), opts, "Criar / atualizar");
        if (Ui.ShowDialog(w, s.MainWindow) != true) return Result.Cancelled;
        bool pl = w.Option("pl"), fr = w.Option("fr") && viewOk, par = w.Option("par");
        if (!pl && !fr) { Ui.Info("Nada a fazer", "Marque Property Line e/ou Região preenchida."); return Result.Cancelled; }

        var ctx = s.NewContext();
        var rows = new List<ReportRow>();
        using (var t = new Transaction(s.Doc, "GeoPoint: elementos nativos"))
        {
            t.Start();
            if (par)
            {
                var cats = new List<BuiltInCategory>();
                if (pl) cats.Add(BuiltInCategory.OST_SiteProperty);
                if (fr) cats.Add(BuiltInCategory.OST_DetailComponents);
                foreach (var warn in NativeElements.EnsureParameters(s.Doc, cats)) rows.Add(new ReportRow { Nivel = "Aviso", Categoria = "Parâmetros", Mensagem = warn });
            }
            foreach (var lot in w.SelectedLots)
            {
                LotReport rep;
                try { rep = ctx.Report(lot); }
                catch (Exception ex) { rows.Add(new ReportRow { Nivel = "Erro", Lote = lot.DisplayName, Mensagem = ex.Message }); continue; }
                if (pl)
                {
                    try
                    {
                        var area = NativeElements.UpsertPropertyLine(s.Doc, lot, rep.Region, out var el);
                        if (par && el is not null) NativeElements.SetParameters(el, lot, rep);
                        rows.Add(Compare(lot, "Property Line", rep.AreaLocal, area));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Property Line " + lot.DisplayName, ex);
                        rows.Add(new ReportRow { Nivel = "Erro", Categoria = "Property Line", Lote = lot.DisplayName, Mensagem = ex.Message, LotId = lot.Id });
                    }
                }
                if (fr)
                {
                    try
                    {
                        var area = NativeElements.CreateFilledRegion(s.Doc, s.ActiveView, lot, rep.Region, out var el);
                        if (par) NativeElements.SetParameters(el, lot, rep);
                        rows.Add(Compare(lot, "Região preenchida", rep.AreaLocal, area));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Região " + lot.DisplayName, ex);
                        rows.Add(new ReportRow { Nivel = "Erro", Categoria = "Região preenchida", Lote = lot.DisplayName, Mensagem = ex.Message, LotId = lot.Id });
                    }
                }
            }
            ProjectStore.Save(s.Doc, s.Project);
            t.Commit();
        }
        var rw = new ReportWindow("Elementos nativos criados", "Comparação entre a área calculada pelo Revit e pelo GeoPoint (diferenças acima de 0,01 m² indicam aproximação de arcos ou geometria inválida).", rows);
        if (Ui.ShowDialog(rw, s.MainWindow) == true && rw.Action == ReportAction.Select)
            LotServices.SelectSources(s, rw.SelectedRows.Where(r => r.LotId is not null).Select(r => s.Project.FindLot(r.LotId!)).OfType<LotRecord>());
        return Result.Succeeded;
    }

    private static ReportRow Compare(LotRecord lot, string what, double gp, double? revit)
    {
        if (revit is null) return new ReportRow { Nivel = "Aviso", Categoria = what, Lote = lot.DisplayName, Mensagem = "Criado, mas o Revit não informou a área.", LotId = lot.Id };
        double d = revit.Value - gp;
        return new ReportRow
        {
            Nivel = Math.Abs(d) <= 0.01 ? "OK" : "Atenção",
            Categoria = what,
            Lote = lot.DisplayName,
            Mensagem = $"Área GeoPoint {Fmt.N(gp, 4)} m² × Revit {Fmt.N(revit.Value, 4)} m² (diferença {Fmt.N(d, 4)} m²)",
            LotId = lot.Id,
        };
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
public sealed class CmdAreaTopo : CommandBase
{
    protected override string Title => "Área sobre Toposolid";

    protected override Result Run(GpSession s)
    {
        if (!s.Project.ActiveLots.Any()) { Ui.Info("Nenhum lote ativo", "Use 'Reconhecer Lotes' primeiro."); return Result.Cancelled; }
        var topos = new FilteredElementCollector(s.Doc).OfClass(typeof(Toposolid)).Cast<Toposolid>().ToList();
        if (topos.Count == 0) { Ui.Info("Nenhum Toposolid", "O modelo não possui Toposolid (terreno)."); return Result.Cancelled; }
        Toposolid topo;
        if (topos.Count == 1) topo = topos[0];
        else
        {
            var r = s.UiDoc.Selection.PickObject(ObjectType.Element, new ToposolidFilter(), "GeoPoint: clique no Toposolid (terreno)");
            topo = (Toposolid)s.Doc.GetElement(r);
        }
        var w = new LotPickerWindow("Área real sobre o terreno", "Calcula a área inclinada (superficial) de cada lote sobre o Toposolid e a altitude de cada vértice (coordenadas compartilhadas do projeto).",
            PickerRows.Build(s, true), null, "Calcular");
        if (Ui.ShowDialog(w, s.MainWindow) != true) return Result.Cancelled;

        var tris = TopoService.TopTriangles(topo);
        if (tris.Count == 0) { Ui.Warn("Superfície não encontrada", "Não foi possível ler a superfície superior do Toposolid."); return Result.Failed; }
        double elevOffset = s.Project.Frame.OriginElevation;
        var rows = new List<ReportRow>();
        foreach (var lot in w.SelectedLots)
        {
            var region = lot.ToRegion();
            double surf = TopoService.SurfaceArea(region, tris);
            int missing = 0;
            lot.VertexElevations.Clear();
            foreach (var v in region.AllRings().SelectMany(r => r.Vertices))
            {
                var z = TopoService.ElevationAt(v, tris);
                if (z is double zf) lot.VertexElevations[LotRecord.VertexKey(v)] = zf * RvtUnits.FtToM + elevOffset;
                else missing++;
            }
            double plan = region.Area;
            double coverage = 0;
            if (surf > 0) lot.AreaSuperficial = surf; else lot.AreaSuperficial = null;
            coverage = surf > 0 ? surf / plan : 0;
            rows.Add(new ReportRow
            {
                Nivel = surf <= 0 ? "Atenção" : missing > 0 ? "Atenção" : "OK",
                Lote = lot.DisplayName,
                Mensagem = surf <= 0 ? "O lote não está sobre o Toposolid."
                    : $"Área em planta {Fmt.N(plan, 2)} m² • área superficial {Fmt.N(surf, 2)} m² (+{Fmt.N((coverage - 1) * 100, 2)}%)" + (missing > 0 ? $" • {missing} vértice(s) fora do terreno" : ""),
                LotId = lot.Id,
            });
        }
        s.SaveProject("GeoPoint: área sobre Toposolid");
        Ui.ShowDialog(new ReportWindow("Área sobre o terreno", "As altitudes dos vértices ficam disponíveis no memorial (opção 'Altitudes') e na planilha.", rows, null, false), s.MainWindow);
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
