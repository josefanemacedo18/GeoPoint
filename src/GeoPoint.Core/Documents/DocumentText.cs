using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GeoPoint.Core.Documents;

/// <summary>Palavra de um documento com a caixa na página (unidades da página, origem no canto superior esquerdo, y para baixo).</summary>
public sealed record DocWord(int Page, string Text, double X0, double Y0, double X1, double Y1)
{
    public double Height => Y1 - Y0;
    public double CenterY => (Y0 + Y1) / 2;
}

/// <summary>Página do documento (tamanho nas mesmas unidades das palavras dela).</summary>
public sealed record DocPage(int Index, double Width, double Height, bool FromOcr);

/// <summary>Texto lido de um PDF ou de uma imagem, com as palavras e as posições (para destacar na revisão).</summary>
public sealed class ExtractedDocument
{
    public string Path { get; init; } = "";
    public List<DocPage> Pages { get; } = new();
    public List<DocWord> Words { get; } = new();
    /// <summary>Páginas de PDF sem camada de texto (escaneadas): precisam de OCR.</summary>
    public List<int> PagesWithoutText { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool UsedOcr => Pages.Any(p => p.FromOcr);

    /// <summary>Texto em linhas (colunas de tabela separadas por tabulação), já com a limpeza dos erros comuns de OCR.</summary>
    public string Text => OcrCleanup.Normalize(DocumentText.BuildText(Words));
}

/// <summary>Monta o texto em linhas a partir das palavras (ordem de leitura; colunas distantes separadas por tabulação).</summary>
public static class DocumentText
{
    /// <summary>Palavra escrita na vertical (texto girado na margem): caixa invertida ou bem mais alta que larga.</summary>
    public static bool IsVertical(DocWord w) =>
        w.Y1 <= w.Y0 || w.X1 <= w.X0 || (w.Text.Trim().Length >= 3 && (w.Y1 - w.Y0) > 1.8 * (w.X1 - w.X0));

    public static string BuildText(IEnumerable<DocWord> words)
    {
        var sb = new StringBuilder();
        foreach (var page in words.GroupBy(w => w.Page).OrderBy(g => g.Key))
        {
            if (sb.Length > 0) sb.Append("\n\n");
            var lines = new List<List<DocWord>>();
            foreach (var w in page.Where(w => w.Text.Trim().Length > 0 && !IsVertical(w)).OrderBy(w => w.CenterY).ThenBy(w => w.X0))
            {
                // Mesma linha: centro vertical dentro de 60% da altura da palavra da linha.
                var line = lines.LastOrDefault(l => Math.Abs(l.Average(x => x.CenterY) - w.CenterY) < Math.Max(w.Height, l.Average(x => x.Height)) * 0.6);
                if (line is null) lines.Add(new List<DocWord> { w });
                else line.Add(w);
            }
            foreach (var line in lines.OrderBy(l => l.Average(x => x.CenterY)))
            {
                var ws = line.OrderBy(x => x.X0).ToList();
                double h = ws.Average(x => x.Height);
                // Espaço muito maior que o normal entre palavras = outra coluna de tabela. Texto justificado tem espaços largos
                // mas parecidos entre si: linha longa (muitas palavras) só separa coluna com espaço enorme.
                double colGap = ws.Count >= 8 ? h * 4 : h * 2.2;
                for (int i = 0; i < ws.Count; i++)
                {
                    if (i > 0) sb.Append(ws[i].X0 - ws[i - 1].X1 > colGap ? '\t' : ' ');
                    sb.Append(ws[i].Text.Trim());
                }
                sb.Append('\n');
            }
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>Correções dos erros mais comuns de OCR em números, ângulos e coordenadas (só dentro de números).</summary>
public static class OcrCleanup
{
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string s = text.Replace("\r", "");
        // Palavra quebrada no fim da linha ("confron-\ntando").
        s = Regex.Replace(s, @"(\p{L})-\n(\p{Ll})", "$1$2");
        // Aspas e apóstrofos tipográficos.
        s = s.Replace('’', '\'').Replace('‘', '\'').Replace('´', '\'').Replace('`', '\'').Replace('′', '\'')
             .Replace('”', '"').Replace('“', '"').Replace('″', '"').Replace("''", "\"");
        // Símbolo de grau lido como "o", "*" ou "º" com espaços: "268o15'23"" → "268°15'23"".
        s = Regex.Replace(s, @"(?<=\b\d{1,3})\s*[o*ºª]\s*(?=\d{1,2}\s*')", "°");
        // Letras no meio de números: O/o → 0, l/I/| → 1 (ex.: "9.298.53l,391", "2O,00").
        s = Regex.Replace(s, @"(?<=\d[.,]?)[Oo](?=[\d.,]*\d)", "0");
        s = Regex.Replace(s, @"(?<=\d[.,]?)[lI|](?=[\d.,]*\d)", "1");
        // Espaço depois do ponto de milhar ("9.298. 531,391" → "9.298.531,391"). Vírgula seguida de espaço é separador de lista: fica.
        s = Regex.Replace(s, @"(?<=\d\.)\s+(?=\d{3}[.,]\d)", "");
        // Ângulo com espaços entre as partes: "268° 15' 23"" → "268°15'23"".
        s = Regex.Replace(s, @"(\d{1,3})\s*°\s*(\d{1,2})\s*'\s*(\d{1,2}(?:[.,]\d+)?)\s*""", "$1°$2'$3\"");
        s = Regex.Replace(s, @"(\d{1,3})\s*°\s*(\d{1,2})\s*'(?!\d)", "$1°$2'");
        // Metro quadrado.
        s = Regex.Replace(s, @"\bm\s*2\b|\bm\^2\b", "m²");
        return s;
    }

    /// <summary>Valor numérico de um texto de documento (número com vírgula/ponto ou ângulo °'"); null = não é número.</summary>
    public static double? Number(string text)
    {
        string t = Normalize(text).Trim().TrimEnd(',', ';', '.', ')', ':').TrimStart('(');
        t = Regex.Replace(t, @"\s*(m²|m|ha)$", "");
        if (t.Contains('°') && Geodesy.AngleText.TryParseAngle(t, out double a)) return a;
        return Export.CoordinateTableReader.Num(t);
    }
}
