using GeoPoint.Core.Calc;
using GeoPoint.Core.Documents;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;

namespace GeoPoint.Core.Tests;

/// <summary>1.8: diagnóstico, conferência, destaques, correções aprendidas, proteção de dados, ligação aos lotes, preparação da foto.</summary>
public class V18bTests
{
    [Fact]
    public void Privacy_MasksDocumentsAndNames()
    {
        var s = Privacy.Mask("CPF nº 123.456.789-09, CNPJ 11.222.333/0001-81, e-mail fulano@exemplo.com, fone (99) 91234-5678, CNH nº 01234567890");
        Assert.DoesNotContain("123.456", s);
        Assert.DoesNotContain("11.222", s);
        Assert.DoesNotContain("fulano@", s);
        Assert.DoesNotContain("91234", s);
        Assert.DoesNotContain("01234567890", s);
        var n = Privacy.MaskNames("confrontando com LOTE 09 – João dos Santos Lima e com a Rua das Flores; Residencial Jardim Exemplo");
        Assert.Contains("LOTE 09 – [nome]", n);
        Assert.DoesNotContain("João", n);
        Assert.Contains("Rua [nome]", n);           // o tipo da via fica
        Assert.Contains("Residencial Jardim [nome]", n);   // nomes próprios de loteamento também são mascarados
        // Coordenadas e medidas ficam.
        Assert.Equal("E 237184,12/N 9294962,23 – 12,00 m", Privacy.Mask("E 237184,12/N 9294962,23 – 12,00 m"));
    }

    [Fact]
    public void Steps_And_Consistency_ForTheSglMemorial()
    {
        var d = CertidaoParser.Parse(V18Tests.SglOcr);
        var steps = ReadingReport.Steps(d, 2800, "Tesseract", null);
        Assert.All(steps, s => Assert.Equal(CheckStatus.Ok, s.Status));
        var c = d.Coordinates;
        var r = PerimeterText.Build(c.Vertices, c.Kind, GeoPoint.Core.Geodesy.DatumId.Sirgas2000, 23, true, true, c.StatedCourses);
        var divs = r.Segments.Select((s, i) => new Divisa(r.Names[s.From], r.Names[s.To], s.Distance, d.Confrontantes[i], d.Trechos[i].Lado)).ToList();
        var checks = ReadingReport.Consistency(d, divs, r.Area, r.Perimeter, null, r.CheckedOk, r.Checked, false, false);
        Assert.Contains(checks, x => x.Name == "Trechos × texto" && x.Status == CheckStatus.Ok);
        Assert.Contains(checks, x => x.Name == "Área" && x.Status == CheckStatus.Ok);
        Assert.Contains(checks, x => x.Name == "Quadro de confrontações" && x.Status == CheckStatus.Ok);
        // O perímetro do cabeçalho lido como 88 fica avisado.
        Assert.Contains(checks, x => x.Name == "Perímetro" && x.Detail.Contains("88,00"));
        // Uma divisa errada aparece no quadro e no fechamento.
        divs[0] = divs[0] with { Length = 13 };
        var bad = ReadingReport.Consistency(d, divs, r.Area, r.Perimeter + 1, 1.0, 5, 6, true, false);
        Assert.Contains(bad, x => x.Name == "Quadro de confrontações" && x.Status == CheckStatus.Aviso && x.Detail.Contains("Fundos"));
        Assert.Contains(bad, x => x.Name == "Fechamento" && x.Status == CheckStatus.Falha);
        Assert.Contains(bad, x => x.Name == "Sistema" && x.Detail.Contains("fuso"));
        // Relatório para copiar: sem nomes nem CPF.
        var txt = ReadingReport.Text("foto.jpg", steps, checks, new[] { "Tesseract: 6 vértices" }, d);
        Assert.Contains("M-01 → M-02", txt);
        Assert.DoesNotContain("Joao", txt);
        Assert.DoesNotContain("000.000.000-00", txt);
    }

    [Fact]
    public void Steps_ShowWhereTheReadingStopped()
    {
        var d = CertidaoParser.Parse("Texto qualquer sem perímetro nenhum, só uma conversa.");
        var steps = ReadingReport.Steps(d, 50, "Tesseract", new ImageQualityReport { ShortSide = 500 });
        Assert.Equal(CheckStatus.Falha, steps.First(s => s.Name == "Vértices").Status);
        Assert.Equal(CheckStatus.Aviso, steps.First(s => s.Name == "Texto").Status);
    }

    [Fact]
    public void Highlights_ByKind()
    {
        var words = new List<DocWord>
        {
            new(0, "Longitude:-47°19'51,247\",", 10, 10, 120, 20), new(0, "112°12'38\"", 130, 10, 180, 20), new(0, "12,00m", 190, 10, 220, 20),
            new(0, "LOTE", 10, 30, 40, 40), new(0, "09-", 45, 30, 60, 40), new(0, "JOAO", 65, 30, 95, 40), new(0, "DOS", 100, 30, 120, 40),
            new(0, "SANTOS", 125, 30, 165, 40), new(0, "LIMA,", 170, 30, 200, 40),
            new(0, "Responsável:", 10, 80, 80, 90), new(0, "Lima", 85, 80, 110, 90),
        };
        var hl = DocumentHighlight.Classify(words, Array.Empty<double>(), new[] { -(47 + 19 / 60.0 + 51.247 / 3600) }, new[] { 112 + 12 / 60.0 + 38 / 3600.0 },
            new[] { 12.0 }, Array.Empty<double>(), new[] { "Lote 09 – Joao dos Santos Lima" });
        Assert.Equal(HighlightKind.Coordenada, hl.Single(h => h.Word.Text.StartsWith("Longitude")).Kind);
        Assert.Equal(HighlightKind.Azimute, hl.Single(h => h.Word.Text.StartsWith("112")).Kind);
        Assert.Equal(HighlightKind.Distancia, hl.Single(h => h.Word.Text == "12,00m").Kind);
        Assert.Contains(hl, h => h.Word.Text == "SANTOS" && h.Kind == HighlightKind.Confrontante);
        Assert.DoesNotContain(hl, h => h.Word.Text == "Lima");   // "Lima" solto no rodapé não é pintado
        Assert.Equal("12,00m", DocumentHighlight.FindValue(words, 12, false)!.Text);
    }

    [Fact]
    public void LearnedFixes_AreAppliedNextTime()
    {
        var fixes = new List<LearnedFix>();
        Assert.True(LearnedFixes.Learn(fixes, "Lote 10 - 3 Alfa Empreendimentos Ltda", "Lote 10 – J. Alfa Empreendimentos Ltda"));
        Assert.False(LearnedFixes.Learn(fixes, "Rua 07", "  rua 07 "));
        var d = new CertidaoData();
        d.Confrontantes.AddRange(new[] { "Rua 07", "lote 10 - 3 alfa empreendimentos ltda" });
        Assert.Equal(1, LearnedFixes.Apply(d, fixes));
        Assert.Equal("Lote 10 – J. Alfa Empreendimentos Ltda", d.Confrontantes[1]);
        Assert.Equal(1, fixes[0].Uses);
    }

    [Fact]
    public void PendingConfrontante_LinksToTheExistingLot()
    {
        var p = new ParcelProject();
        var vizinho = TestProjects.Rect(-12, 0, 12, 30, "09");
        vizinho.Numero = "9"; vizinho.Quadra = "12";
        var outra = TestProjects.Rect(100, 0, 12, 30, "09");
        outra.Quadra = "13";
        p.Lots.AddRange(new[] { vizinho, outra });
        var lines = new List<Curve2>
        {
            new LineCurve(new Vec2(0, 0), new Vec2(12, 0)), new LineCurve(new Vec2(12, 0), new Vec2(12, 30)),
            new LineCurve(new Vec2(12, 30), new Vec2(0, 30)), new LineCurve(new Vec2(0, 30), new Vec2(0, 0)),
        };
        PendingConfrontantes.Store(p, lines, new[] { "a", "b", "c", "d" },
            new[] { ("Rua 07", true), ("", false), ("", false), ("Lote 09 – João dos Santos", false) }, "memorial.jpg",
            new[] { "Frente", "", "", "Lado esquerdo" });
        var lot = TestProjects.Rect(0, 0, 12, 30, "10");
        lot.Quadra = "12";
        p.Lots.Add(lot);
        Assert.Equal(2, PendingConfrontantes.Apply(p, new[] { lot }));
        var left = lot.EdgeInfos.Single(i => Math.Abs(i.MX) < 1e-9);
        Assert.Equal(ConfrontanteTipo.Lote, left.Tipo);          // ligado ao lote 09 da mesma quadra
        Assert.Equal(vizinho.Id, left.LotId);
        Assert.Equal(LadoLote.LateralEsquerda, left.Lado);
        var front = lot.EdgeInfos.Single(i => Math.Abs(i.MY) < 1e-9);
        Assert.Equal(ConfrontanteTipo.Texto, front.Tipo);
        Assert.Equal(LadoLote.Frente, front.Lado);
    }

    [Fact]
    public void ImagePrep_ScaleAndQuality()
    {
        Assert.Equal(2400.0 / 899, ImagePrep.ScaleFor(899, 1599), 6);
        Assert.Equal(1, ImagePrep.ScaleFor(2480, 3508));       // A4 a 300 dpi: já está bom
        Assert.Equal(0.6, ImagePrep.ScaleFor(4000, 6000), 6);   // foto de 24 MP: reduzida
        // Página sintética: papel claro com "letras" (listras) – boa; escura e borrada – avisos.
        var good = new GrayImage(900, 1200);
        for (int y = 0; y < 1200; y++)
            for (int x = 0; x < 900; x++) good.Px[y * 900 + x] = (byte)((y / 6) % 4 == 0 && (x / 3) % 3 != 0 ? 30 : 235);
        Assert.True(ImagePrep.Assess(good).Good, ImagePrep.Assess(good).Summary);
        var dark = new GrayImage(900, 1200, good.Px.Select(v => (byte)(v / 4)).ToArray());
        Assert.Contains(ImagePrep.Assess(dark).Problems, p => p.Contains("escura"));
        var blur = good;
        for (int k = 0; k < 25; k++) blur = ImagePrep.Blur3(blur);
        Assert.Contains(ImagePrep.Assess(blur).Problems, p => p.Contains("nítida"));
        var prepared = ImagePrep.Prepare(good, out double scale);
        Assert.Equal(2400.0 / 900, scale, 6);
        Assert.Equal((int)Math.Round(900 * scale), prepared.Width);
        // PGM ida e volta (formato usado nos testes com o Tesseract).
        var back = GrayImage.FromPgm(good.ToPgm());
        Assert.Equal(good.Px, back.Px);
    }
}
