using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>
/// Desenha os pontos de controle importados (KML/KMZ): uma cruz de linhas de detalhe (o cruzamento serve de snap para desenhar
/// as divisas), um ponto preenchido e o nome.
/// </summary>
public static class ControlPointDrawer
{
    public const string LineStyleName = "GeoPoint - Ponto KML";
    private static readonly Color Orange = new(230, 110, 0);

    public static void Clear(Document doc, ControlPoint cp, View view)
    {
        foreach (var rec in cp.Drawings.Where(d => d.ViewId == view.UniqueId).ToList())
        {
            var ids = ElementUtil.ExistingIds(doc, rec.ElementIds);
            if (ids.Count > 0) doc.Delete(ids);
            cp.Drawings.Remove(rec);
        }
    }

    /// <summary>Desenha os pontos na vista. Dentro de transação. Devolve quantos foram desenhados.</summary>
    public static int Draw(GpSession s, View view, IEnumerable<ControlPoint> points)
    {
        var doc = s.Doc;
        var ctx = s.NewContext();
        double k = Math.Max(1, view.Scale) / 1000.0;                // mm no papel → m
        double arm = 3.0 * k;
        double size = s.Settings.Annotation.TextSizeMm;
        var style = RevitStyles.LineStyle(doc, LineStyleName, Orange, 2);
        var fill = RevitStyles.FillType(doc, LotUse.Lote, "GeoPoint - Ponto KML", (230, 110, 0));
        var text = RevitStyles.TextType(doc, size, s.Settings.Annotation.Font, true, true, Orange);
        double z = ElementUtil.ViewPlaneZ(view);
        int n = 0;
        foreach (var cp in points)
        {
            Clear(doc, cp, view);
            var pos = ControlPointCheck.Position(ctx, cp);
            if (pos is null) continue;
            var p = pos.Value;
            var ids = new List<string>();
            try
            {
                foreach (var (a, b) in new[] { (p - new Vec2(arm, 0), p + new Vec2(arm, 0)), (p - new Vec2(0, arm), p + new Vec2(0, arm)) })
                {
                    var dc = doc.Create.NewDetailCurve(view, Line.CreateBound(RvtUnits.ToXyz(a, z), RvtUnits.ToXyz(b, z)));
                    if (style is not null) dc.LineStyle = style;
                    ids.Add(dc.UniqueId);
                }
                var dot = Annotator.Dot(doc, view, p, s.Settings.Annotation.DotMm / 2000.0 * Math.Max(1, view.Scale), z, fill);
                if (dot is not null) ids.Add(dot.UniqueId);
                var at = p + new Vec2(arm * 0.7, arm * 0.7 + RevitStyles.HalfHeightMm(size) * 2 * k);
                var tn = TextNote.Create(doc, view.Id, RvtUnits.ToXyz(at, z), cp.Nome, new TextNoteOptions(text) { HorizontalAlignment = HorizontalTextAlignment.Left });
                ids.Add(tn.UniqueId);
                n++;
            }
            catch (Exception ex) { Logger.Error("Desenhar ponto " + cp.Nome, ex); }
            cp.Drawings.Add(new AnnotationRecord { ViewId = view.UniqueId, ElementIds = ids });
        }
        return n;
    }

    /// <summary>Redesenha todos os pontos nas vistas em que já estavam (ex.: depois de mudar a referência). Dentro de transação.</summary>
    public static void RedrawAll(GpSession s)
    {
        var views = s.Project.ControlPoints.SelectMany(c => c.Drawings.Select(d => d.ViewId)).Distinct().ToList();
        foreach (var uid in views)
        {
            if (ElementUtil.ByUniqueId(s.Doc, uid) is not View v) continue;
            Draw(s, v, s.Project.ControlPoints.Where(c => c.Drawings.Any(d => d.ViewId == uid)).ToList());
        }
    }
}
