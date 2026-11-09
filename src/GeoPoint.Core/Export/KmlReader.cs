using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace GeoPoint.Core.Export;

public sealed record KmlPoint(string Name, double Lat, double Lon, double Alt);

/// <summary>Lê marcadores (pontos) de arquivos KML/KMZ do Google Earth ou QGIS.</summary>
public static class KmlReader
{
    public static List<KmlPoint> ReadPoints(string path)
    {
        string xml;
        if (path.EndsWith(".kmz", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.Entries.FirstOrDefault(e => e.FullName.Equals("doc.kml", StringComparison.OrdinalIgnoreCase))
                        ?? zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException("O arquivo KMZ não contém um KML.");
            using var s = entry.Open();
            using var r = new StreamReader(s);
            xml = r.ReadToEnd();
        }
        else xml = File.ReadAllText(path);
        return ParsePoints(xml);
    }

    public static List<KmlPoint> ParsePoints(string xml)
    {
        var doc = XDocument.Parse(xml);
        var res = new List<KmlPoint>();
        int k = 1;
        foreach (var pm in doc.Descendants().Where(e => e.Name.LocalName == "Placemark"))
        {
            string name = pm.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value?.Trim() ?? "";
            var point = pm.Descendants().FirstOrDefault(e => e.Name.LocalName == "Point");
            if (point is null) continue;
            var coords = point.Descendants().FirstOrDefault(e => e.Name.LocalName == "coordinates")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(coords)) continue;
            var first = coords.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
            var parts = first.Split(',');
            if (parts.Length < 2) continue;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon)) continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)) continue;
            double alt = parts.Length > 2 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double a) ? a : 0;
            if (Math.Abs(lat) > 90 || Math.Abs(lon) > 180) continue;
            res.Add(new KmlPoint(string.IsNullOrWhiteSpace(name) ? $"Ponto {k}" : name, lat, lon, alt));
            k++;
        }
        return res;
    }
}
