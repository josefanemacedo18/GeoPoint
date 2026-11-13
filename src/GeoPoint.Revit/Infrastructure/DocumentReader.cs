using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GeoPoint.Core.Documents;
using WpfImaging = System.Windows.Media.Imaging;
using WinImaging = Windows.Graphics.Imaging;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>Documento lido (texto e posições) e as imagens das páginas para a revisão.</summary>
public sealed class ReadDocument
{
    public ExtractedDocument Doc { get; init; } = new();
    /// <summary>Imagem de cada página (mesma ordem de <see cref="ExtractedDocument.Pages"/>); null = sem imagem.</summary>
    public List<WpfImaging.BitmapSource?> PageImages { get; } = new();
    /// <summary>Idioma do OCR usado ("" = sem OCR).</summary>
    public string OcrLanguage { get; set; } = "";
}

/// <summary>
/// Lê uma certidão ou memorial em PDF ou imagem:
/// PDF digital → texto do próprio PDF (exato); PDF escaneado, foto ou imagem → OCR do Windows (sem instalar nada e sem enviar o
/// documento para fora do computador). As páginas também viram imagens para a revisão lado a lado.
/// </summary>
public static class DocumentReader
{
    public const int MaxPages = 20;

    public static bool IsPdf(string path) => Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public static async Task<ReadDocument> ReadAsync(string path, IProgress<string>? progress = null)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Arquivo não encontrado.", path);
        return IsPdf(path) ? await ReadPdfAsync(path, progress) : await ReadImageAsync(path, progress);
    }

    // ------------------------------------------------------------------ PDF

    private static async Task<ReadDocument> ReadPdfAsync(string path, IProgress<string>? progress)
    {
        progress?.Report("Lendo o texto do PDF…");
        var doc = PdfTextReader.Read(path);
        var res = new ReadDocument { Doc = doc };
        if (doc.Pages.Count > MaxPages) doc.Warnings.Add($"O PDF tem {doc.Pages.Count} páginas: só as {MaxPages} primeiras foram lidas.");
        Windows.Data.Pdf.PdfDocument? pdf = null;
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask();
            pdf = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file).AsTask();
        }
        catch (Exception ex)
        {
            Logger.Error("PDF (imagens das páginas)", ex);
            doc.Warnings.Add("Não foi possível mostrar as páginas do PDF: " + ex.Message);
        }
        Windows.Media.Ocr.OcrEngine? engine = null;
        int n = Math.Min(doc.Pages.Count, MaxPages);
        for (int i = 0; i < n; i++)
        {
            WpfImaging.BitmapSource? img = null;
            bool scanned = doc.PagesWithoutText.Contains(i);
            if (pdf is not null && i < pdf.PageCount)
            {
                try
                {
                    progress?.Report(scanned ? $"Página {i + 1}: lendo a imagem (OCR)…" : $"Página {i + 1}…");
                    using var page = pdf.GetPage((uint)i);
                    // Página escaneada: 300 dpi para o OCR; página com texto: ~190 dpi só para ver.
                    double dpi = scanned ? 300 : 190;
                    uint w = (uint)Math.Max(200, Math.Round(page.Size.Width / 96.0 * dpi));
                    using var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                    await page.RenderToStreamAsync(ras, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = w }).AsTask();
                    ras.Seek(0);
                    var dec = await WinImaging.BitmapDecoder.CreateAsync(ras).AsTask();
                    using var sb = await dec.GetSoftwareBitmapAsync(WinImaging.BitmapPixelFormat.Bgra8, WinImaging.BitmapAlphaMode.Premultiplied).AsTask();
                    img = await ToWpfAsync(sb);
                    if (scanned)
                    {
                        engine ??= CreateEngine(out string lang);
                        res.OcrLanguage = engine.RecognizerLanguage.DisplayName;
                        var words = await OcrAsync(engine, sb, i);
                        doc.Pages[i] = new DocPage(i, sb.PixelWidth, sb.PixelHeight, true);
                        doc.Words.AddRange(words);
                    }
                }
                catch (Exception ex) when (ex is not OcrUnavailableException)
                {
                    Logger.Error($"PDF página {i + 1}", ex);
                    if (scanned) doc.Warnings.Add($"Página {i + 1}: não foi possível ler a imagem ({ex.Message}).");
                }
            }
            res.PageImages.Add(img);
        }
        if (pdf is null && doc.PagesWithoutText.Count > 0)
            doc.Warnings.Add($"{doc.PagesWithoutText.Count} página(s) escaneada(s) sem texto não puderam ser lidas.");
        return res;
    }

    // ------------------------------------------------------------------ imagem

    private static async Task<ReadDocument> ReadImageAsync(string path, IProgress<string>? progress)
    {
        progress?.Report("Lendo a imagem (OCR)…");
        var doc = new ExtractedDocument { Path = path };
        var res = new ReadDocument { Doc = doc };
        var engine = CreateEngine(out _);
        res.OcrLanguage = engine.RecognizerLanguage.DisplayName;
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask();
        using var stream = await file.OpenReadAsync().AsTask();
        var dec = await WinImaging.BitmapDecoder.CreateAsync(stream).AsTask();
        // Foto pequena: amplia para ~2400 px de largura (o OCR acerta mais); grande demais: reduz ao limite do OCR.
        double scale = 1;
        uint w0 = dec.OrientedPixelWidth, h0 = dec.OrientedPixelHeight;
        if (w0 < 1800) scale = 2400.0 / w0;
        double maxDim = Windows.Media.Ocr.OcrEngine.MaxImageDimension;
        scale = Math.Min(scale, maxDim / Math.Max(w0, h0));
        var transform = new WinImaging.BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, Math.Round(dec.PixelWidth * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(dec.PixelHeight * scale)),
            InterpolationMode = WinImaging.BitmapInterpolationMode.Fant,
        };
        using var sb = await dec.GetSoftwareBitmapAsync(WinImaging.BitmapPixelFormat.Bgra8, WinImaging.BitmapAlphaMode.Premultiplied, transform,
            WinImaging.ExifOrientationMode.RespectExifOrientation, WinImaging.ColorManagementMode.DoNotColorManage).AsTask();
        var words = await OcrAsync(engine, sb, 0);
        doc.Pages.Add(new DocPage(0, sb.PixelWidth, sb.PixelHeight, true));
        doc.Words.AddRange(words);
        res.PageImages.Add(await ToWpfAsync(sb));
        if (words.Count == 0) doc.Warnings.Add("Nenhum texto reconhecido na imagem (confira se está nítida e na posição certa).");
        return res;
    }

    // ------------------------------------------------------------------ OCR do Windows

    public sealed class OcrUnavailableException : InvalidOperationException
    {
        public OcrUnavailableException(string msg) : base(msg) { }
    }

    /// <summary>Motor de OCR em português (pt-BR ou pt); sem o idioma instalado, o do usuário, com aviso.</summary>
    private static Windows.Media.Ocr.OcrEngine CreateEngine(out string language)
    {
        Windows.Media.Ocr.OcrEngine? e = null;
        foreach (var tag in new[] { "pt-BR", "pt" })
        {
            try
            {
                var lang = new Windows.Globalization.Language(tag);
                if (Windows.Media.Ocr.OcrEngine.IsLanguageSupported(lang)) { e = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(lang); if (e is not null) break; }
            }
            catch { }
        }
        e ??= Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
        if (e is null)
            throw new OcrUnavailableException("O reconhecimento de texto (OCR) do Windows não está disponível. Instale o idioma Português (Brasil) em " +
                                              "Configurações do Windows › Hora e idioma › Idioma, com o recurso de reconhecimento óptico de caracteres.");
        language = e.RecognizerLanguage.LanguageTag;
        return e;
    }

    private static async Task<List<DocWord>> OcrAsync(Windows.Media.Ocr.OcrEngine engine, WinImaging.SoftwareBitmap sb, int page)
    {
        var r = await engine.RecognizeAsync(sb).AsTask();
        var res = new List<DocWord>();
        foreach (var line in r.Lines)
            foreach (var w in line.Words)
            {
                var b = w.BoundingRect;
                res.Add(new DocWord(page, w.Text, b.X, b.Y, b.X + b.Width, b.Y + b.Height));
            }
        return res;
    }

    /// <summary>Imagem do Windows → imagem do WPF (congelada, pode ir para a janela).</summary>
    private static async Task<WpfImaging.BitmapSource> ToWpfAsync(WinImaging.SoftwareBitmap sb)
    {
        using var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var enc = await WinImaging.BitmapEncoder.CreateAsync(WinImaging.BitmapEncoder.PngEncoderId, ras).AsTask();
        enc.SetSoftwareBitmap(sb);
        await enc.FlushAsync().AsTask();
        ras.Seek(0);
        var ms = new MemoryStream();
        using (var s = ras.AsStreamForRead()) await s.CopyToAsync(ms);
        ms.Position = 0;
        var bi = new WpfImaging.BitmapImage();
        bi.BeginInit();
        bi.CacheOption = WpfImaging.BitmapCacheOption.OnLoad;
        bi.StreamSource = ms;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }
}
