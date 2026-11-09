using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Settings;

namespace GeoPoint.Core.Export;

/// <summary>Exportação para Google Earth (KML/KMZ) e QGIS (GeoJSON / KML).</summary>
public static class GisExport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const double ArcChord = 0.02; // m – densificação de arcos

    private static List<GeodeticPoint> RingToGeo(ProjectContext ctx, Ring ring)
    {
        var pts = new List<GeodeticPoint>();
        foreach (var p in ring.ToPolygon(ArcChord))
        {
            var g = ctx.Coords.ToGeodetic(p) ?? throw new InvalidOperationException("Projeto sem ponto de referência.");
            pts.Add(new GeodeticPoint(g.LatDeg, g.LonDeg));
        }
        if (pts.Count > 0) pts.Add(pts[0]);
        return pts;
    }

    private readonly record struct GeodeticPoint(double Lat, double Lon);

    private static string KmlColor((byte R, byte G, byte B) c, double opacity)
    {
        byte a = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        return $"{a:x2}{c.B:x2}{c.G:x2}{c.R:x2}";
    }

    private static string Coords(IEnumerable<GeodeticPoint> pts) =>
        string.Join(" ", pts.Select(p => string.Create(Inv, $"{p.Lon:0.#########},{p.Lat:0.#########},0")));

    public static string BuildKml(ProjectContext ctx, IReadOnlyList<LotRecord> lots, string documentName)
    {
        if (!ctx.Coords.HasGeoreference) throw new InvalidOperationException("Defina o ponto de referência (Georreferência › Definir Referência) antes de exportar.");
        var ks = ctx.Settings.Kml;
        var fmt = new ValueFormatter(ctx.Settings.Format);
        var sb = new StringBuilder();
        var xs = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
        using (var w = XmlWriter.Create(sb, xs))
        {
            w.WriteStartDocument();
            w.WriteStartElement("kml", "http://www.opengis.net/kml/2.2");
            w.WriteStartElement("Document");
            w.WriteElementString("name", documentName);
            w.WriteElementString("description", "Gerado pelo GeoPoint (Revit). Datum SIRGAS 2000 ≈ WGS 84.");

            foreach (var uso in LotUseInfo.All)
            {
                var col = LotUseInfo.DefaultColor(uso);
                w.WriteStartElement("Style");
                w.WriteAttributeString("id", "uso" + (int)uso);
                w.WriteStartElement("LineStyle");
                w.WriteElementString("color", KmlColor(col, 1));
                w.WriteElementString("width", ks.LineWidth.ToString("0.##", Inv));
                w.WriteEndElement();
                w.WriteStartElement("PolyStyle");
                w.WriteElementString("color", KmlColor(col, ks.FillOpacity));
                w.WriteEndElement();
                w.WriteEndElement();
            }
            w.WriteStartElement("Style");
            w.WriteAttributeString("id", "vertice");
            w.WriteStartElement("IconStyle");
            w.WriteElementString("scale", "0.6");
            w.WriteStartElement("Icon");
            w.WriteElementString("href", "http://maps.google.com/mapfiles/kml/shapes/placemark_circle.png");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("LabelStyle");
            w.WriteElementString("scale", "0.7");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("Style");
            w.WriteAttributeString("id", "segmento");
            w.WriteStartElement("LineStyle");
            w.WriteElementString("color", "ff00ffff");
            w.WriteElementString("width", "3");
            w.WriteEndElement();
            w.WriteEndElement();

            // Lotes agrupados por quadra.
            w.WriteStartElement("Folder");
            w.WriteElementString("name", "Lotes e áreas");
            foreach (var group in lots.GroupBy(l => string.IsNullOrWhiteSpace(l.Quadra) ? "" : l.Quadra).OrderBy(g => g.Key, NaturalComparer.Instance))
            {
                bool folder = group.Key.Length > 0;
                if (folder) { w.WriteStartElement("Folder"); w.WriteElementString("name", "Quadra " + group.Key); }
                foreach (var lot in ProjectContext.SortLots(group))
                {
                    var r = ctx.Report(lot);
                    w.WriteStartElement("Placemark");
                    w.WriteElementString("name", lot.DisplayName);
                    w.WriteStartElement("description");
                    w.WriteCData(LotHtml(lot, r, fmt));
                    w.WriteEndElement();
                    w.WriteElementString("styleUrl", "#uso" + (int)lot.Uso);
                    WriteExtended(w, lot, r);
                    w.WriteStartElement("Polygon");
                    w.WriteElementString("tessellate", "1");
                    w.WriteElementString("altitudeMode", "clampToGround");
                    w.WriteStartElement("outerBoundaryIs"); w.WriteStartElement("LinearRing");
                    w.WriteElementString("coordinates", Coords(RingToGeo(ctx, r.Region.Outer)));
                    w.WriteEndElement(); w.WriteEndElement();
                    foreach (var h in r.Region.Holes)
                    {
                        w.WriteStartElement("innerBoundaryIs"); w.WriteStartElement("LinearRing");
                        w.WriteElementString("coordinates", Coords(RingToGeo(ctx, h)));
                        w.WriteEndElement(); w.WriteEndElement();
                    }
                    w.WriteEndElement(); // Polygon
                    w.WriteEndElement(); // Placemark
                }
                if (folder) w.WriteEndElement();
            }
            w.WriteEndElement();

            if (ks.IncludeVertices)
            {
                w.WriteStartElement("Folder");
                w.WriteElementString("name", "Vértices (pontos de coordenadas)");
                w.WriteElementString("visibility", "1");
                w.WriteElementString("open", "1");
                var seen = new HashSet<string>();
                foreach (var lot in ProjectContext.SortLots(lots))
                {
                    var r = ctx.Report(lot);
                    foreach (var v in r.Vertices)
                    {
                        if (v.Geo is not { } g) continue;
                        string key = ctx.Settings.Naming.Numbering == VertexNumbering.Projeto ? v.Name : lot.Id + v.Name;
                        if (!seen.Add(key)) continue;
                        w.WriteStartElement("Placemark");
                        w.WriteElementString("name", ctx.Settings.Naming.Numbering == VertexNumbering.Projeto ? v.Name : $"{v.Name} ({lot.DisplayName})");
                        var utm = v.Utm;
                        string desc = $"Latitude: {fmt.Lat(g.LatDeg)}<br/>Longitude: {fmt.Lon(g.LonDeg)}" +
                                      (utm is { } u ? $"<br/>UTM {u.ZoneLabel}: E {fmt.Plane(u.East)} m, N {fmt.Plane(u.North)} m" : "");
                        w.WriteStartElement("description"); w.WriteCData(desc); w.WriteEndElement();
                        w.WriteElementString("styleUrl", "#vertice");
                        w.WriteStartElement("Point");
                        w.WriteElementString("coordinates", string.Create(Inv, $"{g.LonDeg:0.#########},{g.LatDeg:0.#########},0"));
                        w.WriteEndElement();
                        w.WriteEndElement();
                    }
                }
                w.WriteEndElement();
            }

            if (ks.IncludeEdges)
            {
                w.WriteStartElement("Folder");
                w.WriteElementString("name", "Segmentos");
                w.WriteElementString("visibility", "0");
                foreach (var lot in ProjectContext.SortLots(lots))
                {
                    var r = ctx.Report(lot);
                    foreach (var s in r.Segments)
                    {
                        var pts = new List<GeodeticPoint>();
                        foreach (var p in s.Curve.Densify(ArcChord))
                        {
                            var g = ctx.Coords.ToGeodetic(p)!.Value;
                            pts.Add(new GeodeticPoint(g.LatDeg, g.LonDeg));
                        }
                        w.WriteStartElement("Placemark");
                        w.WriteElementString("name", $"{lot.DisplayName}: {s.From.Name}–{s.To.Name}");
                        w.WriteStartElement("description");
                        w.WriteCData($"{fmt.AngleWord}: {fmt.Angle(s.AzimuthTrue)}<br/>Distância: {fmt.Distance(s.LengthHorizontal)}<br/>Confrontante: {System.Net.WebUtility.HtmlEncode(s.Confrontante)}");
                        w.WriteEndElement();
                        w.WriteElementString("styleUrl", "#segmento");
                        w.WriteStartElement("LineString");
                        w.WriteElementString("tessellate", "1");
                        w.WriteElementString("coordinates", Coords(pts));
                        w.WriteEndElement();
                        w.WriteEndElement();
                    }
                }
                w.WriteEndElement();
            }

            w.WriteEndElement(); // Document
            w.WriteEndElement(); // kml
            w.WriteEndDocument();
        }
        return sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"UTF-8\"");
    }

    private static void WriteExtended(XmlWriter w, LotRecord lot, LotReport r)
    {
        w.WriteStartElement("ExtendedData");
        void D(string name, string value)
        {
            w.WriteStartElement("Data");
            w.WriteAttributeString("name", name);
            w.WriteElementString("value", value);
            w.WriteEndElement();
        }
        D("lote", lot.Nome);
        D("quadra", lot.Quadra);
        D("uso", LotUseInfo.Label(lot.Uso));
        D("area_m2", r.AreaLocal.ToString("0.00", Inv));
        D("perimetro_m", r.PerimeterLocal.ToString("0.00", Inv));
        D("matricula", lot.Matricula);
        D("proprietario", lot.Proprietario);
        w.WriteEndElement();
    }

    private static string LotHtml(LotRecord lot, LotReport r, ValueFormatter fmt)
    {
        string E(string s) => System.Net.WebUtility.HtmlEncode(s);
        var sb = new StringBuilder("<table>");
        void Row(string k, string v) { if (!string.IsNullOrWhiteSpace(v)) sb.Append($"<tr><td><b>{E(k)}</b></td><td>{E(v)}</td></tr>"); }
        Row("Uso", LotUseInfo.Label(lot.Uso));
        Row("Área", fmt.Area(r.AreaLocal));
        Row("Perímetro", fmt.Distance(r.PerimeterLocal));
        if (r.Testada > 0) Row("Testada", fmt.Distance(r.Testada));
        Row("Matrícula", lot.Matricula);
        Row("Proprietário", lot.Proprietario);
        Row("Endereço", lot.Endereco);
        sb.Append("</table>");
        return sb.ToString();
    }

    public static byte[] BuildKmz(string kml)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var e = zip.CreateEntry("doc.kml", CompressionLevel.Optimal);
            using var s = e.Open();
            var b = new UTF8Encoding(false).GetBytes(kml);
            s.Write(b, 0, b.Length);
        }
        return ms.ToArray();
    }

    /// <summary>GeoJSON (RFC 7946, WGS 84 ≈ SIRGAS 2000) com lotes e vértices.</summary>
    public static string BuildGeoJson(ProjectContext ctx, IReadOnlyList<LotRecord> lots, bool includeVertices)
    {
        if (!ctx.Coords.HasGeoreference) throw new InvalidOperationException("Defina o ponto de referência antes de exportar.");
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("type", "FeatureCollection");
            w.WriteString("name", "GeoPoint");
            w.WriteStartArray("features");
            foreach (var lot in ProjectContext.SortLots(lots))
            {
                var r = ctx.Report(lot);
                w.WriteStartObject();
                w.WriteString("type", "Feature");
                w.WriteStartObject("properties");
                w.WriteString("tipo", "lote");
                w.WriteString("nome", lot.DisplayName);
                w.WriteString("lote", lot.Nome);
                w.WriteString("quadra", lot.Quadra);
                w.WriteString("uso", LotUseInfo.Label(lot.Uso));
                w.WriteNumber("area_m2", Math.Round(r.AreaLocal, 4));
                if (r.AreaUtm is double au) w.WriteNumber("area_utm_m2", Math.Round(au, 4));
                w.WriteNumber("perimetro_m", Math.Round(r.PerimeterLocal, 4));
                w.WriteNumber("testada_m", Math.Round(r.Testada, 4));
                w.WriteString("matricula", lot.Matricula);
                w.WriteString("proprietario", lot.Proprietario);
                w.WriteEndObject();
                w.WriteStartObject("geometry");
                w.WriteString("type", "Polygon");
                w.WriteStartArray("coordinates");
                // RFC 7946: externo anti-horário, furos horários.
                WriteRing(w, RingToGeo(ctx, r.Region.Outer));
                foreach (var h in r.Region.Holes) WriteRing(w, RingToGeo(ctx, h));
                w.WriteEndArray();
                w.WriteEndObject();
                w.WriteEndObject();
            }
            if (includeVertices)
            {
                var seen = new HashSet<string>();
                foreach (var lot in ProjectContext.SortLots(lots))
                {
                    var r = ctx.Report(lot);
                    foreach (var v in r.Vertices)
                    {
                        if (v.Geo is not { } g) continue;
                        string key = ctx.Settings.Naming.Numbering == VertexNumbering.Projeto ? v.Name : lot.Id + v.Name;
                        if (!seen.Add(key)) continue;
                        w.WriteStartObject();
                        w.WriteString("type", "Feature");
                        w.WriteStartObject("properties");
                        w.WriteString("tipo", "vertice");
                        w.WriteString("nome", v.Name);
                        w.WriteString("lote", lot.DisplayName);
                        if (v.Utm is { } u)
                        {
                            w.WriteString("utm_zona", u.ZoneLabel);
                            w.WriteNumber("utm_e", Math.Round(u.East, 4));
                            w.WriteNumber("utm_n", Math.Round(u.North, 4));
                        }
                        w.WriteEndObject();
                        w.WriteStartObject("geometry");
                        w.WriteString("type", "Point");
                        w.WriteStartArray("coordinates");
                        w.WriteNumberValue(Math.Round(g.LonDeg, 9));
                        w.WriteNumberValue(Math.Round(g.LatDeg, 9));
                        w.WriteEndArray();
                        w.WriteEndObject();
                        w.WriteEndObject();
                    }
                }
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteRing(Utf8JsonWriter w, List<GeodeticPoint> pts)
    {
        w.WriteStartArray();
        foreach (var p in pts)
        {
            w.WriteStartArray();
            w.WriteNumberValue(Math.Round(p.Lon, 9));
            w.WriteNumberValue(Math.Round(p.Lat, 9));
            w.WriteEndArray();
        }
        w.WriteEndArray();
    }
}
