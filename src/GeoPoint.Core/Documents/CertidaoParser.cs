using System.Globalization;
using System.Text.RegularExpressions;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Export;

namespace GeoPoint.Core.Documents;

/// <summary>Medida de um lado sem ângulo (certidões antigas: "12,00 m de frente para a Rua X").</summary>
public sealed record CertidaoSide(string Lado, double Medida, string Confrontante);

/// <summary>Um trecho da descrição: do vértice <see cref="From"/> até o vértice <see cref="To"/>.</summary>
public sealed class CertidaoTrecho
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public double? Distance { get; set; }
    public double? Azimuth { get; set; }
    public string Confrontante { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>Perímetro lido de uma certidão (matrícula / inteiro teor) ou memorial: vértices, trechos e confrontantes.</summary>
public sealed class CertidaoData
{
    /// <summary>Trecho do documento com a descrição do perímetro (o resto – partes, registros, selos – é ignorado).</summary>
    public string Description { get; set; } = "";
    public CoordinateTextResult Coordinates { get; set; } = new();
    public List<CertidaoTrecho> Trechos { get; } = new();
    /// <summary>Azimutes/rumos e distâncias, na ordem (para a aba de azimutes, quando não há coordenadas).</summary>
    public List<Course> Courses { get; } = new();
    /// <summary>
    /// Confrontante de cada divisa: com coordenadas, na ordem dos vértices (vértice i → i+1); sem coordenadas, na ordem de <see cref="Courses"/>.
    /// </summary>
    public List<string> Confrontantes { get; } = new();
    public List<CertidaoSide> Sides { get; } = new();
    /// <summary>Trechos montados das medidas dos lados (ângulos supostos de 90° na frente).</summary>
    public bool FromSides { get; set; }
    /// <summary>Área e perímetro escritos no documento (só para conferir o desenho).</summary>
    public double? Area { get; set; }
    public double? Perimetro { get; set; }
    /// <summary>Fuso UTM escrito no documento (null = não informado).</summary>
    public int? UtmZone { get; set; }
    public bool South { get; set; } = true;
    public List<string> Warnings { get; } = new();

    public bool HasCoordinates => Coordinates.Vertices.Count >= 3;
    public bool HasCourses => Courses.Count >= 3;
}

/// <summary>
/// Lê só a descrição do perímetro de uma certidão de matrícula / inteiro teor (texto do PDF ou do OCR):
/// coordenadas dos vértices, ou azimutes e distâncias, e o confrontante de cada divisa. Dados das partes, registros,
/// selos e emolumentos ficam de fora.
/// </summary>
public static class CertidaoParser
{
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const string Num = @"(?<v>\d{1,3}(?:\.\d{3})+(?:,\d+)?|\d+(?:[.,]\d+)?)";
    private const string Name = @"(?<n>[A-Za-z]{0,6}[-\s]?\d+[A-Za-z0-9\-_./]*|[A-Za-z0-9][A-Za-z0-9\-_./]*\d)";

    /// <summary>Início da descrição do perímetro.</summary>
    private static readonly Regex DescStartRx = new(
        @"descri[çc][ãa]o\s+(?:da\s+propriedade|do\s+im[óo]vel|do\s+per[íi]metro)|limites\s+e\s+confronta[çc][õo]es|" +
        @"partindo\s+(?:do|de\s+um)\s+(?:v[ée]rtice|ponto|marco)|inicia-se|in[íi]cio\s+(?:no|do)\s+(?:v[ée]rtice|ponto|marco)|" +
        @"come[çc]a\s+no\s+(?:v[ée]rtice|ponto|marco)|tem\s+in[íi]cio\s+no", Opt);
    /// <summary>Fim da descrição: "fechando (assim) …", "ponto inicial (da descrição)…".</summary>
    private static readonly Regex DescEndRx = new(@"fechando\s+(?:assim\s+)?(?:o\s+per[íi]metro|a\s+poligonal|uma\s+[áa]rea|o\s+pol[íi]gono|a\s+[áa]rea)[^.;]*|ponto\s+inicial\s+da\s+descri[çc][ãa]o[^.;]*", Opt);
    /// <summary>O que vem depois da descrição (quando ela não termina com "fechando…").</summary>
    private static readonly Regex DescStopRx = new(
        @"propriet[áa]ri[oa]s?\s*(?:atual\s*)?:|registro\s+anterior|respons[áa]vel\s+t[ée]cnico|adquirente\s*:|\b(?:R|AV)[-.]\s?\d+\s*[/-]", Opt);
    /// <summary>Chegada de um trecho: "até o vértice P-02".</summary>
    private static readonly Regex ToRx = new(@"at[ée]\s+(?:o\s+|ao\s+|a\s+)?(?:v[ée]rtice|ponto|marco)\s+" + Name, Opt);
    /// <summary>Saída de um trecho: "do vértice P-02", "partindo do vértice P-01", "inicia-se no vértice P-01".</summary>
    private static readonly Regex FromRx = new(@"(?:\bdo|\bno|\bnum|partindo\s+do|a\s+partir\s+do)\s+(?:v[ée]rtice|ponto|marco)\s+" + Name, Opt);
    /// <summary>Fim do texto de um trecho (depois da chegada).</summary>
    private static readonly Regex TrechoEndRx = new(@";|\.\s+(?=\p{Lu})|\bdeste\b|\bda[íi]\b|\bdali\b|\bfinalmente\b|\bdo\s+(?:v[ée]rtice|ponto|marco)\b", Opt);
    private static readonly Regex ConfRx = new(
        @"(?:confrontando|confronta(?:-se)?|limitando-se|limita-se|fazendo\s+divisa|divisando|lindeiro)\s+(?:com|a)\s+(?<c>.+?)" +
        @"(?=\s*(?:,\s*(?:com\s+(?:o\s+|um\s+)?(?:azimute|rumo)|medindo|mede|na\s+extens|com\s+(?:a\s+)?dist|e\s+dist|em\s+(?:uma\s+)?dist|at[ée]\b|fechando|segue)|;|\.\s|\n|$))", Opt);
    private static readonly Regex AreaRx = new(@"[áa]rea\s+(?:total\s+|superficial\s+|de\s+terreno\s+|do\s+terreno\s+)?(?:de\s+|com\s+|:\s*|=\s*|igual\s+a\s+)?" + Num + @"\s*(?<u>m²|m2|metros\s+quadrados|ha\b|hectares?)", Opt);
    private static readonly Regex PerimRx = new(@"per[íi]metro\s+(?:total\s+)?(?:de\s+|com\s+|:\s*|=\s*|igual\s+a\s+)?" + Num + @"\s*(?:m\b|metros)", Opt);
    private static readonly Regex ZoneRx = new(@"(?:fuso|zona)\s*(?:UTM\s*)?:?\s*(?<z>\d{1,2})\s*(?<h>[NS])?(?![\d\p{L}])|\bUTM\s*(?:zona\s*)?(?<z>\d{2})\s*(?<h>[NS])\b", Opt);
    private static readonly Regex McRx = new(@"(?:\bMC\b|meridiano\s+central)\s*:?\s*(?<mc>-?\d{2})\s*°?\s*(?<w>W|O|WGr)?", Opt);
    private static readonly Regex NorthHemiRx = new(@"hemisf[ée]rio\s+norte", Opt);

    public static CertidaoData Parse(string text)
    {
        var d = new CertidaoData();
        text = OcrCleanup.Normalize(text ?? "");
        if (text.Trim().Length == 0) return d;
        // Parágrafos em uma linha só (o PDF e o OCR quebram as linhas no meio das frases); as linhas de tabela ficam.
        string flat = Flatten(text);
        string desc = Description(flat);
        d.Description = desc;

        // ---- Vértices com coordenadas (memorial, SIGEF; tabela só com cabeçalho).
        d.Coordinates = PerimeterText.ParseCoordinates(desc.Length > 0 ? desc : text, tableNeedsHeader: true);
        if (d.Coordinates.Vertices.Count < 3 && desc.Length > 0)
        {
            var whole = PerimeterText.ParseCoordinates(text, tableNeedsHeader: true);
            if (whole.Vertices.Count >= 3) d.Coordinates = whole;
        }
        d.Coordinates.Datum ??= CoordinateTableReader.DetectDatum(flat);

        // ---- Trechos: saída, chegada, distância, azimute e confrontante.
        d.Trechos.AddRange(ReadTrechos(desc.Length > 0 ? desc : flat));

        if (d.HasCoordinates)
        {
            // Conferência e confrontantes na ordem dos vértices (vértice i → i+1, fechando no primeiro).
            d.Confrontantes.AddRange(ApplyTrechos(d.Coordinates, d.Trechos));
        }
        else
        {
            foreach (var t in d.Trechos.Where(t => t.Azimuth is not null && t.Distance is not null))
            {
                d.Courses.Add(new Course { AzimuthDeg = t.Azimuth!.Value, Distance = t.Distance!.Value, Source = $"{t.From} → {t.To}" });
                d.Confrontantes.Add(t.Confrontante);
            }
            // Só as medidas dos lados (frente, fundos, laterais).
            if (!d.HasCourses)
            {
                // Certidões antigas: só as medidas dos lados. Sem descrição marcada, vale o texto até o proprietário/registro anterior.
                string sideText = desc;
                if (sideText.Length == 0)
                {
                    var stop = DescStopRx.Match(flat);
                    sideText = stop.Success && stop.Index > 40 ? flat[..stop.Index] : flat;
                }
                ReadSides(sideText, d);
                if (d.Sides.Count < 3) ReadSidesClauses(sideText, d);
                if (d.Sides.Any(s => s.Lado == "frente") && d.Sides.Any(s => s.Lado is "direito" or "esquerdo")) BuildFromSides(d);
                else if (d.Sides.Any(s => s.Lado is "sul" or "norte") && d.Sides.Any(s => s.Lado is "leste" or "oeste")) BuildFromCardinal(d);
            }
        }

        // ---- Área e perímetro do documento (conferência do desenho).
        var areaM = DescEndRx.Match(flat) is { Success: true } end && AreaRx.Match(end.Value) is { Success: true } ae ? ae : AreaRx.Match(flat);
        if (areaM.Success && CoordinateTableReader.Num(areaM.Groups["v"].Value) is double av)
            d.Area = areaM.Groups["u"].Value.StartsWith("h", StringComparison.OrdinalIgnoreCase) ? av * 10000 : av;
        if (PerimRx.Match(flat) is { Success: true } p && CoordinateTableReader.Num(p.Groups["v"].Value) is double pv) d.Perimetro = pv;

        // ---- Fuso (ou meridiano central) e hemisfério.
        (d.UtmZone, bool? south) = ReadZone(flat);
        if (south is bool sh) d.South = sh;

        if (!d.HasCoordinates && !d.HasCourses)
            d.Warnings.Add(d.Trechos.Count > 0 && d.Trechos.All(t => t.Azimuth is null)
                ? "A descrição traz distâncias, mas não traz coordenadas nem azimutes: não dá para desenhar só com as distâncias."
                : "Não foi encontrada a descrição do perímetro (coordenadas, azimutes e distâncias ou as medidas dos lados).");
        return d;
    }

    // ------------------------------------------------------------------ descrição e trechos

    /// <summary>Recorta a descrição do perímetro (do início – "Partindo do vértice…", "DESCRIÇÃO DA PROPRIEDADE" – até "fechando…").</summary>
    public static string Description(string flat)
    {
        var s = DescStartRx.Match(flat);
        if (!s.Success) return "";
        var e = DescEndRx.Match(flat, s.Index);
        // Sem "fechando…": vai até o que vem depois da descrição (proprietário, responsável técnico, registros).
        var stop = DescStopRx.Match(flat, s.Index);
        int end = e.Success ? e.Index + e.Length : stop.Success ? stop.Index : flat.Length;
        return flat[s.Index..end].Trim();
    }

    /// <summary>
    /// Trechos da descrição. Cada "até o vértice X" é a chegada de um trecho; o texto do trecho vai do fim do trecho anterior
    /// até o primeiro ";" (ou "deste", "do vértice"…) depois da chegada – assim o confrontante e a distância escritos antes
    /// ("segue confrontando com…, distância…, até o vértice") ou depois ("até o vértice P-02 em uma distância de…, confrontando com…")
    /// ficam no trecho certo.
    /// </summary>
    public static List<CertidaoTrecho> ReadTrechos(string desc)
    {
        var res = new List<CertidaoTrecho>();
        var arrivals = ToRx.Matches(desc).Cast<Match>().ToList();
        int start = 0;
        string prevTo = "";
        foreach (var a in arrivals)
        {
            if (a.Index < start) continue;
            var endM = TrechoEndRx.Match(desc, a.Index + a.Length);
            int end = endM.Success ? endM.Index : desc.Length;
            string seg = desc[start..end];
            string beforeArrival = desc[start..a.Index];
            var from = FromRx.Matches(beforeArrival).Cast<Match>().LastOrDefault();
            var conf = ConfRx.Match(seg);
            res.Add(new CertidaoTrecho
            {
                From = from is not null ? CleanVertexName(from.Groups["n"].Value) : prevTo,
                To = CleanVertexName(a.Groups["n"].Value),
                Distance = MemorialTextParser.FirstDistance(seg),
                Azimuth = MemorialTextParser.FirstAzimuth(seg),
                Confrontante = conf.Success ? CleanConfrontante(conf.Groups["c"].Value) : "",
                Text = seg.Trim(),
            });
            prevTo = res[^1].To;
            start = end;
        }
        return res;
    }

    private static string CleanVertexName(string n) => Regex.Replace(n.Trim().TrimEnd('.', ',', ';', ':', '/'), @"\s+", "");

    /// <summary>Trecho que vai de <paramref name="from"/> a <paramref name="to"/> (pelos nomes; senão o de mesma posição).</summary>
    private static CertidaoTrecho? MatchTrecho(IReadOnlyList<CertidaoTrecho> ts, string from, string to, int index)
    {
        static bool Eq(string a, string b) => string.Equals(a.Replace(" ", ""), b.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
        return ts.FirstOrDefault(t => Eq(t.To, to) && Eq(t.From, from))
               ?? ts.FirstOrDefault(t => Eq(t.To, to))
               ?? (index < ts.Count && !ts.Any(t => Eq(t.To, to)) ? ts[index] : null);
    }

    /// <summary>Junta as linhas de cada parágrafo; mantém a quebra nas linhas de tabela (com tabulação) e entre parágrafos.</summary>
    public static string Flatten(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            sb.Append(lines[i]);
            if (i == lines.Length - 1) break;
            bool table = lines[i].Contains('\t') || lines[i + 1].Contains('\t');
            bool para = lines[i].Trim().Length == 0 || lines[i + 1].Trim().Length == 0;
            sb.Append(table || para ? '\n' : ' ');
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ vértices × trechos

    /// <summary>
    /// Liga os trechos da descrição aos vértices com coordenadas (pelos nomes "do vértice X até o vértice Y"): devolve o confrontante
    /// de cada divisa (vértice i → i+1) e, quando a descrição traz distâncias, troca a conferência pelos valores de cada trecho
    /// (distância e, se houver, azimute). Serve para a certidão e para o texto colado na aba Coordenadas.
    /// </summary>
    public static List<string> ApplyTrechos(CoordinateTextResult c, IReadOnlyList<CertidaoTrecho> trechos)
    {
        var names = c.Vertices.Select(v => v.Name).ToList();
        var confs = new List<string>();
        var stated = new List<Course>();
        for (int i = 0; i < names.Count; i++)
        {
            var t = MatchTrecho(trechos, names[i], names[(i + 1) % names.Count], i);
            confs.Add(t?.Confrontante ?? "");
            stated.Add(new Course
            {
                AzimuthDeg = t?.Azimuth ?? double.NaN, Distance = t?.Distance ?? double.NaN,
                Source = t is null ? "" : $"{t.From} → {t.To}",
            });
        }
        // Só substitui a conferência quando os trechos trazem pelo menos tantos valores quanto a leitura anterior.
        int mine = stated.Count(x => !double.IsNaN(x.Distance));
        int before = c.StatedCourses.Count(x => !double.IsNaN(x.Distance));
        if (mine > 0 && mine >= before)
        {
            c.StatedCourses.Clear();
            c.StatedCourses.AddRange(stated);
        }
        return confs;
    }

    /// <summary>Trechos de um texto qualquer (memorial colado, certidão): limpa o OCR, junta as linhas e recorta a descrição.</summary>
    public static List<CertidaoTrecho> TrechosOf(string text)
    {
        string flat = Flatten(OcrCleanup.Normalize(text ?? ""));
        string desc = Description(flat);
        return ReadTrechos(desc.Length > 0 ? desc : flat);
    }

    /// <summary>Fuso UTM (ou pelo meridiano central) e hemisfério escritos no texto (null = não informado).</summary>
    public static (int? Zone, bool? South) ReadZone(string text)
    {
        int? zone = null;
        bool? south = null;
        if (ZoneRx.Match(text) is { Success: true } z && int.TryParse(z.Groups["z"].Value, out int zn) && zn is >= 1 and <= 60)
        {
            zone = zn;
            if (z.Groups["h"].Success) south = z.Groups["h"].Value.Equals("S", StringComparison.OrdinalIgnoreCase);
        }
        else if (McRx.Match(text) is { Success: true } mc && int.TryParse(mc.Groups["mc"].Value, out int cm))
        {
            if (mc.Groups["w"].Success || cm >= 30) cm = -Math.Abs(cm);
            int zz = (int)Math.Floor((cm + 183) / 6.0);
            if (zz is >= 1 and <= 60) zone = zz;
        }
        if (NorthHemiRx.IsMatch(text)) south = false;
        return (zone, south);
    }

    // ------------------------------------------------------------------ confrontantes

    /// <summary>"o Lote 06 de propriedade de João da Silva" → "João da Silva"; "a RUA A" → "Rua A".</summary>
    public static string CleanConfrontante(string c)
    {
        c = Regex.Replace(c, @"\s+", " ").Trim().Trim(',', ';', '.', ':');
        var own = Regex.Match(c, @"(?:de\s+)?(?:propriedade|posse)\s+(?:de|do|da|dos|das)\s+(?<o>.+)|pertencente\s+(?:a|ao|à|aos|às)\s+(?<o>.+)|(?:terreno|im[óo]vel|lote)\s+(?:de|do|da)\s+(?<o>(?!terceiros)\p{Lu}.+)", Opt);
        if (own.Success) c = own.Groups["o"].Value;
        c = Regex.Replace(c, @"^(?:o|a|os|as|um|uma)\s+", "", Opt);
        c = Regex.Replace(c, @",?\s*(?:brasileir[oa]|casad[oa]|solteir[oa]|portador|inscrit[oa]|CPF|RG)\b.*$", "", Opt).Trim().TrimEnd(',');
        if (c.Length > 90) c = c[..90].TrimEnd();
        return CleanName(c);
    }

    /// <summary>Confrontante que é uma via pública (vira testada).</summary>
    public static bool IsStreet(string c) =>
        Regex.IsMatch(c.Trim(), @"^(?:Rua|R\.|Avenida|Av\.|Travessa|Tv\.|Estrada|Rodovia|Alameda|Praça|Via|Viela|Beco|Logradouro)\b", Opt);

    // ------------------------------------------------------------------ medidas dos lados

    private static readonly (string Lado, string Rx)[] SideWords =
    {
        ("frente", @"frente"),
        ("fundos", @"fundos?"),
        ("direito", @"(?:lado|lateral)\s+direit[oa]|direita"),
        ("esquerdo", @"(?:lado|lateral)\s+esquerd[oa]|esquerda"),
    };

    private static void ReadSides(string text, CertidaoData d)
    {
        foreach (var (lado, rx) in SideWords)
        {
            // "12,00 m (doze metros) de frente para a Rua X" ou "pela frente, medindo 12,00 m, confrontando com a Rua X".
            var a = new Regex(Num + @"\s*(?:m\b|metros)(?:\s*\([^)]*\))?\s*(?:de|pel[oa]s?|na|no|do|da|à)?\s*(?:o\s+|a\s+)?(?:" + rx + @")(?<rest>[^;.\n]*)", Opt).Match(text);
            var b = new Regex(@"(?:pel[oa]s?|n[oa]s?|à|a)\s+(?:" + rx + @")\s*[,:]?\s*(?:medindo|mede|com|de)?\s*" + Num + @"\s*(?:m\b|metros)(?:\s*\([^)]*\))?(?<rest>[^;.\n]*)", Opt).Match(text);
            var m = a.Success && (!b.Success || a.Index <= b.Index) ? a : b;
            if (!m.Success || CoordinateTableReader.Num(m.Groups["v"].Value) is not double v || v <= 0) continue;
            string rest = m.Groups["rest"].Value;
            string conf = "";
            var cm = ConfRx.Match(rest);
            if (cm.Success) conf = CleanConfrontante(cm.Groups["c"].Value);
            else if (Regex.Match(rest, @"^\s*,?\s*(?:para|com|de\s+frente\s+para)\s+(?<c>[^,;]+)", Opt) is { Success: true } pm) conf = CleanConfrontante(pm.Groups["c"].Value);
            d.Sides.Add(new CertidaoSide(lado, v, conf));
        }
    }

    private static readonly Regex SideKeyRx = new(
        @"\b(?:(?:ao|a|à)\s+(?<c>norte|sul|l[ec]ste|o[ec]ste|[ec]ste)\b|(?<f>frente)\b|(?:ao\s+)?(?<u>fundos?)\b|(?:pel[oa]\s+)?(?:lado|lateral)\s+(?<l>direit[oa]|esquerd[oa])\b)", Opt);
    private static string NormCard(string c)
    {
        c = c.ToLowerInvariant();
        return c.EndsWith("ste") ? (c.StartsWith("o") ? "oeste" : "leste") : c;
    }
    private static readonly Regex SideValueRx = new(@"(?:m[ec]dindo|m[ec]de|medida\s+de|com\s+extens[ãa]o\s+de)?\s*" + Num + @"\s*(?:m\b|m[ec]tros)", Opt);
    private static readonly Regex SideConfRx = new(@"^\s*[,:]?\s*(?:limitando-se\s+|limita-se\s+|confrontando\s+)?(?:com|para)\s+(?<c>.+?)(?=\s*,?\s*(?:m[ec]dindo|m[ec]de)\b|\s*,?\s*" + Num + @"\s*(?:m\b|m[ec]tros)|$)", Opt);

    /// <summary>
    /// Lados escritos por cláusula: "Frente com a Rua G, medindo 12,00 metros; ao fundo … medindo 12,00 metros; pelo lado direito …" ou
    /// "limita-se ao Norte, com …, medindo 76 metros; ao Sul, com …". Cada palavra de lado abre uma cláusula que vai até a próxima.
    /// </summary>
    private static void ReadSidesClauses(string text, CertidaoData d)
    {
        d.Sides.Clear();
        var keys = SideKeyRx.Matches(text).Cast<Match>().ToList();
        for (int i = 0; i < keys.Count; i++)
        {
            var k = keys[i];
            int end = i + 1 < keys.Count ? keys[i + 1].Index : text.Length;
            string clause = text[(k.Index + k.Length)..end];
            // A cláusula acaba onde o texto sai da descrição ("situado a…", "nesta cidade", "totalizando…").
            var cut = Regex.Match(clause, @"\b(?:situad[oa]|nesta\s+cidade|totalizando|perfazendo|nos\s+termos)\b", Opt);
            if (cut.Success) clause = clause[..cut.Index];
            var vm = SideValueRx.Match(clause);
            if (!vm.Success || CoordinateTableReader.Num(vm.Groups["v"].Value) is not double v || v <= 0) continue;
            string lado = k.Groups["c"].Success ? (NormCard(k.Groups["c"].Value))
                : k.Groups["f"].Success ? "frente"
                : k.Groups["u"].Success ? "fundos"
                : k.Groups["l"].Value.StartsWith("d", StringComparison.OrdinalIgnoreCase) ? "direito" : "esquerdo";
            string conf = SideConfRx.Match(clause) is { Success: true } cm ? CleanConfrontante(cm.Groups["c"].Value) : "";
            if (d.Sides.Any(x => x.Lado == lado))
            {
                var first = d.Sides.First(x => x.Lado == lado);
                if (Math.Abs(first.Medida - v) > 0.005)
                    d.Warnings.Add($"O documento cita o lado {lado} duas vezes (com {first.Medida:0.##} m e {v:0.##} m): foi usado o primeiro. Confira o texto.");
                continue;
            }
            d.Sides.Add(new CertidaoSide(lado, v, conf));
        }
    }

    /// <summary>
    /// Lados pelos pontos cardeais (norte, sul, leste, oeste): o lado sul na horizontal, o oeste e o leste na vertical (ângulos retos
    /// nos cantos do sul) e o norte ligando as pontas – a direção é a real (o desenho já fica com o norte para cima).
    /// </summary>
    private static void BuildFromCardinal(CertidaoData d)
    {
        var sul = d.Sides.FirstOrDefault(s => s.Lado == "sul");
        var norte = d.Sides.FirstOrDefault(s => s.Lado == "norte");
        var leste = d.Sides.FirstOrDefault(s => s.Lado == "leste");
        var oeste = d.Sides.FirstOrDefault(s => s.Lado == "oeste");
        double B = (sul ?? norte)!.Medida, E = (leste ?? oeste)!.Medida, W = (oeste ?? leste)!.Medida;
        var back = new Geometry.Vec2(-B, W - E);
        d.Courses.Clear();
        d.Courses.Add(new Course { AzimuthDeg = 90, Distance = B, Source = "lado sul" });
        d.Courses.Add(new Course { AzimuthDeg = 0, Distance = E, Source = "lado leste" });
        d.Courses.Add(new Course { AzimuthDeg = CoordinateService.AzimuthOfEnuVector(back), Distance = back.Length, Source = "lado norte" });
        d.Courses.Add(new Course { AzimuthDeg = 180, Distance = W, Source = "lado oeste" });
        d.Confrontantes.Clear();
        d.Confrontantes.Add((sul ?? norte)!.Confrontante == "" ? "" : (sul ?? norte)!.Confrontante);
        d.Confrontantes.Add(leste?.Confrontante ?? "");
        d.Confrontantes.Add(norte?.Confrontante ?? "");
        d.Confrontantes.Add(oeste?.Confrontante ?? "");
        if (sul is null) { d.Confrontantes[0] = ""; d.Confrontantes[2] = norte!.Confrontante; }
        d.FromSides = true;
        d.Warnings.Add("O documento traz só as medidas dos lados (norte, sul, leste e oeste), sem ângulos: o desenho supõe ângulos retos nos cantos do lado sul e já fica orientado (norte para cima). Confira os ângulos.");
        double nm = norte?.Medida ?? back.Length;
        if (sul is not null && norte is not null && Math.Abs(back.Length - nm) > Math.Max(0.1, nm * 0.005))
            d.Warnings.Add($"Com esses lados, o lado norte mediria {back.Length:0.00} m e o documento diz {nm:0.00} m: os ângulos não são retos – ajuste os azimutes na tabela.");
    }

    /// <summary>Perímetro pelas medidas: frente no sentido leste (azimute 90°), laterais perpendiculares à frente, fundos ligando as laterais.</summary>
    private static void BuildFromSides(CertidaoData d)
    {
        double F = d.Sides.First(s => s.Lado == "frente").Medida;
        var dir = d.Sides.FirstOrDefault(s => s.Lado == "direito");
        var esq = d.Sides.FirstOrDefault(s => s.Lado == "esquerdo");
        var fun = d.Sides.FirstOrDefault(s => s.Lado == "fundos");
        double R = (dir ?? esq)!.Medida, L = (esq ?? dir)!.Medida;
        var back = new Geometry.Vec2(-F, L - R);
        d.Courses.Clear();
        d.Courses.Add(new Course { AzimuthDeg = 90, Distance = F, Source = "frente" });
        d.Courses.Add(new Course { AzimuthDeg = 0, Distance = R, Source = "lado direito" });
        d.Courses.Add(new Course { AzimuthDeg = CoordinateService.AzimuthOfEnuVector(back), Distance = back.Length, Source = "fundos" });
        d.Courses.Add(new Course { AzimuthDeg = 180, Distance = L, Source = "lado esquerdo" });
        d.Confrontantes.Clear();
        d.Confrontantes.Add(d.Sides.First(s => s.Lado == "frente").Confrontante);
        d.Confrontantes.Add(dir?.Confrontante ?? "");
        d.Confrontantes.Add(fun?.Confrontante ?? "");
        d.Confrontantes.Add(esq?.Confrontante ?? "");
        d.FromSides = true;
        d.Warnings.Add("O documento traz só as medidas dos lados, sem ângulos: o desenho supõe as laterais perpendiculares à frente (frente para o sul). Confira e gire o desenho no Revit para a posição real.");
        if (fun is not null && Math.Abs(back.Length - fun.Medida) > 0.05)
            d.Warnings.Add($"Com as laterais perpendiculares, os fundos dariam {back.Length:0.00} m e o documento diz {fun.Medida:0.00} m: os ângulos não são retos – ajuste os azimutes na tabela.");
    }

    // ------------------------------------------------------------------ nomes

    private static bool IsAllCaps(string s) => s.Any(char.IsLetter) && s.Where(char.IsLetter).All(char.IsUpper);

    /// <summary>Nome limpo; em maiúsculas vira "Fulano de Tal" ("RESIDENCIAL ESPERANÇA B – ÁREA PÚBLICA" → "Residencial Esperança B – Área Pública").</summary>
    public static string CleanName(string s)
    {
        s = Regex.Replace(s, @"\s+", " ").Trim().Trim(',', ';', '.', ':', '-', '–').Trim();
        if (!IsAllCaps(s)) return s;
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var small = new HashSet<string> { "de", "da", "do", "das", "dos", "e" };
        var words = s.ToLower(pt).Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            var w = words[i];
            if (w.Length == 0 || (i > 0 && small.Contains(w))) continue;
            // Números romanos e letras soltas ficam em maiúsculas (Residencial Jardim Porto Franco I, Quadra B).
            if (w.Length == 1 || Regex.IsMatch(w, "^(?:ii|iii|iv|vi|vii|viii|ix|xi|xii)$")) { words[i] = w.ToUpper(pt); continue; }
            words[i] = char.ToUpper(w[0], pt) + w[1..];
        }
        return string.Join(" ", words);
    }
}

/// <summary>Palavras do documento a destacar na revisão: as que têm os números usados no desenho.</summary>
public static class DocumentHighlight
{
    public static List<DocWord> Find(IEnumerable<DocWord> words, IEnumerable<double> angles, IEnumerable<double> values)
    {
        var angs = angles.Where(a => !double.IsNaN(a)).ToList();
        var vals = values.Where(v => !double.IsNaN(v) && Math.Abs(v) >= 1).ToList();
        var res = new List<DocWord>();
        foreach (var w in words)
        {
            bool angle = w.Text.Contains('°') || w.Text.Contains('º');
            // Coordenadas e distâncias vêm com casas decimais: "12," (quadra 12) ou "2026" não são destacados.
            if (!angle && !Regex.IsMatch(w.Text, @"\d[.,]\d")) continue;
            // "236184,12/N" e "7,35m," trazem letras grudadas: compara cada número da palavra.
            foreach (Match m in Regex.Matches(w.Text, angle ? @"-?\d{1,3}\s*°[^A-Za-z]*" : @"\d[\d.,]*\d"))
            {
                var n = OcrCleanup.Number(m.Value);
                if (n is not double v) continue;
                if (angle ? angs.Any(a => PerimeterText.AngleDiff(a, v) < 1.5 / 3600) : vals.Any(x => Math.Abs(x - v) < 0.0006 + Math.Abs(x) * 1e-9))
                {
                    res.Add(w);
                    break;
                }
            }
        }
        return res;
    }
}
