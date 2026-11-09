using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;

namespace GeoPoint.Core.Calc;

/// <summary>
/// Registro único de vértices do projeto. Cada ponto físico recebe um número na primeira vez que aparece
/// (na ordem de criação dos lotes e no sentido da descrição) e o mantém em desmembramentos, remembramentos
/// e divisões. Também guarda a numeração por quadra.
/// </summary>
public static class VertexRegistry
{
    public const double Tolerance = 0.002; // m
    private const double Cell = 0.01;

    private sealed class Index
    {
        private readonly Dictionary<(long, long), List<VertexRecord>> _grid = new();
        public Index(IEnumerable<VertexRecord> recs) { foreach (var r in recs) Add(r); }
        private static (long, long) Key(double x, double y) => ((long)Math.Floor(x / Cell), (long)Math.Floor(y / Cell));
        public void Add(VertexRecord r)
        {
            var k = Key(r.X, r.Y);
            if (!_grid.TryGetValue(k, out var l)) _grid[k] = l = new List<VertexRecord>();
            l.Add(r);
        }
        public VertexRecord? Find(Vec2 p)
        {
            var (kx, ky) = Key(p.X, p.Y);
            VertexRecord? best = null;
            double bd = Tolerance;
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    if (_grid.TryGetValue((kx + dx, ky + dy), out var l))
                        foreach (var r in l)
                        {
                            double d = Math.Sqrt((r.X - p.X) * (r.X - p.X) + (r.Y - p.Y) * (r.Y - p.Y));
                            if (d <= bd) { bd = d; best = r; }
                        }
            return best;
        }
    }

    /// <summary>Ordem de numeração: lotes mais antigos primeiro (o original antes dos resultantes).</summary>
    public static IEnumerable<LotRecord> CreationOrder(ParcelProject p) =>
        p.Lots.OrderBy(l => l.Created).ThenBy(l => l.Quadra, NaturalComparer.Instance).ThenBy(l => l.Numero, NaturalComparer.Instance).ThenBy(l => l.Nome, NaturalComparer.Instance);

    /// <summary>Registra os vértices ainda não numerados de todos os lotes. Números existentes não mudam.</summary>
    public static int EnsureAll(ProjectContext ctx) => Register(ctx, CreationOrder(ctx.Project));

    /// <summary>Registra os vértices dos lotes informados (na ordem dada). Devolve quantos foram criados.</summary>
    public static int Register(ProjectContext ctx, IEnumerable<LotRecord> lots)
    {
        var p = ctx.Project;
        var index = new Index(p.Vertices);
        int next = p.Vertices.Count == 0 ? 1 : p.Vertices.Max(v => v.Number) + 1;
        int added = 0;
        foreach (var lot in lots)
        {
            Ring ring;
            try { ring = ctx.OrientedRing(lot, out _); }
            catch { continue; }
            string q = (lot.Quadra ?? "").Trim();
            var pts = ring.Edges.Select(e => e.Start).ToList();
            foreach (var h in lot.Holes.Where(h => h.Count > 0))
                pts.AddRange(h.Select(e => new Vec2(e.X1, e.Y1)));
            foreach (var pt in pts)
            {
                var rec = index.Find(pt);
                if (rec is null)
                {
                    rec = new VertexRecord { Number = next++, X = pt.X, Y = pt.Y };
                    p.Vertices.Add(rec);
                    index.Add(rec);
                    added++;
                }
                if (!rec.QuadraNumbers.ContainsKey(q))
                {
                    int n = p.QuadraVertexCounters.TryGetValue(q, out int c) ? c : 1;
                    rec.QuadraNumbers[q] = n;
                    p.QuadraVertexCounters[q] = n + 1;
                }
            }
        }
        return added;
    }

    /// <summary>Refaz toda a numeração (P-01 em diante) na ordem atual dos lotes.</summary>
    public static void Renumber(ProjectContext ctx)
    {
        ctx.Project.Vertices.Clear();
        ctx.Project.QuadraVertexCounters.Clear();
        var active = ProjectContext.SortLots(ctx.Project.ActiveLots).ToList();
        var archived = ctx.Project.Lots.Where(l => l.Status == LotStatus.Arquivado).OrderBy(l => l.Created).ToList();
        Register(ctx, archived.Concat(active));
        ctx.Invalidate();
    }

    /// <summary>Remove vértices que não pertencem a nenhum lote (ex.: após excluir lotes). Os números restantes não mudam.</summary>
    public static int Prune(ParcelProject p)
    {
        var used = new Index(Array.Empty<VertexRecord>());
        var all = new List<VertexRecord>();
        foreach (var lot in p.Lots)
            foreach (var e in lot.Outer.Concat(lot.Holes.SelectMany(h => h)))
            {
                var r = new VertexRecord { X = e.X1, Y = e.Y1 };
                used.Add(r);
            }
        int before = p.Vertices.Count;
        p.Vertices.RemoveAll(v => used.Find(new Vec2(v.X, v.Y)) is null);
        return before - p.Vertices.Count;
    }

    public static VertexRecord? Find(ParcelProject p, Vec2 pt)
    {
        VertexRecord? best = null;
        double bd = Tolerance;
        foreach (var r in p.Vertices)
        {
            double d = Math.Sqrt((r.X - pt.X) * (r.X - pt.X) + (r.Y - pt.Y) * (r.Y - pt.Y));
            if (d <= bd) { bd = d; best = r; }
        }
        return best;
    }

    internal static Func<Vec2, VertexRecord?> Finder(ParcelProject p)
    {
        var idx = new Index(p.Vertices);
        return idx.Find;
    }
}
