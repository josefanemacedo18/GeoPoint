using System.Globalization;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;

namespace GeoPoint.Core.Documents;

/// <summary>Uma correção feita pela conferência (para mostrar na janela e no diagnóstico).</summary>
/// <param name="Derived">Valor ilegível calculado pelo fechamento (não lido): o fechamento deixa de conferir os outros trechos.</param>
public sealed record ReadingFix(string What, string From, string To, string Why, bool Derived = false);

/// <summary>
/// Conferência cruzada da leitura: um memorial diz a mesma coisa de mais de um jeito (coordenadas, azimutes e distâncias, o polígono
/// que fecha, a área). Quando um número foi lido errado (OCR), as outras informações mostram qual foi e qual seria o valor certo:
/// <list type="bullet">
/// <item>trechos que não fecham: o trecho cuja troca de um dígito, da vírgula, dos graus/minutos/segundos ou do quadrante do rumo
/// fecha o polígono dentro do arredondamento do documento;</item>
/// <item>trecho com azimute ou distância ilegível: calculado pelo fechamento (marcado como calculado);</item>
/// <item>coordenada que não bate com os dois trechos vizinhos: o dígito que a faz bater com eles;</item>
/// <item>coordenada muito longe das outras (dígito trocado ou perdido): o dígito que a traz para junto das outras e faz a área
/// bater com a do documento.</item>
/// </list>
/// Regra: só corrige quando existe UMA única leitura parecida com a lida que cabe na precisão do documento. Com duas ou mais
/// possíveis (ex.: 5,42 ou 5,43 – o arredondamento não decide), não escolhe: avisa onde está o erro e pede para conferir. Toda
/// correção aparece para você confirmar antes de desenhar.
/// </summary>
public static class ReadingCheck
{
    /// <summary>Confere e corrige a leitura (no lugar). Devolve as correções feitas.</summary>
    public static List<ReadingFix> Review(SmartReading r)
    {
        var fixes = new List<ReadingFix>();
        if (r.HasCoordinates)
        {
            ConsensusDigits(r.Coordinates, r.Area, r.AreaStep, fixes, r.Coordinates.Warnings);
            if (r.Coordinates.StatedCourses.Count >= 3) VerticesByCourses(r.Coordinates, fixes, r.Coordinates.Warnings);
        }
        else if (r.Courses.Count >= 3) Courses(r.Courses, r.Area, r.Warnings, fixes, r.AreaStep);
        foreach (var f in fixes)
            (r.HasCoordinates ? r.Coordinates.Warnings : r.Warnings).Add(f.Derived
                ? $"{f.What}: {f.From}, calculado {f.To} ({f.Why}). Confira no documento."
                : $"{f.What}: lido {f.From}, corrigido para {f.To} ({f.Why}). Confira no documento.");
        r.Fixes.AddRange(fixes);
        return fixes;
    }

    // ------------------------------------------------------------------ trechos (azimutes e distâncias)

    private static Vec2 V(Course c) => new(Math.Sin(c.AzimuthDeg * Math.PI / 180) * c.Distance, Math.Cos(c.AzimuthDeg * Math.PI / 180) * c.Distance);
    private static double Az(Vec2 v) => (Math.Atan2(v.X, v.Y) * 180 / Math.PI + 360) % 360;
    private static Vec2 Sum(IEnumerable<Course> cs) => cs.Aggregate(new Vec2(0, 0), (a, c) => a + V(c));

    public static double ShoelaceArea(IReadOnlyList<Course> cs) => DrawingCheck.Shoelace(DrawingCheck.TraversePoints(cs));

    private static Course With(Course c, double az, double d) => new()
    {
        AzimuthDeg = az, Distance = d, Radius = c.Radius, ArcToRight = c.ArcToRight, Source = c.Source,
        AzimuthStep = c.AzimuthStep, DistanceStep = c.DistanceStep,
    };

    private static int DecimalsOf(double step) => Math.Clamp((int)Math.Round(-Math.Log10(step)), 0, 6);

    /// <param name="area">Área escrita no documento (m²), para exigir que a correção também a faça bater.</param>
    public static void Courses(List<Course> cs, double? area, List<string> warnings, List<ReadingFix> fixes, double areaStep = 0.01)
    {
        var bad = cs.Select((c, i) => (c, i)).Where(x => double.IsNaN(x.c.AzimuthDeg) || double.IsNaN(x.c.Distance)).ToList();
        if (bad.Count == 1)
        {
            // Um trecho com azimute ou distância ilegível: o fechamento dá o que falta (com a incerteza do arredondamento dos outros).
            var (c, i) = bad[0];
            var others = cs.Where((_, k) => k != i).ToList();
            var need = Sum(others) * -1;
            double unc = others.Sum(WrittenPrecision.CourseError) + 0.001;
            int dec = DecimalsOf(WrittenPrecision.DistanceStepOf(others[0]));
            if (double.IsNaN(c.AzimuthDeg) && double.IsNaN(c.Distance))
            {
                cs[i] = With(c, Az(need), Math.Round(need.Length, dec));
                fixes.Add(new ReadingFix($"Trecho {i + 1}", "ilegível", $"{Fmt(Az(need))} e {N(need.Length, dec)} m", $"pelo fechamento do polígono, ±{N(unc, 3)} m", true));
            }
            else if (double.IsNaN(c.AzimuthDeg) && Math.Abs(need.Length - c.Distance) <= unc + WrittenPrecision.DistanceStepOf(c) / 2)
            {
                cs[i] = With(c, Az(need), c.Distance);
                fixes.Add(new ReadingFix($"Azimute do trecho {i + 1}", "ilegível", Fmt(Az(need)), "pelo fechamento do polígono", true));
            }
            else if (double.IsNaN(c.Distance) && Math.Abs(need.X * Math.Cos(c.AzimuthDeg * Math.PI / 180) - need.Y * Math.Sin(c.AzimuthDeg * Math.PI / 180)) <= unc)
            {
                cs[i] = With(c, c.AzimuthDeg, Math.Round(need.Length, dec));
                fixes.Add(new ReadingFix($"Distância do trecho {i + 1}", "ilegível", N(need.Length, dec) + " m", $"pelo fechamento do polígono, ±{N(unc, 3)} m", true));
            }
            else warnings.Add($"O trecho {i + 1} está incompleto e o resto não fecha o polígono: confira os valores no documento.");
            return;
        }
        if (bad.Count > 1) return;
        // O polígono fecha dentro do que o arredondamento dos trechos explica: nada a corrigir.
        double bound = cs.Sum(WrittenPrecision.CourseError) + 0.001;
        var miss = Sum(cs);
        if (miss.Length <= bound) return;
        // Um número lido errado: para cada trecho, as leituras parecidas com a lida (um dígito trocado, a vírgula fora do lugar, os
        // graus, os minutos ou os segundos trocados, o quadrante do rumo) que fazem o polígono fechar dentro do arredondamento.
        var cands = new List<(int I, bool Az, double Value, Course C, string What, string From, string To, string Why)>();
        for (int i = 0; i < cs.Count; i++)
        {
            var c = cs[i];
            var rest = miss - V(c);
            int dDec = DecimalsOf(WrittenPrecision.DistanceStepOf(c));
            foreach (var (nd, why) in DistanceVariants(c.Distance, dDec))
            {
                var cc = With(c, c.AzimuthDeg, nd);
                // Com a vírgula de volta, o valor tem as casas que tinha no documento.
                if (why.StartsWith("vírgula"))
                {
                    string txt = nd.ToString("0.####", CultureInfo.InvariantCulture);
                    int k = txt.IndexOf('.');
                    cc.DistanceStep = Math.Pow(10, -Math.Max(dDec, k < 0 ? 0 : txt.Length - k - 1));
                }
                if ((rest + V(cc)).Length <= bound)
                    cands.Add((i, false, nd, cc, $"Distância do trecho {i + 1}", N(c.Distance, dDec) + " m", N(nd, dDec) + " m", why));
            }
            foreach (var (na, why) in AngleVariants(c.AzimuthDeg, WrittenPrecision.AzimuthStepOf(c)))
            {
                var cc = With(c, na, c.Distance);
                if ((rest + V(cc)).Length <= bound)
                    cands.Add((i, true, na, cc, $"Azimute do trecho {i + 1}", Fmt(c.AzimuthDeg), Fmt(na), why));
            }
        }
        // Com a área do documento, a correção precisa bater com ela também.
        if (area is double ar && ar > 0)
            cands = cands.Where(x =>
            {
                var test = cs.ToList();
                test[x.I] = x.C;
                double tol = DrawingCheck.CourseAreaTolerance(test) + areaStep / 2 + 0.01 + bound * 50;
                return Math.Abs(ShoelaceArea(test) - ar) <= tol;
            }).ToList();
        cands = cands.GroupBy(x => (x.I, x.Az, Math.Round(x.Value, 7))).Select(g => g.First()).ToList();
        if (cands.Count == 1)
        {
            var x = cands[0];
            cs[x.I] = x.C;
            fixes.Add(new ReadingFix(x.What, x.From, x.To, x.Why + "; é a única leitura que fecha o polígono" + (area is not null ? " e faz a área bater" : "")));
        }
        else if (cands.Count > 1)
        {
            var where = cands.Select(c => c.I + 1).Distinct().OrderBy(k => k).ToList();
            string options = string.Join(" ou ", cands.Take(4).Select(c => $"{c.What.ToLowerInvariant()} = {c.To}"));
            warnings.Add($"O polígono não fecha ({N(miss.Length, 3)} m de erro): provável erro de leitura no(s) trecho(s) {string.Join(", ", where)}. " +
                         $"Mais de um valor explicaria ({options}{(cands.Count > 4 ? "…" : "")}): o GeoPoint não escolhe – confira no documento.");
        }
        else warnings.Add($"O polígono não fecha ({N(miss.Length, 3)} m de erro) e nenhum número sozinho explica a diferença: confira os trechos no documento.");
    }

    /// <summary>Distâncias parecidas com a lida: um dígito trocado, dois vizinhos trocados de lugar, a vírgula em outro lugar.</summary>
    public static IEnumerable<(double D, string Why)> DistanceVariants(double read, int decimals = 2)
    {
        // Vírgula perdida ou fora do lugar: "3144" era "31,44" (as casas decimais voltam junto com a vírgula).
        foreach (int p in new[] { 1, 2, 3, -1, -2 })
        {
            double x = read / Math.Pow(10, p);
            if (x > 0.01) yield return (Math.Round(x, Math.Max(0, decimals + p)), "vírgula no lugar errado");
        }
        string a = read.ToString("F" + decimals, CultureInfo.InvariantCulture);
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

    /// <summary>
    /// Azimutes parecidos com o lido: o quadrante do rumo, e um dígito trocado nos graus, nos minutos ou nos segundos (ou nas casas
    /// do azimute em graus decimais).
    /// </summary>
    public static IEnumerable<(double Az, string Why)> AngleVariants(double read, double step = 1.0 / 3600)
    {
        foreach (double alt in new[] { 180 - read, 180 + read, 360 - read, read - 180 })
            yield return (((alt % 360) + 360) % 360, "quadrante do rumo lido errado");
        bool dms = Math.Abs(step * 3600 - Math.Round(step * 3600)) < 1e-9 || Math.Abs(step * 60 - Math.Round(step * 60)) < 1e-9;
        if (!dms)
        {
            // Azimute em graus decimais ("120,6839°"): um dígito trocado.
            foreach (var (x, _) in DistanceVariants(read, DecimalsOf(step)))
                if (x < 360) yield return (x, "um dígito do azimute lido errado");
            yield break;
        }
        var (d, m, sec) = Dms(read);
        string ds = d.ToString("000", CultureInfo.InvariantCulture), ms = m.ToString("00", CultureInfo.InvariantCulture), ss = sec.ToString("00", CultureInfo.InvariantCulture);
        foreach (int dd in OneDigit(ds).Where(x => x < 360)) yield return (dd + m / 60.0 + sec / 3600.0, "graus lidos errado");
        foreach (int mm in OneDigit(ms).Where(x => x < 60)) yield return (d + mm / 60.0 + sec / 3600.0, "minutos lidos errado");
        foreach (int s2 in OneDigit(ss).Where(x => x < 60)) yield return (d + m / 60.0 + s2 / 3600.0, "segundos lidos errado");
    }

    /// <summary>Inteiros com um dígito trocado (ou dois vizinhos trocados de lugar).</summary>
    private static IEnumerable<int> OneDigit(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            for (char c = '0'; c <= '9'; c++)
                if (c != s[i]) yield return int.Parse(s[..i] + c + s[(i + 1)..], CultureInfo.InvariantCulture);
            if (i + 1 < s.Length && s[i] != s[i + 1]) yield return int.Parse(s[..i] + s[i + 1] + s[i] + s[(i + 2)..], CultureInfo.InvariantCulture);
        }
    }

    private static (int D, int M, int S) Dms(double a)
    {
        long t = (long)Math.Round(((a % 360) + 360) % 360 * 3600);
        return ((int)(t / 3600), (int)(t / 60 % 60), (int)(t % 60));
    }

    private static string Fmt(double az) => Geodesy.AngleText.FormatDms(az, 0, Geodesy.AngleKind.Azimuth);
    private static string N(double v, int dec) => v.ToString("#,##0." + new string('0', Math.Max(1, dec)), new CultureInfo("pt-BR"));

    // ------------------------------------------------------------------ coordenadas

    /// <summary>Coordenadas no plano local (m) – UTM e locais como estão; geográficas pelos raios do elipsoide na latitude média.</summary>
    private static List<Vec2> Plane(CoordinateTextResult c) => ReadingChoice.PlanePoints(c);

    /// <summary>
    /// Vértice que não bate com os dois trechos vizinhos (e os outros trechos batem): a posição pelo trecho anterior também chega certa
    /// no próximo vértice pelo trecho seguinte. Só corrige se uma única troca de dígito na coordenada lida (E ou N) a faz bater –
    /// senão, avisa qual vértice está errado.
    /// </summary>
    private static void VerticesByCourses(CoordinateTextResult c, List<ReadingFix> fixes, List<string> warnings)
    {
        var v = c.Vertices;
        int n = v.Count;
        var st = c.StatedCourses;
        if (st.Count < n - 1 || st.Count > n) return;
        if (c.Kind == PerimeterCoordKind.Geographic) { WarnBadVertices(c, warnings); return; }
        var pts = Plane(c);
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
        double Eps(int i) => WrittenPrecision.RoundingOf(v[i], c.Kind);
        double Tol(int i) => 2 * Math.Sqrt(2) * Math.Max(Eps(i), Eps((i + 1) % n)) + WrittenPrecision.CourseError(st[i]) + 0.002;
        bool Ok(int i) => (pts[(i + 1) % n] - pts[i] - Step(i)).Length <= Tol(i);
        for (int k = 0; k < n; k++)
        {
            int prev = (k - 1 + n) % n;
            if (prev >= st.Count || k >= st.Count) continue;
            if (Ok(prev) || Ok(k)) continue;
            int good = Enumerable.Range(0, st.Count).Count(i => i != prev && i != k && Ok(i));
            if (good < st.Count - 2) continue;
            var p = pts[prev] + Step(prev);
            double tolP = Tol(prev) + Tol(k);
            if ((pts[(k + 1) % n] - p - Step(k)).Length > tolP) continue;
            var old = v[k];
            bool aDiff = Math.Abs(p.X - old.A) > tolP, bDiff = Math.Abs(p.Y - old.B) > tolP;
            string Label(bool isA) => c.Kind == PerimeterCoordKind.Utm ? (isA ? "E" : "N") : (isA ? "X" : "Y");
            if (aDiff == bDiff)
            {
                warnings.Add($"O vértice {old.Name} não confere com os trechos vizinhos (pelos trechos ficaria em {N(p.X, 2)} / {N(p.Y, 2)}): confira as coordenadas no documento.");
                continue;
            }
            double read = aDiff ? old.A : old.B, want = aDiff ? p.X : p.Y;
            int dec = DecimalsOf(2 * Eps(k));
            var options = DigitVariants(read, dec).Where(x => Math.Abs(x - want) <= tolP).Distinct().ToList();
            if (options.Count != 1)
            {
                warnings.Add($"{Label(aDiff)} do vértice {old.Name} não confere com os trechos vizinhos (lido {N(read, dec)}, pelos trechos ≈ {N(want, dec)}): confira no documento.");
                continue;
            }
            if (aDiff) old.A = options[0]; else old.B = options[0];
            pts[k] = new Vec2(old.A, old.B);
            fixes.Add(new ReadingFix($"{Label(aDiff)} do vértice {old.Name}", N(read, dec), N(options[0], dec),
                "um dígito lido errado; é o único valor que faz os dois trechos vizinhos baterem"));
        }
    }

    /// <summary>Geográficas: só avisa o vértice cujos dois trechos vizinhos não conferem (sem corrigir).</summary>
    private static void WarnBadVertices(CoordinateTextResult c, List<string> warnings)
    {
        var r = PerimeterText.Build(c.Vertices, c.Kind, c.Datum ?? Geodesy.DatumId.Sirgas2000, 23, true, true, c.StatedCourses);
        int n = c.Vertices.Count;
        for (int k = 0; k < n; k++)
        {
            int prev = (k - 1 + n) % n;
            var a = r.Segments.FirstOrDefault(s => s.From == prev);
            var b = r.Segments.FirstOrDefault(s => s.From == k);
            if (a?.Matches == false && b?.Matches == false)
                warnings.Add($"O vértice {c.Vertices[k].Name} não confere com os dois trechos vizinhos: provável coordenada lida errada – confira no documento.");
        }
    }

    /// <summary>
    /// Coordenada muito longe das outras (um dígito trocado no OCR, ex.: 098.388 no lugar de 698.388): corrige só com a área do
    /// documento – a única troca de um dígito que traz o vértice para junto dos outros E faz a área bater. Sem área (ou com mais de
    /// uma possível), só avisa.
    /// </summary>
    private static void ConsensusDigits(CoordinateTextResult c, double? area, double areaStep, List<ReadingFix> fixes, List<string> warnings)
    {
        var v = c.Vertices;
        if (v.Count < 4) return;
        bool geo = c.Kind == PerimeterCoordKind.Geographic;
        foreach (bool isA in new[] { true, false })
        {
            double Get(PerimeterVertex x) => isA ? x.A : x.B;
            var vals = v.Select(Get).OrderBy(x => x).ToList();
            double med = vals[vals.Count / 2];
            double spread = Math.Max(vals[(int)(vals.Count * 0.75)] - vals[(int)(vals.Count * 0.25)], geo ? 1e-5 : 1);
            double lim = spread * 20;
            foreach (var x in v)
            {
                double val = Get(x);
                if (Math.Abs(val - med) <= lim) continue;
                string label = geo ? (isA ? "Latitude" : "Longitude") : c.Kind == PerimeterCoordKind.Utm ? (isA ? "E" : "N") : (isA ? "X" : "Y");
                if (geo || area is not double ar || ar <= 0)
                {
                    warnings.Add($"{label} do vértice {x.Name} ({Num(val, geo)}) fica longe dos outros vértices: provável dígito lido errado – confira no documento.");
                    continue;
                }
                var others = v.Where(o => o != x).Select(Get).ToList();
                double lo = others.Min() - spread * 2, hi = others.Max() + spread * 2;
                int dec = DecimalsOf(2 * WrittenPrecision.RoundingOf(x, c.Kind));
                var options = DigitVariants(val, dec).Where(o => o >= lo && o <= hi).Distinct().Where(o =>
                {
                    double keep = Get(x);
                    Set(x, isA, o);
                    var pts = v.Select(q => new Vec2(q.A, q.B)).ToList();
                    double a = DrawingCheck.Shoelace(pts);
                    double tol = DrawingCheck.AreaTolerance(pts, v.Select(q => WrittenPrecision.RoundingOf(q, c.Kind)).ToList()) + areaStep / 2 + 2.5e-4 * 2 * ar + 0.01;
                    Set(x, isA, keep);
                    return Math.Abs(a - ar) <= tol;
                }).ToList();
                if (options.Count != 1)
                {
                    warnings.Add($"{label} do vértice {x.Name} ({Num(val, geo)}) fica longe dos outros vértices: provável dígito lido errado – confira no documento.");
                    continue;
                }
                Set(x, isA, options[0]);
                fixes.Add(new ReadingFix($"{label} do vértice {x.Name}", Num(val, geo), Num(options[0], geo),
                    "um dígito lido errado: o valor ficava longe dos outros vértices, e é a única troca que faz a área bater com a do documento"));
            }
        }
        if (fixes.Count > 0 && !geo && v.All(x => SmartReader.UtmE(x.A) && SmartReader.UtmN(x.B))) c.Kind = PerimeterCoordKind.Utm;
    }

    private static void Set(PerimeterVertex x, bool isA, double val) { if (isA) x.A = val; else x.B = val; }

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
