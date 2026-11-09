using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Settings;
using GeoPoint.Core.Sheets;

namespace GeoPoint.Revit.Infrastructure;

public sealed class SheetBuildOptions
{
    public SheetOrientation Orientation { get; set; } = SheetOrientation.Retrato;
    /// <summary>0 = automática.</summary>
    public int Scale { get; set; }
    public bool Anotacoes { get; set; } = true;
    public bool Vias { get; set; } = true;
    public bool NorteParaCima { get; set; } = true;
    public string LocationImage { get; set; } = "";
    public string NumberPrefix { get; set; } = "GP";
    public string Font { get; set; } = "Arial";
}

/// <summary>Gera vistas (cópias da planta original) e pranchas A4 com moldura e carimbo desenhados pelo GeoPoint.</summary>
public sealed class SheetBuilder
{
    private const double Ft = 304.8; // mm por pé
    private readonly GpSession _s;
    private readonly Document _doc;
    private readonly ProjectContext _ctx;
    private readonly ValueFormatter _fmt;
    private readonly SheetBuildOptions _o;
    private readonly View _source;
    private readonly List<string> _created = new();

    public SheetBuilder(GpSession s, View source, SheetBuildOptions o)
    {
        _s = s;
        _doc = s.Doc;
        _ctx = s.NewContext();
        _fmt = new ValueFormatter(s.Settings.Format);
        _o = o;
        _source = source;
    }

    public List<string> Warnings { get; } = new();
    /// <summary>UniqueIds das vistas e pranchas criadas.</summary>
    public IReadOnlyList<string> Created => _created;

    private static XYZ P(double xMm, double yMm) => new(xMm / Ft, yMm / Ft, 0);

    /// <summary>Cria todas as pranchas. Dentro de transação.</summary>
    public List<ViewSheet> Build(IReadOnlyList<SheetSpec> specs)
    {
        var sheets = new List<ViewSheet>();
        int n = specs.Count, i = 0;
        foreach (var spec in specs)
        {
            i++;
            try { sheets.Add(BuildOne(spec, i, n)); }
            catch (Exception ex)
            {
                Logger.Error("Prancha " + spec.Title, ex);
                Warnings.Add($"{spec.Title}: {ex.Message}");
            }
        }
        return sheets;
    }

    private ViewSheet BuildOne(SheetSpec spec, int index, int count)
    {
        var lots = spec.LotIds.Select(_s.Project.FindLot).Where(l => l is not null).Select(l => l!).ToList();
        if (lots.Count == 0) throw new InvalidOperationException("Nenhum lote para a prancha.");
        var layout = SheetLayout.A4(_o.Orientation);

        // ---- Vista: cópia da planta original (o desenho original não é alterado).
        var view = DuplicateView(spec, lots);
        var regions = lots.Select(l => l.ToRegion()).ToList();

        // ---- Quadro de áreas: ocupa uma faixa acima do carimbo.
        double quadroH = spec.QuadroIds.Count > 0 ? 6 + 4.6 * (spec.QuadroIds.Count + 2) : 0;
        LotReport? coordRep = null;
        if (spec.CoordTable && lots.Count == 1)
        {
            try { coordRep = _ctx.Report(lots[0]); } catch { }
            // Lotes com muitos vértices: o quadro não cabe na folha A4 (fica no memorial e na Tabela de Coordenadas).
            if (coordRep is not null && coordRep.Vertices.Count <= 24) quadroH = 6 + 4.2 * (coordRep.Vertices.Count + 1);
            else coordRep = null;
        }
        var area = layout.Drawing;
        var vpArea = new PaperRect(area.X0, area.Y0 + quadroH, area.X1, area.Y1);

        // ---- Escala e recorte.
        var pts = regions.SelectMany(r => r.AllRings().SelectMany(g => g.ToPolygon(0.05))).Select(p => RvtUnits.ToXyz(p, 0)).ToList();
        var crop = view.CropBox;
        var inv = crop.Transform.Inverse;
        var local = pts.Select(p => inv.OfPoint(p)).ToList();
        double minX = local.Min(p => p.X), maxX = local.Max(p => p.X), minY = local.Min(p => p.Y), maxY = local.Max(p => p.Y);
        double wM = (maxX - minX) * RvtUnits.FtToM, hM = (maxY - minY) * RvtUnits.FtToM;
        double marginMm = 16 + (_o.Vias && _s.Project.Vias.Count > 0 ? 10 : 0);
        int scale = _o.Scale > 0 ? _o.Scale : SheetLayout.ChooseScale(wM, hM, vpArea.Width - 6, vpArea.Height - 6, marginMm);
        view.Scale = scale;
        double marginFt = marginMm / Ft * scale;
        var box = new BoundingBoxXYZ
        {
            Transform = crop.Transform,
            Min = new XYZ(minX - marginFt, minY - marginFt, crop.Min.Z),
            Max = new XYZ(maxX + marginFt, maxY + marginFt, crop.Max.Z),
        };
        view.CropBoxActive = true;
        view.CropBox = box;
        try { view.CropBoxVisible = false; } catch { }
        try { view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.Set(0); } catch { }

        // ---- Conteúdo da vista: vias, hachuras e anotações.
        if (_o.Vias && _s.Project.Vias.Count > 0)
            foreach (var w in StreetDrawer.Draw(_doc, view, _ctx, lots, _s.Settings.Streets, _o.Font)) Warnings.Add($"{spec.Title}: {w}");
        if (spec.Hatch)
            Hatcher.Apply(_doc, view, lots, l => spec.Colors.TryGetValue(l.Id, out int c) ? c : 0);
        if (_o.Anotacoes)
        {
            var fails = new List<string>();
            Annotator.Annotate(_doc, view, lots, _ctx, _fmt, Annotator.Options.From(_s.Settings.Annotation), fails);
            Warnings.AddRange(fails.Select(f => $"{spec.Title}: {f}"));
        }
        _doc.Regenerate();

        // ---- Prancha.
        var sheet = CreateSheet(spec, index);
        _created.Add(sheet.UniqueId);
        bool customTitleBlock = !string.IsNullOrWhiteSpace(_s.Settings.Sheets.TitleBlock) && TitleBlockId() != ElementId.InvalidElementId;
        if (!customTitleBlock) DrawFrame(sheet, layout, spec, lots, scale, index, count);
        if (!Viewport.CanAddViewToSheet(_doc, sheet.Id, view.Id)) throw new InvalidOperationException("A vista não pode ser colocada na prancha.");
        var c = vpArea.Center;
        var vp = Viewport.Create(_doc, sheet.Id, view.Id, P(c.X, c.Y));
        NoTitle(vp);
        if (coordRep is not null) DrawCoordTable(sheet, new PaperRect(area.X0, area.Y0, area.X1, area.Y0 + quadroH), coordRep);
        else if (spec.QuadroIds.Count > 0) DrawQuadro(sheet, new PaperRect(area.X0, area.Y0, area.X1, area.Y0 + quadroH), spec);
        DrawNorth(sheet, view, new Vec2(area.X1 - 10, area.Y1 - 16));
        return sheet;
    }

    // ------------------------------------------------------------------ vista

    private View DuplicateView(SheetSpec spec, List<LotRecord> lots)
    {
        if (!_source.CanViewBeDuplicated(ViewDuplicateOption.Duplicate)) throw new InvalidOperationException($"A vista \"{_source.Name}\" não pode ser duplicada.");
        var id = _source.Duplicate(ViewDuplicateOption.Duplicate);
        var view = (View)_doc.GetElement(id);
        _created.Add(view.UniqueId);
        view.Name = UniqueViewName($"GP - {spec.Title} - {string.Join(", ", lots.Select(l => l.Nome)).Truncate(60)}");
        try { view.ViewTemplateId = ElementId.InvalidElementId; } catch { }
        try
        {
            // Orientação da cópia: a que deixa o norte do GeoPoint (o mesmo dos azimutes e do Google Earth) mais perto do topo.
            // Sem "norte para cima" mantém a orientação da vista original.
            if (_o.NorteParaCima)
            {
                int north = NorthGuard.BestPlanNorth(_ctx, out double residual);
                view.get_Parameter(BuiltInParameter.PLAN_VIEW_NORTH)?.Set(north);
                if (residual > 0.5) Logger.Info($"Prancha: o norte fica a {residual:0.##}° do topo (indicado pela seta do norte).");
            }
        }
        catch { }
        try { view.SetCategoryHidden(new ElementId(BuiltInCategory.OST_SiteProperty), true); } catch { }
        _doc.Regenerate();

        // Linhas de detalhe não são copiadas pela duplicação simples: copia as que formam os lotes e o contexto.
        var shownRegions = lots.Select(l => l.ToRegion()).ToList();
        if (_source.Id != view.Id)
        {
            var detail = new FilteredElementCollector(_doc, _source.Id).OfClass(typeof(CurveElement)).Cast<CurveElement>()
                .Where(c => c.CurveElementType == CurveElementType.DetailCurve && !IsGeoPointElement(c)).ToList();
            var keep = detail.Where(c => Keep(c, shownRegions)).Select(c => c.Id).ToList();
            if (keep.Count > 0)
            {
                try { ElementTransformUtils.CopyElements(_source, keep, view, Transform.Identity, new CopyPasteOptions()); }
                catch (Exception ex) { Logger.Error("Copiar linhas de detalhe", ex); }
            }
        }
        // Linhas de modelo internas aos lotes mostrados (ex.: divisas do desmembramento na situação atual) ficam ocultas.
        var hide = new FilteredElementCollector(_doc, view.Id).OfClass(typeof(CurveElement)).Cast<CurveElement>()
            .Where(c => c.CurveElementType == CurveElementType.ModelCurve && !Keep(c, shownRegions) && c.CanBeHidden(view)).Select(c => c.Id).ToList();
        if (hide.Count > 0) view.HideElements(hide);
        return view;
    }

    private bool IsGeoPointElement(Element e)
    {
        if (e is CurveElement ce)
        {
            try
            {
                string style = ce.LineStyle?.Name ?? "";
                if (style.StartsWith("GeoPoint - Extensão", StringComparison.Ordinal) || style.StartsWith("GeoPoint - Cota", StringComparison.Ordinal)
                    || style.StartsWith("GeoPoint - Meio-fio", StringComparison.Ordinal) || style.StartsWith("GeoPoint - Alinhamento", StringComparison.Ordinal)) return true;
            }
            catch { }
        }
        return false;
    }

    /// <summary>Mantém linhas sobre as divisas dos lotes mostrados ou fora deles; oculta as internas.</summary>
    private static bool Keep(CurveElement c, List<Region> shown)
    {
        Curve? g;
        try { g = c.GeometryCurve; } catch { return true; }
        if (g is null || !g.IsBound) return true;
        var samples = new[] { 0.2, 0.5, 0.8 }.Select(t => RvtUnits.ToVec(g.Evaluate(t, true))).ToList();
        const double tol = 0.01;
        bool onShownBoundary = samples.All(p => shown.Any(r => r.AllRings().Any(ring => ring.Edges.Any(e => e.Bounds.Expand(tol).Contains(p) && e.DistanceTo(p) <= tol))));
        if (onShownBoundary) return true;
        var mid = samples[1];
        bool inside = shown.Any(r => r.Bounds.Contains(mid) && r.Contains(mid));
        return !inside;
    }

    private string UniqueViewName(string name)
    {
        name = Clean(name);
        var names = new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(name)) return name;
        for (int i = 2; ; i++) if (!names.Contains($"{name} ({i})")) return $"{name} ({i})";
    }

    private static string Clean(string s)
    {
        foreach (char ch in "{}[]|;:<>?`~\\") s = s.Replace(ch, '-');
        return s;
    }

    // ------------------------------------------------------------------ prancha

    private ElementId TitleBlockId()
    {
        string want = _s.Settings.Sheets.TitleBlock;
        var tbs = new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_TitleBlocks).Cast<FamilySymbol>().ToList();
        var tb = tbs.FirstOrDefault(t => $"{t.FamilyName}: {t.Name}".Equals(want, StringComparison.OrdinalIgnoreCase) || t.Name.Equals(want, StringComparison.OrdinalIgnoreCase));
        return tb?.Id ?? ElementId.InvalidElementId;
    }

    private ViewSheet CreateSheet(SheetSpec spec, int index)
    {
        var tb = string.IsNullOrWhiteSpace(_s.Settings.Sheets.TitleBlock) ? ElementId.InvalidElementId : TitleBlockId();
        if (tb != ElementId.InvalidElementId && _doc.GetElement(tb) is FamilySymbol fs && !fs.IsActive) fs.Activate();
        var sheet = ViewSheet.Create(_doc, tb);
        var numbers = new FilteredElementCollector(_doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(v => v.SheetNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string prefix = string.IsNullOrWhiteSpace(_o.NumberPrefix) ? "GP" : _o.NumberPrefix.Trim();
        int k = 1;
        while (numbers.Contains($"{prefix}-{k:00}")) k++;
        sheet.SheetNumber = $"{prefix}-{k:00}";
        sheet.Name = Clean(spec.Title.Length > 0 ? spec.Title : "GeoPoint");
        return sheet;
    }

    private void NoTitle(Viewport vp)
    {
        try
        {
            foreach (var id in vp.GetValidTypes())
                if (_doc.GetElement(id) is ElementType t && t.get_Parameter(BuiltInParameter.VIEWPORT_ATTR_SHOW_LABEL) is { } p && p.AsInteger() == 0)
                {
                    vp.ChangeTypeId(id);
                    return;
                }
            if (_doc.GetElement(vp.GetTypeId()) is ElementType cur)
            {
                var nt = cur.Duplicate("GeoPoint - Sem título");
                nt.get_Parameter(BuiltInParameter.VIEWPORT_ATTR_SHOW_LABEL)?.Set(0);
                vp.ChangeTypeId(nt.Id);
            }
        }
        catch (Exception ex) { Logger.Error("Viewport sem título", ex); }
    }

    private void Line(View sheet, Vec2 a, Vec2 b, GraphicsStyle? style)
    {
        if (a.DistanceTo(b) < 0.5) return;
        var dc = _doc.Create.NewDetailCurve(sheet, Autodesk.Revit.DB.Line.CreateBound(P(a.X, a.Y), P(b.X, b.Y)));
        if (style is not null) dc.LineStyle = style;
    }

    private void Rect(View sheet, PaperRect r, GraphicsStyle? style)
    {
        Line(sheet, new Vec2(r.X0, r.Y0), new Vec2(r.X1, r.Y0), style);
        Line(sheet, new Vec2(r.X1, r.Y0), new Vec2(r.X1, r.Y1), style);
        Line(sheet, new Vec2(r.X1, r.Y1), new Vec2(r.X0, r.Y1), style);
        Line(sheet, new Vec2(r.X0, r.Y1), new Vec2(r.X0, r.Y0), style);
    }

    private TextNote Text(View sheet, double x, double y, string text, double sizeMm, bool bold = false, HorizontalTextAlignment h = HorizontalTextAlignment.Left,
        VerticalTextAlignment v = VerticalTextAlignment.Top, double? widthMm = null)
    {
        var type = RevitStyles.TextType(_doc, sizeMm, _o.Font, false, bold);
        var opts = new TextNoteOptions(type) { HorizontalAlignment = h };
        // O Revit insere o texto pela borda superior da caixa: converte o alinhamento pedido.
        int lines = text.Split('\n').Length;
        if (widthMm is double ww && ww > 2) lines = Math.Max(lines, (int)Math.Ceiling(AnnotationPlanner.TextWidth(text, sizeMm, bold) / Math.Max(1, ww - 1)));
        double boxH = 2 * RevitStyles.HalfHeightMm(sizeMm, lines);
        double top = v switch { VerticalTextAlignment.Middle => y + boxH / 2, VerticalTextAlignment.Bottom => y + boxH, _ => y };
        text = text.Replace("\n", "\r");
        return widthMm is double w && w > 2
            ? TextNote.Create(_doc, sheet.Id, P(x, top), w / Ft, text, opts)
            : TextNote.Create(_doc, sheet.Id, P(x, top), text, opts);
    }

    private void DrawFrame(ViewSheet sheet, SheetLayout layout, SheetSpec spec, List<LotRecord> lots, int scale, int index, int count)
    {
        var thick = RevitStyles.LineStyle(_doc, "GeoPoint - Prancha (moldura)", new Color(0, 0, 0), 5);
        var thin = RevitStyles.LineStyle(_doc, "GeoPoint - Prancha (linhas)", new Color(0, 0, 0), 1);
        var paper = RevitStyles.LineStyle(_doc, "GeoPoint - Prancha (folha)", new Color(160, 160, 160), 1);
        Rect(sheet, new PaperRect(0, 0, layout.Width, layout.Height), paper);
        Rect(sheet, layout.Frame, thick);
        Rect(sheet, layout.TitleBlock, thick);
        foreach (var (a, b) in layout.Lines) Line(sheet, a, b, thin);

        var info = _s.Project.Info;
        var tech = _s.Settings.Technician;
        var reports = lots.Select(l => { try { return _ctx.Report(l); } catch { return null; } }).Where(r => r is not null).Select(r => r!).ToList();
        double area = reports.Sum(r => r.AreaLocal);
        string owners = string.Join("; ", lots.Select(l => string.IsNullOrWhiteSpace(l.Proprietario) ? info.Proprietario : l.Proprietario)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
        string matr = string.Join("; ", lots.Select(l => l.Matricula).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
        if (matr.Length == 0) matr = info.Matricula;
        if (!string.IsNullOrWhiteSpace(info.InscricaoMunicipal)) matr += (matr.Length > 0 ? " / Insc. " : "Insc. ") + info.InscricaoMunicipal;
        string endereco = lots.Select(l => l.Endereco).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? info.Endereco;
        if (!string.IsNullOrWhiteSpace(info.Bairro)) endereco += (endereco.Length > 0 ? " – " : "") + info.Bairro;
        string lotNames = string.Join(", ", lots.Select(l => l.DisplayName));
        string imovel = endereco.Length > 0 ? $"{lotNames} – {endereco}" : lotNames;

        var values = new Dictionary<string, string>
        {
            ["proprietario"] = owners,
            ["imovel"] = imovel,
            ["municipio"] = string.Join("/", new[] { info.Municipio, info.UF }.Where(x => !string.IsNullOrWhiteSpace(x))),
            ["matricula"] = matr,
            ["area"] = (lots.Count > 1 ? "Total: " : "") + _fmt.Area(area),
            ["perimetro"] = reports.Count == 1 ? _fmt.Distance(reports[0].PerimeterLocal) : "ver quadro de áreas",
            ["coordenada"] = ReferenceText(),
            ["escala"] = $"1:{scale}",
            ["data"] = DateTime.Now.ToString("dd/MM/yyyy"),
            ["folha"] = $"{index:00}/{count:00}",
            ["prancha"] = sheet.SheetNumber,
        };
        foreach (var cell in layout.Cells)
        {
            var r = cell.Rect;
            if (cell.Key == "titulo")
            {
                Text(sheet, r.X0 + 2, r.Y1 - 1.6, spec.Title, cell.ValueMm, true, widthMm: r.Width - 4);
                string sub = string.Join(" – ", new[] { spec.Subtitle, info.Situacao.Length > 0 && !spec.Subtitle.Contains(info.Situacao.ToUpperInvariant()) ? info.Situacao.ToUpperInvariant() : "" }.Where(x => x.Length > 0));
                if (sub.Length > 0) Text(sheet, r.X0 + 2, r.Y0 + 1.2, sub, 1.9, false, v: VerticalTextAlignment.Bottom, widthMm: r.Width - 4);
                continue;
            }
            if (cell.Label.Length > 0) Text(sheet, r.X0 + 1.2, r.Y1 - 0.4, cell.Label, 1.4);
            switch (cell.Key)
            {
                case "localizacao":
                    PlaceImage(sheet, new PaperRect(r.X0 + 1.5, r.Y0 + 1.5, r.X1 - 1.5, r.Y1 - 4));
                    break;
                case "resp":
                {
                    double ly = r.Y0 + r.Height * 0.42;
                    Line(sheet, new Vec2(r.X0 + 4, ly), new Vec2(r.X1 - 4, ly), thin);
                    string t = string.Join("\n", new[]
                    {
                        tech.Nome, string.Join(" – ", new[] { tech.Profissao, tech.Registro.Length > 0 ? $"{tech.Conselho} {tech.Registro}" : "" }.Where(x => x.Length > 0)),
                        info.RrtArt.Length > 0 ? (tech.Conselho == "CAU" ? "RRT " : "ART ") + info.RrtArt : "",
                    }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    if (t.Length > 0) Text(sheet, r.Center.X, ly - 0.8, t, cell.ValueMm, h: HorizontalTextAlignment.Center, widthMm: r.Width - 4);
                    break;
                }
                case "assprop":
                {
                    double ly = r.Y0 + r.Height * 0.42;
                    Line(sheet, new Vec2(r.X0 + 4, ly), new Vec2(r.X1 - 4, ly), thin);
                    if (owners.Length > 0) Text(sheet, r.Center.X, ly - 0.8, owners, cell.ValueMm, h: HorizontalTextAlignment.Center, widthMm: r.Width - 4);
                    break;
                }
                default:
                    if (values.TryGetValue(cell.Key, out var val) && !string.IsNullOrWhiteSpace(val))
                    {
                        // Texto longo: reduz a altura até caber numa linha (mínimo 1,5 mm); se ainda não couber, quebra a linha.
                        double size = cell.ValueMm, avail = r.Width - 2.4;
                        foreach (var line in val.Split('\n'))
                            while (size > 1.5 && AnnotationPlanner.TextWidth(line, size, cell.Bold) > avail) size = Math.Round(size - 0.1, 2);
                        if (cell.Centered) Text(sheet, r.Center.X, r.Y0 + 0.6, val, size, cell.Bold, HorizontalTextAlignment.Center, VerticalTextAlignment.Bottom, r.Width - 2);
                        else Text(sheet, r.X0 + 1.2, r.Y0 + 0.6, val, size, cell.Bold, v: VerticalTextAlignment.Bottom, widthMm: r.Width - 2.4);
                    }
                    break;
            }
        }
    }

    private string ReferenceText()
    {
        var r = _s.Project.Reference;
        if (!r.IsSet) return "Coordenadas locais (sem georreferência)";
        var anchor = new Vec2(r.AnchorX, r.AnchorY);
        var utm = _ctx.Coords.ToCrs(anchor, CrsDef.Utm());
        string name = string.IsNullOrWhiteSpace(r.AnchorLabel) ? "Ponto de referência" : r.AnchorLabel;
        string geo = $"{_fmt.Lat(r.Lat)}  {_fmt.Lon(r.Lon)}";
        return utm is { } u
            ? $"{name}: E {_fmt.Plane(u.A)} m  N {_fmt.Plane(u.B)} m – UTM {u.ZoneLabel} SIRGAS 2000\n{geo}"
            : $"{name}: {geo} (SIRGAS 2000)";
    }

    private void PlaceImage(ViewSheet sheet, PaperRect r)
    {
        string path = _o.LocationImage;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Text(sheet, r.Center.X, r.Center.Y, "(inserir imagem de localização)", 1.6, h: HorizontalTextAlignment.Center, v: VerticalTextAlignment.Middle);
            return;
        }
        try
        {
            var type = new FilteredElementCollector(_doc).OfClass(typeof(ImageType)).Cast<ImageType>()
                .FirstOrDefault(t => { try { return string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase); } catch { return false; } })
                ?? ImageType.Create(_doc, new ImageTypeOptions(path, false, ImageTypeSource.Import));
            var inst = ImageInstance.Create(_doc, sheet, type.Id, new ImagePlacementOptions(P(r.Center.X, r.Center.Y), BoxPlacement.Center));
            double w = inst.Width, h = inst.Height;
            if (w <= 0 || h <= 0) return;
            double k = Math.Min(r.Width / Ft / w, r.Height / Ft / h);
            inst.Width = w * k;
            if (inst.Height > r.Height / Ft * 1.001) inst.Height = r.Height / Ft;
            // Recentraliza após redimensionar.
            var bb = inst.get_BoundingBox(sheet);
            if (bb is not null)
            {
                var cur = (bb.Min + bb.Max) / 2;
                ElementTransformUtils.MoveElement(_doc, inst.Id, new XYZ(r.Center.X / Ft - cur.X, r.Center.Y / Ft - cur.Y, 0));
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Imagem de localização", ex);
            Warnings.Add("Não foi possível inserir a imagem de localização: " + ex.Message);
        }
    }

    private void DrawQuadro(ViewSheet sheet, PaperRect band, SheetSpec spec)
    {
        var thin = RevitStyles.LineStyle(_doc, "GeoPoint - Prancha (linhas)", new Color(0, 0, 0), 1);
        var lots = spec.QuadroIds.Select(_s.Project.FindLot).Where(l => l is not null).Select(l => l!).ToList();
        double rowH = 4.6;
        double x0 = band.X0 + 3, x1 = band.X1 - 3;
        double top = band.Y1 - 2;
        double[] cols = { 0, 0.34, 0.52, 0.68, 1.0 };
        string[] heads = { "LOTE", "ÁREA (m²)", "PERÍMETRO (m)", "PROPRIETÁRIO" };
        double X(int c) => x0 + (x1 - x0) * cols[c];
        Text(sheet, x0, top + 0.2, "QUADRO DE ÁREAS", 1.8, true, v: VerticalTextAlignment.Bottom);
        int rows = lots.Count + 2;
        double bottom = top - rowH * rows;
        for (int r = 0; r <= rows; r++) Line(sheet, new Vec2(x0, top - r * rowH), new Vec2(x1, top - r * rowH), thin);
        for (int c = 0; c < cols.Length; c++) Line(sheet, new Vec2(X(c), top), new Vec2(X(c), bottom), thin);
        void Row(int r, string[] cells, bool bold)
        {
            double y = top - r * rowH - rowH / 2;
            for (int c = 0; c < 4; c++)
            {
                if (string.IsNullOrEmpty(cells[c])) continue;
                bool left = c == 0 || c == 3;
                Text(sheet, left ? X(c) + 1 : (X(c) + X(c + 1)) / 2, y, cells[c], 1.8, bold, left ? HorizontalTextAlignment.Left : HorizontalTextAlignment.Center, VerticalTextAlignment.Middle);
            }
        }
        Row(0, heads, true);
        double total = 0;
        for (int i = 0; i < lots.Count; i++)
        {
            var l = lots[i];
            LotReport? rep = null;
            try { rep = _ctx.Report(l); } catch { }
            total += rep?.AreaLocal ?? 0;
            string owner = string.IsNullOrWhiteSpace(l.Proprietario) ? _s.Project.Info.Proprietario : l.Proprietario;
            Row(i + 1, new[] { l.DisplayName, rep is null ? "—" : _fmt.Number(rep.AreaLocal, _s.Settings.Format.AreaDecimals), rep is null ? "—" : _fmt.DistanceValue(rep.PerimeterLocal), owner.Truncate(48) }, l.IsRemanescente);
        }
        Row(lots.Count + 1, new[] { "TOTAL", _fmt.Number(total, _s.Settings.Format.AreaDecimals), "", "" }, true);
    }

    private void DrawCoordTable(ViewSheet sheet, PaperRect band, LotReport rep)
    {
        var thin = RevitStyles.LineStyle(_doc, "GeoPoint - Prancha (linhas)", new Color(0, 0, 0), 1);
        var def = _ctx.Coords.HasGeoreference ? (_s.Settings.Memorial.CoordinateSystems.FirstOrDefault() ?? CrsDef.Utm()) : CrsDef.ProjectLocalDef();
        var (ta, tb) = _fmt.ColumnTitles(def);
        string[] heads = { "VÉRTICE", ta.ToUpperInvariant(), tb.ToUpperInvariant(), _s.Settings.Format.AngleStyle == AngleStyle.Rumo ? "RUMO" : "AZIMUTE", "DIST. (m)", "CONFRONTANTE" };
        double[] cols = { 0, 0.09, 0.25, 0.41, 0.57, 0.67, 1.0 };
        double rowH = 4.2, x0 = band.X0 + 3, x1 = band.X1 - 3, top = band.Y1 - 2;
        double X(int c) => x0 + (x1 - x0) * cols[c];
        Text(sheet, x0, top + 0.2, $"QUADRO DE COORDENADAS – {rep.Lot.DisplayName.ToUpperInvariant()} ({def.ShortName(_ctx.Coords.AutoZone)})", 1.8, true, v: VerticalTextAlignment.Bottom);
        int n = rep.Vertices.Count;
        double bottom = top - rowH * (n + 1);
        for (int r = 0; r <= n + 1; r++) Line(sheet, new Vec2(x0, top - r * rowH), new Vec2(x1, top - r * rowH), thin);
        for (int c = 0; c < cols.Length; c++) Line(sheet, new Vec2(X(c), top), new Vec2(X(c), bottom), thin);
        void Row(int r, string[] cells, bool bold)
        {
            double y = top - r * rowH - rowH / 2;
            for (int c = 0; c < cells.Length; c++)
            {
                if (string.IsNullOrEmpty(cells[c])) continue;
                double size = 1.7, avail = (X(c + 1) - X(c)) - 1.5;
                while (size > 1.3 && AnnotationPlanner.TextWidth(cells[c], size, bold) > avail) size = Math.Round(size - 0.1, 2);
                bool left = c == cells.Length - 1;
                Text(sheet, left ? X(c) + 0.8 : (X(c) + X(c + 1)) / 2, y, cells[c], size, bold, left ? HorizontalTextAlignment.Left : HorizontalTextAlignment.Center, VerticalTextAlignment.Middle);
            }
        }
        Row(0, heads, true);
        for (int i = 0; i < n; i++)
        {
            var v = rep.Vertices[i];
            var sgm = rep.Segments[i];
            var c = _ctx.Coords.ToCrs(v.Internal, def);
            var (a, b) = c is { } cc ? _fmt.Columns(cc, def) : ("–", "–");
            string dist = sgm.IsArc ? $"{_fmt.DistanceValue(sgm.LengthHorizontal)} (R={_fmt.DistanceValue(sgm.Radius ?? 0)})" : _fmt.DistanceValue(sgm.LengthHorizontal);
            Row(i + 1, new[] { v.Name, a, b, _fmt.Angle(sgm.AzimuthTrue), dist, $"{sgm.From.Name}–{sgm.To.Name}: {(string.IsNullOrWhiteSpace(sgm.Confrontante) ? "–" : sgm.Confrontante)}" }, false);
        }
    }

    private void DrawNorth(ViewSheet sheet, View view, Vec2 at)
    {
        try
        {
            // Direção do norte verdadeiro na folha, a partir da orientação da vista.
            var n = _ctx.Coords.DirectionFromAzimuth(0);
            var right = view.RightDirection; var up = view.UpDirection;
            var dir = new Vec2(n.X * right.X + n.Y * right.Y, n.X * up.X + n.Y * up.Y);
            dir = dir.LengthSquared < 1e-9 ? new Vec2(0, 1) : dir.Normalized();
            var side = dir.PerpRight;
            var tip = at + dir * 6;
            var tail = at - dir * 6;
            var notch = at - dir * 3;
            var baseL = tail - side * 3.2;
            var baseR = tail + side * 3.2;
            var solid = RevitStyles.FillType(_doc, LotUse.Lote, "GeoPoint - Ponto de vértice", (0, 0, 0));
            var loop = CurveLoop.Create(new List<Curve>
            {
                Autodesk.Revit.DB.Line.CreateBound(P(tip.X, tip.Y), P(baseR.X, baseR.Y)),
                Autodesk.Revit.DB.Line.CreateBound(P(baseR.X, baseR.Y), P(notch.X, notch.Y)),
                Autodesk.Revit.DB.Line.CreateBound(P(notch.X, notch.Y), P(tip.X, tip.Y)),
            });
            FilledRegion.Create(_doc, solid, sheet.Id, new List<CurveLoop> { loop });
            var thin = RevitStyles.LineStyle(_doc, "GeoPoint - Prancha (linhas)", new Color(0, 0, 0), 1);
            Line(sheet, tip, baseL, thin);
            Line(sheet, baseL, notch, thin);
            var label = tip + dir * 1.2;
            Text(sheet, label.X, label.Y, "N", 3, true, HorizontalTextAlignment.Center, VerticalTextAlignment.Bottom);
            Text(sheet, at.X, Math.Min(baseL.Y, baseR.Y) - 1.2, "NV", 1.6, false, HorizontalTextAlignment.Center, VerticalTextAlignment.Top);
        }
        catch (Exception ex) { Logger.Error("Seta do norte", ex); }
    }
}

internal static class StringExt
{
    public static string Truncate(this string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
