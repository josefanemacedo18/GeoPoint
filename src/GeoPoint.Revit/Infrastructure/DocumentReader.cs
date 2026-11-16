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
    public ExtractedDocument Doc { get; set; } = new();
    /// <summary>Imagem de cada página (mesma ordem de <see cref="ExtractedDocument.Pages"/>); null = sem imagem.</summary>
    public List<WpfImaging.BitmapSource?> PageImages { get; } = new();
    /// <summary>OCR usado na leitura escolhida ("" = sem OCR, texto do próprio PDF).</summary>
    public string OcrLanguage { get; set; } = "";
    /// <summary>Qualidade da foto/digitalização (null = PDF digital).</summary>
    public ImageQualityReport? Quality { get; set; }
    /// <summary>Cada tentativa de OCR e o que ela leu (para o diagnóstico).</summary>
    public List<string> Attempts { get; } = new();
}

/// <summary>
/// Lê uma certidão ou memorial em PDF ou imagem. PDF digital → texto do próprio PDF (exato). PDF escaneado, foto ou imagem → OCR
/// no próprio computador: primeiro o Tesseract (com a imagem preparada: ampliada, contraste e nitidez), em mais de uma tentativa
/// se a leitura não fechar, e o OCR do Windows de reserva; fica a leitura com a melhor nota (vértices, trechos, conferência).
/// </summary>
public static class DocumentReader
{
    public const int MaxPages = 20;
    /// <summary>Lado maior máximo de uma foto (fotos de 12–50 MP são reduzidas).</summary>
    private const int MaxSide = 4000;

    public static bool IsPdf(string path) => Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <param name="confirmQuality">
    /// Chamado (em qualquer thread) quando a foto tem problema de qualidade, antes do OCR: true = ler assim mesmo; false = cancelar.
    /// </param>
    public static async Task<ReadDocument> ReadAsync(string path, IProgress<string>? progress = null, Func<ImageQualityReport, bool>? confirmQuality = null)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Arquivo não encontrado.", path);
        return IsPdf(path) ? await ReadPdfAsync(path, progress, confirmQuality) : await ReadImageAsync(path, progress, confirmQuality);
    }

    /// <summary>Página para o OCR: a imagem em cinza (para o Tesseract) e a do Windows (para o OCR de reserva).</summary>
    private sealed class OcrPage
    {
        public int Index { get; init; }
        public GrayImage Gray { get; init; } = null!;
        public WinImaging.SoftwareBitmap? Windows { get; init; }
        /// <summary>Escala da imagem do Windows em relação à página (palavras do OCR do Windows ÷ isto).</summary>
        public double WindowsScale { get; init; } = 1;
    }

    // ------------------------------------------------------------------ PDF

    private static async Task<ReadDocument> ReadPdfAsync(string path, IProgress<string>? progress, Func<ImageQualityReport, bool>? confirm)
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
        var scans = new List<OcrPage>();
        int n = Math.Min(doc.Pages.Count, MaxPages);
        for (int i = 0; i < n; i++)
        {
            WpfImaging.BitmapSource? img = null;
            bool scanned = doc.PagesWithoutText.Contains(i);
            if (pdf is not null && i < pdf.PageCount)
            {
                try
                {
                    progress?.Report($"Página {i + 1}…");
                    using var page = pdf.GetPage((uint)i);
                    // Página escaneada: 300 dpi para o OCR; página com texto: ~190 dpi só para ver.
                    double dpi = scanned ? 300 : 190;
                    uint w = (uint)Math.Max(200, Math.Round(page.Size.Width / 96.0 * dpi));
                    using var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                    await page.RenderToStreamAsync(ras, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = w }).AsTask();
                    ras.Seek(0);
                    var dec = await WinImaging.BitmapDecoder.CreateAsync(ras).AsTask();
                    var sb = await dec.GetSoftwareBitmapAsync(WinImaging.BitmapPixelFormat.Bgra8, WinImaging.BitmapAlphaMode.Premultiplied).AsTask();
                    img = await ToWpfAsync(sb);
                    if (scanned)
                    {
                        doc.Pages[i] = new DocPage(i, sb.PixelWidth, sb.PixelHeight, true);
                        scans.Add(new OcrPage { Index = i, Gray = ToGray(img), Windows = sb });
                    }
                    else sb.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Error($"PDF página {i + 1}", ex);
                    if (scanned) doc.Warnings.Add($"Página {i + 1}: não foi possível ler a imagem ({ex.Message}).");
                }
            }
            res.PageImages.Add(img);
        }
        if (scans.Count > 0) await RunOcrAsync(res, scans, progress, confirm);
        else if (pdf is null && doc.PagesWithoutText.Count > 0)
            doc.Warnings.Add($"{doc.PagesWithoutText.Count} página(s) escaneada(s) sem texto não puderam ser lidas.");
        foreach (var s in scans) s.Windows?.Dispose();
        return res;
    }

    // ------------------------------------------------------------------ imagem

    private static async Task<ReadDocument> ReadImageAsync(string path, IProgress<string>? progress, Func<ImageQualityReport, bool>? confirm)
    {
        progress?.Report("Abrindo a imagem…");
        var doc = new ExtractedDocument { Path = path };
        var res = new ReadDocument { Doc = doc };
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask();
        using var stream = await file.OpenReadAsync().AsTask();
        var dec = await WinImaging.BitmapDecoder.CreateAsync(stream).AsTask();
        // Página na resolução da foto (girada pela orientação EXIF do celular), até 4000 px no lado maior.
        uint w0 = dec.OrientedPixelWidth, h0 = dec.OrientedPixelHeight;
        double k0 = Math.Min(1, (double)MaxSide / Math.Max(w0, h0));
        using var sb = await Decode(dec, k0);
        var img = await ToWpfAsync(sb);
        doc.Pages.Add(new DocPage(0, sb.PixelWidth, sb.PixelHeight, true));
        res.PageImages.Add(img);
        // OCR do Windows (reserva): foto pequena ampliada para ~2400 px; grande, reduzida ao limite dele.
        double kw = sb.PixelWidth < 1800 ? 2400.0 / sb.PixelWidth : 1;
        kw = Math.Min(kw, Windows.Media.Ocr.OcrEngine.MaxImageDimension / (double)Math.Max(sb.PixelWidth, sb.PixelHeight));
        var winSb = Math.Abs(kw - 1) < 1e-6 ? null : await Decode(dec, k0 * kw);
        var pages = new List<OcrPage> { new() { Index = 0, Gray = ToGray(img), Windows = winSb ?? sb, WindowsScale = winSb is null ? 1 : kw } };
        await RunOcrAsync(res, pages, progress, confirm);
        winSb?.Dispose();
        if (doc.Words.Count == 0) doc.Warnings.Add("Nenhum texto reconhecido na imagem (confira se está nítida e na posição certa).");
        return res;
    }

    private static async Task<WinImaging.SoftwareBitmap> Decode(WinImaging.BitmapDecoder dec, double k)
    {
        var transform = new WinImaging.BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, Math.Round(dec.PixelWidth * k)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(dec.PixelHeight * k)),
            InterpolationMode = WinImaging.BitmapInterpolationMode.Fant,
        };
        return await dec.GetSoftwareBitmapAsync(WinImaging.BitmapPixelFormat.Bgra8, WinImaging.BitmapAlphaMode.Premultiplied, transform,
            WinImaging.ExifOrientationMode.RespectExifOrientation, WinImaging.ColorManagementMode.DoNotColorManage).AsTask();
    }

    // ------------------------------------------------------------------ OCR

    /// <summary>
    /// Tentativas de OCR (Tesseract normal, com correção de sombra, em bloco único; OCR do Windows), da melhor para a pior: para na
    /// primeira leitura completa (fecha e confere); senão, fica a de melhor nota.
    /// </summary>
    private static async Task RunOcrAsync(ReadDocument res, List<OcrPage> pages, IProgress<string>? progress, Func<ImageQualityReport, bool>? confirm)
    {
        var doc = res.Doc;
        // Qualidade antes do OCR (a página pior manda).
        var quality = pages.Select(p => ImagePrep.Assess(p.Gray)).OrderByDescending(q => q.Problems.Count).First();
        res.Quality = quality;
        if (!quality.Good)
        {
            doc.Warnings.Add("Qualidade da foto: " + quality.Summary);
            if (confirm is not null && !confirm(quality)) throw new OperationCanceledException("Leitura cancelada para tirar outra foto.");
        }
        var baseWords = doc.Words.ToList();   // palavras das páginas com texto (PDF misto)
        (ExtractedDocument Doc, ReadingScore Score, string Engine)? best = null;

        void Consider(List<DocWord> words, string engine)
        {
            var cand = new ExtractedDocument { Path = doc.Path };
            cand.Pages.AddRange(doc.Pages);
            cand.PagesWithoutText.AddRange(doc.PagesWithoutText);
            cand.Warnings.AddRange(doc.Warnings);
            cand.Words.AddRange(baseWords);
            cand.Words.AddRange(words);
            var score = ReadingScore.Of(CertidaoParser.Parse(cand.Text));
            res.Attempts.Add($"{engine}: {score.Summary} (nota {score.Score:0})");
            if (best is null || score.Score > best.Value.Score.Score) best = (cand, score, engine);
        }

        using (var tess = TesseractReader.TryCreate(out string tessError))
        {
            if (tess is null) res.Attempts.Add("Tesseract indisponível: " + tessError);
            else
            {
                var tries = new (string Name, bool Flatten, Tesseract.PageSegMode Mode)[]
                {
                    ("Tesseract", false, Tesseract.PageSegMode.Auto),
                    ("Tesseract com correção de sombra", true, Tesseract.PageSegMode.Auto),
                    ("Tesseract em bloco único", false, Tesseract.PageSegMode.SingleBlock),
                };
                foreach (var (name, flatten, mode) in tries)
                {
                    progress?.Report($"Lendo a imagem ({name})…");
                    var words = new List<DocWord>();
                    try
                    {
                        foreach (var p in pages) words.AddRange(tess.Read(p.Gray, p.Index, flatten, mode, out _));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(name, ex);
                        res.Attempts.Add($"{name}: falhou ({ex.Message})");
                        break;
                    }
                    Consider(words, name);
                    if (best?.Score.Complete == true) break;
                }
            }
        }
        if (best?.Score.Complete != true)
        {
            try
            {
                progress?.Report("Lendo a imagem (OCR do Windows)…");
                var engine = CreateEngine(out _);
                var words = new List<DocWord>();
                foreach (var p in pages.Where(p => p.Windows is not null))
                    foreach (var w in await OcrAsync(engine, p.Windows!, p.Index))
                        words.Add(new DocWord(w.Page, w.Text, w.X0 / p.WindowsScale, w.Y0 / p.WindowsScale, w.X1 / p.WindowsScale, w.Y1 / p.WindowsScale));
                Consider(words, $"OCR do Windows ({engine.RecognizerLanguage.DisplayName})");
            }
            catch (Exception ex)
            {
                Logger.Error("OCR do Windows", ex);
                res.Attempts.Add("OCR do Windows: " + ex.Message);
                if (best is null) throw;
            }
        }
        if (best is { } b)
        {
            res.Doc = b.Doc;
            res.OcrLanguage = b.Engine;
        }
    }

    public sealed class OcrUnavailableException : InvalidOperationException
    {
        public OcrUnavailableException(string msg) : base(msg) { }
    }

    /// <summary>Motor de OCR do Windows em português (pt-BR ou pt); sem o idioma instalado, o do usuário.</summary>
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

    /// <summary>Imagem do WPF → cinza (para a preparação e o Tesseract).</summary>
    public static GrayImage ToGray(WpfImaging.BitmapSource img)
    {
        var src = img.Format == System.Windows.Media.PixelFormats.Bgra32 ? img
            : new WpfImaging.FormatConvertedBitmap(img, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        src.CopyPixels(px, stride, 0);
        return GrayImage.FromBgra(px, w, h, stride);
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
