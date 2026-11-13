using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;

namespace GeoPoint.Core.Operations;

/// <summary>
/// Junta divisas consecutivas alinhadas (retas na mesma reta, arcos do mesmo círculo). Usado no remembramento: o lote unido
/// fica só com os seus cantos reais, sem os vértices dos lotes antigos no meio das divisas.
/// </summary>
public static class CollinearMerge
{
    private const double AngTol = 1e-5;   // ~2" de arco
    private const double PosTol = 0.002;

    public static bool Mergeable(Curve2 a, Curve2 b)
    {
        if (a.End.DistanceTo(b.Start) > PosTol) return false;
        if (a is LineCurve la && b is LineCurve lb)
        {
            var da = la.Direction; var db = lb.Direction;
            return Math.Abs(da.Cross(db)) < AngTol && da.Dot(db) > 0;
        }
        if (a is ArcCurve aa && b is ArcCurve ab)
            return aa.Center.DistanceTo(ab.Center) < PosTol && Math.Abs(aa.Radius - ab.Radius) < PosTol && Math.Sign(aa.Sweep) == Math.Sign(ab.Sweep);
        return false;
    }

    /// <summary>Aplica no contorno e nos furos do lote. Devolve quantos vértices foram removidos.</summary>
    public static int Apply(LotRecord lot, ProjectContext? ctx = null)
    {
        int removed = 0;
        lot.Outer = MergeRing(lot, lot.Outer, ctx, ref removed);
        for (int h = 0; h < lot.Holes.Count; h++) lot.Holes[h] = MergeRing(lot, lot.Holes[h], ctx, ref removed);
        if (removed > 0)
            lot.ExtraVertices = lot.ExtraVertices.Where(p => lot.Outer.Any(e => new Vec2(e.X1, e.Y1).DistanceTo(p.ToVec()) <= PosTol)).ToList();
        return removed;
    }

    private static List<EdgeDto> MergeRing(LotRecord lot, List<EdgeDto> ring, ProjectContext? ctx, ref int removed)
    {
        int n = ring.Count;
        if (n < 4) return ring;
        var curves = ring.Select(e => e.ToCurve()).ToList();
        // Confrontante de cada trecho: onde muda de confrontante o ponto fica (com a sua distância e azimute).
        var names = curves.Select(c => Name(ProjectContext.FindInfo(lot, c.Mid), ctx, lot, c.Mid).ToUpperInvariant()).ToList();
        bool Join(int i) => Mergeable(curves[(i - 1 + n) % n], curves[i]) && names[(i - 1 + n) % n] == names[i];
        // Começa num vértice que é canto de verdade.
        int start = -1;
        for (int i = 0; i < n; i++) if (!Join(i)) { start = i; break; }
        if (start < 0) return ring;
        var groups = new List<List<int>>();
        for (int k = 0; k < n; k++)
        {
            int i = (start + k) % n;
            if (k == 0 || !Join(i)) groups.Add(new List<int>());
            groups[^1].Add(i);
        }
        if (groups.Count == n || groups.Count < 3) return ring;
        var res = new List<EdgeDto>();
        foreach (var g in groups)
        {
            if (g.Count == 1) { res.Add(ring[g[0]]); continue; }
            removed += g.Count - 1;
            var first = curves[g[0]]; var last = curves[g[^1]];
            Curve2 merged = first is LineCurve
                ? new LineCurve(first.Start, last.End)
                : CurveFactory.FromThreePoints(first.Start, curves[g[g.Count / 2]].PointAt(g.Count % 2 == 1 ? 0.5 : 0), last.End);
            var src = g.SelectMany(i => ring[i].Src).Distinct().ToList();
            res.Add(EdgeDto.FromCurve(merged, src));
            CombineInfos(lot, g.Select(i => curves[i]).ToList(), merged, ctx);
        }
        return res;
    }

    /// <summary>Confrontante da divisa juntada (as partes têm sempre o mesmo confrontante).</summary>
    private static void CombineInfos(LotRecord lot, List<Curve2> parts, Curve2 merged, ProjectContext? ctx)
    {
        var infos = parts.Select(c => ProjectContext.FindInfo(lot, c.Mid)).ToList();
        foreach (var i in infos.Where(i => i is not null)) lot.EdgeInfos.Remove(i!);
        var mid = merged.Mid;
        var real = infos.Where(i => i is not null && !i.IsEmpty || (i is not null && (i.LotId.Length > 0 || i.ViaId.Length > 0))).Select(i => i!).ToList();
        bool same = infos.All(i => i is not null) && real.Count == infos.Count && real.All(i => Same(i, real[0]));
        if (same) { lot.EdgeInfos.Add(real[0].CloneAt(mid)); return; }
        if (real.Count == 0 && ctx is null) return;
        var names = new List<string>();
        for (int k = 0; k < parts.Count; k++)
        {
            string nm = Name(infos[k], ctx, lot, parts[k].Mid);
            if (nm.Length > 0 && !names.Contains(nm, StringComparer.OrdinalIgnoreCase)) names.Add(nm);
        }
        if (names.Count == 0 && !infos.Any(i => i?.Testada == true)) return;
        if (names.Count == 1 && real.Count > 0 && real.All(i => Same(i, real[0])))
        {
            var c = real[0].CloneAt(mid);
            c.Testada = infos.Any(i => i?.Testada == true);
            lot.EdgeInfos.Add(c);
            return;
        }
        lot.EdgeInfos.Add(new EdgeInfo
        {
            MX = mid.X, MY = mid.Y, Tipo = ConfrontanteTipo.Texto,
            Confrontante = names.Count <= 1 ? names.FirstOrDefault() ?? "" : string.Join(", ", names.Take(names.Count - 1)) + " e " + names[^1],
            Testada = infos.Any(i => i?.Testada == true),
        });
    }

    private static bool Same(EdgeInfo a, EdgeInfo b) =>
        a.Tipo == b.Tipo && a.LotId == b.LotId && a.ViaId == b.ViaId && string.Equals(a.Confrontante.Trim(), b.Confrontante.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Name(EdgeInfo? i, ProjectContext? ctx, LotRecord lot, Vec2 mid)
    {
        if (i is not null)
        {
            if (i.Tipo == ConfrontanteTipo.Lote && i.LotId.Length > 0)
                return ctx?.Project.FindLot(i.LotId) is { } nl ? ctx.ConfrontanteName(nl) : i.LotId;
            if (i.Tipo == ConfrontanteTipo.Via && i.ViaId.Length > 0) return ctx?.Project.FindVia(i.ViaId)?.Nome ?? "";
            if (!string.IsNullOrWhiteSpace(i.Confrontante)) return i.Confrontante.Trim();
        }
        return ctx?.NeighborAt(lot, mid) is { } nb ? ctx.ConfrontanteName(nb) : "";
    }
}
