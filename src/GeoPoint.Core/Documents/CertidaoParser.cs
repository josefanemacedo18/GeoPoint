using System.Globalization;
using System.Text.RegularExpressions;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Export;

namespace GeoPoint.Core.Documents;

/// <summary>Medida de um lado sem ângulo (certidões antigas: "12,00 m de frente para a Rua X").</summary>
public sealed record CertidaoSide(string Lado, double Medida, string Confrontante);

/// <summary>O que foi lido da certidão (matrícula/inteiro teor) ou de um memorial.</summary>
public sealed class CertidaoData
{
    public string Matricula { get; set; } = "";
    public string Proprietario { get; set; } = "";
    public string Documento { get; set; } = "";
    public string Endereco { get; set; } = "";
    public string Bairro { get; set; } = "";
    public string Municipio { get; set; } = "";
    public string UF { get; set; } = "";
    public string Comarca { get; set; } = "";
    public string Cartorio { get; set; } = "";
    public string Cep { get; set; } = "";
    public string Lote { get; set; } = "";
    public string Quadra { get; set; } = "";
    public double? Area { get; set; }
    public double? Perimetro { get; set; }
    public int? UtmZone { get; set; }
    public bool South { get; set; } = true;
    /// <summary>Azimutes/rumos e distâncias, na ordem do texto (ou montados das medidas dos lados).</summary>
    public List<Course> Courses { get; } = new();
    /// <summary>Confrontante de cada trecho (mesmo índice de <see cref="Courses"/> ou dos trechos entre vértices); "" = não achado.</summary>
    public List<string> Confrontantes { get; } = new();
    public CoordinateTextResult Coordinates { get; set; } = new();
    public List<CertidaoSide> Sides { get; } = new();
    /// <summary>Trechos montados das medidas dos lados (ângulos supostos de 90° na frente).</summary>
    public bool FromSides { get; set; }
    public List<string> Warnings { get; } = new();

    public bool HasCoordinates => Coordinates.Vertices.Count >= 3;
    public bool HasCourses => Courses.Count >= 3;

    /// <summary>Campos lidos, para mostrar na revisão.</summary>
    public IEnumerable<(string Key, string Label, string Value)> Fields()
    {
        var inv = CultureInfo.GetCultureInfo("pt-BR");
        yield return ("Matricula", "Matrícula", Matricula);
        yield return ("Proprietario", "Proprietário", Proprietario);
        yield return ("Documento", "CPF/CNPJ", Documento);
        yield return ("Lote", "Lote", Lote);
        yield return ("Quadra", "Quadra", Quadra);
        yield return ("Endereco", "Endereço", Endereco);
        yield return ("Bairro", "Bairro", Bairro);
        yield return ("Cep", "CEP", Cep);
        yield return ("Municipio", "Município", Municipio);
        yield return ("UF", "UF", UF);
        yield return ("Comarca", "Comarca", Comarca);
        yield return ("Cartorio", "Cartório", Cartorio);
        yield return ("Area", "Área (m²)", Area is double a ? a.ToString("N2", inv) : "");
        yield return ("Perimetro", "Perímetro (m)", Perimetro is double p ? p.ToString("N2", inv) : "");
    }
}

/// <summary>
/// Lê uma certidão de matrícula / inteiro teor (texto já extraído do PDF ou do OCR): perímetro (coordenadas, azimutes e distâncias,
/// tabela ou só as medidas dos lados), confrontantes de cada trecho e os dados do imóvel.
/// </summary>
public static class CertidaoParser
{
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const string Num = @"(?<v>\d{1,3}(?:\.\d{3})+(?:,\d+)?|\d+(?:[.,]\d+)?)";
    private const string Ufs = "AC|AL|AP|AM|BA|CE|DF|ES|GO|MA|MT|MS|MG|PA|PB|PR|PE|PI|RJ|RN|RS|RO|RR|SC|SP|SE|TO";

    private static readonly Regex MatriculaRx = new(@"matr[íi]cula\s*(?:n[º°o.]*\s*)?[:\-–]?\s*(?<m>\d{1,3}(?:\.\d{3})+|\d{1,7})(?![\d,])", Opt);
    private static readonly Regex OwnerRx = new(@"propriet[áa]ri[oa]s?(?:\s*\(\w+\))*\s*[:\-–]\s*(?<n>[^,;\n]{3,120})", Opt);
    private static readonly Regex BuyerRx = new(@"(?:adquirentes?|outorgad[oa]s?[\s-]+comprador(?:a|es)?|compradore?s?|compradora)(?:\s*\(\w+\))*\s*[:\-–]\s*(?<n>[^,;\n]{3,120})", Opt);
    private static readonly Regex CpfRx = new(@"(?<!\d)(?:\d{3}\.?\d{3}\.?\d{3}\s*[-–]\s*\d{2}|\d{2}\.?\d{3}\.?\d{3}\s*/\s*\d{4}\s*[-–]\s*\d{2})(?!\d)", Opt);
    private static readonly Regex AreaRx = new(@"[áa]rea\s+(?:total\s+|superficial\s+|de\s+terreno\s+|do\s+terreno\s+)?(?:de\s+|com\s+|:\s*|=\s*|igual\s+a\s+)?" + Num + @"\s*(?<u>m²|m2|metros\s+quadrados|ha\b|hectares?)", Opt);
    private static readonly Regex PerimRx = new(@"per[íi]metro\s+(?:total\s+)?(?:de\s+|com\s+|:\s*|=\s*|igual\s+a\s+)?" + Num + @"\s*(?:m\b|metros)", Opt);
    private static readonly Regex LoteRx = new(@"\blote\s+(?:de\s+terreno\s+)?(?:urbano\s+)?(?:sob\s+(?:o\s+)?)?(?:n[º°o.]*\s*)?(?<l>\d{1,4}[A-Z]?)\b", Opt);
    private static readonly Regex QuadraRx = new(@"\bquadra\s+(?:n[º°o.]*\s*)?(?<q>[\dA-Z]{1,4})\b", Opt);
    private static readonly Regex StreetRx = new(@"(?:situad[oa]|localizad[oa]|sito)\s+(?:na|no|à|a|em)\s+(?<e>(?:Rua|R\.|Avenida|Av\.|Travessa|Tv\.|Estrada|Rodovia|Alameda|Praça|Via)\s[^,;\n]{2,80})(?:,\s*(?:n[º°o.]*\s*)?(?<n>\d+[A-Z]?|s/?n))?", Opt);
    private static readonly Regex BairroRx = new(@"\bbairro\s+(?:de\s+|do\s+|da\s+)?(?<b>[^,;\n.]{2,60})", Opt);
    private static readonly Regex CityRx = new(@"(?i:munic[íi]pio|cidade)\s+(?i:de)\s+(?<m>[\p{Lu}][\p{L}' ]{2,40}?)\s*(?:[-/–,]\s*|\s+)\(?(?<uf>" + Ufs + @")\)?(?![\p{L}])", RegexOptions.CultureInvariant);
    private static readonly Regex CityRx2 = new(@"(?i:munic[íi]pio|cidade)\s+(?i:de)\s+(?<m>[\p{Lu}][\p{L}' ]{2,40}?)(?=\s*[,.;\n])", RegexOptions.CultureInvariant);
    private static readonly Regex ComarcaRx = new(@"(?i:comarca)\s+(?i:de)\s+(?<c>[\p{Lu}][\p{L}' ]{2,40}?)(?=\s*(?:[-/–,.;\n(]|\s+(?:" + Ufs + @")\b|\s+Estado))", RegexOptions.CultureInvariant);
    private static readonly Regex CartorioRx = new(@"(?<c>(?:cart[óo]rio|of[íi]cio\s+de\s+registro|registro\s+de\s+im[óo]veis|serventia\s+(?:extrajudicial|registral))[^\n;]{3,140})", Opt);
    private static readonly Regex CepRx = new(@"\bCEP\s*:?\s*(?<c>\d{2}\.?\d{3}\s*-?\s*\d{3})", Opt);
    private static readonly Regex ZoneRx = new(@"(?:fuso|zona)\s*(?:UTM\s*)?:?\s*(?<z>\d{1,2})\s*(?<h>[NS])?(?![\d\p{L}])|\bUTM\s*(?:zona\s*)?(?<z>\d{2})\s*(?<h>[NS])\b", Opt);
    private static readonly Regex McRx = new(@"(?:\bMC\b|meridiano\s+central)\s*:?\s*(?<mc>-?\d{2})\s*°?\s*(?<w>W|O|WGr)?", Opt);
    private static readonly Regex NorthHemiRx = new(@"hemisf[ée]rio\s+norte", Opt);

    private static readonly Regex ConfRx = new(
        @"(?:confrontando|confronta(?:-se)?|limitando-se|limita-se|fazendo\s+divisa|divisando|lindeiro)\s+(?:com|a)\s+(?<c>.+?)(?=\s*(?:,\s*(?:com\s+(?:o\s+|um\s+)?(?:azimute|rumo)|medindo|mede|na\s+extens|com\s+(?:a\s+)?dist|e\s+dist|at[ée]\b)|;|\.\s|\n|$))", Opt);
    private static readonly Regex BoundaryRx = new(@";|\bdeste\b|\bda[íi]\b|\bdali\b", Opt);

    public static CertidaoData Parse(string text)
    {
        var d = new CertidaoData();
        text = OcrCleanup.Normalize(text ?? "");
        if (text.Trim().Length == 0) return d;
        // Parágrafos em uma linha só (o PDF e o OCR quebram as linhas no meio dos nomes); as linhas de tabela ficam.
        string flat = Flatten(text);

        ReadPropertyData(flat, d);
        // Cartório: a linha do cabeçalho (sem juntar com as linhas seguintes).
        if (CartorioRx.Match(text) is { Success: true } ca) d.Cartorio = Regex.Replace(ca.Groups["c"].Value, @"\s+", " ").Trim().TrimEnd(',', '.', '-', '–');

        // ---- Perímetro: coordenadas (memorial/SIGEF/tabela) e azimutes/distâncias.
        d.Coordinates = PerimeterText.ParseCoordinates(text);
        var found = MemorialTextParser.ParseDetailed(flat);
        d.Courses.AddRange(found.Select(f => f.Course));
        d.Confrontantes.AddRange(ConfrontantesOf(flat, found));
        if (d.Coordinates.Vertices.Count >= 3 && d.Confrontantes.Count == 0)
            d.Confrontantes.AddRange(Enumerable.Repeat("", d.Coordinates.Vertices.Count));

        // ---- Só as medidas dos lados (sem ângulos).
        ReadSides(flat, d);
        if (!d.HasCourses && !d.HasCoordinates && d.Sides.Any(s => s.Lado == "frente") && d.Sides.Any(s => s.Lado is "direito" or "esquerdo"))
            BuildFromSides(d);

        if (!d.HasCourses && !d.HasCoordinates)
            d.Warnings.Add("Não foi encontrada a descrição do perímetro (coordenadas, azimutes e distâncias ou as medidas dos lados).");
        return d;
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

    // ------------------------------------------------------------------ dados do imóvel

    private static void ReadPropertyData(string text, CertidaoData d)
    {
        if (MatriculaRx.Match(text) is { Success: true } m) d.Matricula = m.Groups["m"].Value.Replace(".", "");

        // Proprietário atual: o último adquirente (registros de compra e venda) ou o proprietário do cabeçalho.
        Match? owner = BuyerRx.Matches(text).Cast<Match>().LastOrDefault();
        if (owner is null && OwnerRx.Match(text) is { Success: true } o) owner = o;
        if (owner is not null)
        {
            d.Proprietario = CleanName(owner.Groups["n"].Value);
            var cpf = CpfRx.Match(text, owner.Index);
            if (cpf.Success && cpf.Index - owner.Index < 400) d.Documento = FormatDoc(cpf.Value);
        }
        if (d.Documento.Length == 0 && CpfRx.Match(text) is { Success: true } c0) d.Documento = FormatDoc(c0.Value);

        if (AreaRx.Match(text) is { Success: true } a && CoordinateTableReader.Num(a.Groups["v"].Value) is double av)
            d.Area = a.Groups["u"].Value.StartsWith("h", StringComparison.OrdinalIgnoreCase) ? av * 10000 : av;
        if (PerimRx.Match(text) is { Success: true } p && CoordinateTableReader.Num(p.Groups["v"].Value) is double pv) d.Perimetro = pv;

        // Lote e quadra: os do imóvel (primeiros citados, antes da descrição dos confrontantes).
        int descStart = Regex.Match(text, @"im[óo]vel\s*:|descri[çc][ãa]o\s+do\s+im[óo]vel|um\s+(?:lote|terreno)", Opt) is { Success: true } im ? im.Index : 0;
        if (LoteRx.Match(text, descStart) is { Success: true } l) d.Lote = l.Groups["l"].Value;
        if (QuadraRx.Match(text, descStart) is { Success: true } q) d.Quadra = q.Groups["q"].Value.ToUpperInvariant();
        if (StreetRx.Match(text) is { Success: true } st)
        {
            string e = CleanName(st.Groups["e"].Value);
            string n = st.Groups["n"].Success ? st.Groups["n"].Value : "";
            d.Endereco = n.Length == 0 ? e : n.StartsWith("s", StringComparison.OrdinalIgnoreCase) ? e + ", s/n" : $"{e}, nº {n}";
        }
        if (BairroRx.Match(text) is { Success: true } b) d.Bairro = CleanName(b.Groups["b"].Value);
        if (CityRx.Match(text) is { Success: true } ci) { d.Municipio = CleanName(ci.Groups["m"].Value); d.UF = ci.Groups["uf"].Value.ToUpperInvariant(); }
        else if (CityRx2.Match(text) is { Success: true } ci2) d.Municipio = CleanName(ci2.Groups["m"].Value);
        if (ComarcaRx.Match(text) is { Success: true } co) d.Comarca = CleanName(co.Groups["c"].Value);
        if (CepRx.Match(text) is { Success: true } ce)
        {
            var digits = Regex.Replace(ce.Groups["c"].Value, @"\D", "");
            if (digits.Length == 8) d.Cep = digits[..5] + "-" + digits[5..];
        }
        // Fuso UTM (e hemisfério) ou meridiano central.
        if (ZoneRx.Match(text) is { Success: true } z && int.TryParse(z.Groups["z"].Value, out int zone) && zone is >= 1 and <= 60)
        {
            d.UtmZone = zone;
            if (z.Groups["h"].Success) d.South = z.Groups["h"].Value.Equals("S", StringComparison.OrdinalIgnoreCase);
        }
        else if (McRx.Match(text) is { Success: true } mc && int.TryParse(mc.Groups["mc"].Value, out int cm))
        {
            if (mc.Groups["w"].Success || cm > 0 && cm >= 30) cm = -Math.Abs(cm);
            int zz = (int)Math.Floor((cm + 183) / 6.0);
            if (zz is >= 1 and <= 60) d.UtmZone = zz;
        }
        if (NorthHemiRx.IsMatch(text)) d.South = false;
    }

    // ------------------------------------------------------------------ confrontantes

    /// <summary>
    /// Confrontante de cada trecho: o texto de cada trecho vai do fim do trecho anterior ("; deste...") até o próximo;
    /// dentro dele, a primeira expressão "confrontando com ..." (antes ou depois do azimute e da distância).
    /// </summary>
    private static List<string> ConfrontantesOf(string text, IReadOnlyList<MemorialTextParser.Found> found)
    {
        var res = new List<string>();
        if (found.Count == 0) return res;
        var bounds = new List<int> { 0 };
        for (int k = 1; k < found.Count; k++)
        {
            var b = BoundaryRx.Match(text, found[k - 1].End);
            bounds.Add(b.Success && b.Index < found[k].Start ? b.Index : found[k - 1].End);
        }
        bounds.Add(text.Length);
        for (int k = 0; k < found.Count; k++)
        {
            int s0 = bounds[k], s1 = Math.Max(bounds[k + 1], found[k].End);
            var block = text[s0..Math.Min(s1, text.Length)];
            var m = ConfRx.Match(block);
            res.Add(m.Success ? CleanConfrontante(m.Groups["c"].Value) : "");
        }
        return res;
    }

    /// <summary>"o Lote 06 de propriedade de João da Silva" → "João da Silva"; "a Rua das Flores" → "Rua das Flores".</summary>
    public static string CleanConfrontante(string c)
    {
        c = Regex.Replace(c, @"\s+", " ").Trim().Trim(',', ';', '.', ':');
        var own = Regex.Match(c, @"(?:de\s+)?(?:propriedade|posse)\s+(?:de|do|da|dos|das)\s+(?<o>.+)|pertencente\s+(?:a|ao|à|aos|às)\s+(?<o>.+)|(?:terreno|im[óo]vel|lote)\s+(?:de|do|da)\s+(?<o>(?!terceiros)\p{Lu}.+)", Opt);
        if (own.Success) c = own.Groups["o"].Value;
        c = Regex.Replace(c, @"^(?:o|a|os|as|um|uma)\s+", "", Opt);
        c = Regex.Replace(c, @",?\s*(?:brasileir[oa]|casad[oa]|solteir[oa]|portador|inscrit[oa]|CPF|RG)\b.*$", "", Opt).Trim().TrimEnd(',');
        if (c.Length > 90) c = c[..90].TrimEnd();
        return CleanName(c, keepCase: !IsAllCaps(c) || Regex.IsMatch(c, @"^(?:Rua|Avenida|Travessa|Estrada|Rodovia|Alameda|Praça|Lote)\b", Opt) && !IsAllCaps(c));
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

    /// <summary>
    /// Perímetro pelas medidas: frente no sentido leste (azimute 90°), laterais perpendiculares à frente, fundos ligando as laterais.
    /// </summary>
    private static void BuildFromSides(CertidaoData d)
    {
        double F = d.Sides.First(s => s.Lado == "frente").Medida;
        var dir = d.Sides.FirstOrDefault(s => s.Lado == "direito");
        var esq = d.Sides.FirstOrDefault(s => s.Lado == "esquerdo");
        var fun = d.Sides.FirstOrDefault(s => s.Lado == "fundos");
        double R = (dir ?? esq)!.Medida, L = (esq ?? dir)!.Medida;
        // P1 (0,0) canto frente-esquerda → P2 (F,0) → P3 (F,R) → P4 (0,L).
        var back = new Geometry.Vec2(-F, L - R);
        double backAz = CoordinateService.AzimuthOfEnuVector(back);
        d.Courses.Clear();
        d.Courses.Add(new Course { AzimuthDeg = 90, Distance = F, Source = "frente" });
        d.Courses.Add(new Course { AzimuthDeg = 0, Distance = R, Source = "lado direito" });
        d.Courses.Add(new Course { AzimuthDeg = backAz, Distance = back.Length, Source = "fundos" });
        d.Courses.Add(new Course { AzimuthDeg = 180, Distance = L, Source = "lado esquerdo" });
        d.Confrontantes.Clear();
        d.Confrontantes.Add(d.Sides.First(s => s.Lado == "frente").Confrontante);
        d.Confrontantes.Add(dir?.Confrontante ?? "");
        d.Confrontantes.Add(fun?.Confrontante ?? "");
        d.Confrontantes.Add(esq?.Confrontante ?? "");
        d.FromSides = true;
        d.Warnings.Add("A certidão traz só as medidas dos lados, sem ângulos: o desenho supõe as laterais perpendiculares à frente (frente para o sul). Confira e gire o desenho no Revit para a posição real.");
        if (fun is not null && Math.Abs(back.Length - fun.Medida) > 0.05)
            d.Warnings.Add($"Com as laterais perpendiculares, os fundos dariam {back.Length:0.00} m e a certidão diz {fun.Medida:0.00} m: os ângulos não são retos – ajuste os azimutes na tabela.");
    }

    // ------------------------------------------------------------------ utilitários

    private static bool IsAllCaps(string s) => s.Any(char.IsLetter) && s.Where(char.IsLetter).All(char.IsUpper);

    /// <summary>Nome limpo; em maiúsculas vira "Fulano de Tal".</summary>
    public static string CleanName(string s, bool keepCase = false)
    {
        s = Regex.Replace(s, @"\s+", " ").Trim().Trim(',', ';', '.', ':', '-', '–').Trim();
        s = Regex.Replace(s, @"\s+(?:brasileir[oa]s?|casad[oa]s?|solteir[oa]s?|vi[úu]v[oa]|divorciad[oa]|portador[a]?|inscrit[oa]|residente|domiciliad[oa]|maior|menor)\b.*$", "", Opt).Trim();
        if (keepCase || !IsAllCaps(s)) return s;
        var small = new HashSet<string> { "de", "da", "do", "das", "dos", "e" };
        var words = s.ToLower(CultureInfo.GetCultureInfo("pt-BR")).Split(' ');
        for (int i = 0; i < words.Length; i++)
            if (words[i].Length > 0 && (i == 0 || !small.Contains(words[i])))
                words[i] = char.ToUpper(words[i][0], CultureInfo.GetCultureInfo("pt-BR")) + words[i][1..];
        return string.Join(" ", words);
    }

    private static string FormatDoc(string raw)
    {
        var d = Regex.Replace(raw, @"\D", "");
        return d.Length switch
        {
            11 => $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}",
            14 => $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}",
            _ => raw.Trim(),
        };
    }
}

/// <summary>Palavras do documento a destacar na revisão: as que têm os números usados no desenho.</summary>
public static class DocumentHighlight
{
    public static List<DocWord> Find(IEnumerable<DocWord> words, IEnumerable<double> angles, IEnumerable<double> values)
    {
        var angs = angles.ToList();
        var vals = values.Where(v => Math.Abs(v) >= 1).ToList();   // números pequenos demais destacariam datas e números soltos
        var res = new List<DocWord>();
        foreach (var w in words)
        {
            var n = OcrCleanup.Number(w.Text);
            if (n is not double v) continue;
            bool angle = w.Text.Contains('°') || w.Text.Contains('º');
            // Coordenadas e distâncias vêm com casas decimais: "12," (quadra 12) ou "2026" não são destacados.
            if (!angle && !Regex.IsMatch(w.Text, @"\d[.,]\d")) continue;
            if (angle ? angs.Any(a => PerimeterText.AngleDiff(a, v) < 1.5 / 3600) : vals.Any(x => Math.Abs(x - v) < 0.0006 + Math.Abs(x) * 1e-9))
                res.Add(w);
        }
        return res;
    }
}
