using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Settings;

namespace GeoPoint.Core.Sheets;

/// <summary>Prancha montada fora do Revit (prévia): mesmo encaixe, anotações e carimbo que o Revit vai gerar.</summary>
public sealed class SheetComposition
{
    public SheetRenderInput Input { get; init; } = null!;
    /// <summary>Elementos da folha (carimbo, grade, tabela, norte).</summary>
    public List<SheetPrim> Sheet { get; init; } = new();
    /// <summary>Conteúdo do mapa já no papel (lotes, anotações, chamadas).</summary>
    public List<SheetPrim> Map { get; init; } = new();
    public AnnotationPlan Plan { get; init; } = new();
    public MapFitResult Fit { get; init; } = null!;
    public Func<Vec2, Vec2> ToPaper { get; init; } = p => p;
    public List<string> Warnings { get; } = new();

    /// <summary>Tudo na ordem de desenho: grade, mapa, folha.</summary>
    public IEnumerable<SheetPrim> All =>
        Sheet.Where(p => p is PLine { Style: PLineStyle.Grade }).Concat(Map).Concat(Sheet.Where(p => p is not PLine { Style: PLineStyle.Grade }));
}

public static class SheetPreview
{
    /// <summary>
    /// Monta a prancha de <paramref name="lots"/> pelo modelo, com o norte para cima (como o Revit faz com "norte para cima").
    /// <paramref name="annoTextMm"/> = altura dos textos do desenho no papel.
    /// </summary>
    public static SheetComposition Compose(ProjectContext ctx, IReadOnlyList<LotRecord> lots, SheetSpec spec, SheetTemplate tpl,
        double annoTextMm = 2.0, int fixedScale = 0, IReadOnlyList<(byte R, byte G, byte B)>? palette = null, Func<LotReport, string>? labelOf = null)
    {
        palette ??= new[] { ((byte)220, (byte)60, (byte)50), ((byte)40, (byte)90, (byte)210), ((byte)30, (byte)150, (byte)80), ((byte)225, (byte)140, (byte)20) };
        var fmt = new ValueFormatter(ctx.Settings.Format);
        var localSt = ctx.Settings.Clone();
        localSt.Naming.Numbering = VertexNumbering.Lote;
        var local = new ProjectContext(ctx.Project, localSt);
        var actx = spec.Detailed ? local : ctx;
        var def = ctx.Coords.HasGeoreference ? (ctx.Settings.Memorial.CoordinateSystems.FirstOrDefault() ?? CrsDef.Utm()) : CrsDef.ProjectLocalDef();
        var reps = lots.Select(actx.Report).ToList();
        var map = tpl.MapRect();
        // O desenho fica com o norte do GeoPoint para cima: gira as coordenadas pela convergência/rotação do projeto.
        double rot = ctx.Coords.Rotation;
        Vec2 R(Vec2 p) => p.Rotate(rot);
        var bb = BBox.Of(reps.SelectMany(r => r.Region.AllRings().SelectMany(g => g.ToPolygon(0.05))).Select(R));
        SheetTable? table = null;
        if (tpl.TablesInsideMap)
        {
            if (spec.Detailed && spec.CoordTable) table = SheetTable.Coordinates(reps[0], local, fmt, def, tpl.Styles);
            else if (spec.QuadroIds.Count > 0) table = SheetTable.Lots(spec.QuadroIds.Select(ctx.Project.FindLot).Where(l => l is not null).Select(l => l!).ToList(), ctx, fmt, tpl.Styles);
            if (table is not null && !table.FitInto(map.Width * 0.8, map.Height * 0.5)) table = null;
        }
        var northRect = SheetRenderer.NorthRect(tpl);
        var fit = MapFit.Fit(map, bb.Width, bb.Height, tpl.DrawingMarginMm, table is null ? null : (table.Width, table.Height), northRect, tpl.TableCorner, tpl.ScaleList, fixedScale);
        double k = 1000.0 / fit.Scale;     // mm no papel por metro
        var c = bb.Center;
        Vec2 ToPaper(Vec2 p) => fit.DrawingCenter + (R(p) - c) * k;
        Vec2 ToModel(Vec2 q) => (c + (q - fit.DrawingCenter) / k).Rotate(-rot);
        double ang = -rot;                  // horizontal do papel no desenho
        var obstacles = new List<Obb>();
        void Obst(PaperRect r) => obstacles.Add(Obb.Of(ToModel(r.Center), ang, r.Width / 2 / k, r.Height / 2 / k));
        Obst(northRect);
        if (fit.Table is PaperRect tr) Obst(tr);
        foreach (var r in SheetRenderer.OutsideMap(map)) Obst(r);
        double textM = annoTextMm / k;
        var o = new AnnotationPlanOptions
        {
            TextM = textM, OffsetM = 1.2 / k, DotRadiusM = 0.6 / k, PadM = 0.4 / k, LineFactor = 1.5, Obstacles = obstacles, ViewAngle = ang,
            Vertices = spec.Detailed, EdgeAngle = spec.Detailed, Dots = spec.Detailed, Edges = true, Confrontantes = true, Area = true,
            LabelOf = labelOf ?? (r => DefaultLabel(r, ctx, fmt)),
            PerLotVertexNames = lots.Count > 1,
            // Prancha geral (vários lotes) é visão do conjunto: sem coordenadas de referência.
            RefCallout = spec.Detailed ? r => r.Vertices.Count == 0 ? null : RefFor(r, actx, fmt, def) : null,
        };
        var plan = AnnotationPlanner.Plan(reps, fmt, o);
        var content = SheetContent.Build(ctx, fmt, spec, lots, def);
        bool geo = def.Kind == CrsKind.Geographic;
        var input = new SheetRenderInput
        {
            Template = tpl, Content = content, Scale = fit.Scale, Folha = "01",
            CrsAt = q => ctx.Coords.ToCrs(ToModel(q), def) is { } cc ? (geo ? (cc.Lon, cc.Lat) : (cc.East, cc.North)) : null,
            Geographic = geo, WestAsO = ctx.Settings.Format.WestAsO,
            Table = fit.Table is null ? null : table, TableRect = fit.Table, Palette = palette,
        };
        var mapPrims = new List<SheetPrim>();
        for (int i = 0; i < reps.Count; i++)
        {
            int ci = spec.Colors.TryGetValue(reps[i].Lot.Id, out int cc2) ? cc2 : i;
            var col = palette[Math.Abs(ci) % palette.Count];
            foreach (var ring in reps[i].Region.AllRings())
            {
                var pts = ring.ToPolygon(0.05).Select(ToPaper).ToList();
                if (spec.Hatch || reps.Count == 1) mapPrims.Add(new PFill(pts, col.R, col.G, col.B, Hatch: true, Cross: reps[i].Lot.IsRemanescente));
                for (int j = 0; j < pts.Count; j++) mapPrims.Add(new PLine(pts[j], pts[(j + 1) % pts.Count], PLineStyle.Limite));
            }
        }
        foreach (var l in plan.Lines)
            mapPrims.Add(new PLine(ToPaper(l.A), ToPaper(l.B), l.Kind switch
            {
                AnnoLineKind.ConfrontanteTick => PLineStyle.Tracejada,
                AnnoLineKind.ConfrontanteDimension => PLineStyle.Vermelha,
                _ => PLineStyle.Fina,
            }));
        foreach (var d in plan.Dots) mapPrims.Add(new PCircle(ToPaper(d.At), d.Radius * k, true));
        foreach (var t in plan.Texts)
        {
            bool red = t.Kind is AnnoTextKind.Confrontante or AnnoTextKind.Measure;
            double size = t.Kind == AnnoTextKind.LotLabel ? annoTextMm * 1.1 * t.SizeFactor : annoTextMm;
            var lines = t.Text.Split('\n');
            // Primeira linha em negrito (rótulo do lote e nome do ponto de referência): uma linha por texto.
            var up = Vec2.FromAngle(t.Rotation + rot).PerpLeft;
            double lh = size * 1.5;
            var top = ToPaper(t.At) + up * (lines.Length * lh / 2);
            for (int i = 0; i < lines.Length; i++)
                mapPrims.Add(new PText(top - up * (i * lh + lh / 2), lines[i], size, t.BoldFirstLine && i < t.BoldLines, PTextH.Center, PTextV.Middle,
                    t.Rotation + rot, (byte)(red ? 192 : 0), 0, 0));
        }
        var comp = new SheetComposition
        {
            Input = input, Sheet = SheetRenderer.Render(input), Map = mapPrims, Plan = plan, Fit = fit, ToPaper = ToPaper,
        };
        if (!fit.Fits) comp.Warnings.Add($"O desenho não coube folgado; escala 1:{fit.Scale}.");
        if (plan.Conflicts > 0) comp.Warnings.Add($"{plan.Conflicts} texto(s) sem espaço livre.");
        return comp;
    }

    private static RefCallout RefFor(LotReport r, ProjectContext ctx, ValueFormatter fmt, CrsDef def)
    {
        var v = r.Vertices[0];
        if (r.Lot.RefX is double rx && r.Lot.RefY is double ry)
        {
            var want = new Vec2(rx, ry);
            var near = r.Vertices.OrderBy(x => x.Internal.DistanceTo(want)).First();
            if (near.Internal.DistanceTo(want) <= 0.05) v = near;
        }
        return new RefCallout(v.Internal, v.Name, SheetTexts.CalloutLines(ctx.Coords, fmt, def, v.Internal));
    }

    /// <summary>Rótulo padrão do lote: nome, proprietário, matrícula, área e perímetro.</summary>
    public static string DefaultLabel(LotReport rep, ProjectContext ctx, ValueFormatter fmt)
    {
        var lot = rep.Lot;
        var info = ctx.Project.Info;
        var a = ctx.Settings.Annotation;
        var lines = new List<string> { lot.IsRemanescente ? lot.DisplayName.ToUpperInvariant() : lot.DisplayName };
        string owner = ctx.Project.EffectiveOwner(lot);
        if (a.LabelOwner && !string.IsNullOrWhiteSpace(owner)) lines.Add(owner.Trim().ToUpperInvariant());
        string m = ctx.Project.EffectiveMatricula(lot);
        if (a.LabelMatricula && !string.IsNullOrWhiteSpace(m)) lines.Add("MATRÍCULA: " + m.Trim());
        lines.Add("ÁREA = " + fmt.Area(rep.AreaLocal));
        if (a.LabelPerimeter) lines.Add("PERÍMETRO = " + fmt.Distance(rep.PerimeterLocal));
        return string.Join("\n", lines);
    }
}
