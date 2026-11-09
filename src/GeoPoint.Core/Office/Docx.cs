using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GeoPoint.Core.Office;

public enum ParaAlign { Left, Center, Right, Justify }

public sealed record RunSpec(string Text, bool Bold = false, bool Italic = false, double? SizePt = null, bool Underline = false, string? Font = null);

public sealed class ParaSpec
{
    public ParaAlign Align { get; init; } = ParaAlign.Left;
    public double SpaceBeforePt { get; init; }
    public double SpaceAfterPt { get; init; } = 6;
    public double LineSpacing { get; init; } = 1.15;
    public double FirstLineIndentCm { get; init; }
    public double LeftIndentCm { get; init; }
    public bool KeepNext { get; init; }
    public bool PageBreakBefore { get; init; }
}

public sealed class CellSpec
{
    public CellSpec(string text, bool bold = false, ParaAlign align = ParaAlign.Left, string? shade = null)
    { Text = text; Bold = bold; Align = align; Shade = shade; }
    public string Text { get; }
    public bool Bold { get; }
    public ParaAlign Align { get; }
    /// <summary>Cor de fundo hexadecimal (ex.: "D9D9D9").</summary>
    public string? Shade { get; }
    public int GridSpan { get; init; } = 1;
}

/// <summary>Construção de elementos WordprocessingML (sem dependências externas).</summary>
public static class Wml
{
    public static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static readonly XNamespace Xml = XNamespace.Xml;

    private static string Twips(double cm) => ((int)Math.Round(cm * 567)).ToString();

    public static XElement Run(RunSpec r)
    {
        var rpr = new XElement(W + "rPr");
        if (r.Font is not null) rpr.Add(new XElement(W + "rFonts", new XAttribute(W + "ascii", r.Font), new XAttribute(W + "hAnsi", r.Font), new XAttribute(W + "cs", r.Font)));
        if (r.Bold) rpr.Add(new XElement(W + "b"), new XElement(W + "bCs"));
        if (r.Italic) rpr.Add(new XElement(W + "i"), new XElement(W + "iCs"));
        if (r.Underline) rpr.Add(new XElement(W + "u", new XAttribute(W + "val", "single")));
        if (r.SizePt is double s)
        {
            string hp = ((int)Math.Round(s * 2)).ToString();
            rpr.Add(new XElement(W + "sz", new XAttribute(W + "val", hp)), new XElement(W + "szCs", new XAttribute(W + "val", hp)));
        }
        var run = new XElement(W + "r");
        if (rpr.HasElements) run.Add(rpr);
        var lines = (r.Text ?? "").Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) run.Add(new XElement(W + "br"));
            run.Add(new XElement(W + "t", new XAttribute(Xml + "space", "preserve"), Clean(lines[i])));
        }
        return run;
    }

    /// <summary>Remove caracteres inválidos em XML.</summary>
    public static string Clean(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            if (ch == '\t' || ch == '\n' || ch == '\r' || (ch >= 0x20 && ch <= 0xD7FF) || (ch >= 0xE000 && ch <= 0xFFFD)) sb.Append(ch);
        return sb.ToString();
    }

    public static XElement ParaProps(ParaSpec p)
    {
        var ppr = new XElement(W + "pPr");
        if (p.KeepNext) ppr.Add(new XElement(W + "keepNext"));
        if (p.PageBreakBefore) ppr.Add(new XElement(W + "pageBreakBefore"));
        ppr.Add(new XElement(W + "spacing",
            new XAttribute(W + "before", ((int)Math.Round(p.SpaceBeforePt * 20)).ToString()),
            new XAttribute(W + "after", ((int)Math.Round(p.SpaceAfterPt * 20)).ToString()),
            new XAttribute(W + "line", ((int)Math.Round(p.LineSpacing * 240)).ToString()),
            new XAttribute(W + "lineRule", "auto")));
        if (p.FirstLineIndentCm != 0 || p.LeftIndentCm != 0)
        {
            var ind = new XElement(W + "ind");
            if (p.LeftIndentCm != 0) ind.Add(new XAttribute(W + "left", Twips(p.LeftIndentCm)));
            if (p.FirstLineIndentCm != 0) ind.Add(new XAttribute(W + "firstLine", Twips(p.FirstLineIndentCm)));
            ppr.Add(ind);
        }
        ppr.Add(new XElement(W + "jc", new XAttribute(W + "val", p.Align switch
        {
            ParaAlign.Center => "center",
            ParaAlign.Right => "right",
            ParaAlign.Justify => "both",
            _ => "left",
        })));
        return ppr;
    }

    public static XElement Paragraph(ParaSpec spec, params RunSpec[] runs)
    {
        var p = new XElement(W + "p", ParaProps(spec));
        foreach (var r in runs) if (!string.IsNullOrEmpty(r.Text)) p.Add(Run(r));
        return p;
    }

    public static XElement Paragraph(string text, ParaSpec? spec = null, bool bold = false, double? size = null) =>
        Paragraph(spec ?? new ParaSpec(), new RunSpec(text, bold, SizePt: size));

    public static XElement EmptyParagraph(double afterPt = 0) => new(W + "p", ParaProps(new ParaSpec { SpaceAfterPt = afterPt }));

    public static XElement PageBreak() => new(W + "p", new XElement(W + "r", new XElement(W + "br", new XAttribute(W + "type", "page"))));

    /// <summary>Tabela com bordas simples. Larguras em cm (a soma deve caber na página).</summary>
    public static XElement Table(IReadOnlyList<IReadOnlyList<CellSpec>> rows, IReadOnlyList<double> widthsCm, bool headerRepeat = true, double fontPt = 9)
    {
        XElement Border(string side) => new(W + side, new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "808080"));
        var tbl = new XElement(W + "tbl",
            new XElement(W + "tblPr",
                new XElement(W + "tblW", new XAttribute(W + "w", Twips(widthsCm.Sum())), new XAttribute(W + "type", "dxa")),
                new XElement(W + "jc", new XAttribute(W + "val", "center")),
                new XElement(W + "tblBorders", Border("top"), Border("left"), Border("bottom"), Border("right"), Border("insideH"), Border("insideV")),
                new XElement(W + "tblLayout", new XAttribute(W + "type", "fixed")),
                new XElement(W + "tblCellMar",
                    new XElement(W + "left", new XAttribute(W + "w", "57"), new XAttribute(W + "type", "dxa")),
                    new XElement(W + "right", new XAttribute(W + "w", "57"), new XAttribute(W + "type", "dxa")))),
            new XElement(W + "tblGrid", widthsCm.Select(w => new XElement(W + "gridCol", new XAttribute(W + "w", Twips(w))))));

        for (int ri = 0; ri < rows.Count; ri++)
        {
            var tr = new XElement(W + "tr");
            var trPr = new XElement(W + "trPr", new XElement(W + "cantSplit"));
            if (ri == 0 && headerRepeat) trPr.Add(new XElement(W + "tblHeader"));
            tr.Add(trPr);
            int col = 0;
            foreach (var c in rows[ri])
            {
                double w = 0;
                for (int k = 0; k < c.GridSpan && col + k < widthsCm.Count; k++) w += widthsCm[col + k];
                col += c.GridSpan;
                var tcPr = new XElement(W + "tcPr", new XElement(W + "tcW", new XAttribute(W + "w", Twips(w)), new XAttribute(W + "type", "dxa")));
                if (c.GridSpan > 1) tcPr.Add(new XElement(W + "gridSpan", new XAttribute(W + "val", c.GridSpan.ToString())));
                if (c.Shade is not null) tcPr.Add(new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "color", "auto"), new XAttribute(W + "fill", c.Shade)));
                tcPr.Add(new XElement(W + "vAlign", new XAttribute(W + "val", "center")));
                var para = Paragraph(new ParaSpec { Align = c.Align, SpaceAfterPt = 0, LineSpacing = 1.0 }, new RunSpec(c.Text, c.Bold, SizePt: fontPt));
                if (!para.Elements(W + "r").Any()) para.Add(Run(new RunSpec("", SizePt: fontPt)));
                tr.Add(new XElement(W + "tc", tcPr, para));
            }
            tbl.Add(tr);
        }
        return tbl;
    }
}

/// <summary>Pacote .docx mínimo (documento, estilos, configurações) – usado para gerar o modelo padrão.</summary>
public static class DocxPackage
{
    public static byte[] Create(IEnumerable<XElement> body, string defaultFont = "Calibri", double defaultSizePt = 11, string title = "Memorial Descritivo")
    {
        var W = Wml.W;
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(W + "document",
                new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", Wml.R.NamespaceName),
                new XElement(W + "body", body,
                    new XElement(W + "sectPr",
                        new XElement(W + "pgSz", new XAttribute(W + "w", "11906"), new XAttribute(W + "h", "16838")),
                        new XElement(W + "pgMar", new XAttribute(W + "top", "1418"), new XAttribute(W + "right", "1134"), new XAttribute(W + "bottom", "1134"),
                            new XAttribute(W + "left", "1701"), new XAttribute(W + "header", "709"), new XAttribute(W + "footer", "709"), new XAttribute(W + "gutter", "0"))))));

        string hp = ((int)Math.Round(defaultSizePt * 2)).ToString();
        var styles = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(W + "styles", new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                new XElement(W + "docDefaults",
                    new XElement(W + "rPrDefault", new XElement(W + "rPr",
                        new XElement(W + "rFonts", new XAttribute(W + "ascii", defaultFont), new XAttribute(W + "hAnsi", defaultFont), new XAttribute(W + "cs", defaultFont), new XAttribute(W + "eastAsia", defaultFont)),
                        new XElement(W + "sz", new XAttribute(W + "val", hp)), new XElement(W + "szCs", new XAttribute(W + "val", hp)),
                        new XElement(W + "lang", new XAttribute(W + "val", "pt-BR"), new XAttribute(W + "eastAsia", "pt-BR"), new XAttribute(W + "bidi", "ar-SA")))),
                    new XElement(W + "pPrDefault", new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "after", "120"), new XAttribute(W + "line", "276"), new XAttribute(W + "lineRule", "auto"))))),
                new XElement(W + "style", new XAttribute(W + "type", "paragraph"), new XAttribute(W + "default", "1"), new XAttribute(W + "styleId", "Normal"),
                    new XElement(W + "name", new XAttribute(W + "val", "Normal")), new XElement(W + "qFormat")),
                new XElement(W + "style", new XAttribute(W + "type", "table"), new XAttribute(W + "default", "1"), new XAttribute(W + "styleId", "TableNormal"),
                    new XElement(W + "name", new XAttribute(W + "val", "Normal Table")),
                    new XElement(W + "tblPr", new XElement(W + "tblInd", new XAttribute(W + "w", "0"), new XAttribute(W + "type", "dxa")),
                        new XElement(W + "tblCellMar",
                            new XElement(W + "top", new XAttribute(W + "w", "0"), new XAttribute(W + "type", "dxa")),
                            new XElement(W + "left", new XAttribute(W + "w", "108"), new XAttribute(W + "type", "dxa")),
                            new XElement(W + "bottom", new XAttribute(W + "w", "0"), new XAttribute(W + "type", "dxa")),
                            new XElement(W + "right", new XAttribute(W + "w", "108"), new XAttribute(W + "type", "dxa")))))));

        var settings = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(W + "settings", new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                new XElement(W + "defaultTabStop", new XAttribute(W + "val", "708")),
                new XElement(W + "characterSpacingControl", new XAttribute(W + "val", "doNotCompress")),
                new XElement(W + "compat", new XElement(W + "compatSetting",
                    new XAttribute(W + "name", "compatibilityMode"), new XAttribute(W + "uri", "http://schemas.microsoft.com/office/word"), new XAttribute(W + "val", "15")))));

        const string contentTypes = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
            "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>" +
            "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
            "<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>" +
            "</Types>";
        const string rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
            "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/>" +
            "</Relationships>";
        const string docRels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>" +
            "</Relationships>";
        string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        string core = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
            $"<dc:title>{System.Security.SecurityElement.Escape(title)}</dc:title><dc:creator>GeoPoint</dc:creator>" +
            $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{now}</dcterms:created><dcterms:modified xsi:type=\"dcterms:W3CDTF\">{now}</dcterms:modified>" +
            "</cp:coreProperties>";
        const string app = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Application>GeoPoint</Application></Properties>";

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            Put(zip, "[Content_Types].xml", contentTypes);
            Put(zip, "_rels/.rels", rels);
            Put(zip, "word/_rels/document.xml.rels", docRels);
            Put(zip, "word/document.xml", Serialize(doc));
            Put(zip, "word/styles.xml", Serialize(styles));
            Put(zip, "word/settings.xml", Serialize(settings));
            Put(zip, "docProps/core.xml", core);
            Put(zip, "docProps/app.xml", app);
        }
        return ms.ToArray();
    }

    internal static string Serialize(XDocument d)
    {
        var sb = new StringBuilder();
        using (var w = System.Xml.XmlWriter.Create(sb, new System.Xml.XmlWriterSettings { Encoding = new UTF8Encoding(false), OmitXmlDeclaration = false }))
            d.Save(w);
        // XmlWriter com StringBuilder declara utf-16; ajusta.
        return sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"UTF-8\"");
    }

    internal static void Put(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = e.Open();
        var bytes = new UTF8Encoding(false).GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
    }
}

/// <summary>
/// Preenche um modelo .docx com marcadores {{NOME}}.
/// • Marcadores em linha são substituídos mantendo a formatação do trecho onde começam (mesmo que o Word tenha
///   dividido o marcador em vários trechos).
/// • {{NOME?}} remove o parágrafo inteiro quando o valor for vazio.
/// • Um parágrafo contendo apenas um marcador de bloco (ex.: {{CORPO}}) é substituído por parágrafos/tabelas.
/// </summary>
public static class DocxTemplate
{
    private static readonly Regex Placeholder = new(@"\{\{\s*([A-Za-z0-9_]+)(\?)?\s*\}\}", RegexOptions.CultureInvariant);

    public static byte[] Fill(byte[] template, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, IReadOnlyList<XElement>> blocks)
    {
        using var input = new MemoryStream(template);
        using var zipIn = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        bool foundDocument = false;
        using (var zipOut = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var entry in zipIn.Entries)
            {
                bool isPart = Regex.IsMatch(entry.FullName, @"^word/(document|header\d*|footer\d*)\.xml$", RegexOptions.IgnoreCase);
                using var src = entry.Open();
                if (!isPart)
                {
                    var outEntry = zipOut.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                    using var dst = outEntry.Open();
                    src.CopyTo(dst);
                    continue;
                }
                var xdoc = XDocument.Load(src, LoadOptions.PreserveWhitespace);
                bool isMain = entry.FullName.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase);
                if (isMain) foundDocument = true;
                Process(xdoc, values, isMain ? blocks : new Dictionary<string, IReadOnlyList<XElement>>(), isMain);
                var e2 = zipOut.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using var dst2 = e2.Open();
                var settings = new System.Xml.XmlWriterSettings { Encoding = new UTF8Encoding(false) };
                using var xw = System.Xml.XmlWriter.Create(dst2, settings);
                xdoc.Save(xw);
            }
        }
        if (!foundDocument) throw new InvalidDataException("O modelo não é um documento Word válido (word/document.xml ausente).");
        return output.ToArray();
    }

    private static void Process(XDocument doc, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, IReadOnlyList<XElement>> blocks, bool isMain)
    {
        var W = Wml.W;
        var paragraphs = doc.Descendants(W + "p").ToList();
        bool bodyInserted = false;
        foreach (var p in paragraphs)
        {
            if (p.Parent is null) continue;
            var texts = p.Descendants(W + "t").ToList();
            if (texts.Count == 0) continue;
            string full = string.Concat(texts.Select(t => t.Value));
            if (!full.Contains("{{")) continue;

            // Bloco: parágrafo com somente o marcador.
            var only = Placeholder.Match(full.Trim());
            if (only.Success && only.Value.Length == full.Trim().Length && blocks.TryGetValue(only.Groups[1].Value.ToUpperInvariant(), out var blockEls))
            {
                if (p.Parent?.Name == W + "body" || p.Parent?.Name == W + "tc")
                {
                    foreach (var el in blockEls) p.AddBeforeSelf(new XElement(el));
                    p.Remove();
                    bodyInserted |= only.Groups[1].Value.Equals("CORPO", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
            }

            var matches = Placeholder.Matches(full).Cast<Match>().ToList();
            if (matches.Count == 0) continue;
            bool removeParagraph = false;
            bool allEmpty = true;
            // Mapeia posição → (índice do w:t, deslocamento).
            var starts = new int[texts.Count];
            int acc = 0;
            for (int i = 0; i < texts.Count; i++) { starts[i] = acc; acc += texts[i].Value.Length; }
            (int ti, int off) Locate(int pos)
            {
                for (int i = texts.Count - 1; i >= 0; i--)
                    if (pos >= starts[i]) return (i, pos - starts[i]);
                return (0, 0);
            }
            var newValues = texts.Select(t => t.Value).ToArray();
            for (int mi = matches.Count - 1; mi >= 0; mi--)
            {
                var m = matches[mi];
                string key = m.Groups[1].Value.ToUpperInvariant();
                bool optional = m.Groups[2].Success;
                string val = values.TryGetValue(key, out var v) ? v ?? "" : "";
                if (blocks.ContainsKey(key)) val = ""; // bloco usado em linha: ignora
                if (val.Length > 0) allEmpty = false;
                if (optional && val.Length == 0) removeParagraph = true;
                val = Wml.Clean(val.Replace("\r", "").Replace("\n", " "));

                var (t0, o0) = Locate(m.Index);
                var (t1, o1) = Locate(m.Index + m.Length - 1);
                o1 += 1;
                if (t0 == t1)
                {
                    newValues[t0] = newValues[t0][..o0] + val + newValues[t0][o1..];
                }
                else
                {
                    newValues[t0] = newValues[t0][..o0] + val;
                    for (int k = t0 + 1; k < t1; k++) newValues[k] = "";
                    newValues[t1] = newValues[t1][o1..];
                }
            }
            if (removeParagraph)
            {
                p.Remove();
                continue;
            }
            for (int i = 0; i < texts.Count; i++)
            {
                if (texts[i].Value == newValues[i]) continue;
                texts[i].Value = newValues[i];
                texts[i].SetAttributeValue(Wml.Xml + "space", "preserve");
            }
            // Parágrafo composto só de marcadores vazios → remove.
            string rest = Placeholder.Replace(full, "").Trim();
            if (allEmpty && rest.Length == 0) p.Remove();
        }

        // Modelo sem {{CORPO}}: insere o corpo antes das propriedades de seção.
        if (isMain && !bodyInserted && blocks.TryGetValue("CORPO", out var corpo))
        {
            var body = doc.Root?.Element(W + "body");
            if (body is not null)
            {
                var sect = body.Elements(W + "sectPr").LastOrDefault();
                foreach (var el in corpo)
                {
                    if (sect is not null) sect.AddBeforeSelf(new XElement(el));
                    else body.Add(new XElement(el));
                }
            }
        }
    }

    /// <summary>Marcadores encontrados no modelo (para diagnóstico na tela de configurações).</summary>
    public static List<string> ListPlaceholders(byte[] template)
    {
        using var input = new MemoryStream(template);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        var res = new SortedSet<string>();
        foreach (var e in zip.Entries.Where(e => Regex.IsMatch(e.FullName, @"^word/(document|header\d*|footer\d*)\.xml$")))
        {
            using var s = e.Open();
            var x = XDocument.Load(s);
            foreach (var p in x.Descendants(Wml.W + "p"))
            {
                string full = string.Concat(p.Descendants(Wml.W + "t").Select(t => t.Value));
                foreach (Match m in Placeholder.Matches(full)) res.Add(m.Groups[1].Value.ToUpperInvariant());
            }
        }
        return res.ToList();
    }
}
