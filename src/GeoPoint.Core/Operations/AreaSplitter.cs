using GeoPoint.Core.Geometry;
using GeoPoint.Core.Topology;

namespace GeoPoint.Core.Operations;

public enum SplitMode
{
    /// <summary>N partes de áreas iguais.</summary>
    EqualParts = 0,
    /// <summary>Lotes com área-alvo; o restante fica no último.</summary>
    TargetArea = 1,
    /// <summary>Lotes com testada (frente) fixa medida ao longo da frente.</summary>
    FixedFrontage = 2,
    /// <summary>Área de cada lote informada separadamente (o resto fica no último).</summary>
    CustomAreas = 3,
    /// <summary>Testada de cada lote informada separadamente (o resto fica no último).</summary>
    CustomFrontages = 4,
}

public sealed class SplitRequest
{
    public Region Region { get; init; } = null!;
    /// <summary>Direção das linhas de divisão (vetor no desenho).</summary>
    public Vec2 CutDirection { get; init; }
    /// <summary>Curva da frente (testada), usada para medir testadas e no modo de testada fixa.</summary>
    public Curve2? Frontage { get; init; }
    /// <summary>Começa pelo início (true) ou pelo fim (false) da frente.</summary>
    public bool FromFrontageStart { get; init; } = true;
    public SplitMode Mode { get; init; }
    public int Parts { get; init; } = 2;
    public double TargetArea { get; init; }
    public double FrontageWidth { get; init; }
    /// <summary>No modo área-alvo: se o resto for menor que isto, é incorporado ao último lote.</summary>
    public double MinRemainderArea { get; init; }
    /// <summary>Valores por lote (áreas ou testadas) nos modos personalizados.</summary>
    public IReadOnlyList<double> Values { get; init; } = Array.Empty<double>();
}

public sealed class SplitPiece
{
    public int Index { get; init; }
    public double Area { get; init; }
    public double Frontage { get; init; }
}

public sealed class SplitResult
{
    public List<double> Positions { get; } = new();
    /// <summary>Segmentos de corte (interior ao lote), em coordenadas do desenho.</summary>
    public List<LineCurve> Cuts { get; } = new();
    public List<SplitPiece> Pieces { get; } = new();
    public List<Region> PieceRegions { get; } = new();
    public List<string> Warnings { get; } = new();
}

/// <summary>Divisão de um lote em partes por linhas paralelas (cálculo exato de área com arcos).</summary>
public static class AreaSplitter
{
    /// <summary>
    /// Área da parte da região em que n·p ≤ s (n unitário). Usa o teorema de Green com origem sobre a reta de corte,
    /// de modo que os trechos da própria reta não contribuem — resultado exato para retas e arcos.
    /// </summary>
    public static double AreaBelow(Region region, Vec2 n, double s)
    {
        var o = n * s;
        double total = 0;
        foreach (var ring in region.AllRings())
            foreach (var e in ring.Edges)
                foreach (var piece in ClipBelow(e, n, s))
                    total += piece.GreenIntegral(o);
        return total;
    }

    private static IEnumerable<Curve2> ClipBelow(Curve2 c, Vec2 n, double s)
    {
        var ts = CrossParams(c, n, s);
        ts.Insert(0, 0);
        ts.Add(1);
        for (int i = 0; i < ts.Count - 1; i++)
        {
            double t0 = ts[i], t1 = ts[i + 1];
            if (t1 - t0 <= 1e-15) continue;
            var mid = c.PointAt((t0 + t1) / 2);
            if (n.Dot(mid) <= s) yield return (t0 == 0 && t1 == 1) ? c : c.SubCurve(t0, t1);
        }
    }

    /// <summary>Parâmetros (0,1) em que a curva cruza a reta n·p = s.</summary>
    public static List<double> CrossParams(Curve2 c, Vec2 n, double s)
    {
        var res = new List<double>();
        switch (c)
        {
            case LineCurve l:
            {
                double d0 = n.Dot(l.A) - s, d1 = n.Dot(l.B) - s;
                if ((d0 < 0 && d1 > 0) || (d0 > 0 && d1 < 0))
                    res.Add(d0 / (d0 - d1));
                break;
            }
            case ArcCurve a:
            {
                // n·(C + r u(θ)) = s  →  cos(θ − φ) = (s − n·C)/r
                double q = (s - n.Dot(a.Center)) / a.Radius;
                if (Math.Abs(q) >= 1) break;
                double phi = n.Angle;
                double ac = Math.Acos(q);
                foreach (var th in new[] { phi + ac, phi - ac })
                {
                    var p = a.Center + Vec2.FromAngle(th) * a.Radius;
                    double t = a.ParamOf(p);
                    if (t > 1e-12 && t < 1 - 1e-12) res.Add(t);
                }
                res.Sort();
                break;
            }
        }
        return res;
    }

    public static (double Min, double Max) Range(Region region, Vec2 n)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (var p in region.Outer.ToPolygon(1e-5))
        {
            double v = n.Dot(p);
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }
        return (min, max);
    }

    /// <summary>Posição s em que a área abaixo da reta atinge o alvo (bisseção).</summary>
    public static double SolvePosition(Region region, Vec2 n, double targetArea, double lo, double hi)
    {
        for (int i = 0; i < 200; i++)
        {
            double mid = (lo + hi) / 2;
            double a = AreaBelow(region, n, mid);
            if (a < targetArea) lo = mid; else hi = mid;
            if (hi - lo < 1e-10) break;
        }
        return (lo + hi) / 2;
    }

    /// <summary>Segmentos da reta n·p = s contidos na região.</summary>
    public static List<LineCurve> CutSegments(Region region, Vec2 n, double s)
    {
        var d = n.PerpLeft;
        var o = n * s;
        var ts = new List<double>();
        foreach (var ring in region.AllRings())
            foreach (var e in ring.Edges)
                foreach (var t in CrossParams(e, n, s))
                    ts.Add(d.Dot(e.PointAt(t) - o));
        ts.Sort();
        var uniq = new List<double>();
        foreach (var t in ts) if (uniq.Count == 0 || t - uniq[^1] > 1e-7) uniq.Add(t);
        var res = new List<LineCurve>();
        for (int i = 0; i < uniq.Count - 1; i++)
        {
            var a = o + d * uniq[i];
            var b = o + d * uniq[i + 1];
            if (a.DistanceTo(b) < 1e-4) continue;
            if (region.Contains(Vec2.Lerp(a, b, 0.5))) res.Add(new LineCurve(a, b));
        }
        return res;
    }

    /// <summary>Comprimento da curva entre as retas n·p = s0 e n·p = s1.</summary>
    public static double LengthBetween(Curve2 c, Vec2 n, double s0, double s1)
    {
        if (s1 < s0) (s0, s1) = (s1, s0);
        var ts = new List<double> { 0, 1 };
        ts.AddRange(CrossParams(c, n, s0));
        ts.AddRange(CrossParams(c, n, s1));
        ts.Sort();
        double len = 0;
        for (int i = 0; i < ts.Count - 1; i++)
        {
            if (ts[i + 1] - ts[i] <= 1e-15) continue;
            var mid = c.PointAt((ts[i] + ts[i + 1]) / 2);
            double v = n.Dot(mid);
            if (v >= s0 && v <= s1) len += c.SubCurve(ts[i], ts[i + 1]).Length;
        }
        return len;
    }

    /// <summary>
    /// Corte "em leque": todas as divisas passam pelo ponto <paramref name="pivot"/> (um vértice ou um ponto do contorno)
    /// e giram em torno dele até a parte varrida atingir cada área acumulada de <paramref name="cumulative"/>.
    /// A varredura começa pela divisa que sai do ponto (no sentido do contorno) e gira para dentro do lote.
    /// </summary>
    public static SplitResult SplitFromPoint(Region region, Vec2 pivot, IReadOnlyList<double> cumulative, bool reverse = false)
    {
        var res = new SplitResult();
        double total = region.Area;
        // Tangentes do contorno no ponto (contorno anti-horário: interior à esquerda).
        Vec2? tIn = null, tOut = null;
        foreach (var e in region.Outer.Edges)
        {
            if (e.Start.DistanceTo(pivot) < 1e-6) tOut = e.TangentAt(0).Normalized();
            if (e.End.DistanceTo(pivot) < 1e-6) tIn = e.TangentAt(1).Normalized();
        }
        if (tIn is null || tOut is null)
        {
            var edge = region.Outer.Edges.OrderBy(e => e.DistanceTo(pivot)).First();
            if (edge.DistanceTo(pivot) > 0.01) throw new ArgumentException("O ponto do corte em leque precisa estar no contorno do lote.");
            edge.ClosestPoint(pivot, out double t);
            tIn = tOut = edge.TangentAt(t).Normalized();
        }
        // Sentido da varredura: de tOut girando (anti-horário) até -tIn; "reverse" começa do outro lado.
        var d0 = reverse ? -tIn.Value : tOut.Value;
        double sweep = AngleCcw(tOut.Value, -tIn.Value);
        if (sweep < 1e-6) sweep = 2 * Math.PI;
        double sign = reverse ? -1 : 1;
        // Área do lado já varrido da reta que passa pelo ponto com direção d(φ).
        double Swept(double phi)
        {
            var d = Vec2.FromAngle(Math.Atan2(d0.Y, d0.X) + sign * phi);
            var n = reverse ? d.PerpLeft : d.PerpRight;     // aponta para o lado varrido
            double sPos = n.Dot(pivot);
            return total - AreaBelow(region, n, sPos);
        }
        double prev = 0;
        foreach (double target in cumulative)
        {
            if (target <= prev + 1e-6 || target >= total - 1e-6) { res.Warnings.Add("Áreas acumuladas fora do lote foram ignoradas."); continue; }
            double lo = 0, hi = sweep;
            for (int it = 0; it < 200 && hi - lo > 1e-12; it++)
            {
                double mid = (lo + hi) / 2;
                if (Swept(mid) < target) lo = mid; else hi = mid;
            }
            double phi = (lo + hi) / 2;
            var d = Vec2.FromAngle(Math.Atan2(d0.Y, d0.X) + sign * phi);
            // A divisa vai do ponto até o primeiro encontro com o contorno, para dentro do lote.
            var n = d.PerpRight;
            double sLine = n.Dot(pivot);
            var hits = new List<double>();
            foreach (var ring in region.AllRings())
                foreach (var e in ring.Edges)
                {
                    foreach (var t in CrossParams(e, n, sLine)) { double u = d.Dot(e.PointAt(t) - pivot); if (u > 1e-6) hits.Add(u); }
                    if (Math.Abs(n.Dot(e.End) - sLine) < 1e-9) { double u = d.Dot(e.End - pivot); if (u > 1e-6) hits.Add(u); }
                }
            hits.Sort();
            double? reach = null;
            foreach (var u in hits) if (region.Contains(pivot + d * (u / 2))) { reach = u; break; }
            if (reach is null) { res.Warnings.Add("Não foi possível traçar uma das divisas pelo ponto."); continue; }
            var seg = new LineCurve(pivot, pivot + d * reach.Value);
            res.Cuts.Add(seg);
            res.Positions.Add(phi);
            prev = target;
        }
        return res;
    }

    /// <summary>Ângulo anti-horário (0..2π) de a até b.</summary>
    private static double AngleCcw(Vec2 a, Vec2 b)
    {
        double ang = Math.Atan2(a.Cross(b), a.Dot(b));
        return ang < 0 ? ang + 2 * Math.PI : ang;
    }

    public static SplitResult Split(SplitRequest req)
    {
        var res = new SplitResult();
        var region = req.Region;
        var dir = req.CutDirection.Normalized();
        if (dir.LengthSquared == 0) throw new ArgumentException("Direção de corte inválida.");
        // n é perpendicular às linhas de corte; orienta a varredura a partir do início (ou fim) da frente.
        var n = dir.PerpRight;
        if (req.Frontage is not null)
        {
            var startPt = req.FromFrontageStart ? req.Frontage.Start : req.Frontage.End;
            var endPt = req.FromFrontageStart ? req.Frontage.End : req.Frontage.Start;
            if (n.Dot(endPt - startPt) < 0) n = -n;
        }
        var (min, max) = Range(region, n);
        double total = region.Area;
        var targets = new List<double>();

        switch (req.Mode)
        {
            case SplitMode.EqualParts:
            {
                int parts = Math.Max(2, req.Parts);
                for (int k = 1; k < parts; k++) targets.Add(total * k / parts);
                foreach (var t in targets) res.Positions.Add(SolvePosition(region, n, t, min, max));
                break;
            }
            case SplitMode.TargetArea:
            {
                if (req.TargetArea <= 0) throw new ArgumentException("Informe a área-alvo.");
                int count = (int)Math.Floor(total / req.TargetArea + 1e-9);
                double remainder = total - count * req.TargetArea;
                if (remainder < 1e-6) { count--; remainder = req.TargetArea; }
                else if (remainder < req.MinRemainderArea && count > 1) { count--; }
                for (int k = 1; k <= count; k++)
                {
                    double t = req.TargetArea * k;
                    if (t >= total - 1e-6) break;
                    res.Positions.Add(SolvePosition(region, n, t, min, max));
                }
                break;
            }
            case SplitMode.CustomAreas:
            {
                double acc = 0;
                foreach (var v in req.Values)
                {
                    if (v <= 0) throw new ArgumentException("As áreas devem ser positivas.");
                    acc += v;
                    if (acc >= total - 1e-6) { res.Warnings.Add("A soma das áreas informadas alcança ou passa a área do lote; as últimas foram ignoradas."); break; }
                    res.Positions.Add(SolvePosition(region, n, acc, min, max));
                }
                break;
            }
            case SplitMode.CustomFrontages:
            {
                if (req.Frontage is null) throw new ArgumentException("Selecione a frente (testada).");
                var pts = req.Frontage.Densify(1e-5);
                if (!req.FromFrontageStart) pts.Reverse();
                double target = 0;
                var marks = new List<double>();
                foreach (var v in req.Values) { if (v <= 0) throw new ArgumentException("As testadas devem ser positivas."); target += v; marks.Add(target); }
                double acc = 0; int mi = 0;
                for (int i = 0; i < pts.Count - 1 && mi < marks.Count; i++)
                {
                    double segLen = pts[i].DistanceTo(pts[i + 1]);
                    while (mi < marks.Count && acc + segLen >= marks[mi] - 1e-9 && segLen > 0)
                    {
                        var p = Vec2.Lerp(pts[i], pts[i + 1], (marks[mi] - acc) / segLen);
                        double s = n.Dot(p);
                        if (s > min + 1e-6 && s < max - 1e-6) res.Positions.Add(s);
                        mi++;
                    }
                    acc += segLen;
                }
                if (mi < marks.Count) res.Warnings.Add("A soma das testadas informadas passa o comprimento da frente; as últimas foram ignoradas.");
                break;
            }
            case SplitMode.FixedFrontage:
            {
                if (req.Frontage is null) throw new ArgumentException("Selecione a frente (testada).");
                if (req.FrontageWidth <= 0) throw new ArgumentException("Informe a testada.");
                var f = req.Frontage;
                // Percorre a frente acumulando o comprimento ao longo dela.
                var pts = f.Densify(1e-5);
                if (!req.FromFrontageStart) pts.Reverse();
                double acc = 0, next = req.FrontageWidth;
                double sStart = n.Dot(pts[0]);
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    double segLen = pts[i].DistanceTo(pts[i + 1]);
                    while (acc + segLen >= next - 1e-9 && segLen > 0)
                    {
                        var p = Vec2.Lerp(pts[i], pts[i + 1], (next - acc) / segLen);
                        double s = n.Dot(p);
                        if (s > min + 1e-6 && s < max - 1e-6) res.Positions.Add(s);
                        next += req.FrontageWidth;
                    }
                    acc += segLen;
                }
                double rem = acc - (next - req.FrontageWidth);
                if (rem > 1e-6 && rem < req.FrontageWidth * 0.5 && res.Positions.Count > 0)
                    res.Warnings.Add($"A última testada ficou com {rem:0.00} m (menor que metade da testada informada).");
                _ = sStart;
                break;
            }
        }

        res.Positions.Sort();
        // Remove posições praticamente coincidentes.
        for (int i = res.Positions.Count - 1; i > 0; i--)
            if (res.Positions[i] - res.Positions[i - 1] < 1e-4) res.Positions.RemoveAt(i);

        foreach (var s in res.Positions) res.Cuts.AddRange(CutSegments(region, n, s));

        // Áreas e testadas das partes.
        var bounds = new List<double> { double.NegativeInfinity };
        bounds.AddRange(res.Positions);
        bounds.Add(double.PositiveInfinity);
        double prevArea = 0;
        for (int k = 0; k < bounds.Count - 1; k++)
        {
            double a = k == bounds.Count - 2 ? total : AreaBelow(region, n, bounds[k + 1]);
            double fr = req.Frontage is null ? 0 : LengthBetween(req.Frontage, n, Math.Max(bounds[k], min - 1), Math.Min(bounds[k + 1], max + 1));
            res.Pieces.Add(new SplitPiece { Index = k + 1, Area = a - prevArea, Frontage = fr });
            prevArea = a;
        }

        // Regiões exatas das partes (rede planar com o contorno + cortes).
        try
        {
            var inputs = new List<InputCurve>();
            int tag = 0;
            foreach (var ring in region.AllRings()) foreach (var e in ring.Edges) inputs.Add(new InputCurve(e, tag++));
            foreach (var c in res.Cuts) inputs.Add(new InputCurve(c, tag++));
            var net = PlanarNetwork.Build(inputs);
            foreach (var f in net.Faces.OrderBy(f => n.Dot(f.Region.InteriorPoint())))
                if (region.Contains(f.Region.InteriorPoint())) res.PieceRegions.Add(f.Region);
        }
        catch (Exception ex)
        {
            res.Warnings.Add("Não foi possível montar as partes: " + ex.Message);
        }
        return res;
    }
}
