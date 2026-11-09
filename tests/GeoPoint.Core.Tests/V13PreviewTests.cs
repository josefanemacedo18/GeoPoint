using System.Globalization;
using System.Text;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Urban;

namespace GeoPoint.Core.Tests;

/// <summary>Gera SVGs de conferência visual (GEOPOINT_OUT = pasta). Não faz nada sem a variável.</summary>
public class V13PreviewTests
{
    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    [Fact]
    public void WriteV13()
    {
        var dir = Environment.GetEnvironmentVariable("GEOPOINT_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        var p1 = new Vec2(47.651, 22.458); var p2 = new Vec2(18.643, 0); var p3 = new Vec2(0, 24.473);
        var p4 = new Vec2(23.897, 25.553); var p5 = new Vec2(39.733, 33.831);
        var c1 = Vec2.Lerp(p1, p2, 0.45); var c2 = Vec2.Lerp(p2, p3, 0.52);
        var a = TestProjects.Poly("01", c2, p3, p4, p5, p1, c1);
        var b = TestProjects.Poly("02", c1, p2, c2);
        a.Proprietario = "Maria das Dores"; b.Proprietario = "José Carlos Silva";
        var via = new ViaRecord { Nome = "Rua 1", Largura = 12 };
        void Info(LotRecord l, Vec2 x, Vec2 y, EdgeInfo i) { var m = Vec2.Lerp(x, y, 0.5); i.MX = m.X; i.MY = m.Y; l.EdgeInfos.Add(i); }
        Info(a, c2, p3, new EdgeInfo { Tipo = ConfrontanteTipo.Via, ViaId = via.Id, Testada = true });
        Info(b, p2, c2, new EdgeInfo { Tipo = ConfrontanteTipo.Via, ViaId = via.Id, Testada = true });
        Info(a, p3, p4, new EdgeInfo { Confrontante = "Bemeval" });
        Info(a, p4, p5, new EdgeInfo { Confrontante = "Bemeval" });
        Info(a, p5, p1, new EdgeInfo { Confrontante = "Coité" });
        Info(a, p1, c1, new EdgeInfo { Confrontante = "Juca" });
        Info(b, c1, p2, new EdgeInfo { Confrontante = "Juca" });
        var proj = TestProjects.Project(a, b);
        proj.Vias.Add(via);
        var ctx = TestProjects.Context(proj);
        var local = TestProjects.Context(proj, st => st.Naming.Numbering = VertexNumbering.Lote);
        var fmt = new ValueFormatter(ctx.Settings.Format);

        // Meio-fio desenhado pelo usuário, 3 m fora da testada p3–p2 (lado sudoeste).
        var dir23 = (p2 - p3).Normalized();
        var outN = dir23.PerpLeft; if (outN.Dot(p3 - Vec2.Lerp(p1, p4, 0.5)) < 0) outN = -outN;
        var curb = new List<Vec2> { p3 + outN * 3 - dir23 * 14, p2 + outN * 3 + dir23 * 14 };
        via.MeioFio.Add(curb.Select(v => new PointDto { X = v.X, Y = v.Y }).ToList());
        var fronts = new List<StreetFrontage>();
        double calc = StreetSchematic.MeasureCalcada(new Curve2[] { new LineCurve(c2, p3), new LineCurve(p2, c2) }, new[] { curb });
        via.Calcada = calc;
        var street = StreetSchematic.Build(new[] { via }, Array.Empty<CruzamentoRecord>(), fronts, new[] { a.ToRegion(), b.ToRegion() });

        var o = new AnnotationPlanOptions { TextM = 0.5, OffsetM = 0.375, DotRadiusM = 0.15, PadM = 0.1 };
        // 1) Prancha geral
        var og = new AnnotationPlanOptions { TextM = 0.5, OffsetM = 0.375, DotRadiusM = 0.15, PadM = 0.1, Vertices = false, EdgeAngle = false, Dots = false,
            OwnerOf = l => l.Proprietario, Obstacles = { Obb.Of(c2, 0, 1.2, 1.2) } };
        var repsG = new[] { ctx.Report(a), ctx.Report(b) };
        var planG = AnnotationPlanner.Plan(repsG, fmt, og);
        var cps = new[] { ("KML-1", p3 + new Vec2(0.35, -0.2)), ("KML-2", p5 + new Vec2(-0.3, 0.25)), ("KML-3", p1 + new Vec2(0.2, 0.3)) };
        File.WriteAllText(Path.Combine(dir, "01-prancha-geral.svg"), Svg(repsG, planG, street, new[] { c2 }, cps, true, "SITUAÇÃO PROPOSTA – geral (sem vértices e sem azimutes)"));

        // 2) Prancha individual: Lote 01, numeração própria, todos os azimutes
        var repI = new[] { local.Report(a) };
        var planI = AnnotationPlanner.Plan(repI, fmt, o);
        var tbl = CoordTable(local, repI[0], fmt);
        File.WriteAllText(Path.Combine(dir, "02-prancha-individual-lote01.svg"), Svg(repI, planI, street, new[] { repI[0].Vertices[0].Internal }, Array.Empty<(string, Vec2)>(), false,
            "SITUAÇÃO PROPOSTA – Lote 01 (numeração própria P-01…)", tbl));

        // 3) Prancha individual: Lote 02 (também P-01…)
        var repI2 = new[] { local.Report(b) };
        var planI2 = AnnotationPlanner.Plan(repI2, fmt, o);
        File.WriteAllText(Path.Combine(dir, "03-prancha-individual-lote02.svg"), Svg(repI2, planI2, street, new[] { repI2[0].Vertices[0].Internal }, Array.Empty<(string, Vec2)>(), false,
            "SITUAÇÃO PROPOSTA – Lote 02 (também começa em P-01)", CoordTable(local, repI2[0], fmt)));
    }

    private static List<string[]> CoordTable(ProjectContext ctx, LotReport rep, ValueFormatter fmt)
    {
        var def = CrsDef.Utm();
        var (ta, tb) = fmt.ColumnTitles(def);
        var rows = new List<string[]> { new[] { "VÉRTICE", ta, tb, "AZIMUTE", "DIST. (m)", "CONFRONTANTE" } };
        for (int i = 0; i < rep.Vertices.Count; i++)
        {
            var v = rep.Vertices[i]; var s = rep.Segments[i];
            var c = ctx.Coords.ToCrs(v.Internal, def);
            var (x, y) = c is { } cc ? fmt.Columns(cc, def) : ("–", "–");
            rows.Add(new[] { v.Name, x, y, fmt.Angle(s.AzimuthTrue), fmt.DistanceValue(s.LengthHorizontal), $"{s.From.Name}–{s.To.Name}: {s.Confrontante}" });
        }
        return rows;
    }

    private static string Svg(IReadOnlyList<LotReport> reps, AnnotationPlan plan, StreetDrawing street, Vec2[] refPts,
        (string n, Vec2 p)[] control, bool hatch, string title, List<string[]>? table = null)
    {
        var pts = reps.SelectMany(r => r.Vertices.Select(v => v.Internal))
            .Concat(plan.Texts.SelectMany(t => Corners(t.Box)))
            .Concat(street.Curbs.SelectMany(c => c)).Concat(street.Alignments.SelectMany(c => c)).ToList();
        var bb = BBox.Of(pts).Expand(3);
        double s = 17;
        double W = Math.Max(bb.Width * s, 760), H = bb.Height * s + 70 + (table is null ? 0 : (table.Count + 1) * 20 + 20);
        string X(Vec2 p) => F((p.X - bb.MinX) * s);
        string Y(Vec2 p) => F(60 + (bb.MaxY - p.Y) * s);
        var sb = new StringBuilder($"<svg xmlns='http://www.w3.org/2000/svg' width='{F(W)}' height='{F(H)}'><rect width='100%' height='100%' fill='white'/>");
        sb.Append($"<text x='10' y='24' font-family='Arial' font-size='18' font-weight='bold'>{System.Security.SecurityElement.Escape(title)}</text>");
        sb.Append("<defs>");
        string[] cols = { "#dc3c32", "#2a5ad2" };
        for (int i = 0; i < cols.Length; i++)
            sb.Append($"<pattern id='h{i}' width='8' height='8' patternUnits='userSpaceOnUse' patternTransform='rotate(45)'><rect width='8' height='8' fill='{cols[i]}' fill-opacity='0.08'/><line x1='0' y1='0' x2='0' y2='8' stroke='{cols[i]}' stroke-width='1'/></pattern>");
        sb.Append("</defs>");
        // Rua (meio-fio e alinhamento).
        foreach (var c in street.Curbs) sb.Append($"<polyline points='{string.Join(" ", c.Select(p => $"{X(p)},{Y(p)}"))}' fill='none' stroke='#333' stroke-width='2'/>");
        foreach (var c in street.Alignments) sb.Append($"<polyline points='{string.Join(" ", c.Select(p => $"{X(p)},{Y(p)}"))}' fill='none' stroke='#888' stroke-width='1'/>");
        foreach (var l in street.Labels)
            sb.Append($"<text x='{X(l.Position)}' y='{Y(l.Position)}' font-family='Arial' font-weight='bold' font-size='14' text-anchor='middle' transform='rotate({F(-l.AngleRad * 180 / Math.PI)} {X(l.Position)} {Y(l.Position)})'>{System.Security.SecurityElement.Escape(l.Text.ToUpperInvariant())}</text>");
        int ci = 0;
        foreach (var r in reps)
        {
            var poly = string.Join(" ", r.Vertices.Select(v => $"{X(v.Internal)},{Y(v.Internal)}"));
            string fill = hatch ? $"url(#h{ci % 2})" : "none";
            sb.Append($"<polygon points='{poly}' fill='{fill}' stroke='black' stroke-width='1.6'/>");
            ci++;
        }
        foreach (var l in plan.Lines)
            sb.Append($"<line x1='{X(l.A)}' y1='{Y(l.A)}' x2='{X(l.B)}' y2='{Y(l.B)}' stroke='{(l.Kind == AnnoLineKind.Leader ? "gray" : "#c00000")}' stroke-width='0.8' {(l.Kind == AnnoLineKind.ConfrontanteTick ? "stroke-dasharray='4,3'" : "")}/>");
        foreach (var d in plan.Dots) sb.Append($"<circle cx='{X(d.At)}' cy='{Y(d.At)}' r='{F(d.Radius * s)}' fill='black'/>");
        // Pontos de controle (KML): cruz laranja + nome.
        foreach (var (n, p) in control)
        {
            double arm = 0.5 * s;
            sb.Append($"<line x1='{F(double.Parse(X(p)) - arm)}' y1='{Y(p)}' x2='{F(double.Parse(X(p)) + arm)}' y2='{Y(p)}' stroke='#e66e00' stroke-width='2'/><line x1='{X(p)}' y1='{F(double.Parse(Y(p)) - arm)}' x2='{X(p)}' y2='{F(double.Parse(Y(p)) + arm)}' stroke='#e66e00' stroke-width='2'/>");
            sb.Append($"<text x='{F(double.Parse(X(p)) + arm)}' y='{F(double.Parse(Y(p)) - arm)}' font-family='Arial' font-weight='bold' font-size='11' fill='#e66e00'>{n}</text>");
        }
        // Marcador do ponto de referência (triângulo vermelho).
        foreach (var rp in refPts)
        {
            double r0 = 1.2 * s; double cx = double.Parse(X(rp)), cy = double.Parse(Y(rp));
            sb.Append($"<polygon points='{F(cx)},{F(cy - r0)} {F(cx + r0 * 0.87)},{F(cy + r0 * 0.5)} {F(cx - r0 * 0.87)},{F(cy + r0 * 0.5)}' fill='none' stroke='#c00000' stroke-width='2'/>");
            if (hatch) sb.Append($"<text x='{F(cx + r0 + 3)}' y='{F(cy)}' font-family='Arial' font-weight='bold' font-size='12' fill='#c00000'>PR</text>");
        }
        foreach (var t in plan.Texts)
        {
            double deg = -t.Rotation * 180 / Math.PI;
            string color = t.Kind is AnnoTextKind.Confrontante or AnnoTextKind.Measure ? "#c00000" : "black";
            var lines = t.Text.Split('\n');
            double size = (t.Kind == AnnoTextKind.LotLabel ? 0.62 : 0.5) * s;
            var c = Corners(t.Box).ToList();
            sb.Append($"<polygon points='{string.Join(" ", c.Select(p => $"{X(p)},{Y(p)}"))}' fill='white' fill-opacity='0.85' stroke='none'/>");
            for (int i = 0; i < lines.Length; i++)
            {
                double dy = (i - (lines.Length - 1) / 2.0) * size * 1.5 + size * 0.35;
                sb.Append($"<text x='{X(t.At)}' y='{Y(t.At)}' font-family='Arial' font-size='{F(size)}' fill='{color}' text-anchor='middle' {(t.Kind == AnnoTextKind.LotLabel ? "font-weight='bold'" : "")} transform='rotate({F(deg)} {X(t.At)} {Y(t.At)}) translate(0 {F(dy)})'>{System.Security.SecurityElement.Escape(lines[i])}</text>");
            }
        }
        if (table is not null)
        {
            double y0 = 60 + bb.Height * s + 24;
            double[] cw = { 0.09, 0.2, 0.2, 0.16, 0.1, 0.25 };
            double x = 10; double tw = W - 20;
            sb.Append($"<text x='10' y='{F(y0 - 6)}' font-family='Arial' font-weight='bold' font-size='13'>QUADRO DE COORDENADAS</text>");
            for (int r = 0; r < table.Count; r++)
            {
                double xx = x;
                for (int c = 0; c < 6; c++)
                {
                    double w = tw * cw[c];
                    sb.Append($"<rect x='{F(xx)}' y='{F(y0 + r * 20)}' width='{F(w)}' height='20' fill='{(r == 0 ? "#eee" : "white")}' stroke='black' stroke-width='0.6'/><text x='{F(xx + 4)}' y='{F(y0 + r * 20 + 14)}' font-family='Arial' font-size='11' {(r == 0 ? "font-weight='bold'" : "")}>{System.Security.SecurityElement.Escape(table[r][c])}</text>");
                    xx += w;
                }
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
