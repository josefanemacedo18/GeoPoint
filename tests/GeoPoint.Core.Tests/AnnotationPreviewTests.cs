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

public class SheetPreviewTests
{
    [Theory]
    [InlineData(GeoPoint.Core.Sheets.SheetOrientation.Retrato, "GEOPOINT_SHEET_SVG")]
    [InlineData(GeoPoint.Core.Sheets.SheetOrientation.Paisagem, "GEOPOINT_SHEET2_SVG")]
    public void WriteSheetSvg(GeoPoint.Core.Sheets.SheetOrientation o, string env)
    {
        var path = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(path)) return;
        var l = GeoPoint.Core.Sheets.SheetLayout.A4(o);
        double k = 4; // px por mm
        string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        string X(double x) => F(x * k);
        string Y(double y) => F((l.Height - y) * k);
        var sb = new StringBuilder($"<svg xmlns='http://www.w3.org/2000/svg' width='{F(l.Width * k)}' height='{F(l.Height * k)}'><rect width='100%' height='100%' fill='white' stroke='#aaa'/>");
        void Rect(GeoPoint.Core.Sheets.PaperRect r, double w) => sb.Append($"<rect x='{X(r.X0)}' y='{Y(r.Y1)}' width='{F(r.Width * k)}' height='{F(r.Height * k)}' fill='none' stroke='black' stroke-width='{F(w)}'/>");
        Rect(l.Frame, 2); Rect(l.TitleBlock, 2);
        foreach (var (a, b) in l.Lines) sb.Append($"<line x1='{X(a.X)}' y1='{Y(a.Y)}' x2='{X(b.X)}' y2='{Y(b.Y)}' stroke='black' stroke-width='0.7'/>");
        // Texto: caixa com topo no ponto de inserção (regra do Revit); glifo desenhado na linha de base.
        void T(double x, double top, string s, double size, bool bold = false, bool center = false)
        {
            double baseY = top - 0.4 - size * 1.5 * 0.78;
            sb.Append($"<text x='{X(x)}' y='{Y(baseY)}' font-family='Arial' font-size='{F(size * 1.38 * k)}' {(bold ? "font-weight='bold'" : "")} {(center ? "text-anchor='middle'" : "")}>{System.Security.SecurityElement.Escape(s)}</text>");
        }
        var vals = new Dictionary<string, string>
        {
            ["proprietario"] = "Fulano de Tal e Beltrana de Tal", ["imovel"] = "Lote 01 – Quadra A, Lote 02 – Quadra A – Rua 1, s/n – Centro",
            ["municipio"] = "Porto Franco/MA", ["matricula"] = "12.345 / Insc. 001.002", ["area"] = "Total: 736,92 m²", ["perimetro"] = "ver quadro de áreas",
            ["coordenada"] = "P-01: E 230.123,456 m  N 9.298.765,432 m – UTM 23M SIRGAS 2000", ["escala"] = "1:250", ["data"] = "09/11/2026",
            ["folha"] = "01/04", ["prancha"] = "GP-01",
        };
        foreach (var c in l.Cells)
        {
            var r = c.Rect;
            if (c.Key == "titulo") { T(r.X0 + 2, r.Y1 - 1.6, "SITUAÇÃO PROPOSTA", c.ValueMm, true); T(r.X0 + 2, r.Y0 + 1.2 + 1.9 * 1.5 + 0.8, "DESMEMBRAMENTO DO LOTE 01 – QUADRA A", 1.9); continue; }
            if (c.Label.Length > 0) T(r.X0 + 1.2, r.Y1 - 0.4, c.Label, 1.4);
            if (!vals.TryGetValue(c.Key, out var v)) continue;
            double size = c.ValueMm;
            while (size > 1.5 && AnnotationPlanner.TextWidth(v, size, c.Bold) > r.Width - 2.4) size = Math.Round(size - 0.1, 2);
            double boxH = size * 1.5 + 0.8;
            if (c.Centered) T(r.Center.X, r.Y0 + 0.6 + boxH, v, size, c.Bold, true);
            else T(r.X0 + 1.2, r.Y0 + 0.6 + boxH, v, size, c.Bold);
        }
        sb.Append("</svg>");
        File.WriteAllText(path, sb.ToString());
    }
}
