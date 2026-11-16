using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Export;

namespace GeoPoint.Core.Documents;

/// <summary>Modelo de texto esperado na leitura. Automático identifica sozinho; os outros obrigam a leitura a seguir o modelo.</summary>
public enum ReadingModel
{
    Auto = 0,
    /// <summary>Coordenadas UTM (E, N em metros), em memorial ou tabela.</summary>
    Utm = 1,
    /// <summary>Coordenadas geográficas (latitude e longitude), SIGEF ou tabela.</summary>
    Geographic = 2,
    /// <summary>Coordenadas locais X, Y (estação total).</summary>
    Local = 3,
    /// <summary>Só azimutes e distâncias (as coordenadas do texto são ignoradas, menos a do ponto inicial).</summary>
    Courses = 4,
    /// <summary>Rumos e distâncias (os ângulos dos trechos são rumos: NE, SE, SO, NO).</summary>
    Bearings = 5,
}

/// <summary>Papel de um número no texto.</summary>
public enum TokenRole { None, East, North, Lat, Lon, Azimuth, Distance, Area, Ignore }

/// <summary>Número ou ângulo encontrado no texto, com o papel que a leitura deu a ele.</summary>
public sealed class ReadToken
{
    public int Start { get; init; }
    public int End { get; set; }
    public string Raw { get; init; } = "";
    public bool IsAngle { get; init; }
    public double Value { get; set; }
    /// <summary>Outra leitura possível do número (ex.: "236.184" = 236184 com ponto de milhar).</summary>
    public double? Alt { get; init; }
    public int Decimals { get; init; }
    /// <summary>Passo da última casa escrita: em metros (números) ou em graus (ângulos). Define o arredondamento do valor.</summary>
    public double Step { get; set; } = 1;
    /// <summary>Segundos com casas decimais (ângulos de coordenada costumam ter).</summary>
    public int SecDecimals { get; init; }
    /// <summary>Letra do hemisfério depois do ângulo (N, S, E, W).</summary>
    public string Hemi { get; set; } = "";
    /// <summary>Quadrante do rumo (NE, SE, SW, NW).</summary>
    public string Quadrant { get; set; } = "";
    public bool Negative { get; init; }
    public int Line { get; init; }
    /// <summary>Rótulo encontrado perto: N, E, LAT, LON, AZ, RUMO, DIST, AREA, ALT, PERIM.</summary>
    public string Label { get; set; } = "";
    /// <summary>Unidade escrita depois: m, m², ha.</summary>
    public string Unit { get; set; } = "";
    public TokenRole Role { get; set; }
    public bool Used { get; set; }
    public override string ToString() => $"{Raw} [{Role}{(Label.Length > 0 ? " " + Label : "")}]";
}

/// <summary>Resultado da leitura inteligente: vértices (coordenadas), trechos (azimute e distância) e o modelo identificado.</summary>
public sealed class SmartReading
{
    public CoordinateTextResult Coordinates { get; } = new();
    /// <summary>Trechos na ordem do texto. Azimute ou distância ilegível = NaN (para a conferência calcular pelo fechamento).</summary>
    public List<Course> Courses { get; } = new();
    /// <summary>Posição de cada trecho no texto (início, fim), para achar o confrontante.</summary>
    public List<(int Start, int End)> CourseSpans { get; } = new();
    /// <summary>Modelo identificado, em palavras ("Memorial com coordenadas UTM (N e E)").</summary>
    public string Model { get; set; } = "";
    /// <summary>Como foi identificado (rótulos, faixas de valores, cabeçalho da tabela…).</summary>
    public List<string> Evidence { get; } = new();
    public List<ReadToken> Tokens { get; } = new();
    public bool Table { get; set; }
    /// <summary>Texto preparado (normalizado) em que as posições dos números valem.</summary>
    public string Text { get; set; } = "";
    /// <summary>Área escrita no documento (m²), para a conferência.</summary>
    public double? Area { get; set; }
    /// <summary>Passo da última casa da área escrita (m²).</summary>
    public double AreaStep { get; set; } = 0.01;
    /// <summary>Avisos sobre os trechos (os das coordenadas ficam em <see cref="Coordinates"/>).</summary>
    public List<string> Warnings { get; } = new();
    /// <summary>Correções feitas pela conferência cruzada.</summary>
    public List<ReadingFix> Fixes { get; } = new();
    public bool HasCoordinates => Coordinates.Vertices.Count >= 3;
    public bool HasCourses => Courses.Count(c => !double.IsNaN(c.AzimuthDeg) && !double.IsNaN(c.Distance)) >= 3;
}

/// <summary>
/// Leitura "inteligente" de coordenadas, azimutes/rumos e distâncias, em qualquer modelo de memorial ou tabela. Cada número do texto
/// recebe um papel pelo que está em volta (rótulos antes ou depois: "N", "E", "Este", "Latitude", "azimute", "rumo", "distância",
/// "m", "m²"; o cabeçalho da coluna numa tabela) e pela faixa de valores (N de UTM tem 7 dígitos, E tem 6; latitude no Brasil entre
/// −34° e +6°, longitude entre −74° e −28°). Depois junta os pares de coordenadas de cada vértice (com o nome citado antes) e os
/// azimutes com as distâncias de cada trecho. Números de documentos, datas, CPF, CEP, "SIRGAS 2000", fuso e meridiano são ignorados.
/// </summary>
public static class SmartReader
{
    private const RegexOptions Opt = RegexOptions.CultureInvariant;
    private const RegexOptions OptI = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    // ------------------------------------------------------------------ preparo do texto

    /// <summary>Limpeza dos erros de OCR (a mesma da leitura de documentos) e números partidos na quebra de linha.</summary>
    public static string Prepare(string text)
    {
        string s = OcrCleanup.Normalize(text ?? "");
        s = s.Replace('\t', ' ').Replace('\u00A0', ' ');
        // Número partido no fim da linha ("729.830,\n10m", "9.299.\n292,99").
        s = Regex.Replace(s, @"(?<=\d[.,])[ ]*\n[ ]*(?=\d)", "");
        // Espaço depois da vírgula decimal ("729.830, 10m", "9°09'48, 800"").
        s = Regex.Replace(s, @"(?<=\d{1,3}(?:\.\d{3})+),[ ]+(?=\d{1,3}(?![\d.,]))", ",");
        s = Regex.Replace(s, @"(?<=\d{1,3}\s*[°º]\s*\d{1,2}\s*'\s*\d{1,2}),[ ]+(?=\d+\s*"")", ",");
        // Número longo (coordenada) com espaço depois da vírgula ("8295860, 18 mN", "1000, 000") e número seguido de unidade ou
        // símbolo ("120, 6839°", "29, 96 m").
        s = Regex.Replace(s, @"(?<=(?<![\d.,])\d{4,}),[ ]+(?=\d{2,3}(?![\d.,]))", ",");
        s = Regex.Replace(s, @"(?<=\d),[ ]+(?=\d{1,4}\s*(?:°|º|""|m\b|m[EN]\b|metros))", ",");
        s = Regex.Replace(s, @"(?<=(?<![\d.,])\d{3}),[ ]+(?=\d{3}(?:[ \t]|$))", ",", RegexOptions.Multiline);
        // Coordenada partida na quebra de linha depois do rótulo ("N 8\n828 671,65", "E 273\n752,37").
        s = Regex.Replace(s, @"(?<=(?:\b[NE]|(?i:norte|este|leste))\s*\d{1,3}(?: \d{3})*)[ ]*\n[ ]*(?=\d{3}[ ,.])", " ");
        // Ponto decimal com espaço no formato americano ("619,938. 87").
        s = Regex.Replace(s, @"(?<=\d{1,3},\d{3})\.[ ]+(?=\d{1,3}(?![\d.,]))", ".");
        // Letras no lugar de dígitos nos minutos e segundos ("82° os' 34"" → 82°05'34"): o → 0, s → 5, l/I → 1.
        s = Regex.Replace(s, @"(?<=\d{1,3}\s*[°º]\s*)([0-9oOsSlI]{2})(?=\s*[!'|]\s*\d)", m =>
            m.Value.Any(char.IsLetter) ? new string(m.Value.Select(c => c switch { 'o' or 'O' => '0', 's' or 'S' => '5', 'l' or 'I' => '1', _ => c }).ToArray()) : m.Value);
        // Apóstrofo dos minutos lido como "!" ou "|" ("82º 05! 34"") e marca dos minutos perdida ("24°2701,840"").
        s = Regex.Replace(s, @"(?<=\d{1,3}\s*[°º]\s*\d{1,2})\s*[!|]\s*(?=\d{1,2}(?:[.,]\d+)?\s*"")", "'");
        s = Regex.Replace(s, @"(?<=\d{1,3}\s*[°º])\s*([0-5]\d)([0-5]\d(?:[.,]\d+)?)(?=\s*"")", "$1'$2");
        // Grau escrito como º, ˚ ou ° com espaço antes.
        s = Regex.Replace(s, @"(?<=\d)\s?[º˚]", "°");
        // Sinal de menos tipográfico.
        s = s.Replace('−', '-').Replace('–', '-');
        return s;
    }

    // ------------------------------------------------------------------ nomes e trechos a ignorar

    /// <summary>Nome de vértice depois de "vértice", "marco", "ponto", "estaca".</summary>
    private static readonly Regex NameKwRx = new(
        @"(?i:\b(?:v[ée]rtices?|marcos?|pontos?|estacas?|piquetes?|pts?\.))\s*(?i:n[º°o]\.?\s*)?(?<n>[A-Z0-9]{2,6}-[A-Z]{1,2}-\d{1,6}[A-Z]?|[A-Z]{1,6}[-_]?\d{1,5}[A-Z]?|\d{1,4}[A-Z]?|[A-Z]{1,3})(?![\p{L}\d°'-]|[.,]\d)", Opt);
    /// <summary>Código do INCRA ("ABCD-M-1234") ou nomes curtos como P-01, M-02, V1, PP-1.</summary>
    private static readonly Regex NameCodeRx = new(
        @"(?<![\p{L}\d])(?<n>[A-Z0-9]{3,6}-[A-Z]{1,2}-\d{1,6}[A-Z]?|(?:PP|MP|EST|PT|P|M|V|E|A|B|C|D)-\d{1,4}[A-Z]?|(?:PP|MP|EST|PT|P|M|V|A|B|C|D)\d{1,4}[A-Z]?)(?![\p{L}\d]|[.,]\d)", Opt);
    /// <summary>Nome no começo de uma linha de tabela ("P-01   235142,50 …", "1  235142,50 …").</summary>
    private static readonly Regex NameRowRx = new(@"^\s*(?<n>[A-Z0-9]{3,6}-[A-Z]{1,2}-\d{1,6}[A-Z]?|[A-Z]{1,6}-?\d{1,5}[A-Z]?|\d{1,3}(?!\s\d{3}[ ,.]\d))(?=\s*[:;]?\s+-?\d)", RegexOptions.Multiline | Opt);

    /// <summary>Números que não são do perímetro (documentos, datas, datum, fuso, meridiano, dinheiro…).</summary>
    private static readonly Regex IgnoreRx = new(
        @"SIRGAS\s*-?\s*2000|WGS\s*-?\s*84|SAD\s*-?\s*69|EPSG\s*:?\s*\d+|" +
        @"(?i:fuso|zona)\s*(?i:UTM\s*)?:?\s*\d{1,3}(?:\s*[NS]\b)?|\bUTM\s*\d{1,2}\s*[NS]\b|" +
        @"(?:\bMC\b|(?i:meridiano\s+central))[^;\n]{0,20}?\d{2}(?:\s*°)?(?:\s*(?:W|O|WGr)\b)?|" +
        @"\b\d{1,2}/\d{1,2}/\d{2,4}\b|\b\d{3}\.\d{3}\.\d{3}-\d{2}\b|\b\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}\b|\b\d{5}-\d{3}\b|" +
        @"\(\d{2}\)\s*\d{4,5}-?\d{4}|R\$\s*[\d.,]+|" +
        @"(?i:\b(?:matr[íi]cula|protocolo|livro|folhas?|fls?|registro|CNS|CPF|CNPJ|RG|CREA|CAU|ART|RRT|TRT|CCIR|NIRF|CAR|lei|decreto|art|p[áa]gina|selo|cep|c[óo]digo\s+do\s+im[óo]vel|SNCR)\b\.?)\s*(?i:n[º°o]\.?)?\s*:?\s*[\d.\-/]+",
        Opt);

    // ------------------------------------------------------------------ números e ângulos

    private static readonly Regex AngleRx = new(
        @"(?<![\p{L}\d.,])(?<![\p{L}\d][-/])(?:(?<sg>-)\s?)?(?:(?<dd>\d{1,3}[.,]\d+)\s*°|(?<d>\d{1,3})\s*°\s*(?:(?<m>\d{1,2}(?:[.,]\d+)?)\s*'\s*(?:(?<s>\d{1,2}(?:[.,]\d+)?)\s*""?)?)?)", Opt);
    private static readonly Regex NumRx = new(
        @"(?<![\p{L}\d.,])(?<![\p{L}\d][-/])(?:(?<sg>-)\s?)?(?<n>\d{1,3}(?:\.\d{3})+,\d+|\d{1,3}(?: \d{3})+,\d+|\d{1,3}(?:,\d{3})+\.\d+|\d{1,3}(?:\.\d{3}){2,}(?![,\d])|\d+(?:[.,]\d+)?)(?![\d])", Opt);
    private static readonly Regex HemiAfterRx = new(@"^\s{0,2}(?<h>N[EeWwOo]|S[EeWwOo]|N|S|E|W|L|O)(?![\p{L}])", Opt);
    private static readonly Regex QuadBeforeRx = new(@"(?<![\p{L}])(?<q>N[EeWwOo]|S[EeWwOo]|N|S)\s{0,2}$", Opt);

    // ------------------------------------------------------------------ rótulos

    private static readonly (string Label, Regex Rx)[] BeforeRx =
    {
        ("ALT", new Regex(@"(?:(?i:altitude|alt|cota|eleva[çc][ãa]o)\.?\s*(?:\([^)]{0,6}\))?|(?<![\p{L}])[hH])\s*[:=]?\s*$", Opt)),
        ("LAT", new Regex(@"(?i:\blat(?:itude)?)\.?\s*(?:\([^)]{0,6}\))?\s*[:=]?\s*$", Opt)),
        ("LON", new Regex(@"(?i:\blon(?:g(?:itude)?)?)\.?\s*(?:\([^)]{0,6}\))?\s*[:=]?\s*$", Opt)),
        ("N", new Regex(@"(?:(?<![\p{L}])[NY]|(?i:\bnorte))\s*(?:\(\s*[NY]\s*\))?\s*[:=]?\s*$", Opt)),
        ("E", new Regex(@"(?:(?<![\p{L}])[EX]|(?i:\b(?:leste|este)))\s*(?:\(\s*[EX]\s*\))?\s*[:=]?\s*$", Opt)),
        ("AZ", new Regex(@"(?i:\b(?:azimutes?|azim|az))\.?\s*(?:\([^)]{0,12}\)\s*)?(?:(?i:de|igual\s+a|plano|verdadeiro|geod[ée]sico|magn[ée]tico|UTM)\s*)*[:=]?\s*$", Opt)),
        ("RUMO", new Regex(@"(?i:\brumos?)\s*(?:(?i:de|igual\s+a)\s*)?[:=]?\s*$", Opt)),
        ("DIST", new Regex(@"(?:(?i:\b(?:dist[âa]ncias?|dist|medindo|mede|extens[ãa]o|comprimento))\.?\s*(?:(?i:de|igual\s+a|horizontal|plana|geod[ée]sica)\s*)*|(?<![\p{L}])D\s*)[:=]?\s*$", Opt)),
        ("PERIM", new Regex(@"(?i:per[íi]metro)[^\d;]{0,25}$", Opt)),
        ("AREA", new Regex(@"(?i:[áa]rea)[^\d;]{0,30}$", Opt)),
    };
    private static readonly Regex SuffixCoordRx = new(@"^\s*[mM]?\s*\(?\s*(?<l>N|E|X|Y)\s*\)?(?![\p{L}])", Opt);
    private static readonly Regex UnitRx = new(@"^\s*(?:(?<a>m²|m2\b|(?i:metros\s+quadrados))|(?<h>(?i:ha\b|hectares?))|(?<m>m\b|M\b|(?i:metros?|mts?)\b|m(?=[;,.)]|$)))", Opt);

    /// <summary>Palavras do cabeçalho de tabela e o papel da coluna.</summary>
    private static readonly (TokenRole Role, Regex Rx)[] HeaderWords =
    {
        (TokenRole.Lat, new Regex(@"^(?:latitude|lat)$", OptI)),
        (TokenRole.Lon, new Regex(@"^(?:longitude|long|lon)$", OptI)),
        (TokenRole.North, new Regex(@"^(?:(?i:norte|coord\.?n)|N|Y|y)$", Opt)),
        (TokenRole.East, new Regex(@"^(?:(?i:este|leste|coord\.?e)|E|X|x)$", Opt)),
        (TokenRole.Azimuth, new Regex(@"^(?:(?i:azimutes?|azim|rumos?)|Az|AZ)$", Opt)),
        (TokenRole.Distance, new Regex(@"^(?:(?i:dist[âa]ncias?|dist|comprimento)|D)$", Opt)),
        (TokenRole.Ignore, new Regex(@"^(?:(?i:altitude|alt|cota|eleva[çc][ãa]o)|h|H)$", Opt)),
    };

    /// <summary>Outras palavras de cabeçalho de tabela (sem número na coluna).</summary>
    private static readonly Regex TableWordRx = new(@"^(?i:v[ée]rtices?|pontos?|marcos?|c[óo]digo|esta[çc][ãa]o|confrontantes?|confronta[çc][õo]es|lado|segmento|de|para|m|graus?|ordem|n[º°o]|coordenadas?|UTM|geogr[áa]ficas?|plano|vante|r[ée]|descri[çc][ãa]o)$", Opt);

    // ------------------------------------------------------------------ leitura

    /// <param name="check">Conferência cruzada (corrige números lidos errados quando o resto do texto mostra o valor certo).</param>
    public static SmartReading Read(string text, ReadingModel model = ReadingModel.Auto, bool check = true)
    {
        var r = new SmartReading();
        string s = Prepare(text);
        r.Text = s;
        if (s.Trim().Length == 0) return r;
        var lineStarts = new List<int> { 0 };
        for (int i = 0; i < s.Length; i++) if (s[i] == '\n') lineStarts.Add(i + 1);
        int LineOf(int pos) { int k = lineStarts.BinarySearch(pos); return k >= 0 ? k : ~k - 1; }

        // ---- nomes de vértices e trechos a ignorar (mascarados para os números deles não entrarem na leitura)
        var names = new List<(int Start, int End, string Name)>();
        foreach (Match m in NameKwRx.Matches(s)) AddName(names, m);
        foreach (Match m in NameCodeRx.Matches(s)) AddName(names, m);
        foreach (Match m in NameRowRx.Matches(s)) AddName(names, m);
        names.Sort((a, b) => a.Start.CompareTo(b.Start));
        var mask = s.ToCharArray();
        foreach (var n in names) for (int i = n.Start; i < n.End; i++) mask[i] = '#';
        foreach (Match m in IgnoreRx.Matches(s)) for (int i = m.Index; i < m.Index + m.Length; i++) mask[i] = '#';
        string ms = new(mask);

        // ---- estilo dos números do documento (vírgula decimal ou ponto decimal)
        int ptStyle = Regex.Matches(ms, @"\d,\d+(?![\d.])").Count, usStyle = Regex.Matches(ms, @"\d\.\d+(?![\d,])").Count;
        bool ptDoc = ptStyle >= usStyle;

        // ---- ângulos
        var toks = new List<ReadToken>();
        var angleSpans = new List<(int S, int E)>();
        foreach (Match m in AngleRx.Matches(ms))
        {
            if (!TryAngle(m, out double v, out int secDec)) continue;
            bool neg = m.Groups["sg"].Success;
            var t = new ReadToken { Start = m.Index, End = m.Index + m.Length, Raw = s.Substring(m.Index, m.Length), IsAngle = true, Value = neg ? -v : v, Negative = neg, SecDecimals = secDec, Line = LineOf(m.Index), Step = AngleStep(m) };
            // Letra depois: hemisfério (S, W…) ou quadrante do rumo (NE…).
            var h = HemiAfterRx.Match(s[t.End..Math.Min(s.Length, t.End + 4)]);
            if (h.Success)
            {
                string l = h.Groups["h"].Value.ToUpperInvariant();
                l = l.Length == 2 ? l[0] + l[1..].Replace('O', 'W') : l.Replace('O', 'W').Replace('L', 'E');
                if (l.Length == 2) t.Quadrant = l; else t.Hemi = l;
                t.End += h.Index + h.Length;
            }
            // Letra antes (rumo "N 45°30' E" ou "NE 45°30'"), desde que não seja a letra do ângulo anterior.
            int prevEnd = toks.Count > 0 ? toks[^1].End : 0;
            var q = QuadBeforeRx.Match(s[Math.Max(prevEnd, t.Start - 4)..t.Start]);
            if (q.Success)
            {
                string qq = q.Groups["q"].Value.ToUpperInvariant();
                if (qq.Length == 2) qq = qq[0] + qq[1..].Replace('O', 'W');
                if (qq.Length == 2) t.Quadrant = qq;
                else if (t.Hemi is "E" or "W") { t.Quadrant = qq + t.Hemi; t.Hemi = ""; }
            }
            toks.Add(t);
            angleSpans.Add((m.Index, m.Index + m.Length));
        }
        // ---- números
        foreach (Match m in NumRx.Matches(ms))
        {
            if (angleSpans.Any(a => m.Index < a.E && m.Index + m.Length > a.S)) continue;
            var (v, alt, dec) = ParseNumber(m.Groups["n"].Value, ptDoc);
            if (double.IsNaN(v)) continue;
            bool neg = m.Groups["sg"].Success;
            toks.Add(new ReadToken { Start = m.Index, End = m.Index + m.Length, Raw = s.Substring(m.Index, m.Length), Value = neg ? -v : v, Alt = alt, Decimals = dec, Negative = neg, Line = LineOf(m.Index), Step = Math.Pow(10, -dec) });
        }
        toks.Sort((a, b) => a.Start.CompareTo(b.Start));

        // ---- rótulos e unidades
        for (int i = 0; i < toks.Count; i++)
        {
            var t = toks[i];
            int from = Math.Max(i > 0 ? toks[i - 1].End : 0, t.Start - 32);
            int lineStart = s.LastIndexOf('\n', Math.Max(0, t.Start - 1)) + 1;
            from = Math.Max(from, lineStart);
            string before = s[from..t.Start];
            // Número no começo da linha com o rótulo no fim da linha de cima ("(N\n7 994 173,60"), fora de tabela.
            if (before.Trim().Length == 0 && lineStart > 0 && (i == 0 || toks[i - 1].End < lineStart))
            {
                int ps = Math.Max(i > 0 ? toks[i - 1].End : 0, lineStart - 9);
                before = s[ps..(lineStart - 1)] + " ";
            }
            foreach (var (label, rx) in BeforeRx)
                if (rx.IsMatch(before)) { t.Label = label; break; }
            string after = s[t.End..Math.Min(s.Length, t.End + 14)];
            var u = UnitRx.Match(after);
            if (u.Success) t.Unit = u.Groups["a"].Success ? "m²" : u.Groups["h"].Success ? "ha" : "m";
            if (t.Label.Length == 0 && !t.IsAngle && SuffixCoordRx.Match(after) is { Success: true } sc)
                t.Label = sc.Groups["l"].Value is "N" or "Y" ? "N" : "E";
        }

        // ---- tabela: linhas com cabeçalho de coordenadas ou de azimute/distância
        var header = FindHeader(s, lineStarts);
        r.Table = header is not null;

        // ---- papéis
        foreach (var t in toks) t.Role = RoleOf(t, model);
        if (header is not null) ApplyHeader(toks, header.Value.Roles, header.Value.Line, r);
        r.Tokens.AddRange(toks);

        // ---- vértices
        BuildVertices(r, toks, names, model, s);
        // ---- trechos
        BuildCourses(r, toks, s, model);
        // Trechos escritos junto com as coordenadas: conferência (trecho i = do vértice i ao i+1).
        if (r.Coordinates.Vertices.Count >= 3 && r.Courses.Count > 0) r.Coordinates.StatedCourses.AddRange(r.Courses);
        r.Coordinates.Datum = CoordinateTableReader.DetectDatum(s);
        if (toks.FirstOrDefault(t => t.Role == TokenRole.Area && t.Value > 0) is { } at)
        {
            r.Area = at.Unit == "ha" ? at.Value * 10000 : at.Value;
            r.AreaStep = at.Step * (at.Unit == "ha" ? 10000 : 1);
        }
        if (check) ReadingCheck.Review(r);
        Describe(r, toks, model);
        return r;
    }

    private static void AddName(List<(int Start, int End, string Name)> names, Match m)
    {
        var g = m.Groups["n"];
        string n = g.Value.Trim();
        if (n.Length == 0 || names.Any(x => g.Index < x.End && g.Index + g.Length > x.Start)) return;
        names.Add((g.Index, g.Index + g.Length, n));
    }

    /// <summary>Passo da última casa do ângulo escrito, em graus (segundos, minutos, graus ou decimais de grau).</summary>
    private static double AngleStep(Match m)
    {
        static int Dec(string v) { int k = v.IndexOfAny(new[] { ',', '.' }); return k < 0 ? 0 : v.Length - k - 1; }
        if (m.Groups["dd"].Success) return Math.Pow(10, -Dec(m.Groups["dd"].Value));
        if (m.Groups["s"].Success) return Math.Pow(10, -Dec(m.Groups["s"].Value)) / 3600;
        if (m.Groups["m"].Success) return Math.Pow(10, -Dec(m.Groups["m"].Value)) / 60;
        return 1;
    }

    private static bool TryAngle(Match m, out double deg, out int secDecimals)
    {
        deg = 0;
        secDecimals = 0;
        if (m.Groups["dd"].Success)
        {
            if (!double.TryParse(m.Groups["dd"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out deg)) return false;
            secDecimals = m.Groups["dd"].Value.Length - m.Groups["dd"].Value.IndexOfAny(new[] { ',', '.' }) - 1 >= 5 ? 3 : 0;
            return deg <= 360;
        }
        if (!int.TryParse(m.Groups["d"].Value, out int d) || d > 360) return false;
        double mi = 0, se = 0;
        if (m.Groups["m"].Success && !double.TryParse(m.Groups["m"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out mi)) return false;
        if (m.Groups["s"].Success)
        {
            string sv = m.Groups["s"].Value;
            if (!double.TryParse(sv.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out se)) return false;
            int k = sv.IndexOfAny(new[] { ',', '.' });
            secDecimals = k >= 0 ? sv.Length - k - 1 : 0;
        }
        if (mi >= 60 || se >= 60) return false;
        // Só "45°" (sem minutos) também vale, mas é ângulo solto: precisa de contexto (rótulo) para virar azimute.
        deg = d + mi / 60 + se / 3600;
        return true;
    }

    /// <summary>Valor do número (e outra leitura possível quando o separador é ambíguo) e as casas decimais.</summary>
    public static (double Value, double? Alt, int Decimals) ParseNumber(string raw, bool ptDoc = true)
    {
        string n = raw.Replace(" ", "");
        int lastDot = n.LastIndexOf('.'), lastComma = n.LastIndexOf(',');
        double V(string x) => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
        if (lastDot >= 0 && lastComma >= 0)
        {
            bool commaDec = lastComma > lastDot;
            string clean = commaDec ? n.Replace(".", "").Replace(',', '.') : n.Replace(",", "");
            int dec = clean.Length - clean.IndexOf('.') - 1;
            return (V(clean), null, dec);
        }
        if (lastComma >= 0)
        {
            // "236,184": vírgula decimal (Brasil) – ou milhar no formato americano.
            if (Regex.IsMatch(n, @"^\d{1,3}(?:,\d{3})+$") && n.Count(c => c == ',') >= 2) return (V(n.Replace(",", "")), null, 0);
            double v = V(n.Replace(',', '.'));
            double? alt = Regex.IsMatch(n, @"^\d{1,3},\d{3}$") ? V(n.Replace(",", "")) : null;
            return (v, alt, n.Length - lastComma - 1);
        }
        if (lastDot >= 0)
        {
            if (n.Count(c => c == '.') >= 2) return (V(n.Replace(".", "")), null, 0);
            // "236.184": milhar num documento brasileiro, decimal num americano.
            if (Regex.IsMatch(n, @"^\d{1,3}\.\d{3}$"))
                return ptDoc ? (V(n.Replace(".", "")), V(n), 0) : (V(n), V(n.Replace(".", "")), 3);
            return (V(n), null, n.Length - lastDot - 1);
        }
        return (V(n), null, 0);
    }

    // ------------------------------------------------------------------ papéis

    public static bool UtmE(double v) => v >= 100_000 && v <= 900_000;
    public static bool UtmN(double v) => v >= 0 && v <= 10_000_000;
    private static bool UtmNSouth(double v) => v >= 6_000_000 && v <= 10_000_000;
    public static bool LatBr(double v) => v >= -34.5 && v <= 6.5;
    public static bool LonBr(double v) => v >= -74.5 && v <= -28.0;

    private static TokenRole RoleOf(ReadToken t, ReadingModel model)
    {
        if (t.Label is "ALT" or "PERIM") return TokenRole.Ignore;
        if (t.IsAngle)
        {
            // Coordenada geográfica: rótulo, hemisfério com segundos decimais, ou sinal com segundos decimais.
            bool geoEvidence = t.Label is "LAT" or "LON" || (t.Hemi.Length == 1 && t.SecDecimals >= 2) || (t.Negative && t.SecDecimals >= 2);
            if (model is ReadingModel.Courses or ReadingModel.Bearings) geoEvidence = false;
            if (model == ReadingModel.Geographic && t.SecDecimals >= 2 && t.Label is not ("AZ" or "RUMO")) geoEvidence = true;
            if (geoEvidence && t.Label is not ("AZ" or "RUMO" or "DIST"))
            {
                double v = SignedGeo(t);
                if (t.Label == "LAT" || t.Hemi is "N" or "S") return Math.Abs(v) <= 90 ? TokenRole.Lat : TokenRole.None;
                if (t.Label == "LON" || t.Hemi is "E" or "W") return Math.Abs(v) <= 180 ? TokenRole.Lon : TokenRole.None;
                return LatBr(v) && !LonBr(v) ? TokenRole.Lat : LonBr(v) ? TokenRole.Lon : Math.Abs(v) <= 90 ? TokenRole.Lat : TokenRole.None;
            }
            if (t.Value is < 0 or >= 360) return TokenRole.None;
            return TokenRole.Azimuth;
        }
        double a = Math.Abs(t.Value);
        if (t.Label == "AREA" || t.Unit is "m²" or "ha") return TokenRole.Area;
        if (model is ReadingModel.Courses or ReadingModel.Bearings)
        {
            // Só trechos – mas a coordenada do ponto inicial (com rótulo) continua valendo para começar o desenho nela.
            if (t.Label == "N" && UtmN(a)) return TokenRole.North;
            if (t.Label == "E" && UtmE(a)) return TokenRole.East;
            return t.Label == "DIST" || t.Unit == "m" ? (a > 0 && a < 100_000 ? TokenRole.Distance : TokenRole.None) : TokenRole.None;
        }
        // Graus decimais com rótulo ("Lat -6,3336567") ou muitas casas.
        if (t.Label is "LAT" && a <= 90) return TokenRole.Lat;
        if (t.Label is "LON" && a <= 180) return TokenRole.Lon;
        if (t.Label == "N") return TokenRole.North;
        if (t.Label == "E") return TokenRole.East;
        if (t.Label == "DIST") return a > 0 && a < 100_000 ? TokenRole.Distance : TokenRole.None;
        if (t.Label == "AZ" && a < 360) return TokenRole.Azimuth;
        // Sem rótulo: pela faixa de valores.
        if (t.Unit == "m" && a < 100_000 && !(UtmE(a) && t.Decimals >= 2)) return TokenRole.Distance;
        if (UtmNSouth(a) && model != ReadingModel.Local) return TokenRole.North;
        if (UtmE(a) && model != ReadingModel.Local) return TokenRole.East;
        if (t.Alt is double alt && model != ReadingModel.Local)
        {
            if (UtmNSouth(alt)) { t.Value = alt; return TokenRole.North; }
            if (UtmE(alt)) { t.Value = alt; return TokenRole.East; }
        }
        if (t.Decimals >= 5 && a <= 90 && model != ReadingModel.Local)
            return LatBr(t.Value) && !LonBr(t.Value) ? TokenRole.Lat : LonBr(t.Value) ? TokenRole.Lon : TokenRole.None;
        return TokenRole.None;
    }

    /// <summary>Valor do ângulo com o sinal do hemisfério (S e W negativos).</summary>
    private static double SignedGeo(ReadToken t) => t.Hemi is "S" or "W" ? -Math.Abs(t.Value) : t.Value;

    // ------------------------------------------------------------------ tabelas

    private static (int Line, List<TokenRole> Roles)? FindHeader(string s, List<int> lineStarts)
    {
        for (int li = 0; li < lineStarts.Count; li++)
        {
            int a = lineStarts[li], b = li + 1 < lineStarts.Count ? lineStarts[li + 1] - 1 : s.Length;
            string line = s[a..Math.Max(a, b)];
            if (Regex.IsMatch(line, @"\d[.,]\d")) continue;
            var words = Regex.Split(line, @"[\s|;/()\[\]]+").Select(w => w.Trim(':', '.', ',', '-')).Where(w => w.Length > 0).ToList();
            var roles = new List<TokenRole>();
            foreach (var w in words)
                foreach (var (role, rx) in HeaderWords)
                    if (rx.IsMatch(w))
                    {
                        // "Este (X)", "Norte (Y)": a mesma coluna escrita duas vezes.
                        if (roles.Count == 0 || roles[^1] != role) roles.Add(role);
                        break;
                    }
            bool coords = roles.Contains(TokenRole.East) && roles.Contains(TokenRole.North) || roles.Contains(TokenRole.Lat) && roles.Contains(TokenRole.Lon);
            bool courses = roles.Contains(TokenRole.Azimuth) && roles.Contains(TokenRole.Distance);
            // Cabeçalho é uma linha curta feita quase só de nomes de coluna (frase do memorial com "azimute e distância" não é).
            int known = words.Count(w => HeaderWords.Any(h => h.Rx.IsMatch(w)) || TableWordRx.IsMatch(w));
            if ((coords || courses) && words.Count <= 16 && known >= Math.Max(2, words.Count * 0.6)) return (li, roles);
        }
        return null;
    }

    /// <summary>
    /// Linhas abaixo do cabeçalho: os números de cada linha recebem o papel das colunas, na ordem. O alinhamento aceita colunas
    /// vazias (altitude sem valor) e números a mais no fim (do nome do confrontante): é o que casa mais colunas com valores possíveis.
    /// </summary>
    private static void ApplyHeader(List<ReadToken> toks, List<TokenRole> roles, int headerLine, SmartReading r)
    {
        foreach (var row in toks.Where(t => t.Line > headerLine && t.Role != TokenRole.Area).GroupBy(t => t.Line))
        {
            var cells = row.OrderBy(t => t.Start).ToList();
            if (cells.Count < 2) continue;
            var match = Align(roles, cells);
            if (match.Count(m => roles[m.Role] != TokenRole.Ignore) < 2) continue;
            foreach (var (ri, ci) in match) cells[ci].Role = roles[ri];
        }
        r.Evidence.Add("cabeçalho da tabela: " + string.Join(", ", roles.Select(RoleName)));
    }

    private static bool Fits(TokenRole role, ReadToken t) => role switch
    {
        TokenRole.Lat or TokenRole.Lon => t.IsAngle ? Math.Abs(t.Value) <= 180 : t.Decimals >= 4 && Math.Abs(t.Value) <= 180,
        TokenRole.Azimuth => t.IsAngle && t.Value >= 0 && t.Value < 360,
        TokenRole.Distance => !t.IsAngle && t.Value > 0 && t.Value < 100_000,
        TokenRole.East or TokenRole.North => !t.IsAngle,
        TokenRole.Ignore => !t.IsAngle,
        _ => false,
    };

    /// <summary>Alinhamento colunas × valores (maior número de casamentos, na ordem; altitude vale menos).</summary>
    private static List<(int Role, int Cell)> Align(List<TokenRole> roles, List<ReadToken> cells)
    {
        int m = roles.Count, n = cells.Count;
        var best = new double[m + 1, n + 1];
        for (int i = m - 1; i >= 0; i--)
            for (int j = n - 1; j >= 0; j--)
            {
                double v = Math.Max(best[i + 1, j], best[i, j + 1]);
                if (Fits(roles[i], cells[j])) v = Math.Max(v, (roles[i] == TokenRole.Ignore ? 0.9 : 1.0) + best[i + 1, j + 1]);
                best[i, j] = v;
            }
        var res = new List<(int, int)>();
        for (int i = 0, j = 0; i < m && j < n;)
        {
            if (Fits(roles[i], cells[j]) && Math.Abs(best[i, j] - ((roles[i] == TokenRole.Ignore ? 0.9 : 1.0) + best[i + 1, j + 1])) < 1e-9) { res.Add((i, j)); i++; j++; }
            else if (Math.Abs(best[i, j] - best[i + 1, j]) < 1e-9) i++;
            else j++;
        }
        return res;
    }

    private static string RoleName(TokenRole r) => r switch
    {
        TokenRole.East => "E/X", TokenRole.North => "N/Y", TokenRole.Lat => "latitude", TokenRole.Lon => "longitude",
        TokenRole.Azimuth => "azimute", TokenRole.Distance => "distância", TokenRole.Ignore => "altitude", _ => r.ToString(),
    };

    // ------------------------------------------------------------------ vértices

    private static void BuildVertices(SmartReading r, List<ReadToken> toks, List<(int Start, int End, string Name)> names, ReadingModel model, string s)
    {
        var res = r.Coordinates;
        if (model is ReadingModel.Courses or ReadingModel.Bearings)
        {
            // Só a coordenada do ponto inicial (para começar o desenho nela).
            if (Pairs(toks).FirstOrDefault() is { A: not null, B: not null } first)
            {
                AddVertex(res, first, names, 0, s);
                res.Kind = PerimeterCoordKind.Utm;
                res.Format = "memorial";
                CleanupVertices(res);
            }
            return;
        }
        // Colunas em bloco primeiro (senão o último E da coluna se junta ao primeiro N da coluna seguinte).
        var blocks = BlockPairs(toks, names);
        var pairs = Pairs(toks);
        if (blocks.Count > 0)
        {
            pairs = pairs.Concat(blocks).OrderBy(p => p.Start).ToList();
            r.Evidence.Add("colunas da tabela lidas em blocos (uma coluna depois da outra)");
        }
        if (model == ReadingModel.Geographic) pairs = pairs.Where(p => p.Geo).ToList();
        else if (model is ReadingModel.Utm or ReadingModel.Local) pairs = pairs.Where(p => !p.Geo).ToList();
        if (pairs.Count == 0) return;
        // Um sistema só: o que tiver mais vértices (há memoriais com UTM e geográficas de cada vértice).
        int geo = pairs.Count(p => p.Geo), plan = pairs.Count - geo;
        bool useGeo = geo > plan;
        pairs = pairs.Where(p => p.Geo == useGeo).ToList();
        int prevEnd = 0;
        foreach (var p in pairs)
        {
            AddVertex(res, p, names, prevEnd, s);
            if (p.Name is not null) res.Vertices[^1].Name = p.Name;
            prevEnd = Math.Max(p.A!.End, p.B!.End);
        }
        if (useGeo) res.Kind = PerimeterCoordKind.Geographic;
        else
        {
            bool utm = res.Vertices.All(v => UtmE(v.A) && UtmN(v.B));
            res.Kind = model == ReadingModel.Local || !utm ? PerimeterCoordKind.Local : PerimeterCoordKind.Utm;
        }
        res.Format = r.Table ? "tabela" : "memorial";
        CleanupVertices(res);
    }

    private sealed record CoordPair(ReadToken? A, ReadToken? B, bool Geo, string? Name = null)
    {
        public int Start => Math.Min(A?.Start ?? int.MaxValue, B?.Start ?? int.MaxValue);
    }

    private static bool Complement(TokenRole a, TokenRole b) =>
        (a, b) is (TokenRole.East, TokenRole.North) or (TokenRole.North, TokenRole.East) or (TokenRole.Lat, TokenRole.Lon) or (TokenRole.Lon, TokenRole.Lat);

    /// <summary>
    /// Colunas de tabela lidas uma depois da outra (texto copiado de um PDF, OCR por blocos): uma sequência de valores E e depois a
    /// mesma quantidade de N (ou latitudes e longitudes) são os vértices, na ordem; os nomes são os da sequência de nomes antes.
    /// </summary>
    private static List<CoordPair> BlockPairs(List<ReadToken> toks, List<(int Start, int End, string Name)> names)
    {
        var free = toks.Where(t => !t.Used && t.Role is TokenRole.East or TokenRole.North or TokenRole.Lat or TokenRole.Lon).ToList();
        var runs = new List<List<ReadToken>>();
        foreach (var t in free)
        {
            if (runs.Count > 0 && runs[^1][0].Role == t.Role) runs[^1].Add(t);
            else runs.Add(new List<ReadToken> { t });
        }
        var res = new List<CoordPair>();
        for (int i = 0; i < runs.Count; i++)
        {
            var r1 = runs[i];
            // Coluna: pelo menos 3 valores, um por linha.
            static bool Column(List<ReadToken> run) => run.Count >= 3 && run.Zip(run.Skip(1)).All(p => p.Second.Line > p.First.Line);
            if (!Column(r1) || r1[0].Used) continue;
            var r2 = runs.Skip(i + 1).FirstOrDefault(x => !x[0].Used && Complement(r1[0].Role, x[0].Role) && x.Count == r1.Count && Column(x));
            if (r2 is null) continue;
            var nm = names.Where(n => n.End <= r1[0].Start).TakeLast(r1.Count).ToList();
            bool geo = r1[0].Role is TokenRole.Lat or TokenRole.Lon;
            bool firstIsA = r1[0].Role is TokenRole.East or TokenRole.Lat;
            for (int k = 0; k < r1.Count; k++)
            {
                var (a, b) = firstIsA ? (r1[k], r2[k]) : (r2[k], r1[k]);
                res.Add(new CoordPair(a, b, geo, nm.Count == r1.Count ? nm[k].Name : null));
                r1[k].Used = r2[k].Used = true;
            }
        }
        return res;
    }

    /// <summary>Pares de coordenadas (E com N, latitude com longitude) escritos perto um do outro, na ordem do texto.</summary>
    private static List<CoordPair> Pairs(List<ReadToken> toks)
    {
        var res = new List<CoordPair>();
        ReadToken? pending = null;
        foreach (var t in toks.Where(t => !t.Used && t.Role is TokenRole.East or TokenRole.North or TokenRole.Lat or TokenRole.Lon))
        {
            if (pending is not null && t.Start - pending.End < 160 && t.Line - pending.Line <= 2)
            {
                var pr = (pending.Role, t.Role);
                CoordPair? cp = pr switch
                {
                    (TokenRole.East, TokenRole.North) => new(pending, t, false),
                    (TokenRole.North, TokenRole.East) => new(t, pending, false),
                    (TokenRole.Lat, TokenRole.Lon) => new(pending, t, true),
                    (TokenRole.Lon, TokenRole.Lat) => new(t, pending, true),
                    // Hemisfério norte do Brasil (N menor que 1.000.000): dois números na faixa do E sem rótulo = E e N, nessa ordem.
                    (TokenRole.East, TokenRole.East) when pending.Label.Length == 0 && t.Label.Length == 0 && t.Line == pending.Line => new(pending, t, false),
                    _ => null,
                };
                if (cp is not null)
                {
                    res.Add(cp);
                    pending.Used = t.Used = true;
                    pending = null;
                    continue;
                }
            }
            pending = t;
        }
        return res;
    }

    private static void AddVertex(CoordinateTextResult res, CoordPair p, List<(int Start, int End, string Name)> names, int prevEnd, string s)
    {
        int at = Math.Min(p.A!.Start, p.B!.Start);
        // Nome: o último citado entre o par anterior e este (na mesma linha, numa tabela).
        var nm = names.Where(n => n.End <= at && n.Start >= prevEnd).Select(n => n.Name).LastOrDefault() ?? "";
        double a = p.A.Value, b = p.B.Value;
        if (p.Geo)
        {
            a = SignedGeo(p.A);
            b = SignedGeo(p.B);
        }
        // Arredondamento do vértice como foi escrito: metade da última casa (nas geográficas, convertida em metros).
        double rounding = p.Geo
            ? WrittenPrecision.GeoRounding(a, p.A.Step, p.B.Step)
            : Math.Max(p.A.Step, p.B.Step) / 2;
        res.Vertices.Add(new PerimeterVertex
        {
            Name = nm, A = a, B = b, Rounding = rounding,
            Source = (p.A.Start < p.B.Start ? p.A.Raw + " / " + p.B.Raw : p.B.Raw + " / " + p.A.Raw).Trim(),
        });
    }

    private static void CleanupVertices(CoordinateTextResult res)
    {
        var v = res.Vertices;
        if (res.Kind == PerimeterCoordKind.Geographic)
        {
            // Sinal ou letra do hemisfério perdidos (OCR): no Brasil não há longitude leste nem latitude acima de 6,5° N.
            int fixedLon = 0, fixedLat = 0;
            foreach (var x in v)
            {
                if (x.B > 0 && LonBr(-x.B)) { x.B = -x.B; fixedLon++; }
                if (x.A > 6.5 && LatBr(-x.A)) { x.A = -x.A; fixedLat++; }
            }
            // Entre 0 e 6,5°: o hemisfério da maioria.
            int south = v.Count(x => x.A < 0), north = v.Count(x => x.A > 6.5 || (x.A > 0 && !LatBr(-x.A)));
            if (south > v.Count / 2)
                foreach (var x in v.Where(x => x.A > 0 && x.A <= 6.5)) { x.A = -x.A; fixedLat++; }
            if (fixedLon + fixedLat > 0)
                res.Warnings.Add($"{fixedLon + fixedLat} coordenada(s) sem o sinal ou a letra do hemisfério: consideradas no hemisfério sul/oeste, como as demais (Brasil). Confira.");
        }
        double tol = res.Kind == PerimeterCoordKind.Geographic ? 1e-8 : 0.0005;
        bool Same(PerimeterVertex a, PerimeterVertex b) => Math.Abs(a.A - b.A) <= tol && Math.Abs(a.B - b.B) <= tol;
        for (int i = v.Count - 1; i > 0; i--)
            if (Same(v[i], v[i - 1])) v.RemoveAt(i);
        if (v.Count >= 3 && Same(v[^1], v[0]))
        {
            v.RemoveAt(v.Count - 1);
            res.ClosedInText = true;
        }
        for (int i = 1; i < v.Count; i++)
            if (v[i].Name.Length > 0 && v.Take(i).Any(x => x.Name == v[i].Name)) v[i].Name = "";
        for (int i = 0; i < v.Count; i++)
            if (v[i].Name.Length == 0)
                for (int k = i + 1; ; k++)
                {
                    string n = $"P-{k:00}";
                    if (!v.Any(x => x.Name == n)) { v[i].Name = n; break; }
                }
    }

    // ------------------------------------------------------------------ trechos

    /// <summary>Fim de um trecho no texto corrido: ";", "deste", "daí", "dali", fim de frase.</summary>
    private static readonly Regex ClauseRx = new(@";|(?i:\bdeste\b|\bda[íi]\b|\bdali\b|\bdo\s+(?:v[ée]rtice|marco|ponto)\b)|\.\s+(?=\p{Lu})", Opt);

    private static void BuildCourses(SmartReading r, List<ReadToken> toks, string s, ReadingModel model)
    {
        var cand = toks.Where(t => !t.Used && (t.Role == TokenRole.Distance || (t.Role == TokenRole.Azimuth && t.IsAngle))).ToList();
        if (cand.Count == 0) return;
        // Cláusulas: numa tabela (ou lista com um trecho por linha), cada linha; no texto corrido, entre ";", "deste", "daí"…
        bool lineMode = r.Table || (LinesHoldCourses(cand) && ClauseRx.Matches(s).Count < 3);
        var bounds = new List<int>();
        if (lineMode)
        {
            for (int i = 0; i < s.Length; i++) if (s[i] == '\n') bounds.Add(i);
        }
        else foreach (Match m in ClauseRx.Matches(s)) bounds.Add(m.Index);
        bounds.Add(s.Length);
        int ci = 0, start = 0;
        foreach (int b in bounds)
        {
            var inClause = cand.Where(t => t.Start >= start && t.Start < b).ToList();
            start = b;
            if (inClause.Count == 0) continue;
            var angs = inClause.Where(t => t.IsAngle).ToList();
            var dists = inClause.Where(t => !t.IsAngle && t.Role == TokenRole.Distance).ToList();
            if (angs.Count == 0 && dists.Count == 0) continue;
            int n = Math.Max(angs.Count, dists.Count);
            // Ângulo sem rótulo e sem distância na cláusula não é trecho (ex.: "45°" solto no texto).
            if (dists.Count == 0 && angs.All(a => a.Label.Length == 0 && a.Quadrant.Length == 0 && a.Raw.IndexOf('\'') < 0)) continue;
            for (int k = 0; k < n; k++)
            {
                var a = k < angs.Count ? angs[k] : null;
                var d = k < dists.Count ? dists[k] : null;
                double az = a is null ? double.NaN : AzimuthOf(a, model);
                double dist = d is null ? double.NaN : d.Value;
                if (a is null && n > 1) continue;
                var c = new Course
                {
                    AzimuthDeg = az, Distance = dist, Source = ((a?.Raw ?? "?") + " – " + (d?.Raw ?? "?")).Trim(),
                    DistanceStep = d?.Step, AzimuthStep = a?.Step,
                };
                r.Courses.Add(c);
                int cs = Math.Min(a?.Start ?? int.MaxValue, d?.Start ?? int.MaxValue), ce = Math.Max(a?.End ?? 0, d?.End ?? 0);
                r.CourseSpans.Add((cs, ce));
                if (a is not null) a.Used = true;
                if (d is not null) d.Used = true;
                ci++;
            }
        }
        // Trecho partido em duas cláusulas (azimute numa, distância na seguinte – quebra estranha do OCR): junta.
        for (int k = r.Courses.Count - 2; k >= 0; k--)
        {
            var a = r.Courses[k];
            var b = r.Courses[k + 1];
            bool aNoDist = !double.IsNaN(a.AzimuthDeg) && double.IsNaN(a.Distance), bNoAz = double.IsNaN(b.AzimuthDeg) && !double.IsNaN(b.Distance);
            bool aNoAz = double.IsNaN(a.AzimuthDeg) && !double.IsNaN(a.Distance), bNoDist = !double.IsNaN(b.AzimuthDeg) && double.IsNaN(b.Distance);
            if ((aNoDist && bNoAz) || (aNoAz && bNoDist))
            {
                r.Courses[k] = new Course
                {
                    AzimuthDeg = aNoDist ? a.AzimuthDeg : b.AzimuthDeg, Distance = aNoDist ? b.Distance : a.Distance, Source = a.Source + " … " + b.Source,
                    AzimuthStep = aNoDist ? a.AzimuthStep : b.AzimuthStep, DistanceStep = aNoDist ? b.DistanceStep : a.DistanceStep,
                };
                r.CourseSpans[k] = (r.CourseSpans[k].Start, r.CourseSpans[k + 1].End);
                r.Courses.RemoveAt(k + 1);
                r.CourseSpans.RemoveAt(k + 1);
            }
        }
        // Distâncias soltas que sobraram no começo ou no fim (testada, área por extenso…) não são trechos.
        while (r.Courses.Count > 0 && double.IsNaN(r.Courses[^1].AzimuthDeg)) { r.Courses.RemoveAt(r.Courses.Count - 1); r.CourseSpans.RemoveAt(r.CourseSpans.Count - 1); }
        while (r.Courses.Count > 0 && double.IsNaN(r.Courses[0].AzimuthDeg)) { r.Courses.RemoveAt(0); r.CourseSpans.RemoveAt(0); }
        // Muito incompleto (menos da metade dos trechos com azimute e distância): não é uma descrição por trechos.
        int full = r.Courses.Count(c => !double.IsNaN(c.AzimuthDeg) && !double.IsNaN(c.Distance));
        if (full < Math.Max(2, r.Courses.Count / 2)) { r.Courses.Clear(); r.CourseSpans.Clear(); }
    }

    /// <summary>Lista com um trecho por linha (azimute e distância na mesma linha, várias linhas assim).</summary>
    private static bool LinesHoldCourses(List<ReadToken> cand)
    {
        var byLine = cand.GroupBy(t => t.Line).ToList();
        int good = byLine.Count(g => g.Count(t => t.IsAngle) == 1 && g.Count(t => !t.IsAngle) == 1);
        return good >= 3 && good >= byLine.Count * 0.7;
    }

    private static double AzimuthOf(ReadToken a, ReadingModel model)
    {
        double v = Math.Abs(a.Value);
        if (a.Quadrant.Length == 2) return MemorialTextParser.BearingToAzimuth(v, a.Quadrant);
        if (model == ReadingModel.Bearings && a.Hemi.Length == 1) return v;
        return v % 360;
    }

    // ------------------------------------------------------------------ descrição do modelo

    private static void Describe(SmartReading r, List<ReadToken> toks, ReadingModel model)
    {
        var c = r.Coordinates;
        if (c.Vertices.Count >= 3)
        {
            string sys = c.Kind switch
            {
                PerimeterCoordKind.Geographic => "coordenadas geográficas (latitude e longitude)",
                PerimeterCoordKind.Local => "coordenadas locais (X, Y)",
                _ => "coordenadas UTM (E, N)",
            };
            r.Model = (r.Table ? "Tabela de " : "Memorial com ") + sys;
            int labeled = toks.Count(t => t.Used && t.Label is "N" or "E" or "LAT" or "LON");
            if (labeled > 0) r.Evidence.Add($"{labeled} coordenada(s) com rótulo (N, E, latitude, longitude…)");
            if (c.Kind == PerimeterCoordKind.Utm) r.Evidence.Add("valores na faixa do UTM (E com 6 dígitos, N com 7)");
            if (c.Kind == PerimeterCoordKind.Geographic && toks.Any(t => t.Used && t.Hemi.Length == 1)) r.Evidence.Add("hemisfério pelas letras (S, W/O)");
            if (r.Courses.Count > 0) r.Evidence.Add($"{r.Courses.Count} trecho(s) com azimute/rumo e distância para conferir");
        }
        else if (r.Courses.Count >= 3)
        {
            bool rumos = toks.Any(t => t.Used && t.IsAngle && t.Quadrant.Length == 2);
            r.Model = rumos ? "Rumos e distâncias" : "Azimutes e distâncias";
            if (c.Vertices.Count == 1) r.Model += ", com a coordenada do ponto inicial";
            if (rumos) r.Evidence.Add("quadrantes NE, SE, SO, NO depois dos ângulos (convertidos em azimute)");
        }
        else r.Model = "Não identificado";
        if (toks.Any(t => t.Raw.Contains(' ') && !t.IsAngle && t.Used)) r.Evidence.Add("números com espaço como separador de milhar");
        if (model != ReadingModel.Auto) r.Evidence.Add("modelo escolhido por você");
    }
}
