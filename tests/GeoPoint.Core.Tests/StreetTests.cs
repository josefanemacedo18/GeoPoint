using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Urban;

namespace GeoPoint.Core.Tests;

public class StreetTests
{
    private static ViaRecord Via(string name) => new() { Nome = name, Largura = 12, Calcada = 2.5 };

    [Fact]
    public void StraightStreet_CurbsAndFarAlignment()
    {
        var a = TestProjects.Rect(0, 0, 10, 30, "01").ToRegion();
        var b = TestProjects.Rect(10, 0, 10, 30, "02").ToRegion();
        var via = Via("Rua A");
        var fr = new List<StreetFrontage>
        {
            new(via.Id, new LineCurve(new(0, 0), new(10, 0)), new Vec2(0, -1)),
            new(via.Id, new LineCurve(new(20, 0), new(10, 0)), new Vec2(0, -1)),
        };
        var d = StreetSchematic.Build(new[] { via }, Array.Empty<CruzamentoRecord>(), fr, new[] { a, b });
        Assert.Empty(d.Warnings);
        // Dois meios-fios (y = -2,5 e y = -14,5), de x = 0 a 20, sem tampa nas pontas.
        Assert.Equal(2, d.Curbs.Count);
        var ys = d.Curbs.Select(c => Math.Round(c.Average(p => p.Y), 3)).OrderBy(y => y).ToList();
        Assert.Equal(new[] { -14.5, -2.5 }, ys);
        Assert.All(d.Curbs, c => Assert.Equal(20, Math.Abs(c[^1].X - c[0].X), 3));
        // Alinhamento só do lado sem lotes (y = -17); o lado dos lotes coincide com as testadas.
        Assert.Single(d.Alignments);
        Assert.All(d.Alignments[0], p => Assert.Equal(-17, p.Y, 3));
        var label = Assert.Single(d.Labels);
        Assert.Equal("Rua A", label.Text);
        Assert.Equal(-8.5, label.Position.Y, 3);
        Assert.Equal(10, label.Position.X, 3);
        Assert.Equal(0, label.AngleRad, 6);
    }

    [Fact]
    public void CornerLot_LCrossing()
    {
        // Lote de esquina com frente para a Rua A (sul) e a Rua B (leste).
        var lot = TestProjects.Rect(0, 0, 20, 20, "01").ToRegion();
        var ra = Via("Rua A"); var rb = Via("Rua B");
        var fr = new List<StreetFrontage>
        {
            new(ra.Id, new LineCurve(new(0, 0), new(20, 0)), new Vec2(0, -1)),
            new(rb.Id, new LineCurve(new(20, 0), new(20, 20)), new Vec2(1, 0)),
        };
        var semCruzamento = StreetSchematic.Build(new[] { ra, rb }, Array.Empty<CruzamentoRecord>(), fr, new[] { lot });
        var cr = new CruzamentoRecord { ViaIds = { ra.Id, rb.Id }, RaioEsquina = 3 };
        var d = StreetSchematic.Build(new[] { ra, rb }, new[] { cr }, fr, new[] { lot });
        Assert.Equal(2, d.Labels.Count);
        // Com o cruzamento, o leito vai até o encontro das ruas (x além de 20 na Rua A).
        double maxX = d.Curbs.SelectMany(c => c).Max(p => p.X);
        Assert.True(maxX >= 20 + 2.5 + 12 - 0.01, $"maxX = {maxX}");
        // O canto do meio-fio na esquina (perto de (22,5; -2,5)) é arredondado: nenhum ponto exatamente no canto vivo.
        var corner = new Vec2(22.5, -2.5);
        double nearest = d.Curbs.SelectMany(c => c).Min(p => p.DistanceTo(corner));
        Assert.True(nearest > 0.5, $"canto vivo encontrado ({nearest})");
        // Sem cruzamento as duas vias param nas testadas.
        Assert.True(semCruzamento.Curbs.SelectMany(c => c).Max(p => p.X) <= 20 + 2.5 + 12 + 0.01);
    }

    [Fact]
    public void LeastSquaresIntersection_TwoLines()
    {
        var c = StreetSchematic.LeastSquaresIntersection(new[] { (new Vec2(0, -8.5), new Vec2(1, 0)), (new Vec2(28.5, 0), new Vec2(0, -1)) });
        Assert.Equal(28.5, c.X, 6);
        Assert.Equal(-8.5, c.Y, 6);
    }

    [Fact]
    public void OffsetPolyline_Miter()
    {
        var pts = new List<Vec2> { new(0, 0), new(10, 0), new(10, 10) };
        var o = StreetSchematic.OffsetPolyline(pts, 1, false);
        Assert.Equal(new Vec2(0, 1), o[0]);
        Assert.Equal(9, o[1].X, 9); Assert.Equal(1, o[1].Y, 9);
        Assert.Equal(new Vec2(9, 10), o[2]);
    }

    [Fact]
    public void Readable_Angles()
    {
        Assert.Equal(0, StreetSchematic.Readable(Math.PI), 9);
        Assert.Equal(Math.PI / 2, StreetSchematic.Readable(Math.PI / 2), 9);
        Assert.Equal(Math.PI / 2, StreetSchematic.Readable(-Math.PI / 2), 9);
    }
}
