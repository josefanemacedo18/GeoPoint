using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;

namespace GeoPoint.Core.Operations;

/// <summary>
/// Herança de confrontantes em desmembramentos, remembramentos e divisões:
/// divisas sobre o contorno do(s) lote(s) original(is) herdam o confrontante dele(s);
/// divisas internas recebem o lote vizinho resultante.
/// </summary>
public static class ConfrontanteInheritance
{
    public const double Tolerance = 0.002;

    /// <summary>
    /// Atributos das divisas de <paramref name="child"/>. Atributos já informados no lote resultante são mantidos.
    /// </summary>
    /// <param name="parents">Lote(s) de origem.</param>
    /// <param name="siblings">Outros lotes resultantes da mesma operação.</param>
    /// <param name="ctx">Contexto (para converter vizinhos sugeridos do original em referências explícitas). Pode ser nulo.</param>
    public static List<EdgeInfo> Inherit(IReadOnlyList<LotRecord> parents, LotRecord child, IReadOnlyList<LotRecord> siblings, ProjectContext? ctx)
    {
        var result = new List<EdgeInfo>();
        var parentCurves = parents.Select(p => (lot: p, curves: p.Outer.Concat(p.Holes.SelectMany(h => h)).Select(e => e.ToCurve()).ToList())).ToList();
        var siblingCurves = siblings.Where(s => s.Id != child.Id && parents.All(p => p.Id != s.Id))
            .Select(s => (lot: s, curves: s.Outer.Select(e => e.ToCurve()).ToList())).ToList();

        foreach (var dto in child.Outer)
        {
            var curve = dto.ToCurve();
            var mid = curve.Mid;
            var existing = ProjectContext.FindInfo(child, mid);
            if (existing is not null && !existing.IsEmpty)
            {
                result.Add(existing);
                continue;
            }

            EdgeInfo? info = null;
            foreach (var (plot, pcs) in parentCurves)
            {
                var pc = pcs.FirstOrDefault(c => c.DistanceTo(mid) <= Tolerance);
                if (pc is null) continue;
                var pinfo = ProjectContext.FindInfo(plot, pc.Mid);
                if (pinfo is not null && !pinfo.IsEmpty)
                {
                    info = pinfo.CloneAt(mid);
                    info.Lado = LadoLote.Automatico; // o lado é recalculado para o novo lote
                }
                else if (ctx is not null)
                {
                    var nb = ctx.NeighborAt(plot, mid);
                    if (nb is not null && parents.All(p => p.Id != nb.Id) && siblings.All(s => s.Id != nb.Id))
                        info = new EdgeInfo { MX = mid.X, MY = mid.Y, Tipo = ConfrontanteTipo.Lote, LotId = nb.Id };
                }
                break;
            }

            if (info is null)
            {
                // Divisa interna: o confrontante é outro lote resultante.
                foreach (var (slot, scs) in siblingCurves)
                {
                    if (scs.Any(c => c.DistanceTo(mid) <= Tolerance))
                    {
                        info = new EdgeInfo { MX = mid.X, MY = mid.Y, Tipo = ConfrontanteTipo.Lote, LotId = slot.Id };
                        break;
                    }
                }
            }
            if (info is not null) result.Add(info);
        }
        return result;
    }

    /// <summary>
    /// Outros lotes que apontavam para lotes arquivados passam a apontar para o lote que ocupa a divisa agora.
    /// </summary>
    public static int Retarget(ParcelProject project, IReadOnlyCollection<string> archivedIds, IReadOnlyList<LotRecord> newLots)
    {
        int changed = 0;
        var curves = newLots.Select(l => (lot: l, cs: l.Outer.Select(e => e.ToCurve()).ToList())).ToList();
        foreach (var lot in project.ActiveLots)
        {
            if (newLots.Any(n => n.Id == lot.Id)) continue;
            foreach (var info in lot.EdgeInfos.Where(i => i.Tipo == ConfrontanteTipo.Lote && archivedIds.Contains(i.LotId)))
            {
                var mid = new Vec2(info.MX, info.MY);
                var hit = curves.FirstOrDefault(c => c.cs.Any(cv => cv.DistanceTo(mid) <= Tolerance));
                if (hit.lot is null) continue;
                info.LotId = hit.lot.Id;
                changed++;
            }
        }
        return changed;
    }
}
