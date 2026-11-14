using System.Globalization;
using System.Text.RegularExpressions;
using GeoPoint.Core.Export;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;

namespace GeoPoint.Core.Calc;

/// <summary>Sistema das coordenadas dos vértices.</summary>
public enum PerimeterCoordKind
{
    /// <summary>UTM: E e N em metros (fuso e hemisfério informados).</summary>
    Utm = 0,
    /// <summary>Latitude e longitude (graus decimais ou graus/minutos/segundos).</summary>
    Geographic = 1,
    /// <summary>Coordenadas locais X (leste) e Y (norte) em metros, ex.: levantamento com estação total.</summary>
    Local = 2,
}

/// <summary>Vértice dado por coordenadas.</summary>
public sealed class PerimeterVertex
{
    public string Name { get; set; } = "";
    /// <summary>UTM: E (m); geográficas: latitude (graus); locais: X (m, para leste).</summary>
    public double A { get; set; }
    /// <summary>UTM: N (m); geográficas: longitude (graus); locais: Y (m, para norte).</summary>
    public double B { get; set; }
    /// <summary>Raio (m) do trecho até o próximo vértice, quando o trecho é um arco.</summary>
    public double? Radius { get; set; }
    public bool ArcToRight { get; set; } = true;
    public string Source { get; set; } = "";
}

/// <summary>Coordenadas encontradas num texto (memorial, SIGEF, planilha).</summary>
public sealed class CoordinateTextResult
{
    public List<PerimeterVertex> Vertices { get; } = new();
    public PerimeterCoordKind Kind { get; set; } = PerimeterCoordKind.Utm;
    /// <summary>Datum escrito no texto (null = não informado).</summary>
    public DatumId? Datum { get; set; }
    /// <summary>Azimutes/rumos e distâncias escritos no texto, na ordem (para conferir com as coordenadas).</summary>
    public List<Course> StatedCourses { get; } = new();
    /// <summary>O texto volta ao primeiro vértice no fim (o último vértice repetido foi retirado).</summary>
    public bool ClosedInText { get; set; }
    /// <summary>"memorial" (coordenadas no meio do texto) ou "tabela" (uma linha por vértice).</summary>
    public string Format { get; set; } = "";
    public List<string> Warnings { get; } = new();
}

/// <summary>Trecho do perímetro calculado pelas coordenadas, com a conferência dos valores escritos no texto.</summary>
public sealed class PerimeterSegment
{
    public int From { get; init; }
    public int To { get; init; }
    /// <summary>Azimute verdadeiro e distância horizontal (corda, nos arcos) no plano local – o que o desenho terá.</summary>
    public double Azimuth { get; init; }
    public double Distance { get; init; }
    /// <summary>Azimute e distância planos (UTM), só com coordenadas UTM.</summary>
    public double? GridAzimuth { get; init; }
    public double? GridDistance { get; init; }
    public double? Radius { get; init; }
    /// <summary>Azimute e distância escritos no texto para este trecho.</summary>
    public Course? Stated { get; set; }
    public double? AzimuthDiffDeg { get; set; }
    public double? DistanceDiff { get; set; }
    /// <summary>Base com que o texto bateu: "plano UTM" ou "verdadeiro".</summary>
    public string Basis { get; set; } = "";
    /// <summary>null = sem valor no texto para conferir.</summary>
    public bool? Matches { get; set; }
}

/// <summary>Perímetro pronto para desenhar: pontos no plano local (leste, norte) com o primeiro vértice em (0, 0).</summary>
public sealed class PerimeterResult
{
    public List<Vec2> Points { get; } = new();
    public List<string> Names { get; } = new();
    /// <summary>Coordenadas geodésicas SIRGAS 2000 dos vértices (vazio com coordenadas locais).</summary>
    public List<GeodeticCoord> Geodetic { get; } = new();
    public List<Curve2> Curves { get; } = new();
    public List<PerimeterSegment> Segments { get; } = new();
    public double Area { get; set; }
    public double Perimeter { get; set; }
    public bool Closed { get; set; }
    public int Checked => Segments.Count(s => s.Matches is not null);
    public int CheckedOk => Segments.Count(s => s.Matches == true);
    public List<string> Warnings { get; } = new();
}

/// <summary>
/// Perímetro por coordenadas: lê as coordenadas dos vértices de um texto (memorial descritivo, SIGEF, planilha colada),
/// calcula os trechos no plano local e confere com os azimutes e distâncias escritos no texto.
/// </summary>
public static class PerimeterText
{
    // Número com vírgula ou ponto (aceita separador de milhar); não pode ser seguido de ° (é ângulo, não coordenada).
    private const string NumP = @"(?<v>-?\d[\d.,]*\d|-?\d)(?![\d°º'′])";
    private const string AngP =
        @"(?<v>-?\s*\d{1,3}\s*[°º]\s*\d{1,2}\s*['′’´]\s*\d{1,2}(?:[.,]\d+)?\s*(?:""|″|”|''|’’)?(?:\s*[NSEWLO](?![\p{L}]))?" +
        @"|-?\d{1,3}[.,]\d{3,}\s*[°º]?(?:\s*[NSEWLO](?![\p{L}]))?)";

    private static readonly Regex NorthRx = new(@"(?<![\p{L}\d])(?:N|Y|(?i:norte))(?:\s*\(\s*[YN]\s*\))?(?:\s*[:=]\s*|\s+)" + NumP, RegexOptions.CultureInvariant);
    private static readonly Regex EastRx = new(@"(?<![\p{L}\d])(?:E|X|(?i:leste|este))(?:\s*\(\s*[XE]\s*\))?(?:\s*[:=]\s*|\s+)" + NumP, RegexOptions.CultureInvariant);
    private static readonly Regex LatRx = new(@"(?<![\p{L}])(?i:latitude|lat\.?)\s*[:=]?\s*" + AngP, RegexOptions.CultureInvariant);
    private static readonly Regex LonRx = new(@"(?<![\p{L}])(?i:longitude|long\.?|lon\.?)\s*[:=]?\s*" + AngP, RegexOptions.CultureInvariant);
    private static readonly Regex NameRx = new(@"v[ée]rtices?\s+(?:de\s+)?(?:n[ºo°.]\s*)?(?<n>[A-Za-z0-9][A-Za-z0-9\-_./]*[A-Za-z0-9]|\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private sealed record Tok(int Pos, int End, char Type, double Value);

    /// <summary>Lê as coordenadas dos vértices. Primeiro procura coordenadas no meio do texto (memorial/SIGEF); senão, uma tabela.</summary>
    /// <param name="tableNeedsHeader">
    /// Documento importado (certidão): só aceita tabela de vértices com cabeçalho (Vértice | E | N…). Sem isso, números soltos do
    /// documento (datas, protocolos, valores) virariam vértices.
    /// </param>
    public static CoordinateTextResult ParseCoordinates(string text, bool tableNeedsHeader = false)
    {
        var res = new CoordinateTextResult();
        text ??= "";
        res.Datum = CoordinateTableReader.DetectDatum(text);
        res.StatedCourses.AddRange(MemorialTextParser.Parse(text));

        var memorial = FromMemorial(text, out var memKind);
        var tableWarnings = new List<string>();
        var tableStated = new List<Course>();
        var (table, tableKind) = memorial.Count >= 2 || (tableNeedsHeader && HeaderOrder(text) is null)
            ? (new List<PerimeterVertex>(), PerimeterCoordKind.Utm) : FromTable(text, tableWarnings, tableStated);
        if (memorial.Count >= 2 || (memorial.Count == 1 && table.Count < 2))
        {
            // Coordenadas no meio do texto (memorial, SIGEF). Com uma só, é a do ponto de partida.
            res.Format = "memorial";
            res.Kind = memKind;
            res.Vertices.AddRange(memorial);
        }
        else if (table.Count > 0)
        {
            res.Format = "tabela";
            res.Kind = tableKind;
            res.Vertices.AddRange(table);
            res.Warnings.AddRange(tableWarnings);
            // Azimutes e distâncias das colunas da tabela (quando o texto não os traz por extenso).
            if (tableStated.Count > 0 && res.StatedCourses.Count == 0) res.StatedCourses.AddRange(tableStated);
        }
        Cleanup(res);
        return res;
    }

    /// <summary>Sistema provável de um par de valores (E/N ou latitude/longitude).</summary>
    public static PerimeterCoordKind GuessPlanarKind(IEnumerable<(double E, double N)> pts)
    {
        var l = pts.ToList();
        if (l.Count == 0) return PerimeterCoordKind.Utm;
        return l.All(p => p.E >= 100_000 && p.E <= 900_000 && p.N >= 0 && p.N <= 10_000_000) ? PerimeterCoordKind.Utm : PerimeterCoordKind.Local;
    }

    // ------------------------------------------------------------------ memorial / SIGEF

    private static List<PerimeterVertex> FromMemorial(string text, out PerimeterCoordKind kind)
    {
        var planar = new List<Tok>();
        foreach (Match m in NorthRx.Matches(text)) if (CoordinateTableReader.Num(m.Groups["v"].Value) is double v) planar.Add(new Tok(m.Index, m.Index + m.Length, 'N', v));
        foreach (Match m in EastRx.Matches(text)) if (CoordinateTableReader.Num(m.Groups["v"].Value) is double v) planar.Add(new Tok(m.Index, m.Index + m.Length, 'E', v));
        var geo = new List<Tok>();
        foreach (Match m in LatRx.Matches(text)) if (AngleValue(m.Groups["v"].Value) is double v && Math.Abs(v) <= 90) geo.Add(new Tok(m.Index, m.Index + m.Length, 'A', v));
        foreach (Match m in LonRx.Matches(text)) if (AngleValue(m.Groups["v"].Value) is double v && Math.Abs(v) <= 180) geo.Add(new Tok(m.Index, m.Index + m.Length, 'O', v));
        var names = NameRx.Matches(text).Cast<Match>()
            .Select(m => (Pos: m.Index, Name: m.Groups["n"].Value.TrimEnd('.', '/')))
            .Where(x => x.Name.Any(char.IsDigit)).ToList();

        var p = Pair(planar, 'E', 'N', names);
        var g = Pair(geo, 'A', 'O', names);
        if (g.Count > p.Count)
        {
            kind = PerimeterCoordKind.Geographic;
            return g;
        }
        kind = GuessPlanarKind(p.Select(v => (v.A, v.B)));
        return p;
    }

    /// <summary>Junta os pares (E com N, latitude com longitude) que aparecem perto um do outro, com o nome do vértice citado antes.</summary>
    private static List<PerimeterVertex> Pair(List<Tok> toks, char first, char second, List<(int Pos, string Name)> names)
    {
        var res = new List<PerimeterVertex>();
        Tok? pending = null;
        foreach (var t in toks.OrderBy(t => t.Pos))
        {
            if (pending is not null && pending.Type != t.Type && t.Pos - pending.End < 90)
            {
                var a = pending.Type == first ? pending : t;
                var b = pending.Type == first ? t : pending;
                int at = Math.Min(pending.Pos, t.Pos);
                var nm = names.Where(n => n.Pos < at && at - n.Pos < 220).Select(n => n.Name).LastOrDefault() ?? "";
                res.Add(new PerimeterVertex { Name = nm, A = a.Value, B = b.Value, Source = $"{a.Value.ToString(CultureInfo.InvariantCulture)} / {b.Value.ToString(CultureInfo.InvariantCulture)}" });
                pending = null;
            }
            else pending = t;
        }
        return res;
    }

    /// <summary>Ângulo (graus): decimal ou °'" com hemisfério (S, W, O = negativo).</summary>
    private static double? AngleValue(string s)
    {
        s = s.Trim().Replace('’', '\'').Replace('´', '\'').Replace('′', '\'').Replace('”', '"').Replace('″', '"').Replace("''", "\"");
        bool neg = false;
        var m = Regex.Match(s, @"\s*([NSEWLO])$", RegexOptions.IgnoreCase);
        if (m.Success && !s.Contains('°') && !s.Contains('º'))
        {
            neg = m.Groups[1].Value.ToUpperInvariant() is "S" or "W" or "O";
            s = s[..m.Index];
        }
        s = s.Replace(" ", "");
        var v = CoordinateTableReader.ParseAngle(s.TrimEnd('°', 'º'));
        if (v is null) v = CoordinateTableReader.ParseAngle(s);
        if (v is null) return null;
        return neg ? -Math.Abs(v.Value) : v.Value;
    }

    // ------------------------------------------------------------------ tabela

    private static (List<PerimeterVertex>, PerimeterCoordKind) FromTable(string text, List<string> warnings, List<Course>? stated = null)
    {
        var (rows, _) = CoordinateTableReader.ReadRows(text);
        var res = new List<PerimeterVertex>();
        if (rows.Count < 2) return (res, PerimeterCoordKind.Utm);
        // Tabela de vértices com azimute e distância (ex.: Vértice | Azimute | Dist. | E | N): as coordenadas são as duas últimas
        // colunas numéricas; azimute e distância de cada linha (até o próximo vértice) servem para a conferência.
        static bool IsAngle(string v) => v.Contains('°') || v.Contains('º');
        if (rows.Count(r => r.Values.Count >= 4 && r.Values.Any(IsAngle)) >= Math.Max(2, rows.Count / 2))
        {
            var kept = new List<CoordinateTableReader.Row>();
            var courses = new List<Course>();
            foreach (var r in rows)
            {
                var nums = r.Values.Where(v => !IsAngle(v)).ToList();
                if (nums.Count < 2) continue;
                kept.Add(r with { A = nums[^2], B = nums[^1], C = null });
                int ai = r.Values.ToList().FindIndex(IsAngle);
                if (ai >= 0 && ai + 1 < r.Values.Count && !IsAngle(r.Values[ai + 1])
                    && AngleText.TryParseAngle(r.Values[ai], out double az) && CoordinateTableReader.Num(r.Values[ai + 1]) is double d && d > 0 && d < Math.Min(Math.Abs(CoordinateTableReader.Num(nums[^2]) ?? 0), Math.Abs(CoordinateTableReader.Num(nums[^1]) ?? 0)))
                    courses.Add(new Course { AzimuthDeg = az, Distance = d, Source = $"{r.Name}: {r.Values[ai]} – {r.Values[ai + 1]}" });
            }
            rows = kept;
            if (stated is not null && courses.Count >= 2) { stated.Clear(); stated.AddRange(courses); }
        }
        // Geográficas: ° ou muitas casas decimais (-6,333656). Coordenadas locais pequenas (10,00) não contam como graus.
        static bool GeoLike(string s) => s.Contains('°') || s.Contains('º') || Regex.IsMatch(s.Trim(), @"^-?\d{1,3}[.,]\d{5,}$");
        bool geo = rows.All(r => GeoLike(r.A) && GeoLike(r.B));
        var header = HeaderOrder(text);
        if (geo)
        {
            foreach (var r in rows)
            {
                if (CoordinateTableReader.ParseAngle(r.A) is not double a || CoordinateTableReader.ParseAngle(r.B) is not double b) { warnings.Add($"Linha {r.Line}: coordenadas inválidas."); continue; }
                double lat = a, lon = b;
                if (header == true) (lat, lon) = (b, a);
                else if (header is null && (Math.Abs(a) > 90 || (Math.Abs(b) <= 35 && Math.Abs(a) > 35))) (lat, lon) = (b, a);
                res.Add(new PerimeterVertex { Name = r.Name, A = lat, B = lon, Source = $"{r.A} / {r.B}" });
            }
            return (res, PerimeterCoordKind.Geographic);
        }
        foreach (var r in rows)
        {
            if (CoordinateTableReader.Num(r.A) is not double a || CoordinateTableReader.Num(r.B) is not double b) { warnings.Add($"Linha {r.Line}: coordenadas inválidas."); continue; }
            double e = a, n = b;
            if (header == true) (e, n) = (b, a);
            else if (header is null && a > 1_000_000 && b < 1_000_000) (e, n) = (b, a);   // N antes de E
            res.Add(new PerimeterVertex { Name = r.Name, A = e, B = n, Source = $"{r.A} / {r.B}" });
        }
        return (res, GuessPlanarKind(res.Select(v => (v.A, v.B))));
    }

    /// <summary>true = colunas invertidas no cabeçalho (N antes de E, ou longitude antes de latitude); false = ordem normal; null = sem cabeçalho.</summary>
    private static bool? HeaderOrder(string text)
    {
        foreach (var line in text.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            var words = Regex.Split(line.ToUpperInvariant(), @"[^\p{L}]+").Where(w => w.Length > 0).ToList();
            if (words.Count == 0) continue;
            int iE = words.FindIndex(w => w is "E" or "ESTE" or "LESTE" or "X" or "LONGITUDE" or "LONG" or "LON");
            int iN = words.FindIndex(w => w is "N" or "NORTE" or "Y" or "LATITUDE" or "LAT");
            if (iE < 0 || iN < 0) continue;
            bool lonLat = words.Contains("LATITUDE") || words.Contains("LAT");
            // Planas: o certo é E antes de N. Geográficas: o certo é latitude antes de longitude.
            return lonLat ? iE < iN : iN < iE;
        }
        return null;
    }

    // ------------------------------------------------------------------ limpeza

    private static void Cleanup(CoordinateTextResult res)
    {
        var v = res.Vertices;
        double tol = res.Kind == PerimeterCoordKind.Geographic ? 1e-8 : 0.0005;
        bool Same(PerimeterVertex a, PerimeterVertex b) => Math.Abs(a.A - b.A) <= tol && Math.Abs(a.B - b.B) <= tol;
        for (int i = v.Count - 1; i > 0; i--)
            if (Same(v[i], v[i - 1])) v.RemoveAt(i);
        if (v.Count >= 3 && (Same(v[^1], v[0]) || (v[^1].Name.Length > 0 && v[^1].Name == v[0].Name)))
        {
            if (!Same(v[^1], v[0])) res.Warnings.Add($"O vértice {v[0].Name} aparece de novo no fim com outras coordenadas: ficou valendo a primeira.");
            v.RemoveAt(v.Count - 1);
            res.ClosedInText = true;
        }
        // Nomes repetidos com coordenadas diferentes: o nome não é deste vértice (foi o citado antes).
        for (int i = 1; i < v.Count; i++)
            if (v[i].Name.Length > 0 && v.Take(i).Any(x => x.Name == v[i].Name)) v[i].Name = "";
        for (int i = 0; i < v.Count; i++)
            if (v[i].Name.Length == 0) v[i].Name = NextName(v, i);
    }

    private static string NextName(List<PerimeterVertex> v, int i)
    {
        for (int k = i + 1; ; k++)
        {
            string n = $"P-{k:00}";
            if (!v.Any(x => x.Name == n)) return n;
        }
    }

    // ------------------------------------------------------------------ cálculo

    /// <summary>
    /// Pontos no plano local (leste, norte, m) com o primeiro vértice na origem; trechos (retas ou arcos), área e perímetro;
    /// conferência com os azimutes e distâncias do texto (<paramref name="stated"/>), trecho a trecho, na ordem.
    /// </summary>
    /// <param name="zone">Fuso UTM (só para coordenadas UTM).</param>
    /// <param name="height">Altitude do plano local (m) – a do ponto de referência do projeto.</param>
    public static PerimeterResult Build(IReadOnlyList<PerimeterVertex> vs, PerimeterCoordKind kind, DatumId datum, int zone, bool south,
        bool close, IReadOnlyList<Course>? stated = null, double height = 0)
    {
        var res = new PerimeterResult();
        if (vs.Count < 2) { res.Warnings.Add("Informe pelo menos dois vértices."); return res; }
        if (kind == PerimeterCoordKind.Utm && (zone < 1 || zone > 60)) throw new InvalidOperationException("Informe o fuso UTM (1 a 60).");
        if (kind == PerimeterCoordKind.Local)
        {
            foreach (var v in vs) res.Points.Add(new Vec2(v.A - vs[0].A, v.B - vs[0].B));
        }
        else
        {
            var def = kind == PerimeterCoordKind.Utm ? CrsDef.Utm(datum, zone) : CrsDef.Geographic(datum);
            if (kind == PerimeterCoordKind.Utm) def.UtmSouth = south;
            foreach (var v in vs)
            {
                if (kind == PerimeterCoordKind.Geographic && (Math.Abs(v.A) > 90 || Math.Abs(v.B) > 180))
                    throw new InvalidOperationException($"{v.Name}: latitude/longitude fora do intervalo.");
                var g = CoordinateService.CrsToGeodetic(new CrsCoord(v.A, v.B, kind == PerimeterCoordKind.Geographic), def, 0);
                res.Geodetic.Add(new GeodeticCoord(g.LatDeg, g.LonDeg, height));
            }
            var o = res.Geodetic[0];
            foreach (var g in res.Geodetic)
            {
                var (e, n, _) = Geocentric.ToEnu(o, g, Ellipsoid.GRS80);
                res.Points.Add(new Vec2(e, n));
            }
            // Trecho de vários quilômetros: provavelmente fuso/hemisfério ou coluna trocados.
            double span = res.Points.Max(p => p.Length);
            if (span > 20_000) res.Warnings.Add($"Os vértices ficam a até {span / 1000:0.#} km do primeiro: confira o sistema, o fuso e a ordem das colunas.");
        }
        res.Names.AddRange(vs.Select(v => v.Name));
        res.Closed = close && vs.Count >= 3;
        int count = res.Closed ? vs.Count : vs.Count - 1;
        for (int i = 0; i < count; i++)
        {
            int j = (i + 1) % vs.Count;
            var a = res.Points[i]; var b = res.Points[j];
            if (a.DistanceTo(b) < 1e-6) { res.Warnings.Add($"{vs[i].Name} e {vs[j].Name} estão no mesmo lugar."); continue; }
            var c = Traverse.Segment(a, b, vs[i].Radius, vs[i].ArcToRight);
            if (vs[i].Radius is double r && r * 2 < a.DistanceTo(b))
                res.Warnings.Add($"Trecho {vs[i].Name}–{vs[j].Name}: o raio ({r:0.##} m) é menor que metade da corda; desenhado em reta.");
            res.Curves.Add(c);
            res.Perimeter += c.Length;
            double? gAz = null, gD = null;
            if (kind == PerimeterCoordKind.Utm)
            {
                double dE = vs[j].A - vs[i].A, dN = vs[j].B - vs[i].B;
                gAz = CoordinateService.AzimuthOfEnuVector(new Vec2(dE, dN));
                gD = Math.Sqrt(dE * dE + dN * dN);
            }
            res.Segments.Add(new PerimeterSegment
            {
                From = i, To = j, Azimuth = CoordinateService.AzimuthOfEnuVector(b - a), Distance = a.DistanceTo(b),
                GridAzimuth = gAz, GridDistance = gD, Radius = vs[i].Radius,
            });
        }
        if (res.Closed && res.Curves.Count >= 2)
        {
            double area = 0;
            var o = res.Curves[0].Start;
            foreach (var cv in res.Curves) area += cv.GreenIntegral(o);
            res.Area = Math.Abs(area);
        }
        if (stated is { Count: > 0 }) Check(res, stated);
        return res;
    }

    /// <summary>Diferença entre dois azimutes (graus, 0–180).</summary>
    public static double AngleDiff(double a, double b) => Math.Abs(((a - b) % 360 + 540) % 360 - 180);

    private static void Check(PerimeterResult res, IReadOnlyList<Course> stated)
    {
        if (stated.Count != res.Segments.Count && stated.Any(x => !double.IsNaN(x.AzimuthDeg)))
            res.Warnings.Add($"O texto tem {stated.Count} trecho(s) com azimute e distância e as coordenadas formam {res.Segments.Count}: a conferência compara na ordem.");
        for (int k = 0; k < Math.Min(stated.Count, res.Segments.Count); k++)
        {
            var s = res.Segments[k];
            var st = stated[k];
            if (double.IsNaN(st.Distance) || st.Distance <= 0) continue;
            if (double.IsNaN(st.AzimuthDeg))
            {
                // Só a distância escrita (certidões que descrevem por coordenadas e distâncias, sem azimute).
                double dd = Math.Min(Math.Abs(st.Distance - s.Distance), s.GridDistance is double g0 ? Math.Abs(st.Distance - g0) : double.MaxValue);
                s.Stated = st;
                s.DistanceDiff = dd;
                s.Basis = "distância";
                // Distâncias de certidão costumam ser arredondadas em relação às coordenadas: 10 cm ou 0,5%.
                s.Matches = dd <= Math.Max(0.10, s.Distance * 0.005);
                continue;
            }
            double dTrue = AngleDiff(st.AzimuthDeg, s.Azimuth);
            double dGrid = s.GridAzimuth is double ga ? AngleDiff(st.AzimuthDeg, ga) : double.MaxValue;
            bool grid = dGrid < dTrue;
            double az = Math.Min(dTrue, dGrid);
            double dist = Math.Min(Math.Abs(st.Distance - s.Distance), s.GridDistance is double gd ? Math.Abs(st.Distance - gd) : double.MaxValue);
            // Tolerância: 30" ou o ângulo de 2 cm no trecho (trechos curtos variam mais com o arredondamento das coordenadas).
            double tolAz = Math.Max(30.0 / 3600, Math.Atan2(0.02, Math.Max(s.Distance, 0.01)) * 180 / Math.PI);
            double tolD = Math.Max(0.05, s.Distance * 0.0005);
            s.Stated = st;
            s.AzimuthDiffDeg = az;
            s.DistanceDiff = dist;
            s.Basis = grid ? "plano UTM" : "verdadeiro";
            s.Matches = az <= tolAz && dist <= tolD;
        }
    }
}
