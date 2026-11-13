using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;

namespace GeoPoint.Core.Urban;

/// <summary>Afastamento da edificação até uma divisa do lote.</summary>
public sealed class Setback
{
    public int SegmentIndex { get; init; }
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public string Confrontante { get; init; } = "";
    public LadoLote Lado { get; init; }
    public double Distance { get; init; }
    /// <summary>Ponto da edificação e ponto da divisa mais próximos (para a cota).</summary>
    public Vec2 AtBuilding { get; init; }
    public Vec2 AtBoundary { get; init; }
    public string SideText => Lado is LadoLote.Automatico or LadoLote.Outro ? $"divisa {From}–{To}" : LadoInfo.Label(Lado).ToLowerInvariant() + $" ({From}–{To})";
}

public sealed class BuildingReport
{
    public BuildingRecord Building { get; init; } = null!;
    public LotRecord Lot { get; init; } = null!;
    public double Area { get; init; }
    public double Perimeter { get; init; }
    public double AreaConstruida { get; init; }
    /// <summary>Taxa de ocupação (%) = projeção / área do lote.</summary>
    public double TaxaOcupacao { get; init; }
    /// <summary>Coeficiente de aproveitamento = área construída / área do lote.</summary>
    public double Coeficiente { get; init; }
    /// <summary>Área da edificação fora do lote (m²) – 0 quando está toda dentro.</summary>
    public double AreaFora { get; init; }
    public List<Setback> Afastamentos { get; } = new();
    public double MenorAfastamento => Afastamentos.Count == 0 ? 0 : Afastamentos.Min(a => a.Distance);
}

public static class BuildingAnalyzer
{
    /// <summary>Lote ativo (da fase da edificação) que contém a edificação: o registrado ou, se ele não estiver ativo, o que contém o seu interior.</summary>
    public static LotRecord? OwnerLot(ParcelProject p, BuildingRecord b)
    {
        var lot = p.FindLot(b.LotId);
        if (lot is { Status: LotStatus.Ativo } && lot.FaseId == b.FaseId) return lot;
        Vec2 c;
        try { c = b.ToRegion().InteriorPoint(); } catch { return lot; }
        foreach (var l in p.Lots.Where(l => l.FaseId == b.FaseId && l.Status == LotStatus.Ativo))
        {
            try { if (l.ToRegion().Contains(c)) return l; } catch { }
        }
        return lot;
    }

    /// <summary>Edificações de um lote (na fase do lote).</summary>
    public static List<BuildingRecord> Of(ParcelProject p, LotRecord lot) =>
        p.Buildings.Where(b => b.FaseId == lot.FaseId && OwnerLot(p, b)?.Id == lot.Id).OrderBy(b => b.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();

    public static BuildingReport Analyze(LotReport lot, BuildingRecord b)
    {
        var reg = b.ToRegion();
        var lotReg = lot.Region;
        double fora = 0;
        try
        {
            var inter = Overlay.IntersectionArea(reg, lotReg);
            fora = Math.Max(0, reg.Area - inter);
            if (fora < 0.01) fora = 0;
        }
        catch { }
        double area = reg.Area;
        double construida = b.AreaConstruidaInformada ?? area * Math.Max(1, b.Pavimentos);
        var rep = new BuildingReport
        {
            Building = b, Lot = lot.Lot, Area = area, Perimeter = reg.Outer.Perimeter, AreaConstruida = construida,
            TaxaOcupacao = lot.AreaLocal > 0 ? area / lot.AreaLocal * 100 : 0,
            Coeficiente = lot.AreaLocal > 0 ? construida / lot.AreaLocal : 0,
            AreaFora = fora,
        };
        var bEdges = reg.Outer.Edges.ToList();
        var bPts = bEdges.SelectMany(e => e.Densify(0.05)).ToList();
        for (int i = 0; i < lot.Segments.Count; i++)
        {
            var s = lot.Segments[i];
            double best = double.MaxValue;
            Vec2 pb = default, pl = default;
            // Pontos da edificação até a divisa.
            foreach (var p in bPts)
            {
                var q = s.Curve.ClosestPoint(p, out _);
                double d = p.DistanceTo(q);
                if (d < best) { best = d; pb = p; pl = q; }
            }
            // Pontos da divisa até a edificação (cantos do lote perto de paredes longas).
            foreach (var q in s.Curve.Densify(0.05))
                foreach (var e in bEdges)
                {
                    var p = e.ClosestPoint(q, out _);
                    double d = p.DistanceTo(q);
                    if (d < best) { best = d; pb = p; pl = q; }
                }
            rep.Afastamentos.Add(new Setback
            {
                SegmentIndex = i, From = s.From.Name, To = s.To.Name, Confrontante = s.Confrontante ?? "", Lado = s.Lado,
                Distance = best < 1e-6 ? 0 : best, AtBuilding = pb, AtBoundary = pl,
            });
        }
        return rep;
    }

    /// <summary>Parágrafo do memorial com a área edificada e os afastamentos de cada edificação.</summary>
    public static List<string> MemorialText(IEnumerable<BuildingReport> reps, ValueFormatter fmt)
    {
        var res = new List<string>();
        foreach (var r in reps)
        {
            var b = r.Building;
            string pav = b.Pavimentos == 1 ? "1 pavimento" : $"{b.Pavimentos} pavimentos";
            string t = $"{b.Nome}{(string.IsNullOrWhiteSpace(b.Uso) ? "" : $" ({b.Uso.ToLowerInvariant()})")}: área de projeção de {fmt.Area(r.Area)}, {pav}, " +
                       $"área construída de {fmt.Area(r.AreaConstruida)}, taxa de ocupação de {fmt.Number(r.TaxaOcupacao, 2)}%.";
            // Afastamentos: um por lado (o menor), na ordem das divisas.
            var parts = r.Afastamentos.Select(a => $"{a.SideText}{(a.Confrontante.Length > 0 ? ", " + a.Confrontante : "")}: {fmt.Distance(a.Distance)}");
            t += " Distâncias até as divisas – " + string.Join("; ", parts) + ".";
            if (r.AreaFora > 0) t += $" Atenção: {fmt.Area(r.AreaFora)} da edificação ficam fora do lote.";
            res.Add(t);
        }
        return res;
    }
}
