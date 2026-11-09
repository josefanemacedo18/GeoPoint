using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Settings;

namespace GeoPoint.Core.Tests;

internal static class TestProjects
{
    public static LotRecord Rect(double x0, double y0, double w, double h, string name, string quadra = "A", LotUse uso = LotUse.Lote)
    {
        var pts = new[] { new Vec2(x0, y0), new Vec2(x0 + w, y0), new Vec2(x0 + w, y0 + h), new Vec2(x0, y0 + h) };
        var lot = new LotRecord { Nome = name, Numero = name, Quadra = quadra, Uso = uso };
        for (int i = 0; i < 4; i++)
            lot.Outer.Add(EdgeDto.FromCurve(new LineCurve(pts[i], pts[(i + 1) % 4]), new[] { $"src-{name}-{i}" }));
        return lot;
    }

    public static LotRecord FromCurves(IEnumerable<Curve2> curves, string name)
    {
        var lot = new LotRecord { Nome = name, Numero = name };
        foreach (var c in curves) lot.Outer.Add(EdgeDto.FromCurve(c));
        return lot;
    }

    /// <summary>Projeto georreferenciado em Porto Franco (MA), referência na origem do desenho.</summary>
    public static ParcelProject Project(params LotRecord[] lots)
    {
        var p = new ParcelProject();
        p.Reference = new GeoReferenceRecord
        {
            IsSet = true, AnchorX = 0, AnchorY = 0, Lat = -6.3375, Lon = -47.39, Height = 150,
            Mode = OrientationMode.ProjectTrueNorth, Precision = PrecisionClass.GoogleMapsEarth, AnchorLabel = "P-01",
        };
        p.Frame = new SharedFrame { AngleRad = 0 };
        p.Info = new ProjectInfo { Proprietario = "Fulano de Tal", Documento = "000.000.000-00", Municipio = "Porto Franco", UF = "MA", Denominacao = "Lote Urbano", Endereco = "Rua E, s/n" };
        p.Lots.AddRange(lots);
        return p;
    }

    public static ProjectContext Context(ParcelProject p, Action<GeoPointSettings>? configure = null)
    {
        var s = new GeoPointSettings();
        s.Technician.Nome = "Arquiteta Teste";
        s.Technician.Registro = "A000000-0";
        configure?.Invoke(s);
        return new ProjectContext(p, s);
    }
}
