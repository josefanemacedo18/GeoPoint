using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;
using GeoPoint.Core.Geometry;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>Curva lida do Revit, já convertida para o núcleo (metros, plano XY).</summary>
public sealed class SourceCurve
{
    public Curve2 Curve { get; init; } = null!;
    public string UniqueId { get; init; } = "";
    public ElementId Id { get; init; } = ElementId.InvalidElementId;
    /// <summary>"ModelLine", "DetailLine" ou "Cad".</summary>
    public string Kind { get; init; } = "ModelLine";
    public double ZFeet { get; init; }
    public string ViewUniqueId { get; init; } = "";
    public string SketchPlaneUniqueId { get; init; } = "";
    public string Layer { get; init; } = "";
}

public static class GeometryReader
{
    private const double FlatTol = 1e-4; // pés

    public static bool IsLine(Element e) => e is ModelCurve || e is DetailCurve;
    public static bool IsCad(Element e) => e is ImportInstance;

    /// <summary>Lê curvas horizontais de linhas de modelo/detalhe e de CAD importado/vinculado.</summary>
    public static List<SourceCurve> Read(Document doc, View activeView, IEnumerable<Element> elements, ISet<string>? cadLayers, List<string> warnings)
    {
        var res = new List<SourceCurve>();
        int skipped = 0, tessellated = 0;
        foreach (var el in elements)
        {
            try
            {
                if (el is CurveElement ce && IsLine(el))
                {
                    var gc = ce.GeometryCurve;
                    if (gc is null) continue;
                    string kind = el is DetailCurve ? "DetailLine" : "ModelLine";
                    string viewId = "";
                    if (el is DetailCurve && el.OwnerViewId != ElementId.InvalidElementId)
                        viewId = doc.GetElement(el.OwnerViewId)?.UniqueId ?? "";
                    string spId = "";
                    try { spId = ce.SketchPlane?.UniqueId ?? ""; } catch { }
                    var curves = Convert(gc, ref skipped, ref tessellated);
                    foreach (var (c, z) in curves)
                        res.Add(new SourceCurve { Curve = c, UniqueId = el.UniqueId, Id = el.Id, Kind = kind, ZFeet = z, ViewUniqueId = viewId, SketchPlaneUniqueId = spId });
                }
                else if (el is ImportInstance ii)
                {
                    var opt = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false };
                    if (ii.ViewSpecific) opt.View = activeView;
                    var geom = ii.get_Geometry(opt);
                    if (geom is null) continue;
                    foreach (var go in geom)
                    {
                        if (go is not GeometryInstance gi) continue;
                        var inst = gi.GetInstanceGeometry();
                        if (inst is null) continue;
                        foreach (var obj in inst)
                        {
                            string layer = LayerName(doc, obj);
                            if (cadLayers is not null && !cadLayers.Contains(layer)) continue;
                            IEnumerable<Curve> rcurves = obj switch
                            {
                                Curve cv => new[] { cv },
                                PolyLine pl => PolyToLines(pl),
                                _ => Array.Empty<Curve>(),
                            };
                            foreach (var rc in rcurves)
                                foreach (var (c, z) in Convert(rc, ref skipped, ref tessellated))
                                    res.Add(new SourceCurve { Curve = c, UniqueId = ii.UniqueId, Id = ii.Id, Kind = "Cad", ZFeet = z, Layer = layer });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Falha ao ler geometria do elemento {el.Id.Value}.", ex);
                warnings.Add($"Elemento {el.Id.Value} ignorado (geometria ilegível).");
            }
        }
        if (skipped > 0) warnings.Add($"{skipped} linha(s) fora do plano horizontal foram ignoradas.");
        if (tessellated > 0) warnings.Add($"{tessellated} curva(s) não circulares (spline/elipse) foram convertidas em segmentos de reta.");
        return res;
    }

    public static string LayerName(Document doc, GeometryObject obj)
    {
        try
        {
            if (obj.GraphicsStyleId != ElementId.InvalidElementId && doc.GetElement(obj.GraphicsStyleId) is GraphicsStyle gs)
                return gs.GraphicsStyleCategory?.Name ?? gs.Name;
        }
        catch { }
        return "(sem layer)";
    }

    /// <summary>Layers presentes nos CAD selecionados.</summary>
    public static SortedSet<string> CadLayers(Document doc, View activeView, IEnumerable<ImportInstance> cads)
    {
        var set = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var ii in cads)
        {
            try
            {
                var opt = new Options();
                if (ii.ViewSpecific) opt.View = activeView;
                var geom = ii.get_Geometry(opt);
                if (geom is null) continue;
                foreach (var go in geom)
                    if (go is GeometryInstance gi && gi.GetInstanceGeometry() is GeometryElement inst)
                        foreach (var obj in inst)
                            if (obj is Curve || obj is PolyLine) set.Add(LayerName(doc, obj));
            }
            catch { }
        }
        return set;
    }

    private static IEnumerable<Curve> PolyToLines(PolyLine pl)
    {
        var pts = pl.GetCoordinates();
        for (int i = 0; i < pts.Count - 1; i++)
        {
            if (pts[i].DistanceTo(pts[i + 1]) < 1e-6) continue;
            Curve? l = null;
            try { l = Line.CreateBound(pts[i], pts[i + 1]); } catch { }
            if (l is not null) yield return l;
        }
    }

    private static List<(Curve2, double)> Convert(Curve c, ref int skipped, ref int tessellated)
    {
        var res = new List<(Curve2, double)>();
        if (c is Line line)
        {
            if (!line.IsBound) return res;
            var a = line.GetEndPoint(0); var b = line.GetEndPoint(1);
            if (Math.Abs(a.Z - b.Z) > FlatTol) { skipped++; return res; }
            res.Add((new LineCurve(RvtUnits.ToVec(a), RvtUnits.ToVec(b)), a.Z));
            return res;
        }
        if (c is Arc arc)
        {
            if (Math.Abs(Math.Abs(arc.Normal.Z) - 1) > 1e-6) { skipped++; return res; }
            double z = arc.Center.Z;
            double r = arc.Radius;
            bool full = !arc.IsBound || Math.Abs(arc.Length - 2 * Math.PI * r) < 1e-6;
            if (full)
            {
                var ctr = RvtUnits.ToVec(arc.Center);
                double rm = r * RvtUnits.FtToM;
                var x = new Vec2(arc.XDirection.X, arc.XDirection.Y).Normalized();
                var y = x.PerpLeft;
                var p0 = ctr + x * rm; var p1 = ctr + y * rm; var p2 = ctr - x * rm; var p3 = ctr - y * rm;
                res.Add((CurveFactory.FromThreePoints(p0, p1, p2), z));
                res.Add((CurveFactory.FromThreePoints(p2, p3, p0), z));
                return res;
            }
            var s = arc.GetEndPoint(0); var e = arc.GetEndPoint(1); var m = arc.Evaluate(0.5, true);
            res.Add((CurveFactory.FromThreePoints(RvtUnits.ToVec(s), RvtUnits.ToVec(m), RvtUnits.ToVec(e)), z));
            return res;
        }
        // Outras curvas: segmentos.
        IList<XYZ> pts;
        try { pts = c.Tessellate(); } catch { skipped++; return res; }
        if (pts.Count < 2) return res;
        if (pts.Any(p => Math.Abs(p.Z - pts[0].Z) > FlatTol)) { skipped++; return res; }
        tessellated++;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            var a = RvtUnits.ToVec(pts[i]); var b = RvtUnits.ToVec(pts[i + 1]);
            if (a.DistanceTo(b) > 1e-6) res.Add((new LineCurve(a, b), pts[0].Z));
        }
        return res;
    }
}

/// <summary>Filtro de seleção: linhas de modelo/detalhe e CAD importado.</summary>
public sealed class LineAndCadFilter : ISelectionFilter
{
    private readonly bool _allowCad;
    public LineAndCadFilter(bool allowCad = true) { _allowCad = allowCad; }
    public bool AllowElement(Element elem) => GeometryReader.IsLine(elem) || (_allowCad && GeometryReader.IsCad(elem));
    public bool AllowReference(Reference reference, XYZ position) => false;
}

public sealed class ToposolidFilter : ISelectionFilter
{
    public bool AllowElement(Element elem) => elem is Toposolid;
    public bool AllowReference(Reference reference, XYZ position) => false;
}
