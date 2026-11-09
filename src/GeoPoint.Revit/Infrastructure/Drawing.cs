using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Settings;
using GeoPoint.Core.Urban;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>Anotações dos lotes na vista: divisas, vértices (com ponto preenchido), área e extensão dos confrontantes.</summary>
public static class Annotator
{
    public static readonly Color Red = new(192, 0, 0);
    public const string LineConfrontante = "GeoPoint - Extensão do confrontante";
    public const string LineCotaConfrontante = "GeoPoint - Cota do confrontante";
    public const string LineChamada = "GeoPoint - Linha de chamada";

    public sealed class Options
    {
        public bool Vertices { get; set; } = true;
        public bool Edges { get; set; } = true;
        public bool Area { get; set; } = true;
        public bool EdgeAngle { get; set; } = true;
        public bool Dots { get; set; } = true;
        public bool Outside { get; set; } = true;
        public bool Confrontantes { get; set; } = true;
        public bool Opaque { get; set; } = true;
        public double TextMm { get; set; } = 2.0;
        public double OffsetMm { get; set; } = 1.5;
        public double DotMm { get; set; } = 1.2;
        public string Font { get; set; } = "Arial";

        public static Options From(AnnotationSettings a) => new()
        {
            Vertices = a.Vertices, Edges = a.Edges, Area = a.AreaLabel, EdgeAngle = a.EdgeAzimuth, Dots = a.VertexDots,
            Outside = a.TextOutside, Confrontantes = a.Confrontantes, Opaque = a.OpaqueText, TextMm = a.TextSizeMm, OffsetMm = a.OffsetMm,
            DotMm = a.DotMm, Font = a.Font,
        };
    }

    /// <summary>Remove anotações anteriores do lote nesta vista. Dentro de transação.</summary>
    public static void Clear(Document doc, LotRecord lot, View view)
    {
        foreach (var rec in lot.Annotations.Where(a => a.ViewId == view.UniqueId).ToList())
        {
            var ids = ElementUtil.ExistingIds(doc, rec.ElementIds);
            if (ids.Count > 0) doc.Delete(ids);
            lot.Annotations.Remove(rec);
        }
    }

    /// <summary>Remove anotações e hachuras do lote em todas as vistas (lote arquivado: o desenho mostra a situação nova). Dentro de transação.</summary>
    public static int ClearEverywhere(Document doc, LotRecord lot)
    {
        var uids = lot.Annotations.SelectMany(a => a.ElementIds).Concat(lot.Hatches.SelectMany(h => h.ElementIds)).ToList();
        var ids = ElementUtil.ExistingIds(doc, uids);
        foreach (var id in ids)
        {
            try { if (doc.GetElement(id) is not null) doc.Delete(id); } catch { }
        }
        lot.Annotations.Clear();
        lot.Hatches.Clear();
        return ids.Count;
    }

    /// <summary>Anota um conjunto de lotes de uma vez (divisas e vértices compartilhados uma vez só). Dentro de transação.</summary>
    public static int Annotate(Document doc, View view, IReadOnlyList<LotRecord> lots, ProjectContext ctx, ValueFormatter fmt, Options o, IList<string>? failures = null,
        Func<LotRecord, string>? ownerOf = null, IEnumerable<Obb>? obstacles = null)
    {
        foreach (var l in lots) Clear(doc, l, view);
        // O que outros lotes já anotaram nesta vista não é repetido.
        var existing = new HashSet<string>();
        foreach (var other in ctx.Project.PhaseLots.Where(l => !lots.Contains(l)))
            foreach (var rec in other.Annotations.Where(a => a.ViewId == view.UniqueId).ToList())
            {
                if (ElementUtil.ExistingIds(doc, rec.ElementIds).Count == 0) { other.Annotations.Remove(rec); continue; }
                foreach (var k in rec.Keys) existing.Add(k);
            }

        var reports = new List<LotReport>();
        foreach (var l in lots)
        {
            try { reports.Add(ctx.Report(l)); }
            catch (Exception ex) { failures?.Add($"{l.DisplayName}: {ex.Message}"); }
        }
        double scale = Math.Max(1, view.Scale);
        double textM = o.TextMm / 1000.0 * scale;
        var plan = AnnotationPlanner.Plan(reports, fmt, new AnnotationPlanOptions
        {
            Edges = o.Edges, EdgeAngle = o.EdgeAngle, Vertices = o.Vertices, Area = o.Area, Dots = o.Dots, Outside = o.Outside,
            Confrontantes = o.Confrontantes, TextM = textM, OffsetM = o.OffsetMm / 1000.0 * scale, DotRadiusM = o.DotMm / 2000.0 * scale,
            PadM = RevitStyles.TextPadMm / 1000.0 * scale, LineFactor = RevitStyles.LineFactor,
            OwnerOf = ownerOf, Obstacles = obstacles?.ToList() ?? new List<Obb>(),
        }, existing);
        if (plan.Conflicts > 0) Logger.Info($"Anotação: {plan.Conflicts} texto(s) sem espaço livre na vista {view.Name} (posição de menor conflito).");

        var normal = RevitStyles.TextType(doc, o.TextMm, o.Font, o.Opaque);
        var label = RevitStyles.TextType(doc, Math.Round(o.TextMm * 1.25, 2), o.Font, o.Opaque, bold: true);
        var red = RevitStyles.TextType(doc, o.TextMm, o.Font, o.Opaque, color: Red);
        var dotType = plan.Dots.Count > 0 ? RevitStyles.FillType(doc, LotUse.Lote, "GeoPoint - Ponto de vértice", (0, 0, 0)) : ElementId.InvalidElementId;
        GraphicsStyle? tickStyle = null, dimStyle = null, leaderStyle = null;
        if (plan.Lines.Count > 0)
        {
            var dash = RevitStyles.DashPattern(doc, "GeoPoint tracejado", 1.5, 1.0);
            tickStyle = RevitStyles.LineStyle(doc, LineConfrontante, Red, 1, dash);
            dimStyle = RevitStyles.LineStyle(doc, LineCotaConfrontante, Red, 1);
            leaderStyle = RevitStyles.LineStyle(doc, LineChamada, new Color(90, 90, 90), 1);
        }
        var byLot = lots.ToDictionary(l => l.Id, l => new List<string>());
        double zDefault = ElementUtil.ViewPlaneZ(view);
        double Z(string owner)
        {
            var lot = lots.FirstOrDefault(l => l.Id == owner);
            return lot is not null && lot.SourceKind == "DetailLine" ? lot.ZFeet : zDefault;
        }

        foreach (var t in plan.Texts)
        {
            try
            {
                var type = t.Kind switch { AnnoTextKind.LotLabel => label, AnnoTextKind.Confrontante or AnnoTextKind.Measure => red, _ => normal };
                // O Revit posiciona o texto centralizado pelo meio da borda SUPERIOR da caixa; a rotação já vem legível.
                var opts = new TextNoteOptions(type)
                {
                    HorizontalAlignment = HorizontalTextAlignment.Center,
                    Rotation = t.Rotation,
                    KeepRotatedTextReadable = false,
                };
                var tn = TextNote.Create(doc, view.Id, RvtUnits.ToXyz(t.TopCenter, Z(t.Owner)), t.Text.Replace("\n", "\r"), opts);
                byLot[t.Owner].Add(tn.UniqueId);
            }
            catch (Exception ex) { Logger.Error("Texto de anotação", ex); }
        }
        foreach (var d in plan.Dots)
        {
            try
            {
                var fr = Dot(doc, view, d.At, d.Radius, Z(d.Owner), dotType);
                if (fr is not null) byLot[d.Owner].Add(fr.UniqueId);
            }
            catch (Exception ex) { Logger.Error("Ponto do vértice", ex); }
        }
        foreach (var ln in plan.Lines)
        {
            try
            {
                if (ln.A.DistanceTo(ln.B) < 0.002) continue;
                var dc = doc.Create.NewDetailCurve(view, Line.CreateBound(RvtUnits.ToXyz(ln.A, Z(ln.Owner)), RvtUnits.ToXyz(ln.B, Z(ln.Owner))));
                var st = ln.Kind switch { AnnoLineKind.ConfrontanteTick => tickStyle, AnnoLineKind.Leader => leaderStyle, _ => dimStyle };
                if (st is not null) dc.LineStyle = st;
                byLot[ln.Owner].Add(dc.UniqueId);
            }
            catch (Exception ex) { Logger.Error("Linha de confrontante", ex); }
        }
        int total = 0;
        foreach (var l in lots)
        {
            var ids = byLot[l.Id];
            total += ids.Count;
            if (ids.Count > 0) l.Annotations.Add(new AnnotationRecord { ViewId = view.UniqueId, ElementIds = ids, Keys = plan.KeysOf(l.Id).ToList() });
        }
        return total;
    }

    /// <summary>Círculo preenchido (ponto do vértice).</summary>
    public static FilledRegion? Dot(Document doc, View view, Vec2 at, double radiusM, double z, ElementId typeId)
    {
        double r = RvtUnits.MToFt(radiusM);
        if (r < 1.0 / 256) r = 1.0 / 256;
        var c = RvtUnits.ToXyz(at, z);
        var a1 = Arc.Create(c, r, 0, Math.PI, XYZ.BasisX, XYZ.BasisY);
        var a2 = Arc.Create(c, r, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY);
        var loop = CurveLoop.Create(new List<Curve> { a1, a2 });
        return FilledRegion.Create(doc, typeId, view.Id, new List<CurveLoop> { loop });
    }
}

/// <summary>Hachuras dos lotes (cores distintas; lote remanescente com hachura cruzada).</summary>
public static class Hatcher
{
    public static readonly (byte R, byte G, byte B)[] Palette =
    {
        (220, 60, 50), (40, 90, 210), (30, 150, 80), (225, 140, 20), (140, 60, 190), (20, 150, 170), (200, 50, 130), (110, 110, 30),
    };

    public static void Clear(Document doc, LotRecord lot, View view)
    {
        foreach (var rec in lot.Hatches.Where(a => a.ViewId == view.UniqueId).ToList())
        {
            var ids = ElementUtil.ExistingIds(doc, rec.ElementIds);
            if (ids.Count > 0) doc.Delete(ids);
            lot.Hatches.Remove(rec);
        }
    }

    /// <summary>Cria as hachuras. <paramref name="colorIndex"/> define a cor de cada lote. Dentro de transação.</summary>
    public static int Apply(Document doc, View view, IReadOnlyList<LotRecord> lots, Func<LotRecord, int> colorIndex, double spacingMm = 1.6, bool invisibleBoundary = true)
    {
        var simple = RevitStyles.HatchPattern(doc, $"GeoPoint hachura 45° {spacingMm:0.0} mm", 45, spacingMm, false);
        var cross = RevitStyles.HatchPattern(doc, $"GeoPoint hachura cruzada {spacingMm:0.0} mm", 45, spacingMm * 1.4, true);
        var invisible = invisibleBoundary ? RevitStyles.InvisibleLines(doc) : ElementId.InvalidElementId;
        double zDefault = ElementUtil.ViewPlaneZ(view);
        int n = 0;
        foreach (var lot in lots)
        {
            Clear(doc, lot, view);
            Region region;
            try { region = lot.ToRegion(); } catch { continue; }
            var (r, g, b) = Palette[Math.Abs(colorIndex(lot)) % Palette.Length];
            bool rem = lot.IsRemanescente;
            string name = $"GeoPoint - Hachura {(rem ? "remanescente " : "")}{Math.Abs(colorIndex(lot)) % Palette.Length + 1}";
            var type = RevitStyles.HatchType(doc, name, rem ? cross : simple, new Color(r, g, b), true);
            double z = lot.SourceKind == "DetailLine" ? lot.ZFeet : zDefault;
            var loops = new List<CurveLoop> { RvtUnits.ToCurveLoop(region.Outer, z) };
            foreach (var h in region.Holes) loops.Add(RvtUnits.ToCurveLoop(h, z));
            try
            {
                var fr = FilledRegion.Create(doc, type, view.Id, loops);
                if (invisible != ElementId.InvalidElementId && FilledRegion.IsValidLineStyleIdForFilledRegion(doc, invisible)) fr.SetLineStyleId(invisible);
                lot.Hatches.Add(new AnnotationRecord { ViewId = view.UniqueId, ElementIds = { fr.UniqueId } });
                n++;
            }
            catch (Exception ex) { Logger.Error("Hachura de " + lot.DisplayName, ex); }
        }
        return n;
    }
}

/// <summary>Desenho esquemático das vias (meio-fio, alinhamento e nome da rua) numa vista.</summary>
public static class StreetDrawer
{
    public const string LineMeioFio = "GeoPoint - Meio-fio";
    public const string LineAlinhamento = "GeoPoint - Alinhamento (via)";

    public static List<StreetFrontage> Frontages(ProjectContext ctx, IEnumerable<LotRecord> lots)
    {
        var res = new List<StreetFrontage>();
        foreach (var lot in lots)
        {
            LotReport rep;
            try { rep = ctx.Report(lot); } catch { continue; }
            foreach (var s in rep.Segments.Where(s => s.ConfrontanteViaId.Length > 0))
                res.Add(new StreetFrontage(s.ConfrontanteViaId, s.Curve, s.OutwardNormal));
        }
        return res;
    }

    public static void Clear(Document doc, ParcelProject p, View view)
    {
        foreach (var rec in p.StreetDrawings.Where(a => a.ViewId == view.UniqueId).ToList())
        {
            var ids = ElementUtil.ExistingIds(doc, rec.ElementIds);
            if (ids.Count > 0) doc.Delete(ids);
            p.StreetDrawings.Remove(rec);
        }
    }

    /// <summary>Desenha as vias. Dentro de transação. Devolve avisos.</summary>
    public static List<string> Draw(Document doc, View view, ProjectContext ctx, IReadOnlyList<LotRecord> lots, StreetSettings st, string font)
    {
        var p = ctx.Project;
        Clear(doc, p, view);
        var frontages = Frontages(ctx, lots);
        var regions = p.ActiveLots.Select(l => { try { return l.ToRegion(); } catch { return null; } }).Where(r => r is not null).Select(r => r!).ToList();
        var area = BBox.Empty;
        foreach (var l in lots) { try { area = area.Union(l.ToRegion().Bounds); } catch { } }
        area = area.IsEmpty ? area : area.Expand(40);
        var vias = p.Vias.Where(v => frontages.Any(f => f.ViaId == v.Id)
                                     || v.MeioFio.Any(m => m.Any(pt => !area.IsEmpty && area.Contains(pt.ToVec())))).ToList();
        if (vias.Count == 0) return new List<string> { "Nenhuma divisa está associada a uma via (marque a testada e escolha a via em 'Vias')." };
        var crossings = p.Cruzamentos.Where(c => c.ViaIds.Count(id => vias.Any(v => v.Id == id)) >= 2).ToList();
        var drawing = StreetSchematic.Build(vias, crossings, frontages, regions);
        var meioFio = RevitStyles.LineStyle(doc, LineMeioFio, new Color(60, 60, 60), 2);
        var alinh = RevitStyles.LineStyle(doc, LineAlinhamento, new Color(110, 110, 110), 1);
        var textType = RevitStyles.TextType(doc, st.NomeMm, font, false, bold: true);
        double z = lots.FirstOrDefault()?.SourceKind == "DetailLine" ? lots[0].ZFeet : ElementUtil.ViewPlaneZ(view);
        var ids = new List<string>();
        void Poly(List<Vec2> pts, GraphicsStyle? style)
        {
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                if (pts[i].DistanceTo(pts[i + 1]) < 0.003) continue;
                try
                {
                    var dc = doc.Create.NewDetailCurve(view, Line.CreateBound(RvtUnits.ToXyz(pts[i], z), RvtUnits.ToXyz(pts[i + 1], z)));
                    if (style is not null) dc.LineStyle = style;
                    ids.Add(dc.UniqueId);
                }
                catch { }
            }
        }
        foreach (var c in drawing.Curbs) Poly(c, meioFio);
        foreach (var a in drawing.Alignments) Poly(a, alinh);
        foreach (var l in drawing.Labels)
        {
            try
            {
                double ang = AnnotationPlanner.Readable(l.AngleRad);
                double hh = RevitStyles.HalfHeightMm(st.NomeMm) / 1000.0 * Math.Max(1, view.Scale);
                var top = l.Position + Vec2.FromAngle(ang).PerpLeft * hh;
                var tn = TextNote.Create(doc, view.Id, RvtUnits.ToXyz(top, z), l.Text.ToUpperInvariant(), new TextNoteOptions(textType)
                {
                    HorizontalAlignment = HorizontalTextAlignment.Center, Rotation = ang, KeepRotatedTextReadable = false,
                });
                ids.Add(tn.UniqueId);
            }
            catch { }
        }
        p.StreetDrawings.Add(new AnnotationRecord { ViewId = view.UniqueId, ElementIds = ids });
        return drawing.Warnings;
    }
}
