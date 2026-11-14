using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;

namespace GeoPoint.Core.Operations;

/// <summary>
/// Confrontantes da certidão guardados com as linhas do Desenhar Perímetro e aplicados quando o Reconhecer Lotes cria o lote
/// daquele contorno (o lote não é cadastrado no desenho: o usuário cadastra pelo Reconhecer Lotes, como sempre).
/// </summary>
public static class PendingConfrontantes
{
    /// <summary>Tolerância (m) para a divisa do lote estar sobre a linha desenhada.</summary>
    public const double Tolerance = 0.02;

    /// <summary>Guarda o confrontante de cada linha desenhada (mesma ordem de <paramref name="lines"/>); devolve quantos.</summary>
    public static int Store(ParcelProject p, IReadOnlyList<Curve2> lines, IReadOnlyList<string> lineIds,
        IReadOnlyList<(string Name, bool Testada)> confrontantes, string sourceFile)
    {
        if (lines.Count < 3 || lineIds.Count != lines.Count) return 0;
        Vec2 inside;
        try { inside = new Region(new Ring(lines.ToList())).InteriorPoint(); }
        catch { return 0; }
        int n = 0;
        for (int i = 0; i < lines.Count && i < confrontantes.Count; i++)
        {
            var (name, testada) = confrontantes[i];
            name = (name ?? "").Trim();
            if (name.Length == 0 && !testada) continue;
            // Linha redesenhada no mesmo lugar: o confrontante novo substitui o antigo.
            p.PendingConfrontantes.RemoveAll(x => x.Line.Src.Contains(lineIds[i]));
            p.PendingConfrontantes.Add(new PendingConfrontante
            {
                Line = EdgeDto.FromCurve(lines[i], new[] { lineIds[i] }),
                IX = inside.X, IY = inside.Y, Confrontante = name, Testada = testada, SourceFile = sourceFile ?? "",
            });
            n++;
        }
        return n;
    }

    /// <summary>Tira os pendentes cujas linhas não existem mais no modelo; devolve quantos.</summary>
    public static int Prune(ParcelProject p, Func<string, bool> lineExists) =>
        p.PendingConfrontantes.RemoveAll(x => x.Line.Src.Count == 0 || !x.Line.Src.Any(lineExists));

    /// <summary>
    /// Leva os pendentes para as divisas dos lotes: o ponto de dentro do perímetro desenhado tem de cair no lote e a divisa tem de
    /// estar sobre a linha (ou vir dela). Divisas que já têm confrontante ficam como estão. Devolve quantas divisas receberam.
    /// </summary>
    public static int Apply(ParcelProject p, IEnumerable<LotRecord> lots)
    {
        if (p.PendingConfrontantes.Count == 0) return 0;
        int n = 0;
        var used = new HashSet<PendingConfrontante>();
        foreach (var lot in lots)
        {
            Region reg;
            try { reg = lot.ToRegion(includeHoles: false); }
            catch { continue; }
            foreach (var pc in p.PendingConfrontantes)
            {
                if (!reg.Contains(new Vec2(pc.IX, pc.IY))) continue;
                Curve2 line;
                try { line = pc.Line.ToCurve(); }
                catch { continue; }
                foreach (var e in lot.Outer)
                {
                    var c = e.ToCurve();
                    // A divisa está sobre a linha desenhada (pelo meio e pelas pontas – a linha pode ter sido dividida em mais divisas).
                    bool on = line.DistanceTo(c.Mid) <= Tolerance && line.DistanceTo(c.Start) <= Tolerance && line.DistanceTo(c.End) <= Tolerance;
                    if (!on) continue;
                    used.Add(pc);
                    var mid = c.Mid;
                    var old = lot.EdgeInfos.Where(i => new Vec2(i.MX, i.MY).DistanceTo(mid) <= 0.01).ToList();
                    if (old.Any(i => !i.IsEmpty)) continue;
                    foreach (var i in old) lot.EdgeInfos.Remove(i);
                    lot.EdgeInfos.Add(new EdgeInfo { MX = mid.X, MY = mid.Y, Confrontante = pc.Confrontante, Testada = pc.Testada });
                    n++;
                }
            }
        }
        p.PendingConfrontantes.RemoveAll(used.Contains);
        return n;
    }
}
