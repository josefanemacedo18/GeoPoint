using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;

namespace GeoPoint.Core.Documents;

/// <summary>Trechos lidos de um texto (o melhor entre os leitores), com as correções e o modelo identificado.</summary>
public sealed class CourseReading
{
    public List<Course> Courses { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<ReadingFix> Fixes { get; } = new();
    /// <summary>Números com cara de lidos errado (casas fora do padrão): pedem confirmação.</summary>
    public List<string> Doubts { get; } = new();
    public string Model { get; set; } = "";
    public List<string> Evidence { get; } = new();
    /// <summary>Veio do leitor inteligente (as posições dos trechos no texto não são as do texto original).</summary>
    public bool FromSmart { get; set; }
}

/// <summary>
/// Escolha entre o leitor antigo (expressões fixas) e o leitor inteligente: fica a leitura que faz mais sentido – mais vértices ou
/// trechos, valores na faixa certa, coordenadas que conferem com as distâncias do texto, polígono que fecha.
/// </summary>
public static class ReadingChoice
{
    /// <summary>Nota de uma leitura de coordenadas (maior = melhor).</summary>
    public static double Score(CoordinateTextResult r)
    {
        var v = r.Vertices;
        if (v.Count == 0) return 0;
        double s = v.Count + (v.Count >= 3 ? 10 : 0);
        if (!Plausible(r)) s -= 15;
        s += 2 * MatchCount(r);
        return s;
    }

    /// <summary>Valores na faixa do sistema e vértices próximos uns dos outros (um lote: de 30 cm a 50 km).</summary>
    public static bool Plausible(CoordinateTextResult r)
    {
        var v = r.Vertices;
        bool range = r.Kind switch
        {
            PerimeterCoordKind.Utm => v.All(x => SmartReader.UtmE(x.A) && SmartReader.UtmN(x.B)),
            PerimeterCoordKind.Geographic => v.All(x => Math.Abs(x.A) <= 90 && Math.Abs(x.B) <= 180),
            _ => v.All(x => double.IsFinite(x.A) && double.IsFinite(x.B)),
        };
        if (!range) return false;
        if (v.Count < 2) return true;
        var pts = PlanePoints(r);
        var bb = BBox.Of(pts);
        double diag = Math.Sqrt(bb.Width * bb.Width + bb.Height * bb.Height);
        return diag >= 0.3 && diag <= 50_000;
    }

    /// <summary>Pontos em metros (UTM e locais como estão; geográficas pelos raios do elipsoide).</summary>
    public static List<Vec2> PlanePoints(CoordinateTextResult r)
    {
        var v = r.Vertices;
        if (r.Kind != PerimeterCoordKind.Geographic || v.Count == 0) return v.Select(x => new Vec2(x.A, x.B)).ToList();
        double lat0 = v.Average(x => x.A), lon0 = v.Average(x => x.B);
        const double a = 6378137.0, f = 1 / 298.257222101;
        double e2 = f * (2 - f), sn = Math.Sin(lat0 * Math.PI / 180), w = Math.Sqrt(1 - e2 * sn * sn);
        double ky = a * (1 - e2) / (w * w * w) * Math.PI / 180, kx = a / w * Math.Cos(lat0 * Math.PI / 180) * Math.PI / 180;
        return v.Select(x => new Vec2((x.B - lon0) * kx, (x.A - lat0) * ky)).ToList();
    }

    /// <summary>Quantos trechos têm a distância entre as coordenadas igual à distância escrita no texto.</summary>
    public static int MatchCount(CoordinateTextResult r)
    {
        var st = r.StatedCourses;
        var v = r.Vertices;
        if (st.Count == 0 || v.Count < 2) return 0;
        var pts = PlanePoints(r);
        int n = 0;
        for (int i = 0; i < st.Count && i < v.Count; i++)
        {
            if (double.IsNaN(st[i].Distance)) continue;
            double d = (pts[(i + 1) % v.Count] - pts[i]).Length;
            if (Math.Abs(d - st[i].Distance) <= Math.Max(0.05, st[i].Distance * 0.0015)) n++;
        }
        return n;
    }

    /// <summary>Nota de uma lista de trechos (completos e o polígono fechando).</summary>
    public static double CourseScore(IReadOnlyList<Course> cs)
    {
        var full = cs.Where(c => !double.IsNaN(c.AzimuthDeg) && !double.IsNaN(c.Distance) && c.Distance > 0).ToList();
        if (full.Count < 3) return full.Count;
        double per = full.Sum(c => c.Distance);
        var sum = full.Aggregate(new Vec2(0, 0), (s, c) => s + new Vec2(Math.Sin(c.AzimuthDeg * Math.PI / 180), Math.Cos(c.AzimuthDeg * Math.PI / 180)) * c.Distance);
        double rel = per > 0 ? sum.Length / per : 1;
        double s = full.Count - (cs.Count - full.Count) * 2;
        return s + (rel <= 1.0 / 3000 ? 10 : rel <= 1.0 / 300 ? 5 : rel <= 1.0 / 30 ? 2 : 0);
    }

    /// <summary>Os trechos de um texto: a melhor leitura entre a antiga e a inteligente (com a conferência pelo fechamento).</summary>
    public static CourseReading Courses(string text, ReadingModel model = ReadingModel.Auto)
    {
        var res = new CourseReading();
        var sm = SmartReader.Read(text ?? "", model);
        var smart = sm.Courses.Select(c => new Course
        {
            AzimuthDeg = c.AzimuthDeg, Distance = c.Distance, Radius = c.Radius, ArcToRight = c.ArcToRight, Source = c.Source,
            DistanceStep = c.DistanceStep, AzimuthStep = c.AzimuthStep,
        }).ToList();
        var warnings = new List<string>(sm.HasCoordinates ? new List<string>() : sm.Warnings);
        var fixes = new List<ReadingFix>(sm.HasCoordinates ? new List<ReadingFix>() : sm.Fixes);
        // Com coordenadas no texto, a conferência do leitor foi nas coordenadas: confere os trechos aqui.
        if (sm.HasCoordinates && smart.Count >= 3) ReadingCheck.Courses(smart, sm.Area, warnings, fixes, sm.AreaStep);
        var legacy = model == ReadingModel.Auto ? MemorialTextParser.Parse(text ?? "") : new List<Course>();
        bool useSmart = model != ReadingModel.Auto || CourseScore(smart) > CourseScore(legacy);
        if (useSmart)
        {
            res.Courses.AddRange(smart.Where(c => !double.IsNaN(c.AzimuthDeg) && !double.IsNaN(c.Distance)));
            res.Warnings.AddRange(warnings);
            res.Fixes.AddRange(fixes);
            res.Doubts.AddRange(sm.Doubts);
            res.FromSmart = true;
        }
        else res.Courses.AddRange(legacy);
        res.Model = sm.HasCourses || sm.HasCoordinates ? sm.Model : "";
        res.Evidence.AddRange(sm.Evidence);
        return res;
    }
}
