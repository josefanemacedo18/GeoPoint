using System.Xml.Linq;
using GeoPoint.Core.Export;
using GeoPoint.Core.Model;
using GeoPoint.Core.Settings;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Sheets;

namespace GeoPoint.Core.Tests;

/// <summary>1.7: imagens de satélite guardadas no projeto (geral e por fase) e Google Earth com a cor da hachura, 45% e um lote por arquivo.</summary>
public class V17Tests
{
    private static ParcelProject TwoPhases(out FaseRecord f0, out FaseRecord f1)
    {
        var p = new ParcelProject();
        f0 = p.Fase;   // cria a Fase 0
        f1 = new FaseRecord { Numero = 1, Nome = "Desmembramento" };
        p.Fases.Add(f1);
        return p;
    }

    [Fact]
    public void LocationImage_PhaseImage_ElseGeneral_ElseNone()
    {
        var p = TwoPhases(out var f0, out var f1);
        Assert.Null(LocationImages.For(p, f0.Id));

        var geral = new SatelliteImage { Nome = "geral.png" };
        Assert.Null(LocationImages.Set(p, geral));
        Assert.Same(geral, LocationImages.For(p, f0.Id));
        Assert.Same(geral, LocationImages.For(p, f1.Id));   // fase sem imagem própria usa a geral

        var propria = new SatelliteImage { Nome = "fase1.png", FaseId = f1.Id };
        LocationImages.Set(p, propria);
        Assert.Same(propria, LocationImages.For(p, f1.Id));
        Assert.Same(geral, LocationImages.For(p, f0.Id));
        Assert.Null(LocationImages.Own(p, f0.Id));

        // Trocar a imagem do escopo: a anterior sai (para apagar os bytes).
        var nova = new SatelliteImage { Nome = "geral2.png" };
        Assert.Same(geral, LocationImages.Set(p, nova));
        Assert.Equal(2, p.SatelliteImages.Count);

        // Fase removida: a imagem dela sai na limpeza.
        p.Fases.Remove(f1);
        var gone = LocationImages.Prune(p);
        Assert.Same(propria, Assert.Single(gone));
        Assert.Same(nova, Assert.Single(p.SatelliteImages));

        Assert.Equal("Geral (todas as pranchas)", LocationImages.ScopeLabel(p, ""));
    }

    [Theory]
    [InlineData(1920, 1080, 1.0, 420, 0, 1080, 1080)]        // print Full HD num espaço quadrado: corta as laterais
    [InlineData(1920, 1080, 16.0 / 9, 0, 0, 1920, 1080)]     // mesma proporção: inteira
    [InlineData(1920, 1080, 0.75, 555, 0, 810, 1080)]        // espaço em pé
    [InlineData(1080, 1920, 2.0, 0, 690, 1080, 540)]         // print em pé num espaço deitado: corta em cima e embaixo
    public void Cover_CropsCenterToTheSpaceProportion(int w, int h, double aspect, int x, int y, int cw, int ch)
    {
        var r = LocationImages.Cover(w, h, aspect);
        Assert.Equal((x, y, cw, ch), r);
        Assert.Equal(aspect, (double)r.W / r.H, 2);
    }

    [Fact]
    public void Reduce_KeepsFullHd_ShrinksBigger()
    {
        Assert.Equal((1920, 1080), LocationImages.Reduce(1920, 1080));
        Assert.Equal((1366, 768), LocationImages.Reduce(1366, 768));
        Assert.Equal((1920, 1080), LocationImages.Reduce(3840, 2160));
        Assert.Equal((1080, 1920), LocationImages.Reduce(2160, 3840));
    }

    [Fact]
    public void Templates_HaveCroquiSpaceProportion()
    {
        foreach (var t in new[] { SheetTemplate.Default(), SheetTemplate.Default(SheetOrientation.Paisagem) })
            Assert.InRange(t.CroquiAspect(), 0.2, 5);
        var none = SheetTemplate.Default();
        none.Blocks.RemoveAll(b => b.Kind == SheetBlockKind.Croqui);
        Assert.Equal(0, none.CroquiAspect());
    }

    [Fact]
    public void SatelliteImages_SurviveProjectJson()
    {
        var p = TwoPhases(out _, out var f1);
        LocationImages.Set(p, new SatelliteImage { Nome = "print.png", FaseId = f1.Id, Width = 1920, Height = 1080, Bytes = 400000 });
        var back = System.Text.Json.JsonSerializer.Deserialize<ParcelProject>(System.Text.Json.JsonSerializer.Serialize(p))!;
        var img = Assert.Single(back.SatelliteImages);
        Assert.Equal(f1.Id, img.FaseId);
        Assert.Equal(1920, img.Width);
    }

    [Fact]
    public void Kml_LotColorFromHatch_With45PercentFill()
    {
        var a = TestProjects.Rect(0, 0, 12, 30, "01");
        var b = TestProjects.Rect(12, 0, 12, 30, "02");
        var p = TestProjects.Project(a, b);
        var ctx = TestProjects.Context(p);
        Assert.Equal(0.45, ctx.Settings.Kml.FillOpacity);
        var colors = new Dictionary<string, (byte R, byte G, byte B)> { [a.Id] = LotPalette.Of(0), [b.Id] = LotPalette.Of(1) };
        var doc = XDocument.Parse(GisExport.BuildKml(ctx, new[] { a, b }, "Teste", colors));
        XNamespace k = "http://www.opengis.net/kml/2.2";
        string StyleOf(string lotName)
        {
            var pm = doc.Descendants(k + "Placemark").First(x => x.Element(k + "name")?.Value.StartsWith("Lote " + lotName) == true || x.Element(k + "name")?.Value == lotName);
            return pm.Element(k + "styleUrl")!.Value.TrimStart('#');
        }
        var styles = doc.Descendants(k + "Style").ToDictionary(x => (string)x.Attribute("id")!, x => x);
        var s1 = styles[StyleOf("01")];
        // Vermelho da hachura 1 (220, 60, 50): KML grava aabbggrr; 45% de cor = alfa 0x73.
        Assert.Equal("ff323cdc", s1.Element(k + "LineStyle")!.Element(k + "color")!.Value);
        Assert.Equal("73323cdc", s1.Element(k + "PolyStyle")!.Element(k + "color")!.Value);
        var s2 = styles[StyleOf("02")];
        Assert.Equal("73d25a28", s2.Element(k + "PolyStyle")!.Element(k + "color")!.Value);   // azul (40, 90, 210)
        // Sem cores: continua pela cor do uso.
        var plain = XDocument.Parse(GisExport.BuildKml(ctx, new[] { a }, "Teste"));
        Assert.StartsWith("#uso", plain.Descendants(k + "styleUrl").First().Value);
    }

    [Fact]
    public void Kml_SingleLotFile_HasOnlyThatLot()
    {
        var a = TestProjects.Rect(0, 0, 12, 30, "01");
        var b = TestProjects.Rect(12, 0, 12, 30, "02");
        var ctx = TestProjects.Context(TestProjects.Project(a, b));
        var doc = XDocument.Parse(GisExport.BuildKml(ctx, new[] { b }, "Projeto – Lote 02", new Dictionary<string, (byte, byte, byte)> { [b.Id] = LotPalette.Of(1) }));
        XNamespace k = "http://www.opengis.net/kml/2.2";
        var polys = doc.Descendants(k + "Placemark").Where(x => x.Element(k + "Polygon") is not null).ToList();
        Assert.Single(polys);
        Assert.Contains("02", polys[0].Element(k + "name")!.Value);
        // Vértices do lote (pontos com as coordenadas).
        Assert.Equal(4, doc.Descendants(k + "Placemark").Count(x => x.Element(k + "Point") is not null));
    }

    [Fact]
    public void Settings_Migrate_OldDefaultOpacity_To45()
    {
        var old = new GeoPointSettings { Version = 3 };
        old.Kml.FillOpacity = 0.35;
        var json = SettingsStore.Serialize(old);
        var path = Path.Combine(Path.GetTempPath(), "gp-set-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, json);
            var s = SettingsStore.Load(path);
            Assert.Equal(0.45, s.Kml.FillOpacity);
            Assert.True(s.Kml.OneFilePerLot);
            Assert.Equal(4, s.Version);
            // Valor escolhido pelo usuário fica.
            old.Kml.FillOpacity = 0.6;
            File.WriteAllText(path, SettingsStore.Serialize(old));
            Assert.Equal(0.6, SettingsStore.Load(path).Kml.FillOpacity);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("Projeto - Lote 01 – Quadra A", "Projeto - Lote 01 - Quadra A")]
    [InlineData("Lote 3/4: \"novo\"?", "Lote 3_4_ _novo__")]
    [InlineData("   ", "lote")]
    public void SafeFileName_ForEachLot(string name, string expected) => Assert.Equal(expected, GisExport.SafeFileName(name));

    [Fact]
    public void Palette_IsSharedAndCycles()
    {
        Assert.Equal(8, LotPalette.Colors.Length);
        Assert.Equal(LotPalette.Of(0), LotPalette.Of(8));
        Assert.Equal(((byte)220, (byte)60, (byte)50), LotPalette.Of(0));
    }

    /// <summary>Prévia da prancha com a imagem de satélite no espaço de localização (só com GEOPOINT_OUT e GEOPOINT_SAT_IMG).</summary>
    [Fact]
    public void WritesSheetPreviewWithSatelliteImage()
    {
        var outPath = Environment.GetEnvironmentVariable("GEOPOINT_OUT");
        var img = Environment.GetEnvironmentVariable("GEOPOINT_SAT_IMG");
        if (string.IsNullOrEmpty(outPath) || string.IsNullOrEmpty(img) || !File.Exists(img)) return;
        Directory.CreateDirectory(outPath);
        var a1 = TestProjects.Rect(0, 0, 12, 30, "01"); var a2 = TestProjects.Rect(12, 0, 12, 30, "02");
        var p = TestProjects.Project(a1, a2);
        p.Info.Proprietario = "Fulano de Tal"; p.Info.Matricula = "1234"; p.Info.Municipio = "Porto Franco"; p.Info.UF = "MA";
        var ctx = TestProjects.Context(p);
        SheetV15PreviewTests.CroquiForSvg = File.ReadAllBytes(img);
        try
        {
            foreach (var o in new[] { SheetOrientation.Retrato, SheetOrientation.Paisagem })
            {
                var spec = new SheetSpec { Kind = SheetKind.Atual, Title = "SITUAÇÃO ATUAL", LotIds = { a1.Id, a2.Id } };
                var comp = SheetPreview.Compose(ctx, new[] { a1, a2 }, spec, SheetTemplate.Default(o), croqui: true);
                Assert.Contains(comp.Sheet, x => x is PImage { Key: "croqui" });
                File.WriteAllText(Path.Combine(outPath, $"v17-prancha-{(o == SheetOrientation.Retrato ? "retrato" : "paisagem")}.svg"), SheetV15PreviewTests.PrimSvg(comp));
            }
        }
        finally { SheetV15PreviewTests.CroquiForSvg = null; }
    }

    [Fact]
    public void ClipShapes_RemovesTheSegmentInsideAPolygon()
    {
        var tri = new[] { new Vec2(0, 0), new Vec2(10, 0), new Vec2(5, 10) };
        var parts = SheetRenderer.ClipShapes(new Vec2(-5, 5), new Vec2(15, 5), new[] { (IReadOnlyList<Vec2>)tri });
        Assert.Equal(2, parts.Count);
        Assert.Equal(2.5, parts[0].Item2.X, 6);
        Assert.Equal(7.5, parts[1].Item1.X, 6);
        // Fora do polígono: inteiro.
        Assert.Single(SheetRenderer.ClipShapes(new Vec2(-5, 20), new Vec2(15, 20), new[] { (IReadOnlyList<Vec2>)tri }));
        // Côncavo (forma de U): a linha que atravessa as duas pernas fica com o vão do meio.
        var u = new[] { new Vec2(0, 0), new Vec2(9, 0), new Vec2(9, 9), new Vec2(6, 9), new Vec2(6, 3), new Vec2(3, 3), new Vec2(3, 9), new Vec2(0, 9) };
        var mid = SheetRenderer.ClipShapes(new Vec2(-2, 6), new Vec2(11, 6), new[] { (IReadOnlyList<Vec2>)u });
        Assert.Equal(3, mid.Count);
        Assert.Equal(3, mid[1].Item1.X, 6);
        Assert.Equal(6, mid[1].Item2.X, 6);
    }

    [Fact]
    public void Grid_DoesNotCrossTheDrawing()
    {
        // Triângulo como o da prancha do usuário: nenhuma linha da grade passa dentro do lote nem encosta nas divisas.
        var lot = TestProjects.Poly("01", new Vec2(19.021, 1.366), new Vec2(-0.979, 1.366), new Vec2(5.496, 22.392));
        var p = TestProjects.Project(lot);
        var ctx = TestProjects.Context(p);
        var spec = new SheetSpec { Kind = SheetKind.Atual, Title = "PLANTA DE SITUAÇÃO", LotIds = { lot.Id }, CoordTable = true };
        var comp = SheetPreview.Compose(ctx, new[] { lot }, spec, SheetTemplate.Default(), crs: GeoPoint.Core.Geodesy.CrsDef.Utm());
        var shapes = comp.Input.GridShapes;
        Assert.NotEmpty(shapes);
        var grid = comp.Sheet.OfType<PLine>().Where(l => l.Style == PLineStyle.Grade).ToList();
        Assert.NotEmpty(grid);
        var area = shapes[0];   // a área do lote (no papel)
        foreach (var g in grid)
            for (int k = 1; k < 20; k++)
            {
                var q = g.A + (g.B - g.A) * (k / 20.0);
                Assert.False(GeoPoint.Core.Geometry.PolygonUtil.PointInPolygon(area, q), "grade dentro do lote");
            }
    }

    [Fact]
    public void Legend_SymbolsMatchTheDrawing()
    {
        var lot = TestProjects.Rect(0, 0, 12, 30, "01");
        var ctx = TestProjects.Context(TestProjects.Project(lot));
        var spec = new SheetSpec { Kind = SheetKind.Atual, Title = "SITUAÇÃO", LotIds = { lot.Id }, Hatch = true };
        var comp = SheetPreview.Compose(ctx, new[] { lot }, spec, SheetTemplate.Default(), crs: GeoPoint.Core.Geodesy.CrsDef.Utm());
        var conv = SheetTemplate.Default().Block(SheetBlockKind.Convencoes)!.Rect;
        var inLegend = comp.Sheet.Where(x => x switch
        {
            PLine l => conv.Contains(l.A) && conv.Contains(l.B) && !(Math.Abs(l.A.Y - l.B.Y) < 1e-9 && (Math.Abs(l.A.Y - conv.Y0) < 1e-6 || Math.Abs(l.A.Y - conv.Y1) < 1e-6)),
            PCircle c => conv.Contains(c.C),
            _ => false,
        }).ToList();
        // Hachura em linhas na cor do lote (a mesma paleta do desenho), sem linha grossa (as divisas são finas) e ponto cheio do vértice.
        Assert.Contains(inLegend, x => x is PLine { Style: PLineStyle.Hachura });
        Assert.DoesNotContain(inLegend, x => x is PLine { Style: PLineStyle.Limite });
        Assert.Contains(inLegend, x => x is PCircle { Filled: true });
    }

    [Fact]
    public void ScanLines_StayInsideThePolygon()
    {
        var r = new[] { new Vec2(0, 0), new Vec2(8, 0), new Vec2(8, 2.6), new Vec2(0, 2.6) };
        var lines = SheetRenderer.ScanLines(r, 45, 1.1);
        Assert.InRange(lines.Count, 6, 14);
        foreach (var (a, b) in lines)
            foreach (var q in new[] { a, b, (a + b) * 0.5 })
            {
                Assert.InRange(q.X, -1e-6, 8 + 1e-6);
                Assert.InRange(q.Y, -1e-6, 2.6 + 1e-6);
            }
        Assert.Equal(14, SheetRenderer.ScanLines(r, 0, 0.18).Count);   // 2,6 mm a cada 0,18 mm
    }
}
