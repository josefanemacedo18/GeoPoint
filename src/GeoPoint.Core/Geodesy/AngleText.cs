using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GeoPoint.Core.Geodesy;

public enum AngleKind { Azimuth, Latitude, Longitude, Plain }

/// <summary>Formatação e interpretação de ângulos, coordenadas e números (padrão brasileiro).</summary>
public static class AngleText
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Decompõe graus decimais em graus, minutos e segundos com arredondamento correto.</summary>
    public static (int Deg, int Min, double Sec) ToDms(double absDeg, int secDecimals)
    {
        absDeg = Math.Abs(absDeg);
        double totalSec = Math.Round(absDeg * 3600.0, secDecimals, MidpointRounding.AwayFromZero);
        int deg = (int)Math.Floor(totalSec / 3600.0 + 1e-12);
        double rem = totalSec - deg * 3600.0;
        int min = (int)Math.Floor(rem / 60.0 + 1e-12);
        double sec = rem - min * 60.0;
        if (sec < 0) sec = 0;
        sec = Math.Round(sec, secDecimals, MidpointRounding.AwayFromZero);
        if (sec >= 60) { sec -= 60; min++; }
        if (min >= 60) { min -= 60; deg++; }
        return (deg, min, sec);
    }

    public static string FormatDms(double deg, int secDecimals, AngleKind kind = AngleKind.Plain, bool westAsO = true, bool padDegrees = false)
    {
        double value = deg;
        if (kind == AngleKind.Azimuth)
        {
            value %= 360.0;
            if (value < 0) value += 360.0;
        }
        var (d, m, s) = ToDms(value, secDecimals);
        if (kind == AngleKind.Azimuth && d >= 360) d -= 360;
        string secFmt = secDecimals > 0 ? "00." + new string('0', secDecimals) : "00";
        string degStr = padDegrees ? d.ToString(kind == AngleKind.Longitude || kind == AngleKind.Azimuth ? "000" : "00", PtBr) : d.ToString(PtBr);
        var sb = new StringBuilder();
        sb.Append(degStr).Append('°').Append(m.ToString("00", PtBr)).Append('\'').Append(s.ToString(secFmt, PtBr)).Append('"');
        switch (kind)
        {
            case AngleKind.Latitude: sb.Append(deg < 0 ? " S" : " N"); break;
            case AngleKind.Longitude: sb.Append(deg < 0 ? (westAsO ? " O" : " W") : " E"); break;
            case AngleKind.Plain: if (deg < 0) sb.Insert(0, '-'); break;
        }
        return sb.ToString();
    }

    /// <summary>Converte azimute (graus) em rumo: (ângulo 0–90°, quadrante "NE", "SE", "SO"/"SW", "NO"/"NW").</summary>
    public static (double Angle, string Quadrant) AzimuthToBearing(double az, bool westAsO = true)
    {
        az %= 360.0;
        if (az < 0) az += 360.0;
        string w = westAsO ? "O" : "W";
        if (az <= 90) return (az, "NE");
        if (az <= 180) return (180 - az, "SE");
        if (az <= 270) return (az - 180, "S" + w);
        return (360 - az, "N" + w);
    }

    public static string FormatBearing(double az, int secDecimals, bool westAsO = true)
    {
        var (ang, q) = AzimuthToBearing(az, westAsO);
        return FormatDms(ang, secDecimals) + " " + q;
    }

    public static string FormatNumber(double v, int decimals, bool thousands = true)
    {
        string fmt = (thousands ? "#,##0" : "0") + (decimals > 0 ? "." + new string('0', decimals) : "");
        double r = Math.Round(v, decimals, MidpointRounding.AwayFromZero);
        if (r == 0) r = 0; // evita "-0,00"
        return r.ToString(fmt, PtBr);
    }

    /// <summary>
    /// Interpreta número aceitando vírgula ou ponto decimal e separadores de milhar (ex.: "9.299.316,16", "235096.80").
    /// </summary>
    public static bool TryParseNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string s = text.Trim().Replace(" ", "").Replace("\u00A0", "").Replace("m", "").Replace("M", "");
        s = s.Replace('−', '-');
        if (s.Length == 0) return false;
        int lastComma = s.LastIndexOf(',');
        int lastDot = s.LastIndexOf('.');
        if (lastComma >= 0 && lastDot >= 0)
        {
            char dec = lastComma > lastDot ? ',' : '.';
            char th = dec == ',' ? '.' : ',';
            s = s.Replace(th.ToString(), "").Replace(dec, '.');
        }
        else if (lastComma >= 0)
        {
            if (s.Count(c => c == ',') > 1) s = s.Replace(",", ""); else s = s.Replace(',', '.');
        }
        else if (lastDot >= 0 && s.Count(c => c == '.') > 1)
        {
            s = s.Replace(".", "");
        }
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    private static readonly Regex AngleRx = new(
        @"^\s*(?<h1>[NSEWLO])?\s*(?<sign>[-+])?\s*(?<d>\d+(?:[.,]\d+)?)\s*(?:[°ºd:\s]\s*(?:(?<m>\d+(?:[.,]\d+)?)\s*(?:['′’m:\s]\s*(?:(?<s>\d+(?:[.,]\d+)?)\s*(?:""|″|”|''|’’|′′|s)?)?)?)?)?\s*(?<h2>[NSEWLO])?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Interpreta um ângulo em graus decimais ou GMS (ex.: "-22.9068", "22°54'24.6\"S", "22 54 24,6 S", "43°10,37' O").
    /// Hemisférios S, W e O resultam em valor negativo.
    /// </summary>
    public static bool TryParseAngle(string? text, out double degrees)
    {
        degrees = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text.Trim().Replace('−', '-');
        var m = AngleRx.Match(t);
        if (!m.Success) return false;
        if (!TryParseNumber(m.Groups["d"].Value, out double d)) return false;
        double min = 0, sec = 0;
        if (m.Groups["m"].Success && !TryParseNumber(m.Groups["m"].Value, out min)) return false;
        if (m.Groups["s"].Success && !TryParseNumber(m.Groups["s"].Value, out sec)) return false;
        if (min >= 60 || sec >= 60) return false;
        if (m.Groups["m"].Success && d != Math.Floor(d)) return false;
        double v = d + min / 60.0 + sec / 3600.0;
        bool neg = m.Groups["sign"].Value == "-";
        string h = (m.Groups["h1"].Success ? m.Groups["h1"].Value : m.Groups["h2"].Value).ToUpperInvariant();
        // Hemisférios Sul e Oeste (W/O) são negativos; Norte e Leste (E/L) positivos.
        if (h is "S" or "W" or "O") neg = true;
        degrees = neg ? -v : v;
        return true;
    }

    /// <summary>
    /// Interpreta um par latitude/longitude copiado do Google Maps, Google Earth, QGIS ou GPS.
    /// Aceita "lat, lon" em decimal (ponto ou vírgula) e GMS com hemisférios.
    /// </summary>
    public static bool TryParseLatLon(string? text, out double lat, out double lon)
    {
        lat = lon = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text.Trim().Replace('−', '-').Replace("\t", " ");

        var candidates = new List<(string a, string b)>();
        // Separado por ponto e vírgula ou barra.
        foreach (var sep in new[] { ';', '|', '/' })
        {
            var parts = t.Split(sep);
            if (parts.Length == 2) candidates.Add((parts[0], parts[1]));
        }
        // GMS com hemisférios: separa após a letra do primeiro hemisfério.
        var hm = Regex.Match(t, @"^(?<a>.*?[0-9""″”'’]\s*[NS])\s*[,;]?\s*(?<b>.+)$", RegexOptions.IgnoreCase);
        if (hm.Success) candidates.Add((hm.Groups["a"].Value, hm.Groups["b"].Value));
        var hm2 = Regex.Match(t, @"^(?<a>[NS].*?)\s*[,;]?\s*(?<b>[EWLO].+)$", RegexOptions.IgnoreCase);
        if (hm2.Success) candidates.Add((hm2.Groups["a"].Value, hm2.Groups["b"].Value));
        // Vírgulas.
        int commas = t.Count(c => c == ',');
        if (commas == 1)
        {
            var parts = t.Split(',');
            candidates.Add((parts[0], parts[1]));
        }
        else if (commas == 3)
        {
            int idx = NthIndex(t, ',', 2);
            candidates.Add((t[..idx], t[(idx + 1)..]));
        }
        // Espaço simples entre dois números decimais.
        var ws = Regex.Split(t.Replace(",", " ").Trim(), @"\s+");
        if (ws.Length == 2) candidates.Add((ws[0], ws[1]));
        if (commas == 2)
        {
            // "-22,90 -43,17" com vírgula decimal e separador por espaço
            var ws2 = Regex.Split(t.Trim(), @"\s+");
            if (ws2.Length == 2) candidates.Add((ws2[0], ws2[1]));
        }

        foreach (var (a, b) in candidates)
        {
            if (TryParseAngle(a, out double la) && TryParseAngle(b, out double lo) && Math.Abs(la) <= 90 && Math.Abs(lo) <= 180)
            {
                // Se a primeira parte tem hemisfério E/W, inverte.
                if (Regex.IsMatch(a, "[EWLO]\\s*$|^\\s*[EWLO]", RegexOptions.IgnoreCase) && Regex.IsMatch(b, "[NS]\\s*$|^\\s*[NS]", RegexOptions.IgnoreCase))
                    (la, lo) = (lo, la);
                lat = la; lon = lo;
                return true;
            }
        }
        return false;
    }

    private static int NthIndex(string s, char c, int n)
    {
        int count = 0;
        for (int i = 0; i < s.Length; i++) if (s[i] == c && ++count == n) return i;
        return -1;
    }
}
