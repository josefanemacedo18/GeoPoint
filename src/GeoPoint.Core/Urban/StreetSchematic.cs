using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;
using GeoPoint.ThirdParty.Clipper2;

namespace GeoPoint.Core.Urban;

/// <summary>Testada de lote voltada para uma via.</summary>
public sealed record StreetFrontage(string ViaId, Curve2 Edge, Vec2 Outward);

public sealed class StreetLabel
{
    public Vec2 Position { get; init; }
    /// <summary>Ângulo do texto (rad), já ajustado para leitura.</summary>
    public double AngleRad { get; init; }
    public string Text { get; init; } = "";
    public string ViaId { get; init; } = "";
}

public sealed class StreetDrawing
{
    /// <summary>Meio-fio (limite do leito carroçável).</summary>
    public List<List<Vec2>> Curbs { get; } = new();
    /// <summary>Alinhamento predial do lado sem lotes (borda externa da calçada).</summary>
    public List<List<Vec2>> Alignments { get; } = new();
    public List<StreetLabel> Labels { get; } = new();
    public List<string> Warnings { get; } = new();
}

/// <summary>
/// Desenho esquemático de vias: a partir das testadas, calcula o eixo (afastado da calçada + meia largura),
/// as faixas de rua e calçada, une as vias (cruzamentos em L, T ou + surgem da união) e concorda as esquinas.
/// </summary>
public static class StreetSchematic
{
    private const double JoinTol = 0.02;
    private const double ArcChord = 0.02;

    private sealed class Chain
    {
        public ViaRecord Via = null!;
        public List<Vec2> Points = new();   // testada (rua à esquerda)
        public List<Vec2> Axis = new();
        public bool Closed;
        public double StartExt, EndExt;
        public bool StartCrossing, EndCrossing;
        public double Length => PolyLength(Points, Closed);
    }

    public static StreetDrawing Build(IReadOnlyList<ViaRecord> vias, IReadOnlyList<CruzamentoRecord> crossings,
        IReadOnlyList<StreetFrontage> frontages, IReadOnlyList<Region> lots)
    {
        var res = new StreetDrawing();
        var chains = new List<Chain>();
        foreach (var via in vias)
        {
            var mine = frontages.Where(f => f.ViaId == via.Id).ToList();
            if (mine.Count == 0) { res.Warnings.Add($"A via \"{via.Nome}\" não tem testadas associadas."); continue; }
            foreach (var pts in BuildChains(mine, out var closedFlags).Zip(closedFlags))
            {
                var ch = new Chain { Via = via, Points = pts.First, Closed = pts.Second };
                double off = via.Calcada + via.Largura / 2;
                ch.Axis = OffsetPolyline(ch.Points, off, ch.Closed);
                ch.StartExt = ch.EndExt = Math.Max(0, via.Prolongamento);
                chains.Add(ch);
            }
        }
        if (chains.Count == 0) return res;

        ApplyCrossings(chains, crossings, res);

        // Faixas (rua e rua + calçadas) e união.
        var carriage = new PathsD();
        var full = new PathsD();
        var caps = new List<(Vec2 a, Vec2 b)>();
        foreach (var ch in chains)
        {
            var axis = Extend(ch);
            if (axis.Count < 2) continue;
            var path = new PathsD { Overlay.ToPath(axis) };
            var end = ch.Closed ? EndType.Joined : EndType.Butt;
            carriage.AddRange(Clipper.InflatePaths(path, ch.Via.Largura / 2, JoinType.Miter, end, 4.0, Overlay.Precision));
            full.AddRange(Clipper.InflatePaths(path, ch.Via.Largura / 2 + ch.Via.Calcada, JoinType.Miter, end, 4.0, Overlay.Precision));
            if (!ch.Closed)
            {
                if (!ch.StartCrossing) AddCaps(caps, axis[0], axis[1], ch.Via);
                if (!ch.EndCrossing) AddCaps(caps, axis[^1], axis[^2], ch.Via);
            }
        }
        var cUnion = Overlay.Union(carriage, FillRule.NonZero);
        var fUnion = Overlay.Union(full, FillRule.NonZero);

        double radius = crossings.Where(c => c.ViaIds.Count >= 2).Select(c => c.RaioEsquina).DefaultIfEmpty(0).Max();
        if (radius > 0.01)
        {
            // Fechamento morfológico: arredonda os cantos reentrantes do leito (esquinas do meio-fio).
            var grown = Clipper.InflatePaths(cUnion, radius, JoinType.Round, EndType.Polygon, 2.0, Overlay.Precision, 0.01);
            cUnion = Clipper.InflatePaths(grown, -radius, JoinType.Round, EndType.Polygon, 2.0, Overlay.Precision, 0.01);
        }

        var lotPolys = lots.SelectMany(r => r.AllRings()).Select(r => r.ToPolygon(0.005)).ToList();
        foreach (var path in cUnion)
            res.Curbs.AddRange(KeepPieces(Overlay.ToVecs(path), seg => !IsCap(seg, caps)));
        foreach (var path in fUnion)
            res.Alignments.AddRange(KeepPieces(Overlay.ToVecs(path), seg => !IsCap(seg, caps) && !OnLots(seg, lotPolys)));

        // Nomes: um por via (no trecho mais longo) e outros para trechos distantes.
        foreach (var g in chains.GroupBy(c => c.Via.Id))
        {
            var placed = new List<Vec2>();
            foreach (var ch in g.OrderByDescending(c => c.Length))
            {
                var (pos, ang) = PointAtHalf(ch.Axis, ch.Closed);
                if (placed.Any(p => p.DistanceTo(pos) < 80)) continue;
                placed.Add(pos);
                res.Labels.Add(new StreetLabel { Position = pos, AngleRad = Readable(ang), Text = ch.Via.Nome, ViaId = ch.Via.Id });
            }
        }
        return res;
    }

    // ------------------------------------------------------------ cadeias de testadas

    private static List<List<Vec2>> BuildChains(List<StreetFrontage> frontages, out List<bool> closed)
    {
        // Orienta cada testada com a rua à esquerda.
        var segs = new List<List<Vec2>>();
        foreach (var f in frontages)
        {
            var c = f.Edge;
            var d = (c.End - c.Start).Normalized();
            if (d.PerpLeft.Dot(f.Outward) < 0) c = c.Reversed();
            var pts = c.Densify(ArcChord);
            if (pts.Count >= 2) segs.Add(pts);
        }
        var used = new bool[segs.Count];
        var chains = new List<List<Vec2>>();
        closed = new List<bool>();
        int Next(Vec2 end)
        {
            for (int j = 0; j < segs.Count; j++)
                if (!used[j] && segs[j][0].DistanceTo(end) <= JoinTol) return j;
            return -1;
        }
        bool HasPrev(int i)
        {
            for (int j = 0; j < segs.Count; j++)
                if (j != i && segs[j][^1].DistanceTo(segs[i][0]) <= JoinTol) return true;
            return false;
        }
        // Primeiro as cadeias abertas (começam em testadas sem antecessora), depois as fechadas.
        foreach (bool pass in new[] { true, false })
        {
            for (int i = 0; i < segs.Count; i++)
            {
                if (used[i] || (pass && HasPrev(i))) continue;
                used[i] = true;
                var chain = new List<Vec2>(segs[i]);
                int k;
                while ((k = Next(chain[^1])) >= 0)
                {
                    used[k] = true;
                    chain.AddRange(segs[k].Skip(1));
                }
                bool isClosed = chain.Count > 3 && chain[0].DistanceTo(chain[^1]) <= JoinTol;
                if (isClosed) chain.RemoveAt(chain.Count - 1);
                chains.Add(Simplify(chain, isClosed));
                closed.Add(isClosed);
            }
        }
        return chains;
    }

    private static List<Vec2> Simplify(List<Vec2> pts, bool closed)
    {
        var res = new List<Vec2>();
        foreach (var p in pts)
            if (res.Count == 0 || res[^1].DistanceTo(p) > 1e-6) res.Add(p);
        // Remove vértices colineares.
        bool changed = true;
        while (changed && res.Count > 2)
        {
            changed = false;
            for (int i = closed ? 0 : 1; i < (closed ? res.Count : res.Count - 1); i++)
            {
                var a = res[(i - 1 + res.Count) % res.Count]; var b = res[i]; var c = res[(i + 1) % res.Count];
                var u = (b - a); var v = (c - b);
                if (u.Length < 1e-9 || v.Length < 1e-9) continue;
                if (Math.Abs(u.Normalized().Cross(v.Normalized())) < 1e-7 && u.Dot(v) > 0) { res.RemoveAt(i); changed = true; break; }
            }
        }
        return res;
    }

    /// <summary>Offset de polilinha para a esquerda com juntas em esquadria (limite 4×).</summary>
    public static List<Vec2> OffsetPolyline(List<Vec2> pts, double d, bool closed)
    {
        int n = pts.Count;
        var res = new List<Vec2>();
        if (n < 2) return res;
        int segCount = closed ? n : n - 1;
        var dirs = new Vec2[segCount];
        for (int i = 0; i < segCount; i++) dirs[i] = (pts[(i + 1) % n] - pts[i]).Normalized();
        for (int i = 0; i < n; i++)
        {
            bool hasPrev = closed || i > 0;
            bool hasNext = closed || i < n - 1;
            if (!hasPrev) { res.Add(pts[i] + dirs[0].PerpLeft * d); continue; }
            if (!hasNext) { res.Add(pts[i] + dirs[segCount - 1].PerpLeft * d); continue; }
            var d0 = dirs[(i - 1 + segCount) % segCount];
            var d1 = dirs[i % segCount];
            var n0 = d0.PerpLeft; var n1 = d1.PerpLeft;
            double cross = d0.Cross(d1);
            if (Math.Abs(cross) < 1e-9) { res.Add(pts[i] + n1 * d); continue; }
            // Interseção das duas retas deslocadas.
            var p0 = pts[i] + n0 * d;
            var p1 = pts[i] + n1 * d;
            double t = (p1 - p0).Cross(d1) / cross;
            var x = p0 + d0 * t;
            if (x.DistanceTo(pts[i]) > Math.Abs(d) * 4) { res.Add(p0); res.Add(p1); }
            else res.Add(x);
        }
        return res;
    }

    // ------------------------------------------------------------ cruzamentos

    private static void ApplyCrossings(List<Chain> chains, IReadOnlyList<CruzamentoRecord> crossings, StreetDrawing res)
    {
        foreach (var cr in crossings)
        {
            var viaIds = cr.ViaIds.Distinct().ToList();
            if (viaIds.Count < 2) continue;
            // Pontas candidatas de cada via.
            var ends = new Dictionary<string, List<(Chain ch, bool start, Vec2 p, Vec2 t)>>();
            foreach (var id in viaIds)
            {
                var list = new List<(Chain, bool, Vec2, Vec2)>();
                foreach (var ch in chains.Where(c => c.Via.Id == id && !c.Closed && c.Axis.Count >= 2))
                {
                    list.Add((ch, true, ch.Axis[0], (ch.Axis[0] - ch.Axis[1]).Normalized()));
                    list.Add((ch, false, ch.Axis[^1], (ch.Axis[^1] - ch.Axis[^2]).Normalized()));
                }
                if (list.Count > 0) ends[id] = list;
            }
            if (ends.Count < 2)
            {
                res.Warnings.Add("Cruzamento ignorado: as vias escolhidas não têm testadas suficientes.");
                continue;
            }
            // Para cada via, a ponta mais próxima das pontas das outras vias.
            var chosen = new List<(Chain ch, bool start, Vec2 p, Vec2 t)>();
            foreach (var (id, list) in ends)
            {
                var others = ends.Where(kv => kv.Key != id).SelectMany(kv => kv.Value).ToList();
                var best = list.OrderBy(e => others.Min(o => o.p.DistanceTo(e.p))).First();
                chosen.Add(best);
            }
            var center = LeastSquaresIntersection(chosen.Select(c => (c.p, c.t)).ToList());
            foreach (var c in chosen)
            {
                var others = chosen.Where(o => o.ch.Via.Id != c.ch.Via.Id).Select(o => o.ch.Via).ToList();
                double margin = others.Max(v => v.Largura / 2 + v.Calcada);
                if (cr.Atravessam.Contains(c.ch.Via.Id))
                    margin = others.Max(v => v.Largura + 2 * v.Calcada) + Math.Max(0, cr.Continuacao);
                double along = (center - c.p).Dot(c.t);
                double ext = Math.Max(0, along) + margin;
                if (c.start) { c.ch.StartExt = Math.Max(c.ch.StartExt, ext); c.ch.StartCrossing = !cr.Atravessam.Contains(c.ch.Via.Id) || cr.Continuacao <= 0; }
                else { c.ch.EndExt = Math.Max(c.ch.EndExt, ext); c.ch.EndCrossing = !cr.Atravessam.Contains(c.ch.Via.Id) || cr.Continuacao <= 0; }
            }
        }
    }

    /// <summary>Ponto mais próximo (mínimos quadrados) de um conjunto de retas (ponto, direção).</summary>
    public static Vec2 LeastSquaresIntersection(IReadOnlyList<(Vec2 p, Vec2 t)> lines)
    {
        double a11 = 0, a12 = 0, a22 = 0, b1 = 0, b2 = 0;
        foreach (var (p, t) in lines)
        {
            double m11 = 1 - t.X * t.X, m12 = -t.X * t.Y, m22 = 1 - t.Y * t.Y;
            a11 += m11; a12 += m12; a22 += m22;
            b1 += m11 * p.X + m12 * p.Y;
            b2 += m12 * p.X + m22 * p.Y;
        }
        double det = a11 * a22 - a12 * a12;
        if (Math.Abs(det) < 1e-9 * Math.Max(1, a11 * a22))
            return new Vec2(lines.Average(l => l.p.X), lines.Average(l => l.p.Y));
        return new Vec2((b1 * a22 - b2 * a12) / det, (a11 * b2 - a12 * b1) / det);
    }

    private static List<Vec2> Extend(Chain ch)
    {
        var a = new List<Vec2>(ch.Axis);
        if (ch.Closed || a.Count < 2) return a;
        if (ch.StartExt > 0) a.Insert(0, a[0] + (a[0] - a[1]).Normalized() * ch.StartExt);
        if (ch.EndExt > 0) a.Add(a[^1] + (a[^1] - a[^2]).Normalized() * ch.EndExt);
        return a;
    }

    // ------------------------------------------------------------ linhas finais

    private static void AddCaps(List<(Vec2, Vec2)> caps, Vec2 end, Vec2 prev, ViaRecord via)
    {
        var t = (end - prev).Normalized();
        var m = t.PerpLeft;
        double h = via.Largura / 2 + via.Calcada;
        caps.Add((end - m * h, end + m * h));
    }

    private static bool IsCap((Vec2 a, Vec2 b) seg, List<(Vec2 a, Vec2 b)> caps)
    {
        foreach (var (a, b) in caps)
        {
            if (PolygonUtil.SegmentDistance(seg.a, a, b) <= 0.05 && PolygonUtil.SegmentDistance(seg.b, a, b) <= 0.05) return true;
        }
        return false;
    }

    private static bool OnLots((Vec2 a, Vec2 b) seg, List<List<Vec2>> polys)
    {
        var mid = Vec2.Lerp(seg.a, seg.b, 0.5);
        foreach (var poly in polys)
        {
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % n];
                if (PolygonUtil.SegmentDistance(mid, p, q) <= 0.03 &&
                    PolygonUtil.SegmentDistance(seg.a, p, q) <= 0.03 && PolygonUtil.SegmentDistance(seg.b, p, q) <= 0.03) return true;
            }
            // Peça sobre o contorno, cruzando um vértice do lote.
            double best = double.PositiveInfinity;
            for (int i = 0; i < n; i++) best = Math.Min(best, PolygonUtil.SegmentDistance(mid, poly[i], poly[(i + 1) % n]));
            if (best <= 0.03 && SegDistToPoly(seg.a, poly) <= 0.03 && SegDistToPoly(seg.b, poly) <= 0.03) return true;
        }
        return false;
    }

    private static double SegDistToPoly(Vec2 p, List<Vec2> poly)
    {
        double best = double.PositiveInfinity;
        for (int i = 0; i < poly.Count; i++) best = Math.Min(best, PolygonUtil.SegmentDistance(p, poly[i], poly[(i + 1) % poly.Count]));
        return best;
    }

    /// <summary>Divide o contorno fechado em pedaços curtos, mantém os aprovados e junta-os em polilinhas.</summary>
    private static List<List<Vec2>> KeepPieces(List<Vec2> ring, Func<(Vec2 a, Vec2 b), bool> keep)
    {
        var pieces = new List<(Vec2 a, Vec2 b, bool k)>();
        int n = ring.Count;
        for (int i = 0; i < n; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % n];
            double len = a.DistanceTo(b);
            if (len < 1e-6) continue;
            int parts = Math.Max(1, (int)Math.Ceiling(len / 0.5));
            for (int k = 0; k < parts; k++)
            {
                var pa = Vec2.Lerp(a, b, (double)k / parts);
                var pb = Vec2.Lerp(a, b, (double)(k + 1) / parts);
                pieces.Add((pa, pb, keep((pa, pb))));
            }
        }
        var res = new List<List<Vec2>>();
        if (pieces.Count == 0) return res;
        // Começa após um pedaço descartado, para não cortar uma sequência ao meio.
        int start = pieces.FindIndex(p => !p.k);
        if (start < 0)
        {
            var all = pieces.Select(p => p.a).ToList();
            all.Add(pieces[0].a);
            res.Add(Merge(all));
            return res;
        }
        List<Vec2>? cur = null;
        for (int j = 1; j <= pieces.Count; j++)
        {
            var p = pieces[(start + j) % pieces.Count];
            if (p.k)
            {
                cur ??= new List<Vec2> { p.a };
                cur.Add(p.b);
            }
            else if (cur is not null)
            {
                res.Add(Merge(cur));
                cur = null;
            }
        }
        if (cur is not null) res.Add(Merge(cur));
        return res.Where(r => r.Count >= 2 && PolyLength(r, false) > 0.05).ToList();
    }

    /// <summary>Remove pontos intermediários alinhados.</summary>
    private static List<Vec2> Merge(List<Vec2> pts)
    {
        var res = new List<Vec2>();
        foreach (var p in pts)
        {
            if (res.Count > 0 && res[^1].DistanceTo(p) < 1e-6) continue;
            if (res.Count >= 2)
            {
                var u = (res[^1] - res[^2]).Normalized();
                var v = (p - res[^1]).Normalized();
                if (Math.Abs(u.Cross(v)) < 1e-6 && u.Dot(v) > 0) { res[^1] = p; continue; }
            }
            res.Add(p);
        }
        return res;
    }

    private static double PolyLength(List<Vec2> pts, bool closed)
    {
        double s = 0;
        for (int i = 0; i < pts.Count - 1; i++) s += pts[i].DistanceTo(pts[i + 1]);
        if (closed && pts.Count > 2) s += pts[^1].DistanceTo(pts[0]);
        return s;
    }

    private static (Vec2, double) PointAtHalf(List<Vec2> pts, bool closed)
    {
        var list = new List<Vec2>(pts);
        if (closed && list.Count > 2) list.Add(list[0]);
        double total = PolyLength(list, false), half = total / 2, acc = 0;
        for (int i = 0; i < list.Count - 1; i++)
        {
            double l = list[i].DistanceTo(list[i + 1]);
            if (acc + l >= half && l > 0)
            {
                var d = list[i + 1] - list[i];
                return (Vec2.Lerp(list[i], list[i + 1], (half - acc) / l), Math.Atan2(d.Y, d.X));
            }
            acc += l;
        }
        return (list[0], 0);
    }

    /// <summary>Ângulo de texto legível (entre -90° e +90°).</summary>
    public static double Readable(double a)
    {
        a = AngleUtil.NormPi(a);
        if (a > Math.PI / 2 + 1e-9) a -= Math.PI;
        else if (a <= -Math.PI / 2 + 1e-9) a += Math.PI;
        return a;
    }
}
