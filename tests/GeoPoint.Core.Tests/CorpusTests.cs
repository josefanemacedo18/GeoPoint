using System.Diagnostics;
using System.Text;
using System.Text.Json;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Documents;
using GeoPoint.Core.Model;
using GeoPoint.Core.Sheets;

namespace GeoPoint.Core.Tests;

/// <summary>
/// Conjunto de documentos de referência: os modelos que já apareceram (anonimizados) com o resultado esperado. Toda versão precisa
/// continuar lendo todos. E um teste opcional com uma pasta local de documentos reais (GEOPOINT_CORPUS), fora do repositório.
/// </summary>
public class CorpusTests
{
    public const string AntigaFrente =
        "IMÓVEL: UM lote urbano localizado na Rua G, n° 08, QD 05, com a área de 360,00m² (trezentos e sessenta metros quadrados), limitando-se: " +
        "Frente com a Rua G, medindo 12,00 metros; ao fundo limitando-se com o Lote 07, medindo 12,00 metros; pelo lado direito limitando-se com " +
        "Ana Souza, medindo 30 metros, e por fim limitando-se pelo lado esquerdo com Bruno Lima, medindo 30 metros, totalizando um perímetro de 84,00 metros. PROPRIETÁRIAS: FULANA";
    public const string AntigaCardeal =
        "IMOVEL: - Um terreno que limita-se ao Norte, com Avenida Central, medindo 76 metros; ao Sul, com a Rua Um, medindo 76 metros, a Leste, com " +
        "Lotes de Fulano e Beltrano, medindo 85 metros, e a Oeste, com a Rua Dois, medindo 85 metros, situado a Rua Dois, nesta cidade. PROPRIETÁRIO:- Alguém";

    public static IEnumerable<object[]> Reference() => new[]
    {
        new object[] { "certidão moderna – UTM com azimutes e distâncias", CertidaoTests.Moderna, 4, 4, 4, 360.0, true },
        // Inconsistente: os lados de 7,35 m diferem 3 e 8 cm das coordenadas – a leitura está certa, mas não "confere".
        new object[] { "certidão de inteiro teor comum – UTM só com distâncias", CertidaoTests.ModeloComum, 4, 0, 4, 761.46, false },
        new object[] { "memorial SGL – geográficas, texto limpo", V18Tests.SglLimpo, 6, 6, 6, 600.0, true },
        new object[] { "memorial SGL – texto do OCR de uma foto", V18Tests.SglOcr, 6, 6, 6, 600.0, true },
        new object[] { "certidão antiga – frente, fundos e laterais", AntigaFrente, 0, 0, 4, 360.0, false },
        new object[] { "certidão antiga – norte, sul, leste e oeste", AntigaCardeal, 0, 0, 4, 0.0, false },
    };

    [Theory]
    [MemberData(nameof(Reference))]
    public void ReferenceDocuments_AreRead(string name, string text, int vertices, int pairs, int confs, double area, bool complete)
    {
        var d = CertidaoParser.Parse(text);
        Assert.True(d.HasCoordinates || d.HasCourses, name + ": perímetro não encontrado");
        Assert.Equal(vertices, d.HasCoordinates ? d.Coordinates.Vertices.Count : 0);
        Assert.Equal(pairs, d.Trechos.Count(t => t.Azimuth is not null && t.Distance is not null));
        Assert.Equal(confs, d.Confrontantes.Count(c => c.Length > 0));
        if (area > 0) Assert.Equal(area, d.Area ?? 0, 2);
        var score = ReadingScore.Of(d);
        Assert.Equal(complete, score.Complete);
        var steps = ReadingReport.Steps(d, text.Length, "", null);
        Assert.DoesNotContain(steps, s => s.Status == CheckStatus.Falha);
    }

    [Fact]
    public void SheetReview_FlagsLowResolutionAndLegend()
    {
        var lot = TestProjects.Rect(0, 0, 12, 30, "01");
        var p = TestProjects.Project(lot);
        var ctx = TestProjects.Context(p);
        var spec = new SheetSpec { Kind = SheetKind.Atual, Title = "SITUAÇÃO", LotIds = { lot.Id }, Hatch = true };
        var tpl = SheetTemplate.Default();
        var comp = SheetPreview.Compose(ctx, new[] { lot }, spec, tpl, croqui: true, croquiPx: 200);
        Assert.Contains(comp.Warnings, w => w.Contains("dpi"));
        Assert.DoesNotContain(comp.Warnings, w => w.Contains("grade"));
        var ok = SheetPreview.Compose(ctx, new[] { lot }, spec, tpl, croqui: true, croquiPx: 1600);
        Assert.DoesNotContain(ok.Warnings, w => w.Contains("dpi"));
        // Recorte da imagem para a resolução: zoom 3 num print Full HD.
        var img = new SatelliteImage { Width = 1920, Height = 1080 };
        p.SatelliteImages.Add(img);
        int? px = SheetReview.CropWidth(p, new SheetImageChoice { ImageId = img.Id, Zoom = 3 }, tpl);
        Assert.InRange(px!.Value, 300, 700);
    }

    [Fact]
    public void NoImageOnThisSheet_OverridesThePhaseImage()
    {
        var p = new ParcelProject();
        var img = new SatelliteImage { Nome = "geral" };
        p.SatelliteImages.Add(img);
        p.ImageDefaults[""] = new SheetImageChoice { ImageId = img.Id };
        p.SheetImages["k"] = new SheetImageChoice { ImageId = "" };
        Assert.Null(LocationImages.Resolve(p, "k", ""));
        Assert.StartsWith("nenhuma", LocationImages.Origin(p, "k", ""));
        Assert.NotNull(LocationImages.Resolve(p, "outra", ""));
    }

    /// <summary>
    /// Pasta local de documentos reais (fora do repositório): GEOPOINT_CORPUS com arquivos .txt, .pdf (com texto) ou imagens, cada um
    /// com "&lt;nome&gt;.expected.json" ({"vertices":6,"pairs":6,"confs":6,"area":600,"complete":true}). Imagens passam pela mesma
    /// preparação do plugin e pelo Tesseract de linha de comando (precisa do "tesseract" e do "convert" instalados).
    /// Grava corpus-report.txt (com os dados pessoais mascarados) e falha se algum documento não for lido como esperado.
    /// </summary>
    [Fact]
    public void LocalCorpus()
    {
        var dir = Environment.GetEnvironmentVariable("GEOPOINT_CORPUS");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        var report = new StringBuilder();
        var failures = new List<string>();
        foreach (var exp in Directory.GetFiles(dir, "*.expected.json").OrderBy(x => x))
        {
            string stem = exp[..^".expected.json".Length];
            var file = Directory.GetFiles(dir).FirstOrDefault(f => f.StartsWith(stem + ".") && !f.EndsWith(".expected.json") && !f.EndsWith(".ocr.txt"));
            if (file is null) continue;
            var e = JsonDocument.Parse(File.ReadAllText(exp)).RootElement;
            string text = Path.GetExtension(file).ToLowerInvariant() switch
            {
                ".txt" => File.ReadAllText(file),
                ".pdf" => PdfTextReader.Read(file).Text,
                _ => Ocr(file),
            };
            var d = CertidaoParser.Parse(text);
            var score = ReadingScore.Of(d);
            int v = d.HasCoordinates ? d.Coordinates.Vertices.Count : 0;
            int pairs = d.Trechos.Count(t => t.Azimuth is not null && t.Distance is not null);
            int confs = d.Confrontantes.Count(c => c.Length > 0);
            var errs = new List<string>();
            if (e.TryGetProperty("vertices", out var ev) && ev.GetInt32() != v) errs.Add($"vértices {v} (esperado {ev.GetInt32()})");
            if (e.TryGetProperty("courses", out var ec) && ec.GetInt32() != d.Courses.Count) errs.Add($"trechos {d.Courses.Count} (esperado {ec.GetInt32()})");
            if (e.TryGetProperty("pairs", out var ep) && ep.GetInt32() != pairs) errs.Add($"azimute+distância {pairs} (esperado {ep.GetInt32()})");
            if (e.TryGetProperty("confs", out var ef) && ef.GetInt32() != confs) errs.Add($"confrontantes {confs} (esperado {ef.GetInt32()})");
            if (e.TryGetProperty("area", out var ea) && ea.GetDouble() > 0 && Math.Abs((d.Area ?? 0) - ea.GetDouble()) > ea.GetDouble() * 0.001) errs.Add($"área {d.Area} (esperado {ea.GetDouble()})");
            if (e.TryGetProperty("complete", out var eo) && eo.GetBoolean() != score.Complete) errs.Add($"completa {score.Complete} (esperado {eo.GetBoolean()})");
            report.AppendLine($"{(errs.Count == 0 ? "✓" : "✗")} {Path.GetFileName(file)}: {score.Summary}" + (errs.Count > 0 ? " – " + string.Join("; ", errs) : ""));
            foreach (var t in d.Trechos)
                report.AppendLine($"    {t.From} → {t.To}: az {t.Azimuth:0.0000} dist {t.Distance:0.00} {t.Lado} {Privacy.MaskNames(t.Confrontante)}{(t.Inherited ? " (herdado)" : "")}");
            if (errs.Count > 0) failures.Add(Path.GetFileName(file) + ": " + string.Join("; ", errs));
        }
        File.WriteAllText(Path.Combine(dir, "corpus-report.txt"), report.ToString());
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>Imagem → (convert) cinza → preparação do GeoPoint → Tesseract (por).</summary>
    private static string Ocr(string image)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "gp-corpus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            string raw = Path.Combine(tmp, "raw.pgm"), prep = Path.Combine(tmp, "prep.pgm");
            Run("convert", $"\"{image}\" -auto-orient -colorspace Gray -depth 8 \"{raw}\"");
            var g = ImagePrep.Prepare(GrayImage.FromPgm(File.ReadAllBytes(raw)), out _);
            File.WriteAllBytes(prep, g.ToPgm());
            Run("tesseract", $"\"{prep}\" \"{Path.Combine(tmp, "out")}\" -l por --psm 3");
            string text = File.ReadAllText(Path.Combine(tmp, "out.txt"));
            File.WriteAllText(Path.ChangeExtension(image, ".ocr.txt"), text);
            return text;
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    private static void Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false })!;
        p.WaitForExit(120000);
    }
}
