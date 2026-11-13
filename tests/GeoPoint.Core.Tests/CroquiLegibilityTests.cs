using System.Globalization;
using System.Text;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Imagery;
using GeoPoint.Core.Model;

namespace GeoPoint.Core.Tests;

/// <summary>Croqui legível: tile "sem dados", limite de ampliação da foto e croqui desenhado (sem imagem).</summary>
public class CroquiLegibilityTests
{
    private static byte[] Image(int w, int h, Func<int, int, (byte R, byte G, byte B)> px)
    {
        var d = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var (r, g, b) = px(x, y);
                int i = (y * w + x) * 4;
                d[i] = b; d[i + 1] = g; d[i + 2] = r; d[i + 3] = 255;
            }
        return d;
    }

    [Fact]
    public void BlankTile_IsDetected_RealPhotoIsNot()
    {
        // Cinza com um texto branco no meio (como o "Map data not yet available" da Esri).
        var blank = Image(256, 256, (x, y) => x > 70 && x < 185 && y > 105 && y < 140 && (x + y) % 3 == 0 ? ((byte)255, (byte)255, (byte)255) : ((byte)204, (byte)204, (byte)204));
        Assert.True(TileQuality.LooksBlank(blank, 256, 256, 256 * 4));
        // "Foto": cores variadas (telhados, ruas, vegetação).
        var rnd = new Random(7);
        var photo = Image(256, 256, (x, y) => ((byte)rnd.Next(30, 200), (byte)rnd.Next(30, 180), (byte)rnd.Next(20, 160)));
        Assert.False(TileQuality.LooksBlank(photo, 256, 256, 256 * 4));
        // Imagem lisa mas com algumas manchas (ex.: lago grande): continua foto se houver variação suficiente.
        var lake = Image(256, 256, (x, y) => x < 140 ? ((byte)30, (byte)60, (byte)90) : ((byte)rnd.Next(60, 200), (byte)rnd.Next(60, 180), (byte)rnd.Next(40, 140)));
        Assert.False(TileQuality.LooksBlank(lake, 256, 256, 256 * 4));
    }

    [Fact]
    public void EsriUrl_AsksForMissingTilesAs404()
    {
        Assert.Contains("blankTile=false", ImagerySettings.EsriUrl);
        Assert.Equal("https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/17/67846/48280?blankTile=false",
            TileMath.TileUrl(ImagerySettings.EsriUrl, 48280, 67846, 17));
    }

    [Fact]
    public void Fit_LimitsUpscaling_ShowingMoreNeighborhood()
    {
        // Lote de 12 × 30 m em Porto Franco; bloco de 49,5 mm a 220 dpi ≈ 429 px.
        double lat = -6.333656, lon = -47.394191;
        double dLat = 30 / 111320.0, dLon = 12 / (111320.0 * Math.Cos(lat * Math.PI / 180));
        var poly = new List<(double, double)> { (lon, lat), (lon + dLon, lat), (lon + dLon, lat + dLat), (lon, lat + dLat) };
        // Só há imagem até o zoom 17: sem limite, a foto seria ampliada ~4x (borrada).
        var blurry = CroquiFrame.Fit(poly, 1.6, 429, 3, maxZoom: 17);
        Assert.Equal(17, blurry.Zoom);
        Assert.True(blurry.Resample > 3.5);
        var sharp = CroquiFrame.Fit(poly, 1.6, 429, 3, maxZoom: 17, maxResample: 2);
        Assert.Equal(17, sharp.Zoom);
        Assert.Equal(2, sharp.Resample, 6);
        // Mais vizinhança: a janela passa de ~144 m (3 × o lote) para ~255 m.
        Assert.InRange(blurry.SpanM(lat), 140, 148);
        Assert.InRange(sharp.SpanM(lat), 240, 270);
        // Onde há imagem nos zooms altos, o enquadramento é o normal (zoom 18 aqui, 144 m).
        var full = CroquiFrame.Fit(poly, 1.6, 429, 3, maxZoom: 19, maxResample: 2);
        Assert.Equal(18, full.Zoom);
        Assert.InRange(full.SpanM(lat), 140, 148);
        Assert.True(full.Resample <= 2);
    }

    /// <summary>Quadra com duas fileiras de 4 lotes de 12 × 30 m, Rua A ao sul e Rua B ao norte.</summary>
    public static (ProjectContext ctx, List<LotRecord> lots, LotRecord sheetLot) Quadra(bool vias = true)
    {
        var lots = new List<LotRecord>();
        for (int i = 0; i < 4; i++) lots.Add(TestProjects.Rect(i * 12, 0, 12, 30, $"0{i + 1}"));
        for (int i = 0; i < 4; i++) lots.Add(TestProjects.Rect(i * 12, 30, 12, 30, $"0{i + 5}"));
        var p = TestProjects.Project(lots.ToArray());
        if (vias)
        {
            var a = new ViaRecord { Nome = "Rua A" }; var b = new ViaRecord { Nome = "Rua B" };
            p.Vias.Add(a); p.Vias.Add(b);
            for (int i = 0; i < 4; i++)
            {
                lots[i].EdgeInfos.Add(new EdgeInfo { MX = i * 12 + 6, MY = 0, Tipo = ConfrontanteTipo.Via, ViaId = a.Id, Testada = true });
                lots[i + 4].EdgeInfos.Add(new EdgeInfo { MX = i * 12 + 6, MY = 60, Tipo = ConfrontanteTipo.Via, ViaId = b.Id, Testada = true });
            }
        }
        else lots[1].EdgeInfos.Add(new EdgeInfo { MX = 18, MY = 0, Confrontante = "Rua A", Testada = true });
        return (TestProjects.Context(p), lots, lots[1]);
    }

    [Fact]
    public void VectorCroqui_ShowsLotNeighborsStreetsNorthAndScale()
    {
        var (ctx, _, lot) = Quadra();
        var d = CroquiVector.Build(ctx, new[] { lot }, 429, 1.6);
        Assert.Equal(429, d.Width);
        Assert.Equal(268, d.Height);
        // Lote em destaque, dentro da imagem, com tamanho legível.
        var hl = Assert.Single(d.Shapes, s => s.Stroke == CroquiVector.Highlight);
        var bb = BBox.Of(hl.Pts);
        Assert.True(bb.MinX > 0 && bb.MaxX < d.Width && bb.MinY > 0 && bb.MaxY < d.Height);
        Assert.True(bb.Width > 30 && bb.Height > 60, $"{bb.Width:0} × {bb.Height:0} px");
        // Vizinhos, vias com nome, norte e escala gráfica.
        Assert.True(d.Shapes.Count(s => s.Fill == new Rgba(229, 228, 222)) >= 5);
        Assert.Contains(d.Texts, t => t.Text == "RUA A");
        Assert.Contains(d.Texts, t => t.Text == "N");
        Assert.Contains(d.Texts, t => t.Text.EndsWith(" m"));
        // O mesmo croqui gera a mesma assinatura (mesmo arquivo, sem duplicar a imagem no Revit).
        Assert.Equal(d.Signature(), CroquiVector.Build(ctx, new[] { lot }, 429, 1.6).Signature());

        // Sem vias cadastradas: o nome da rua vem da testada.
        var (ctx2, _, lot2) = Quadra(vias: false);
        var d2 = CroquiVector.Build(ctx2, new[] { lot2 }, 429, 1.6);
        var t2 = Assert.Single(d2.Texts, t => t.Text == "RUA A");
        Assert.True(t2.At.Y > BBox.Of(d2.Shapes.Single(s => s.Stroke == CroquiVector.Highlight).Pts).MaxY);   // ao sul (abaixo) do lote

        var outPath = Environment.GetEnvironmentVariable("GEOPOINT_OUT");
        if (string.IsNullOrEmpty(outPath)) return;
        Directory.CreateDirectory(outPath);
        File.WriteAllText(Path.Combine(outPath, "croqui-desenho.svg"), Svg(d, 2));
        File.WriteAllText(Path.Combine(outPath, "croqui-desenho-sem-vias.svg"), Svg(d2, 2));
    }

    /// <summary>SVG do croqui desenhado (mesmo resultado que o PNG gerado no Revit).</summary>
    public static string Svg(CroquiDrawing d, double k = 1)
    {
        string F(double v) => (v * k).ToString("0.##", CultureInfo.InvariantCulture);
        string C(Rgba c) => c.A == 0 ? "none" : $"rgba({c.R},{c.G},{c.B},{(c.A / 255.0).ToString("0.##", CultureInfo.InvariantCulture)})";
        var sb = new StringBuilder($"<svg xmlns='http://www.w3.org/2000/svg' width='{F(d.Width)}' height='{F(d.Height)}'><rect width='100%' height='100%' fill='{C(d.Background)}' stroke='#888'/>");
        foreach (var s in d.Shapes)
        {
            string pts = string.Join(" ", s.Pts.Select(p => $"{F(p.X)},{F(p.Y)}"));
            sb.Append(s.Closed
                ? $"<polygon points='{pts}' fill='{C(s.Fill)}' stroke='{C(s.Stroke)}' stroke-width='{F(s.Width)}' stroke-linejoin='round'/>"
                : $"<polyline points='{pts}' fill='none' stroke='{C(s.Stroke)}' stroke-width='{F(s.Width)}'/>");
        }
        foreach (var t in d.Texts)
        {
            string tr = $"rotate({(t.AngleRad * 180 / Math.PI).ToString("0.##", CultureInfo.InvariantCulture)} {F(t.At.X)} {F(t.At.Y)})";
            string common = $"x='{F(t.At.X)}' y='{F(t.At.Y)}' font-family='Arial' font-size='{F(t.SizePx)}' text-anchor='middle' dominant-baseline='central' {(t.Bold ? "font-weight='bold'" : "")} transform='{tr}'";
            string txt = System.Security.SecurityElement.Escape(t.Text);
            if (t.Halo) sb.Append($"<text {common} fill='none' stroke='white' stroke-width='{F(t.SizePx / 4)}' stroke-linejoin='round'>{txt}</text>");
            sb.Append($"<text {common} fill='{C(t.Color)}'>{txt}</text>");
        }
        return sb.Append("</svg>").ToString();
    }

    [Fact]
    public void TextFitCheck_FlagsTextsBiggerThanPlanned()
    {
        // Texto horizontal planejado com 10 m × 1 m (meia largura 5, meia altura 0,5).
        var plan = Obb.Of(new Vec2(100, 50), 0, 5, 0.5);
        var ok = TextFitCheck.Compare(plan, new BBox(95.1, 49.6, 104.9, 50.4));
        Assert.False(ok.Bigger);
        Assert.Equal(0.98, ok.WidthRatio!.Value, 2);
        // 15% mais largo (fonte mais larga que o Arial): passa da caixa mais que meia altura.
        var wide = TextFitCheck.Compare(plan, new BBox(94.2, 49.6, 105.8, 50.4));
        Assert.True(wide.Bigger);
        Assert.Equal(1.16, wide.WidthRatio!.Value, 2);
        // Texto vertical: a largura é medida no eixo Y.
        var vert = Obb.Of(new Vec2(0, 0), Math.PI / 2, 5, 0.5);
        var v = TextFitCheck.Compare(vert, new BBox(-0.5, -5, 0.5, 5));
        Assert.False(v.Bigger);
        Assert.Equal(1, v.WidthRatio!.Value, 6);
        // Inclinado: só confere se passou da caixa (sem razão de largura).
        Assert.Null(TextFitCheck.Compare(Obb.Of(Vec2.Zero, 0.6, 5, 0.5), new BBox(-4, -3, 4, 3)).WidthRatio);
    }
}
