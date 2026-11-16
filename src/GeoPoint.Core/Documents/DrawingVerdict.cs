using System.Globalization;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;

namespace GeoPoint.Core.Documents;

/// <summary>Resultado da conferência antes de desenhar.</summary>
public enum VerdictLevel
{
    /// <summary>Tudo confere dentro da precisão do documento: desenha sem perguntar.</summary>
    Conferido = 0,
    /// <summary>Nada diverge, mas nem tudo pôde ser conferido (ou houve correção automática): pergunta antes.</summary>
    Parcial = 1,
    /// <summary>O documento não traz como conferir (só coordenadas, só medidas dos lados): pergunta antes.</summary>
    SemConferencia = 2,
    /// <summary>Alguma conferência falhou: algum número foi lido ou escrito errado. Pergunta antes (o padrão é não desenhar).</summary>
    Divergente = 3,
}

/// <summary>Área e perímetro escritos no documento (para conferir o desenho), com o passo da última casa escrita.</summary>
public sealed class StatedFigures
{
    public double? Area { get; init; }
    public double AreaStep { get; init; } = 0.01;
    public double? Perimeter { get; init; }
    public double PerimeterStep { get; init; } = 0.01;
    public static StatedFigures None { get; } = new();
}

/// <summary>Conferência do desenho com o documento: os itens conferidos e o veredito.</summary>
public sealed class DrawingVerdict
{
    public VerdictLevel Level { get; set; } = VerdictLevel.SemConferencia;
    public List<CheckItem> Items { get; } = new();
    /// <summary>Desenhar pelos azimutes e distâncias (mais precisos que as coordenadas escritas), a partir do primeiro vértice.</summary>
    public bool DrawFromCourses { get; set; }
    /// <summary>Pode desenhar sem perguntar.</summary>
    public bool Verified => Level == VerdictLevel.Conferido;

    public string Headline => Level switch
    {
        VerdictLevel.Conferido => "CONFERIDO – o desenho confere com o documento dentro da precisão em que ele foi escrito.",
        VerdictLevel.Parcial => "CONFERIDO EM PARTE – nada diverge, mas há pontos para você confirmar antes de desenhar.",
        VerdictLevel.SemConferencia => "SEM CONFERÊNCIA – o documento não traz informação para conferir o desenho.",
        _ => "NÃO CONFERE – algum número foi lido ou está escrito errado.",
    };

    /// <summary>Pergunta antes de desenhar sem conferência completa (o que não conferiu e o que não pôde ser conferido).</summary>
    public string Question()
    {
        var lines = new List<string> { Headline, "" };
        foreach (var i in Items.Where(i => i.Status != CheckStatus.Ok)) lines.Add($"{i.Icon} {i.Name}: {i.Detail}");
        lines.Add("");
        lines.Add(Level == VerdictLevel.Divergente
            ? "Confira os números em vermelho com o documento e corrija na tabela. Desenhar assim mesmo?"
            : "Confira com o documento. Desenhar assim mesmo?");
        return string.Join("\n", lines);
    }
}

/// <summary>
/// Conferência antes de desenhar: o desenho só sai sem pergunta quando o documento se confirma sozinho dentro da precisão com que
/// foi escrito – trechos × coordenadas, fechamento do polígono, área e perímetro. A tolerância de cada item é o maior erro que o
/// arredondamento dos números escritos explica (<see cref="WrittenPrecision"/>); passou disso, algum número está errado.
/// </summary>
public static class DrawingCheck
{
    private static readonly CultureInfo Pt = new("pt-BR");
    private static string N(double v, int d = 2) => v.ToString("N" + d, Pt);
    private static string Ang(double deg) => Geodesy.AngleText.FormatDms(deg, 0, Geodesy.AngleKind.Plain);
    private static double Rel(PerimeterCoordKind kind) => kind == PerimeterCoordKind.Local ? 0 : 2.5e-4;

    // ------------------------------------------------------------------ desenho pelas coordenadas

    /// <param name="res">Resultado de <see cref="PerimeterText.Build"/> (com a conferência dos trechos do texto).</param>
    /// <param name="fixes">Números corrigidos automaticamente na leitura (precisam da sua confirmação).</param>
    /// <param name="doubts">Números lidos diferente em duas leituras do OCR.</param>
    public static DrawingVerdict ForCoordinates(PerimeterResult res, IReadOnlyList<PerimeterVertex> vs, PerimeterCoordKind kind,
        StatedFigures? fig = null, int fixes = 0, IReadOnlyList<string>? doubts = null)
    {
        fig ??= StatedFigures.None;
        var v = new DrawingVerdict();
        if (vs.Count < 2 || res.Points.Count != vs.Count) { v.Level = VerdictLevel.SemConferencia; return v; }
        var eps = vs.Select(x => WrittenPrecision.RoundingOf(x, kind)).ToList();
        double rel = Rel(kind);
        v.Items.Add(new CheckItem("Precisão das coordenadas", CheckStatus.Ok,
            $"escritas com arredondamento de até {WrittenPrecision.Describe(eps.Max())} em cada coordenada" +
            (vs.Any(x => x.Rounding is null) ? " (suposto: o texto não mostra)" : "")));

        // Trechos × coordenadas.
        var segs = res.Segments.Where(s => s.Matches is not null).ToList();
        var badSegs = segs.Where(s => s.Matches == false).ToList();
        if (segs.Count > 0)
        {
            if (badSegs.Count == 0)
            {
                double maxD = segs.Max(s => s.DistanceDiff ?? 0);
                var withAz = segs.Where(s => s.AzimuthDiffDeg is not null).ToList();
                v.Items.Add(new CheckItem("Trechos × coordenadas", CheckStatus.Ok,
                    $"os {segs.Count} trecho(s) escritos conferem com as coordenadas (maior diferença {N(maxD, 3)} m" +
                    (withAz.Count > 0 ? $" e {Ang(withAz.Max(s => s.AzimuthDiffDeg!.Value))}" : "") + ", dentro do arredondamento)"));
            }
            else
                v.Items.Add(new CheckItem("Trechos × coordenadas", CheckStatus.Falha, string.Join("; ", badSegs.Select(s =>
                {
                    string nm = $"{res.Names[s.From]} → {res.Names[s.To]}";
                    var st = s.Stated!;
                    string dPart = $"distância escrita {N(st.Distance)} m, pelas coordenadas {N(s.GridDistance is double g && Math.Abs(g - st.Distance) < Math.Abs(s.Distance - st.Distance) ? g : s.Distance, 3)} m " +
                                   $"(diferença {N(s.DistanceDiff ?? 0, 3)} m; o arredondamento explica até {N(s.DistanceTol ?? 0, 3)} m)";
                    string aPart = s.AzimuthDiffDeg is double ad && s.AzimuthTolDeg is double at && ad > at
                        ? $"azimute escrito {Ang(st.AzimuthDeg)}, diferença {Ang(ad)} (até {Ang(at)})" : "";
                    return $"{nm}: " + (aPart.Length > 0 ? aPart + "; " : "") + dPart;
                }))));
        }
        // Cada vértice precisa de duas medidas que o amarrem (azimute e distância de um trecho, ou as distâncias dos dois trechos).
        var cons = new int[vs.Count];
        foreach (var s in segs.Where(s => s.Matches == true))
        {
            int w = s.Stated is { } st && double.IsNaN(st.AzimuthDeg) ? 1 : 2;
            cons[s.From] += w;
            cons[s.To] += w;
        }
        bool allVertices = segs.Count > 0 && cons.All(c => c >= 2);
        if (segs.Count > 0 && badSegs.Count == 0 && !allVertices)
        {
            var loose = Enumerable.Range(0, vs.Count).Where(i => cons[i] < 2).Select(i => res.Names[i]).ToList();
            v.Items.Add(new CheckItem("Vértices conferidos", CheckStatus.Aviso,
                $"{loose.Count} vértice(s) sem trecho escrito para conferir: {string.Join(", ", loose)}"));
        }

        // Área: a do plano local e, em UTM, a do plano da projeção (o documento pode dar qualquer uma).
        bool areaOk = false, perimOk = false;
        if (fig.Area is double A && A > 0 && res.Closed && res.Points.Count >= 3)
        {
            double tol = AreaTolerance(res.Points, eps) + fig.AreaStep / 2 + 2 * rel * A + 0.01;
            var cands = new List<(string Name, double Value)> { ("plano local", res.Area) };
            if (kind == PerimeterCoordKind.Utm) cands.Add(("plano UTM", Shoelace(vs.Select(x => new Vec2(x.A, x.B)).ToList())));
            var best = cands.OrderBy(c => Math.Abs(c.Value - A)).First();
            areaOk = Math.Abs(best.Value - A) <= tol;
            v.Items.Add(new CheckItem("Área", areaOk ? CheckStatus.Ok : CheckStatus.Falha,
                $"documento {N(A)} m², pelas coordenadas {N(best.Value)} m² ({best.Name}; diferença {N(Math.Abs(best.Value - A))} m², o arredondamento explica até {N(tol)} m²)"));
        }
        if (fig.Perimeter is double P && P > 0 && res.Segments.Count > 0)
        {
            double tol = res.Segments.Sum(s => 2 * Math.Sqrt(2) * Math.Max(eps[s.From], eps[s.To])) + fig.PerimeterStep / 2 + rel * P + 0.002;
            var cands = new List<double> { res.Perimeter };
            if (res.Segments.All(s => s.GridDistance is not null)) cands.Add(res.Segments.Sum(s => s.GridDistance!.Value));
            double best = cands.OrderBy(c => Math.Abs(c - P)).First();
            perimOk = Math.Abs(best - P) <= tol;
            v.Items.Add(new CheckItem("Perímetro", perimOk ? CheckStatus.Ok : CheckStatus.Falha,
                $"documento {N(P)} m, pelas coordenadas {N(best, 3)} m (diferença {N(Math.Abs(best - P), 3)} m, o arredondamento explica até {N(tol, 3)} m)"));
        }
        AddFixesAndDoubts(v, fixes, doubts);

        bool anyFail = v.Items.Any(i => i.Status == CheckStatus.Falha);
        v.Level = anyFail ? VerdictLevel.Divergente
            : allVertices && fixes == 0 && (doubts?.Count ?? 0) == 0 ? VerdictLevel.Conferido
            : segs.Count > 0 || areaOk || perimOk ? VerdictLevel.Parcial
            : VerdictLevel.SemConferencia;
        if (v.Level == VerdictLevel.SemConferencia)
            v.Items.Add(new CheckItem("Conferência", CheckStatus.Aviso,
                "o documento só traz as coordenadas (sem azimutes e distâncias, área ou perímetro): não há como conferir se algum número foi lido errado"));
        else if (v.Level == VerdictLevel.Parcial && segs.Count == 0)
            v.Items.Add(new CheckItem("Conferência", CheckStatus.Aviso,
                "a área/o perímetro conferem, mas cada coordenada não pode ser conferida (o documento não traz azimutes e distâncias)"));
        return v;
    }

    /// <summary>Maior erro de área (m²) que o arredondamento das coordenadas explica.</summary>
    public static double AreaTolerance(IReadOnlyList<Vec2> pts, IReadOnlyList<double> eps)
    {
        int n = pts.Count;
        double t = 0;
        for (int i = 0; i < n; i++)
        {
            var prev = pts[(i - 1 + n) % n];
            var next = pts[(i + 1) % n];
            t += eps[i] * (Math.Abs(next.Y - prev.Y) + Math.Abs(next.X - prev.X)) / 2;
        }
        return t;
    }

    public static double Shoelace(IReadOnlyList<Vec2> p)
    {
        double a2 = 0;
        for (int i = 0; i < p.Count; i++)
        {
            var q = p[(i + 1) % p.Count];
            a2 += p[i].X * q.Y - q.X * p[i].Y;
        }
        return Math.Abs(a2) / 2;
    }

    // ------------------------------------------------------------------ desenho pelos azimutes e distâncias

    private static Vec2 Vec(Course c) => new(Math.Sin(c.AzimuthDeg * Math.PI / 180) * c.Distance, Math.Cos(c.AzimuthDeg * Math.PI / 180) * c.Distance);

    /// <summary>Vértices do caminhamento (o primeiro na origem), sem ajuste.</summary>
    public static List<Vec2> TraversePoints(IReadOnlyList<Course> cs)
    {
        var pts = new List<Vec2> { new(0, 0) };
        foreach (var c in cs.Take(Math.Max(0, cs.Count - 1))) pts.Add(pts[^1] + Vec(c));
        return pts;
    }

    /// <param name="fromSides">Trechos montados só das medidas dos lados (sem ângulos no documento).</param>
    /// <param name="derived">Valores ilegíveis calculados pelo fechamento (o fechamento já não confere nada).</param>
    public static DrawingVerdict ForCourses(IReadOnlyList<Course> cs, StatedFigures? fig = null, int fixes = 0,
        IReadOnlyList<string>? doubts = null, bool fromSides = false, int derived = 0)
    {
        fig ??= StatedFigures.None;
        var v = new DrawingVerdict();
        if (cs.Count < 3 || cs.Any(c => double.IsNaN(c.AzimuthDeg) || double.IsNaN(c.Distance)))
        {
            v.Level = VerdictLevel.SemConferencia;
            v.Items.Add(new CheckItem("Trechos", CheckStatus.Aviso, "menos de 3 trechos completos: não há polígono para conferir"));
            return v;
        }
        if (fromSides)
        {
            v.Items.Add(new CheckItem("Medidas dos lados", CheckStatus.Aviso,
                "o documento não traz ângulos nem coordenadas: o desenho supõe ângulos retos. Isso não é uma medida do documento – confira."));
        }
        double bound = cs.Sum(WrittenPrecision.CourseError) + 0.001;
        var sum = cs.Aggregate(new Vec2(0, 0), (s, c) => s + Vec(c));
        bool closes = sum.Length <= bound;
        if (!fromSides)
            v.Items.Add(new CheckItem("Fechamento", closes ? CheckStatus.Ok : CheckStatus.Falha,
                closes ? $"o polígono fecha (erro {N(sum.Length, 3)} m; o arredondamento dos trechos explica até {N(bound, 3)} m)"
                       : $"o polígono não fecha: erro de {N(sum.Length, 3)} m, e o arredondamento dos trechos explica só até {N(bound, 3)} m – algum azimute ou distância está errado"));
        if (derived > 0)
            v.Items.Add(new CheckItem("Fechamento", CheckStatus.Aviso, $"{derived} valor(es) ilegível(is) calculado(s) pelo fechamento: o fechamento não confere mais os outros trechos"));

        bool areaOk = false, perimOk = false;
        var pts = TraversePoints(cs);
        if (fig.Area is double A && A > 0)
        {
            double area = Shoelace(pts);
            double tol = CourseAreaTolerance(cs) + sum.Length * MaxExtent(pts) + fig.AreaStep / 2 + 0.01;
            areaOk = Math.Abs(area - A) <= tol;
            v.Items.Add(new CheckItem("Área", areaOk ? CheckStatus.Ok : CheckStatus.Falha,
                $"documento {N(A)} m², pelos trechos {N(area)} m² (diferença {N(Math.Abs(area - A))} m², o arredondamento explica até {N(tol)} m²)"));
        }
        if (fig.Perimeter is double P && P > 0)
        {
            double per = cs.Sum(c => c.Distance);
            double tol = cs.Sum(c => WrittenPrecision.DistanceStepOf(c) / 2) + fig.PerimeterStep / 2 + 0.002;
            perimOk = Math.Abs(per - P) <= tol;
            v.Items.Add(new CheckItem("Perímetro", perimOk ? CheckStatus.Ok : CheckStatus.Falha,
                $"documento {N(P)} m, soma dos trechos {N(per, 3)} m (diferença {N(Math.Abs(per - P), 3)} m, o arredondamento explica até {N(tol, 3)} m)"));
        }
        AddFixesAndDoubts(v, fixes, doubts);
        bool anyFail = v.Items.Any(i => i.Status == CheckStatus.Falha);
        if (fromSides) v.Level = anyFail ? VerdictLevel.Divergente : VerdictLevel.SemConferencia;
        else if (anyFail) v.Level = VerdictLevel.Divergente;
        else if (closes && (areaOk || perimOk) && fixes == 0 && derived == 0 && (doubts?.Count ?? 0) == 0) v.Level = VerdictLevel.Conferido;
        else
        {
            v.Level = VerdictLevel.Parcial;
            if (!areaOk && !perimOk)
                v.Items.Add(new CheckItem("Área e perímetro", CheckStatus.Aviso,
                    "o documento não traz área nem perímetro: o fechamento confere os trechos, mas um erro igual em dois lados opostos não seria percebido"));
        }
        return v;
    }

    /// <summary>Maior erro de área (m²) que o arredondamento dos trechos explica (sensibilidade da área a cada valor).</summary>
    public static double CourseAreaTolerance(IReadOnlyList<Course> cs)
    {
        double A(IReadOnlyList<Course> x) => Shoelace(TraversePoints(x));
        double t = 0;
        for (int i = 0; i < cs.Count; i++)
        {
            var c = cs[i];
            Course With(double az, double d) => new() { AzimuthDeg = az, Distance = d };
            var plusD = cs.ToList(); plusD[i] = With(c.AzimuthDeg, c.Distance + 0.001);
            var minusD = cs.ToList(); minusD[i] = With(c.AzimuthDeg, c.Distance - 0.001);
            double dA_dd = Math.Abs(A(plusD) - A(minusD)) / 0.002;
            var plusA = cs.ToList(); plusA[i] = With(c.AzimuthDeg + 1.0 / 3600, c.Distance);
            var minusA = cs.ToList(); minusA[i] = With(c.AzimuthDeg - 1.0 / 3600, c.Distance);
            double dA_da = Math.Abs(A(plusA) - A(minusA)) / (2.0 / 3600);
            t += dA_dd * WrittenPrecision.DistanceStepOf(c) / 2 + dA_da * WrittenPrecision.AzimuthStepOf(c) / 2;
        }
        return t;
    }

    private static double MaxExtent(IReadOnlyList<Vec2> pts)
    {
        var bb = BBox.Of(pts);
        return Math.Max(bb.Width, bb.Height);
    }

    private static void AddFixesAndDoubts(DrawingVerdict v, int fixes, IReadOnlyList<string>? doubts)
    {
        if (fixes > 0)
            v.Items.Add(new CheckItem("Correções automáticas", CheckStatus.Aviso,
                $"{fixes} número(s) lido(s) errado(s) foi(ram) corrigido(s) pela conferência (em amarelo): confirme no documento"));
        foreach (var d in doubts ?? Array.Empty<string>())
            v.Items.Add(new CheckItem("Dupla leitura", CheckStatus.Aviso, d));
    }

    // ------------------------------------------------------------------ qual desenhar: coordenadas ou trechos

    /// <summary>
    /// As coordenadas do documento são grossas (ex.: segundos com 2 casas, ±15 cm) e os azimutes e distâncias foram escritos com
    /// precisão bem maior (centímetro e segundo): o desenho fica mais fiel ao documento pelos trechos, começando no primeiro vértice.
    /// Precisa de um trecho escrito para cada lado.
    /// </summary>
    public static bool CoursesMorePrecise(IReadOnlyList<PerimeterVertex> vs, PerimeterCoordKind kind, IReadOnlyList<Course> stated)
    {
        if (vs.Count < 3 || stated.Count != vs.Count) return false;
        if (stated.Any(c => double.IsNaN(c.AzimuthDeg) || double.IsNaN(c.Distance) || c.Distance <= 0)) return false;
        double eps = vs.Max(x => WrittenPrecision.RoundingOf(x, kind));
        if (eps <= 0.02) return false;
        double course = stated.Max(WrittenPrecision.CourseError);
        return course < eps / 4;
    }

    /// <summary>
    /// Desenho pelos trechos quando eles são mais precisos que as coordenadas: o veredito junta a conferência das coordenadas (cada
    /// trecho cabe no arredondamento delas) com a dos trechos (fechamento, área, perímetro).
    /// </summary>
    public static DrawingVerdict ForCoursesWithCoordinates(PerimeterResult coordRes, IReadOnlyList<PerimeterVertex> vs, PerimeterCoordKind kind,
        IReadOnlyList<Course> stated, StatedFigures? fig = null, int fixes = 0, IReadOnlyList<string>? doubts = null)
    {
        var byCoords = ForCoordinates(coordRes, vs, kind, StatedFigures.None, 0, null);
        var byCourses = ForCourses(stated, fig, fixes, doubts);
        var v = new DrawingVerdict { DrawFromCourses = true };
        double eps = vs.Max(x => WrittenPrecision.RoundingOf(x, kind));
        v.Items.Add(new CheckItem("Desenho", CheckStatus.Ok,
            $"pelos azimutes e distâncias, a partir do vértice {vs[0].Name}: o documento escreveu as coordenadas com arredondamento de até " +
            $"{WrittenPrecision.Describe(eps)} e os trechos com até {WrittenPrecision.Describe(stated.Max(WrittenPrecision.CourseError))}"));
        v.Items.AddRange(byCoords.Items.Where(i => i.Name != "Conferência"));
        v.Items.AddRange(byCourses.Items);
        v.Level = (VerdictLevel)Math.Max((int)byCoords.Level, (int)byCourses.Level);
        // Coordenadas que conferem trecho a trecho e trechos que fecham com área/perímetro: conferido.
        if (byCoords.Level == VerdictLevel.Conferido && byCourses.Level == VerdictLevel.Conferido) v.Level = VerdictLevel.Conferido;
        return v;
    }

    /// <summary>
    /// Trechos do texto no plano do desenho (norte verdadeiro). Se os azimutes do documento são do plano UTM (convergência
    /// meridiana) e as distâncias também, passam para o norte verdadeiro e o plano local pela diferença que as próprias coordenadas
    /// mostram em cada trecho (azimute verdadeiro − de quadrícula; distância local ÷ de quadrícula).
    /// </summary>
    public static List<Course> ToTrueNorth(IReadOnlyList<Course> stated, PerimeterResult coordRes)
    {
        var segs = coordRes.Segments;
        bool grid = segs.Count(s => s.Basis == "plano UTM") > segs.Count(s => s.Basis == "verdadeiro");
        if (!grid || segs.Any(s => s.GridAzimuth is null || s.GridDistance is null))
            return stated.Select(c => new Course { AzimuthDeg = c.AzimuthDeg, Distance = c.Distance, Radius = c.Radius, ArcToRight = c.ArcToRight, Source = c.Source, AzimuthStep = c.AzimuthStep, DistanceStep = c.DistanceStep }).ToList();
        double Median(IEnumerable<double> x) { var l = x.OrderBy(y => y).ToList(); return l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2; }
        double conv = Median(segs.Select(s => ((s.Azimuth - s.GridAzimuth!.Value) % 360 + 540) % 360 - 180));
        double scale = Median(segs.Where(s => s.GridDistance > 0).Select(s => s.Distance / s.GridDistance!.Value));
        return stated.Select(c => new Course
        {
            AzimuthDeg = ((c.AzimuthDeg + conv) % 360 + 360) % 360, Distance = c.Distance * scale, Radius = c.Radius * scale, ArcToRight = c.ArcToRight,
            Source = c.Source, AzimuthStep = c.AzimuthStep, DistanceStep = c.DistanceStep,
        }).ToList();
    }
}
