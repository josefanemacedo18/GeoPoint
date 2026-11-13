using GeoPoint.Core.Calc;
using GeoPoint.Core.Documents;
using GeoPoint.Core.Geodesy;
using UglyToad.PdfPig.Writer;

namespace GeoPoint.Core.Tests;

/// <summary>Leitura de certidão de matrícula / inteiro teor (texto do PDF ou do OCR).</summary>
public class CertidaoTests
{
    public const string Moderna =
        "CARTÓRIO DO 1º OFÍCIO DE REGISTRO DE IMÓVEIS DA COMARCA DE PORTO FRANCO - MA\n" +
        "CERTIDÃO DE INTEIRO TEOR\n" +
        "MATRÍCULA Nº 7.607 - Livro 2 - Registro Geral\n" +
        "IMÓVEL: Um lote de terreno urbano sob nº 05 da Quadra 12, situado na Rua das Flores, nº 120, Bairro Jardim São Manoel, " +
        "nesta cidade de Porto Franco/MA, CEP 65970-000, com área de 360,00 m² e perímetro de 84,00 m, com os seguintes limites e confrontações: " +
        "Inicia-se no vértice P-01, de coordenadas N 9.299.292,99 m e E 235.142,50 m; deste, segue confrontando com a Rua das Flores, " +
        "com azimute de 90°00'00\" e distância de 30,00 m, até o vértice P-02, de coordenadas N 9.299.292,99 m e E 235.172,50 m; " +
        "deste, segue confrontando com o Lote 06 de propriedade de JOÃO PEREIRA DA SILVA, com azimute de 180°00'00\" e distância de 12,00 m, " +
        "até o vértice P-03, de coordenadas N 9.299.280,99 m e E 235.172,50 m; deste, segue confrontando com o Lote 10, com azimute de 270°00'00\" " +
        "e distância de 30,00 m, até o vértice P-04, de coordenadas N 9.299.280,99 m e E 235.142,50 m; deste, segue confrontando com terreno de " +
        "Maria José Lima, com azimute de 0°00'00\" e distância de 12,00 m, até o vértice P-01, ponto inicial. Coordenadas no Sistema Geodésico " +
        "Brasileiro, SIRGAS 2000, projeção UTM, fuso 23 S.\n" +
        "PROPRIETÁRIO: JOSÉ CARLOS SOUZA, brasileiro, casado, CPF 111.222.333-44.\n" +
        "R-2-7.607 - COMPRA E VENDA. ADQUIRENTE: MARIA DAS GRAÇAS OLIVEIRA, brasileira, solteira, inscrita no CPF sob nº 987.654.321-00.";

    [Fact]
    public void Moderna_ReadsPerimeterConfrontantesAndPropertyData()
    {
        var d = CertidaoParser.Parse(Moderna);
        Assert.Equal("7607", d.Matricula);
        Assert.Equal("Maria das Graças Oliveira", d.Proprietario);      // o último adquirente
        Assert.Equal("987.654.321-00", d.Documento);
        Assert.Equal("05", d.Lote);
        Assert.Equal("12", d.Quadra);
        Assert.Equal("Rua das Flores, nº 120", d.Endereco);
        Assert.Equal("Jardim São Manoel", d.Bairro);
        Assert.Equal("Porto Franco", d.Municipio);
        Assert.Equal("MA", d.UF);
        Assert.Equal("Porto Franco", d.Comarca);
        Assert.Contains("REGISTRO DE IMÓVEIS", d.Cartorio);
        Assert.Equal("65970-000", d.Cep);
        Assert.Equal(360, d.Area);
        Assert.Equal(84, d.Perimetro);
        Assert.Equal(23, d.UtmZone);
        Assert.True(d.South);
        // Perímetro por coordenadas, com os azimutes e distâncias do texto para conferir.
        Assert.True(d.HasCoordinates);
        Assert.Equal(new[] { "P-01", "P-02", "P-03", "P-04" }, d.Coordinates.Vertices.Select(v => v.Name));
        Assert.Equal(DatumId.Sirgas2000, d.Coordinates.Datum);
        Assert.Equal(4, d.Courses.Count);
        // Confrontante de cada trecho, pelo proprietário quando o texto diz "de propriedade de".
        Assert.Equal(new[] { "Rua das Flores", "João Pereira da Silva", "Lote 10", "Maria José Lima" }, d.Confrontantes);
        Assert.True(CertidaoParser.IsStreet(d.Confrontantes[0]));
        Assert.False(CertidaoParser.IsStreet(d.Confrontantes[1]));
        // Conferência: desenho pelas coordenadas confere com o texto e com a área da certidão.
        var r = PerimeterText.Build(d.Coordinates.Vertices, d.Coordinates.Kind, DatumId.Sirgas2000, d.UtmZone!.Value, d.South, true, d.Coordinates.StatedCourses);
        Assert.Equal(4, r.CheckedOk);
        Assert.InRange(Math.Abs(r.Area - d.Area!.Value) / d.Area.Value, 0, 0.005);
    }

    [Fact]
    public void Antiga_OnlySideMeasures_DrawsRectangle()
    {
        const string antiga =
            "Um terreno urbano, lote nº 08 da quadra B, situado na Avenida Brasil, s/n, nesta cidade de Imperatriz - MA, " +
            "medindo 12,00 m (doze metros) de frente para a Avenida Brasil; 12,00 m (doze metros) de fundos confrontando com o lote 15; " +
            "pelo lado direito, medindo 30,00 m, confrontando com o lote 09 de propriedade de Ana Lima; " +
            "pelo lado esquerdo, medindo 30,00 m, confrontando com o lote 07; perfazendo uma área total de 360,00 m². " +
            "Proprietário: Pedro Alves Costa, CPF 123.456.789-09.";
        var d = CertidaoParser.Parse(antiga);
        Assert.True(d.FromSides);
        Assert.Equal(4, d.Courses.Count);
        Assert.Equal(new double[] { 90, 0, 270, 180 }, d.Courses.Select(c => Math.Round(c.AzimuthDeg, 6)));
        Assert.Equal(new double[] { 12, 30, 12, 30 }, d.Courses.Select(c => Math.Round(c.Distance, 6)));
        Assert.Equal(new[] { "Avenida Brasil", "Ana Lima", "lote 15", "lote 07" }, d.Confrontantes);
        Assert.Equal("Pedro Alves Costa", d.Proprietario);
        Assert.Equal("123.456.789-09", d.Documento);
        Assert.Equal("Imperatriz", d.Municipio);
        Assert.Equal("Avenida Brasil, s/n", d.Endereco);
        Assert.Equal("08", d.Lote);
        Assert.Equal("B", d.Quadra);
        Assert.Contains(d.Warnings, w => w.Contains("perpendiculares"));
        var t = Traverse.Compute(d.Courses, adjust: false);
        Assert.Equal(0, t.MisclosureLength, 6);
        Assert.Equal(360, t.Area, 6);

        // Fundos diferentes: as laterais não são perpendiculares – aviso para ajustar os ângulos.
        var torto = CertidaoParser.Parse(antiga.Replace("12,00 m (doze metros) de fundos", "13,50 m (treze metros e cinquenta) de fundos"));
        Assert.Contains(torto.Warnings, w => w.Contains("não são retos"));
    }

    // Tabela de coordenadas da prancha do GeoPoint (a da imagem), como sai do texto do PDF (colunas separadas por tabulação).
    public const string Tabela =
        "TABELA DE COORDENADAS DOS VÉRTICES – LOTE\n" +
        "SISTEMA GEODÉSICO DE REFERÊNCIA: SIRGAS 2000\n" +
        "PROJEÇÃO UTM – FUSO 23S – EPSG:31983\n" +
        "Vértice\tAzimute\tDist. (m)\tE (m)\tN (m)\n" +
        "P-01\t268°15'23\"\t20,00\t236.405,249\t9.298.531,391\n" +
        "P-02\t15°22'23\"\t22,00\t236.385,252\t9.298.530,690\n" +
        "P-03\t145°30'17\"\t25,00\t236.390,990\t9.298.551,939";

    [Fact]
    public void Tabela_WithAzimuthDistanceAndCoordinates()
    {
        var d = CertidaoParser.Parse(Tabela);
        Assert.Equal(23, d.UtmZone);
        Assert.True(d.South);
        var c = d.Coordinates;
        Assert.Equal("tabela", c.Format);
        Assert.Equal(PerimeterCoordKind.Utm, c.Kind);
        Assert.Equal(DatumId.Sirgas2000, c.Datum);
        Assert.Equal(new[] { "P-01", "P-02", "P-03" }, c.Vertices.Select(v => v.Name));
        Assert.Equal(236_405.249, c.Vertices[0].A, 6);
        Assert.Equal(9_298_531.391, c.Vertices[0].B, 6);
        // Azimute e distância de cada linha (até o próximo vértice) vêm para a conferência.
        Assert.Equal(3, c.StatedCourses.Count);
        Assert.Equal(268 + 15 / 60.0 + 23 / 3600.0, c.StatedCourses[0].AzimuthDeg, 6);
        var r = PerimeterText.Build(c.Vertices, c.Kind, DatumId.Sirgas2000, 23, true, true, c.StatedCourses);
        Assert.Equal(3, r.Checked);
        Assert.Equal(3, r.CheckedOk);
        Assert.All(r.Segments, s => Assert.Equal("verdadeiro", s.Basis));   // a tabela do GeoPoint traz o azimute verdadeiro
    }

    [Fact]
    public void Tabela_WithTypicalOcrErrors_IsStillRead()
    {
        // Espaços no ângulo, "l" no lugar de 1, "O" no lugar de 0, espaço depois do ponto de milhar e colunas por espaços.
        const string ocr =
            "SISTEMA GEODÉSICO DE REFERÊNCIA: SIRGAS 2000 PROJEÇÃO UTM – FUSO 23S\n" +
            "Vértice Azimute Dist. (m) E (m) N (m)\n" +
            "P-01 268° 15' 23\" 2O,00 236.405,249 9.298.53l,391\n" +
            "P-02 15° 22' 23\" 22,00 236.385,252 9.298. 530,690\n" +
            "P-03 145o30'17\" 25,00 236.390,990 9.298.551,939";
        var d = CertidaoParser.Parse(ocr);
        var c = d.Coordinates;
        Assert.Equal(3, c.Vertices.Count);
        Assert.Equal(9_298_531.391, c.Vertices[0].B, 6);
        Assert.Equal(9_298_530.690, c.Vertices[1].B, 6);
        Assert.Equal(3, c.StatedCourses.Count);
        Assert.Equal(20, c.StatedCourses[0].Distance, 6);
        Assert.Equal(145 + 30 / 60.0 + 17 / 3600.0, c.StatedCourses[2].AzimuthDeg, 6);
    }

    [Fact]
    public void OcrCleanup_FixesNumbersButNotWords()
    {
        Assert.Equal("9.298.531,391", OcrCleanup.Normalize("9.298.53l,391"));
        Assert.Equal("20,00", OcrCleanup.Normalize("2O,00"));
        Assert.Equal("268°15'23\"", OcrCleanup.Normalize("268° 15’ 23”"));
        Assert.Equal("268°15'23\"", OcrCleanup.Normalize("268o15'23\""));
        Assert.Equal("9.298.531,391", OcrCleanup.Normalize("9.298. 531,391"));
        Assert.Equal("confrontando", OcrCleanup.Normalize("confron-\ntando"));
        // Ordinal, lista com vírgula e palavras com "o" ficam como estão.
        Assert.Equal("Cartório do 1o Ofício", OcrCleanup.Normalize("Cartório do 1o Ofício"));
        Assert.Equal("P-01, 235142,50, 9299292,99", OcrCleanup.Normalize("P-01, 235142,50, 9299292,99"));
        Assert.Equal(360, OcrCleanup.Number("360,00 m²"));
        Assert.Equal(268 + 15 / 60.0 + 23 / 3600.0, OcrCleanup.Number("268°15'23\";")!.Value, 9);
    }

    [Fact]
    public void Pdf_TextLayer_IsReadWithPositions_AndParsed()
    {
        // PDF digital com a certidão (fonte padrão Helvetica, acentos do WinAnsi).
        // Fonte TrueType com °, ², acentos (Liberation Sans no Linux, Arial no Windows).
        var ttf = new[] { "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", @"C:\Windows\Fonts\arial.ttf" }
            .FirstOrDefault(File.Exists);
        if (ttf is null) return;
        var b = new PdfDocumentBuilder();
        var page = b.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        var font = b.AddTrueTypeFont(File.ReadAllBytes(ttf));
        double y = 800;
        foreach (var line in Moderna.Split('\n').SelectMany(l => Wrap(l, 95)))
        {
            page.AddText(line, 9, new UglyToad.PdfPig.Core.PdfPoint(40, y), font);
            y -= 12;
        }
        var dir = Path.Combine(Path.GetTempPath(), "gp-cert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "certidao.pdf");
            File.WriteAllBytes(path, b.Build());
            var doc = PdfTextReader.Read(path);
            Assert.Single(doc.Pages);
            Assert.Empty(doc.PagesWithoutText);
            Assert.False(doc.UsedOcr);
            Assert.Contains(doc.Words, w => w.Text == "9.299.292,99");
            var w0 = doc.Words.First(w => w.Text == "CERTIDÃO");
            Assert.True(w0.Y0 < 60 && w0.Y1 > w0.Y0);   // origem em cima
            var d = CertidaoParser.Parse(doc.Text);
            Assert.Equal("7607", d.Matricula);
            Assert.Equal(4, d.Coordinates.Vertices.Count);
            Assert.Equal(new[] { "Rua das Flores", "João Pereira da Silva", "Lote 10", "Maria José Lima" }, d.Confrontantes);
            // Destaque na revisão: as palavras com os números usados no desenho.
            var hl = DocumentHighlight.Find(doc.Words, d.Courses.Select(c => c.AzimuthDeg),
                d.Courses.Select(c => c.Distance).Concat(d.Coordinates.Vertices.SelectMany(v => new[] { v.A, v.B })));
            Assert.Contains(hl, w => w.Text.StartsWith("235.172,50"));
            Assert.Contains(hl, w => w.Text.StartsWith("90°00'00"));
            Assert.DoesNotContain(hl, w => w.Text.StartsWith("CERTIDÃO"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DocumentText_RebuildsLinesAndTableColumns()
    {
        var words = new List<DocWord>
        {
            new(0, "Vértice", 10, 10, 50, 20), new(0, "E", 120, 10, 128, 20), new(0, "(m)", 131, 10, 145, 20), new(0, "N", 220, 10, 228, 20),
            new(0, "P-01", 10, 30, 40, 40), new(0, "236.405,249", 120, 31, 190, 41), new(0, "9.298.531,391", 220, 29, 300, 39),
        };
        var text = DocumentText.BuildText(words);
        Assert.Equal("Vértice\tE (m)\tN\nP-01\t236.405,249\t9.298.531,391", text);
    }

    private static IEnumerable<string> Wrap(string s, int max)
    {
        var words = s.Split(' ');
        var line = "";
        foreach (var w in words)
        {
            if (line.Length + w.Length + 1 > max && line.Length > 0) { yield return line; line = ""; }
            line = line.Length == 0 ? w : line + " " + w;
        }
        if (line.Length > 0) yield return line;
    }

    /// <summary>Dados para a simulação da janela com a certidão (só com GEOPOINT_OUT definido).</summary>
    [Fact]
    public void WriteReviewSample()
    {
        var outPath = Environment.GetEnvironmentVariable("GEOPOINT_OUT");
        var ttf = new[] { "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf", @"C:\Windows\Fonts\arial.ttf" }.FirstOrDefault(File.Exists);
        if (string.IsNullOrEmpty(outPath) || ttf is null) return;
        Directory.CreateDirectory(outPath);
        var b = new PdfDocumentBuilder();
        var page = b.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        var font = b.AddTrueTypeFont(File.ReadAllBytes(ttf));
        double y = 790;
        foreach (var line in Moderna.Split('\n').SelectMany(l => Wrap(l, 100)))
        {
            page.AddText(line, 8.5, new UglyToad.PdfPig.Core.PdfPoint(45, y), font);
            y -= 13;
        }
        string pdf = Path.Combine(outPath, "certidao-exemplo.pdf");
        File.WriteAllBytes(pdf, b.Build());
        var doc = PdfTextReader.Read(pdf);
        var d = CertidaoParser.Parse(doc.Text);
        var c = d.Coordinates;
        var r = PerimeterText.Build(c.Vertices, c.Kind, DatumId.Sirgas2000, d.UtmZone ?? 23, d.South, true, c.StatedCourses);
        var hl = DocumentHighlight.Find(doc.Words, c.StatedCourses.Select(x => x.AzimuthDeg),
            c.StatedCourses.Select(x => x.Distance).Concat(c.Vertices.SelectMany(v => new[] { v.A, v.B })).Append(d.Area ?? 0).Append(d.Perimetro ?? 0));
        var fmt = new GeoPoint.Core.Memorial.ValueFormatter(new GeoPoint.Core.Settings.FormatSettings());
        var data = new
        {
            page = new { w = doc.Pages[0].Width, h = doc.Pages[0].Height },
            words = doc.Words.Select(w => new { t = w.Text, x0 = w.X0, y0 = w.Y0, x1 = w.X1, y1 = w.Y1, hl = hl.Contains(w) }),
            fields = d.Fields().Select(f => new { label = f.Label, value = f.Value }),
            confs = r.Segments.Select((s, i) => new { trecho = $"{r.Names[s.From]} → {r.Names[s.To]}", conf = i < d.Confrontantes.Count ? d.Confrontantes[i] : "", testada = i < d.Confrontantes.Count && CertidaoParser.IsStreet(d.Confrontantes[i]) }),
            area = d.Area, perim = d.Perimetro, dArea = r.Area, dPerim = r.Perimeter, ok = r.CheckedOk, total = r.Checked,
        };
        File.WriteAllText(Path.Combine(outPath, "certidao-janela.json"), System.Text.Json.JsonSerializer.Serialize(data,
            new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
}
