using System.Globalization;
using System.Text;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Operations;

namespace GeoPoint.Core.Tests;

/// <summary>Catálogo de formatos em SVG (GEOPOINT_OUT = pasta). Não faz nada sem a variável.</summary>
public class LotShapesPreviewTests
{
    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    [Fact]
    public void WriteCatalog()
    {
        var dir = Environment.GetEnvironmentVariable("GEOPOINT_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        const int cols = 4, cw = 330, ch = 360;
        var shapes = LotShapes.All.ToList();
        int rows = (shapes.Count + cols - 1) / cols;
        var sb = new StringBuilder($"<svg xmlns='http://www.w3.org/2000/svg' width='{cols * cw}' height='{rows * ch + 40}'><rect width='100%' height='100%' fill='white'/>");
        sb.Append("<text x='12' y='28' font-family='Arial' font-size='20' font-weight='bold'>GeoPoint – Gerar lote: formatos (valores padrão da janela)</text>");
        for (int i = 0; i < shapes.Count; i++)
        {
            var d = shapes[i];
            var r = LotShapes.Build(d.Kind, new Dictionary<string, double>());
            double ox = (i % cols) * cw, oy = 40 + (i / cols) * ch;
            sb.Append($"<rect x='{ox + 6}' y='{oy + 6}' width='{cw - 12}' height='{ch - 12}' fill='#fafbfd' stroke='#ccd' rx='6'/>");
            sb.Append($"<text x='{ox + 16}' y='{oy + 28}' font-family='Arial' font-size='14' font-weight='bold'>{System.Security.SecurityElement.Escape(d.Nome)}</text>");
            var pts = r.Edges.SelectMany(e => e.Densify(0.05)).ToList();
            var bb = BBox.Of(pts);
            double s = Math.Min((cw - 120) / bb.Width, (ch - 150) / bb.Height);
            double bx = ox + (cw - bb.Width * s) / 2, by = oy + 60 + (ch - 150 - bb.Height * s) / 2;
            string X(Vec2 p) => F(bx + (p.X - bb.MinX) * s);
            string Y(Vec2 p) => F(by + (bb.MaxY - p.Y) * s);
            var poly = string.Join(" ", r.Edges.SelectMany(e => e.Densify(0.02).Take(e.Densify(0.02).Count - 1)).Select(p => $"{X(p)},{Y(p)}"));
            sb.Append($"<polygon points='{poly}' fill='#dbe7ff' stroke='#1f5fa8' stroke-width='2'/>");
            var c = new Ring(r.Edges).Bounds.Center;
            for (int k = 0; k < r.Edges.Count; k++)
            {
                var e = r.Edges[k];
                var m = e.PointAt(0.5);
                var n = e.TangentAt(0.5).PerpRight;
                if (n.Dot(m - c) < 0) n = -n;
                var lp = m + n * (14 / s);
                sb.Append($"<text x='{X(lp)}' y='{Y(lp)}' font-family='Arial' font-size='11' text-anchor='middle' fill='#333'>{F(e.Length).Replace('.', ',')} m</text>");
            }
            sb.Append($"<line x1='{F(bx - 10)}' y1='{F(by + bb.Height * s + 26)}' x2='{F(bx + bb.Width * s + 10)}' y2='{F(by + bb.Height * s + 26)}' stroke='#999' stroke-dasharray='5,4'/>");
            sb.Append($"<text x='{F(ox + cw / 2.0)}' y='{F(by + bb.Height * s + 42)}' font-family='Arial' font-size='10' fill='#999' text-anchor='middle'>RUA (frente)</text>");
            sb.Append($"<text x='{ox + 16}' y='{oy + ch - 22}' font-family='Arial' font-size='12'>Área: {F(r.Area).Replace('.', ',')} m²</text>");
        }
        sb.Append("</svg>");
        File.WriteAllText(Path.Combine(dir, "formatos-de-lote.svg"), sb.ToString());
    }
}
