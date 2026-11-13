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
    /// <summary>Modelo de prancha (biblioteca de modelos).</summary>
    public SheetTemplate Template { get; set; } = SheetTemplate.Default();
    /// <summary>0 = automática (a menor escala da lista do modelo em que o desenho cabe).</summary>
    public int Scale { get; set; }
    public bool Anotacoes { get; set; } = true;
    public bool Vias { get; set; } = true;
    public bool NorteParaCima { get; set; } = true;
    /// <summary>Imagem de localização escolhida pelo usuário (usada quando não há croqui automático).</summary>
    public string LocationImage { get; set; } = "";
    /// <summary>Croqui automático (imagem de satélite com o lote): devolve o caminho da imagem ou null.</summary>
    public Func<SheetSpec, IReadOnlyList<LotRecord>, string?>? Croqui { get; set; }
    public string NumberPrefix { get; set; } = "GP";
    public string Font { get; set; } = "Arial";
}

/// <summary>
/// Gera pranchas pelo modelo escolhido: cópia da planta recortada exatamente no mapa, grade de coordenadas atrás do desenho,
/// tabela num canto livre, norte, chamada da coordenada de referência de cada lote e os blocos do carimbo.
/// </summary>
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
    /// <summary>Pranchas mantidas (já existiam e foram preservadas).</summary>
    public int Kept { get; private set; }

    private static XYZ P(double xMm, double yMm) => new(xMm / Ft, yMm / Ft, 0);

    /// <summary>Cria todas as pranchas. Dentro de transação.</summary>
    public List<ViewSheet> Build(IReadOnlyList<SheetSpec> specs)
    {
        var sheets = new List<ViewSheet>();
        foreach (var spec in specs)
        {
            var old = SheetRegistry.Find(_s, spec.Key);
            if (old is not null && spec.Replace == SheetReplace.Manter) { Kept++; continue; }
            string? number = null;
            int folha = old?.Folha ?? 0;
            try
            {
                if (old is not null && spec.Replace == SheetReplace.Substituir)
                {
                    number = (ElementUtil.ByUniqueId(_doc, old.SheetId) as ViewSheet)?.SheetNumber;
                    SheetRegistry.Delete(_s, old);
                }
                if (folha <= 0) folha = _s.Project.Sheets.Select(g => g.Folha).DefaultIfEmpty(0).Max() + 1;
                var sheet = BuildOne(spec, number, folha);
                sheets.Add(sheet);
            }
            catch (Exception ex)
            {
                Logger.Error("Prancha " + spec.Title, ex);
                Warnings.Add($"{spec.Title}: {ex.Message}");
            }
        }
        return sheets;
    }

    /// <summary>Contexto com numeração própria de cada lote (P-01…), para as pranchas de um lote só.</summary>
    private ProjectContext LocalContext()
    {
        var st = _s.Settings.Clone();
        st.Naming.Numbering = VertexNumbering.Lote;
        return new ProjectContext(_s.Project, st);
    }

    /// <summary>Sistema das coordenadas da prancha: o primeiro do memorial (o mesmo das opções), ou local sem georreferência.</summary>
    private CrsDef TableCrs() => _ctx.Coords.HasGeoreference ? (_s.Settings.Memorial.CoordinateSystems.FirstOrDefault() ?? CrsDef.Utm()) : CrsDef.ProjectLocalDef();

    /// <summary>Chamada da coordenada de referência do lote (vértice escolhido ou P-01).</summary>
    public static RefCallout? Callout(LotReport rep, ProjectContext ctx, ValueFormatter fmt, CrsDef def)
    {
        if (rep.Vertices.Count == 0) return null;
        var lot = rep.Lot;
        var v = rep.Vertices[0];
        if (lot.RefX is double rx && lot.RefY is double ry)
        {
            var want = new Vec2(rx, ry);
            v = rep.Vertices.OrderBy(x => x.Internal.DistanceTo(want)).First();
            if (v.Internal.DistanceTo(want) > 0.05) v = rep.Vertices[0];
        }
        return new RefCallout(v.Internal, v.Name, SheetTexts.CalloutLines(ctx.Coords, fmt, def, v.Internal));
    }

    private ViewSheet BuildOne(SheetSpec spec, string? number, int folha)
    {
        var tpl = _o.Template;
        var st = tpl.Styles;
        var lots = spec.LotIds.Select(_s.Project.FindLot).Where(l => l is not null).Select(l => l!).ToList();
        if (lots.Count == 0) throw new InvalidOperationException("Nenhum lote para a prancha.");
        var local = LocalContext();
        // Prancha de um lote: numeração própria (P-01…). Com vários lotes: visualização (sem vértices nem azimutes).
        var ctx = spec.Detailed ? local : _ctx;
        var def = TableCrs();
        var map = tpl.MapRect();

        // ---- Tabela dentro do mapa: coordenadas (um lote) ou quadro de áreas (vários lotes).
        SheetTable? table = null;
        if (tpl.TablesInsideMap)
        {
            try
            {
                if (spec.Detailed && spec.CoordTable) table = SheetTable.Coordinates(local.Report(lots[0]), local, _fmt, def, st);
                else if (spec.QuadroIds.Count > 0)
                    table = SheetTable.Lots(spec.QuadroIds.Select(_s.Project.FindLot).Where(l => l is not null).Select(l => l!).ToList(), _ctx, _fmt, st);
            }
            catch (Exception ex) { Logger.Error("Tabela da prancha", ex); }
            if (table is not null && !table.FitInto(map.Width * 0.8, map.Height * 0.5))
            {
                Warnings.Add($"{spec.Title}: a tabela ({table.Rows.Count} linhas) não cabe na folha; ela continua no memorial e em 'Tabela de Coordenadas'.");
                table = null;
            }
            else if (table is not null && table.TextMm < st.TabelaMm - 1e-9)
                Warnings.Add($"{spec.Title}: o texto da tabela foi reduzido para {table.TextMm:0.0} mm para caber.");
        }
        var northRect = SheetRenderer.NorthRect(tpl);

        // ---- Vista: cópia da planta original (o desenho original não é alterado).
        var view = DuplicateView(spec, lots);
        var regions = lots.Select(l => l.ToRegion()).ToList();
        var crop = view.CropBox;
        var inv = crop.Transform.Inverse;
        var local3 = regions.SelectMany(r => r.AllRings().SelectMany(g => g.ToPolygon(0.05))).Select(p => inv.OfPoint(RvtUnits.ToXyz(p, 0))).ToList();
        double minX = local3.Min(p => p.X), maxX = local3.Max(p => p.X), minY = local3.Min(p => p.Y), maxY = local3.Max(p => p.Y);
        double wM = (maxX - minX) * RvtUnits.FtToM, hM = (maxY - minY) * RvtUnits.FtToM;
        double marginMm = tpl.DrawingMarginMm + (_o.Vias && _s.Project.Vias.Count > 0 ? 8 : 0);
        var fit = MapFit.Fit(map, wM, hM, marginMm, table is null ? null : (table.Width, table.Height), northRect, tpl.TableCorner, tpl.ScaleList, _o.Scale);
        if (!fit.Fits)
            Warnings.Add(_o.Scale > 0
                ? $"{spec.Title}: na escala 1:{fit.Scale} o desenho não cabe livre de tabela/norte; use 'Automática' ou outra escala."
                : $"{spec.Title}: o desenho não coube folgado nem em 1:{fit.Scale}; confira a prancha.");
        int scale = fit.Scale;
        view.Scale = scale;
        double k = scale / Ft;   // pés no modelo por mm no papel
        double cx = (minX + maxX) / 2 + (map.Center.X - fit.DrawingCenter.X) * k;
        double cy = (minY + maxY) / 2 + (map.Center.Y - fit.DrawingCenter.Y) * k;
        view.CropBoxActive = true;
        view.CropBox = new BoundingBoxXYZ
        {
            Transform = crop.Transform,
            Min = new XYZ(cx - map.Width / 2 * k, cy - map.Height / 2 * k, crop.Min.Z),
            Max = new XYZ(cx + map.Width / 2 * k, cy + map.Height / 2 * k, crop.Max.Z),
        };
        try { view.CropBoxVisible = false; } catch { }
        try { view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.Set(0); } catch { }
        // Papel (mm) → modelo (m).
        var tr = crop.Transform;
        Vec2 ToModel(Vec2 q) => RvtUnits.ToVec(tr.OfPoint(new XYZ(cx + (q.X - map.Center.X) * k, cy + (q.Y - map.Center.Y) * k, 0)));
        double viewAngle = Annotator.ViewAngle(view);
        double mPerMm = scale / 1000.0;
        Obb Obst(PaperRect r) => Obb.Of(ToModel(r.Center), viewAngle, r.Width / 2 * mPerMm, r.Height / 2 * mPerMm);

        // ---- Prancha (o número entra no carimbo).
        var sheet = CreateSheet(spec, number);
        _created.Add(sheet.UniqueId);
        bool customTitleBlock = !string.IsNullOrWhiteSpace(_s.Settings.Sheets.TitleBlock) && TitleBlockId() != ElementId.InvalidElementId;

        // ---- Conteúdo do carimbo e elementos da folha.
        // Edificações dos lotes da prancha (área edificada).
        var buildings = new List<GeoPoint.Core.Urban.BuildingReport>();
        foreach (var l in lots)
            foreach (var b in GeoPoint.Core.Urban.BuildingAnalyzer.Of(_s.Project, l))
                try { buildings.Add(GeoPoint.Core.Urban.BuildingAnalyzer.Analyze(ctx.Report(l), b)); } catch { }
        var content = SheetContent.Build(_ctx, _fmt, spec, lots, def, vias: _o.Vias && _s.Project.Vias.Count > 0, edificacoes: buildings.Count > 0);
        var images = new HashSet<string>();
        string? croqui = null;
        try { croqui = _o.Croqui?.Invoke(spec, lots); } catch (Exception ex) { Logger.Error("Croqui automático", ex); Warnings.Add($"{spec.Title}: croqui automático – {ex.Message}"); }
        if (string.IsNullOrWhiteSpace(croqui) || !File.Exists(croqui)) croqui = File.Exists(_o.LocationImage) ? _o.LocationImage : null;
        if (croqui is not null) images.Add("croqui");
        // Logomarca do escritório (embutida no modelo): gravada na pasta de cache para o Revit inserir.
        string? logo = null;
        try { logo = SheetTemplateLibrary.LogoFile(tpl); } catch (Exception ex) { Logger.Error("Logomarca", ex); Warnings.Add("Logomarca do escritório: " + ex.Message); }
        if (logo is not null) images.Add("logo");
        var northDir = NorthOnPaper(view);
        bool geo = def.Kind == CrsKind.Geographic;
        var prims = SheetRenderer.Render(new SheetRenderInput
        {
            Template = tpl, Content = content, Scale = scale, Folha = folha.ToString("00"), SheetNumber = sheet.SheetNumber, Date = DateTime.Today,
            CrsAt = q => _ctx.Coords.ToCrs(ToModel(q), def) is { } c ? (geo ? (c.Lon, c.Lat) : (c.East, c.North)) : null,
            Geographic = geo, WestAsO = _s.Settings.Format.WestAsO,
            Table = fit.Table is null ? null : table, TableRect = fit.Table, North = northDir, FrameAndBlocks = !customTitleBlock,
            Palette = Hatcher.Palette, Images = images, LogoAspect = logo is null ? 0 : tpl.LogoAspect(),
        });

        // ---- Conteúdo da vista. A grade vem primeiro (fica atrás das hachuras e do desenho).
        DrawGridInView(view, prims.OfType<PLine>().Where(l => l.Style == PLineStyle.Grade), ToModel, tpl.GridColor);
        if (_o.Vias && _s.Project.Vias.Count > 0)
            foreach (var w in StreetDrawer.Draw(_doc, view, _ctx, lots, _s.Settings.Streets, _o.Font)) Warnings.Add($"{spec.Title}: {w}");
        if (spec.Hatch)
            Hatcher.Apply(_doc, view, lots, l => spec.Colors.TryGetValue(l.Id, out int c) ? c : 0);
        double annoMm = st.DesenhoMm > 0 ? st.DesenhoMm : _s.Settings.Annotation.TextSizeMm;
        if (buildings.Count > 0)
            BuildingDrawer.Draw(_doc, view, buildings, _fmt, annoMm, string.IsNullOrWhiteSpace(st.Font) ? _o.Font : st.Font, setbacks: spec.Detailed);
        if (_o.Anotacoes)
        {
            var obstacles = new List<Obb> { Obst(northRect) };
            obstacles.AddRange(BuildingDrawer.LabelBoxes(buildings, view, annoMm, _fmt));
            if (fit.Table is PaperRect tbr) obstacles.Add(Obst(tbr));
            obstacles.AddRange(SheetRenderer.OutsideMap(map).Select(Obst));
            var fails = new List<string>();
            var ao = Annotator.Options.From(_s.Settings.Annotation);
            if (st.DesenhoMm > 0) ao.TextMm = st.DesenhoMm;
            ao.Font = string.IsNullOrWhiteSpace(st.Font) ? ao.Font : st.Font;
            ao.Edges = true;
            ao.Confrontantes = true;
            ao.Area = true;
            if (spec.Detailed) { ao.Vertices = true; ao.EdgeAngle = true; ao.Dots = true; }
            else { ao.Vertices = false; ao.EdgeAngle = false; ao.Dots = true; }   // geral: só os pontos dos vértices
            var callCtx = ctx;
            // Prancha geral (vários lotes): visão do conjunto, sem coordenadas de referência.
            Func<LotReport, RefCallout?>? refc = spec.Detailed ? r => Callout(r, callCtx, _fmt, def) : null;
            Annotator.Annotate(_doc, view, lots, ctx, _fmt, ao, fails, null, obstacles, refCallout: refc);
            Warnings.AddRange(fails.Select(f => $"{spec.Title}: {f}"));
            if (Annotator.LastConflicts > 0)
                Warnings.Add($"{spec.Title}: {Annotator.LastConflicts} texto(s) sem espaço livre (ficaram na posição de menor conflito) – confira e arraste se precisar.");
        }
        _doc.Regenerate();

        // ---- Viewport no mapa, alinhado com precisão (a grade e os valores das bordas dependem disso).
        if (!Viewport.CanAddViewToSheet(_doc, sheet.Id, view.Id)) throw new InvalidOperationException("A vista não pode ser colocada na prancha.");
        var vp = Viewport.Create(_doc, sheet.Id, view.Id, P(map.Center.X, map.Center.Y));
        NoTitle(vp);
        _doc.Regenerate();
        AlignViewport(vp, view, ToModel(map.Center), map.Center);

        // ---- Elementos da folha (tudo menos a grade, que está na vista).
        DrawPrims(sheet, prims.Where(x => x is not PLine { Style: PLineStyle.Grade }), croqui, logo);
        _doc.Regenerate();
        var g = SheetRegistry.Register(_s, spec, sheet, view);
        g.Folha = folha;
        return sheet;
    }

    /// <summary>Direção do norte (o mesmo dos azimutes) no papel, pela orientação da vista.</summary>
    private Vec2 NorthOnPaper(View view)
    {
        var n = _ctx.Coords.DirectionFromAzimuth(0);
        var right = view.RightDirection; var up = view.UpDirection;
        var dir = new Vec2(n.X * right.X + n.Y * right.Y, n.X * up.X + n.Y * up.Y);
        return dir.LengthSquared < 1e-9 ? new Vec2(0, 1) : dir.Normalized();
    }

    /// <summary>Move o viewport para o centro do recorte cair exatamente no centro do mapa.</summary>
    private void AlignViewport(Viewport vp, View view, Vec2 modelCenter, Vec2 paperCenter)
    {
        try
        {
            var tws = view.GetModelToProjectionTransforms();
            if (tws.Count == 0) return;
            var m2p = tws[0].GetModelToProjectionTransform();
            var p2s = vp.GetProjectionToSheetTransform();
            var at = p2s.OfPoint(m2p.OfPoint(RvtUnits.ToXyz(modelCenter, ElementUtil.ViewPlaneZ(view))));
            var delta = new XYZ(paperCenter.X / Ft - at.X, paperCenter.Y / Ft - at.Y, 0);
            if (delta.GetLength() * Ft > 0.05) vp.SetBoxCenter(vp.GetBoxCenter() + delta);
        }
        catch (Exception ex) { Logger.Error("Alinhar viewport", ex); }
    }

    /// <summary>Linhas da grade como linhas de detalhe na vista (atrás das hachuras e anotações).</summary>
    private void DrawGridInView(View view, IEnumerable<PLine> lines, Func<Vec2, Vec2> toModel, byte[] rgb)
    {
        var list = lines.ToList();
        if (list.Count == 0) return;
        var style = RevitStyles.LineStyle(_doc, "GeoPoint - Grade de coordenadas", new Color(rgb[0], rgb[1], rgb[2]), 1);
        double z = ElementUtil.ViewPlaneZ(view);
        foreach (var l in list)
        {
            try
            {
                var a = toModel(l.A); var b = toModel(l.B);
                if (a.DistanceTo(b) < 0.01) continue;
                var dc = _doc.Create.NewDetailCurve(view, Autodesk.Revit.DB.Line.CreateBound(RvtUnits.ToXyz(a, z), RvtUnits.ToXyz(b, z)));
                if (style is not null) dc.LineStyle = style;
            }
            catch (Exception ex) { Logger.Error("Grade de coordenadas", ex); }
        }
    }

    // ------------------------------------------------------------------ elementos da folha

    private readonly Dictionary<PLineStyle, GraphicsStyle?> _lineStyles = new();

    private GraphicsStyle? Style(PLineStyle s)
    {
        if (_lineStyles.TryGetValue(s, out var gs)) return gs;
        gs = s switch
        {
            PLineStyle.Moldura => RevitStyles.LineStyle(_doc, "GeoPoint - Prancha (moldura)", new Color(0, 0, 0), 5),
            PLineStyle.Bloco => RevitStyles.LineStyle(_doc, "GeoPoint - Prancha (blocos)", new Color(0, 0, 0), 3),
            PLineStyle.Papel => RevitStyles.LineStyle(_doc, "GeoPoint - Prancha (folha)", new Color(160, 160, 160), 1),
            PLineStyle.Tracejada => RevitStyles.LineStyle(_doc, "GeoPoint - Prancha (tracejada)", new Color(90, 90, 90), 1, RevitStyles.DashPattern(_doc, "GeoPoint tracejado curto", 1.0, 0.8)),
            PLineStyle.Vermelha => RevitStyles.LineStyle(_doc, "GeoPoint - Prancha (vermelha)", Annotator.Red, 2),
            PLineStyle.MeioFio => RevitStyles.LineStyle(_doc, "GeoPoint - Prancha (meio-fio)", new Color(100, 100, 100), 2),
            PLineStyle.Limite => RevitStyles.LineStyle(_doc, "GeoPoint - Prancha (limite)", new Color(0, 0, 0), 4),
            PLineStyle.Divisao => RevitStyles.LineStyle(_doc, Annotator.LineDivisao, Annotator.DivisaoColor, 4),
            PLineStyle.Grade => RevitStyles.LineStyle(_doc, "GeoPoint - Grade de coordenadas", new Color(175, 175, 175), 1),
            _ => RevitStyles.LineStyle(_doc, "GeoPoint - Prancha (linhas)", new Color(0, 0, 0), 1),
        };
        _lineStyles[s] = gs;
        return gs;
    }

    private void DrawPrims(ViewSheet sheet, IEnumerable<SheetPrim> prims, string? croqui, string? logo)
    {
        foreach (var pr in prims)
        {
            try
            {
                switch (pr)
                {
                    case PLine l:
                        Line(sheet, l.A, l.B, Style(l.Style));
                        break;
                    case PFill f:
                    {
                        if (f.Pts.Count < 3) break;
                        ElementId type = f.Hatch
                            ? Hatcher.TypeFor(_doc, Array.FindIndex(Hatcher.Palette.ToArray(), c => c.R == f.R && c.G == f.G && c.B == f.B) is int ix && ix >= 0 ? ix : 0, f.Cross, gray: f.R == f.G && f.G == f.B)
                            : RevitStyles.FillType(_doc, LotUse.Lote, $"GeoPoint - Sólido {f.R:X2}{f.G:X2}{f.B:X2}", (f.R, f.G, f.B));
                        var curves = new List<Curve>();
                        for (int i = 0; i < f.Pts.Count; i++)
                        {
                            var a = f.Pts[i]; var b = f.Pts[(i + 1) % f.Pts.Count];
                            if (a.DistanceTo(b) > 0.01) curves.Add(Autodesk.Revit.DB.Line.CreateBound(P(a.X, a.Y), P(b.X, b.Y)));
                        }
                        var fr = FilledRegion.Create(_doc, type, sheet.Id, new List<CurveLoop> { CurveLoop.Create(curves) });
                        var inv = RevitStyles.InvisibleLines(_doc);
                        if (inv != ElementId.InvalidElementId && FilledRegion.IsValidLineStyleIdForFilledRegion(_doc, inv)) fr.SetLineStyleId(inv);
                        break;
                    }
                    case PCircle c:
                        if (c.Filled)
                            Annotator.Dot(_doc, sheet, c.C * 0.001, c.Radius * 0.001, 0,
                                RevitStyles.FillType(_doc, LotUse.Lote, "GeoPoint - Ponto de vértice", (0, 0, 0)));
                        break;
                    case PText t:
                        Text(sheet, t);
                        break;
                    case PImage im:
                        string? path = im.Key == "croqui" ? croqui : im.Key == "logo" ? logo : null;
                        if (path is not null) PlaceImage(sheet, im.Rect, path);
                        break;
                }
            }
            catch (Exception ex) { Logger.Error("Elemento da prancha", ex); }
        }
    }

    /// <summary>Texto na prancha. O Revit insere pelo topo da caixa (no eixo do texto): converte o alinhamento pedido.</summary>
    private TextNote Text(View sheet, PText t)
    {
        var color = t.R == 0 && t.G == 0 && t.B == 0 ? null : new Color(t.R, t.G, t.B);
        var type = RevitStyles.TextType(_doc, Math.Round(t.SizeMm, 2), _o.Template.Styles.Font, false, t.Bold, color);
        var opts = new TextNoteOptions(type)
        {
            HorizontalAlignment = t.H switch { PTextH.Center => HorizontalTextAlignment.Center, PTextH.Right => HorizontalTextAlignment.Right, _ => HorizontalTextAlignment.Left },
            Rotation = t.Rotation,
            KeepRotatedTextReadable = false,
        };
        double boxH = 2 * RevitStyles.HalfHeightMm(t.SizeMm, t.LineCount);
        double up = t.V switch { PTextV.Middle => boxH / 2, PTextV.Bottom => boxH, _ => 0 };
        var dirUp = Vec2.FromAngle(t.Rotation).PerpLeft;
        var at = t.At + dirUp * up;
        return TextNote.Create(_doc, sheet.Id, P(at.X, at.Y), t.Text.Replace("\n", "\r"), opts);
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
                .Where(c => c.CurveElementType == CurveElementType.DetailCurve && !PhaseViews.IsGeoPointLine(c)).ToList();
            var allDiv = PhaseViews.AllLotRegions(_s.Project);
            var keep = detail.Where(c => PhaseViews.Keep(c, shownRegions) || !PhaseViews.OnBoundary(c, allDiv)).Select(c => c.Id).ToList();
            if (keep.Count > 0)
            {
                try { ElementTransformUtils.CopyElements(_source, keep, view, Transform.Identity, new CopyPasteOptions()); }
                catch (Exception ex) { Logger.Error("Copiar linhas de detalhe", ex); }
            }
        }
        // Linhas de modelo: internas aos lotes mostrados ficam ocultas (ex.: divisas do desmembramento na situação atual);
        // as que formam as divisas mostradas voltam a aparecer (ex.: ocultadas na vista da fase).
        var hide = new List<ElementId>();
        var unhide = new List<ElementId>();
        var divisas = PhaseViews.AllLotRegions(_s.Project);
        foreach (var c in new FilteredElementCollector(_doc).OfClass(typeof(CurveElement)).Cast<CurveElement>().Where(c => c.CurveElementType == CurveElementType.ModelCurve))
        {
            if (PhaseViews.IsGeoPointLine(c)) continue;
            bool hidden;
            try { hidden = c.IsHidden(view); } catch { continue; }
            if (!PhaseViews.Keep(c, shownRegions) && PhaseViews.OnBoundary(c, divisas)) { if (!hidden && c.CanBeHidden(view)) hide.Add(c.Id); }
            else if (hidden && PhaseViews.OnBoundary(c, shownRegions)) unhide.Add(c.Id);
        }
        try { if (hide.Count > 0) view.HideElements(hide); } catch (Exception ex) { Logger.Error("Ocultar linhas na prancha", ex); }
        try { if (unhide.Count > 0) view.UnhideElements(unhide); } catch (Exception ex) { Logger.Error("Mostrar linhas na prancha", ex); }
        CompleteBoundaries(view, lots);
        return view;
    }

    /// <summary>Divisas sem linha visível (linha apagada ou de outra fase) são desenhadas como linhas de detalhe na cópia.</summary>
    private void CompleteBoundaries(View view, List<LotRecord> lots)
    {
        try
        {
            var segs = new List<(Vec2 A, Vec2 B)>();
            foreach (var c in new FilteredElementCollector(_doc).OfClass(typeof(CurveElement)).Cast<CurveElement>())
            {
                bool relevant = c.CurveElementType == CurveElementType.ModelCurve || (c.CurveElementType == CurveElementType.DetailCurve && c.OwnerViewId == view.Id);
                if (!relevant || PhaseViews.IsGeoPointLine(c)) continue;
                try
                {
                    if (c.IsHidden(view)) continue;
                    var pts = c.GeometryCurve.Tessellate().Select(RvtUnits.ToVec).ToList();
                    for (int i = 0; i + 1 < pts.Count; i++) segs.Add((pts[i], pts[i + 1]));
                }
                catch { }
            }
            static double Dist(Vec2 p, Vec2 a, Vec2 b)
            {
                var ab = b - a; double l2 = ab.LengthSquared;
                double t = l2 < 1e-12 ? 0 : Math.Clamp((p - a).Dot(ab) / l2, 0, 1);
                return p.DistanceTo(a + ab * t);
            }
            var missing = new List<Curve2>();
            foreach (var lot in lots)
                foreach (var e in lot.Outer.Concat(lot.Holes.SelectMany(h => h)).Select(x => x.ToCurve()))
                {
                    bool covered = new[] { 0.25, 0.5, 0.75 }.All(t => { var p = e.PointAt(t); return segs.Any(sg => Dist(p, sg.A, sg.B) <= 0.01); });
                    if (!covered && !missing.Any(m => m.Mid.DistanceTo(e.Mid) < 0.001)) missing.Add(e);
                }
            if (missing.Count == 0) return;
            var style = RevitStyles.LineStyle(_doc, "GeoPoint - Divisa (prancha)", new Color(0, 0, 0), 3);
            double z = ElementUtil.ViewPlaneZ(view);
            foreach (var m in missing)
            {
                try
                {
                    var dc = _doc.Create.NewDetailCurve(view, RvtUnits.ToRevitCurve(m, z));
                    if (style is not null) dc.LineStyle = style;
                }
                catch { }
            }
            Logger.Info($"Prancha: {missing.Count} divisa(s) sem linha visível desenhada(s) na cópia da vista.");
        }
        catch (Exception ex) { Logger.Error("Completar divisas", ex); }
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

    private ViewSheet CreateSheet(SheetSpec spec, string? reuseNumber)
    {
        var tb = string.IsNullOrWhiteSpace(_s.Settings.Sheets.TitleBlock) ? ElementId.InvalidElementId : TitleBlockId();
        if (tb != ElementId.InvalidElementId && _doc.GetElement(tb) is FamilySymbol fs && !fs.IsActive) fs.Activate();
        var sheet = ViewSheet.Create(_doc, tb);
        var numbers = new FilteredElementCollector(_doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(v => v.SheetNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string prefix = string.IsNullOrWhiteSpace(_o.NumberPrefix) ? "GP" : _o.NumberPrefix.Trim();
        if (_s.Project.Fases.Count > 1) prefix += $"-F{_s.Project.Fase.Numero}";
        if (!string.IsNullOrWhiteSpace(reuseNumber) && !numbers.Contains(reuseNumber)) sheet.SheetNumber = reuseNumber;
        else
        {
            int k = 1;
            while (numbers.Contains($"{prefix}-{k:00}")) k++;
            sheet.SheetNumber = $"{prefix}-{k:00}";
        }
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

    private void PlaceImage(ViewSheet sheet, PaperRect r, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
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
            Logger.Error("Imagem da prancha", ex);
            Warnings.Add("Não foi possível inserir a imagem: " + ex.Message);
        }
    }

}

internal static class StringExt
{
    public static string Truncate(this string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
