using System.IO.Compression;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Sheets;

namespace GeoPoint.Core.Tests;

/// <summary>Imagens de teste geradas na hora (PNG sem dependências).</summary>
public static class TestImages
{
    private static readonly uint[] Crc = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc32(IEnumerable<byte> data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (var b in data) c = Crc[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static byte[] Be(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

    /// <summary>PNG RGB de <paramref name="w"/>×<paramref name="h"/> px com a cor de cada pixel.</summary>
    public static byte[] Png(int w, int h, Func<int, int, (byte R, byte G, byte B)> px)
    {
        var raw = new MemoryStream();
        for (int y = 0; y < h; y++)
        {
            raw.WriteByte(0);
            for (int x = 0; x < w; x++) { var (r, g, b) = px(x, y); raw.WriteByte(r); raw.WriteByte(g); raw.WriteByte(b); }
        }
        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true)) raw.WriteTo(zs);
        var o = new MemoryStream();
        o.Write(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10 });
        void Chunk(string type, byte[] data)
        {
            o.Write(Be((uint)data.Length));
            var td = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            o.Write(td);
            o.Write(Be(Crc32(td)));
        }
        Chunk("IHDR", Be((uint)w).Concat(Be((uint)h)).Concat(new byte[] { 8, 2, 0, 0, 0 }).ToArray());
        Chunk("IDAT", z.ToArray());
        Chunk("IEND", Array.Empty<byte>());
        return o.ToArray();
    }

    /// <summary>Logomarca fictícia (marcador de mapa + faixas no lugar do nome), 360×110 px.</summary>
    public static byte[] SampleLogo() => Png(360, 110, (x, y) =>
    {
        // Marcador: círculo com ponta para baixo.
        double cx = 55, cy = 42, r = 30;
        double dx = x - cx, dy = y - cy;
        bool circle = dx * dx + dy * dy <= r * r;
        bool tip = y > cy && y < 100 && Math.Abs(dx) <= (100 - y) * 0.55;
        bool hole = dx * dx + dy * dy <= 11 * 11;
        if ((circle || tip) && !hole) return (20, 92, 140);
        // "Nome" e "slogan" estilizados.
        if (x >= 110 && x <= 340 && y >= 24 && y <= 52) return (30, 30, 30);
        if (x >= 110 && x <= 290 && y >= 64 && y <= 76) return (110, 150, 60);
        return (255, 255, 255);
    });
}

public class LogoTests
{
    [Fact]
    public void ImageHeader_ReadsPngAndJpeg()
    {
        var png = TestImages.Png(40, 12, (_, _) => (1, 2, 3));
        Assert.Equal("png", ImageHeader.Extension(png));
        Assert.Equal((40, 12), ImageHeader.Size(png));
        // JPEG mínimo: SOI, APP0 (16 bytes), SOF0 com altura 300 e largura 500.
        var jpg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0,
            0xFF, 0xC0, 0, 17, 8, 0x01, 0x2C, 0x01, 0xF4, 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1, 0xFF, 0xD9 };
        Assert.Equal("jpg", ImageHeader.Extension(jpg));
        Assert.Equal((500, 300), ImageHeader.Size(jpg));
        Assert.Null(ImageHeader.Size(new byte[] { 1, 2, 3, 4, 5 }));
    }

    [Theory]
    [InlineData(SheetOrientation.Retrato, "A4")]
    [InlineData(SheetOrientation.Paisagem, "A4")]
    [InlineData(SheetOrientation.Retrato, "A3")]
    [InlineData(SheetOrientation.Paisagem, "A2")]
    public void DefaultTemplates_ReserveLogoSpace(SheetOrientation o, string paper)
    {
        var t = SheetTemplate.Default(o, paper);
        Assert.Empty(t.Validate());
        var esc = t.Block(SheetBlockKind.Escritorio);
        Assert.NotNull(esc);
        var tit = t.Block(SheetBlockKind.Titulo)!;
        var cro = t.Block(SheetBlockKind.Croqui)!;
        // Entre o croqui e o título, com a largura da coluna.
        Assert.True(esc!.Y >= tit.Y + tit.H - 0.01 && esc.Y + esc.H <= cro.Y + 0.01);
        Assert.Equal(tit.W, esc.W, 3);
        Assert.True(esc.H >= 13 && cro.H >= 25, $"logo {esc.H:0.0} croqui {cro.H:0.0}");
        // O mapa não perde espaço (a logomarca fica dentro da faixa dos blocos).
        var old = t.Clone();
        old.Blocks.First(b => b.Kind == SheetBlockKind.Escritorio).Visible = false;
        Assert.Equal(old.MapRect(), t.MapRect());
    }

    [Fact]
    public void Logo_EmbeddedInTemplate_TravelsAndKeepsAspect()
    {
        var t = SheetTemplate.Default();
        Assert.False(t.HasLogo);
        var logo = TestImages.SampleLogo();
        t.SetLogo(logo);
        Assert.True(t.HasLogo);
        Assert.Equal("png", t.LogoExt);
        Assert.Equal(360.0 / 110, t.LogoAspect(), 6);
        Assert.Throws<InvalidOperationException>(() => t.Clone().SetLogo(new byte[] { 1, 2, 3, 4, 5, 6 }));
        // Exporta e importa: a imagem vai junto no arquivo do modelo.
        var dir = Path.Combine(Path.GetTempPath(), "gp-logo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string f = Path.Combine(dir, "modelo.json");
            SheetTemplateLibrary.Export(t, f);
            var back = SheetTemplateLibrary.Import(f);
            Assert.Equal(logo, back.LogoBytes());
            // Arquivo de cache para o Revit: o mesmo para a mesma imagem.
            string? a = SheetTemplateLibrary.LogoFile(back, dir), b = SheetTemplateLibrary.LogoFile(t, dir);
            Assert.NotNull(a);
            Assert.Equal(a, b);
            Assert.Equal(logo, File.ReadAllBytes(a!));
            Assert.Null(SheetTemplateLibrary.LogoFile(SheetTemplate.Default(), dir));
            // Histórico sem a imagem (o arquivo não cresce); restaurar mantém a logomarca atual.
            SheetTemplateLibrary.Save(t, "", dir);
            t.Gap = 2;
            SheetTemplateLibrary.Save(t, "gap", dir);
            var saved = SheetTemplateLibrary.LoadAll(dir).First(x => x.Name == t.Name);
            Assert.Single(saved.History);
            Assert.DoesNotContain(t.LogoData[..40], saved.History[0].Json);
            var restored = SheetTemplateLibrary.Restore(saved, saved.History[0]);
            Assert.Equal(1.5, restored.Gap);
            Assert.Equal(logo, restored.LogoBytes());
            // Modelo antigo, só com o caminho do arquivo: continua funcionando.
            var legacy = SheetTemplate.Default();
            legacy.LogoPath = a!;
            Assert.True(legacy.HasLogo);
            Assert.Equal(a, SheetTemplateLibrary.LogoFile(legacy, dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Render_LogoSpace()
    {
        var t = SheetTemplate.Default();
        var esc = t.Block(SheetBlockKind.Escritorio)!.Rect;
        bool Inside(PaperRect r) => r.X0 >= esc.X0 - 1e-6 && r.X1 <= esc.X1 + 1e-6 && r.Y0 >= esc.Y0 - 1e-6 && r.Y1 <= esc.Y1 + 1e-6;
        // Sem logomarca: na prévia aparece o aviso; no Revit o espaço fica em branco.
        var prev = SheetRenderer.Render(new SheetRenderInput { Template = t, Content = SheetContent.Sample(), Placeholders = true });
        Assert.Contains(prev, x => x is PText { Text: "(logomarca do escritório)" } p && esc.Contains(p.At));
        var revit = SheetRenderer.Render(new SheetRenderInput { Template = t, Content = SheetContent.Sample() });
        Assert.DoesNotContain(revit, x => x is PText { Text: "(logomarca do escritório)" });
        Assert.DoesNotContain(revit, x => x is PText p && esc.Contains(p.At));
        // Com logomarca: ocupa o espaço com a proporção da imagem, centralizada.
        t.SetLogo(TestImages.SampleLogo());
        var prims = SheetRenderer.Render(new SheetRenderInput { Template = t, Content = SheetContent.Sample(), Images = new HashSet<string> { "logo" }, LogoAspect = t.LogoAspect() });
        var im = Assert.Single(prims.OfType<PImage>(), x => x.Key == "logo");
        Assert.True(Inside(im.Rect));
        Assert.Equal(t.LogoAspect(), im.Rect.Width / im.Rect.Height, 3);
        Assert.Equal(esc.Center.X, im.Rect.Center.X, 3);
        // Com o nome do escritório: logomarca à esquerda e o nome ao lado.
        t.OfficeName = "Escritório de Topografia Exemplo";
        prims = SheetRenderer.Render(new SheetRenderInput { Template = t, Content = SheetContent.Sample(), Images = new HashSet<string> { "logo" }, LogoAspect = t.LogoAspect() });
        im = prims.OfType<PImage>().Single(x => x.Key == "logo");
        var name = prims.OfType<PText>().Single(x => x.Text.Contains("Topografia"));
        Assert.True(Inside(im.Rect));
        Assert.True(name.At.X >= im.Rect.X1);
    }

    [Fact]
    public void Compose_ShowsLogo()
    {
        var lot = TestProjects.Rect(0, 0, 12, 30, "01");
        var ctx = TestProjects.Context(TestProjects.Project(lot));
        var t = SheetTemplate.Default();
        t.SetLogo(TestImages.SampleLogo());
        var comp = SheetPreview.Compose(ctx, new[] { lot }, new SheetSpec { Kind = SheetKind.Atual, Title = "SITUAÇÃO ATUAL", LotIds = { lot.Id } }, t);
        Assert.Contains(comp.Sheet, x => x is PImage { Key: "logo" });
        var outPath = Environment.GetEnvironmentVariable("GEOPOINT_OUT");
        if (string.IsNullOrEmpty(outPath)) return;
        Directory.CreateDirectory(outPath);
        File.WriteAllBytes(Path.Combine(outPath, "logo-exemplo.png"), TestImages.SampleLogo());
        File.WriteAllText(Path.Combine(outPath, "v153-logo-retrato.svg"), SheetV15PreviewTests.PrimSvg(comp));
        var land = SheetTemplate.Default(SheetOrientation.Paisagem);
        land.SetLogo(TestImages.SampleLogo());
        land.OfficeName = "Escritório de Topografia Exemplo";
        File.WriteAllText(Path.Combine(outPath, "v153-logo-paisagem.svg"),
            SheetV15PreviewTests.PrimSvg(SheetPreview.Compose(ctx, new[] { lot }, new SheetSpec { Kind = SheetKind.Atual, Title = "SITUAÇÃO ATUAL", LotIds = { lot.Id } }, land)));
        File.WriteAllText(Path.Combine(outPath, "v153-logo-vazio.svg"),
            SheetV15PreviewTests.PrimSvg(SheetPreview.Compose(ctx, new[] { lot }, new SheetSpec { Kind = SheetKind.Atual, Title = "SITUAÇÃO ATUAL", LotIds = { lot.Id } }, SheetTemplate.Default())));
    }
}
