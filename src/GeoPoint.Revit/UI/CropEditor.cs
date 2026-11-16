using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Sheets;
using GeoPoint.Revit.Infrastructure;

namespace GeoPoint.Revit.UI;

/// <summary>
/// Enquadramento da imagem no espaço da prancha: o quadro tem a proporção do espaço; arraste a imagem para posicionar, use a roda
/// do mouse para aproximar e, com o contorno do lote ligado, Ctrl + arrastar move o contorno.
/// </summary>
public sealed class CropEditor : FrameworkElement
{
    private BitmapSource? _img;
    private double _aspect = 4.0 / 3;
    private SheetImageChoice _c = new();
    private List<List<Vec2>> _outline = new();
    private Point? _drag;
    private bool _dragOutline;
    private static readonly Pen FramePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(31, 95, 168)), 2));
    private static readonly Pen OutlinePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(230, 30, 30)), 2));
    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public CropEditor()
    {
        Cursor = Cursors.SizeAll;
        Focusable = true;
    }

    public event EventHandler? Changed;

    public SheetImageChoice Choice => _c;

    public void Set(BitmapSource? img, double aspect, SheetImageChoice choice, List<List<Vec2>> outline)
    {
        _img = img;
        _aspect = aspect > 0.2 && aspect < 5 ? aspect : 4.0 / 3;
        _c = choice;
        _outline = outline;
        InvalidateVisual();
    }

    public void Refresh() => InvalidateVisual();

    private Rect Frame()
    {
        double w = Math.Max(10, ActualWidth - 8), h = Math.Max(10, ActualHeight - 8);
        double fw = w, fh = w / _aspect;
        if (fh > h) { fh = h; fw = h * _aspect; }
        return new Rect((ActualWidth - fw) / 2, (ActualHeight - fh) / 2, fw, fh);
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Um erro no desenho da janela não pode chegar ao Revit: registra e mostra o quadro vazio.
        try { Draw(dc); }
        catch (Exception ex) { Logger.Error("Galeria: desenhar o enquadramento", ex); }
    }

    private void Draw(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(60, 64, 70)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        var f = Frame();
        if (_img is null || _img.PixelWidth <= 0 || _img.PixelHeight <= 0)
        {
            dc.DrawRectangle(Brushes.White, FramePen, f);
            var ft = new FormattedText("Escolha uma imagem da galeria", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 13, Brushes.Gray, 1.0);
            dc.DrawText(ft, new Point(f.X + (f.Width - ft.Width) / 2, f.Y + (f.Height - ft.Height) / 2));
            return;
        }
        var (x, y, w, h) = LocationImages.Crop(_img.PixelWidth, _img.PixelHeight, _aspect, _c);
        if (w <= 0 || h <= 0) return;
        double k = f.Width / w;
        dc.PushClip(new RectangleGeometry(f));
        dc.DrawImage(_img, new Rect(f.X - x * k, f.Y - y * k, _img.PixelWidth * k, _img.PixelHeight * k));
        if (_c.ShowOutline && _outline.Count > 0)
            foreach (var ring in LocationImages.OutlineInRect(_outline, new PaperRect(0, 0, f.Width, f.Height), _c))
            {
                if (ring.Count < 2) continue;
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new Point(f.X + ring[0].X, f.Y + f.Height - ring[0].Y), false, true);
                    g.PolyLineTo(ring.Skip(1).Select(p => new Point(f.X + p.X, f.Y + f.Height - p.Y)).ToList(), true, true);
                }
                dc.DrawGeometry(null, OutlinePen, geo);
            }
        dc.Pop();
        dc.DrawRectangle(null, FramePen, f);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_img is null) return;
        _drag = e.GetPosition(this);
        _dragOutline = _c.ShowOutline && (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        CaptureMouse();
        Focus();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag is not Point p0 || _img is null || _img.PixelWidth <= 0 || _img.PixelHeight <= 0) return;
        var p = e.GetPosition(this);
        var f = Frame();
        double dx = (p.X - p0.X) / f.Width, dy = (p.Y - p0.Y) / f.Height;
        if (_dragOutline)
        {
            _c.OutlineDX = Math.Clamp(_c.OutlineDX + dx, -0.5, 0.5);
            _c.OutlineDY = Math.Clamp(_c.OutlineDY + dy, -0.5, 0.5);
        }
        else
        {
            // dy em larguras do quadro → em larguras: a mesma conta do Pan (que usa a fração do recorte).
            (_c.CX, _c.CY) = LocationImages.Pan(_img.PixelWidth, _img.PixelHeight, _aspect, _c, dx, dy);
        }
        _drag = p;
        InvalidateVisual();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _drag = null;
        ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (_img is null) return;
        SetZoom(_c.Zoom * Math.Pow(1.15, e.Delta / 120.0));
        e.Handled = true;
    }

    public void SetZoom(double z)
    {
        if (_img is null || _img.PixelWidth <= 0 || _img.PixelHeight <= 0 || double.IsNaN(z)) return;
        _c.Zoom = Math.Clamp(z, 1, 6);
        (_c.CX, _c.CY) = LocationImages.Pan(_img.PixelWidth, _img.PixelHeight, _aspect, _c, 0, 0);
        InvalidateVisual();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
