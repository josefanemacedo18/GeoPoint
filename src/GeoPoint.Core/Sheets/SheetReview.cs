using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;

namespace GeoPoint.Core.Sheets;

/// <summary>
/// Revisão automática da prancha antes de gerar: grade de coordenadas sobre textos ou desenho, resolução da imagem de satélite no
/// espaço dela e convenções topográficas × o que aparece no desenho.
/// </summary>
public static class SheetReview
{
    /// <summary>Largura (px) do recorte da imagem no espaço (para a resolução); null = sem imagem.</summary>
    public static int? CropWidth(GeoPoint.Core.Model.ParcelProject p, GeoPoint.Core.Model.SheetImageChoice? c, SheetTemplate t)
    {
        if (c is null || LocationImages.Find(p, c.ImageId) is not { Width: > 0, Height: > 0 } img || SheetRenderer.CroquiRect(t) is not PaperRect r) return null;
        return LocationImages.Crop(img.Width, img.Height, r.Width / r.Height, c).W;
    }

    /// <summary>Resolução mínima da imagem impressa (pontos por polegada) para não ficar borrada.</summary>
    public const double MinDpi = 150;

    /// <param name="imageCropPx">Largura (px) do recorte da imagem de satélite colocado no espaço; null = sem imagem.</param>
    /// <param name="hatched">Os lotes da prancha são hachurados.</param>
    /// <param name="dots">O desenho tem os pontos dos vértices.</param>
    public static List<string> Check(SheetRenderInput input, IReadOnlyList<SheetPrim> sheet, IReadOnlyList<SheetPrim> map,
        int? imageCropPx, bool hatched, bool dots)
    {
        var res = new List<string>();
        // 1) Grade sobre textos (folha e desenho) ou sobre as formas do desenho.
        var grid = sheet.OfType<PLine>().Where(l => l.Style == PLineStyle.Grade).ToList();
        int hits = 0;
        foreach (var t in sheet.Concat(map).OfType<PText>().Where(t => t.SizeMm > 0 && !IsGridLabel(t, input)))
        {
            var box = TextBox(t);
            foreach (var g in grid)
                if (Crosses(g.A, g.B, box)) { hits++; break; }
        }
        if (input.GridShapes.Count > 0)
            foreach (var g in grid)
                for (int k = 1; k < 10; k++)
                {
                    var q = g.A + (g.B - g.A) * (k / 10.0);
                    if (input.GridShapes.Any(sh => sh.Count >= 3 && PolygonUtil.PointInPolygon(sh, q))) { hits++; break; }
                }
        if (hits > 0) res.Add($"A grade de coordenadas passa por cima de {hits} texto(s) ou parte(s) do desenho.");
        // 2) Resolução da imagem de satélite.
        if (imageCropPx is int px && px > 0 && SheetRenderer.CroquiRect(input.Template) is PaperRect r && r.Width > 0)
        {
            double dpi = px / (r.Width / 25.4);
            if (dpi < MinDpi)
                res.Add($"A imagem de satélite fica com {dpi:0} dpi no espaço (abaixo de {MinDpi:0}): use um print maior ou aproxime menos.");
        }
        // 3) Convenções × desenho.
        var legend = input.Content.Legenda.Select(l => l.Kind).ToHashSet();
        if (hatched && !legend.Contains(LegendKind.AreaImovel) && !legend.Contains(LegendKind.Remanescente))
            res.Add("O desenho tem hachuras, mas as convenções não mostram a área.");
        if (dots && !legend.Contains(LegendKind.Vertice)) res.Add("O desenho tem os pontos dos vértices, mas as convenções não mostram VÉRTICES.");
        return res;
    }

    private static bool IsGridLabel(PText t, SheetRenderInput input) => !input.Template.MapRect().Inset(1).Contains(t.At);

    /// <summary>Caixa (papel) de um texto, sem rotação (aproximação suficiente para a revisão).</summary>
    private static PaperRect TextBox(PText t)
    {
        var lines = t.Text.Split('\n');
        double w = lines.Max(l => AnnotationPlanner.TextWidth(l, t.SizeMm, t.Bold));
        double h = lines.Length * t.SizeMm * 1.5;
        if (Math.Abs(Math.Sin(t.Rotation)) > 0.7) (w, h) = (h, w);
        double x0 = t.H switch { PTextH.Center => t.At.X - w / 2, PTextH.Right => t.At.X - w, _ => t.At.X };
        double y1 = t.V switch { PTextV.Middle => t.At.Y + h / 2, PTextV.Bottom => t.At.Y + h, _ => t.At.Y };
        return new PaperRect(x0 + 0.3, y1 - h + 0.3, x0 + w - 0.3, y1 - 0.3);
    }

    private static bool Crosses(Vec2 a, Vec2 b, PaperRect r) => SheetRenderer.Clip(a, b, new[] { r }).Sum(s => s.Item1.DistanceTo(s.Item2)) < a.DistanceTo(b) - 0.2;
}
