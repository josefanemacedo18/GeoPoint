using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;
using GeoPoint.Core.Calc;

namespace GeoPoint.Core.Tests;

public class OperationsTests
{
    [Fact]
    public void Subdivision_Valid()
    {
        var parent = TestProjects.Rect(0, 0, 20, 50, "Mae");
        var a = TestProjects.Rect(0, 0, 10, 50, "01");
        var b = TestProjects.Rect(10, 0, 10, 50, "02");
        var chk = ParcelOperations.CheckSubdivision(parent, new[] { a, b });
        Assert.True(chk.Ok, string.Join("; ", chk.Errors));
        Assert.Equal(1000, chk.ParentArea, 6);
        Assert.Equal(1000, chk.ChildrenArea, 6);
    }

    [Fact]
    public void Subdivision_DetectsOverlapAndOverflow()
    {
        var parent = TestProjects.Rect(0, 0, 20, 50, "Mae");
        var a = TestProjects.Rect(0, 0, 12, 50, "01");
        var b = TestProjects.Rect(10, 0, 12, 50, "02");
        var chk = ParcelOperations.CheckSubdivision(parent, new[] { a, b });
        Assert.False(chk.Ok);
        Assert.Contains(chk.Errors, e => e.Contains("sobrepõem"));
        Assert.Contains(chk.Errors, e => e.Contains("ultrapassa"));
    }

    [Fact]
    public void Merge_AdjacentLots_KeepsSources()
    {
        var a = TestProjects.Rect(0, 0, 10, 30, "01");
        var b = TestProjects.Rect(10, 0, 12, 30, "02");
        var chk = ParcelOperations.CheckMerge(new[] { a, b }, out var u);
        Assert.True(chk.Ok, string.Join("; ", chk.Errors));
        Assert.Equal(660, u.Area, 6);
        Assert.Equal(1, u.OuterRingCount);
        var srcs = u.Outer.SelectMany(e => e.Src).Distinct().ToList();
        Assert.Contains("src-01-0", srcs);
        Assert.Contains("src-02-2", srcs);
        Assert.DoesNotContain("src-01-1", srcs); // divisa interna eliminada
    }

    [Fact]
    public void Merge_TJunctionNeighbors()
    {
        // B tem a divisa com A dividida por um vértice de outro lote (T).
        var a = TestProjects.Rect(0, 0, 10, 20, "01");
        var b = new LotRecord { Nome = "02" };
        var pts = new[] { new Vec2(10, 0), new Vec2(20, 0), new Vec2(20, 20), new Vec2(10, 20), new Vec2(10, 10) };
        for (int i = 0; i < pts.Length; i++) b.Outer.Add(EdgeDto.FromCurve(new LineCurve(pts[i], pts[(i + 1) % pts.Length])));
        var chk = ParcelOperations.CheckMerge(new[] { a, b }, out var u);
        Assert.True(chk.Ok, string.Join("; ", chk.Errors));
        Assert.Equal(400, u.Area, 6);
    }

    [Fact]
    public void Merge_NonContiguous_Fails()
    {
        var a = TestProjects.Rect(0, 0, 10, 10, "01");
        var b = TestProjects.Rect(20, 0, 10, 10, "02");
        var chk = ParcelOperations.CheckMerge(new[] { a, b }, out _);
        Assert.False(chk.Ok);
    }

    [Fact]
    public void Split_EqualParts_PerpendicularToFrontage()
    {
        var lot = TestProjects.Rect(0, 0, 20, 50, "G").ToRegion();
        var frontage = new LineCurve(new Vec2(0, 0), new Vec2(20, 0));
        var res = AreaSplitter.Split(new SplitRequest { Region = lot, CutDirection = new Vec2(0, 1), Frontage = frontage, Mode = SplitMode.EqualParts, Parts = 4 });
        Assert.Equal(3, res.Cuts.Count);
        Assert.Equal(4, res.Pieces.Count);
        Assert.All(res.Pieces, p => Assert.Equal(250, p.Area, 6));
        Assert.All(res.Pieces, p => Assert.Equal(5, p.Frontage, 6));
        Assert.All(res.Cuts, c => Assert.Equal(50, c.Length, 6));
        Assert.Equal(4, res.PieceRegions.Count);
        Assert.All(res.PieceRegions, r => Assert.Equal(250, r.Area, 6));
    }

    [Fact]
    public void Split_TargetArea_RemainderRules()
    {
        var lot = TestProjects.Rect(0, 0, 20, 50, "G").ToRegion();
        var f = new LineCurve(new Vec2(0, 0), new Vec2(20, 0));
        var r1 = AreaSplitter.Split(new SplitRequest { Region = lot, CutDirection = new Vec2(0, 1), Frontage = f, Mode = SplitMode.TargetArea, TargetArea = 300 });
        Assert.Equal(new[] { 300.0, 300, 300, 100 }, r1.Pieces.Select(p => Math.Round(p.Area, 6)).ToArray());
        var r2 = AreaSplitter.Split(new SplitRequest { Region = lot, CutDirection = new Vec2(0, 1), Frontage = f, Mode = SplitMode.TargetArea, TargetArea = 300, MinRemainderArea = 125 });
        Assert.Equal(new[] { 300.0, 300, 400 }, r2.Pieces.Select(p => Math.Round(p.Area, 6)).ToArray());
    }

    [Fact]
    public void Split_FixedFrontage()
    {
        var lot = TestProjects.Rect(0, 0, 20, 50, "G").ToRegion();
        var f = new LineCurve(new Vec2(0, 0), new Vec2(20, 0));
        var r = AreaSplitter.Split(new SplitRequest { Region = lot, CutDirection = new Vec2(0, 1), Frontage = f, Mode = SplitMode.FixedFrontage, FrontageWidth = 6 });
        Assert.Equal(new[] { 6.0, 6, 6, 2 }, r.Pieces.Select(p => Math.Round(p.Frontage, 6)).ToArray());
        Assert.NotEmpty(r.Warnings);
        var rEnd = AreaSplitter.Split(new SplitRequest { Region = lot, CutDirection = new Vec2(0, 1), Frontage = f, Mode = SplitMode.FixedFrontage, FrontageWidth = 6, FromFrontageStart = false });
        Assert.Equal(new[] { 6.0, 6, 6, 2 }, rEnd.Pieces.Select(p => Math.Round(p.Frontage, 6)).ToArray());
    }

    [Fact]
    public void Split_ArcRegion_Exact()
    {
        // Semicírculo de raio 10 (área 50π) dividido em 2 partes iguais por cortes verticais.
        var arc = CurveFactory.FromThreePoints(new Vec2(10, 0), new Vec2(0, 10), new Vec2(-10, 0));
        var region = new Region(new Ring(new Curve2[] { arc, new LineCurve(new Vec2(-10, 0), new Vec2(10, 0)) }));
        var res = AreaSplitter.Split(new SplitRequest { Region = region, CutDirection = new Vec2(0, 1), Mode = SplitMode.EqualParts, Parts = 2 });
        Assert.Single(res.Positions);
        Assert.Equal(0, res.Positions[0], 7);
        Assert.All(res.Pieces, p => Assert.Equal(25 * Math.PI, p.Area, 6));
        Assert.Equal(25 * Math.PI, AreaSplitter.AreaBelow(region, new Vec2(1, 0), 0), 9);
        // Faixa até x = 5: área exata do segmento circular.
        double expected = 50 * Math.PI - (100 * Math.Acos(0.5) - 5 * Math.Sqrt(75)) / 2;
        Assert.Equal(expected, AreaSplitter.AreaBelow(region, new Vec2(1, 0), 5), 9);
    }

    [Fact]
    public void Setbacks_ConvexRectangle()
    {
        var lot = TestProjects.Rect(0, 0, 10, 20, "L").ToRegion();
        var edges = lot.Outer.Edges.Select(e => (e, 1.0)).ToList();
        var r = Setbacks.Compute(lot, edges);
        Assert.Equal(144, r.Area, 1);
        Assert.Single(r.Rings);
    }

    [Fact]
    public void Setbacks_FrontOnly()
    {
        var lot = TestProjects.Rect(0, 0, 10, 20, "L").ToRegion();
        var edges = lot.Outer.Edges.Select((e, i) => (e, i == 0 ? 5.0 : 0.0)).ToList();
        var r = Setbacks.Compute(lot, edges);
        Assert.Equal(150, r.Area, 1);
    }

    [Fact]
    public void Traverse_UserMemorialText()
    {
        const string text = "O primeiro começa partindo-se do vértice P-01 em uma linha seca na distância 50,00m de Azimute 299°36’55’’ até o vértice P-02, confrontando com RUA E nas coordenadas Longitude UTM 235096.80 m E, Latitude UTM 9299316.16 m S, Zona 23M- UTM Sirgas 2000, partindo-se do vértice P-02  segue em direção até o vértice P-03 em uma linha seca na distância 48,63m de Azimute 180°28’14’’, confrontando com VALTENES DOS SANTOS ALMEIDA nas coordenadas 235142.50 m E, 9299292.99 m S, partindo-se do vértice P-03 descrito em planta em anexo finalmente segue em direção até o vértice P-01 em uma linha seca na distância 50,00m de Azimute 61°22’21’’, confrontando com JACY RODRIGUES AGUIAR nas coordenadas Longitude UTM 235143.16 m E, Latitude UTM 9299343.01 m S; fechando assim uma área de 1.062,37m².";
        var courses = MemorialTextParser.Parse(text);
        Assert.Equal(3, courses.Count);
        Assert.Equal(299 + 36 / 60.0 + 55 / 3600.0, courses[0].AzimuthDeg, 9);
        Assert.Equal(50, courses[0].Distance, 9);
        Assert.Equal(48.63, courses[1].Distance, 9);
        Assert.Equal(61 + 22 / 60.0 + 21 / 3600.0, courses[2].AzimuthDeg, 9);
        var tr = Traverse.Compute(courses, adjust: false);
        Assert.InRange(tr.MisclosureLength, 0.01, 0.06);
        Assert.InRange(tr.Area, 1061, 1063.5);
        var adj = Traverse.Compute(courses, adjust: true);
        Assert.Equal(0, adj.AdjustedPoints[^1].DistanceTo(adj.AdjustedPoints[0]), 9);
    }

    [Fact]
    public void Parser_RumoAndMedindo()
    {
        var c = MemorialTextParser.Parse("segue com rumo de 45°30'00\" SE medindo 12,50 m; depois rumo N 10°00'00\" W e distância de 3,00 metros");
        Assert.Equal(2, c.Count);
        Assert.Equal(180 - 45.5, c[0].AzimuthDeg, 9);
        Assert.Equal(12.5, c[0].Distance, 9);
        Assert.Equal(350, c[1].AzimuthDeg, 9);
    }
}

public class KmlReaderTests
{
    [Fact]
    public void ReadsGoogleEarthPlacemarks()
    {
        const string kml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <kml xmlns="http://www.opengis.net/kml/2.2"><Document>
          <Placemark><name>Canto do lote</name><Point><coordinates>-47.390125,-6.337512,0</coordinates></Point></Placemark>
          <Placemark><name>Linha</name><LineString><coordinates>-47.39,-6.33,0 -47.38,-6.33,0</coordinates></LineString></Placemark>
          <Placemark><Point><coordinates> -43.1729,-22.9068 </coordinates></Point></Placemark>
        </Document></kml>
        """;
        var pts = GeoPoint.Core.Export.KmlReader.ParsePoints(kml);
        Assert.Equal(2, pts.Count);
        Assert.Equal("Canto do lote", pts[0].Name);
        Assert.Equal(-6.337512, pts[0].Lat, 9);
        Assert.Equal(-47.390125, pts[0].Lon, 9);
        Assert.Equal(-22.9068, pts[1].Lat, 9);
    }
}
