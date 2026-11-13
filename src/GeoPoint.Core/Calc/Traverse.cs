using System.Text.RegularExpressions;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;

namespace GeoPoint.Core.Calc;

/// <summary>Trecho de caminhamento (azimute verdadeiro e distância horizontal; arcos pela corda).</summary>
public sealed class Course
{
    public double AzimuthDeg { get; set; }
    public double Distance { get; set; }
    /// <summary>Raio (m) quando o trecho é um arco; a distância é então a corda e o azimute é o da corda.</summary>
    public double? Radius { get; set; }
    /// <summary>Arco à direita (sentido horário) no sentido do percurso.</summary>
    public bool ArcToRight { get; set; } = true;
    public string Source { get; set; } = "";
}

public sealed class TraverseResult
{
    /// <summary>Vértices no plano local (leste, norte), iniciando em (0,0).</summary>
    public List<Vec2> Points { get; } = new();
    public List<Vec2> AdjustedPoints { get; } = new();
    public List<Curve2> Curves { get; } = new();
    public Vec2 Misclosure { get; set; }
    public double MisclosureLength => Misclosure.Length;
    public double Perimeter { get; set; }
    /// <summary>Precisão relativa (1 : x). Infinito se fechou exatamente.</summary>
    public double RelativePrecision => MisclosureLength > 1e-9 ? Perimeter / MisclosureLength : double.PositiveInfinity;
    public double Area { get; set; }
}

public static class Traverse
{
    /// <summary>Trecho entre dois pontos: reta, ou arco com o raio dado (a–b é a corda; arco à direita = flecha à direita do percurso).</summary>
    public static Curve2 Segment(Vec2 a, Vec2 b, double? radius, bool arcToRight)
    {
        double chord = a.DistanceTo(b);
        if (radius is double r && r > 0 && r * 2 >= chord && chord > 1e-9)
        {
            double sagitta = r - Math.Sqrt(Math.Max(0, r * r - chord * chord / 4));
            var mid = Vec2.Lerp(a, b, 0.5);
            var dir = (b - a).Normalized();
            var off = arcToRight ? dir.PerpRight : dir.PerpLeft;
            return CurveFactory.FromThreePoints(a, mid + off * sagitta, b);
        }
        return new LineCurve(a, b);
    }

    public static TraverseResult Compute(IReadOnlyList<Course> courses, bool adjust)
    {
        var res = new TraverseResult();
        var p = Vec2.Zero;
        res.Points.Add(p);
        double perim = 0;
        var chordVecs = new List<Vec2>();
        foreach (var c in courses)
        {
            double a = c.AzimuthDeg * Math.PI / 180.0;
            var v = new Vec2(Math.Sin(a), Math.Cos(a)) * c.Distance;
            chordVecs.Add(v);
            var q = p + v;
            res.Points.Add(q);
            p = q;
        }
        res.Misclosure = p - res.Points[0];

        // Ajuste proporcional às distâncias (método de Bowditch).
        double total = courses.Sum(c => c.Distance);
        res.AdjustedPoints.Add(res.Points[0]);
        double cum = 0;
        for (int i = 0; i < courses.Count; i++)
        {
            cum += courses[i].Distance;
            var corr = total > 0 && adjust ? res.Misclosure * (cum / total) : Vec2.Zero;
            res.AdjustedPoints.Add(res.Points[i + 1] - corr);
        }

        var pts = adjust ? res.AdjustedPoints : res.Points;
        for (int i = 0; i < courses.Count; i++)
        {
            var a0 = pts[i]; var b0 = pts[i + 1];
            if (a0.DistanceTo(b0) < 1e-9) continue;
            var c = courses[i];
            var curve = Segment(a0, b0, c.Radius, c.ArcToRight);
            res.Curves.Add(curve);
            perim += curve.Length;
        }
        res.Perimeter = perim;
        if (res.Curves.Count >= 2)
        {
            double area = 0;
            var o = res.Curves[0].Start;
            foreach (var cv in res.Curves) area += cv.GreenIntegral(o);
            // Fecha (se não ajustado) com a reta de volta.
            if (!adjust && res.MisclosureLength > 1e-9) area += new LineCurve(pts[^1], pts[0]).GreenIntegral(o);
            res.Area = Math.Abs(area);
        }
        return res;
    }
}

/// <summary>Extrai azimutes/rumos e distâncias de um memorial descritivo em texto.</summary>
public static class MemorialTextParser
{
    private const string Ang = @"(?<d>\d{1,3})\s*[°º]\s*(?<m>\d{1,2})\s*['′’´`]\s*(?<s>\d{1,2}(?:[.,]\d+)?)?\s*(?:""|″|”|''|’’|′′|´´)?";
    private static readonly Regex AzRx = new(@"azimute(?:s)?(?:\s+(?:de|igual a|plano|verdadeiro|geod[eé]sico))*\s*:?\s*" + Ang, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RumoRx = new(@"rumo(?:\s+(?:de|igual a))?\s*:?\s*" + Ang + @"\s*(?<q>NE|SE|SO|SW|NO|NW)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RumoRx2 = new(@"rumo(?:\s+(?:de|igual a))?\s*:?\s*(?<q1>[NS])\s*" + Ang + @"\s*(?<q2>[EWLO])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DistRx = new(@"(?:dist[aâ]ncia|medindo|extens[aã]o|comprimento)(?:\s+(?:de|igual a|horizontal))*\s*:?\s*(?<v>\d{1,3}(?:\.\d{3})*(?:,\d+)?|\d+(?:[.,]\d+)?)\s*(?:metros|m(?![²2a-zA-Z]))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private sealed record Token(int Pos, bool IsAngle, double Value, string Text);

    public static List<Course> Parse(string text)
    {
        var tokens = new List<Token>();
        foreach (Match m in AzRx.Matches(text))
            if (TryAngle(m, out double a)) tokens.Add(new Token(m.Index, true, a, m.Value));
        foreach (Match m in RumoRx.Matches(text))
            if (TryAngle(m, out double a)) tokens.Add(new Token(m.Index, true, BearingToAzimuth(a, m.Groups["q"].Value), m.Value));
        foreach (Match m in RumoRx2.Matches(text))
            if (TryAngle(m, out double a)) tokens.Add(new Token(m.Index, true, BearingToAzimuth(a, m.Groups["q1"].Value + m.Groups["q2"].Value), m.Value));
        foreach (Match m in DistRx.Matches(text))
            if (AngleText.TryParseNumber(m.Groups["v"].Value, out double d) && d > 0) tokens.Add(new Token(m.Index, false, d, m.Value));
        tokens.Sort((x, y) => x.Pos.CompareTo(y.Pos));

        var res = new List<Course>();
        Token? pa = null, pd = null;
        foreach (var t in tokens)
        {
            if (t.IsAngle) pa = t; else pd = t;
            if (pa is not null && pd is not null)
            {
                res.Add(new Course { AzimuthDeg = pa.Value, Distance = pd.Value, Source = pa.Pos < pd.Pos ? pa.Text + " … " + pd.Text : pd.Text + " … " + pa.Text });
                pa = null; pd = null;
            }
        }
        return res;
    }

    private static bool TryAngle(Match m, out double deg)
    {
        deg = 0;
        if (!int.TryParse(m.Groups["d"].Value, out int d)) return false;
        if (!int.TryParse(m.Groups["m"].Value, out int mi)) return false;
        double s = 0;
        if (m.Groups["s"].Success && !AngleText.TryParseNumber(m.Groups["s"].Value, out s)) return false;
        if (mi >= 60 || s >= 60 || d > 360) return false;
        deg = d + mi / 60.0 + s / 3600.0;
        return true;
    }

    public static double BearingToAzimuth(double angle, string quadrant)
    {
        string q = quadrant.ToUpperInvariant().Replace('W', 'O').Replace('L', 'E');
        return q switch
        {
            "NE" => angle,
            "SE" => 180 - angle,
            "SO" => 180 + angle,
            "NO" => (360 - angle) % 360,
            _ => angle,
        };
    }
}
