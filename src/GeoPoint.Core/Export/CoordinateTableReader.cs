using System.Globalization;
using System.Text.RegularExpressions;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;

namespace GeoPoint.Core.Export;

/// <summary>
/// Lê coordenadas de uma planilha em texto (CSV/TXT, ou colado da área de transferência):
/// "nome; E; N" (UTM), "nome; latitude; longitude" (graus decimais ou graus/minutos/segundos) e variações.
/// </summary>
public static class CoordinateTableReader
{
    public sealed class Result
    {
        public List<KmlPoint> Points { get; } = new();
        public List<string> Warnings { get; } = new();
        /// <summary>"UTM" ou "Geográficas".</summary>
        public string Kind { get; set; } = "";
        /// <summary>Datum das coordenadas lidas (os pontos já saem convertidos para SIRGAS 2000).</summary>
        public DatumId Datum { get; set; } = DatumId.Sirgas2000;
        /// <summary>true = o datum foi escrito na planilha (cabeçalho, título ou código EPSG); false = considerado SIRGAS 2000.</summary>
        public bool DatumFromFile { get; set; }

        /// <summary>Frase para o usuário sobre o datum usado.</summary>
        public string DatumText => DatumFromFile
            ? $"Datum {GeoPoint.Core.Geodesy.Datum.Get(Datum).Name} (informado na planilha)" + (Datum is DatumId.Sad69 or DatumId.CorregoAlegre ? ", convertido para SIRGAS 2000." : ".")
            : "Datum não informado na planilha: considerado SIRGAS 2000. Se for SAD 69 ou Córrego Alegre, escreva o datum no cabeçalho (ex.: \"Datum: SAD 69\") e importe de novo.";
    }

    /// <summary>Datum escrito no texto (nome ou código EPSG); null = não informado. Com mais de um, vale o primeiro.</summary>
    public static DatumId? DetectDatum(string text)
    {
        string t = RemoveAccents(text).ToUpperInvariant();
        var found = new List<(int At, DatumId D)>();
        void Find(string pattern, DatumId d)
        {
            var m = Regex.Match(t, pattern, RegexOptions.CultureInvariant);
            if (m.Success) found.Add((m.Index, d));
        }
        Find(@"\bSAD\s*[-_ ]?\s*69\b|\bSAD69\b|\bSAD\b(?=.{0,3}\b69)", DatumId.Sad69);
        Find(@"CORREGO\s+ALEGRE", DatumId.CorregoAlegre);
        Find(@"\bSIRGAS\b|\bSIRGAS\s*2000\b|SIRGAS2000", DatumId.Sirgas2000);
        Find(@"\bWGS\s*[-_ ]?\s*84\b|\bWGS84\b", DatumId.Wgs84);
        // Códigos EPSG usuais no Brasil.
        Find(@"\bEPSG\s*:?\s*(4674|3198[0-9]|5880)\b", DatumId.Sirgas2000);
        Find(@"\bEPSG\s*:?\s*(4618|2919[0-9]|5530)\b", DatumId.Sad69);
        Find(@"\bEPSG\s*:?\s*(4225|2252[0-9])\b", DatumId.CorregoAlegre);
        Find(@"\bEPSG\s*:?\s*(4326|3272[0-9]|3262[0-9])\b", DatumId.Wgs84);
        return found.Count == 0 ? null : found.OrderBy(f => f.At).First().D;
    }

    private static string RemoveAccents(string s)
    {
        var n = s.Normalize(System.Text.NormalizationForm.FormD);
        return new string(n.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }

    private static readonly Regex Dms = new(@"(-?\d+(?:[.,]\d+)?)\s*[°º]\s*(?:(\d+(?:[.,]\d+)?)\s*['′])?\s*(?:(\d+(?:[.,]\d+)?)\s*(?:""|″|''))?\s*([NSEWLO])?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <param name="utmZone">Fuso para coordenadas UTM (0 = o fuso do projeto).</param>
    /// <param name="south">Hemisfério das coordenadas UTM.</param>
    /// <param name="datum">Datum das coordenadas (null = o escrito na planilha; sem nada escrito, SIRGAS 2000).</param>
    public static Result Parse(string text, int utmZone, bool south, DatumId? datum = null)
    {
        var res = new Result();
        var lines = text.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) return res;
        // Datum: o informado, ou o escrito no texto (cabeçalho/título), ou SIRGAS 2000.
        var detected = datum ?? DetectDatum(string.Join("\n", lines.Where(l => !Regex.IsMatch(l, @"^[\s\d.,;\-+°º'""′″NSEWLO\t|]*$", RegexOptions.IgnoreCase))));
        res.Datum = detected ?? DatumId.Sirgas2000;
        res.DatumFromFile = detected is not null;
        var (rows, warnings) = ReadRows(lines);
        res.Warnings.AddRange(warnings);
        if (rows.Count == 0) return res;
        // UTM: valores grandes (E 100.000–900.000, N até 10.000.000). Geográficas: |valor| ≤ 180 ou com °.
        bool geo = rows.All(r => LooksAngle(r.A) && LooksAngle(r.B));
        res.Kind = geo ? "Geográficas" : "UTM";
        foreach (var r in rows)
        {
            try
            {
                if (geo)
                {
                    double a = Angle(r.A), b = Angle(r.B);
                    // Latitude é a que tem |valor| ≤ 90 (no Brasil, longitudes são -74..-28): se a primeira parecer longitude, troca.
                    double lat = a, lon = b;
                    if (Math.Abs(a) > 90 || (Math.Abs(b) <= 35 && Math.Abs(a) > 35)) (lat, lon) = (b, a);
                    double hh = r.C is { } c && Num(c) is double h ? h : 0;
                    // Converte para SIRGAS 2000 (SAD 69 / Córrego Alegre mudam dezenas de metros).
                    var g = CoordinateService.CrsToGeodetic(new CrsCoord(lat, lon, true), CrsDef.Geographic(res.Datum), 0);
                    res.Points.Add(new KmlPoint(r.Name, g.LatDeg, g.LonDeg, hh));
                }
                else
                {
                    double a = Num(r.A) ?? throw new FormatException(), b = Num(r.B) ?? throw new FormatException();
                    double e = a, n = b;
                    if (a > 1_000_000 && b < 1_000_000) (e, n) = (b, a);   // N antes de E
                    if (utmZone <= 0) throw new InvalidOperationException("Informe o fuso UTM (ou defina a referência do projeto).");
                    var def = CrsDef.Utm(res.Datum, utmZone);
                    def.UtmSouth = south;
                    var g = CoordinateService.CrsToGeodetic(new CrsCoord(e, n, false), def, 0);
                    res.Points.Add(new KmlPoint(r.Name, g.LatDeg, g.LonDeg, r.C is { } c && Num(c) is double h ? h : 0));
                }
            }
            catch (InvalidOperationException) { throw; }
            catch { res.Warnings.Add($"Linha {r.Line}: coordenadas inválidas."); }
        }
        return res;
    }

    /// <summary>Linha de planilha: nome e as coordenadas como texto (ainda sem interpretar).</summary>
    public sealed record Row(string Name, string A, string B, string? C, int Line);

    /// <summary>Linhas com nome e duas (ou três) coordenadas; as demais (cabeçalho, títulos) ficam de fora.</summary>
    public static (List<Row> Rows, List<string> Warnings) ReadRows(string text) =>
        ReadRows(text.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList());

    private static (List<Row> Rows, List<string> Warnings) ReadRows(List<string> lines)
    {
        var rows = new List<Row>();
        var warnings = new List<string>();
        if (lines.Count == 0) return (rows, warnings);
        char sep = DetectSeparator(lines);
        int auto = 1;
        for (int i = 0; i < lines.Count; i++)
        {
            var cells = Split(lines[i], sep);
            // Colunas numéricas (ou ângulos): as duas primeiras são as coordenadas; a primeira não numérica é o nome.
            var coordIdx = cells.Select((c, k) => (c, k)).Where(x => IsCoord(x.c)).Select(x => x.k).ToList();
            if (coordIdx.Count < 2) { if (i > 0) warnings.Add($"Linha {i + 1} ignorada: \"{lines[i]}\"."); continue; }
            string name = cells.Where((c, k) => !coordIdx.Contains(k) && c.Trim().Length > 0).FirstOrDefault()?.Trim() ?? "";
            // Nome numérico (ex.: "1;235142,50;9299292,99"): três números, o primeiro é o nome.
            if (name.Length == 0 && coordIdx.Count >= 3 && int.TryParse(cells[coordIdx[0]].Trim(), out _))
            {
                name = cells[coordIdx[0]].Trim();
                coordIdx.RemoveAt(0);
            }
            if (name.Length == 0) name = $"P-{auto:00}";
            auto++;
            rows.Add(new Row(name, cells[coordIdx[0]], cells[coordIdx[1]], coordIdx.Count > 2 ? cells[coordIdx[2]] : null, i + 1));
        }
        return (rows, warnings);
    }

    /// <summary>Ângulo em graus decimais ou graus/minutos/segundos (com S/W/O = negativo); null = inválido.</summary>
    public static double? ParseAngle(string s)
    {
        try { return Angle(s); } catch { return null; }
    }

    private static char DetectSeparator(List<string> lines)
    {
        var cands = new[] { '\t', ';', ',', '|' };
        foreach (var c in cands)
            if (lines.Take(10).Count(l => l.Contains(c)) >= Math.Min(lines.Count, 2)) return c;
        return ' ';
    }

    private static List<string> Split(string line, char sep) =>
        sep == ' ' ? Regex.Split(line, @"\s+").ToList() : line.Split(sep).Select(x => x.Trim().Trim('"')).ToList();

    private static bool IsCoord(string s) => Num(s) is not null || Dms.IsMatch(s.Trim()) && s.Contains('°');

    public static bool LooksAngle(string s) => s.Contains('°') || (Num(s) is double v && Math.Abs(v) <= 180);

    /// <summary>Número com vírgula ou ponto decimal (aceita separador de milhar).</summary>
    public static double? Num(string s)
    {
        s = s.Trim().Replace(" ", "");
        if (s.Length == 0) return null;
        if (!Regex.IsMatch(s, @"^-?[\d.,]+$")) return null;
        int lastComma = s.LastIndexOf(','), lastDot = s.LastIndexOf('.');
        string norm;
        if (lastComma >= 0 && lastDot >= 0) norm = lastComma > lastDot ? s.Replace(".", "").Replace(',', '.') : s.Replace(",", "");
        else if (lastComma >= 0) norm = s.Count(c => c == ',') > 1 ? s.Replace(",", "") : s.Replace(',', '.');
        else norm = s.Count(c => c == '.') > 1 ? s.Replace(".", "") : s;
        return double.TryParse(norm, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
    }

    private static double Angle(string s)
    {
        if (Num(s) is double v) return v;
        var m = Dms.Match(s);
        if (!m.Success) throw new FormatException();
        double d = Num(m.Groups[1].Value) ?? 0, mi = m.Groups[2].Success ? Num(m.Groups[2].Value) ?? 0 : 0, se = m.Groups[3].Success ? Num(m.Groups[3].Value) ?? 0 : 0;
        double val = Math.Abs(d) + mi / 60 + se / 3600;
        string h = m.Groups[4].Value.ToUpperInvariant();
        if (d < 0 || h is "S" or "W" or "O") val = -val;
        return val;
    }
}
