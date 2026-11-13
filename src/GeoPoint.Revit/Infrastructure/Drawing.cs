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
    /// <summary>Divisória no ponto em que o confrontante muda (linha contínua e forte).</summary>
    public const string LineDivisao = "GeoPoint - Divisão de confrontante";
    public static readonly Color DivisaoColor = new(170, 0, 0);

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
        /// <summary>Rótulo do lote: proprietário, matrícula e perímetro além do nome e da área.</summary>
        public bool LabelOwner { get; set; } = true;
        public bool LabelMatricula { get; set; } = true;
        public bool LabelPerimeter { get; set; } = true;

        public static Options From(AnnotationSettings a) => new()
        {
            Vertices = a.Vertices, Edges = a.Edges, Area = a.AreaLabel, EdgeAngle = a.EdgeAzimuth, Dots = a.VertexDots,
            Outside = a.TextOutside, Confrontantes = a.Confrontantes, Opaque = a.OpaqueText, TextMm = a.TextSizeMm, OffsetMm = a.OffsetMm,
            DotMm = a.DotMm, Font = a.Font, LabelOwner = a.LabelOwner, LabelMatricula = a.LabelMatricula, LabelPerimeter = a.LabelPerimeter,
        };
    }

    /// <summary>Ângulo (rad) da horizontal da vista no desenho (vistas giradas: norte verdadeiro, recorte girado).</summary>
    public static double ViewAngle(View v)
    {
        try { var r = v.RightDirection; return Math.Atan2(r.Y, r.X); } catch { return 0; }
    }

    /// <summary>Rótulo do lote: nome; proprietário; matrícula; área; perímetro (sem CPF). Mesmo texto das pranchas (<see cref="LotLabels"/>).</summary>
    public static string LotLabel(LotReport rep, ProjectContext ctx, ValueFormatter fmt, Options o) =>
        LotLabels.Text(rep, ctx.Project, fmt, o.LabelOwner, o.LabelMatricula, o.LabelPerimeter);

    /// <summary>
    /// Opções do planejador de textos para a vista (as mesmas na prévia da janela Anotar e na anotação de verdade).
    /// </summary>
    public static AnnotationPlanOptions PlanOptions(Document doc, View view, ProjectContext ctx, ValueFormatter fmt, Options o, int lotCount,
        Func<LotRecord, string>? ownerOf = null, IEnumerable<Obb>? obstacles = null, bool? perLotNames = null, Func<LotReport, RefCallout?>? refCallout = null)
    {
        double scale = Math.Max(1, view.Scale);
        return new AnnotationPlanOptions
        {
            Edges = o.Edges, EdgeAngle = o.EdgeAngle, Vertices = o.Vertices, Area = o.Area, Dots = o.Dots, Outside = o.Outside,
            Confrontantes = o.Confrontantes, TextM = o.TextMm / 1000.0 * scale, OffsetM = o.OffsetMm / 1000.0 * scale, DotRadiusM = o.DotMm / 2000.0 * scale,
            PadM = RevitStyles.TextPadMm / 1000.0 * scale, LineFactor = RevitStyles.LineFactor,
            OwnerOf = ownerOf, Obstacles = obstacles?.ToList() ?? new List<Obb>(),
            ViewAngle = ViewAngle(view),
            LabelOf = r => LotLabel(r, ctx, fmt, o),
            // Numeração por lote com vários lotes: cada lote mostra os seus números (dentro dele).
            PerLotVertexNames = perLotNames ?? (ctx.Settings.Naming.Numbering == VertexNumbering.Lote && lotCount > 1),
            // Rua desenhada nesta vista: o nome da via já aparece na rua.
            SkipViaNames = ctx.Project.StreetDrawings.Any(d => d.ViewId == view.UniqueId && ElementUtil.ExistingIds(doc, d.ElementIds).Count > 0),
            RefCallout = refCallout,
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
        Func<LotRecord, string>? ownerOf = null, IEnumerable<Obb>? obstacles = null, bool? perLotNames = null, Func<LotReport, RefCallout?>? refCallout = null)
    {
        LastConflicts = 0;
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
        var plan = AnnotationPlanner.Plan(reports, fmt, PlanOptions(doc, view, ctx, fmt, o, lots.Count, ownerOf, obstacles, perLotNames, refCallout), existing);
        LastConflicts = plan.Conflicts;
        if (plan.Conflicts > 0) Logger.Info($"Anotação: {plan.Conflicts} texto(s) sem espaço livre na vista {view.Name} (posição de menor conflito).");

        var normal = RevitStyles.TextType(doc, o.TextMm, o.Font, o.Opaque);
        var label = RevitStyles.TextType(doc, Math.Round(o.TextMm * 1.1, 2), o.Font, o.Opaque);
        var red = RevitStyles.TextType(doc, o.TextMm, o.Font, o.Opaque, color: Red);
        var refName = RevitStyles.TextType(doc, o.TextMm, o.Font, o.Opaque, bold: true);
        var dotType = plan.Dots.Count > 0 ? RevitStyles.FillType(doc, LotUse.Lote, "GeoPoint - Ponto de vértice", (0, 0, 0)) : ElementId.InvalidElementId;
        GraphicsStyle? tickStyle = null, dimStyle = null, leaderStyle = null, refStyle = null;
        if (plan.Lines.Count > 0)
        {
            tickStyle = RevitStyles.LineStyle(doc, LineDivisao, DivisaoColor, 4);
            dimStyle = RevitStyles.LineStyle(doc, LineCotaConfrontante, Red, 1);
            leaderStyle = RevitStyles.LineStyle(doc, LineChamada, new Color(90, 90, 90), 1);
            refStyle = RevitStyles.LineStyle(doc, "GeoPoint - Chamada da coordenada", new Color(0, 0, 0), 1);
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
                var type = t.Kind switch
                {
                    AnnoTextKind.LotLabel => Math.Abs(t.SizeFactor - 1) < 1e-6 ? label : RevitStyles.TextType(doc, Math.Round(o.TextMm * 1.1 * t.SizeFactor, 2), o.Font, o.Opaque),
                    AnnoTextKind.Confrontante or AnnoTextKind.Measure => red, AnnoTextKind.RefName => refName, _ => normal,
                };
                // O Revit posiciona o texto centralizado pelo meio da borda SUPERIOR da caixa; a rotação já vem legível
                // e é dada em relação à vista. Depois de criado, a direção real do texto é conferida e corrigida.
                var opts = new TextNoteOptions(type)
                {
                    HorizontalAlignment = HorizontalTextAlignment.Center,
                    Rotation = AngleUtil.NormPi(t.Rotation - ViewAngle(view)),
                    KeepRotatedTextReadable = false,
                };
                var at = RvtUnits.ToXyz(t.TopCenter, Z(t.Owner));
                var tn = TextNote.Create(doc, view.Id, at, t.Text.Replace("\n", "\r"), opts);
                AlignText(doc, tn, t.Rotation, at);
                if (t.BoldFirstLine) BoldFirstLine(tn, t.BoldLines);
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
                var st = ln.Kind switch { AnnoLineKind.ConfrontanteTick => tickStyle, AnnoLineKind.Leader => leaderStyle, AnnoLineKind.RefLeader => refStyle, _ => dimStyle };
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
            if (ids.Count > 0) l.Annotations.Add(new AnnotationRecord { ViewId = view.UniqueId, ElementIds = ids, Keys = plan.KeysOf(l.Id).ToList(), Scale = view.Scale });
        }
        return total;
    }

    /// <summary>Textos sem posição livre na última anotação (aviso de legibilidade).</summary>
    public static int LastConflicts { get; private set; }

    /// <summary>Garante que a linha de base do texto siga a direção pedida (no desenho), girando em torno do ponto de inserção.</summary>
    public static void AlignText(Document doc, TextNote tn, double desired, XYZ at)
    {
        try
        {
            var bd = tn.BaseDirection;
            double diff = AngleUtil.NormPi(desired - Math.Atan2(bd.Y, bd.X));
            if (Math.Abs(diff) > 1e-4)
                ElementTransformUtils.RotateElement(doc, tn.Id, Line.CreateBound(at, at + XYZ.BasisZ), diff);
        }
        catch (Exception ex) { Logger.Error("Alinhar texto", ex); }
    }

    /// <summary>Primeira linha em negrito (nome do lote).</summary>
    public static void BoldFirstLine(TextNote tn, int lines = 1)
    {
        try
        {
            var ft = tn.GetFormattedText();
            string text = ft.GetPlainText();
            int n = -1;
            for (int k = 0; k < Math.Max(1, lines); k++)
            {
                int next = text.IndexOf('\r', n + 1);
                if (next < 0) { n = text.Length; break; }
                n = next;
            }
            if (n < 0) n = text.Length;
            ft.SetBoldStatus(new TextRange(0, n), true);
            tn.SetFormattedText(ft);
        }
        catch (Exception ex) { Logger.Error("Negrito do rótulo", ex); }
    }

    /// <summary>Lotes anotados nesta vista com outra escala (a escala da vista mudou depois da anotação).</summary>
    public static List<LotRecord> ScaleChanged(GpSession s, View view) =>
        s.Project.PhaseLots.Where(l => l.Annotations.Any(a => a.ViewId == view.UniqueId && a.Scale > 0 && a.Scale != view.Scale)).ToList();

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

    /// <summary>Tipo de hachura do índice de cor (o mesmo das hachuras dos lotes); cinza = área edificada.</summary>
    public static ElementId TypeFor(Document doc, int colorIndex, bool cross, double spacingMm = 1.6, bool gray = false)
    {
        var pattern = cross
            ? RevitStyles.HatchPattern(doc, $"GeoPoint hachura cruzada {spacingMm:0.0} mm", 45, spacingMm * 1.4, true)
            : RevitStyles.HatchPattern(doc, $"GeoPoint hachura 45° {spacingMm:0.0} mm", 45, spacingMm, false);
        if (gray) return RevitStyles.HatchType(doc, "GeoPoint - Área edificada", pattern, new Color(110, 110, 110), true);
        int k = Math.Abs(colorIndex) % Palette.Length;
        var (r, g, b) = Palette[k];
        return RevitStyles.HatchType(doc, $"GeoPoint - Hachura {(cross ? "remanescente " : "")}{k + 1}", pattern, new Color(r, g, b), true);
    }

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
    public static int Apply(Document doc, View view, IReadOnlyList<LotRecord> lots, Func<LotRecord, int> colorIndex, double spacingMm = 1.6, bool invisibleBoundary = true, Action<FilledRegion, LotRecord>? after = null)
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
                try { after?.Invoke(fr, lot); } catch (Exception ex) { Logger.Error("Parâmetros da hachura de " + lot.DisplayName, ex); }
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
                double va = Annotator.ViewAngle(view);
                double ang = va + AnnotationPlanner.Readable(l.AngleRad - va);
                double hh = RevitStyles.HalfHeightMm(st.NomeMm) / 1000.0 * Math.Max(1, view.Scale);
                var top = l.Position + Vec2.FromAngle(ang).PerpLeft * hh;
                var at = RvtUnits.ToXyz(top, z);
                var tn = TextNote.Create(doc, view.Id, at, l.Text.ToUpperInvariant(), new TextNoteOptions(textType)
                {
                    HorizontalAlignment = HorizontalTextAlignment.Center, Rotation = AngleUtil.NormPi(ang - va), KeepRotatedTextReadable = false,
                });
                Annotator.AlignText(doc, tn, ang, at);
                ids.Add(tn.UniqueId);
            }
            catch { }
        }
        p.StreetDrawings.Add(new AnnotationRecord { ViewId = view.UniqueId, ElementIds = ids });
        return drawing.Warnings;
    }
}

/// <summary>Desenho das edificações (área edificada): hachura cinza, rótulo e cotas de afastamento até as divisas.</summary>
public static class BuildingDrawer
{
    public const string LineAfastamento = "GeoPoint - Afastamento da edificação";
    public const string LineEdificacao = "GeoPoint - Edificação";

    public static void Clear(Document doc, BuildingRecord b, View view)
    {
        foreach (var rec in b.Drawings.Where(a => a.ViewId == view.UniqueId).ToList())
        {
            var ids = ElementUtil.ExistingIds(doc, rec.ElementIds);
            if (ids.Count > 0) doc.Delete(ids);
            b.Drawings.Remove(rec);
        }
    }

    /// <summary>Retângulos ocupados pelos rótulos das edificações (para as outras anotações desviarem).</summary>
    public static List<Obb> LabelBoxes(IEnumerable<BuildingReport> reps, View view, double textMm, ValueFormatter fmt)
    {
        double k = Math.Max(1, view.Scale) / 1000.0;
        double ang = Annotator.ViewAngle(view);
        var res = new List<Obb>();
        foreach (var r in reps)
        {
            var reg = r.Building.ToRegion();
            var (text, hw, hh) = Label(r, textMm * k, fmt);
            res.Add(Obb.Of(reg.InteriorPoint(), ang, hw, hh));
        }
        return res;
    }

    private static (string Text, double Hw, double Hh) Label(BuildingReport r, double hM, ValueFormatter fmt)
    {
        var lines = new[] { r.Building.Nome.ToUpperInvariant(), "ÁREA = " + fmt.Area(r.Area) + (r.Building.Pavimentos > 1 ? $" ({r.Building.Pavimentos} pav.)" : "") };
        double hw = lines.Max(l => AnnotationPlanner.TextWidth(l, hM)) / 2 + RevitStyles.TextPadMm / 1000.0;
        double hh = lines.Length * hM * RevitStyles.LineFactor / 2 + RevitStyles.TextPadMm / 1000.0;
        return (string.Join("\n", lines), hw, hh);
    }

    /// <summary>Desenha as edificações na vista. Dentro de transação.</summary>
    public static int Draw(Document doc, View view, IReadOnlyList<BuildingReport> reps, ValueFormatter fmt, double textMm, string font, bool setbacks)
    {
        int n = 0;
        double scale = Math.Max(1, view.Scale);
        double k = scale / 1000.0;
        double ang = Annotator.ViewAngle(view);
        double z = ElementUtil.ViewPlaneZ(view);
        var hatch = Hatcher.TypeFor(doc, 0, true, 1.2, gray: true);
        var outline = RevitStyles.LineStyle(doc, LineEdificacao, new Color(60, 60, 60), 3);
        var dimStyle = RevitStyles.LineStyle(doc, LineAfastamento, new Color(0, 90, 170), 1);
        var textType = RevitStyles.TextType(doc, textMm, font, true);
        var dimText = RevitStyles.TextType(doc, Math.Round(textMm * 0.9, 2), font, true, color: new Color(0, 90, 170));
        foreach (var r in reps)
        {
            var b = r.Building;
            Clear(doc, b, view);
            var ids = new List<string>();
            try
            {
                var reg = b.ToRegion();
                var fr = FilledRegion.Create(doc, hatch, view.Id, new List<CurveLoop> { RvtUnits.ToCurveLoop(reg.Outer, z) });
                if (outline is not null && FilledRegion.IsValidLineStyleIdForFilledRegion(doc, outline.Id)) fr.SetLineStyleId(outline.Id);
                ids.Add(fr.UniqueId);
                var (text, hw, hh) = Label(r, textMm * k, fmt);
                var c = reg.InteriorPoint();
                var top = c + Vec2.FromAngle(ang).PerpLeft * hh;
                var at = RvtUnits.ToXyz(top, z);
                var tn = TextNote.Create(doc, view.Id, at, text.Replace("\n", "\r"), new TextNoteOptions(textType) { HorizontalAlignment = HorizontalTextAlignment.Center, KeepRotatedTextReadable = false });
                Annotator.AlignText(doc, tn, ang, at);
                Annotator.BoldFirstLine(tn);
                ids.Add(tn.UniqueId);
                if (setbacks)
                    foreach (var s in r.Afastamentos.Where(s => s.Distance > 0.05))
                    {
                        var a = s.AtBuilding; var e = s.AtBoundary;
                        var dc = doc.Create.NewDetailCurve(view, Line.CreateBound(RvtUnits.ToXyz(a, z), RvtUnits.ToXyz(e, z)));
                        if (dimStyle is not null) dc.LineStyle = dimStyle;
                        ids.Add(dc.UniqueId);
                        // Traços nas pontas (como uma cota).
                        var dir = (e - a).Normalized();
                        var perp = dir.PerpLeft * (1.2 * k);
                        foreach (var p in new[] { a, e })
                        {
                            var t = doc.Create.NewDetailCurve(view, Line.CreateBound(RvtUnits.ToXyz(p - perp, z), RvtUnits.ToXyz(p + perp, z)));
                            if (dimStyle is not null) t.LineStyle = dimStyle;
                            ids.Add(t.UniqueId);
                        }
                        double rot = ang + AnnotationPlanner.Readable(Math.Atan2(dir.Y, dir.X) - ang);
                        var mid = (a + e) * 0.5 + Vec2.FromAngle(rot).PerpLeft * (textMm * 0.9 * k * 1.3);
                        var at2 = RvtUnits.ToXyz(mid, z);
                        var t2 = TextNote.Create(doc, view.Id, at2, fmt.Distance(s.Distance), new TextNoteOptions(dimText)
                        {
                            HorizontalAlignment = HorizontalTextAlignment.Center, Rotation = AngleUtil.NormPi(rot - ang), KeepRotatedTextReadable = false,
                        });
                        Annotator.AlignText(doc, t2, rot, at2);
                        ids.Add(t2.UniqueId);
                    }
                n++;
            }
            catch (Exception ex) { Logger.Error("Edificação " + b.Nome, ex); }
            if (ids.Count > 0) b.Drawings.Add(new AnnotationRecord { ViewId = view.UniqueId, ElementIds = ids, Scale = view.Scale });
        }
        return n;
    }
}
