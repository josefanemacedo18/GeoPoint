using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Topology;

namespace GeoPoint.Core.Operations;

public sealed class OperationCheck
{
    public bool Ok => Errors.Count == 0;
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Infos { get; } = new();
    public double ParentArea { get; set; }
    public double ChildrenArea { get; set; }
    public double Difference => ChildrenArea - ParentArea;
}

public sealed class UnionResult
{
    public List<EdgeDto> Outer { get; } = new();
    public List<List<EdgeDto>> Holes { get; } = new();
    public int OuterRingCount { get; set; }
    public double Area { get; set; }
    public List<string> Errors { get; } = new();
    public bool Ok => Errors.Count == 0 && Outer.Count > 0;
}

/// <summary>Desmembramento, remembramento e verificações de consistência entre lotes.</summary>
public static class ParcelOperations
{
    public const double AreaTolerance = 0.01; // m²

    /// <summary>União exata (com arcos) de lotes adjacentes, preservando os elementos de origem das divisas.</summary>
    public static UnionResult Union(IReadOnlyList<LotRecord> lots, double tolerance = 0.001)
    {
        var result = new UnionResult();
        if (lots.Count == 0) { result.Errors.Add("Nenhum lote selecionado."); return result; }

        var inputs = new List<InputCurve>();
        var sources = new List<List<string>>();
        var regions = new List<Region>();
        foreach (var lot in lots)
        {
            var region = lot.ToRegion();
            regions.Add(region);
            foreach (var dto in lot.Outer.Concat(lot.Holes.SelectMany(h => h)))
            {
                inputs.Add(new InputCurve(dto.ToCurve(), sources.Count));
                sources.Add(dto.Src);
            }
        }

        var net = PlanarNetwork.Build(inputs, new NetworkOptions { Tolerance = tolerance });
        var marked = new HashSet<int>();
        foreach (var f in net.Faces)
        {
            var p = f.Region.InteriorPoint();
            if (regions.Any(r => r.Contains(p))) marked.Add(f.Id);
        }
        var rings = net.UnionBoundary(marked);
        var outers = new List<List<FaceEdgeRef>>();
        var holes = new List<List<FaceEdgeRef>>();
        foreach (var ring in rings)
        {
            var r = new Ring(ring.Select(e => e.Curve).ToList());
            if (r.SignedArea > 0) outers.Add(ring); else holes.Add(ring);
        }
        result.OuterRingCount = outers.Count;
        if (outers.Count == 0) { result.Errors.Add("Não foi possível obter o contorno da união."); return result; }
        if (outers.Count > 1) result.Errors.Add("Os lotes selecionados não são contíguos (a união resulta em mais de uma área).");

        List<EdgeDto> ToDtos(List<FaceEdgeRef> ring) =>
            ring.Select(e => EdgeDto.FromCurve(e.Curve, e.Tags.SelectMany(t => sources[t]))).ToList();

        var main = outers.OrderByDescending(o => new Ring(o.Select(e => e.Curve).ToList()).Area).First();
        result.Outer.AddRange(ToDtos(main));
        foreach (var h in holes) result.Holes.Add(ToDtos(h));
        var region2 = new Region(new Ring(main.Select(e => e.Curve).ToList()), holes.Select(h => new Ring(h.Select(e => e.Curve).ToList())).ToList());
        result.Area = region2.Area;
        return result;
    }

    /// <summary>Verifica se os lotes resultantes reconstituem exatamente o lote original (desmembramento).</summary>
    public static OperationCheck CheckSubdivision(LotRecord parent, IReadOnlyList<LotRecord> children)
    {
        var chk = new OperationCheck();
        var pr = parent.ToRegion();
        chk.ParentArea = pr.Area;
        if (children.Count < 2) chk.Errors.Add("Selecione pelo menos dois lotes resultantes.");

        var regs = children.Select(c => c.ToRegion()).ToList();
        chk.ChildrenArea = regs.Sum(r => r.Area);

        for (int i = 0; i < children.Count; i++)
        {
            var inside = Overlay.IntersectionArea(pr, regs[i]);
            if (regs[i].Area - inside > AreaTolerance)
                chk.Errors.Add($"{children[i].DisplayName} ultrapassa os limites do lote original ({regs[i].Area - inside:0.00} m² fora).");
            for (int j = i + 1; j < children.Count; j++)
            {
                double ov = Overlay.IntersectionArea(regs[i], regs[j]);
                if (ov > AreaTolerance)
                    chk.Errors.Add($"{children[i].DisplayName} e {children[j].DisplayName} se sobrepõem ({ov:0.00} m²).");
            }
        }

        double diff = chk.ChildrenArea - chk.ParentArea;
        if (Math.Abs(diff) > AreaTolerance)
        {
            if (diff < 0) chk.Warnings.Add($"A soma das áreas resultantes é {(-diff):0.00} m² menor que a área original (há área remanescente não incluída).");
            else chk.Errors.Add($"A soma das áreas resultantes é {diff:0.00} m² maior que a área original.");
        }
        else chk.Infos.Add("A soma das áreas resultantes confere com a área original.");

        if (chk.Errors.Count == 0 && children.Count >= 2)
        {
            var u = Union(children);
            if (!u.Ok) chk.Warnings.AddRange(u.Errors);
            else if (Math.Abs(u.Area - chk.ParentArea) > AreaTolerance && Math.Abs(diff) <= AreaTolerance)
                chk.Warnings.Add("O contorno da união dos lotes resultantes difere do contorno original.");
        }
        return chk;
    }

    /// <summary>Verifica a união de lotes (remembramento).</summary>
    public static OperationCheck CheckMerge(IReadOnlyList<LotRecord> lots, out UnionResult union)
    {
        var chk = new OperationCheck();
        union = Union(lots);
        chk.ParentArea = lots.Sum(l => l.ToRegion().Area);
        chk.ChildrenArea = union.Area;
        if (lots.Count < 2) chk.Errors.Add("Selecione pelo menos dois lotes para remembrar.");
        chk.Errors.AddRange(union.Errors);
        var regs = lots.Select(l => l.ToRegion()).ToList();
        for (int i = 0; i < lots.Count; i++)
            for (int j = i + 1; j < lots.Count; j++)
            {
                double ov = Overlay.IntersectionArea(regs[i], regs[j]);
                if (ov > AreaTolerance) chk.Errors.Add($"{lots[i].DisplayName} e {lots[j].DisplayName} se sobrepõem ({ov:0.00} m²).");
            }
        if (union.Holes.Count > 0) chk.Warnings.Add("A área remembrada contém vazio interno (lotes envolvem outra área).");
        if (Math.Abs(chk.Difference) > AreaTolerance && chk.Errors.Count == 0)
            chk.Warnings.Add($"Diferença entre a soma das áreas e a área remembrada: {chk.Difference:0.00} m².");
        else if (chk.Errors.Count == 0) chk.Infos.Add("A área remembrada confere com a soma das áreas originais.");
        return chk;
    }

    /// <summary>Sobreposições entre lotes ativos (ignora descontos sobre lotes e o par lote/APP).</summary>
    public static List<(LotRecord A, LotRecord B, double Area)> FindOverlaps(IEnumerable<LotRecord> lots)
    {
        var list = lots.Select(l => { try { return (lot: l, reg: l.ToRegion()); } catch { return (lot: l, reg: (Region?)null); } })
            .Where(x => x.reg is not null).Select(x => (x.lot, reg: x.reg!)).ToList();
        var res = new List<(LotRecord, LotRecord, double)>();
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
            {
                var a = list[i]; var b = list[j];
                if (LotUseInfo.IsDeduction(a.lot.Uso) != LotUseInfo.IsDeduction(b.lot.Uso)) continue;
                if (a.lot.Uso == LotUse.Gleba || b.lot.Uso == LotUse.Gleba) continue;
                if (!a.reg.Bounds.Intersects(b.reg.Bounds)) continue;
                double ov = Overlay.IntersectionArea(a.reg, b.reg);
                if (ov > AreaTolerance) res.Add((a.lot, b.lot, ov));
            }
        return res;
    }
}
