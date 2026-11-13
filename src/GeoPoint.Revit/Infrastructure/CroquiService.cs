using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Imagery;
using GeoPoint.Core.Model;
using GeoPoint.Core.Sheets;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>
/// Croqui de localização automático: baixa a imagem de satélite da região (Esri, Google ou servidor próprio),
/// desenha o lote por cima e grava um PNG para o bloco "Croqui" da prancha.
/// </summary>
public static class CroquiService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("GeoPoint-Revit/1.5 (+croqui de localizacao)");
        return c;
    }

    private static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GeoPoint");

    /// <summary>Pergunta (uma vez) se o usuário tem direito de uso das imagens do fornecedor. false = não usar o croqui automático.</summary>
    public static bool EnsureTerms(GpSession s)
    {
        var im = s.Settings.Imagery;
        if (im.Provider == ImageryProvider.Nenhum) return false;
        if (im.Provider == ImageryProvider.Google && string.IsNullOrWhiteSpace(im.GoogleKey))
        {
            Ui.Warn("Croqui automático", "O fornecedor Google precisa de uma chave da Google Maps Static API (Configurações › Croqui). O croqui automático não será usado agora.");
            return false;
        }
        if (im.Provider == ImageryProvider.Personalizado && !im.CustomUrl.Contains("{z}") && !im.CustomUrl.Contains("{q}"))
        {
            Ui.Warn("Croqui automático", "Informe a URL do servidor de tiles com {z}, {x} e {y} (Configurações › Croqui).");
            return false;
        }
        if (im.AcceptedTerms) return true;
        var td = new TaskDialog("GeoPoint – Imagem de satélite")
        {
            MainInstruction = "Croqui de localização automático",
            MainContent = "O GeoPoint vai baixar a imagem de satélite da região do imóvel e desenhar o lote por cima, no bloco \"Croqui de localização\".\n\n"
                          + im.LicenseNote + "\n\nO fornecedor pode ser trocado em Configurações › Croqui.",
            CommonButtons = TaskDialogCommonButtons.Cancel,
        };
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Tenho direito de uso – usar o croqui automático");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Agora não (usar a imagem escolhida, se houver)");
        if (td.Show() != TaskDialogResult.CommandLink1) return false;
        im.AcceptedTerms = true;
        try { s.SaveSettings(); } catch { }
        return true;
    }

    /// <summary>Função para o gerador de pranchas: um croqui por prancha (lotes da prancha), na proporção do bloco do modelo.</summary>
    public static Func<SheetSpec, IReadOnlyList<LotRecord>, string?> ForSheets(GpSession s, SheetTemplate? template = null)
    {
        var ctx = s.NewContext();
        var im = s.Settings.Imagery;
        var block = (template ?? SheetTemplate.Default()).Block(SheetBlockKind.Croqui);
        double aspect = block is null ? 1.2 : block.W / Math.Max(1, block.H - 4);
        double widthMm = block?.W ?? 55;
        return (spec, lots) => Capture(ctx, im, lots, aspect, widthMm, spec.Title);
    }

    /// <summary>Gera o croqui (PNG) e devolve o caminho; null sem georreferência.</summary>
    public static string? Capture(ProjectContext ctx, ImagerySettings im, IReadOnlyList<LotRecord> lots, double aspect, double widthMm, string label)
    {
        if (!ctx.Coords.HasGeoreference || im.Provider == ImageryProvider.Nenhum) return null;
        var polys = new List<List<(double Lon, double Lat)>>();
        foreach (var l in lots)
        {
            try
            {
                var ring = l.ToRegion().Outer.ToPolygon(0.2);
                var ll = ring.Select(p => ctx.Coords.ToGeodetic(p)).Where(g => g is not null).Select(g => (g!.Value.LonDeg, g.Value.LatDeg)).ToList();
                if (ll.Count >= 3) polys.Add(ll);
            }
            catch { }
        }
        if (polys.Count == 0) return null;
        int widthPx = (int)Math.Round(widthMm / 25.4 * Math.Clamp(im.Dpi, 96, 400));
        var all = polys.SelectMany(p => p).ToList();
        string key = Hash($"{im.Provider}|{im.CustomUrl}|{widthPx}|{aspect:0.###}|{im.Context}|" + string.Join(";", all.Select(p => $"{p.Lon:0.0000000},{p.Lat:0.0000000}")));
        Directory.CreateDirectory(Path.Combine(Root, "croquis"));
        string outPath = Path.Combine(Root, "croquis", key + ".png");
        if (File.Exists(outPath)) return outPath;

        BitmapSource? baseImage;
        CroquiFrame frame;
        double k;   // pixels do croqui por pixel do enquadramento
        if (im.Provider == ImageryProvider.Google)
        {
            frame = CroquiFrame.Fit(all, aspect, 600, im.Context, Math.Min(im.MaxZoom, 20));
            baseImage = GoogleStatic(im, frame);
            k = widthPx / (double)frame.WidthPx;
        }
        else
        {
            frame = CroquiFrame.Fit(all, aspect, widthPx, im.Context, im.MaxZoom);
            baseImage = null;
            k = 1;
        }
        int W = (int)Math.Round(frame.WidthPx * k), H = (int)Math.Round(frame.HeightPx * k);
        var dv = new DrawingVisual();
        int failed = 0;
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(70, 80, 70)), null, new Rect(0, 0, W, H));
            if (baseImage is not null) dc.DrawImage(baseImage, new Rect(0, 0, W, H));
            else
            {
                string template = im.Provider == ImageryProvider.Esri ? ImagerySettings.EsriUrl : im.CustomUrl;
                int n = 1 << frame.Zoom;
                foreach (var (tx, ty) in frame.Tiles)
                {
                    var bmp = Tile(template, ((tx % n) + n) % n, ty, frame.Zoom, im.Provider.ToString());
                    if (bmp is null) { failed++; continue; }
                    double size = TileMath.TileSize * frame.Resample;
                    var x = (tx * TileMath.TileSize - frame.Origin.X) * frame.Resample;
                    var y = (ty * TileMath.TileSize - frame.Origin.Y) * frame.Resample;
                    dc.DrawImage(bmp, new Rect(x, y, size + 0.5, size + 0.5));
                }
            }
            // Lotes por cima (contorno laranja e preenchimento translúcido, como no modelo).
            double px = Math.Max(1.5, W / 220.0);
            var stroke = new Pen(new SolidColorBrush(Color.FromRgb(255, 140, 20)), px) { LineJoin = PenLineJoin.Round };
            var fill = new SolidColorBrush(Color.FromArgb(70, 255, 150, 40));
            foreach (var poly in polys)
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    var pts = poly.Select(p => frame.ImagePixel(p.Lon, p.Lat) * k).Select(v => new Point(v.X, v.Y)).ToList();
                    c.BeginFigure(pts[0], true, true);
                    c.PolyLineTo(pts.Skip(1).ToList(), true, true);
                }
                dc.DrawGeometry(fill, stroke, g);
            }
            // Atribuição do fornecedor.
            string attr = im.Attribution;
            if (attr.Length > 0)
            {
                var ft = new FormattedText(attr, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Arial"), Math.Max(8, W / 45.0), Brushes.White, 1.0);
                var r = new Rect(W - ft.Width - 6, H - ft.Height - 4, ft.Width + 4, ft.Height + 2);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), null, r);
                dc.DrawText(ft, new Point(r.X + 2, r.Y + 1));
            }
        }
        if (baseImage is null && failed == frame.Tiles.Count)
            throw new InvalidOperationException("não foi possível baixar a imagem de satélite (sem internet ou servidor indisponível).");
        var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(outPath)) enc.Save(fs);
        if (failed > 0) Logger.Info($"Croqui \"{label}\": {failed} de {frame.Tiles.Count} tile(s) não baixados.");
        return outPath;
    }

    private static BitmapSource? Tile(string template, int x, int y, int z, string provider)
    {
        string cache = Path.Combine(Root, "tiles", provider, z.ToString(), x.ToString(), y + ".img");
        try
        {
            byte[] data;
            if (File.Exists(cache) && (DateTime.Now - File.GetLastWriteTime(cache)).TotalDays < 90) data = File.ReadAllBytes(cache);
            else
            {
                data = Http.GetByteArrayAsync(TileMath.TileUrl(template, x, y, z)).GetAwaiter().GetResult();
                Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
                File.WriteAllBytes(cache, data);
            }
            return Decode(data);
        }
        catch (Exception ex)
        {
            Logger.Error($"Tile {z}/{x}/{y}", ex);
            return null;
        }
    }

    private static BitmapSource? GoogleStatic(ImagerySettings im, CroquiFrame f)
    {
        var c = TileMath.FromPixel(f.Origin + new GeoPoint.Core.Geometry.Vec2(f.WidthPx / f.Resample / 2, f.HeightPx / f.Resample / 2), f.Zoom);
        int w = Math.Min(640, (int)Math.Ceiling(f.WidthPx / f.Resample)), h = Math.Min(640, (int)Math.Ceiling(f.HeightPx / f.Resample));
        string url = string.Format(CultureInfo.InvariantCulture,
            "https://maps.googleapis.com/maps/api/staticmap?center={0:0.0000000},{1:0.0000000}&zoom={2}&size={3}x{4}&scale=2&maptype=satellite&key={5}",
            c.Lat, c.Lon, f.Zoom, w, h, Uri.EscapeDataString(im.GoogleKey));
        var data = Http.GetByteArrayAsync(url).GetAwaiter().GetResult();
        return Decode(data);
    }

    private static BitmapSource? Decode(byte[] data)
    {
        using var ms = new MemoryStream(data);
        var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = dec.Frames[0];
        frame.Freeze();
        return frame;
    }

    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..24].ToLowerInvariant();
}
