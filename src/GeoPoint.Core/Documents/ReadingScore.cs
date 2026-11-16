using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;

namespace GeoPoint.Core.Documents;

/// <summary>
/// Nota de uma leitura (para escolher a melhor entre as tentativas de OCR): vértices com coordenadas, trechos com azimute e distância,
/// confrontantes, conferência dos trechos com as coordenadas e área do desenho × a do documento.
/// </summary>
public sealed class ReadingScore
{
    public double Score { get; init; }
    /// <summary>A leitura fecha e confere (não precisa tentar outro OCR).</summary>
    public bool Complete { get; init; }
    public string Summary { get; init; } = "";

    public static ReadingScore Of(CertidaoData d)
    {
        double score = 0;
        bool complete = false;
        var parts = new List<string>();
        if (d.HasCoordinates)
        {
            var c = d.Coordinates;
            score += 10 + c.Vertices.Count * 2;
            parts.Add($"{c.Vertices.Count} vértices");
            try
            {
                var r = PerimeterText.Build(c.Vertices, c.Kind, c.Datum ?? DatumId.Sirgas2000, d.UtmZone ?? 23, d.South, true, c.StatedCourses);
                score += r.CheckedOk * 2 - (r.Checked - r.CheckedOk) * 3;
                if (r.Checked > 0) parts.Add($"{r.CheckedOk}/{r.Checked} trechos conferem");
                bool areaOk = d.Area is not double a || a <= 0 || Math.Abs(r.Area - a) / a <= 0.01;
                if (d.Area is double a2 && a2 > 0) { score += areaOk ? 5 : -5; parts.Add(areaOk ? "área confere" : "área não confere"); }
                complete = r.Checked >= Math.Max(3, c.Vertices.Count - 1) && r.CheckedOk == r.Checked && areaOk;
            }
            catch { score -= 5; }
        }
        else if (d.HasCourses)
        {
            score += 8 + d.Courses.Count * 2;
            var t = Traverse.Compute(d.Courses, adjust: false);
            double rel = t.Perimeter > 0 ? t.MisclosureLength / t.Perimeter : 1;
            parts.Add($"{d.Courses.Count} trechos, fechamento {t.MisclosureLength:0.00} m");
            if (rel < 1.0 / 1000) score += 8;
            bool areaOk = d.Area is not double a || a <= 0 || Math.Abs(t.Area - a) / a <= 0.01;
            if (d.Area is double a2 && a2 > 0) score += areaOk ? 5 : -5;
            complete = !d.FromSides && rel < 1.0 / 1000 && areaOk;
        }
        int withPair = d.Trechos.Count(t => t.Azimuth is not null && t.Distance is not null);
        score += withPair * 1.5;
        int confs = d.Confrontantes.Count(x => x.Length > 0);
        score += confs;
        if (confs > 0) parts.Add($"{confs} confrontante(s)");
        if (parts.Count == 0) parts.Add("perímetro não encontrado");
        return new ReadingScore { Score = score, Complete = complete, Summary = string.Join(", ", parts) };
    }
}
