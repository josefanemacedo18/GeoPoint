using GeoPoint.Core.Geometry;

namespace GeoPoint.Core.Topology;

/// <summary>Curva de entrada com etiqueta do chamador (ex.: índice do elemento do Revit).</summary>
public sealed record InputCurve(Curve2 Curve, int Tag);

public sealed class NetworkOptions
{
    /// <summary>Tolerância de coincidência de pontos (m). Padrão: 1 mm.</summary>
    public double Tolerance { get; set; } = 0.001;

    /// <summary>Distância máxima para sugerir "falha de conexão" em linhas soltas (m).</summary>
    public double GapSearchDistance { get; set; } = 0.10;

    /// <summary>Faces com área menor que isto geram alerta (m²).</summary>
    public double TinyFaceArea { get; set; } = 0.01;

    /// <summary>Segmentos menores que isto geram alerta (m).</summary>
    public double ShortEdgeLength { get; set; } = 0.01;
}

public enum DiagnosticSeverity { Info = 0, Warning = 1, Error = 2 }

public enum DiagnosticKind
{
    ZeroLength,
    SnappedEndpoints,
    Crossing,
    Duplicate,
    DanglingLine,
    PossibleGap,
    Bridge,
    TinyFace,
    ShortEdge,
}

public sealed class NetworkDiagnostic
{
    public DiagnosticKind Kind { get; init; }
    public DiagnosticSeverity Severity { get; init; }
    public string Message { get; init; } = "";
    public Vec2 Location { get; init; }
    public int[] Tags { get; init; } = Array.Empty<int>();
    public double Value { get; init; }
    public override string ToString() => $"[{Severity}] {Kind}: {Message}";
}

public sealed class NetNode
{
    public int Id { get; init; }
    public Vec2 P { get; set; }
    public bool IsOriginalEndpoint { get; set; }
    internal List<int> Out { get; } = new();
    public int Degree { get; internal set; }
}

public sealed class NetEdge
{
    public int Id { get; init; }
    public int A { get; init; }
    public int B { get; init; }
    public Curve2 Curve { get; init; } = null!;
    public List<int> Tags { get; } = new();
    public bool Removed { get; internal set; }
    public DiagnosticKind? RemovedReason { get; internal set; }
}

public sealed class FaceEdgeRef
{
    public int EdgeId { get; init; }
    public bool Forward { get; init; }
    public Curve2 Curve { get; init; } = null!;
    public int FromNode { get; init; }
    public int ToNode { get; init; }
    public IReadOnlyList<int> Tags { get; init; } = Array.Empty<int>();
}

public sealed class NetFace
{
    public int Id { get; init; }
    public Region Region { get; internal set; } = null!;
    public List<FaceEdgeRef> OuterEdges { get; } = new();
    public List<List<FaceEdgeRef>> HoleEdges { get; } = new();
    public double Area => Region.Area;
    public double OuterArea => Region.Outer.Area;
}

/// <summary>
/// Rede planar: une extremidades por tolerância, quebra as linhas em todos os encontros (inclusive em "T"),
/// remove pontas soltas e extrai as faces fechadas (áreas).
/// </summary>
public sealed class PlanarNetwork
{
    private readonly NetworkOptions _opt;
    private readonly List<NetNode> _nodes = new();
    private readonly List<NetEdge> _edges = new();
    private readonly List<NetFace> _faces = new();
    private readonly List<NetworkDiagnostic> _diag = new();

    // Semiarestas.
    private readonly List<HalfEdge> _half = new();

    private sealed class HalfEdge
    {
        public int Id;
        public int Edge;
        public bool Forward;
        public int From, To;
        public Curve2 Curve = null!;
        public int Twin;
        public int Next = -1;
        public int Face = -1;
        public double Angle;
        public double Curv;
    }

    private PlanarNetwork(NetworkOptions opt) { _opt = opt; }

    public IReadOnlyList<NetNode> Nodes => _nodes;
    public IReadOnlyList<NetEdge> Edges => _edges;
    public IReadOnlyList<NetFace> Faces => _faces;
    public IReadOnlyList<NetworkDiagnostic> Diagnostics => _diag;
    public NetworkOptions Options => _opt;

    // Mapa face-de-cada-semiaresta para operações de união.
    private readonly Dictionary<int, int> _cycleToFace = new();
    private readonly List<int> _halfCycle = new();

    public static PlanarNetwork Build(IReadOnlyList<InputCurve> input, NetworkOptions? options = null)
    {
        var net = new PlanarNetwork(options ?? new NetworkOptions());
        net.Run(input);
        return net;
    }

    /// <summary>Face limitada de menor área que contém o ponto.</summary>
    public NetFace? FindFace(Vec2 p)
    {
        NetFace? best = null;
        foreach (var f in _faces)
        {
            if (!f.Region.Bounds.Contains(p)) continue;
            if (!f.Region.Contains(p)) continue;
            if (best is null || f.OuterArea < best.OuterArea) best = f;
        }
        return best;
    }

    public int ActiveDegree(int node) => _nodes[node].Degree;

    // ------------------------------------------------------------------ construção

    private void Run(IReadOnlyList<InputCurve> input)
    {
        double tol = _opt.Tolerance;

        // 1) Remove curvas degeneradas.
        var curves = new List<(Curve2 c, int tag)>();
        foreach (var ic in input)
        {
            if (ic.Curve.Length <= tol)
            {
                _diag.Add(new NetworkDiagnostic
                {
                    Kind = DiagnosticKind.ZeroLength, Severity = DiagnosticSeverity.Info,
                    Message = "Linha de comprimento desprezível ignorada.", Location = ic.Curve.Start, Tags = new[] { ic.Tag },
                });
                continue;
            }
            curves.Add((ic.Curve, ic.Tag));
        }
        if (curves.Count == 0) return;

        // 2) Une extremidades próximas.
        SnapEndpoints(curves, tol);

        // 3) Parâmetros de quebra por interseção e por encontros em "T".
        var splits = new List<double>[curves.Count];
        for (int i = 0; i < curves.Count; i++) splits[i] = new List<double>();
        ComputeSplits(curves, splits, tol);

        // 4) Quebra das curvas.
        var pieces = new List<(Curve2 c, int tag, bool origStart, bool origEnd)>();
        for (int i = 0; i < curves.Count; i++)
        {
            var c = curves[i].c;
            var ts = splits[i];
            ts.Add(0); ts.Add(1);
            ts.Sort();
            var uniq = new List<double>();
            foreach (var t in ts)
            {
                double tt = Math.Clamp(t, 0, 1);
                if (uniq.Count == 0) { uniq.Add(tt); continue; }
                if ((tt - uniq[^1]) * c.Length <= tol) continue;
                uniq.Add(tt);
            }
            if (uniq[^1] < 1)
            {
                if ((1 - uniq[^1]) * c.Length <= tol) uniq[^1] = 1; else uniq.Add(1);
            }
            for (int k = 0; k < uniq.Count - 1; k++)
            {
                double t0 = uniq[k], t1 = uniq[k + 1];
                pieces.Add((c.SubCurve(t0, t1), curves[i].tag, t0 == 0, t1 == 1));
            }
        }

        // 5) Nós.
        var nodeOf = BuildNodes(pieces, tol);

        // 6) Arestas com geometria ajustada aos nós.
        var dedupe = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < pieces.Count; i++)
        {
            int a = nodeOf[2 * i], b = nodeOf[2 * i + 1];
            var c = pieces[i].c;
            if (a == b)
            {
                if (c.Length > 4 * tol)
                {
                    // Laço fechado (ex.: círculo com um único nó) → divide ao meio.
                    var mid = c.PointAt(0.5);
                    int m = AddNode(mid, false);
                    AddEdge(a, m, c.SubCurve(0, 0.5), pieces[i].tag, dedupe);
                    AddEdge(m, b, c.SubCurve(0.5, 1), pieces[i].tag, dedupe);
                }
                continue;
            }
            AddEdge(a, b, c, pieces[i].tag, dedupe);
        }

        // Alertas de segmentos curtos.
        foreach (var e in _edges)
        {
            if (e.Curve.Length < _opt.ShortEdgeLength)
                _diag.Add(new NetworkDiagnostic
                {
                    Kind = DiagnosticKind.ShortEdge, Severity = DiagnosticSeverity.Warning,
                    Message = $"Segmento muito curto ({e.Curve.Length * 1000:0.#} mm).",
                    Location = e.Curve.Mid, Tags = e.Tags.Distinct().ToArray(), Value = e.Curve.Length,
                });
        }

        // 7) Topologia: remove pontas soltas e pontes, extrai faces.
        RecomputeDegrees();
        RemoveDangling(reportGaps: true);
        int guard = 0;
        while (guard++ < 1000)
        {
            BuildHalfEdges();
            TraceCycles();
            if (!RemoveBridges()) break;
            RecomputeDegrees();
            RemoveDangling(reportGaps: false);
        }
        BuildFaces();
    }

    private int AddNode(Vec2 p, bool original)
    {
        var n = new NetNode { Id = _nodes.Count, P = p, IsOriginalEndpoint = original };
        _nodes.Add(n);
        return n.Id;
    }

    private void AddEdge(int a, int b, Curve2 c, int tag, Dictionary<(int, int), List<int>> dedupe)
    {
        var pa = _nodes[a].P; var pb = _nodes[b].P;
        if (!c.Start.Equals(pa) || !c.End.Equals(pb)) c = c.WithEndpoints(pa, pb);
        if (c.Length <= _opt.Tolerance / 2) return;

        var key = a < b ? (a, b) : (b, a);
        if (dedupe.TryGetValue(key, out var list))
        {
            var mid = c.Mid;
            foreach (var eid in list)
            {
                var other = _edges[eid];
                if (other.Curve.Mid.DistanceTo(mid) <= 2 * _opt.Tolerance)
                {
                    if (!other.Tags.Contains(tag) || other.Tags.Count > 0)
                    {
                        _diag.Add(new NetworkDiagnostic
                        {
                            Kind = DiagnosticKind.Duplicate, Severity = DiagnosticSeverity.Warning,
                            Message = "Linhas sobrepostas (trecho duplicado).", Location = mid,
                            Tags = other.Tags.Append(tag).Distinct().ToArray(),
                        });
                    }
                    if (!other.Tags.Contains(tag)) other.Tags.Add(tag);
                    return;
                }
            }
        }
        else
        {
            list = new List<int>();
            dedupe[key] = list;
        }
        var e = new NetEdge { Id = _edges.Count, A = a, B = b, Curve = c };
        e.Tags.Add(tag);
        _edges.Add(e);
        list.Add(e.Id);
    }

    private void SnapEndpoints(List<(Curve2 c, int tag)> curves, double tol)
    {
        int n = curves.Count * 2;
        var pts = new Vec2[n];
        for (int i = 0; i < curves.Count; i++) { pts[2 * i] = curves[i].c.Start; pts[2 * i + 1] = curves[i].c.End; }
        var uf = new UnionFind(n);
        var grid = new SpatialHash(tol);
        for (int i = 0; i < n; i++)
        {
            foreach (var j in grid.Query(pts[i]))
                if (pts[i].DistanceTo(pts[j]) <= tol) uf.Union(i, j);
            grid.Add(pts[i], i);
        }
        var rep = new Dictionary<int, Vec2>();
        var maxGap = new Dictionary<int, double>();
        for (int i = 0; i < n; i++)
        {
            int r = uf.Find(i);
            if (!rep.ContainsKey(r)) rep[r] = pts[i];
        }
        for (int i = 0; i < curves.Count; i++)
        {
            var s = rep[uf.Find(2 * i)];
            var e = rep[uf.Find(2 * i + 1)];
            double gs = s.DistanceTo(pts[2 * i]), ge = e.DistanceTo(pts[2 * i + 1]);
            foreach (var (idx, g) in new[] { (2 * i, gs), (2 * i + 1, ge) })
            {
                if (g <= 0) continue;
                int r = uf.Find(idx);
                maxGap[r] = Math.Max(maxGap.GetValueOrDefault(r), g);
            }
            if (gs > 0 || ge > 0)
            {
                var c = curves[i].c.WithEndpoints(s, e);
                curves[i] = (c, curves[i].tag);
            }
        }
        foreach (var kv in maxGap)
        {
            if (kv.Value < 1e-9) continue;
            _diag.Add(new NetworkDiagnostic
            {
                Kind = DiagnosticKind.SnappedEndpoints, Severity = DiagnosticSeverity.Info,
                Message = $"Extremidades unidas por tolerância (folga de {kv.Value * 1000:0.###} mm).",
                Location = rep[kv.Key], Value = kv.Value,
            });
        }
    }

    private void ComputeSplits(List<(Curve2 c, int tag)> curves, List<double>[] splits, double tol)
    {
        int n = curves.Count;
        var bounds = curves.Select(c => c.c.Bounds.Expand(tol)).ToArray();
        var order = Enumerable.Range(0, n).OrderBy(i => bounds[i].MinX).ToArray();
        var active = new List<int>();
        var crossingSeen = new HashSet<(long, long)>();

        foreach (var i in order)
        {
            var bi = bounds[i];
            active.RemoveAll(j => bounds[j].MaxX < bi.MinX);
            foreach (var j in active)
            {
                if (!bi.Intersects(bounds[j])) continue;
                var ci = curves[i].c; var cj = curves[j].c;
                double pi = tol / ci.Length, pj = tol / cj.Length;
                foreach (var pp in Intersections.Intersect(ci, cj, tol))
                {
                    splits[i].Add(pp.T1);
                    splits[j].Add(pp.T2);
                    bool interiorI = pp.T1 > pi && pp.T1 < 1 - pi;
                    bool interiorJ = pp.T2 > pj && pp.T2 < 1 - pj;
                    if (interiorI && interiorJ)
                    {
                        var p = ci.PointAt(pp.T1);
                        var key = (Quant(p.X, tol), Quant(p.Y, tol));
                        if (crossingSeen.Add(key))
                            _diag.Add(new NetworkDiagnostic
                            {
                                Kind = DiagnosticKind.Crossing, Severity = DiagnosticSeverity.Info,
                                Message = "Linhas se cruzam sem vértice desenhado (o cruzamento foi considerado como vértice).",
                                Location = p, Tags = new[] { curves[i].tag, curves[j].tag },
                            });
                    }
                }
                // Encontros em "T" dentro da tolerância.
                AddTSplits(ci, cj, splits[j], tol);
                AddTSplits(cj, ci, splits[i], tol);
            }
            active.Add(i);
        }
    }

    private static long Quant(double v, double tol) => (long)Math.Round(v / Math.Max(tol, 1e-9));

    private static void AddTSplits(Curve2 from, Curve2 onto, List<double> ontoSplits, double tol)
    {
        double pt = tol / onto.Length;
        foreach (var p in new[] { from.Start, from.End })
        {
            var q = onto.ClosestPoint(p, out double t);
            if (q.DistanceTo(p) <= tol && t > pt && t < 1 - pt) ontoSplits.Add(t);
        }
    }

    private int[] BuildNodes(List<(Curve2 c, int tag, bool origStart, bool origEnd)> pieces, double tol)
    {
        int n = pieces.Count * 2;
        var pts = new Vec2[n];
        var orig = new bool[n];
        for (int i = 0; i < pieces.Count; i++)
        {
            pts[2 * i] = pieces[i].c.Start; orig[2 * i] = pieces[i].origStart;
            pts[2 * i + 1] = pieces[i].c.End; orig[2 * i + 1] = pieces[i].origEnd;
        }
        var uf = new UnionFind(n);
        var grid = new SpatialHash(tol);
        for (int i = 0; i < n; i++)
        {
            foreach (var j in grid.Query(pts[i]))
                if (pts[i].DistanceTo(pts[j]) <= tol) uf.Union(i, j);
            grid.Add(pts[i], i);
        }
        // Representante: extremidade original (vértice desenhado) quando existir.
        var repIndex = new Dictionary<int, int>();
        for (int i = 0; i < n; i++)
        {
            int r = uf.Find(i);
            if (!repIndex.TryGetValue(r, out int cur)) repIndex[r] = i;
            else if (!orig[cur] && orig[i]) repIndex[r] = i;
        }
        var nodeIdOfRoot = new Dictionary<int, int>();
        var result = new int[n];
        for (int i = 0; i < n; i++)
        {
            int r = uf.Find(i);
            if (!nodeIdOfRoot.TryGetValue(r, out int id))
            {
                int ri = repIndex[r];
                id = AddNode(pts[ri], orig[ri]);
                nodeIdOfRoot[r] = id;
            }
            if (orig[i]) _nodes[id].IsOriginalEndpoint = true;
            result[i] = id;
        }
        return result;
    }

    private void RecomputeDegrees()
    {
        foreach (var nd in _nodes) nd.Degree = 0;
        foreach (var e in _edges)
        {
            if (e.Removed) continue;
            _nodes[e.A].Degree++;
            _nodes[e.B].Degree++;
        }
    }

    private void RemoveDangling(bool reportGaps)
    {
        var adj = new Dictionary<int, List<int>>();
        foreach (var e in _edges)
        {
            if (e.Removed) continue;
            (adj.TryGetValue(e.A, out var la) ? la : adj[e.A] = new List<int>()).Add(e.Id);
            (adj.TryGetValue(e.B, out var lb) ? lb : adj[e.B] = new List<int>()).Add(e.Id);
        }
        var queue = new Queue<int>(_nodes.Where(nd => nd.Degree == 1).Select(nd => nd.Id));
        var freeEnds = new List<(int edge, int node)>();
        var originalDangle = new HashSet<int>(_nodes.Where(nd => nd.Degree == 1).Select(nd => nd.Id));
        while (queue.Count > 0)
        {
            int v = queue.Dequeue();
            if (_nodes[v].Degree != 1) continue;
            int eid = adj[v].First(id => !_edges[id].Removed);
            var e = _edges[eid];
            e.Removed = true;
            e.RemovedReason = DiagnosticKind.DanglingLine;
            if (originalDangle.Contains(v)) freeEnds.Add((eid, v));
            _nodes[e.A].Degree--;
            _nodes[e.B].Degree--;
            int other = e.A == v ? e.B : e.A;
            if (_nodes[other].Degree == 1) queue.Enqueue(other);
        }

        foreach (var (eid, v) in freeEnds)
        {
            var e = _edges[eid];
            var p = _nodes[v].P;
            double best = double.PositiveInfinity;
            Vec2 bestPt = p;
            if (reportGaps)
            {
                foreach (var o in _edges)
                {
                    if (o.Id == eid || o.A == v || o.B == v) continue;
                    if (!o.Curve.Bounds.Expand(_opt.GapSearchDistance).Contains(p)) continue;
                    var q = o.Curve.ClosestPoint(p, out _);
                    double d = q.DistanceTo(p);
                    if (d < best) { best = d; bestPt = q; }
                }
            }
            if (best <= _opt.GapSearchDistance)
            {
                _diag.Add(new NetworkDiagnostic
                {
                    Kind = DiagnosticKind.PossibleGap, Severity = DiagnosticSeverity.Warning,
                    Message = $"Possível falha de conexão: extremidade a {best * 1000:0.#} mm de outra linha.",
                    Location = p, Tags = e.Tags.Distinct().ToArray(), Value = best,
                });
            }
            else
            {
                _diag.Add(new NetworkDiagnostic
                {
                    Kind = DiagnosticKind.DanglingLine, Severity = DiagnosticSeverity.Warning,
                    Message = "Linha solta (não fecha nenhuma área).",
                    Location = p, Tags = e.Tags.Distinct().ToArray(),
                });
            }
        }
    }

    private void BuildHalfEdges()
    {
        _half.Clear();
        foreach (var nd in _nodes) nd.Out.Clear();
        foreach (var e in _edges)
        {
            if (e.Removed) continue;
            var f = new HalfEdge { Id = _half.Count, Edge = e.Id, Forward = true, From = e.A, To = e.B, Curve = e.Curve };
            _half.Add(f);
            var rev = e.Curve.Reversed();
            var b = new HalfEdge { Id = _half.Count, Edge = e.Id, Forward = false, From = e.B, To = e.A, Curve = rev };
            _half.Add(b);
            f.Twin = b.Id; b.Twin = f.Id;
            foreach (var h in new[] { f, b })
            {
                double ang = AngleUtil.Norm2Pi(h.Curve.TangentAt(0).Angle);
                if (ang > AngleUtil.TwoPi - 1e-10) ang = 0;
                h.Angle = ang;
                h.Curv = h.Curve.SignedCurvature;
                _nodes[h.From].Out.Add(h.Id);
            }
        }
        foreach (var nd in _nodes)
        {
            nd.Out.Sort((x, y) =>
            {
                var hx = _half[x]; var hy = _half[y];
                double d = hx.Angle - hy.Angle;
                if (Math.Abs(d) > 1e-10) return d < 0 ? -1 : 1;
                int c = hx.Curv.CompareTo(hy.Curv);
                return c != 0 ? c : x.CompareTo(y);
            });
        }
        // next(h) = semiaresta de saída imediatamente horária a twin(h) no nó de chegada.
        foreach (var h in _half)
        {
            var outList = _nodes[h.To].Out;
            int idx = outList.IndexOf(h.Twin);
            int n = outList.Count;
            h.Next = outList[(idx - 1 + n) % n];
        }
    }

    private readonly List<List<int>> _cycles = new();
    private readonly List<double> _cycleArea = new();

    private void TraceCycles()
    {
        _cycles.Clear();
        _cycleArea.Clear();
        _halfCycle.Clear();
        foreach (var h in _half) { h.Face = -1; _halfCycle.Add(-1); }
        int limit = _half.Count + 5;
        foreach (var start in _half)
        {
            if (start.Face >= 0) continue;
            var cyc = new List<int>();
            int cur = start.Id;
            int cid = _cycles.Count;
            int steps = 0;
            while (_half[cur].Face < 0 && steps++ < limit)
            {
                _half[cur].Face = cid;
                _halfCycle[cur] = cid;
                cyc.Add(cur);
                cur = _half[cur].Next;
            }
            var o = _half[cyc[0]].Curve.Start;
            double area = 0;
            foreach (var hid in cyc) area += _half[hid].Curve.GreenIntegral(o);
            _cycles.Add(cyc);
            _cycleArea.Add(area);
        }
    }

    private bool RemoveBridges()
    {
        bool any = false;
        foreach (var h in _half)
        {
            if (!h.Forward) continue;
            if (h.Face == _half[h.Twin].Face)
            {
                var e = _edges[h.Edge];
                if (e.Removed) continue;
                e.Removed = true;
                e.RemovedReason = DiagnosticKind.Bridge;
                any = true;
                _diag.Add(new NetworkDiagnostic
                {
                    Kind = DiagnosticKind.Bridge, Severity = DiagnosticSeverity.Warning,
                    Message = "Linha interna que não divide a área (ignorada).",
                    Location = e.Curve.Mid, Tags = e.Tags.Distinct().ToArray(),
                });
            }
        }
        return any;
    }

    private void BuildFaces()
    {
        _faces.Clear();
        _cycleToFace.Clear();
        if (_cycles.Count == 0) return;

        // Componentes conectados.
        var uf = new UnionFind(_nodes.Count);
        foreach (var e in _edges) if (!e.Removed) uf.Union(e.A, e.B);

        var bounded = new List<int>();
        var outer = new List<int>();
        for (int c = 0; c < _cycles.Count; c++)
        {
            if (_cycleArea[c] > 0) bounded.Add(c); else outer.Add(c);
        }

        var faceOfCycle = new Dictionary<int, NetFace>();
        foreach (var c in bounded)
        {
            var refs = CycleRefs(_cycles[c]);
            var ring = new Ring(refs.Select(r => r.Curve).ToList());
            var f = new NetFace { Id = _faces.Count, Region = new Region(ring) };
            f.OuterEdges.AddRange(refs);
            _faces.Add(f);
            faceOfCycle[c] = f;
            _cycleToFace[c] = f.Id;
            if (ring.Area < _opt.TinyFaceArea)
                _diag.Add(new NetworkDiagnostic
                {
                    Kind = DiagnosticKind.TinyFace, Severity = DiagnosticSeverity.Warning,
                    Message = $"Área muito pequena ({ring.Area:0.####} m²) – possível erro de desenho.",
                    Location = PolygonUtil.Centroid(ring.ToPolygon()),
                    Tags = refs.SelectMany(r => r.Tags).Distinct().ToArray(), Value = ring.Area,
                });
        }

        // Furos: contorno externo de um componente dentro de uma face de outro componente.
        var holesOf = new Dictionary<int, List<int>>();
        foreach (var c in outer)
        {
            var cyc = _cycles[c];
            int comp = uf.Find(_half[cyc[0]].From);
            var test = _nodes[_half[cyc[0]].From].P;
            NetFace? container = null;
            int containerCycle = -1;
            foreach (var bc in bounded)
            {
                var f = faceOfCycle[bc];
                if (uf.Find(_half[_cycles[bc][0]].From) == comp) continue;
                if (!f.Region.Outer.Bounds.Contains(test)) continue;
                if (!f.Region.Outer.Contains(test)) continue;
                if (container is null || f.OuterArea < container.OuterArea) { container = f; containerCycle = bc; }
            }
            if (container is null) continue;
            (holesOf.TryGetValue(containerCycle, out var l) ? l : holesOf[containerCycle] = new List<int>()).Add(c);
        }
        foreach (var kv in holesOf)
        {
            var f = faceOfCycle[kv.Key];
            var holeRings = new List<Ring>();
            foreach (var hc in kv.Value)
            {
                var refs = CycleRefs(_cycles[hc]);
                f.HoleEdges.Add(refs);
                holeRings.Add(new Ring(refs.Select(r => r.Curve).ToList()));
            }
            f.Region = new Region(f.Region.Outer, holeRings);
        }
    }

    private List<FaceEdgeRef> CycleRefs(List<int> cyc) =>
        cyc.Select(hid =>
        {
            var h = _half[hid];
            return new FaceEdgeRef
            {
                EdgeId = h.Edge, Forward = h.Forward, Curve = h.Curve, FromNode = h.From, ToNode = h.To,
                Tags = _edges[h.Edge].Tags.ToArray(),
            };
        }).ToList();

    // ------------------------------------------------------------------ união de faces

    /// <summary>
    /// Contorno da união de um conjunto de faces desta rede (faces marcadas).
    /// Devolve anéis anti-horários (externos) e horários (furos).
    /// </summary>
    public List<List<FaceEdgeRef>> UnionBoundary(ISet<int> faceIds)
    {
        bool Marked(int halfId)
        {
            int cyc = _half[halfId].Face;
            return cyc >= 0 && _cycleToFace.TryGetValue(cyc, out int fid) && faceIds.Contains(fid);
        }
        bool IsBoundary(int halfId) => Marked(halfId) && !Marked(_half[halfId].Twin);

        var used = new HashSet<int>();
        var rings = new List<List<FaceEdgeRef>>();
        foreach (var h in _half)
        {
            if (used.Contains(h.Id) || !IsBoundary(h.Id)) continue;
            var ring = new List<int>();
            int cur = h.Id;
            int guard = 0;
            while (!used.Contains(cur) && guard++ < _half.Count + 5)
            {
                used.Add(cur);
                ring.Add(cur);
                int g = _half[cur].Next;
                int inner = 0;
                while (!IsBoundary(g) && inner++ < _half.Count + 5) g = _half[_half[g].Twin].Next;
                cur = g;
            }
            rings.Add(CycleRefs(ring));
        }
        return rings;
    }
}

internal sealed class UnionFind
{
    private readonly int[] _p;
    private readonly int[] _r;
    public UnionFind(int n) { _p = Enumerable.Range(0, n).ToArray(); _r = new int[n]; }
    public int Find(int x)
    {
        while (_p[x] != x) { _p[x] = _p[_p[x]]; x = _p[x]; }
        return x;
    }
    public void Union(int a, int b)
    {
        a = Find(a); b = Find(b);
        if (a == b) return;
        if (_r[a] < _r[b]) (a, b) = (b, a);
        _p[b] = a;
        if (_r[a] == _r[b]) _r[a]++;
    }
}

internal sealed class SpatialHash
{
    private readonly double _cell;
    private readonly Dictionary<(long, long), List<int>> _cells = new();
    private readonly List<Vec2> _pts = new();

    public SpatialHash(double cell) { _cell = Math.Max(cell, 1e-9); }

    private (long, long) Key(Vec2 p) => ((long)Math.Floor(p.X / _cell), (long)Math.Floor(p.Y / _cell));

    public void Add(Vec2 p, int id)
    {
        var k = Key(p);
        if (!_cells.TryGetValue(k, out var l)) _cells[k] = l = new List<int>();
        l.Add(id);
    }

    public IEnumerable<int> Query(Vec2 p)
    {
        var (kx, ky) = Key(p);
        for (long dx = -1; dx <= 1; dx++)
            for (long dy = -1; dy <= 1; dy++)
                if (_cells.TryGetValue((kx + dx, ky + dy), out var l))
                    foreach (var id in l) yield return id;
    }
}
