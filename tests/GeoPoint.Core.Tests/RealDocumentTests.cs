using GeoPoint.Core.Calc;
using GeoPoint.Core.Documents;

namespace GeoPoint.Core.Tests;

/// <summary>
/// Diagnóstico com um documento real (não fica no repositório: tem dados pessoais). Só roda com GEOPOINT_CERT_PDF apontando
/// para o PDF e GEOPOINT_OUT para a pasta onde grava o texto extraído.
/// </summary>
public class RealDocumentTests
{
    [Fact]
    public void DumpRealPdfText()
    {
        var pdf = Environment.GetEnvironmentVariable("GEOPOINT_CERT_PDF");
        var outPath = Environment.GetEnvironmentVariable("GEOPOINT_OUT");
        if (string.IsNullOrEmpty(pdf) || string.IsNullOrEmpty(outPath) || !File.Exists(pdf)) return;
        Directory.CreateDirectory(outPath);
        var doc = PdfTextReader.Read(pdf);
        File.WriteAllText(Path.Combine(outPath, "texto.txt"), doc.Text);
        File.WriteAllText(Path.Combine(outPath, "palavras.txt"), string.Join("\n", doc.Words.Select(w => $"p{w.Page} [{w.Text}] {w.X0:0.0},{w.Y0:0.0}-{w.X1:0.0},{w.Y1:0.0} h={w.Y1 - w.Y0:0.0} w={w.X1 - w.X0:0.0}")));
        File.WriteAllText(Path.Combine(outPath, "paginas.txt"), string.Join("\n", doc.Pages.Select(p => $"{p.Index}: {p.Width}x{p.Height}")) + "\nsem texto: " + string.Join(",", doc.PagesWithoutText));
        var d = CertidaoParser.Parse(doc.Text);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("DESCRIÇÃO: " + d.Description);
        sb.AppendLine($"sistema: {d.Coordinates.Kind} datum={d.Coordinates.Datum} fuso={d.UtmZone} sul={d.South} área={d.Area} perímetro={d.Perimetro} fromSides={d.FromSides}");
        foreach (var v in d.Coordinates.Vertices) sb.AppendLine($"V {v.Name}: {v.A} {v.B}");
        foreach (var t in d.Trechos) sb.AppendLine($"T {t.From} → {t.To} dist={t.Distance} az={t.Azimuth} conf=[{t.Confrontante}]");
        for (int i = 0; i < d.Confrontantes.Count; i++) sb.AppendLine($"C{i}: [{d.Confrontantes[i]}] rua={CertidaoParser.IsStreet(d.Confrontantes[i])}");
        foreach (var c in d.Coordinates.StatedCourses) sb.AppendLine($"S {c.Source} az={c.AzimuthDeg} d={c.Distance}");
        foreach (var c in d.Courses) sb.AppendLine($"K {c.Source} az={c.AzimuthDeg} d={c.Distance}");
        foreach (var w in d.Warnings.Concat(d.Coordinates.Warnings)) sb.AppendLine("! " + w);
        if (d.HasCoordinates)
        {
            var r = PerimeterText.Build(d.Coordinates.Vertices, d.Coordinates.Kind, d.Coordinates.Datum ?? GeoPoint.Core.Geodesy.DatumId.Sirgas2000,
                d.UtmZone ?? 23, d.South, true, d.Coordinates.StatedCourses);
            sb.AppendLine($"área calc={r.Area:0.00} perímetro calc={r.Perimeter:0.00}");
            foreach (var s in r.Segments) sb.AppendLine($"seg {s.From}→{s.To} d={s.Distance:0.00} grid={s.GridDistance:0.00} az={s.Azimuth:0.0000} stated={s.Stated?.Distance} ok={s.Matches} {s.Basis}");
            foreach (var w in r.Warnings) sb.AppendLine("!! " + w);
        }
        File.WriteAllText(Path.Combine(outPath, "certidao.txt"), sb.ToString());
        // O modelo comum: quatro vértices, um confrontante por divisa e a área do desenho perto da do documento.
        Assert.True(d.HasCoordinates);
        Assert.Equal(d.Coordinates.Vertices.Count, d.Confrontantes.Count);
        Assert.All(d.Confrontantes, c => Assert.False(string.IsNullOrWhiteSpace(c)));
        if (d.Area is double area)
        {
            var r = PerimeterText.Build(d.Coordinates.Vertices, d.Coordinates.Kind, d.Coordinates.Datum ?? GeoPoint.Core.Geodesy.DatumId.Sirgas2000,
                d.UtmZone ?? 23, d.South, true, d.Coordinates.StatedCourses);
            Assert.InRange(Math.Abs(r.Area - area) / area, 0, 0.01);
        }
    }
}
