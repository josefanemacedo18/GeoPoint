using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;

namespace GeoPoint.Core.Calc;

public enum AnnoTextKind { Distance, Azimuth, Vertex, LotLabel, Confrontante, Measure }

public sealed class AnnoText
{
    public string Owner { get; init; } = "";
    public string Key { get; init; } = "";
    public AnnoTextKind Kind { get; init; }
    public Vec2 At { get; init; }
    public string Text { get; init; } = "";
    /// <summary>Rotação (rad) no plano do desenho.</summary>
    public double Rotation { get; init; }
}

public sealed class AnnoDot
{
    public string Owner { get; init; } = "";
    public string Key { get; init; } = "";
    public Vec2 At { get; init; }
    public double Radius { get; init; }
}

public enum AnnoLineKind { ConfrontanteTick, ConfrontanteDimension }

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
}

/// <summary>
/// Posiciona as anotações de um conjunto de lotes numa vista: textos das divisas fora do lote, nomes dos vértices
/// fora do conjunto, pontos nos vértices e extensão de cada confrontante. Divisas e vértices compartilhados são anotados uma vez só.
/// </summary>
public static class AnnotationPlanner
{
    public const double KeyTol = 0.001;
    private const double LineFactor = 1.45;

    public static string VertexKey(Vec2 p) => $"v:{Math.Round(p.X / KeyTol)}:{Math.Round(p.Y / KeyTol)}";

    public static string EdgeKey(Curve2 c)
    {
        string a = VertexKey(c.Start), b = VertexKey(c.End), m = VertexKey(c.Mid);
        return string.CompareOrdinal(a, b) <= 0 ? $"e:{a}|{b}|{m}" : $"e:{b}|{a}|{m}";
    }

    /// <param name="existingKeys">Chaves já anotadas na vista por outros lotes (não serão repetidas).</param>
    public static AnnotationPlan Plan(IReadOnlyList<LotReport> reports, ValueFormatter fmt, AnnotationPlanOptions o, ISet<string>? existingKeys = null)
    {
        var plan = new AnnotationPlan();
        var used = new HashSet<string>(existingKeys ?? new HashSet<string>());
        var regions = reports.Select(r => r.Region).ToList();
        var allEdges = reports.SelectMany(r => r.Segments.Select(s => s.Curve)).ToList();
        // Divisas compartilhadas entre lotes anotados (internas ao conjunto).
        var edgeCount = new Dictionary<string, int>();
        foreach (var c in allEdges) { var k = EdgeKey(c); edgeCount[k] = edgeCount.TryGetValue(k, out int n) ? n + 1 : 1; }
        bool InsideAny(Vec2 p) => regions.Any(r => r.Bounds.Contains(p) && r.Contains(p));
        double line = o.TextM * LineFactor;

        foreach (var rep in reports)
        {
            string owner = rep.Lot.Id;
            int n = rep.Segments.Count;

            // ---- Divisas: distância (mais perto da linha) e azimute/rumo (logo depois), do lado de fora.
            if (o.Edges)
            {
                foreach (var s in rep.Segments)
                {
                    string key = EdgeKey(s.Curve);
                    if (!used.Add(key)) continue;
                    var mid = s.Curve.PointAt(0.5);
                    var tan = s.Curve.TangentAt(0.5);
                    var outward = s.OutwardNormal.LengthSquared > 1e-12 ? s.OutwardNormal.Normalized() : (rep.Clockwise ? tan.PerpLeft : tan.PerpRight);
                    var side = o.Outside ? outward : -outward;
                    double rot = Math.Atan2(tan.Y, tan.X);
                    string dist = s.IsArc ? $"D={fmt.Distance(s.LengthHorizontal)} R={fmt.Distance(s.Radius ?? 0)}" : fmt.Distance(s.LengthHorizontal);
                    plan.Texts.Add(new AnnoText { Owner = owner, Key = key, Kind = AnnoTextKind.Distance, At = mid + side * (o.OffsetM + line * 0.5), Text = dist, Rotation = rot });
                    if (o.EdgeAngle)
                        plan.Texts.Add(new AnnoText { Owner = owner, Key = key + ":az", Kind = AnnoTextKind.Azimuth, At = mid + side * (o.OffsetM + line * 1.5), Text = fmt.Angle(s.AzimuthTrue), Rotation = rot });
                }
            }

            // ---- Confrontantes: nome do lado de fora e extensão de cada um ao longo de um mesmo lado.
            if (o.Confrontantes && n > 0)
                PlanConfrontantes(plan, rep, o, used, edgeCount, line);

            // ---- Vértices: ponto preenchido e nome fora do conjunto de lotes.
            for (int i = 0; i < n; i++)
            {
                var v = rep.Vertices[i];
                string vk = VertexKey(v.Internal);
                if (o.Dots && used.Add("d" + vk))
                    plan.Dots.Add(new AnnoDot { Owner = owner, Key = "d" + vk, At = v.Internal, Radius = o.DotRadiusM });
                if (!o.Vertices || !used.Add("n" + vk)) continue;
                var tIn = rep.Segments[(i - 1 + n) % n].Curve.TangentAt(1);
                var tOut = rep.Segments[i].Curve.TangentAt(0);
                var bis = tOut - tIn;
                if (bis.LengthSquared < 1e-12) bis = rep.Clockwise ? tOut.PerpLeft : tOut.PerpRight;
                bis = bis.Normalized();
                double reach = o.OffsetM + o.TextM * 1.2 + o.DotRadiusM;
                if (InsideAny(v.Internal + bis * reach)) bis = -bis;
                var dir = BestDirection(v.Internal, bis, reach, InsideAny, allEdges, o.TextM);
                plan.Texts.Add(new AnnoText { Owner = owner, Key = "n" + vk, Kind = AnnoTextKind.Vertex, At = v.Internal + dir * reach, Text = v.Name, Rotation = 0 });
            }

            // ---- Nome e área no interior.
            if (o.Area)
            {
                string label = rep.Lot.IsRemanescente ? $"{rep.Lot.DisplayName.ToUpperInvariant()}\n{fmt.Area(rep.AreaLocal)}" : $"{rep.Lot.DisplayName}\n{fmt.Area(rep.AreaLocal)}";
                plan.Texts.Add(new AnnoText { Owner = owner, Key = "a:" + owner, Kind = AnnoTextKind.LotLabel, At = rep.Region.InteriorPoint(), Text = label, Rotation = 0 });
            }
        }
        return plan;
    }

    /// <summary>Escolhe a direção do rótulo do vértice: fora dos lotes e longe das divisas.</summary>
    private static Vec2 BestDirection(Vec2 p, Vec2 preferred, double reach, Func<Vec2, bool> insideAny, List<Curve2> edges, double textM)
    {
        var near = edges.Where(e => e.Bounds.Expand(reach * 2).Contains(p)).ToList();
        double Score(Vec2 d)
        {
            var q = p + d * reach;
            if (insideAny(q)) return double.NegativeInfinity;
            double clear = near.Count == 0 ? reach : near.Min(e => e.DistanceTo(q));
            return Math.Min(clear, textM) + 0.15 * d.Dot(preferred) * textM;
        }
        Vec2 best = preferred;
        double bestScore = Score(preferred);
        for (int k = 0; k < 16; k++)
        {
            var d = Vec2.FromAngle(k * Math.PI / 8);
            double sc = Score(d);
            if (sc > bestScore + 1e-9) { best = d; bestScore = sc; }
        }
        return best;
    }

    private static void PlanConfrontantes(AnnotationPlan plan, LotReport rep, AnnotationPlanOptions o, HashSet<string> used, Dictionary<string, int> edgeCount, double line)
    {
        string owner = rep.Lot.Id;
        int n = rep.Segments.Count;
        // Agrupa divisas consecutivas alinhadas (mesmo "lado" do lote).
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
        double textBand = o.OffsetM + line * (o.Edges ? (o.EdgeAngle ? 2.2 : 1.2) : 0.2);
        foreach (var g in groups)
        {
            // Divisa interna ao conjunto anotado: o nome do vizinho já aparece no lote vizinho.
            if (g.All(s => edgeCount.TryGetValue(EdgeKey(s.Curve), out int c) && c > 1)) continue;
            // Trechos do lado com o mesmo confrontante.
            var runs = new List<List<SegmentReport>>();
            foreach (var s in g)
            {
                if (runs.Count == 0 || !SameConfrontante(runs[^1][^1], s)) runs.Add(new List<SegmentReport>());
                runs[^1].Add(s);
            }
            bool split = runs.Count > 1;
            double dimOff = textBand + line * 0.3;
            foreach (var run in runs)
            {
                if (run.All(s => edgeCount.TryGetValue(EdgeKey(s.Curve), out int c) && c > 1)) continue;
                string name = run[0].Confrontante;
                var a = run[0].Curve.Start;
                var b = run[^1].Curve.End;
                var midSeg = run[run.Count / 2];
                var midPt = run.Count % 2 == 1 ? midSeg.Curve.PointAt(0.5) : midSeg.Curve.Start;
                var tan = midSeg.Curve.TangentAt(run.Count % 2 == 1 ? 0.5 : 0);
                var outward = midSeg.OutwardNormal.LengthSquared > 1e-12 ? midSeg.OutwardNormal.Normalized() : (rep.Clockwise ? tan.PerpLeft : tan.PerpRight);
                double rot = Math.Atan2(tan.Y, tan.X);
                string key = "c:" + string.Join("+", run.Select(s => EdgeKey(s.Curve)));
                if (!used.Add(key)) continue;
                if (split)
                {
                    // Cota da extensão do confrontante (paralela ao lado), com marcas nas pontas.
                    var oa = a + Normal(run[0], rep) * dimOff;
                    var ob = b + Normal(run[^1], rep) * dimOff;
                    double runLen = run.Sum(s => s.LengthHorizontal);
                    if (run.All(s => !s.IsArc))
                        plan.Lines.Add(new AnnoLine { Owner = owner, Key = key + ":dim", Kind = AnnoLineKind.ConfrontanteDimension, A = oa, B = ob });
                    double tick = o.TextM * 0.6;
                    foreach (var (p, nrm) in new[] { (a, Normal(run[0], rep)), (b, Normal(run[^1], rep)) })
                    {
                        string tk = "t" + VertexKey(p);
                        if (used.Add(tk))
                            plan.Lines.Add(new AnnoLine { Owner = owner, Key = tk, Kind = AnnoLineKind.ConfrontanteTick, A = p + nrm * (o.DotRadiusM * 1.5), B = p + nrm * (dimOff + tick) });
                    }
                    if (run.Count > 1)
                        plan.Texts.Add(new AnnoText { Owner = owner, Key = key + ":len", Kind = AnnoTextKind.Measure, At = midPt + outward * (dimOff + line * 0.55), Text = FormatLen(runLen), Rotation = rot });
                }
                if (string.IsNullOrWhiteSpace(name)) continue;
                double nameOff = split ? dimOff + line * (run.Count > 1 ? 1.6 : 0.6) : textBand + line * 0.5;
                plan.Texts.Add(new AnnoText { Owner = owner, Key = key, Kind = AnnoTextKind.Confrontante, At = midPt + outward * nameOff, Text = name, Rotation = rot });
            }
        }
    }

    private static string FormatLen(double m) => AngleText.FormatNumber(m, 2, true) + " m";

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
