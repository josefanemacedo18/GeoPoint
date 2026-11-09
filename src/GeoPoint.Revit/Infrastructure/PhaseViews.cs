using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>Vistas de trabalho das fases e visibilidade das linhas (linhas internas aos lotes da fase ficam ocultas).</summary>
public static class PhaseViews
{
    /// <summary>Linha sobre a divisa dos lotes mostrados ou fora deles (contexto)? Linhas internas devolvem false.</summary>
    public static bool Keep(CurveElement c, IReadOnlyList<Region> shown)
    {
        Curve? g;
        try { g = c.GeometryCurve; } catch { return true; }
        if (g is null || !g.IsBound) return true;
        var samples = new[] { 0.2, 0.5, 0.8 }.Select(t => RvtUnits.ToVec(g.Evaluate(t, true))).ToList();
        if (OnBoundary(samples, shown)) return true;
        var mid = samples[1];
        return !shown.Any(r => r.Bounds.Contains(mid) && r.Contains(mid));
    }

    /// <summary>A linha está sobre a divisa de algum dos lotes?</summary>
    public static bool OnBoundary(CurveElement c, IReadOnlyList<Region> shown)
    {
        try
        {
            var g = c.GeometryCurve;
            if (g is null || !g.IsBound) return false;
            return OnBoundary(new[] { 0.2, 0.5, 0.8 }.Select(t => RvtUnits.ToVec(g.Evaluate(t, true))).ToList(), shown);
        }
        catch { return false; }
    }

    private static bool OnBoundary(List<Vec2> samples, IReadOnlyList<Region> shown)
    {
        const double tol = 0.01;
        return samples.All(p => shown.Any(r => r.AllRings().Any(ring => ring.Edges.Any(e => e.Bounds.Expand(tol).Contains(p) && e.DistanceTo(p) <= tol))));
    }

    public static bool IsGeoPointLine(CurveElement ce)
    {
        try
        {
            string style = ce.LineStyle?.Name ?? "";
            return style.StartsWith("GeoPoint - Extensão", StringComparison.Ordinal) || style.StartsWith("GeoPoint - Cota", StringComparison.Ordinal)
                   || style.StartsWith("GeoPoint - Meio-fio", StringComparison.Ordinal) || style.StartsWith("GeoPoint - Alinhamento", StringComparison.Ordinal)
                   || style.StartsWith("GeoPoint - Linha de chamada", StringComparison.Ordinal) || style.StartsWith("GeoPoint - Ponto", StringComparison.Ordinal)
                   || style.StartsWith("GeoPoint - Prancha", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    /// <summary>Contornos de todos os lotes de todas as fases (só linhas de divisa podem ser ocultadas).</summary>
    public static List<Region> AllLotRegions(ParcelProject p) => Regions(p.Lots);

    private static List<Region> Regions(IEnumerable<LotRecord> lots)
    {
        var res = new List<Region>();
        foreach (var l in lots) { try { res.Add(l.ToRegion()); } catch { } }
        return res;
    }

    /// <summary>
    /// Cria a vista de trabalho da fase (cópia de <paramref name="source"/>). Linhas de detalhe dos lotes são copiadas e os lotes
    /// da fase passam a apontar para as cópias. Dentro de transação.
    /// </summary>
    public static View CreateView(GpSession s, FaseRecord fase, View source)
    {
        var doc = s.Doc;
        if (!source.CanViewBeDuplicated(ViewDuplicateOption.Duplicate)) throw new InvalidOperationException($"A vista \"{source.Name}\" não pode ser duplicada.");
        var view = (View)doc.GetElement(source.Duplicate(ViewDuplicateOption.Duplicate));
        view.Name = UniqueName(doc, $"F{fase.Numero} – {fase.Nome}");
        var lots = s.Project.Lots.Where(l => l.FaseId == fase.Id).ToList();
        var regions = Regions(lots.Where(l => l.Status == LotStatus.Ativo));

        // Linhas de detalhe: copia as da vista de origem (menos as do GeoPoint e as internas aos lotes da fase).
        var allDiv = AllLotRegions(s.Project);
        var detail = new FilteredElementCollector(doc, source.Id).OfClass(typeof(CurveElement)).Cast<CurveElement>()
            .Where(c => c.CurveElementType == CurveElementType.DetailCurve && !IsGeoPointLine(c) && (Keep(c, regions) || !OnBoundary(c, allDiv))).ToList();
        if (detail.Count > 0)
        {
            try
            {
                var copied = ElementTransformUtils.CopyElements(source, detail.Select(d => d.Id).ToList(), view, Transform.Identity, new CopyPasteOptions());
                var map = MapByGeometry(doc, detail, copied);
                foreach (var lot in lots.Where(l => l.SourceViewId == source.UniqueId))
                {
                    foreach (var e in lot.Outer.Concat(lot.Holes.SelectMany(h => h)))
                        e.Src = e.Src.Select(u => map.TryGetValue(u, out var n) ? n : u).ToList();
                    lot.SourceViewId = view.UniqueId;
                }
            }
            catch (Exception ex) { Logger.Error("Copiar linhas de detalhe para a vista da fase", ex); }
        }
        fase.ViewIds.Add(view.UniqueId);
        Refresh(s, fase, view);
        // Pontos de controle (KML) que estavam na vista de origem também aparecem na vista da fase.
        var cps = s.Project.ControlPoints.Where(c => c.Drawings.Any(d => d.ViewId == source.UniqueId)).ToList();
        if (cps.Count > 0) ControlPointDrawer.Draw(s, view, cps);
        return view;
    }

    private static Dictionary<string, string> MapByGeometry(Document doc, List<CurveElement> originals, ICollection<ElementId> copies)
    {
        string Key(Curve c)
        {
            XYZ a = c.GetEndPoint(0), b = c.GetEndPoint(1), m = c.Evaluate(0.5, true);
            string f(XYZ p) => $"{Math.Round(p.X, 5)};{Math.Round(p.Y, 5)}";
            return $"{f(a)}|{f(b)}|{f(m)}";
        }
        var byKey = new Dictionary<string, string>();
        foreach (var o in originals)
        {
            try { byKey[Key(o.GeometryCurve)] = o.UniqueId; } catch { }
        }
        var map = new Dictionary<string, string>();
        foreach (var id in copies)
        {
            if (doc.GetElement(id) is not CurveElement ce) continue;
            try { if (byKey.TryGetValue(Key(ce.GeometryCurve), out var orig)) map[orig] = ce.UniqueId; } catch { }
        }
        return map;
    }

    private static string UniqueName(Document doc, string name)
    {
        foreach (char ch in "{}[]|;:<>?`~\\") name = name.Replace(ch, '-');
        var names = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(name)) return name;
        for (int i = 2; ; i++) if (!names.Contains($"{name} ({i})")) return $"{name} ({i})";
    }

    /// <summary>Atualiza a visibilidade das linhas em todas as vistas de todas as fases. Dentro de transação.</summary>
    public static void RefreshAll(GpSession s)
    {
        foreach (var f in s.Project.Fases)
            foreach (var uid in f.ViewIds.ToList())
            {
                if (ElementUtil.ByUniqueId(s.Doc, uid) is View v) Refresh(s, f, v);
                else { f.ViewIds.Remove(uid); f.HiddenByView.Remove(uid); }
            }
    }

    /// <summary>Oculta as linhas de modelo internas aos lotes ativos da fase e volta a mostrar as que o GeoPoint ocultou e não são mais internas.</summary>
    public static void Refresh(GpSession s, FaseRecord fase, View view)
    {
        var doc = s.Doc;
        var regions = Regions(s.Project.Lots.Where(l => l.FaseId == fase.Id && l.Status == LotStatus.Ativo));
        var divisas = AllLotRegions(s.Project);
        if (!fase.HiddenByView.TryGetValue(view.UniqueId, out var hiddenBefore)) hiddenBefore = new List<string>();
        var hide = new List<ElementId>();
        var unhide = new List<ElementId>();
        var nowHidden = new List<string>();
        var curves = new FilteredElementCollector(doc).OfClass(typeof(CurveElement)).Cast<CurveElement>()
            .Where(c => c.CurveElementType == CurveElementType.ModelCurve || (c.CurveElementType == CurveElementType.DetailCurve && c.OwnerViewId == view.Id));
        foreach (var c in curves)
        {
            if (IsGeoPointLine(c)) continue;
            // Só divisas (de qualquer fase) são ocultadas; outras linhas do desenho dentro dos lotes ficam como estão.
            bool keep = Keep(c, regions) || !OnBoundary(c, divisas);
            bool hidden;
            try { hidden = c.IsHidden(view); } catch { continue; }
            if (!keep)
            {
                if (!hidden && c.CanBeHidden(view)) hide.Add(c.Id);
                if (!hidden || hiddenBefore.Contains(c.UniqueId)) nowHidden.Add(c.UniqueId);
            }
            else if (hidden && hiddenBefore.Contains(c.UniqueId)) unhide.Add(c.Id);
        }
        try { if (hide.Count > 0) view.HideElements(hide); } catch (Exception ex) { Logger.Error("Ocultar linhas na vista da fase", ex); }
        try { if (unhide.Count > 0) view.UnhideElements(unhide); } catch (Exception ex) { Logger.Error("Mostrar linhas na vista da fase", ex); }
        fase.HiddenByView[view.UniqueId] = nowHidden;
    }

    /// <summary>Primeira vista existente da fase ativa (ou null).</summary>
    public static View? ActivePhaseView(GpSession s) =>
        s.Project.Fase.ViewIds.Select(u => ElementUtil.ByUniqueId(s.Doc, u)).OfType<View>().FirstOrDefault();
}
