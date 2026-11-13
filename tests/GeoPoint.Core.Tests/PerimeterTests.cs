using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;

namespace GeoPoint.Core.Tests;

/// <summary>Desenhar Perímetro por coordenadas: leitura de memorial, SIGEF e tabelas, cálculo e conferência.</summary>
public class PerimeterTests
{
    // Retângulo 30 m (leste–oeste) × 12 m (norte–sul) em Porto Franco – MA (UTM 23S).
    public const string Memorial =
        "Inicia-se a descrição deste perímetro no vértice P-01, de coordenadas N 9.299.292,99 m e E 235.142,50 m; " +
        "deste, segue confrontando com João Pereira, com azimute de 90°00'00\" e distância de 30,00 m, até o vértice P-02, de coordenadas N 9.299.292,99 m e E 235.172,50 m; " +
        "deste, segue com azimute de 180°00'00\" e distância de 12,00 m, até o vértice P-03, de coordenadas N 9.299.280,99 m e E 235.172,50 m; " +
        "deste, segue com azimute de 270°00'00\" e distância de 30,00 m, até o vértice P-04, de coordenadas N 9.299.280,99 m e E 235.142,50 m; " +
        "deste, segue com azimute de 0°00'00\" e distância de 12,00 m, até o vértice P-01, ponto inicial da descrição deste perímetro. " +
        "Todas as coordenadas aqui descritas estão georreferenciadas ao Sistema Geodésico Brasileiro, datum SIRGAS 2000, projeção UTM, fuso 23.";

    [Fact]
    public void Memorial_Utm_ReadsVertices_AndChecksCourses()
    {
        var t = PerimeterText.ParseCoordinates(Memorial);
        Assert.Equal("memorial", t.Format);
        Assert.Equal(PerimeterCoordKind.Utm, t.Kind);
        Assert.Equal(DatumId.Sirgas2000, t.Datum);
        Assert.Equal(new[] { "P-01", "P-02", "P-03", "P-04" }, t.Vertices.Select(v => v.Name));
        Assert.Equal(235_172.50, t.Vertices[1].A, 6);
        Assert.Equal(9_299_280.99, t.Vertices[2].B, 6);
        Assert.Equal(4, t.StatedCourses.Count);

        var r = PerimeterText.Build(t.Vertices, t.Kind, DatumId.Sirgas2000, 23, true, close: true, t.StatedCourses);
        Assert.True(r.Closed);
        Assert.Equal(4, r.Segments.Count);
        Assert.Equal(Vec2.Zero, r.Points[0]);
        // Plano local ≈ UTM (fator de escala perto de 1): 360 m² e 84 m com poucos centímetros de diferença.
        Assert.InRange(r.Area, 359.5, 360.5);
        Assert.InRange(r.Perimeter, 83.95, 84.05);
        Assert.Equal(4, r.Checked);
        Assert.Equal(4, r.CheckedOk);
        Assert.All(r.Segments, s => Assert.Equal("plano UTM", s.Basis));
        Assert.Equal(90, r.Segments[0].GridAzimuth!.Value, 6);
        // O azimute verdadeiro difere do plano pela convergência meridiana (pequena aqui).
        Assert.InRange(PerimeterText.AngleDiff(r.Segments[0].Azimuth, 90), 1e-6, 0.5);
    }

    [Fact]
    public void Memorial_WrongDistance_IsFlagged()
    {
        var bad = Memorial.Replace("distância de 12,00 m, até o vértice P-03", "distância de 12,40 m, até o vértice P-03");
        var t = PerimeterText.ParseCoordinates(bad);
        var r = PerimeterText.Build(t.Vertices, t.Kind, DatumId.Sirgas2000, 23, true, true, t.StatedCourses);
        Assert.Equal(3, r.CheckedOk);
        var s = r.Segments[1];
        Assert.False(s.Matches);
        Assert.Equal(0.40, s.DistanceDiff!.Value, 2);
    }

    [Fact]
    public void Sigef_Geographic_DmsWithHemisphere()
    {
        const string sigef =
            "Inicia-se no vértice ABCD-M-0001, de coordenadas Longitude: 47°23'39,086\" W, Latitude: 6°20'01,164\" S e Altitude: 152,34 m; " +
            "deste segue até o vértice ABCD-M-0002, de coordenadas Longitude: -47°23'38,110\", Latitude: -6°20'01,164\" e Altitude: 152,10 m; " +
            "deste segue até o vértice ABCD-P-0003, de coordenadas Longitude: -47°23'38,110\", Latitude: -6°20'01,555\"; " +
            "deste segue até o vértice ABCD-M-0001, de coordenadas Longitude: 47°23'39,086\" W, Latitude: 6°20'01,164\" S.";
        var t = PerimeterText.ParseCoordinates(sigef);
        Assert.Equal(PerimeterCoordKind.Geographic, t.Kind);
        Assert.True(t.ClosedInText);
        Assert.Equal(new[] { "ABCD-M-0001", "ABCD-M-0002", "ABCD-P-0003" }, t.Vertices.Select(v => v.Name));
        Assert.Equal(-6.33365667, t.Vertices[0].A, 6);
        Assert.Equal(-47.39419056, t.Vertices[0].B, 6);
        var r = PerimeterText.Build(t.Vertices, t.Kind, DatumId.Sirgas2000, 0, true, true);
        // 0,976" de longitude ≈ 30 m a leste; 0,391" de latitude ≈ 12 m ao sul.
        Assert.InRange(r.Points[1].X, 29.8, 30.2);
        Assert.InRange(Math.Abs(r.Points[1].Y), 0, 0.01);
        Assert.InRange(r.Points[2].Y, -12.1, -11.9);
        Assert.Equal(3, r.Geodetic.Count);
    }

    [Fact]
    public void Table_Utm_WithHeaderInNEOrder_AndLocalCoordinates()
    {
        var t = PerimeterText.ParseCoordinates("Vértice;N;E\nV1;9299292,99;235142,50\nV2;9299292,99;235172,50\nV3;9299280,99;235172,50\nV4;9299280,99;235142,50\nV1;9299292,99;235142,50");
        Assert.Equal("tabela", t.Format);
        Assert.Equal(PerimeterCoordKind.Utm, t.Kind);
        Assert.True(t.ClosedInText);
        Assert.Equal(4, t.Vertices.Count);
        Assert.Equal(235_142.50, t.Vertices[0].A, 6);

        // Estação total com coordenadas arbitrárias (1000/1000): locais, sem fuso nem datum.
        var loc = PerimeterText.ParseCoordinates("1\t1000,000\t1000,000\n2\t1030,000\t1000,000\n3\t1030,000\t988,000\n4\t1000,000\t988,000");
        Assert.Equal(PerimeterCoordKind.Local, loc.Kind);
        Assert.Equal(new[] { "1", "2", "3", "4" }, loc.Vertices.Select(v => v.Name));
        var r = PerimeterText.Build(loc.Vertices, loc.Kind, DatumId.Sirgas2000, 0, true, true);
        Assert.Equal(360, r.Area, 6);
        Assert.Equal(84, r.Perimeter, 6);
        Assert.Equal(90, r.Segments[0].Azimuth, 9);
        Assert.Empty(r.Geodetic);
    }

    [Fact]
    public void Datum_Sad69_ShiftsPosition_ButNotShape()
    {
        var sad = PerimeterText.ParseCoordinates("Datum SAD 69\n" + Memorial.Replace("datum SIRGAS 2000", "datum SAD 69"));
        Assert.Equal(DatumId.Sad69, sad.Datum);
        var rs = PerimeterText.Build(sad.Vertices, sad.Kind, DatumId.Sad69, 23, true, true);
        var rg = PerimeterText.Build(sad.Vertices, sad.Kind, DatumId.Sirgas2000, 23, true, true);
        // Mesma forma (diferenças milimétricas), posição deslocada dezenas de metros.
        Assert.Equal(rg.Area, rs.Area, 1);
        double shift = (rs.Geodetic[0].LatDeg - rg.Geodetic[0].LatDeg) * 111_320;
        double shiftE = (rs.Geodetic[0].LonDeg - rg.Geodetic[0].LonDeg) * 111_320 * Math.Cos(rs.Geodetic[0].LatDeg * Math.PI / 180);
        Assert.InRange(Math.Sqrt(shift * shift + shiftE * shiftE), 20, 120);
    }

    [Fact]
    public void Arcs_OpenPolyline_And_Warnings()
    {
        var vs = new List<PerimeterVertex>
        {
            new() { Name = "A", A = 0, B = 0 },
            new() { Name = "B", A = 20, B = 0, Radius = 10, ArcToRight = true },
            new() { Name = "C", A = 20, B = 20 },
            new() { Name = "D", A = 0, B = 20 },
        };
        // B→C (indo para o norte) é um semicírculo de raio 10 à direita = para leste, fora do quadrado.
        var r = PerimeterText.Build(vs, PerimeterCoordKind.Local, DatumId.Sirgas2000, 0, true, true);
        Assert.Equal(400 + Math.PI * 100 / 2, r.Area, 3);
        Assert.Equal(60 + Math.PI * 10, r.Perimeter, 3);
        vs[1].ArcToRight = false;   // para dentro
        Assert.Equal(400 - Math.PI * 100 / 2, PerimeterText.Build(vs, PerimeterCoordKind.Local, DatumId.Sirgas2000, 0, true, true).Area, 3);
        // Raio pequeno demais: reta, com aviso.
        vs[1].Radius = 5;
        var r2 = PerimeterText.Build(vs, PerimeterCoordKind.Local, DatumId.Sirgas2000, 0, true, true);
        Assert.Equal(400, r2.Area, 6);
        Assert.Contains(r2.Warnings, w => w.Contains("raio"));
        // UTM sem fuso: erro claro.
        Assert.Throws<InvalidOperationException>(() => PerimeterText.Build(vs, PerimeterCoordKind.Utm, DatumId.Sirgas2000, 0, true, true));
        // Aberto: sem área, um trecho a menos.
        var open = PerimeterText.Build(vs, PerimeterCoordKind.Local, DatumId.Sirgas2000, 0, true, close: false);
        Assert.Equal(3, open.Segments.Count);
        Assert.Equal(0, open.Area);
    }

    [Fact]
    public void AzimuthText_Has_NoCoordinates_And_FirstVertexOnly()
    {
        // O modelo de azimutes não tem coordenadas: nada é lido como vértice.
        var none = PerimeterText.ParseCoordinates("Inicia-se no vértice P-01; deste, segue com azimute de 90°00'00\" e distância de 30,00 m até o vértice P-02;\n" +
                                                  "deste, segue com rumo N 30°00' E e distância de 12,00 m até o vértice P-03.");
        Assert.Empty(none.Vertices);
        Assert.Equal(2, none.StatedCourses.Count);
        // Só o ponto de partida com coordenadas (comum em memoriais antigos).
        var first = PerimeterText.ParseCoordinates("Inicia-se no vértice P-01, de coordenadas E=235.142,50 m e N=9.299.292,99 m; deste, segue com azimute de 90°00'00\" e distância de 30,00 m.");
        Assert.Equal("memorial", first.Format);
        var v = Assert.Single(first.Vertices);
        Assert.Equal("P-01", v.Name);
        Assert.Equal(235_142.50, v.A, 6);
    }

    // Cópia do modelo de texto da aba Coordenadas (PerimeterWindow.CoordTemplateText).
    private const string WindowTemplate =
        "Inicia-se no vértice P-01, de coordenadas N 9.299.292,99 m e E 235.142,50 m;\r\n" +
        "deste, segue com azimute de 90°00'00\" e distância de 30,00 m até o vértice P-02, de coordenadas N 9.299.292,99 m e E 235.172,50 m;\r\n" +
        "deste, segue com azimute de 180°00'00\" e distância de 12,00 m até o vértice P-03, de coordenadas N 9.299.280,99 m e E 235.172,50 m;\r\n" +
        "deste, segue com azimute de 270°00'00\" e distância de 30,00 m até o vértice P-04, de coordenadas N 9.299.280,99 m e E 235.142,50 m;\r\n" +
        "deste, segue com azimute de 0°00'00\" e distância de 12,00 m até o vértice P-01, ponto inicial.\r\n" +
        "Datum SIRGAS 2000, projeção UTM.\r\n" +
        "\r\n" +
        "(Também pode colar linhas de planilha: P-01; 235142,50; 9299292,99 – ou latitude e longitude.)";

    [Fact]
    public void WindowTemplate_ParsesAndConfirms()
    {
        var t = PerimeterText.ParseCoordinates(WindowTemplate);
        Assert.Equal("memorial", t.Format);
        Assert.Equal(4, t.Vertices.Count);
        Assert.Equal(DatumId.Sirgas2000, t.Datum);
        // O texto volta ao P-01 sem repetir a coordenada: quem fecha é a opção "Fechar o polígono".
        Assert.False(t.ClosedInText);
        var r = PerimeterText.Build(t.Vertices, t.Kind, DatumId.Sirgas2000, 23, true, true, t.StatedCourses);
        Assert.Equal(4, r.CheckedOk);
        Assert.Empty(r.Warnings);
    }

    /// <summary>Dados para a simulação da janela (só com GEOPOINT_OUT definido).</summary>
    [Fact]
    public void WriteWindowSample()
    {
        var outPath = Environment.GetEnvironmentVariable("GEOPOINT_OUT");
        if (string.IsNullOrEmpty(outPath)) return;
        var text = WindowTemplate.Replace("distância de 12,00 m até o vértice P-03", "distância de 12,40 m até o vértice P-03");
        var t = PerimeterText.ParseCoordinates(text);
        var r = PerimeterText.Build(t.Vertices, t.Kind, DatumId.Sirgas2000, 23, true, true, t.StatedCourses);
        var fmt = new GeoPoint.Core.Memorial.ValueFormatter(new GeoPoint.Core.Settings.FormatSettings());
        var rows = t.Vertices.Select((v, i) =>
        {
            var sg = r.Segments.FirstOrDefault(x => x.From == i);
            string outp = sg is null ? "" : fmt.Angle(sg.Azimuth) + " – " + fmt.Distance(sg.Distance);
            string chk = sg?.Matches is bool ok && sg.Stated is { } st
                ? ok ? $"✓ confere ({sg.Basis})" : $"⚠ texto: {AngleText.FormatDms(st.AzimuthDeg, 0, AngleKind.Azimuth)} – {GeoPoint.Core.Geodesy.AngleText.FormatNumber(st.Distance, 2)} m  (dif. {AngleText.FormatDms(sg.AzimuthDiffDeg ?? 0, 0)} e {AngleText.FormatNumber(sg.DistanceDiff ?? 0, 2)} m)"
                : "";
            return new { name = v.Name, a = v.A.ToString("0.000", AngleText.PtBr), b = v.B.ToString("0.000", AngleText.PtBr), output = outp, check = chk, bad = sg?.Matches == false };
        }).ToList();
        var data = new
        {
            text, rows, perimeter = AngleText.FormatNumber(r.Perimeter, 3), area = AngleText.FormatNumber(r.Area, 2), ok = r.CheckedOk, total = r.Checked,
            points = r.Points.Select(p => new[] { p.X, p.Y }), names = r.Names, bad = r.Segments.Select(x => x.Matches == false),
        };
        Directory.CreateDirectory(outPath);
        File.WriteAllText(Path.Combine(outPath, "perimetro-janela.json"), System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
}
