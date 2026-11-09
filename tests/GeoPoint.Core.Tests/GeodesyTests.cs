using System.Text.Json;
using GeoPoint.Core.Geodesy;

namespace GeoPoint.Core.Tests;

public class GeodesyTests
{
    private static JsonElement Ref()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "geo_ref.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    [Fact]
    public void Utm_MatchesPyproj()
    {
        foreach (var p in Ref().GetProperty("utm").EnumerateArray())
        {
            double lat = p.GetProperty("lat").GetDouble(), lon = p.GetProperty("lon").GetDouble();
            var zone = new UtmZone(p.GetProperty("zone").GetInt32(), p.GetProperty("south").GetBoolean());
            Assert.Equal(zone, UtmZone.FromLonLat(lon, lat));
            var tm = zone.Projection(Ellipsoid.GRS80);
            var r = tm.Forward(lat, lon);
            Assert.Equal(p.GetProperty("e").GetDouble(), r.E, 3);
            Assert.Equal(p.GetProperty("n").GetDouble(), r.N, 3);
            Assert.Equal(p.GetProperty("k").GetDouble(), r.Scale, 8);
            Assert.True(Math.Abs(p.GetProperty("conv").GetDouble() - r.ConvergenceDeg) < 1e-8, $"convergência {r.ConvergenceDeg}");
            var (la, lo) = tm.Inverse(r.E, r.N);
            Assert.Equal(lat, la, 9);
            Assert.Equal(lon, lo, 9);
        }
    }

    [Fact]
    public void Geodesic_MatchesPyproj()
    {
        foreach (var p in Ref().GetProperty("geod").EnumerateArray())
        {
            var a = new GeodeticCoord(p.GetProperty("lat1").GetDouble(), p.GetProperty("lon1").GetDouble());
            var b = new GeodeticCoord(p.GetProperty("lat2").GetDouble(), p.GetProperty("lon2").GetDouble());
            var r = Geodesic.Inverse(a, b, Ellipsoid.GRS80);
            Assert.True(r.Converged);
            Assert.Equal(p.GetProperty("d").GetDouble(), r.Distance, 4);
            Assert.Equal(p.GetProperty("az1").GetDouble(), r.Azimuth1Deg, 7);
        }
    }

    [Fact]
    public void DatumShifts_MatchPyproj()
    {
        foreach (var (key, datum) in new[] { ("sad69", Datum.Sad69), ("ca", Datum.CorregoAlegre) })
        {
            foreach (var p in Ref().GetProperty(key).EnumerateArray())
            {
                var g = new GeodeticCoord(p.GetProperty("lat").GetDouble(), p.GetProperty("lon").GetDouble());
                var s = datum.ToSirgas(g);
                Assert.Equal(p.GetProperty("lat2").GetDouble(), s.LatDeg, 8);
                Assert.Equal(p.GetProperty("lon2").GetDouble(), s.LonDeg, 8);
                var back = datum.FromSirgas(s);
                Assert.Equal(g.LatDeg, back.LatDeg, 9);
                Assert.Equal(g.LonDeg, back.LonDeg, 9);
            }
        }
    }

    [Fact]
    public void Enu_MatchesPyprojTopocentric()
    {
        foreach (var p in Ref().GetProperty("enu").EnumerateArray())
        {
            var o = new GeodeticCoord(p.GetProperty("lat0").GetDouble(), p.GetProperty("lon0").GetDouble(), p.GetProperty("h0").GetDouble());
            var q = new GeodeticCoord(p.GetProperty("lat").GetDouble(), p.GetProperty("lon").GetDouble(), p.GetProperty("h").GetDouble());
            var (e, n, u) = Geocentric.ToEnu(o, q, Ellipsoid.GRS80);
            Assert.Equal(p.GetProperty("e").GetDouble(), e, 5);
            Assert.Equal(p.GetProperty("n").GetDouble(), n, 5);
            Assert.Equal(p.GetProperty("u").GetDouble(), u, 5);
            var back = Geocentric.FromEnu(o, e, n, u, Ellipsoid.GRS80);
            Assert.Equal(q.LatDeg, back.LatDeg, 10);
            Assert.Equal(q.LonDeg, back.LonDeg, 10);
            Assert.Equal(q.Height, back.Height, 5);
        }
    }

    [Fact]
    public void LatitudeBand_MatchesUserMemorial()
    {
        Assert.Equal('M', UtmZone.LatitudeBand(-6.3));
        Assert.Equal('K', UtmZone.LatitudeBand(-22.9));
        Assert.Equal('J', UtmZone.LatitudeBand(-30.0));
    }

    [Theory]
    [InlineData(299.615277777, 0, "299°36'55\"")]
    [InlineData(59.99999999, 0, "60°00'00\"")]
    [InlineData(10.5, 2, "10°30'00,00\"")]
    [InlineData(0.000138888, 1, "0°00'00,5\"")]
    public void Dms_Formatting(double deg, int dec, string expected)
    {
        Assert.Equal(expected, AngleText.FormatDms(deg, dec, AngleKind.Azimuth));
    }

    [Fact]
    public void Bearing_Conversion()
    {
        Assert.Equal("29°36'55\" NO", AngleText.FormatBearing(330.384722, 0));
        Assert.Equal("0°28'14\" SO", AngleText.FormatBearing(180.470555, 0));
        Assert.Equal("61°22'21\" NE", AngleText.FormatBearing(61.3725, 0));
        Assert.Equal("45°00'00\" SE", AngleText.FormatBearing(135, 0));
    }

    [Theory]
    [InlineData("-22.906847, -43.172897", -22.906847, -43.172897)]
    [InlineData("-22,906847, -43,172897", -22.906847, -43.172897)]
    [InlineData("-22,906847; -43,172897", -22.906847, -43.172897)]
    [InlineData("22°54'24.6\"S 43°10'22.4\"W", -22.906833, -43.172889)]
    [InlineData("22°54'24,6\" S, 43°10'22,4\" O", -22.906833, -43.172889)]
    [InlineData("S 22 54 24.6 W 43 10 22.4", -22.906833, -43.172889)]
    [InlineData("-6.3375 -47.39", -6.3375, -47.39)]
    public void LatLon_Parsing(string text, double lat, double lon)
    {
        Assert.True(AngleText.TryParseLatLon(text, out double la, out double lo), text);
        Assert.Equal(lat, la, 5);
        Assert.Equal(lon, lo, 5);
    }

    [Theory]
    [InlineData("9.299.316,16", 9299316.16)]
    [InlineData("235096.80", 235096.80)]
    [InlineData("235096,80", 235096.80)]
    [InlineData("1,062.37", 1062.37)]
    [InlineData("50,00 m", 50.0)]
    public void Number_Parsing(string text, double expected)
    {
        Assert.True(AngleText.TryParseNumber(text, out double v));
        Assert.Equal(expected, v, 6);
    }
}
