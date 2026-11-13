using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeoPoint.Core.Documents;

namespace GeoPoint.Revit.UI;

/// <summary>
/// Páginas do documento (certidão/memorial) uma embaixo da outra, na largura disponível, com as palavras usadas no desenho
/// destacadas em amarelo – para conferir o que foi lido lado a lado com a tabela.
/// </summary>
public sealed class DocumentView : FrameworkElement
{
    private List<BitmapSource?> _images = new();
    private List<DocPage> _pages = new();
    private List<DocWord> _highlights = new();
    private const double Gap = 10;
    private static readonly Brush HighlightFill = Freeze(new SolidColorBrush(Color.FromArgb(95, 255, 214, 0)));
    private static readonly Pen HighlightPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(230, 140, 0)), 1));
    private static readonly Pen PageBorder = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(200, 200, 200)), 1));

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public void SetDocument(IReadOnlyList<BitmapSource?> images, IReadOnlyList<DocPage> pages)
    {
        _images = images.ToList();
        _pages = pages.ToList();
        _highlights = new();
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SetHighlights(IEnumerable<DocWord> words)
    {
        _highlights = words.ToList();
        InvalidateVisual();
    }

    public bool HasPages => _images.Any(i => i is not null);

    /// <summary>Largura de "ajustar à largura" (a do painel); o zoom multiplica.</summary>
    public double FitWidth { get; set; } = 380;
    private double _zoom = 2;
    /// <summary>1 = largura do painel; 2 = o dobro (texto legível numa página A4), com rolagem horizontal.</summary>
    public double Zoom
    {
        get => _zoom;
        set { _zoom = Math.Clamp(value, 0.5, 6); InvalidateMeasure(); InvalidateVisual(); }
    }
    private double PageWidth => Math.Max(100, FitWidth * _zoom);

    private IEnumerable<(int Index, BitmapSource Img, Rect Rect)> Layout(double width)
    {
        double y = 0;
        for (int i = 0; i < _images.Count; i++)
        {
            var img = _images[i];
            if (img is null) continue;
            double h = width * img.PixelHeight / Math.Max(1, img.PixelWidth);
            yield return (i, img, new Rect(0, y, width, h));
            y += h + Gap;
        }
    }

    protected override Size MeasureOverride(Size available)
    {
        double w = HasPages ? PageWidth : (double.IsInfinity(available.Width) || available.Width <= 0 ? FitWidth : available.Width);
        var last = Layout(w).LastOrDefault();
        double h = last.Img is null ? 60 : last.Rect.Bottom;
        return new Size(w, h);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        if (w < 10) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, ActualHeight));
        if (!HasPages)
        {
            var ft = new FormattedText("Importe uma certidão (PDF ou imagem) para ver o documento aqui.", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.Gray, 1.0) { MaxTextWidth = Math.Max(50, w - 20) };
            dc.DrawText(ft, new Point(10, 10));
            return;
        }
        foreach (var (i, img, r) in Layout(w))
        {
            dc.DrawImage(img, r);
            dc.DrawRectangle(null, PageBorder, r);
            if (i >= _pages.Count) continue;
            var p = _pages[i];
            double k = r.Width / Math.Max(1e-6, p.Width);
            foreach (var hw in _highlights.Where(x => x.Page == i))
            {
                var rr = new Rect(r.X + hw.X0 * k - 1, r.Y + hw.Y0 * k - 1, (hw.X1 - hw.X0) * k + 2, (hw.Y1 - hw.Y0) * k + 2);
                dc.DrawRectangle(HighlightFill, HighlightPen, rr);
            }
        }
    }

    /// <summary>Posição (na lista de páginas) da primeira palavra destacada, para rolar até ela.</summary>
    public Point? FirstHighlight()
    {
        var first = _highlights.OrderBy(h => h.Page).ThenBy(h => h.Y0).FirstOrDefault();
        if (first is null) return null;
        foreach (var (i, _, r) in Layout(PageWidth))
            if (i == first.Page && i < _pages.Count)
            {
                double k = r.Width / Math.Max(1e-6, _pages[i].Width);
                return new Point(r.X + first.X0 * k, r.Y + first.Y0 * k);
            }
        return null;
    }
}
