using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;

namespace GeoPoint.Core.Topology;

/// <summary>Converte faces da rede planar em geometria de lote (com rastreio dos elementos de origem).</summary>
public static class LotBuilder
{
    /// <param name="sourcesOfTag">Elementos de origem (UniqueId) de cada etiqueta da rede.</param>
    public static (List<EdgeDto> Outer, List<List<EdgeDto>> Holes) FromFace(
        PlanarNetwork net, NetFace face, Func<int, IEnumerable<string>> sourcesOfTag, bool includeHoles, bool mergeCollinear)
    {
        var outer = Convert(net, face.OuterEdges, sourcesOfTag, mergeCollinear);
        var holes = new List<List<EdgeDto>>();
        if (includeHoles)
            foreach (var h in face.HoleEdges) holes.Add(Convert(net, h, sourcesOfTag, mergeCollinear));
        return (outer, holes);
    }

    public static List<EdgeDto> Convert(PlanarNetwork net, IReadOnlyList<FaceEdgeRef> ring, Func<int, IEnumerable<string>> sourcesOfTag, bool mergeCollinear)
    {
        var items = ring.Select(e => (curve: e.Curve, src: e.Tags.SelectMany(sourcesOfTag).Distinct().ToList(), from: e.FromNode, to: e.ToNode)).ToList();
        if (mergeCollinear && items.Count > 3)
        {
            bool changed = true;
            int guard = 0;
            while (changed && items.Count > 3 && guard++ < 10000)
            {
                changed = false;
                for (int i = 0; i < items.Count && items.Count > 3; i++)
                {
                    int j = (i + 1) % items.Count;
                    var a = items[i]; var b = items[j];
                    if (a.to < 0 || net.ActiveDegree(a.to) != 2) continue;
                    var merged = TryMerge(a.curve, b.curve);
                    if (merged is null) continue;
                    var src = a.src.Concat(b.src).Distinct().ToList();
                    items[i] = (merged, src, a.from, b.to);
                    items.RemoveAt(j);
                    if (j < i) i--;
                    changed = true;
                }
            }
        }
        return items.Select(it => EdgeDto.FromCurve(it.curve, it.src)).ToList();
    }

    /// <summary>Une duas curvas consecutivas colineares (retas) ou cocirculares (arcos de mesmo sentido).</summary>
    public static Curve2? TryMerge(Curve2 a, Curve2 b)
    {
        if (a is LineCurve la && b is LineCurve lb)
        {
            var d1 = la.B - la.A; var d2 = lb.B - lb.A;
            double l1 = d1.Length, l2 = d2.Length;
            if (l1 == 0 || l2 == 0) return null;
            double sin = d1.Cross(d2) / (l1 * l2);
            double cos = d1.Dot(d2) / (l1 * l2);
            if (Math.Abs(sin) < 1e-9 && cos > 0) return new LineCurve(la.A, lb.B);
            return null;
        }
        if (a is ArcCurve aa && b is ArcCurve ab)
        {
            if (aa.Center.DistanceTo(ab.Center) > 1e-6 || Math.Abs(aa.Radius - ab.Radius) > 1e-6) return null;
            if (Math.Sign(aa.Sweep) != Math.Sign(ab.Sweep)) return null;
            double sweep = aa.Sweep + ab.Sweep;
            if (Math.Abs(sweep) >= AngleUtil.TwoPi - 1e-9) return null;
            return new ArcCurve(aa.Center, aa.Radius, aa.StartAngle, sweep);
        }
        return null;
    }

    /// <summary>Verifica se duas geometrias de lote são iguais (mesma área e mesmo contorno, com tolerância).</summary>
    public static bool SameShape(Region a, Region b, double tol = 0.002)
    {
        if (Math.Abs(a.Area - b.Area) > Math.Max(1e-4, a.Area * 1e-9)) return false;
        if (Math.Abs(a.Outer.Perimeter - b.Outer.Perimeter) > tol) return false;
        var pa = a.InteriorPoint();
        var pb = b.InteriorPoint();
        return b.Contains(pa) && a.Contains(pb);
    }

    /// <summary>Transfere atributos de divisas (confrontante, testada, recuo) da geometria antiga para a nova.</summary>
    public static List<EdgeInfo> RemapEdgeInfos(IReadOnlyList<EdgeInfo> old, IReadOnlyList<EdgeDto> newEdges, double tol = 0.002)
    {
        var res = new List<EdgeInfo>();
        var curves = newEdges.Select(e => e.ToCurve()).ToList();
        foreach (var info in old)
        {
            var mid = new Vec2(info.MX, info.MY);
            Curve2? target = null;
            double best = double.PositiveInfinity;
            foreach (var c in curves)
            {
                double d = c.DistanceTo(mid);
                if (d < best) { best = d; target = c; }
            }
            if (target is null || best > tol) continue;
            var m = target.Mid;
            if (res.Any(r => new Vec2(r.MX, r.MY).DistanceTo(m) < 1e-6)) continue;
            res.Add(new EdgeInfo { MX = m.X, MY = m.Y, Confrontante = info.Confrontante, Testada = info.Testada, Recuo = info.Recuo });
        }
        return res;
    }
}
