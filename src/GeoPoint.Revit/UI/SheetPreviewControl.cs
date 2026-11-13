using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Sheets;

namespace GeoPoint.Revit.UI;

/// <summary>Prévia de uma folha (elementos do <see cref="SheetRenderer"/> + desenho de exemplo no mapa).</summary>
public sealed class SheetPreviewControl : FrameworkElement
{
    private List<SheetPrim> _prims = new();
    private double _w = 210, _h = 297;
    private List<(List<Vec2> Pts, Color Fill)> _shapes = new();
    private byte[] _grid = { 175, 175, 175 };

    public void Show(SheetTemplate t, List<SheetPrim> prims, List<(List<Vec2> Pts, Color Fill)>? shapes = null)
    {
        (_w, _h) = t.PaperSize();
        _prims = prims;
        _shapes = shapes ?? new();
        _grid = t.GridColor;
        InvalidateVisual();
    }

    /// <summary>Monta a prévia de um modelo com conteúdo de exemplo (triângulo do modelo da Folha 02).</summary>
    public void ShowSample(SheetTemplate t)
    {
        var map = t.MapRect();
        var table = t.TablesInsideMap ? SheetContent.SampleTable(t.Styles) : null;
        if (table is not null && !table.FitInto(map.Width * 0.8, map.Height * 0.5)) table = null;
        var fit = MapFit.Fit(map, 46, 42, t.DrawingMarginMm, table is null ? null : (table.Width, table.Height), SheetRenderer.NorthRect(t), t.TableCorner, t.ScaleList);
        double k = 1000.0 / fit.Scale;
        var tri = new[] { new Vec2(0, -20.8), new Vec2(-23, 0), new Vec2(23, 20.8) }.Select(p => fit.DrawingCenter + p * k).ToList();
        var prims = SheetRenderer.Render(new SheetRenderInput
        {
            Template = t, Content = SheetContent.Sample(), Scale = fit.Scale, Folha = "02", SheetNumber = "GP-02",
            CrsAt = q => (235140 + (q.X - map.X0) / k, 9299280 + (q.Y - map.Y0) / k), Table = table is null ? null : table, TableRect = fit.Table,
            Palette = new[] { ((byte)220, (byte)60, (byte)50), ((byte)40, (byte)90, (byte)210), ((byte)30, (byte)150, (byte)80), ((byte)225, (byte)140, (byte)20) },
        });
        Show(t, prims, new() { (tri, Color.FromRgb(248, 215, 176)) });
    }

    protected override void OnRender(DrawingContext dc)
    {
        double aw = ActualWidth, ah = ActualHeight;
        if (aw < 10 || ah < 10) return;
        double s = Math.Min((aw - 8) / _w, (ah - 8) / _h);
        double ox = (aw - _w * s) / 2, oy = (ah - _h * s) / 2;
        Point P(Vec2 v) => new(ox + v.X * s, oy + (_h - v.Y) * s);
        dc.DrawRectangle(Brushes.White, new Pen(Brushes.Gray, 0.5), new Rect(ox, oy, _w * s, _h * s));
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(_grid[0], _grid[1], _grid[2])), Math.Max(0.4, 0.13 * s));
        // Grade atrás do desenho.
        foreach (var l in _prims.OfType<PLine>().Where(l => l.Style == PLineStyle.Grade)) dc.DrawLine(gridPen, P(l.A), P(l.B));
        foreach (var (pts, fill) in _shapes)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(P(pts[0]), true, true);
                c.PolyLineTo(pts.Skip(1).Select(P).ToList(), true, true);
            }
            dc.DrawGeometry(new SolidColorBrush(fill), new Pen(Brushes.Black, Math.Max(0.6, 0.35 * s)), g);
        }
        foreach (var pr in _prims)
        {
            switch (pr)
            {
                case PFill f when f.Pts.Count >= 3:
                {
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(P(f.Pts[0]), true, true);
                        c.PolyLineTo(f.Pts.Skip(1).Select(P).ToList(), true, true);
                    }
                    var col = Color.FromRgb(f.R, f.G, f.B);
                    Brush b = f.Hatch ? new SolidColorBrush(Color.FromArgb(110, col.R, col.G, col.B)) : new SolidColorBrush(col);
                    dc.DrawGeometry(b, null, g);
                    break;
                }
                case PLine { Style: PLineStyle.Grade }:
                    break;
                case PLine l:
                {
                    var (w, c) = l.Style switch
                    {
                        PLineStyle.Moldura => (0.5, Colors.Black),
                        PLineStyle.Bloco => (0.3, Colors.Black),
                        PLineStyle.Limite => (0.35, Colors.Black),
                        PLineStyle.Vermelha => (0.25, Color.FromRgb(192, 0, 0)),
                        PLineStyle.Papel => (0.1, Colors.Silver),
                        PLineStyle.Tracejada or PLineStyle.MeioFio => (0.15, Colors.DimGray),
                        _ => (0.15, Colors.Black),
                    };
                    var pen = new Pen(new SolidColorBrush(c), Math.Max(0.5, w * s));
                    if (l.Style == PLineStyle.Tracejada) pen.DashStyle = DashStyles.Dash;
                    dc.DrawLine(pen, P(l.A), P(l.B));
                    break;
                }
                case PCircle ci:
                    dc.DrawEllipse(ci.Filled ? Brushes.Black : null, new Pen(Brushes.Black, 0.5), P(ci.C), ci.Radius * s, ci.Radius * s);
                    break;
                case PImage im:
                {
                    var a = P(new Vec2(im.Rect.X0, im.Rect.Y1));
                    var r = new Rect(a.X, a.Y, im.Rect.Width * s, im.Rect.Height * s);
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(90, 122, 74)), null, r);
                    break;
                }
                case PText t:
                {
                    var lines = t.Text.Split('\n');
                    double size = t.SizeMm * s / Core.Calc.AnnotationPlanner.ArialCapHeight;   // corpo da fonte
                    if (size < 1.2) break;
                    var tf = new Typeface(new FontFamily("Arial"), FontStyles.Normal, t.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
                    var brush = new SolidColorBrush(Color.FromRgb(t.R, t.G, t.B));
                    double lh = t.SizeMm * 1.55 * s;
                    double total = lines.Length * lh;
                    double top = t.V switch { PTextV.Top => 0, PTextV.Middle => total / 2, _ => total };
                    var at = P(t.At);
                    dc.PushTransform(new RotateTransform(-t.Rotation * 180 / Math.PI, at.X, at.Y));
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var ft = new FormattedText(lines[i], CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, size, brush, 1.0);
                        double x = t.H switch { PTextH.Center => at.X - ft.Width / 2, PTextH.Right => at.X - ft.Width, _ => at.X };
                        double y = at.Y - top + i * lh + (lh - ft.Height) / 2;
                        dc.DrawText(ft, new Point(x, y));
                    }
                    dc.Pop();
                    break;
                }
            }
        }
    }
}
