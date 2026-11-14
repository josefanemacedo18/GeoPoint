using UglyToad.PdfPig;

namespace GeoPoint.Core.Documents;

/// <summary>
/// Texto de um PDF digital (camada de texto), com a posição de cada palavra – sem OCR, os números saem exatos.
/// Páginas escaneadas (só imagem) ficam em <see cref="ExtractedDocument.PagesWithoutText"/> para o OCR.
/// </summary>
public static class PdfTextReader
{
    /// <summary>Menos letras que isso numa página = página escaneada (só imagem).</summary>
    public const int MinLettersPerPage = 25;

    public static ExtractedDocument Read(string path)
    {
        var res = new ExtractedDocument { Path = path };
        PdfDocument doc;
        try { doc = PdfDocument.Open(path); }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Não foi possível abrir o PDF (arquivo protegido por senha ou danificado?): " + ex.Message, ex);
        }
        using (doc)
        {
            foreach (var page in doc.GetPages())
            {
                int idx = page.Number - 1;
                res.Pages.Add(new DocPage(idx, page.Width, page.Height, false));
                // Só o texto na horizontal: o texto vertical da margem (código de validação da assinatura digital) fica de fora.
                var words = page.GetWords()
                    .Where(w => !string.IsNullOrWhiteSpace(w.Text) && w.TextOrientation == UglyToad.PdfPig.Content.TextOrientation.Horizontal)
                    .Select(w => (Word: w, Size: w.Letters.Count == 0 ? w.BoundingBox.Height : w.Letters.Max(l => l.PointSize)))
                    .ToList();
                int letters = words.Sum(x => x.Word.Text.Length);
                if (letters < MinLettersPerPage) { res.PagesWithoutText.Add(idx); continue; }
                // Letras bem menores que as do texto (selos, rodapés de emolumentos) ficam de fora.
                var sizes = words.Select(x => x.Size).OrderBy(v => v).ToList();
                double body = sizes[sizes.Count / 2];
                foreach (var (w, size) in words)
                {
                    if (size < body * 0.72) continue;
                    var b = w.BoundingBox;
                    // Caixa pela linha de base e pelo tamanho da fonte (altura igual para todas as palavras da linha – a altura do
                    // desenho das letras varia: "uma" é mais baixa que "distância"). Origem em cima à esquerda.
                    double baseY = w.Letters.Count > 0 ? w.Letters.Average(l => l.StartBaseLine.Y) : b.Bottom;
                    res.Words.Add(new DocWord(idx, w.Text, b.Left, page.Height - (baseY + size * 0.78), b.Right, page.Height - (baseY - size * 0.22)));
                }
            }
        }
        return res;
    }
}
