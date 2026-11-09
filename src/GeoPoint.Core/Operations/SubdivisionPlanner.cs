using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Topology;

namespace GeoPoint.Core.Operations;

/// <summary>Parte resultante de um desmembramento (prévia e criação).</summary>
public sealed class PlannedPiece
{
    public Region Region { get; init; } = null!;
    public List<EdgeDto> Outer { get; init; } = new();
    public List<List<EdgeDto>> Holes { get; init; } = new();
    public double Area => Region.Area;
    public double Frontage { get; set; }
}

/// <summary>Monta as partes de um desmembramento a partir do lote original e das divisas novas.</summary>
public static class SubdivisionPlanner
{
    /// <summary>
    /// Partes formadas pelo contorno do lote original e pelas divisas novas (simuladas ou desenhadas).
    /// Cada divisa nova pode ter o UniqueId do elemento do Revit (para rastrear a origem).
    /// </summary>
    public static List<PlannedPiece> FromCuts(LotRecord parent, IReadOnlyList<(Curve2 Curve, string? Uid)> cuts, double tolerance = 0.001)
    {
        var inputs = new List<InputCurve>();
        var srcs = new List<IEnumerable<string>>();
        foreach (var dto in parent.Outer.Concat(parent.Holes.SelectMany(h => h)))
        {
            inputs.Add(new InputCurve(dto.ToCurve(), srcs.Count));
            srcs.Add(dto.Src);
        }
        foreach (var (c, uid) in cuts)
        {
            if (c.Length < 1e-4) continue;
            inputs.Add(new InputCurve(c, srcs.Count));
            srcs.Add(uid is null ? Array.Empty<string>() : new[] { uid });
        }
        var net = PlanarNetwork.Build(inputs, new NetworkOptions { Tolerance = tolerance });
        var parentRegion = parent.ToRegion();
        var res = new List<PlannedPiece>();
        foreach (var f in net.Faces)
        {
            if (!parentRegion.Contains(f.Region.InteriorPoint())) continue;
            var (outer, holes) = LotBuilder.FromFace(net, f, t => srcs[t], parent.Holes.Count > 0, false);
            res.Add(new PlannedPiece { Region = f.Region, Outer = outer, Holes = holes, Frontage = FrontageOn(parent, f.Region) });
        }
        return res;
    }

    /// <summary>Comprimento do contorno da parte que fica sobre as testadas do lote original.</summary>
    public static double FrontageOn(LotRecord parent, Region piece)
    {
        var fronts = parent.Outer.Select(e => e.ToCurve()).Where(c => ProjectContext.FindInfo(parent, c.Mid)?.Testada == true).ToList();
        if (fronts.Count == 0) return 0;
        double len = 0;
        foreach (var e in piece.Outer.Edges)
            if (fronts.Any(f => f.DistanceTo(e.Mid) <= 0.002 && f.DistanceTo(e.Start) <= 0.002 && f.DistanceTo(e.End) <= 0.002)) len += e.Length;
        return len;
    }

    /// <summary>Ordena as partes ao longo de uma direção (ex.: a frente), da origem para o fim.</summary>
    public static List<PlannedPiece> OrderAlong(List<PlannedPiece> pieces, Vec2 direction) =>
        pieces.OrderBy(p => direction.Dot(p.Region.InteriorPoint())).ToList();

    /// <summary>Ordena de cima para baixo, da esquerda para a direita (no desenho).</summary>
    public static List<PlannedPiece> OrderReading(List<PlannedPiece> pieces) =>
        pieces.OrderByDescending(p => Math.Round(p.Region.InteriorPoint().Y, 0)).ThenBy(p => p.Region.InteriorPoint().X).ToList();

    /// <summary>
    /// Leva as pontas das divisas que terminam a menos de <paramref name="tolerance"/> de um vértice do lote até esse vértice
    /// (evita trechos de poucos centímetros e vértices "colados"). Devolve quantas pontas foram movidas.
    /// </summary>
    public static int SnapCuts(LotRecord parent, IList<LineCurve> cuts, double tolerance)
    {
        if (tolerance <= 0) return 0;
        var verts = parent.Outer.Select(e => new Vec2(e.X1, e.Y1)).Concat(parent.Holes.SelectMany(h => h).Select(e => new Vec2(e.X1, e.Y1))).ToList();
        int moved = 0;
        for (int i = 0; i < cuts.Count; i++)
        {
            var c = cuts[i];
            Vec2 Snap(Vec2 p)
            {
                var best = verts.OrderBy(v => v.DistanceTo(p)).FirstOrDefault();
                double d = best.DistanceTo(p);
                if (verts.Count == 0 || d <= 1e-6 || d > tolerance) return p;
                moved++;
                return best;
            }
            var a = Snap(c.Start);
            var b = Snap(c.End);
            if (a.DistanceTo(b) > tolerance * 2) cuts[i] = new LineCurve(a, b);
        }
        return moved;
    }
}
