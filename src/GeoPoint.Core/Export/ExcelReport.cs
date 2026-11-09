using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Office;
using GeoPoint.Core.Settings;
using GeoPoint.Core.Urban;

namespace GeoPoint.Core.Export;

/// <summary>Planilha com quadro de áreas, vértices (todas as coordenadas) e segmentos.</summary>
public static class ExcelReport
{
    public static byte[] Build(ProjectContext ctx, IReadOnlyList<LotRecord> lots)
    {
        var fmt = new ValueFormatter(ctx.Settings.Format);
        var sheets = new List<XlSheet>();
        var sorted = ProjectContext.SortLots(lots).ToList();
        bool geo = ctx.Coords.HasGeoreference;

        // Quadro de lotes.
        var q = new XlSheet("Lotes");
        q.AddHeader("Lote", "Quadra", "Uso", "Área local (m²)", "Área UTM (m²)", "Área (ha)", "Perímetro (m)", "Testada (m)",
            "Área líquida (m²)", "Área edificável (m²)", "Área superficial (m²)", "Área do título (m²)", "Diferença (m²)", "Matrícula", "Proprietário", "Situação");
        var quadro = QuadroAreas.Build(ctx);
        foreach (var lot in sorted)
        {
            var r = ctx.Report(lot);
            var line = quadro.Lotes.FirstOrDefault(x => x.Lot.Id == lot.Id);
            q.AddRow(lot.Nome, lot.Quadra, LotUseInfo.Label(lot.Uso),
                new XlCell(r.AreaLocal, XlStyle.Num2),
                r.AreaUtm is double au ? new XlCell(au, XlStyle.Num2) : new XlCell("–"),
                new XlCell(r.AreaLocal / 10000, XlStyle.Num4),
                new XlCell(r.PerimeterLocal, XlStyle.Num2),
                new XlCell(r.Testada, XlStyle.Num2),
                new XlCell(r.AreaLiquida, XlStyle.Num2),
                lot.AreaEdificavel is double ae ? new XlCell(ae, XlStyle.Num2) : new XlCell("–"),
                lot.AreaSuperficial is double asup ? new XlCell(asup, XlStyle.Num2) : new XlCell("–"),
                lot.AreaTitulo is double at ? new XlCell(at, XlStyle.Num2) : new XlCell("–"),
                lot.AreaTitulo is double at2 ? new XlCell(r.AreaLocal - at2, XlStyle.Num2) : new XlCell("–"),
                lot.Matricula, lot.Proprietario,
                line is null ? (lot.Status == LotStatus.Arquivado ? "Arquivado" : "") : (line.Problemas.Count == 0 ? "OK" : string.Join(" | ", line.Problemas)));
        }
        sheets.Add(q);

        // Quadro de áreas por uso.
        var qa = new XlSheet("Quadro de áreas") { AutoFilter = false };
        qa.AddHeader("Especificação", "Quantidade", "Área (m²)", "%");
        foreach (var c in quadro.Categorias) qa.AddRow(c.Categoria, new XlCell(c.Quantidade, XlStyle.Int), new XlCell(c.Area, XlStyle.Num2), new XlCell(c.Percentual, XlStyle.Num2));
        qa.AddRow(new XlCell("TOTAL", XlStyle.Bold), new XlCell(quadro.Categorias.Sum(c => c.Quantidade), XlStyle.Int), new XlCell(quadro.AreaTotal, XlStyle.Num2), null);
        if (quadro.AreaGleba is double ag) qa.AddRow(new XlCell("Área da gleba", XlStyle.Bold), null, new XlCell(ag, XlStyle.Num2), null);
        foreach (var v in quadro.Verificacoes) qa.AddRow(new XlCell("OK: " + v));
        foreach (var p in quadro.Problemas) qa.AddRow(new XlCell("Verificar: " + p));
        sheets.Add(qa);

        // Vértices.
        var vs = new XlSheet("Vértices");
        var hdr = new List<string> { "Lote", "Vértice", "X desenho (m)", "Y desenho (m)", "E local (m)", "N local (m)" };
        if (geo) hdr.AddRange(new[] { "Latitude (GMS)", "Longitude (GMS)", "Latitude (°)", "Longitude (°)", "UTM zona", "UTM E (m)", "UTM N (m)", "Convergência (°)", "Fator de escala" });
        hdr.Add("Altitude (m)");
        vs.AddHeader(hdr.ToArray());
        foreach (var lot in sorted)
        {
            var r = ctx.Report(lot);
            foreach (var v in r.Vertices)
            {
                var row = new List<XlCell?>
                {
                    lot.DisplayName, v.Name,
                    new XlCell(v.Internal.X, XlStyle.Num3), new XlCell(v.Internal.Y, XlStyle.Num3),
                    new XlCell(v.Enu.X, XlStyle.Num3), new XlCell(v.Enu.Y, XlStyle.Num3),
                };
                if (geo)
                {
                    if (v.Geo is { } g)
                    {
                        row.Add(fmt.Lat(g.LatDeg)); row.Add(fmt.Lon(g.LonDeg));
                        row.Add(new XlCell(g.LatDeg, XlStyle.Num8)); row.Add(new XlCell(g.LonDeg, XlStyle.Num8));
                    }
                    else { row.Add("–"); row.Add("–"); row.Add("–"); row.Add("–"); }
                    if (v.Utm is { } u)
                    {
                        row.Add(u.ZoneLabel); row.Add(new XlCell(u.East, XlStyle.Num3)); row.Add(new XlCell(u.North, XlStyle.Num3));
                        row.Add(new XlCell(u.ConvergenceDeg, XlStyle.Num8)); row.Add(new XlCell(u.Scale, XlStyle.Num8));
                    }
                    else { row.Add("–"); row.Add("–"); row.Add("–"); row.Add("–"); row.Add("–"); }
                }
                row.Add(v.Elevation is double z ? new XlCell(z, XlStyle.Num3) : new XlCell("–"));
                vs.Rows.Add(row);
            }
        }
        sheets.Add(vs);

        // Segmentos.
        var ss = new XlSheet("Segmentos");
        ss.AddHeader("Lote", "De", "Para", "Tipo", "Azimute verdadeiro", "Rumo", "Azimute UTM", "Azimute geodésico", "Distância horizontal (m)",
            "Distância UTM (m)", "Distância geodésica (m)", "Raio (m)", "Ângulo central", "Corda (m)", "Confrontante", "Testada");
        foreach (var lot in sorted)
        {
            var r = ctx.Report(lot);
            foreach (var s in r.Segments)
            {
                ss.AddRow(lot.DisplayName, s.From.Name, s.To.Name, s.IsArc ? "Arco" : "Reta",
                    AngleText.FormatDms(s.AzimuthTrue, ctx.Settings.Format.AzimuthSecondsDecimals, AngleKind.Azimuth),
                    AngleText.FormatBearing(s.AzimuthTrue, ctx.Settings.Format.AzimuthSecondsDecimals, ctx.Settings.Format.WestAsO),
                    s.AzimuthGrid is double ag2 ? AngleText.FormatDms(ag2, ctx.Settings.Format.AzimuthSecondsDecimals, AngleKind.Azimuth) : "–",
                    s.AzimuthGeodesic is double agd ? AngleText.FormatDms(agd, ctx.Settings.Format.AzimuthSecondsDecimals, AngleKind.Azimuth) : "–",
                    new XlCell(s.LengthHorizontal, XlStyle.Num3),
                    s.LengthUtm is double lu ? new XlCell(lu, XlStyle.Num3) : new XlCell("–"),
                    s.LengthGeodesic is double lg ? new XlCell(lg, XlStyle.Num3) : new XlCell("–"),
                    s.Radius is double rad ? new XlCell(rad, XlStyle.Num3) : new XlCell("–"),
                    s.CentralAngleDeg is double ca ? AngleText.FormatDms(ca, 2) : "–",
                    new XlCell(s.ChordHorizontal, XlStyle.Num3),
                    s.Confrontante, s.Testada ? "Sim" : "");
            }
        }
        sheets.Add(ss);
        return XlsxWriter.Build(sheets);
    }
}
