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
    /// <summary>Divisa escrita no texto ("Frente", "Fundos", "Lado direito", "Lado esquerdo"); vazio = não informada.</summary>
    public string Lado { get; set; } = "";
    /// <summary>O texto do trecho não repete o confrontante: veio do trecho anterior ("deste segue com 292°… até o vértice M-06").</summary>
    public bool Inherited { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>Linha do quadro de confrontações do memorial ("Frente - RUA 07 - 24,00 m").</summary>
public sealed record CertidaoConfrontacao(string Lado, string Confrontante, double Medida);

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
    /// <summary>Quadro "CONFRONTAÇÕES" do fim do memorial (para conferir as medidas de cada divisa).</summary>
    public List<CertidaoConfrontacao> Confrontacoes { get; } = new();
    /// <summary>Passo da última casa da área e do perímetro escritos (m² e m): a tolerância da conferência.</summary>
    public double AreaStep { get; set; } = 0.01;
    public double PerimetroStep { get; set; } = 0.01;
    /// <summary>Área e perímetro do documento para a conferência do desenho.</summary>
    public StatedFigures Figures => new() { Area = Area, AreaStep = AreaStep, Perimeter = Perimetro, PerimeterStep = PerimetroStep };
    /// <summary>Perímetro escrito em outro lugar do documento com valor diferente (ex.: cabeçalho lido errado pelo OCR).</summary>
    public double? PerimetroOutro { get; set; }
    /// <summary>Trechos montados das medidas dos lados (ângulos supostos de 90° na frente).</summary>
    public bool FromSides { get; set; }
    /// <summary>Área e perímetro escritos no documento (só para conferir o desenho).</summary>
    public double? Area { get; set; }
    public double? Perimetro { get; set; }
    /// <summary>Fuso UTM escrito no documento (null = não informado).</summary>
    public int? UtmZone { get; set; }
    public bool South { get; set; } = true;
    public List<string> Warnings { get; } = new();

    /// <summary>Modelo do texto identificado na leitura ("Memorial com coordenadas UTM (E, N)"…).</summary>
    public string Model { get; set; } = "";
    /// <summary>Como o modelo foi identificado (rótulos, faixas de valores, cabeçalho da tabela…).</summary>
    public List<string> Evidence { get; } = new();
    /// <summary>Números lidos errados e corrigidos pela conferência cruzada.</summary>
    public List<ReadingFix> Fixes { get; } = new();
    /// <summary>Números com cara de lidos errado (casas decimais fora do padrão do documento): pedem confirmação antes de desenhar.</summary>
    public List<string> Doubts { get; } = new();
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
        @"(?=\s*(?:,?\s*(?:com\s+(?:o\s+|um\s+|os\s+seguintes\s+|as\s+seguintes\s+|o\s+seguinte\s+)?(?:azimutes?|rumos?|dist)|com\s+(?:os|as)\s+seguintes)|,\s*(?:medindo|mede|na\s+extens|com\s+(?:a\s+)?dist|e\s+dist|em\s+(?:uma\s+)?dist|at[ée]\b|fechando|segue)|;|(?<!\b\p{L}{1,3})\.\s|\n|$))", Opt);
    /// <summary>Divisa escrita antes do confrontante: "segue Fundos confrontando…", "pelo lado direito, confrontando…".</summary>
    private static readonly Regex LadoRx = new(@"\b(?<l>frente|fundos?|lado\s+direito|lado\s+esquerdo|lateral\s+direita|lateral\s+esquerda)\b", Opt);
    /// <summary>Linha do quadro de confrontações: "Frente - RUA 07 - 24,00 m", "Lado Esquerdo LOTE 26 - … - 25,00 m".</summary>
    private static readonly Regex ConfLineRx = new(
        @"^\s*(?:(?<l>frente|fundos?|lado\s+direito|lado\s+esquerdo|lateral\s+direita|lateral\s+esquerda)\s*[-–:]?\s*)?(?<c>\S.*?)\s*[-–:]\s*" + Num + @"\s*(?:m\b|metros)\s*(?:e\s*)?$", Opt);
    private static readonly Regex AreaRx = new(@"[áa]rea\s*(?:\([^)\n]{0,60}\)\s*)?(?:(?:total|superficial|para\s+retifica[çc][ãa]o|para\s+regulariza[çc][ãa]o|a\s+retificar|retificada|regularizada|de\s+terreno|do\s+terreno|do\s+lote|do\s+im[óo]vel|da\s+gleba)\s*)?(?:de\s+|com\s+|:\s*|=\s*|igual\s+a\s+)?" + Num + @"\s*(?<u>m²|m2|metros\s+quadrados|ha\b|hectares?)" +
        // "Área para retificaçã etificação: 360,00 m²" (rótulo com palavras estragadas pelo OCR, terminando em ":"). Área construída,
        // edificada, útil, privativa ou comum não é a do terreno.
        @"|[áa]rea\b(?![\p{L} ]{0,40}?(?:constru|edifica|[úu]til|privativ|comum|cobert))[\p{L} ]{1,40}?:\s*" + Num + @"\s*(?<u>m²|m2|metros\s+quadrados|ha\b|hectares?)", Opt);
    private static readonly Regex PerimRx = new(@"per[íi]metro\s*(?:total\s+)?(?:de\s+|com\s+|:\s*|=\s*|igual\s+a\s+)?" + Num + @"\s*(?:m\b|metros)", Opt);
    private static readonly Regex ZoneRx = new(@"(?:fuso|zona)\s*(?:UTM\s*)?:?\s*(?<z>\d{1,2})\s*(?<h>[NS])?(?![\d\p{L}])|\bUTM\s*(?:zona\s*)?(?<z>\d{2})\s*(?<h>[NS])\b", Opt);
    private static readonly Regex McRx = new(@"(?:\bMC\b|meridiano\s+central)\s*:?\s*(?<mc>-?\d{2})\s*°?\s*(?<w>W|O|WGr)?", Opt);
    private static readonly Regex NorthHemiRx = new(@"hemisf[ée]rio\s+norte", Opt);

    public static CertidaoData Parse(string text) => Parse(text, ReadingModel.Auto);

    /// <param name="model">Modelo de texto esperado (Automático identifica sozinho).</param>
    public static CertidaoData Parse(string text, ReadingModel model)
    {
        var d = new CertidaoData();
        text = OcrCleanup.Normalize(text ?? "");
        if (text.Trim().Length == 0) return d;
        // Parágrafos em uma linha só (o PDF e o OCR quebram as linhas no meio das frases); as linhas de tabela ficam.
        string flat = Flatten(text);
        string desc = Description(flat);
        d.Description = desc;

        // ---- Vértices com coordenadas (memorial, SIGEF; tabela só com cabeçalho).
        d.Coordinates = PerimeterText.ParseCoordinates(desc.Length > 0 ? desc : text, true, model, out var smartUsed);
        if (d.Coordinates.Vertices.Count < 3 && desc.Length > 0)
        {
            var whole = PerimeterText.ParseCoordinates(text, true, model, out var smartWhole);
            if (whole.Vertices.Count >= 3) { d.Coordinates = whole; smartUsed = smartWhole; }
        }
        if (smartUsed is { HasCoordinates: true } su && d.HasCoordinates)
        {
            d.Model = su.Model;
            d.Evidence.AddRange(su.Evidence);
            d.Fixes.AddRange(su.Fixes);
        }
        if (d.HasCoordinates) d.Doubts.AddRange(d.Coordinates.Doubts);
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
            // Leitura inteligente dos trechos (rumos, tabelas, valores lidos errados corrigidos pelo fechamento): fica a melhor.
            var cr = ReadingChoice.Courses(desc.Length > 0 ? desc : text, model);
            if (cr.FromSmart && (model != ReadingModel.Auto || ReadingChoice.CourseScore(cr.Courses) > ReadingChoice.CourseScore(d.Courses)))
            {
                var confs = d.Confrontantes.ToList();
                d.Courses.Clear();
                d.Courses.AddRange(cr.Courses);
                d.Confrontantes.Clear();
                // Os confrontantes dos trechos do texto, se a quantidade de trechos for a mesma.
                var trechoConfs = d.Trechos.Select(t => t.Confrontante).ToList();
                var src = confs.Count == d.Courses.Count ? confs : trechoConfs.Count == d.Courses.Count ? trechoConfs : null;
                for (int i = 0; i < d.Courses.Count; i++) d.Confrontantes.Add(src?[i] ?? "");
                d.Warnings.AddRange(cr.Warnings);
                d.Fixes.AddRange(cr.Fixes);
                d.Doubts.AddRange(cr.Doubts);
            }
            if (d.HasCourses)
            {
                d.Model = cr.Model.Length > 0 && !cr.Model.StartsWith("Memorial") && !cr.Model.StartsWith("Tabela") ? cr.Model : "Azimutes e distâncias";
                d.Evidence.AddRange(cr.Evidence);
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
        ReadFigures(flat, desc, d);
        // Quadro de confrontações (conferência das medidas de cada divisa) e a divisa dos trechos que não a dizem.
        d.Confrontacoes.AddRange(ReadConfrontacoes(text));
        foreach (var t in d.Trechos.Where(t => t.Lado.Length == 0 && t.Confrontante.Length > 0))
            if (BestConfrontacao(d.Confrontacoes, t.Confrontante) is { } q) t.Lado = q.Lado;

        // ---- Fuso (ou meridiano central) e hemisfério.
        (d.UtmZone, bool? south) = ReadZone(flat);
        if (south is bool sh) d.South = sh;

        if (!d.HasCoordinates && !d.HasCourses)
            d.Warnings.Add(d.Trechos.Count > 0 && d.Trechos.All(t => t.Azimuth is null)
                ? "A descrição traz distâncias, mas não traz coordenadas nem azimutes: não dá para desenhar só com as distâncias."
                : "Não foi encontrada a descrição do perímetro (coordenadas, azimutes e distâncias ou as medidas dos lados).");
        return d;
    }

    /// <summary>Área e perímetro escritos (e o passo da última casa de cada um).</summary>
    private static void ReadFigures(string flat, string desc, CertidaoData d)
    {
        var areaM = DescEndRx.Match(flat) is { Success: true } end && AreaRx.Match(end.Value) is { Success: true } ae ? ae : AreaRx.Match(flat);
        if (areaM.Success && CoordinateTableReader.Num(areaM.Groups["v"].Value) is double av)
        {
            bool ha = areaM.Groups["u"].Value.StartsWith("h", StringComparison.OrdinalIgnoreCase);
            d.Area = ha ? av * 10000 : av;
            d.AreaStep = WrittenPrecision.NumberStep(areaM.Groups["v"].Value) * (ha ? 10000 : 1);
        }
        // Perímetro: o escrito na própria descrição ("ponto inicial … perímetro de 98,00 m") vale mais que o do cabeçalho.
        var perims = PerimRx.Matches(flat).Cast<Match>().Select(m => (m.Index, V: CoordinateTableReader.Num(m.Groups["v"].Value), Raw: m.Groups["v"].Value)).Where(x => x.V is > 0).ToList();
        int descAt = desc.Length > 0 ? flat.IndexOf(desc, StringComparison.Ordinal) : -1;
        var inDesc = descAt >= 0 ? perims.Where(x => x.Index >= descAt && x.Index < descAt + desc.Length).ToList() : new();
        var main = inDesc.Count > 0 ? inDesc[^1] : perims.FirstOrDefault();
        if (main.V is double pv)
        {
            d.Perimetro = pv;
            d.PerimetroStep = WrittenPrecision.NumberStep(main.Raw);
            var other = perims.FirstOrDefault(x => x.V is double o && Math.Abs(o - pv) > 0.005);
            if (other.V is double ov) d.PerimetroOutro = ov;
        }
    }

    /// <summary>Área e perímetro escritos num texto colado (para a conferência do desenho).</summary>
    public static StatedFigures FiguresOf(string text)
    {
        var d = new CertidaoData();
        string norm = OcrCleanup.Normalize(text ?? "");
        if (norm.Trim().Length == 0) return StatedFigures.None;
        string flat = Flatten(norm);
        ReadFigures(flat, Description(flat), d);
        return d.Figures;
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
            // Divisa ("Frente", "Fundos"…) escrita antes do confrontante.
            var lado = LadoRx.Match(conf.Success ? seg[..conf.Index] : beforeArrival);
            var t = new CertidaoTrecho
            {
                From = from is not null ? CleanVertexName(from.Groups["n"].Value) : prevTo,
                To = CleanVertexName(a.Groups["n"].Value),
                Distance = MemorialTextParser.FirstDistance(seg),
                Azimuth = MemorialTextParser.FirstAzimuth(seg),
                Confrontante = conf.Success ? CleanConfrontante(conf.Groups["c"].Value) : "",
                Lado = lado.Success ? LadoName(lado.Groups["l"].Value) : "",
                Text = seg.Trim(),
            };
            // "deste segue com 292°12'38" e 12,00m até o vértice M-06": o mesmo confrontante e a mesma divisa do trecho anterior.
            if (t.Confrontante.Length == 0 && t.Lado.Length == 0 && res.Count > 0 && res[^1].Confrontante.Length > 0 &&
                !Regex.IsMatch(beforeArrival, @"confront|limit|divisa|lindeiro", Opt))
            {
                t.Confrontante = res[^1].Confrontante;
                t.Lado = res[^1].Lado;
                t.Inherited = true;
            }
            res.Add(t);
            prevTo = res[^1].To;
            start = end;
        }
        return res;
    }

    /// <summary>Linha do quadro de confrontações com o nome mais parecido (palavras em comum; null = nenhuma parecida).</summary>
    public static CertidaoConfrontacao? BestConfrontacao(IEnumerable<CertidaoConfrontacao> list, string name)
    {
        var a = Words(name);
        if (a.Count == 0) return null;
        CertidaoConfrontacao? best = null;
        double bestScore = 0;
        foreach (var q in list)
        {
            var b = Words(q.Confrontante);
            if (b.Count == 0) continue;
            double score = (double)a.Intersect(b).Count() / Math.Max(a.Count, b.Count);
            if (score > bestScore) { bestScore = score; best = q; }
        }
        return bestScore >= 0.5 ? best : null;
    }

    /// <summary>Palavras significativas de um nome (sem acento, minúsculas, sem "de/da/do", números e letras soltas).</summary>
    public static HashSet<string> Words(string s)
    {
        var plain = new string(s.Normalize(System.Text.NormalizationForm.FormD).Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray());
        var stop = new HashSet<string> { "de", "da", "do", "das", "dos", "e", "lote", "lt", "quadra", "qd" };
        return Regex.Matches(plain.ToLowerInvariant(), @"[a-z]{2,}").Select(m => m.Value).Where(w => !stop.Contains(w)).ToHashSet();
    }

    /// <summary>Nome padronizado da divisa.</summary>
    public static string LadoName(string l)
    {
        l = Regex.Replace(l.Trim().ToLowerInvariant(), @"\s+", " ");
        return l switch
        {
            "frente" => "Frente",
            "fundo" or "fundos" => "Fundos",
            "lado direito" or "lateral direita" => "Lado direito",
            "lado esquerdo" or "lateral esquerda" => "Lado esquerdo",
            _ => l,
        };
    }

    /// <summary>
    /// Quadro "CONFRONTAÇÕES" do memorial: uma linha por divisa, com a medida; linhas sem a divisa (continuação, "… - 12,00m e")
    /// ficam com a divisa da linha anterior.
    /// </summary>
    public static List<CertidaoConfrontacao> ReadConfrontacoes(string text)
    {
        var res = new List<CertidaoConfrontacao>();
        var head = Regex.Match(text, @"^\s*confronta[çc][õo]es\s*:?\s*$|^\s*confrontantes\s*:?\s*$", Opt | RegexOptions.Multiline);
        if (!head.Success) return res;
        string lado = "";
        int blank = 0;
        foreach (var raw in text[(head.Index + head.Length)..].Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) { if (++blank > 2 && res.Count > 0) break; continue; }
            if (Regex.IsMatch(line, @"^confronta[çc][õo]es", Opt)) continue;   // título repetido pelo OCR
            var m = ConfLineRx.Match(line);
            if (!m.Success || CoordinateTableReader.Num(m.Groups["v"].Value) is not double v || v <= 0)
            {
                if (res.Count > 0) break;   // fim do quadro
                continue;
            }
            blank = 0;
            if (m.Groups["l"].Success) lado = LadoName(m.Groups["l"].Value);
            res.Add(new CertidaoConfrontacao(lado, CleanConfrontante(m.Groups["c"].Value.Trim().Trim('-', '–', ':').Trim()), v));
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
        var matched = MatchedTrechos(names, trechos);
        for (int i = 0; i < names.Count; i++)
        {
            var t = matched[i];
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
            // A precisão com que cada valor foi escrito (lida antes) continua valendo para os mesmos valores.
            for (int i = 0; i < stated.Count && i < c.StatedCourses.Count; i++)
            {
                var o = c.StatedCourses[i];
                if (Math.Abs(o.Distance - stated[i].Distance) < 1e-9) stated[i].DistanceStep = o.DistanceStep;
                if (!double.IsNaN(stated[i].AzimuthDeg) && PerimeterText.AngleDiff(o.AzimuthDeg, stated[i].AzimuthDeg) < 1e-9) stated[i].AzimuthStep = o.AzimuthStep;
            }
            c.StatedCourses.Clear();
            c.StatedCourses.AddRange(stated);
        }
        return confs;
    }

    /// <summary>Trecho de cada divisa (vértice i → i+1, fechando no primeiro), pelos nomes; null = sem trecho no texto.</summary>
    public static List<CertidaoTrecho?> MatchedTrechos(IReadOnlyList<string> names, IReadOnlyList<CertidaoTrecho> trechos) =>
        names.Select((n, i) => MatchTrecho(trechos, n, names[(i + 1) % names.Count], i)).ToList();

    /// <summary>
    /// Trechos de um texto qualquer (memorial colado, certidão): limpa o OCR, junta as linhas, recorta a descrição e completa a divisa
    /// pelo quadro de confrontações.
    /// </summary>
    public static List<CertidaoTrecho> TrechosOf(string text)
    {
        string norm = OcrCleanup.Normalize(text ?? "");
        string flat = Flatten(norm);
        string desc = Description(flat);
        var res = ReadTrechos(desc.Length > 0 ? desc : flat);
        var quadro = ReadConfrontacoes(norm);
        foreach (var t in res.Where(t => t.Lado.Length == 0 && t.Confrontante.Length > 0))
            if (BestConfrontacao(quadro, t.Confrontante) is { } q) t.Lado = q.Lado;
        return res;
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
        // Separador entre o lote e o nome: "LOTE 09- JOÃO", "LOTE 10 - ALFA" → "Lote 09 – João", "Lote 10 – Alfa".
        c = Regex.Replace(c, @"(?<=\d)\s*[-–]\s+(?=\p{L})|(?<=\d)\s+[-–]\s*(?=\p{L})", " – ");
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
        @"\b(?:(?:ao|a|à)\s+(?<c>norte|sul|l[ec]ste|o[ec]ste|[ec]ste)\b|(?<f>frente)\b|(?:ao\s+)?(?<u>fundos?)\b|(?:pel[oa]\s+)?(?:lado|lateral)\s+(?<l>direit[oa]|esquerd[oa])\b|" +
        // "pela direita com o lote nº 19, medindo 30 metros", "à esquerda, com…"
        @"(?:pel[oa]|à|a)\s+(?<l>direit[oa]|esquerd[oa])\b)", Opt);
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
        d.Model = "Medidas dos lados por ponto cardeal (norte, sul, leste, oeste), sem ângulos";
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
        d.Model = "Medidas dos lados (frente, fundos e laterais), sem ângulos";
        d.Warnings.Add("O documento traz só as medidas dos lados, sem ângulos: o desenho supõe as laterais perpendiculares à frente (frente para o sul). Confira e gire o desenho no Revit para a posição real.");
        if (fun is not null && Math.Abs(back.Length - fun.Medida) > 0.05)
            d.Warnings.Add($"Com as laterais perpendiculares, os fundos dariam {back.Length:0.00} m e o documento diz {fun.Medida:0.00} m: os ângulos não são retos – ajuste os azimutes na tabela.");
    }

    // ------------------------------------------------------------------ nomes

    /// <summary>Texto em maiúsculas (aceita poucas minúsculas, como o "d" de "Jd." lido pelo OCR).</summary>
    private static bool IsAllCaps(string s)
    {
        var letters = s.Where(char.IsLetter).ToList();
        return letters.Count > 0 && letters.Count(char.IsUpper) >= letters.Count * 0.85;
    }

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

/// <summary>Tipo do que foi lido (cor do destaque na revisão).</summary>
public enum HighlightKind { Coordenada, Azimute, Distancia, Confrontante, Area }

public sealed record Highlight(DocWord Word, HighlightKind Kind);

/// <summary>Palavras do documento a destacar na revisão: as que têm os números usados no desenho.</summary>
public static class DocumentHighlight
{
    private static bool AngleWord(DocWord w) => w.Text.Contains('°') || w.Text.Contains('º');

    private static double? NumberIn(DocWord w, IEnumerable<double> vals, bool angle)
    {
        if (!angle && !Regex.IsMatch(w.Text, @"\d[.,]\d")) return null;
        foreach (Match m in Regex.Matches(w.Text, angle ? @"-?\d{1,3}\s*°[^A-Za-z]*" : @"\d[\d.,]*\d"))
        {
            if (OcrCleanup.Number(m.Value) is not double v) continue;
            foreach (var x in vals)
                if (angle ? PerimeterText.AngleDiff(x, v) < 1.5 / 3600 || PerimeterText.AngleDiff(Math.Abs(x), Math.Abs(v)) < 1.5 / 3600
                          : Math.Abs(x - v) < 0.0006 + Math.Abs(x) * 1e-9)
                    return x;
        }
        return null;
    }

    /// <summary>
    /// Destaques por tipo: coordenadas (UTM ou geográficas), azimutes, distâncias, área e as palavras dos confrontantes (só em
    /// sequências de duas ou mais palavras do nome, para não pintar um "Silva" solto no rodapé).
    /// </summary>
    public static List<Highlight> Classify(IReadOnlyList<DocWord> words, IEnumerable<double> coords, IEnumerable<double> geoAngles,
        IEnumerable<double> azimuths, IEnumerable<double> distances, IEnumerable<double> areas, IEnumerable<string> confrontantes)
    {
        var co = coords.Where(v => !double.IsNaN(v) && Math.Abs(v) >= 1).ToList();
        var ga = geoAngles.Where(v => !double.IsNaN(v)).ToList();
        var az = azimuths.Where(v => !double.IsNaN(v)).ToList();
        var di = distances.Where(v => !double.IsNaN(v) && v > 0).ToList();
        var ar = areas.Where(v => !double.IsNaN(v) && v > 0).ToList();
        var res = new List<Highlight>();
        var taken = new HashSet<DocWord>();
        foreach (var w in words)
        {
            HighlightKind? k = null;
            if (AngleWord(w))
            {
                if (NumberIn(w, az, true) is not null) k = HighlightKind.Azimute;
                else if (NumberIn(w, ga, true) is not null) k = HighlightKind.Coordenada;
            }
            else if (NumberIn(w, co, false) is not null) k = HighlightKind.Coordenada;
            else if (NumberIn(w, di, false) is not null) k = HighlightKind.Distancia;
            else if (NumberIn(w, ar, false) is not null) k = HighlightKind.Area;
            if (k is HighlightKind kk) { res.Add(new Highlight(w, kk)); taken.Add(w); }
        }
        // Confrontantes: sequências de palavras do nome na ordem de leitura.
        var names = confrontantes.Where(c => c.Trim().Length > 0).Select(CertidaoParser.Words).Where(x => x.Count > 0).ToList();
        if (names.Count == 0) return res;
        var all = names.SelectMany(x => x).ToHashSet();
        var ordered = words.OrderBy(w => w.Page).ThenBy(w => Math.Round(w.CenterY / Math.Max(1, w.Height))).ThenBy(w => w.X0).ToList();
        var run = new List<DocWord>();
        void Flush()
        {
            var ws = run.SelectMany(w => CertidaoParser.Words(w.Text)).ToHashSet();
            if (run.Count >= 2 || names.Any(n => n.Count == 1 && n.SetEquals(ws)))
                foreach (var w in run.Where(w => !taken.Contains(w))) res.Add(new Highlight(w, HighlightKind.Confrontante));
            run.Clear();
        }
        foreach (var w in ordered)
        {
            var ws = CertidaoParser.Words(w.Text);
            bool hit = ws.Count > 0 && ws.All(all.Contains);
            bool connector = ws.Count == 0 && Regex.IsMatch(w.Text, @"^(?:d[aeo]s?|e|[-–]|\d+[-–]?)$", RegexOptions.IgnoreCase);
            if (hit || (connector && run.Count > 0)) run.Add(w);
            else Flush();
        }
        Flush();
        return res;
    }

    /// <summary>Primeira palavra com o valor (para rolar a revisão até ela ao clicar numa linha da tabela).</summary>
    public static DocWord? FindValue(IEnumerable<DocWord> words, double value, bool angle) =>
        words.FirstOrDefault(w => AngleWord(w) == angle && NumberIn(w, new[] { value }, angle) is not null);

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
