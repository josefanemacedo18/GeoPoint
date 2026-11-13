using System.Globalization;
using System.Text;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;

namespace GeoPoint.Core.Tests;

/// <summary>Gera um SVG da anotação planejada (para conferência visual). Só roda com GEOPOINT_SVG definido.</summary>
public class AnnotationPreviewTests
{
    [Fact]
    public void WriteSvg()
    {
        var path = Environment.GetEnvironmentVariable("GEOPOINT_SVG");
        if (string.IsNullOrEmpty(path)) return;
        var p1 = new Vec2(47.651, 22.458); var p2 = new Vec2(18.643, 0); var p3 = new Vec2(0, 24.473);
        var p4 = new Vec2(23.897, 25.553); var p5 = new Vec2(39.733, 33.831);
        var c1 = Vec2.Lerp(p1, p2, 0.45); var c2 = Vec2.Lerp(p2, p3, 0.52);
        var a = TestProjects.Poly("01", c2, p3, p4, p5, p1, c1);
        var b = TestProjects.Poly("02", c1, p2, c2);
        a.EdgeInfos.Add(new EdgeInfo { MX = (p3.X + p4.X) / 2, MY = (p3.Y + p4.Y) / 2, Confrontante = "Rua 1" });
        a.EdgeInfos.Add(new EdgeInfo { MX = (p4.X + p5.X) / 2, MY = (p4.Y + p5.Y) / 2, Confrontante = "Bemeval" });
        a.EdgeInfos.Add(new EdgeInfo { MX = (p5.X + p1.X) / 2, MY = (p5.Y + p1.Y) / 2, Confrontante = "Coité" });
        var mid = Vec2.Lerp(p1, c1, 0.5);
        a.EdgeInfos.Add(new EdgeInfo { MX = mid.X, MY = mid.Y, Confrontante = "Juca" });
        var mb = Vec2.Lerp(c1, p2, 0.5);
        b.EdgeInfos.Add(new EdgeInfo { MX = mb.X, MY = mb.Y, Confrontante = "Juca" });
        var mc = Vec2.Lerp(p2, c2, 0.5);
        b.EdgeInfos.Add(new EdgeInfo { MX = mc.X, MY = mc.Y, Confrontante = "Aaaa" });
        var ctx = TestProjects.Context(TestProjects.Project(a, b));
        var reps = new[] { ctx.Report(a), ctx.Report(b) };
        var o = new AnnotationPlanOptions { TextM = 0.5, OffsetM = 0.375, DotRadiusM = 0.15, PadM = 0.1 };
        var plan = AnnotationPlanner.Plan(reps, new ValueFormatter(ctx.Settings.Format), o);
        File.WriteAllText(path, Svg(reps, plan));
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Svg(IReadOnlyList<LotReport> reps, AnnotationPlan plan)
    {
        var pts = reps.SelectMany(r => r.Vertices.Select(v => v.Internal)).Concat(plan.Texts.SelectMany(t => Corners(t.Box))).ToList();
        var bb = BBox.Of(pts).Expand(2);
        double s = 18;
        double W = bb.Width * s, H = bb.Height * s;
        string X(Vec2 p) => F((p.X - bb.MinX) * s);
        string Y(Vec2 p) => F((bb.MaxY - p.Y) * s);
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{F(W)}' height='{F(H)}' viewBox='0 0 {F(W)} {F(H)}'><rect width='100%' height='100%' fill='white'/>");
        var colors = new[] { "#dbe7ff", "#dcf3e3" };
        int ci = 0;
        foreach (var r in reps)
        {
            var poly = string.Join(" ", r.Vertices.Select(v => $"{X(v.Internal)},{Y(v.Internal)}"));
            sb.Append($"<polygon points='{poly}' fill='{colors[ci++ % 2]}' stroke='black' stroke-width='1.2'/>");
        }
        foreach (var l in plan.Lines)
            sb.Append($"<line x1='{X(l.A)}' y1='{Y(l.A)}' x2='{X(l.B)}' y2='{Y(l.B)}' stroke='{(l.Kind == AnnoLineKind.Leader ? "gray" : "#c00000")}' stroke-width='0.8' {(l.Kind == AnnoLineKind.ConfrontanteTick ? "stroke-dasharray='4,3'" : "")}/>");
        foreach (var d in plan.Dots) sb.Append($"<circle cx='{X(d.At)}' cy='{Y(d.At)}' r='{F(d.Radius * s)}' fill='black'/>");
        foreach (var t in plan.Texts)
        {
            var c = Corners(t.Box).ToList();
            sb.Append($"<polygon points='{string.Join(" ", c.Select(p => $"{X(p)},{Y(p)}"))}' fill='none' stroke='#bbb' stroke-width='0.5'/>");
            double deg = -t.Rotation * 180 / Math.PI;
            string color = t.Kind is AnnoTextKind.Confrontante or AnnoTextKind.Measure ? "#c00000" : "black";
            var lines = t.Text.Split('\n');
            double size = (t.Kind == AnnoTextKind.LotLabel ? 0.625 : 0.5) * s;
            for (int i = 0; i < lines.Length; i++)
            {
                double dy = (i - (lines.Length - 1) / 2.0) * size * 1.5 + size * 0.35;
                sb.Append($"<text x='{X(t.At)}' y='{Y(t.At)}' font-family='Arial' font-size='{F(size)}' fill='{color}' text-anchor='middle' {(t.Kind == AnnoTextKind.LotLabel ? "font-weight='bold'" : "")} transform='rotate({F(deg)} {X(t.At)} {Y(t.At)}) translate(0 {F(dy)})'>{System.Security.SecurityElement.Escape(lines[i])}</text>");
            }
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static IEnumerable<Vec2> Corners(Obb b)
    {
        yield return b.C + b.U * b.Hw + b.V * b.Hh;
        yield return b.C - b.U * b.Hw + b.V * b.Hh;
        yield return b.C - b.U * b.Hw - b.V * b.Hh;
        yield return b.C + b.U * b.Hw - b.V * b.Hh;
    }
}
