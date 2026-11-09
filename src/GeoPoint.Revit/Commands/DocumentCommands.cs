using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Export;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Revit.Infrastructure;
using GeoPoint.Revit.UI;

namespace GeoPoint.Revit.Commands;

internal static class Folders
{
    public static string Default(GpSession s, string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && Directory.Exists(preferred)) return preferred;
        if (!string.IsNullOrWhiteSpace(s.Settings.LastExportFolder) && Directory.Exists(s.Settings.LastExportFolder)) return s.Settings.LastExportFolder;
        try
        {
            if (!string.IsNullOrWhiteSpace(s.Doc.PathName))
            {
                var d = Path.GetDirectoryName(s.Doc.PathName);
                if (!string.IsNullOrWhiteSpace(d) && Directory.Exists(d)) return d;
            }
        }
        catch { }
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    public static void Remember(GpSession s, string file)
    {
        try
        {
            s.Settings.LastExportFolder = Path.GetDirectoryName(file) ?? "";
            s.SaveSettings();
        }
        catch { }
    }

    public static string ProjectName(GpSession s)
    {
        string t = s.Doc.Title ?? "Projeto";
        if (t.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)) t = t[..^4];
        return Ui.SafeFileName(t);
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdMemorial : CommandBase
{
    protected override string Title => "Memorial descritivo";

    protected override Result Run(GpSession s) => Open(s, null, null);

    internal static Result Open(GpSession s, MemorialKind? kind, OperationRecord? operation)
    {
        if (!s.Project.Lots.Any()) { Ui.Info("Nenhum lote cadastrado", "Use 'Reconhecer Lotes' primeiro."); return Result.Cancelled; }
        if (!NorthGuard.Ensure(s)) return Result.Cancelled;
        var ctx = s.NewContext();
        var pre = LotServices.LotsFromSelection(s);
        var w = new MemorialWindow(s.Project, s.Settings, ctx, pre, s.MainWindow, kind, operation);
        if (Ui.ShowDialog(w, s.MainWindow) != true) return Result.Cancelled;
        try { s.SaveSettings(); } catch (Exception ex) { Logger.Error("Configurações", ex); }
        if (w.InfoChanged) s.SaveProject("GeoPoint: dados do imóvel");

        var m = s.Settings.Memorial;
        if (!s.Project.Reference.IsSet && m.CoordinateSystems.Any(c => c.Kind != CrsKind.ProjectLocal))
        {
            if (!Ui.Confirm("Projeto sem ponto de referência",
                    "Sem o ponto de referência não há coordenadas geográficas/UTM. O memorial será gerado com as coordenadas locais do projeto.\n\nDeseja continuar?",
                    "Gerar com coordenadas locais", "Cancelar (definir a referência antes)"))
                return Result.Cancelled;
        }
        if (string.IsNullOrWhiteSpace(s.Settings.Technician.Nome))
            Ui.Warn("Responsável técnico não informado", "O memorial será gerado sem o nome do responsável técnico. Informe em Configurações › Responsável técnico.");

        byte[]? template = null;
        if (!string.IsNullOrWhiteSpace(m.TemplatePath))
        {
            if (File.Exists(m.TemplatePath))
            {
                try { template = File.ReadAllBytes(m.TemplatePath); }
                catch (Exception ex) { Ui.Warn("Modelo indisponível", $"Não foi possível ler o modelo ({ex.Message}). Será usado o modelo padrão."); }
            }
            else Ui.Warn("Modelo não encontrado", $"O modelo configurado não existe mais:\n{m.TemplatePath}\nSerá usado o modelo padrão.");
        }
        var phrases = MemorialPhrases.Load(m.PhrasesPath);
        ctx = s.NewContext();
        var gen = new MemorialGenerator(ctx, phrases);
        // Memoriais individuais: numeração própria de cada lote (P-01…), como nas pranchas individuais.
        var localSettings = s.Settings.Clone();
        localSettings.Naming.Numbering = VertexNumbering.Lote;
        var genLocal = new MemorialGenerator(new ProjectContext(s.Project, localSettings), phrases);

        var jobs = new List<(MemorialRequest req, string name, bool local)>();
        if (w.Kind == MemorialKind.Lotes && m.OneFilePerLot && w.Lots.Count > 1)
            foreach (var l in w.Lots) jobs.Add((new MemorialRequest { Kind = MemorialKind.Lotes, Lots = new() { l }, Date = w.MemorialDate }, $"Memorial descritivo - {l.DisplayName}", false));
        else if (w.Kind == MemorialKind.Desmembramento && w.Operation is { } op)
        {
            if (w.SplitIndividual)
                foreach (var c in ProjectContext.SortLots(op.ChildIds.Select(s.Project.FindLot).Where(l => l is not null).Select(l => l!)))
                    jobs.Add((new MemorialRequest { Kind = MemorialKind.Lotes, Lots = new() { c }, Operation = op, Date = w.MemorialDate },
                        $"Memorial descritivo - {c.DisplayName} (desmembramento)", true));
            if (w.SplitGeneral)
                jobs.Add((new MemorialRequest { Kind = MemorialKind.Desmembramento, Lots = w.Lots, Operation = op, Date = w.MemorialDate }, "Memorial descritivo - Desmembramento (geral)", false));
        }
        else
        {
            string name = w.Kind switch
            {
                MemorialKind.Desmembramento => "Memorial descritivo - Desmembramento",
                MemorialKind.Remembramento => "Memorial descritivo - Remembramento",
                MemorialKind.Loteamento => "Memorial descritivo - Loteamento",
                _ => w.Lots.Count == 1 ? $"Memorial descritivo - {w.Lots[0].DisplayName}" : "Memorial descritivo - Lotes",
            };
            jobs.Add((new MemorialRequest { Kind = w.Kind, Lots = w.Lots, Operation = w.Operation, Date = w.MemorialDate }, name, false));
        }
        if (jobs.Count == 0) { Ui.Info("Memorial", "Nada a gerar: a operação não tem lotes resultantes."); return Result.Cancelled; }
        if (s.Project.Fases.Count > 1)
            jobs = jobs.Select(j => (j.req, $"{j.name} - Fase {s.Project.Fase.Numero}", j.local)).ToList();

        string folder = Folders.Default(s, m.OutputFolder);
        var files = new List<string>();
        if (jobs.Count == 1)
        {
            var path = Ui.SaveFile("Salvar memorial descritivo", "Documento do Word (*.docx)|*.docx", jobs[0].name + ".docx", folder);
            if (path is null) return Result.Cancelled;
            var bytes = (jobs[0].local ? genLocal : gen).Generate(jobs[0].req, template);
            if (!Ui.TryWrite(path, bytes)) return Result.Failed;
            files.Add(path);
        }
        else
        {
            var dir = Ui.PickFolder($"Pasta para os memoriais ({jobs.Count} arquivos)", folder);
            if (dir is null) return Result.Cancelled;
            foreach (var (req, name, local) in jobs)
            {
                var path = Path.Combine(dir, Ui.SafeFileName(name) + ".docx");
                if (!Ui.TryWrite(path, (local ? genLocal : gen).Generate(req, template))) return Result.Failed;
                files.Add(path);
            }
        }
        Folders.Remember(s, files[0]);

        var warnings = gen.Warnings.Concat(genLocal.Warnings).Distinct().ToList();
        foreach (var l in w.Lots.Where(l => l.Status == LotStatus.Ativo))
        {
            try
            {
                int missing = ctx.Report(l).Segments.Count(x => string.IsNullOrWhiteSpace(x.Confrontante));
                if (missing > 0) warnings.Add($"{l.DisplayName}: {missing} divisa(s) sem confrontante.");
            }
            catch { }
        }
        if (m.OpenAfterGenerate) foreach (var f in files.Take(5)) Ui.OpenFile(f);
        string msg = files.Count == 1 ? files[0] : $"{files.Count} arquivos em {Path.GetDirectoryName(files[0])}";
        if (warnings.Count > 0) Ui.Warn("Memorial gerado com avisos", msg + "\n\n• " + string.Join("\n• ", warnings.Take(15)));
        else if (!m.OpenAfterGenerate) Ui.Info("Memorial gerado", msg);
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdExportarSig : CommandBase
{
    protected override string Title => "Exportar para Google Earth / QGIS";

    protected override Result Run(GpSession s)
    {
        if (!s.Project.Reference.IsSet)
        {
            Ui.Warn("Ponto de referência não definido", "Para exportar para o Google Earth ou QGIS é preciso informar a coordenada de um vértice (Georreferência › Definir Referência).");
            return Result.Cancelled;
        }
        if (!NorthGuard.Ensure(s)) return Result.Cancelled;
        if (!s.Project.Lots.Any()) { Ui.Info("Nenhum lote cadastrado", "Use 'Reconhecer Lotes' primeiro."); return Result.Cancelled; }
        var ctx = s.NewContext();
        var pre = LotServices.LotsFromSelection(s);
        var rows = ProjectContext.SortLots(s.Project.PhaseLots).Select(l => new LotRow(l, SafeArea(l)) { Selected = pre.Count == 0 ? l.Status == LotStatus.Ativo : pre.Contains(l.Id) }).ToList();
        var k = s.Settings.Kml;
        var opts = new List<OptionItem>
        {
            new("kmz", "KMZ – Google Earth (recomendado)", true),
            new("kml", "KML – Google Earth / QGIS", false),
            new("geojson", "GeoJSON – QGIS", true),
            new("vert", "Incluir cada vértice como ponto (nome e coordenadas) – recomendado para conferir no Earth", true),
            new("edges", "Incluir as divisas como linhas (com azimute e distância)", k.IncludeEdges),
            new("open", "Abrir o KMZ no Google Earth ao terminar", true),
            new("opacity", "Opacidade do preenchimento (0 a 1)", k.FillOpacity),
        };
        var w = new LotPickerWindow("Exportar para Google Earth / QGIS",
            "Os lotes são exportados em latitude/longitude (SIRGAS 2000 ≈ WGS 84), coloridos por uso, para conferência sobre imagem de satélite.",
            rows, opts, "Exportar");
        if (Ui.ShowDialog(w, s.MainWindow) != true) return Result.Cancelled;
        bool kmz = w.Option("kmz"), kml = w.Option("kml"), gj = w.Option("geojson");
        if (!kmz && !kml && !gj) { Ui.Warn("Nenhum formato", "Marque pelo menos um formato."); return Result.Cancelled; }
        k.IncludeVertices = w.Option("vert");
        k.IncludeEdges = w.Option("edges");
        k.FillOpacity = Math.Clamp(w.Number("opacity"), 0, 1);
        try { s.SaveSettings(); } catch { }

        string baseName = Folders.ProjectName(s) + " - GeoPoint";
        var path = Ui.SaveFile("Salvar exportação (o nome é usado para todos os formatos)", kmz ? "Google Earth (*.kmz)|*.kmz" : kml ? "KML (*.kml)|*.kml" : "GeoJSON (*.geojson)|*.geojson",
            baseName + (kmz ? ".kmz" : kml ? ".kml" : ".geojson"), Folders.Default(s, null));
        if (path is null) return Result.Cancelled;
        string stem = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
        var lots = w.SelectedLots;
        var written = new List<string>();
        var kmlText = GisExport.BuildKml(ctx, lots, Folders.ProjectName(s));
        if (kmz && Ui.TryWrite(stem + ".kmz", GisExport.BuildKmz(kmlText))) written.Add(stem + ".kmz");
        if (kml && Ui.TryWrite(stem + ".kml", new System.Text.UTF8Encoding(false).GetBytes(kmlText))) written.Add(stem + ".kml");
        if (gj && Ui.TryWrite(stem + ".geojson", new System.Text.UTF8Encoding(false).GetBytes(GisExport.BuildGeoJson(ctx, lots, k.IncludeVertices)))) written.Add(stem + ".geojson");
        if (written.Count == 0) return Result.Failed;
        Folders.Remember(s, written[0]);
        if (w.Option("open") && written.FirstOrDefault(f => f.EndsWith(".kmz") || f.EndsWith(".kml")) is string toOpen) Ui.OpenFile(toOpen);
        else Ui.Info("Exportação concluída", string.Join("\n", written) + "\n\nNo QGIS: Camada › Adicionar camada vetorial e escolha o arquivo .geojson ou .kml.");
        return Result.Succeeded;
    }

    private static double SafeArea(LotRecord l) { try { return l.ToRegion().Area; } catch { return 0; } }

    /// <summary>Exporta todos os lotes ativos para um KMZ temporário e abre no Google Earth.</summary>
    internal static void QuickKmz(GpSession s)
    {
        if (!NorthGuard.Ensure(s)) return;
        try
        {
            var ctx = s.NewContext();
            var lots = s.Project.ActiveLots.ToList();
            ctx.Settings.Kml.IncludeVertices = true; // conferência: sempre com todos os pontos
            var kml = GisExport.BuildKml(ctx, lots, Folders.ProjectName(s));
            var dir = Path.Combine(Path.GetTempPath(), "GeoPoint");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, Folders.ProjectName(s) + " - conferencia.kmz");
            if (Ui.TryWrite(path, GisExport.BuildKmz(kml))) Ui.OpenFile(path);
        }
        catch (Exception ex)
        {
            Logger.Error("KMZ rápido", ex);
            Ui.Warn("Não foi possível abrir no Google Earth", ex.Message);
        }
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdExportarExcel : CommandBase
{
    protected override string Title => "Exportar planilha";

    protected override Result Run(GpSession s)
    {
        if (!s.Project.Lots.Any()) { Ui.Info("Nenhum lote cadastrado", "Use 'Reconhecer Lotes' primeiro."); return Result.Cancelled; }
        if (!NorthGuard.Ensure(s)) return Result.Cancelled;
        var ctx = s.NewContext();
        var pre = LotServices.LotsFromSelection(s);
        var rows = ProjectContext.SortLots(s.Project.PhaseLots).Select(l =>
        {
            double a = 0; try { a = l.ToRegion().Area; } catch { }
            return new LotRow(l, a) { Selected = pre.Count == 0 ? l.Status == LotStatus.Ativo : pre.Contains(l.Id) };
        }).ToList();
        var w = new LotPickerWindow("Planilha Excel", "Gera abas com: lotes (áreas, testadas, títulos), quadro de áreas, vértices (coordenadas em todos os sistemas) e segmentos.", rows, null, "Exportar");
        if (Ui.ShowDialog(w, s.MainWindow) != true) return Result.Cancelled;
        var path = Ui.SaveFile("Salvar planilha", "Excel (*.xlsx)|*.xlsx", Folders.ProjectName(s) + " - GeoPoint.xlsx", Folders.Default(s, null));
        if (path is null) return Result.Cancelled;
        if (!Ui.TryWrite(path, ExcelReport.Build(ctx, w.SelectedLots))) return Result.Failed;
        Folders.Remember(s, path);
        Ui.OpenFile(path);
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdQuadroAreas : CommandBase
{
    protected override string Title => "Quadro de áreas";

    protected override Result Run(GpSession s)
    {
        if (!s.Project.ActiveLots.Any()) { Ui.Info("Nenhum lote ativo", "Use 'Reconhecer Lotes' primeiro."); return Result.Cancelled; }
        var ctx = s.NewContext();
        var w = new QuadroWindow(ctx);
        Ui.ShowDialog(w, s.MainWindow);
        if (w.ProfileChanged) s.SaveProject("GeoPoint: perfil urbanístico");
        if (w.ExportRequested)
        {
            var path = Ui.SaveFile("Salvar planilha", "Excel (*.xlsx)|*.xlsx", Folders.ProjectName(s) + " - Quadro de areas.xlsx", Folders.Default(s, null));
            if (path is null) return Result.Succeeded;
            if (Ui.TryWrite(path, ExcelReport.Build(s.NewContext(), s.Project.ActiveLots.ToList())))
            {
                Folders.Remember(s, path);
                Ui.OpenFile(path);
            }
        }
        return Result.Succeeded;
    }
}
