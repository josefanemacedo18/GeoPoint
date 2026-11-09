using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;

namespace GeoPoint.Core.Calc;

public enum AnnoTextKind { Distance, Azimuth, Vertex, LotLabel, Confrontante, Measure }

/// <summary>Caixa orientada (retângulo girado) usada para evitar sobreposição de textos.</summary>
public readonly record struct Obb(Vec2 C, Vec2 U, double Hw, double Hh)
{
    public Vec2 V => U.PerpLeft;

    public static Obb Of(Vec2 center, double angle, double hw, double hh) => new(center, Vec2.FromAngle(angle), hw, hh);

    private double Radius(Vec2 n) => Hw * Math.Abs(U.Dot(n)) + Hh * Math.Abs(V.Dot(n));

    public bool Intersects(Obb o, double margin = 0)
    {
        var d = o.C - C;
        foreach (var n in new[] { U, V, o.U, o.V })
            if (Math.Abs(d.Dot(n)) > Radius(n) + o.Radius(n) + margin) return false;
        return true;
    }

    /// <summary>Profundidade aproximada da sobreposição (0 se não sobrepõe).</summary>
    public double Penetration(Obb o)
    {
        var d = o.C - C;
        double min = double.MaxValue;
        foreach (var n in new[] { U, V, o.U, o.V })
        {
            double p = Radius(n) + o.Radius(n) - Math.Abs(d.Dot(n));
            if (p <= 0) return 0;
            min = Math.Min(min, p);
        }
        return min;
    }

    public bool Contains(Vec2 p)
    {
        var d = p - C;
        return Math.Abs(d.Dot(U)) <= Hw && Math.Abs(d.Dot(V)) <= Hh;
    }

    /// <summary>O segmento atravessa (ou toca) a caixa?</summary>
    public bool Intersects(Vec2 a, Vec2 b)
    {
        double ax = (a - C).Dot(U), ay = (a - C).Dot(V), bx = (b - C).Dot(U), by = (b - C).Dot(V);
        double t0 = 0, t1 = 1, dx = bx - ax, dy = by - ay;
        bool Clip(double p, double q)
        {
            if (Math.Abs(p) < 1e-15) return q >= 0;
            double r = q / p;
            if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
            return true;
        }
        return Clip(-dx, ax + Hw) && Clip(dx, Hw - ax) && Clip(-dy, ay + Hh) && Clip(dy, Hh - ay) && t0 <= t1;
    }
}

public sealed class AnnoText
{
    public string Owner { get; init; } = "";
    public string Key { get; init; } = "";
    public AnnoTextKind Kind { get; init; }
    /// <summary>Centro da caixa do texto.</summary>
    public Vec2 At { get; init; }
    public string Text { get; init; } = "";
    /// <summary>Rotação (rad) já no sentido de leitura (entre -90° e +90°).</summary>
    public double Rotation { get; init; }
    public double HalfW { get; init; }
    public double HalfH { get; init; }
    public int Lines { get; init; } = 1;
    public Obb Box => Obb.Of(At, Rotation, HalfW, HalfH);
    /// <summary>Ponto de inserção do Revit para texto centralizado: meio da borda superior da caixa.</summary>
    public Vec2 TopCenter => At + Vec2.FromAngle(Rotation).PerpLeft * HalfH;
}

public sealed class AnnoDot
{
    public string Owner { get; init; } = "";
    public string Key { get; init; } = "";
    public Vec2 At { get; init; }
    public double Radius { get; init; }
}

public enum AnnoLineKind { ConfrontanteTick, ConfrontanteDimension, Leader }

public sealed class AnnoLine
{
    public string Owner { get; init; } = "";
    public string Key { get; init; } = "";
    public AnnoLineKind Kind { get; init; }
    public Vec2 A { get; init; }
    public Vec2 B { get; init; }
}

public sealed class AnnotationPlan
{
    public List<AnnoText> Texts { get; } = new();
    public List<AnnoDot> Dots { get; } = new();
    public List<AnnoLine> Lines { get; } = new();
    /// <summary>Textos que não couberam sem sobreposição (posição de menor conflito).</summary>
    public int Conflicts { get; set; }
    public IEnumerable<string> KeysOf(string owner) =>
        Texts.Where(t => t.Owner == owner).Select(t => t.Key)
            .Concat(Dots.Where(d => d.Owner == owner).Select(d => d.Key))
            .Concat(Lines.Where(l => l.Owner == owner).Select(l => l.Key))
            .Where(k => k.Length > 0).Distinct();
}

public sealed class AnnotationPlanOptions
{
    public bool Edges { get; set; } = true;
    public bool EdgeAngle { get; set; } = true;
    public bool Vertices { get; set; } = true;
    public bool Area { get; set; } = true;
    public bool Dots { get; set; } = true;
    /// <summary>Textos das divisas do lado de fora do lote.</summary>
    public bool Outside { get; set; } = true;
    /// <summary>Nome do confrontante e marcação da extensão de cada confrontante.</summary>
    public bool Confrontantes { get; set; } = true;
    /// <summary>Altura do texto no modelo (m) = altura no papel × escala.</summary>
    public double TextM { get; set; } = 1.0;
    /// <summary>Afastamento entre linha e texto no modelo (m).</summary>
    public double OffsetM { get; set; } = 0.75;
    /// <summary>Raio do ponto do vértice no modelo (m).</summary>
    public double DotRadiusM { get; set; } = 0.3;
    /// <summary>Margem interna da caixa do texto no modelo (m) (deslocamento de borda do tipo de texto).</summary>
    public double PadM { get; set; } = 0.1;
    /// <summary>Altura de uma linha de texto em relação à altura do texto.</summary>
    public double LineFactor { get; set; } = 1.5;
}

/// <summary>
/// Posiciona as anotações de um conjunto de lotes numa vista sem sobreposição: distância e azimute do lado de fora de cada
/// divisa (alinhados à divisa e legíveis), nomes dos vértices fora dos lotes, ponto nos vértices, nome e área no interior e a
/// extensão de cada confrontante. Divisas e vértices compartilhados são anotados uma vez só.
/// </summary>
public static class AnnotationPlanner
{
    public const double KeyTol = 0.001;

    public static string VertexKey(Vec2 p) => $"v:{Math.Round(p.X / KeyTol)}:{Math.Round(p.Y / KeyTol)}";

    public static string EdgeKey(Curve2 c)
    {
        string a = VertexKey(c.Start), b = VertexKey(c.End), m = VertexKey(c.Mid);
        return string.CompareOrdinal(a, b) <= 0 ? $"e:{a}|{b}|{m}" : $"e:{b}|{a}|{m}";
    }

    /// <summary>Ângulo de leitura (texto nunca de cabeça para baixo): resultado em (-90°, +90°].</summary>
    public static double Readable(double a)
    {
        a = AngleUtil.NormPi(a);
        if (a > Math.PI / 2 + 1e-9) a -= Math.PI;
        else if (a <= -Math.PI / 2 + 1e-9) a += Math.PI;
        return a;
    }

    /// <summary>Largura estimada (m) de um texto Arial com altura <paramref name="h"/>.</summary>
    public static double TextWidth(string s, double h, bool bold = false)
    {
        double w = 0;
        foreach (char c in s)
        {
            w += c switch
            {
                '°' or '\'' or '"' or ',' or '.' or ':' or ';' or '|' or 'i' or 'l' or 'I' or 'j' or '!' => 0.32,
                ' ' => 0.3,
                '-' or '(' or ')' or 'r' or 't' or 'f' => 0.4,
                'm' or 'M' or 'W' or 'w' => 0.9,
                >= '0' and <= '9' => 0.6,
                _ when char.IsUpper(c) => 0.7,
                _ => 0.58,
            };
        }
        return w * h * (bold ? 1.08 : 1.0);
    }

    private sealed class Ctx
    {
        public readonly List<Obb> Placed = new();
        public readonly List<(Vec2 A, Vec2 B)> Segs = new();
        public readonly List<Region> Regions = new();
        public double Margin;

        public bool Free(Obb b, int ignoreSeg = -1)
        {
            foreach (var p in Placed) if (p.Intersects(b, Margin)) return false;
            for (int i = 0; i < Segs.Count; i++)
            {
                if (i == ignoreSeg) continue;
                var (a, c) = Segs[i];
                if (b.Intersects(a, c)) return false;
            }
            return true;
        }

        public double Cost(Obb b)
        {
            double c = 0;
            foreach (var p in Placed) { double pen = p.Penetration(b); if (pen > 0) c += 10 + pen * 100; }
            foreach (var (a, d) in Segs) if (b.Intersects(a, d)) c += 6;
            return c;
        }

        public bool InsideAny(Vec2 p) => Regions.Any(r => r.Bounds.Contains(p) && r.Contains(p));
    }

    /// <param name="existingKeys">Chaves já anotadas na vista por outros lotes (não serão repetidas).</param>
    public static AnnotationPlan Plan(IReadOnlyList<LotReport> reports, ValueFormatter fmt, AnnotationPlanOptions o, ISet<string>? existingKeys = null)
    {
        var plan = new AnnotationPlan();
        var used = new HashSet<string>(existingKeys ?? new HashSet<string>());
        var cx = new Ctx { Margin = o.TextM * 0.12 };
        foreach (var r in reports)
        {
            cx.Regions.Add(r.Region);
            foreach (var ring in r.Region.AllRings())
                foreach (var e in ring.Edges)
                {
                    var pts = e.Densify(Math.Max(o.TextM * 0.05, 0.005));
                    for (int i = 0; i + 1 < pts.Count; i++) cx.Segs.Add((pts[i], pts[i + 1]));
                }
        }
        var edgeCount = new Dictionary<string, int>();
        foreach (var c in reports.SelectMany(r => r.Segments.Select(s => s.Curve)))
        {
            var k = EdgeKey(c);
            edgeCount[k] = edgeCount.TryGetValue(k, out int n) ? n + 1 : 1;
        }
        double hh = o.TextM * o.LineFactor / 2 + o.PadM;   // meia altura de uma linha
        double gap = o.TextM * 0.25;

        // 1) Pontos dos vértices (também são obstáculos para os textos).
        foreach (var rep in reports)
            foreach (var v in rep.Vertices)
            {
                string vk = VertexKey(v.Internal);
                if (o.Dots && used.Add("d" + vk))
                    plan.Dots.Add(new AnnoDot { Owner = rep.Lot.Id, Key = "d" + vk, At = v.Internal, Radius = o.DotRadiusM });
                cx.Placed.Add(Obb.Of(v.Internal, 0, o.DotRadiusM * 1.3, o.DotRadiusM * 1.3));
            }

        // 2) Nome e área no interior (ponto com mais folga).
        if (o.Area)
            foreach (var rep in reports)
            {
                string label = $"{(rep.Lot.IsRemanescente ? rep.Lot.DisplayName.ToUpperInvariant() : rep.Lot.DisplayName)}\n{fmt.Area(rep.AreaLocal)}";
                var lines = label.Split('\n');
                double h = o.TextM * 1.25;
                double hw = lines.Max(l => TextWidth(l, h, true)) / 2 + o.PadM;
                double lh = lines.Length * h * o.LineFactor / 2 + o.PadM;
                var at = BestInterior(rep.Region, hw, lh, cx);
                var box = Obb.Of(at, 0, hw, lh);
                cx.Placed.Add(box);
                plan.Texts.Add(new AnnoText { Owner = rep.Lot.Id, Key = "a:" + rep.Lot.Id, Kind = AnnoTextKind.LotLabel, At = at, Text = label, HalfW = hw, HalfH = lh, Lines = lines.Length });
            }

        // 3) Divisas: distância (junto da linha) e azimute (logo depois), do lado de fora.
        var blockSide = new Dictionary<string, double>(); // chave da divisa → +1 fora, -1 dentro
        if (o.Edges)
            foreach (var rep in reports)
                foreach (var s in rep.Segments)
                {
                    string key = EdgeKey(s.Curve);
                    if (!used.Add(key)) continue;
                    PlaceEdge(plan, cx, rep, s, key, fmt, o, hh, gap, blockSide);
                }

        // 4) Confrontantes (nome e extensão).
        if (o.Confrontantes)
            foreach (var rep in reports)
                PlanConfrontantes(plan, cx, rep, o, used, edgeCount, hh, gap);

        // 5) Nomes dos vértices: fora dos lotes, sem cruzar linhas nem outros textos.
        if (o.Vertices)
            foreach (var rep in reports)
            {
                int n = rep.Segments.Count;
                for (int i = 0; i < n; i++)
                {
                    var v = rep.Vertices[i];
                    string vk = "n" + VertexKey(v.Internal);
                    if (!used.Add(vk)) continue;
                    var tIn = rep.Segments[(i - 1 + n) % n].Curve.TangentAt(1);
                    var tOut = rep.Segments[i].Curve.TangentAt(0);
                    var bis = tOut - tIn;
                    if (bis.LengthSquared < 1e-12) bis = rep.Clockwise ? tOut.PerpLeft : tOut.PerpRight;
                    bis = bis.Normalized();
                    if (cx.InsideAny(v.Internal + bis * o.TextM)) bis = -bis;
                    double hw = TextWidth(v.Name, o.TextM) / 2 + o.PadM;
                    var (box, ok) = PlaceVertex(cx, v.Internal, bis, hw, hh, o.DotRadiusM + gap);
                    if (!ok) plan.Conflicts++;
                    cx.Placed.Add(box);
                    plan.Texts.Add(new AnnoText { Owner = rep.Lot.Id, Key = vk, Kind = AnnoTextKind.Vertex, At = box.C, Text = v.Name, HalfW = hw, HalfH = hh });
                }
            }
        return plan;
    }

    private static Vec2 BestInterior(Region r, double hw, double hh, Ctx cx)
    {
        var bb = r.Bounds;
        var cands = new List<Vec2> { r.InteriorPoint() };
        const int N = 14;
        for (int i = 1; i < N; i++)
            for (int j = 1; j < N; j++)
                cands.Add(new Vec2(bb.MinX + bb.Width * i / N, bb.MinY + bb.Height * j / N));
        var edges = r.AllRings().SelectMany(x => x.Edges).ToList();
        Vec2 best = cands[0];
        double bestScore = double.NegativeInfinity;
        var centroid = bb.Center;
        foreach (var p in cands)
        {
            if (!r.Contains(p)) continue;
            var box = Obb.Of(p, 0, hw, hh);
            double clear = edges.Min(e => e.DistanceTo(p));
            bool fits = cx.Free(box);
            double score = (fits ? 1000 : 0) + Math.Min(clear, Math.Max(hw, hh) * 3) - 0.05 * p.DistanceTo(centroid);
            if (score > bestScore) { bestScore = score; best = p; }
        }
        return best;
    }

    private static void PlaceEdge(AnnotationPlan plan, Ctx cx, LotReport rep, SegmentReport s, string key, ValueFormatter fmt,
        AnnotationPlanOptions o, double hh, double gap, Dictionary<string, double> blockSide)
    {
        string owner = rep.Lot.Id;
        string dist = s.IsArc ? $"D={fmt.Distance(s.LengthHorizontal)} R={fmt.Distance(s.Radius ?? 0)}" : fmt.Distance(s.LengthHorizontal);
        string az = fmt.Angle(s.AzimuthTrue);
        double wD = TextWidth(dist, o.TextM) / 2 + o.PadM;
        double wA = TextWidth(az, o.TextM) / 2 + o.PadM;
        double len = s.Curve.Length;
        bool shortEdge = len < 2 * Math.Max(wD, o.EdgeAngle ? wA : 0) * 1.05;
        bool withAz = o.EdgeAngle && !shortEdge;
        var outward = Normal(s, rep);
        var sides = o.Outside ? new[] { 1.0, -1.0 } : new[] { -1.0, 1.0 };
        var ts = new[] { 0.5, 0.42, 0.58, 0.34, 0.66, 0.26, 0.74 };
        var extras = shortEdge ? new[] { 0.0, 2.0, 4.0, 6.5 } : new[] { 0.0, 0.8, 2.0 };

        (Obb d, Obb? a, double side, Vec2 p, double extra)? best = null;
        double bestCost = double.MaxValue;
        foreach (double extra in extras)
        {
            foreach (double side in sides)
            {
                foreach (double t in shortEdge ? new[] { 0.5 } : ts)
                {
                    var p = s.Curve.PointAt(t);
                    var tan = s.Curve.TangentAt(t);
                    var n = s.IsArc ? NormalAt(s.Curve, t, outward) : outward;
                    n *= side;
                    double ang = Readable(Math.Atan2(tan.Y, tan.X));
                    double off = o.OffsetM + extra * hh;
                    var dBox = Obb.Of(p + n * (off + hh), ang, wD, hh);
                    Obb? aBox = withAz ? Obb.Of(p + n * (off + 3 * hh + gap * 0.3), ang, wA, hh) : null;
                    double cost = cx.Cost(dBox) + (aBox is { } ab ? cx.Cost(ab) : 0);
                    // Preferências: lado de fora, centro da divisa, perto da linha.
                    cost += (side < 0 ? 0.6 : 0) + Math.Abs(t - 0.5) * 0.8 + extra * 0.3;
                    if (cost < bestCost) { bestCost = cost; best = (dBox, aBox, side, p, extra); }
                }
                if (bestCost < 1) break;
            }
            if (bestCost < 1) break;
        }
        var (db, abx, sd, pt, ex) = best!.Value;
        if (bestCost >= 6) plan.Conflicts++;
        blockSide[key] = sd;
        cx.Placed.Add(db);
        plan.Texts.Add(new AnnoText { Owner = owner, Key = key, Kind = AnnoTextKind.Distance, At = db.C, Text = dist, Rotation = Math.Atan2(db.U.Y, db.U.X), HalfW = db.Hw, HalfH = db.Hh });
        if (abx is { } a)
        {
            cx.Placed.Add(a);
            plan.Texts.Add(new AnnoText { Owner = owner, Key = key + ":az", Kind = AnnoTextKind.Azimuth, At = a.C, Text = az, Rotation = Math.Atan2(a.U.Y, a.U.X), HalfW = a.Hw, HalfH = a.Hh });
        }
        // Texto afastado da linha: linha de chamada até a divisa.
        if (ex > 1.5)
        {
            var near = db.C - (db.C - pt).Normalized() * Math.Min(db.Hh, (db.C - pt).Length);
            plan.Lines.Add(new AnnoLine { Owner = owner, Key = key + ":ld", Kind = AnnoLineKind.Leader, A = pt, B = near });
        }
    }

    private static (Obb box, bool ok) PlaceVertex(Ctx cx, Vec2 v, Vec2 preferred, double hw, double hh, double clearance)
    {
        Obb best = default;
        double bestCost = double.MaxValue;
        foreach (double mult in new[] { 1.0, 1.8, 2.8 })
            for (int k = 0; k < 24; k++)
            {
                var d = k == 0 ? preferred : Vec2.FromAngle(k * Math.PI / 12);
                double support = Math.Abs(d.X) * hw + Math.Abs(d.Y) * hh;
                var c = v + d * (clearance * mult + support);
                var box = Obb.Of(c, 0, hw, hh);
                double cost = cx.Cost(box) + (cx.InsideAny(c) ? 4 : 0) + (1 - d.Dot(preferred)) * 0.4 + (mult - 1) * 0.8;
                if (cost < bestCost) { bestCost = cost; best = box; }
            }
        return (best, bestCost < 4);
    }

    private static void PlanConfrontantes(AnnotationPlan plan, Ctx cx, LotReport rep, AnnotationPlanOptions o, HashSet<string> used,
        Dictionary<string, int> edgeCount, double hh, double gap)
    {
        string owner = rep.Lot.Id;
        int n = rep.Segments.Count;
        if (n == 0) return;
        bool Collinear(int i)
        {
            var a = rep.Segments[(i - 1 + n) % n].Curve.TangentAt(1);
            var b = rep.Segments[i].Curve.TangentAt(0);
            return a.Dot(b) > Math.Cos(Math.PI / 90); // < 2°
        }
        int start = Enumerable.Range(0, n).FirstOrDefault(i => !Collinear(i));
        var groups = new List<List<SegmentReport>>();
        for (int k = 0; k < n; k++)
        {
            int i = (start + k) % n;
            if (k == 0 || !Collinear(i)) groups.Add(new List<SegmentReport>());
            groups[^1].Add(rep.Segments[i]);
        }
        double block = o.OffsetM + (o.Edges ? (o.EdgeAngle ? 4 * hh + gap * 0.3 : 2 * hh) : 0) + gap;
        foreach (var g in groups)
        {
            bool Shared(SegmentReport s) => edgeCount.TryGetValue(EdgeKey(s.Curve), out int c) && c > 1;
            if (g.All(Shared)) continue;
            var runs = new List<List<SegmentReport>>();
            foreach (var s in g)
            {
                if (runs.Count == 0 || !SameConfrontante(runs[^1][^1], s)) runs.Add(new List<SegmentReport>());
                runs[^1].Add(s);
            }
            bool split = runs.Count > 1;
            double dimOff = block + gap;
            foreach (var run in runs)
            {
                if (run.All(Shared)) continue;
                string key = "c:" + string.Join("+", run.Select(s => EdgeKey(s.Curve)));
                if (!used.Add(key)) continue;
                var a = run[0].Curve.Start;
                var b = run[^1].Curve.End;
                var na = Normal(run[0], rep);
                var nb = Normal(run[^1], rep);
                double runLen = run.Sum(s => s.LengthHorizontal);
                if (split)
                {
                    if (run.All(s => !s.IsArc))
                        plan.Lines.Add(new AnnoLine { Owner = owner, Key = key + ":dim", Kind = AnnoLineKind.ConfrontanteDimension, A = a + na * dimOff, B = b + nb * dimOff });
                    double tick = o.TextM * 0.6;
                    foreach (var (p, nrm) in new[] { (a, na), (b, nb) })
                    {
                        string tk = "t" + VertexKey(p);
                        if (used.Add(tk))
                            plan.Lines.Add(new AnnoLine { Owner = owner, Key = tk, Kind = AnnoLineKind.ConfrontanteTick, A = p + nrm * (o.DotRadiusM * 1.5), B = p + nrm * (dimOff + tick) });
                    }
                }
                // Ponto médio do trecho (pelo comprimento).
                var (mid, tan, nrmMid) = PointAlongRun(run, runLen / 2, rep);
                double ang = Readable(Math.Atan2(tan.Y, tan.X));
                double baseOff = split ? dimOff : block;
                if (split && run.Count > 1)
                {
                    string meas = AngleText.FormatNumber(runLen, 2, true) + " m";
                    double mw = TextWidth(meas, o.TextM) / 2 + o.PadM;
                    var mb = Obb.Of(mid + nrmMid * (baseOff + gap + hh), ang, mw, hh);
                    cx.Placed.Add(mb);
                    plan.Texts.Add(new AnnoText { Owner = owner, Key = key + ":len", Kind = AnnoTextKind.Measure, At = mb.C, Text = meas, Rotation = ang, HalfW = mw, HalfH = hh });
                    baseOff += 2 * hh + gap;
                }
                string name = run[0].Confrontante;
                if (string.IsNullOrWhiteSpace(name)) continue;
                double w = TextWidth(name, o.TextM) / 2 + o.PadM;
                Obb best = default;
                double bestCost = double.MaxValue;
                foreach (double extra in new[] { 0.0, 1.2, 2.6, 4.5 })
                    foreach (double frac in new[] { 0.5, 0.35, 0.65, 0.2, 0.8 })
                    {
                        var (p, t, nm) = PointAlongRun(run, runLen * frac, rep);
                        double an = Readable(Math.Atan2(t.Y, t.X));
                        var box = Obb.Of(p + nm * (baseOff + gap + hh + extra * hh), an, w, hh);
                        double cost = cx.Cost(box) + extra * 0.4 + Math.Abs(frac - 0.5);
                        if (cost < bestCost) { bestCost = cost; best = box; }
                    }
                if (bestCost >= 6) plan.Conflicts++;
                cx.Placed.Add(best);
                plan.Texts.Add(new AnnoText { Owner = owner, Key = key, Kind = AnnoTextKind.Confrontante, At = best.C, Text = name, Rotation = Math.Atan2(best.U.Y, best.U.X), HalfW = w, HalfH = hh });
            }
        }
    }

    private static (Vec2 p, Vec2 tan, Vec2 normal) PointAlongRun(List<SegmentReport> run, double dist, LotReport rep)
    {
        double acc = 0;
        foreach (var s in run)
        {
            double l = s.Curve.Length;
            if (acc + l >= dist - 1e-9 || s == run[^1])
            {
                double t = l > 0 ? Math.Clamp((dist - acc) / l, 0, 1) : 0.5;
                var nrm = s.IsArc ? NormalAt(s.Curve, t, Normal(s, rep)) : Normal(s, rep);
                return (s.Curve.PointAt(t), s.Curve.TangentAt(t), nrm);
            }
            acc += l;
        }
        var last = run[^1];
        return (last.Curve.Mid, last.Curve.TangentAt(0.5), Normal(last, rep));
    }

    private static Vec2 NormalAt(Curve2 c, double t, Vec2 outwardHint)
    {
        var tan = c.TangentAt(t);
        var n = tan.PerpLeft;
        return n.Dot(outwardHint) >= 0 ? n : -n;
    }

    private static Vec2 Normal(SegmentReport s, LotReport rep)
    {
        if (s.OutwardNormal.LengthSquared > 1e-12) return s.OutwardNormal.Normalized();
        var t = s.Curve.TangentAt(0.5);
        return rep.Clockwise ? t.PerpLeft : t.PerpRight;
    }

    private static bool SameConfrontante(SegmentReport a, SegmentReport b)
    {
        if (a.ConfrontanteTipo != b.ConfrontanteTipo) return false;
        if (a.ConfrontanteLotId.Length > 0 || b.ConfrontanteLotId.Length > 0) return a.ConfrontanteLotId == b.ConfrontanteLotId;
        if (a.ConfrontanteViaId.Length > 0 || b.ConfrontanteViaId.Length > 0) return a.ConfrontanteViaId == b.ConfrontanteViaId;
        return string.Equals(a.Confrontante.Trim(), b.Confrontante.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
