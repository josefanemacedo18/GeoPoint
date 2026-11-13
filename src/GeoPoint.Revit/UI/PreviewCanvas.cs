using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using GeoPoint.Core.Geometry;

namespace GeoPoint.Revit.UI;

/// <summary>Elemento da prévia: polígono (com furos), polilinha ou texto, em coordenadas do desenho (m).</summary>
public sealed class PreviewItem
{
    public List<List<Vec2>> Rings { get; init; } = new();
    public List<Vec2>? Line { get; init; }
    public Color Fill { get; init; } = Colors.Transparent;
    public bool Hatch { get; init; }
    public Color Stroke { get; init; } = Color.FromRgb(40, 40, 40);
    public double StrokeWidth { get; init; } = 1.2;
    public bool Dashed { get; init; }
    public string? Label { get; init; }
    public Vec2? LabelAt { get; init; }
    /// <summary>Ângulo do texto no desenho (rad) e altura em metros do desenho (0 = texto fixo de tela, com fundo).</summary>
    public double LabelAngle { get; init; }
    public double LabelSizeM { get; init; }
    public Color LabelColor { get; init; } = Colors.Black;
    public bool LabelBoldFirst { get; init; }
    /// <summary>Ponto preenchido (raio em metros do desenho).</summary>
    public double DotRadiusM { get; init; }

    public static PreviewItem FromRegion(Region r, Color fill, string? label = null, bool hatch = true, Color? stroke = null, double width = 1.2) => new()
    {
        Rings = r.AllRings().Select(x => x.ToPolygon(0.05)).ToList(),
        Fill = fill, Hatch = hatch, Label = label, LabelAt = label is null ? null : r.InteriorPoint(),
        Stroke = stroke ?? Color.FromRgb(40, 40, 40), StrokeWidth = width,
    };
}

/// <summary>Prévia gráfica (norte para cima), redimensionável.</summary>
public sealed class PreviewCanvas : FrameworkElement
{
    private List<PreviewItem> _items = new();
    private double _rotation;

    public static readonly Color[] Palette =
    {
        Color.FromRgb(230, 80, 70), Color.FromRgb(60, 90, 220), Color.FromRgb(40, 160, 90), Color.FromRgb(230, 150, 30),
        Color.FromRgb(150, 70, 200), Color.FromRgb(20, 160, 180), Color.FromRgb(200, 60, 140), Color.FromRgb(120, 120, 40),
    };

    /// <summary>Mostra a seta do norte (desligar quando a prévia não está orientada).</summary>
    public bool ShowNorth { get; set; } = true;

    /// <summary>Define os itens. <paramref name="rotationRad"/> leva o desenho para leste/norte (norte para cima).</summary>
    public void SetItems(IEnumerable<PreviewItem> items, double rotationRad)
    {
        _items = items.ToList();
        _rotation = rotationRad;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(250, 251, 253)), new Pen(new SolidColorBrush(Color.FromRgb(200, 207, 216)), 1), new Rect(0, 0, w, h));
        var pts = _items.SelectMany(i => i.Rings.SelectMany(r => r).Concat(i.Line ?? new List<Vec2>()).Concat(i.LabelSizeM > 0 && i.LabelAt is { } la ? new[] { la } : Array.Empty<Vec2>())).Select(p => p.Rotate(_rotation)).ToList();
        if (pts.Count == 0 || w < 20 || h < 20)
        {
            var ft = Text("Sem prévia", 12, Brushes.Gray);
            dc.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
            return;
        }
        var bb = BBox.Of(pts);
        double margin = 18;
        double sx = (w - 2 * margin) / Math.Max(bb.Width, 1e-6), sy = (h - 2 * margin) / Math.Max(bb.Height, 1e-6);
        double s = Math.Min(sx, sy);
        double ox = margin + (w - 2 * margin - bb.Width * s) / 2, oy = margin + (h - 2 * margin - bb.Height * s) / 2;
        Point Map(Vec2 p)
        {
            var q = p.Rotate(_rotation);
            return new Point(ox + (q.X - bb.MinX) * s, h - (oy + (q.Y - bb.MinY) * s));
        }

        foreach (var it in _items)
        {
            if (it.Rings.Count > 0)
            {
                var g = new StreamGeometry { FillRule = FillRule.EvenOdd };
                using (var ctx = g.Open())
                    foreach (var r in it.Rings.Where(r => r.Count >= 3))
                    {
                        ctx.BeginFigure(Map(r[0]), true, true);
                        ctx.PolyLineTo(r.Skip(1).Select(Map).ToList(), true, false);
                    }
                g.Freeze();
                Brush? fill = null;
                if (it.Fill.A > 0) fill = it.Hatch ? HatchBrush(it.Fill) : new SolidColorBrush(Color.FromArgb(70, it.Fill.R, it.Fill.G, it.Fill.B));
                var pen = new Pen(new SolidColorBrush(it.Stroke), it.StrokeWidth);
                if (it.Dashed) pen.DashStyle = DashStyles.Dash;
                dc.DrawGeometry(fill, pen, g);
            }
            if (it.Line is { Count: >= 2 } line)
            {
                var pen = new Pen(new SolidColorBrush(it.Stroke), it.StrokeWidth);
                if (it.Dashed) pen.DashStyle = DashStyles.Dash;
                for (int i = 0; i < line.Count - 1; i++) dc.DrawLine(pen, Map(line[i]), Map(line[i + 1]));
            }
        }
        foreach (var it in _items.Where(i => i.DotRadiusM > 0 && i.LabelAt is not null))
            dc.DrawEllipse(Brushes.Black, null, Map(it.LabelAt!.Value), Math.Max(1.2, it.DotRadiusM * s), Math.Max(1.2, it.DotRadiusM * s));
        // Textos em escala do desenho (anotações): girados como no Revit.
        foreach (var it in _items.Where(i => i.Label is not null && i.LabelAt is not null && i.LabelSizeM > 0))
        {
            double px = it.LabelSizeM * s / 0.716;
            if (px < 2.5) continue;
            var lines = it.Label!.Split('\n');
            var p = Map(it.LabelAt!.Value);
            double ang = -(it.LabelAngle + _rotation) * 180 / Math.PI;
            double lh = it.LabelSizeM * 1.5 * s;
            dc.PushTransform(new RotateTransform(ang, p.X, p.Y));
            for (int i = 0; i < lines.Length; i++)
            {
                var tf = new Typeface(new FontFamily("Arial"), FontStyles.Normal, it.LabelBoldFirst && i == 0 ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
                var ft = new FormattedText(lines[i], CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, px, new SolidColorBrush(it.LabelColor), VisualTreeHelper.GetDpi(this).PixelsPerDip);
                double y = p.Y - lines.Length * lh / 2 + i * lh + (lh - ft.Height) / 2;
                dc.DrawText(ft, new Point(p.X - ft.Width / 2, y));
            }
            dc.Pop();
        }
        foreach (var it in _items.Where(i => i.Label is not null && i.LabelAt is not null && i.LabelSizeM <= 0))
        {
            var ft = Text(it.Label!, 11, Brushes.Black);
            var p = Map(it.LabelAt!.Value);
            var box = new Rect(p.X - ft.Width / 2 - 3, p.Y - ft.Height / 2 - 1, ft.Width + 6, ft.Height + 2);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)), null, box, 3, 3);
            dc.DrawText(ft, new Point(p.X - ft.Width / 2, p.Y - ft.Height / 2));
        }
        if (!ShowNorth) return;
        // Seta do norte.
        var n0 = new Point(w - 22, 40); var n1 = new Point(w - 22, 14);
        dc.DrawLine(new Pen(Brushes.Black, 1.5), n0, n1);
        var tri = new StreamGeometry();
        using (var c = tri.Open()) { c.BeginFigure(n1, true, true); c.LineTo(new Point(w - 27, 24), true, false); c.LineTo(new Point(w - 17, 24), true, false); }
        tri.Freeze();
        dc.DrawGeometry(Brushes.Black, null, tri);
        var nt = Text("N", 11, Brushes.Black);
        dc.DrawText(nt, new Point(w - 22 - nt.Width / 2, 40));
    }

    private FormattedText Text(string s, double size, Brush b) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, b, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static Brush HatchBrush(Color c)
    {
        var geo = new GeometryGroup();
        geo.Children.Add(new LineGeometry(new Point(0, 8), new Point(8, 0)));
        var drawing = new GeometryDrawing(null, new Pen(new SolidColorBrush(c), 1), geo);
        var bg = new GeometryDrawing(new SolidColorBrush(Color.FromArgb(35, c.R, c.G, c.B)), null, new RectangleGeometry(new Rect(0, 0, 8, 8)));
        var group = new DrawingGroup();
        group.Children.Add(bg);
        group.Children.Add(drawing);
        var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        brush.Freeze();
        return brush;
    }
}
