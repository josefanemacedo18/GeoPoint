using System;
using System.Collections.Generic;
using System.IO;
using GeoPoint.Core.Documents;
using Tesseract;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>
/// OCR offline com o Tesseract 5 (português), instalado junto com o plugin: as DLLs nativas ficam na pasta "x64" e o idioma na
/// pasta "tessdata", ao lado do GeoPoint.Revit.dll. Nada é enviado para fora do computador.
/// </summary>
public sealed class TesseractReader : IDisposable
{
    private readonly TesseractEngine _engine;

    private TesseractReader(TesseractEngine e) { _engine = e; }

    public static string Folder => Path.GetDirectoryName(typeof(TesseractReader).Assembly.Location) ?? "";
    public static string TessData => Path.Combine(Folder, "tessdata");

    /// <summary>Cria o leitor; null (com o motivo) se o Tesseract não puder ser carregado – aí fica o OCR do Windows.</summary>
    public static TesseractReader? TryCreate(out string error)
    {
        error = "";
        try
        {
            if (!File.Exists(Path.Combine(TessData, "por.traineddata")))
            {
                error = "arquivo do idioma português do Tesseract não encontrado na pasta do plugin (tessdata\\por.traineddata)";
                return null;
            }
            // O Revit roda de outra pasta: as DLLs nativas são procuradas em <pasta do plugin>\x64.
            TesseractEnviornment.CustomSearchPath = Folder;
            var e = new TesseractEngine(TessData, "por", EngineMode.LstmOnly);
            e.SetVariable("user_defined_dpi", "300");
            e.SetVariable("preserve_interword_spaces", "1");
            return new TesseractReader(e);
        }
        catch (Exception ex)
        {
            Logger.Error("Tesseract", ex);
            error = ex is DllNotFoundException || ex.InnerException is DllNotFoundException
                ? "bibliotecas do Tesseract não carregaram (falta o Visual C++ 2015–2022 x64?)"
                : ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Lê uma página já em cinza: prepara a imagem (<see cref="ImagePrep"/>), roda o OCR e devolve as palavras na escala da página
    /// original (para os destaques na revisão).
    /// </summary>
    public List<DocWord> Read(GrayImage page, int pageIndex, bool flatten, PageSegMode mode, out float meanConfidence)
    {
        var prepared = ImagePrep.Prepare(page, out double k, flatten: flatten);
        var words = new List<DocWord>();
        using var pix = Pix.LoadFromMemory(prepared.ToPgm());
        using var p = _engine.Process(pix, mode);
        meanConfidence = p.GetMeanConfidence();
        using var it = p.GetIterator();
        it.Begin();
        do
        {
            if (!it.TryGetBoundingBox(PageIteratorLevel.Word, out var r)) continue;
            var t = it.GetText(PageIteratorLevel.Word);
            if (string.IsNullOrWhiteSpace(t)) continue;
            words.Add(new DocWord(pageIndex, t.Trim(), r.X1 / k, r.Y1 / k, r.X2 / k, r.Y2 / k));
        }
        while (it.Next(PageIteratorLevel.Word));
        return words;
    }

    public void Dispose() => _engine.Dispose();
}
