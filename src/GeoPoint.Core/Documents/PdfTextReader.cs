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
                int letters = page.Letters.Count(l => !string.IsNullOrWhiteSpace(l.Value));
                if (letters < MinLettersPerPage) { res.PagesWithoutText.Add(idx); continue; }
                foreach (var w in page.GetWords())
                {
                    if (string.IsNullOrWhiteSpace(w.Text)) continue;
                    var b = w.BoundingBox;
                    // PDF: origem embaixo à esquerda (pontos) → origem em cima à esquerda.
                    res.Words.Add(new DocWord(idx, w.Text, b.Left, page.Height - b.Top, b.Right, page.Height - b.Bottom));
                }
            }
        }
        return res;
    }
}
