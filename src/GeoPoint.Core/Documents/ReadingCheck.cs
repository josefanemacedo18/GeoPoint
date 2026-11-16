using System.Globalization;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;

namespace GeoPoint.Core.Documents;

/// <summary>Uma correção feita pela conferência (para mostrar na janela e no diagnóstico).</summary>
public sealed record ReadingFix(string What, string From, string To, string Why);

/// <summary>
/// Conferência cruzada da leitura: um memorial diz a mesma coisa de mais de um jeito (coordenadas, azimutes e distâncias, o polígono
/// que fecha, a área). Quando um número foi lido errado (OCR), as outras informações mostram qual foi e qual seria o valor certo:
/// <list type="bullet">
/// <item>trechos que não fecham: o único trecho cuja troca de um dígito, da vírgula ou do quadrante do rumo fecha o polígono;</item>
/// <item>trecho com azimute ou distância ilegível: calculado pelo fechamento;</item>
/// <item>coordenada que não bate com os dois trechos vizinhos: recalculada pelo azimute e distância do texto;</item>
/// <item>coordenada muito longe das outras (dígito trocado): o dígito que a traz para junto das outras (com a área, se houver).</item>
/// </list>
/// Só corrige quando a correção é única e explica tudo; senão, só avisa onde está o provável erro.
/// </summary>
public static class ReadingCheck
{
    /// <summary>Confere e corrige a leitura (no lugar). Devolve as correções feitas.</summary>
    public static List<ReadingFix> Review(SmartReading r)
    {
        var fixes = new List<ReadingFix>();
        if (r.HasCoordinates)
        {
            ConsensusDigits(r.Coordinates, r.Area, fixes);
            if (r.Coordinates.StatedCourses.Count >= 3) VerticesByCourses(r.Coordinates, fixes);
        }
        else if (r.Courses.Count >= 3) Courses(r.Courses, r.Area, r.Warnings, fixes);
        foreach (var f in fixes) (r.HasCoordinates ? r.Coordinates.Warnings : r.Warnings).Add($"{f.What}: lido {f.From}, corrigido para {f.To} ({f.Why}). Confira no documento.");
        r.Fixes.AddRange(fixes);
        return fixes;
    }

    // ------------------------------------------------------------------ trechos (azimutes e distâncias)

    private static Vec2 V(Course c) => new(Math.Sin(c.AzimuthDeg * Math.PI / 180) * c.Distance, Math.Cos(c.AzimuthDeg * Math.PI / 180) * c.Distance);
    private static double Az(Vec2 v) => (Math.Atan2(v.X, v.Y) * 180 / Math.PI + 360) % 360;
    private static Vec2 Sum(IEnumerable<Course> cs) => cs.Aggregate(new Vec2(0, 0), (a, c) => a + V(c));
    private static double Tol(IEnumerable<Course> cs) => Math.Max(0.05, cs.Where(c => !double.IsNaN(c.Distance)).Sum(c => c.Distance) / 5000);

    public static double ShoelaceArea(IReadOnlyList<Course> cs)
    {
        var p = new Vec2(0, 0);
        double a2 = 0;
        foreach (var c in cs)
        {
            var q = p + V(c);
            a2 += p.X * q.Y - q.X * p.Y;
            p = q;
        }
        return Math.Abs(a2) / 2;
    }

    public static void Courses(List<Course> cs, double? area, List<string> warnings, List<ReadingFix> fixes)
    {
        var bad = cs.Select((c, i) => (c, i)).Where(x => double.IsNaN(x.c.AzimuthDeg) || double.IsNaN(x.c.Distance)).ToList();
        if (bad.Count == 1)
        {
            // Um trecho com azimute ou distância ilegível: o fechamento dá o que falta.
            var (c, i) = bad[0];
            var others = cs.Where((_, k) => k != i).ToList();
            var need = Sum(others) * -1;
            double tol = Tol(others);
            if (double.IsNaN(c.AzimuthDeg) && double.IsNaN(c.Distance))
            {
                cs[i] = new Course { AzimuthDeg = Az(need), Distance = Math.Round(need.Length, 2), Source = c.Source };
                fixes.Add(new ReadingFix($"Trecho {i + 1}", "ilegível", $"{Fmt(Az(need))} e {N2(need.Length)} m", "calculado pelo fechamento do polígono"));
            }
            else if (double.IsNaN(c.AzimuthDeg) && Math.Abs(need.Length - c.Distance) <= tol)
            {
                cs[i] = new Course { AzimuthDeg = Az(need), Distance = c.Distance, Source = c.Source };
                fixes.Add(new ReadingFix($"Azimute do trecho {i + 1}", "ilegível", Fmt(Az(need)), "calculado pelo fechamento do polígono"));
            }
            else if (double.IsNaN(c.Distance) && PerimeterText.AngleDiff(Az(need), c.AzimuthDeg) * Math.PI / 180 * need.Length <= tol)
            {
                cs[i] = new Course { AzimuthDeg = c.AzimuthDeg, Distance = Math.Round(need.Length, 2), Source = c.Source };
                fixes.Add(new ReadingFix($"Distância do trecho {i + 1}", "ilegível", N2(need.Length) + " m", "calculada pelo fechamento do polígono"));
            }
            else warnings.Add($"O trecho {i + 1} está incompleto e o resto não fecha o polígono: confira os valores no documento.");
            return;
        }
        if (bad.Count > 1) return;
        double t = Tol(cs);
        var miss = Sum(cs);
        if (miss.Length <= t) return;
        // Um número lido errado: para cada trecho, as leituras parecidas com a lida (um dígito trocado, a vírgula fora do lugar, os
        // graus, os minutos ou os segundos trocados, o quadrante do rumo) – fica a que fecha o polígono, se for uma só.
        var cands = new List<(int I, Course C, double Res, string What, string From, string To, string Why)>();
        for (int i = 0; i < cs.Count; i++)
        {
            var c = cs[i];
            var rest = miss - V(c);
            foreach (var (nd, why) in DistanceVariants(c.Distance))
            {
                var cc = new Course { AzimuthDeg = c.AzimuthDeg, Distance = nd, Source = c.Source };
                double res = (rest + V(cc)).Length;
                if (res <= t) cands.Add((i, cc, res, $"Distância do trecho {i + 1}", N2(c.Distance) + " m", N2(nd) + " m", why));
            }
            foreach (var (na, why) in AngleVariants(c.AzimuthDeg))
            {
                var cc = new Course { AzimuthDeg = na, Distance = c.Distance, Source = c.Source };
                double res = (rest + V(cc)).Length;
                if (res <= t) cands.Add((i, cc, res, $"Azimute do trecho {i + 1}", Fmt(c.AzimuthDeg), Fmt(na), why));
            }
        }
        // Com a área do documento, a correção precisa bater com ela também.
        if (area is double ar && ar > 0)
            cands = cands.Where(x =>
            {
                var test = cs.ToList();
                test[x.I] = x.C;
                return Math.Abs(ShoelaceArea(test) - ar) <= Math.Max(0.5, ar * 0.005);
            }).ToList();
        // A melhor de cada número (trecho e azimute ou distância); só corrige se um número explica bem melhor que os outros.
        cands = cands.GroupBy(x => (x.I, x.What)).Select(g => g.OrderBy(x => x.Res).First()).OrderBy(x => x.Res).ToList();
        if (cands.Count >= 1 && (cands.Count == 1 || cands[1].Res > Math.Max(cands[0].Res * 2.5, t * 0.3)))
        {
            var x = cands[0];
            cs[x.I] = x.C;
            fixes.Add(new ReadingFix(x.What, x.From, x.To, x.Why + "; assim o polígono fecha" + (area is not null ? " e a área confere" : "")));
        }
        else if (cands.Count > 1)
            warnings.Add($"O polígono não fecha ({N2(miss.Length)} m de erro): provável erro de leitura no(s) trecho(s) {string.Join(", ", cands.Select(c => c.I + 1).Distinct().OrderBy(k => k))}. Confira no documento.");
        else warnings.Add($"O polígono não fecha ({N2(miss.Length)} m de erro) e nenhum valor sozinho explica a diferença: confira os trechos no documento.");
    }

    /// <summary>Distâncias parecidas com a lida: um dígito trocado, dois vizinhos trocados de lugar, a vírgula em outro lugar.</summary>
    public static IEnumerable<(double D, string Why)> DistanceVariants(double read)
    {
        foreach (double k in new[] { 10.0, 100.0, 0.1, 0.01 })
            if (read / k > 0.01) yield return (Math.Round(read / k, 2), "vírgula no lugar errado");
        string a = read.ToString("0.00", CultureInfo.InvariantCulture);
        for (int i = 0; i < a.Length; i++)
        {
            if (!char.IsDigit(a[i])) continue;
            for (char d = '0'; d <= '9'; d++)
                if (d != a[i] && double.TryParse(a[..i] + d + a[(i + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) && x > 0)
                    yield return (x, "um dígito lido errado");
            if (i + 1 < a.Length && char.IsDigit(a[i + 1]) && a[i] != a[i + 1] &&
                double.TryParse(a[..i] + a[i + 1] + a[i] + a[(i + 2)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double y) && y > 0)
                yield return (y, "dois dígitos trocados de lugar");
        }
    }

    /// <summary>Azimutes parecidos com o lido: graus, minutos ou segundos trocados, ou o quadrante do rumo.</summary>
    public static IEnumerable<(double Az, string Why)> AngleVariants(double read)
    {
        foreach (double alt in new[] { 180 - read, 180 + read, 360 - read, read - 180 })
            yield return (((alt % 360) + 360) % 360, "quadrante do rumo lido errado");
        var (d, m, sec) = Dms(read);
        for (int dd = 0; dd < 360; dd++) if (dd != d) yield return (dd + m / 60.0 + sec / 3600.0, "graus lidos errado");
        for (int mm = 0; mm < 60; mm++) if (mm != m) yield return (d + mm / 60.0 + sec / 3600.0, "minutos lidos errado");
        for (int ss = 0; ss < 60; ss++) if (ss != sec) yield return (d + m / 60.0 + ss / 3600.0, "segundos lidos errado");
    }

    private static (int D, int M, int S) Dms(double a)
    {
        long t = (long)Math.Round(((a % 360) + 360) % 360 * 3600);
        return ((int)(t / 3600), (int)(t / 60 % 60), (int)(t % 60));
    }

    private static string Fmt(double az) => Geodesy.AngleText.FormatDms(az, 0, Geodesy.AngleKind.Azimuth);
    private static string N2(double v) => v.ToString("#,##0.00", new CultureInfo("pt-BR"));

    // ------------------------------------------------------------------ coordenadas

    /// <summary>Coordenadas no plano local (m) – UTM e locais como estão; geográficas pelos raios do elipsoide na latitude média.</summary>
    private static (List<Vec2> Pts, Func<Vec2, (double A, double B)> Back) Plane(CoordinateTextResult c)
    {
        var v = c.Vertices;
        if (c.Kind != PerimeterCoordKind.Geographic) return (v.Select(x => new Vec2(x.A, x.B)).ToList(), p => (p.X, p.Y));
        double lat0 = v.Average(x => x.A), lon0 = v.Average(x => x.B);
        const double a = 6378137.0, f = 1 / 298.257222101;
        double e2 = f * (2 - f), sn = Math.Sin(lat0 * Math.PI / 180), w = Math.Sqrt(1 - e2 * sn * sn);
        double M = a * (1 - e2) / (w * w * w), Nr = a / w, kx = Nr * Math.Cos(lat0 * Math.PI / 180) * Math.PI / 180, ky = M * Math.PI / 180;
        return (v.Select(x => new Vec2((x.B - lon0) * kx, (x.A - lat0) * ky)).ToList(), p => (lat0 + p.Y / ky, lon0 + p.X / kx));
    }

    /// <summary>
    /// Vértice que não bate com os dois trechos vizinhos (e os outros trechos batem): a posição pelo trecho anterior também chega certa
    /// no próximo vértice pelo trecho seguinte – é a coordenada certa. Corrige só a coordenada (E ou N) que mudou.
    /// </summary>
    private static void VerticesByCourses(CoordinateTextResult c, List<ReadingFix> fixes)
    {
        var v = c.Vertices;
        int n = v.Count;
        var st = c.StatedCourses;
        if (st.Count < n - 1 || st.Count > n) return;
        var (pts, back) = Plane(c);
        // Rotação e escala entre os trechos do texto e as coordenadas (azimute verdadeiro × de quadrícula; distância plana × real).
        var rot = new List<double>();
        var sc = new List<double>();
        for (int i = 0; i < st.Count; i++)
        {
            var d = pts[(i + 1) % n] - pts[i];
            if (d.Length < 0.01 || double.IsNaN(st[i].AzimuthDeg) || double.IsNaN(st[i].Distance)) continue;
            double dr = ((st[i].AzimuthDeg - Az(d)) % 360 + 540) % 360 - 180;
            if (Math.Abs(dr) < 3 && Math.Abs(st[i].Distance / d.Length - 1) < 0.01) { rot.Add(dr); sc.Add(st[i].Distance / d.Length); }
        }
        if (rot.Count < Math.Max(2, st.Count / 2)) return;
        double r0 = Median(rot), k0 = Median(sc);
        Vec2 Step(int i) => V(new Course { AzimuthDeg = st[i].AzimuthDeg - r0, Distance = st[i].Distance / k0 });
        bool Ok(int i)
        {
            var d = pts[(i + 1) % n] - pts[i];
            return (d - Step(i)).Length <= Math.Max(0.03, st[i].Distance * 0.0005);
        }
        for (int k = 0; k < n; k++)
        {
            int prev = (k - 1 + n) % n;
            if (prev >= st.Count || k >= st.Count) continue;
            if (Ok(prev) || Ok(k)) continue;
            // Os outros trechos batem?
            int good = Enumerable.Range(0, st.Count).Count(i => i != prev && i != k && Ok(i));
            if (good < st.Count - 2) continue;
            var p = pts[prev] + Step(prev);
            if ((pts[(k + 1) % n] - p - Step(k)).Length > Math.Max(0.05, st[k].Distance * 0.001)) continue;
            var (na, nb) = back(p);
            var old = v[k];
            bool geo = c.Kind == PerimeterCoordKind.Geographic;
            double tolA = geo ? 2e-6 : 0.05;
            bool aDiff = Math.Abs(na - old.A) > tolA, bDiff = Math.Abs(nb - old.B) > tolA;
            if (aDiff && bDiff) continue;   // as duas mudaram: não é um número lido errado
            string label = geo ? (aDiff ? "Latitude" : "Longitude") : c.Kind == PerimeterCoordKind.Utm ? (aDiff ? "E" : "N") : (aDiff ? "X" : "Y");
            double from = aDiff ? old.A : old.B, to = aDiff ? na : nb;
            if (!geo) to = Math.Round(to, 2);
            if (aDiff) old.A = to; else old.B = to;
            pts[k] = p;
            fixes.Add(new ReadingFix($"{label} do vértice {old.Name}", Num(from, geo), Num(to, geo), "pelo azimute e distância dos trechos vizinhos, que batem com as outras coordenadas"));
        }
    }

    /// <summary>
    /// Coordenada muito longe das outras (um dígito trocado no OCR, ex.: 098.388 no lugar de 698.388): a troca de um dígito que a traz
    /// para junto das outras – única, ou a que faz a área bater com a do documento.
    /// </summary>
    private static void ConsensusDigits(CoordinateTextResult c, double? area, List<ReadingFix> fixes)
    {
        var v = c.Vertices;
        if (v.Count < 4) return;
        bool geo = c.Kind == PerimeterCoordKind.Geographic;
        foreach (bool isA in new[] { true, false })
        {
            double Get(PerimeterVertex x) => isA ? x.A : x.B;
            var vals = v.Select(Get).OrderBy(x => x).ToList();
            double med = vals[vals.Count / 2];
            // Espalhamento "normal": a metade do meio.
            double spread = Math.Max(vals[(int)(vals.Count * 0.75)] - vals[(int)(vals.Count * 0.25)], geo ? 1e-5 : 1);
            double lim = spread * 20;
            foreach (var x in v)
            {
                double val = Get(x);
                if (Math.Abs(val - med) <= lim) continue;
                var others = v.Where(o => o != x).Select(Get).ToList();
                double lo = others.Min() - spread * 2, hi = others.Max() + spread * 2;
                var options = DigitVariants(val, geo ? 7 : 3).Where(o => o >= lo && o <= hi).Distinct().ToList();
                if (options.Count > 1 && area is double ar && ar > 0 && !geo)
                    options = options.Where(o =>
                    {
                        double keep = Get(x);
                        Set(x, isA, o);
                        double a = PolyArea(v);
                        Set(x, isA, keep);
                        return Math.Abs(a - ar) <= Math.Max(0.5, ar * 0.005);
                    }).ToList();
                if (options.Count != 1) continue;
                Set(x, isA, options[0]);
                string label = geo ? (isA ? "Latitude" : "Longitude") : c.Kind == PerimeterCoordKind.Utm ? (isA ? "E" : "N") : (isA ? "X" : "Y");
                fixes.Add(new ReadingFix($"{label} do vértice {x.Name}", Num(val, geo), Num(options[0], geo), "um dígito lido errado: o valor ficava longe dos outros vértices"));
            }
        }
        if (fixes.Count > 0 && !geo && v.All(x => SmartReader.UtmE(x.A) && SmartReader.UtmN(x.B))) c.Kind = PerimeterCoordKind.Utm;
    }

    private static void Set(PerimeterVertex x, bool isA, double val) { if (isA) x.A = val; else x.B = val; }

    private static double PolyArea(List<PerimeterVertex> v)
    {
        double a2 = 0;
        for (int i = 0; i < v.Count; i++)
        {
            var p = v[i];
            var q = v[(i + 1) % v.Count];
            a2 += p.A * q.B - q.A * p.B;
        }
        return Math.Abs(a2) / 2;
    }

    /// <summary>O número com um dígito trocado, a mais ou a menos (cada posição da parte inteira e decimal), com as mesmas casas decimais.</summary>
    private static IEnumerable<double> DigitVariants(double v, int decimals)
    {
        string s = Math.Abs(v).ToString("F" + decimals, CultureInfo.InvariantCulture);
        int dot = s.IndexOf('.');
        double? P(string t) => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ? (v < 0 ? -x : x) : null;
        for (int i = 0; i < s.Length; i++)
        {
            if (!char.IsDigit(s[i])) continue;
            for (char d = '0'; d <= '9'; d++)
                if (d != s[i] && P(s[..i] + d + s[(i + 1)..]) is double x) yield return x;
            // Dígito a mais na parte inteira (lido em dobro).
            if (dot < 0 || i < dot) if (P(s[..i] + s[(i + 1)..]) is double y) yield return y;
        }
        // Dígito perdido na parte inteira ("98.388" no lugar de "698.388").
        for (int i = 0; i <= (dot < 0 ? s.Length : dot); i++)
            for (char d = '0'; d <= '9'; d++)
                if (P(s[..i] + d + s[i..]) is double x) yield return x;
    }

    private static double Median(List<double> l)
    {
        var s = l.OrderBy(x => x).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }

    private static string Num(double v, bool geo) => geo
        ? Geodesy.AngleText.FormatDms(v, 3, Geodesy.AngleKind.Latitude)
        : v.ToString("#,##0.00", new CultureInfo("pt-BR"));
}
