using GeoPoint.Core.Calc;
using GeoPoint.Core.Documents;
using GeoPoint.Core.Geodesy;

namespace GeoPoint.Core.Tests;

/// <summary>
/// 1.10: o desenho só sai sem pergunta quando o documento se confirma sozinho, dentro da precisão com que foi escrito. Dados
/// fictícios (o mesmo formato dos documentos reais).
/// </summary>
public class V110Tests
{
    // Memorial no formato do de retificação (fictício): geográficas com 2 casas nos segundos, trechos do SGL, área e perímetro.
    // Retângulo de 12 × 30 m girado 28°37'12", com a divisa de 30 m em três trechos.
    public const string Retificacao =
        "MEMORIAL DESCRITIVO\nÁrea para retificação: 360,00 m²\n" +
        "Inicia-se a descrição deste perímetro para retificação de área cravado na margem da Rua A no vértice P01, com Coordenadas " +
        "Geográficas Latitude 06°20'12.79\"S e Longitude 47°23'36.74\"W, deste segue confrontando com propriedade de Lote 1, no azimute e " +
        "distância de 208°37'12\" – 7,26 metros, até o vértice P02, de coordenadas Latitude 06°20'13.00\"S e Longitude 47°23'36.86\"W, " +
        "deste segue confrontando com propriedade de Lote 2 no azimute e distância de 208°37'12\"- 7,26 metros, até o vértice P03, de " +
        "coordenadas Latitude 06°20'13.21\"S e Longitude 47°23'36.97\"W, deste segue confrontando com propriedade de Lote 3 no azimute e " +
        "distância de 208°37'12\" – 15,48 metros, até o vértice P04, de coordenadas Latitude 06°20'13.65\"S e Longitude 47°23'37.21\"W, " +
        "deste segue confrontando com propriedade de Lote 4 no azimute e distância de 298°37'12\" – 12,00 metros, até o vértice P05, de " +
        "coordenadas Latitude 06°20'13.46\"S e Longitude 47°23'37.55\"W, deste segue confrontando com propriedade de Lote 5 no azimute e " +
        "distância de 28°37'12\" – 30,00 metros, até o vértice P06, de coordenadas Latitude 06°20'12.60\"S e Longitude 47°23'37.08\"W, " +
        "deste segue confrontando com a referida Rua A no azimute e distância de 118°37'12\" – 12,00 metros, encontrando o ponto inicial " +
        "de partida e fechando seu perímetro com 84,00 metros. Todas as coordenadas estão georreferenciadas ao Sistema Geodésico " +
        "Brasileiro, DATUM SIRGAS 2000. Azimutes e distâncias, área e perímetro calculados no sistema local de coordenadas (SGL).";

    [Fact]
    public void PrecisaoEscrita()
    {
        Assert.Equal(0.01, WrittenPrecision.NumberStep("12,00"), 9);
        Assert.Equal(1, WrittenPrecision.NumberStep("30"), 9);
        Assert.Equal(0.01, WrittenPrecision.NumberStep("9.299.292,99"), 9);
        Assert.Equal(1.0 / 3600, WrittenPrecision.AngleStep("208°37'12\""), 12);
        Assert.Equal(0.01 / 3600, WrittenPrecision.AngleStep("06°20'12.79\"S"), 12);
        Assert.Equal(1.0 / 60, WrittenPrecision.AngleStep("45°30'"), 12);
        Assert.Equal(0.0001, WrittenPrecision.AngleStep("120,6839°"), 12);
        // Segundos com 2 casas: ±0,005" ≈ ±15 cm no terreno.
        Assert.InRange(WrittenPrecision.GeoRounding(-6.3, 0.01 / 3600, 0.01 / 3600), 0.15, 0.16);
    }

    [Fact]
    public void TravessaoNaoEhSinalDeMenos()
    {
        var r = SmartReader.Read("no azimute e distância de 208°37'12\" - 7,26 metros, até o vértice P02; deste, no azimute e distância de " +
                                 "298°37'12\" – 12,00 metros, até o vértice P03; deste, no azimute e distância de 28°37'12\" -30,00 metros até o P04", check: false);
        Assert.All(r.Courses, c => Assert.True(c.Distance > 0));
        Assert.Equal(new[] { 7.26, 12.00, 30.00 }, r.Courses.Select(c => c.Distance));
    }

    [Fact]
    public void MemorialDeRetificacao_DesenhaPelosTrechos()
    {
        var d = CertidaoParser.Parse(Retificacao);
        Assert.True(d.HasCoordinates);
        Assert.Equal(PerimeterCoordKind.Geographic, d.Coordinates.Kind);
        Assert.Equal(6, d.Coordinates.Vertices.Count);
        Assert.Equal(360.0, d.Area);
        Assert.Equal(84.0, d.Perimetro);
        // Coordenadas com 2 casas nos segundos (±15 cm); trechos ao centímetro e ao segundo.
        Assert.All(d.Coordinates.Vertices, v => Assert.InRange(v.Rounding!.Value, 0.15, 0.16));
        var cs = d.Coordinates.StatedCourses;
        Assert.Equal(new[] { 7.26, 7.26, 15.48, 12.00, 30.00, 12.00 }, cs.Select(c => c.Distance));
        Assert.True(DrawingCheck.CoursesMorePrecise(d.Coordinates.Vertices, d.Coordinates.Kind, cs));
        var res = PerimeterText.Build(d.Coordinates.Vertices, d.Coordinates.Kind, DatumId.Sirgas2000, 23, true, true, cs);
        // Cada trecho cabe no arredondamento das coordenadas (até ~0,2 m de diferença, que ±15 cm em cada ponta explicam).
        Assert.Equal(6, res.CheckedOk);
        var v = DrawingCheck.ForCoursesWithCoordinates(res, d.Coordinates.Vertices, d.Coordinates.Kind, cs, d.Figures);
        Assert.Equal(VerdictLevel.Conferido, v.Level);
        Assert.True(v.DrawFromCourses);
        // O desenho pelos trechos fecha o retângulo de 12 × 30 m.
        var t = Traverse.Compute(DrawingCheck.ToTrueNorth(cs, res), adjust: true);
        Assert.InRange(t.Area, 359.9, 360.1);
    }

    [Fact]
    public void CasasDecimaisPerdidasViramDuvida()
    {
        // O OCR perdeu as casas da latitude do P04 ("06°20'13"S"): não pode afrouxar a conferência – vira dúvida.
        var d = CertidaoParser.Parse(Retificacao.Replace("Latitude 06°20'13.65\"S", "Latitude 06°20'13\"S"));
        Assert.Contains(d.Doubts, x => x.Contains("P04") && x.Contains("menos casas decimais"));
        Assert.All(d.Coordinates.Vertices, v => Assert.InRange(v.Rounding!.Value, 0.15, 0.16));
        var res = PerimeterText.Build(d.Coordinates.Vertices, d.Coordinates.Kind, DatumId.Sirgas2000, 23, true, true, d.Coordinates.StatedCourses);
        var v = SynthCorpusTests.Plugin(d, 23, d.Doubts).Verdict;
        Assert.NotEqual(VerdictLevel.Conferido, v.Level);
    }

    [Fact]
    public void DistanciaErradaNaoConfere()
    {
        // 15,48 lido como 16,48: o polígono não fecha, a área e o perímetro não batem.
        var d = CertidaoParser.Parse(Retificacao.Replace("15,48 metros", "16,48 metros"));
        var v = SynthCorpusTests.Plugin(d, 23, d.Doubts).Verdict;
        Assert.Equal(VerdictLevel.Divergente, v.Level);
    }

    [Fact]
    public void SoCoordenadas_SemConferencia()
    {
        var d = CertidaoParser.Parse("Vértice  Este (X)  Norte (Y)\nP-01  709.598,39  9.142.612,10\nP-02  709.628,39  9.142.612,10\n" +
                                     "P-03  709.628,39  9.142.592,10\nP-04  709.598,39  9.142.592,10");
        Assert.True(d.HasCoordinates);
        var v = SynthCorpusTests.Plugin(d, 23).Verdict;
        Assert.Equal(VerdictLevel.SemConferencia, v.Level);
        Assert.False(v.Verified);
    }

    [Fact]
    public void TrechosSemAreaNemPerimetro_EmParte()
    {
        var d = CertidaoParser.Parse("Partindo do vértice P1, segue com azimute de 86°04'45\" e distância de 31,44 m até o vértice P2; deste, segue " +
                                     "com azimute de 189°03'53\" e distância de 22,03 m até o vértice P3; deste, segue com azimute de 273°18'45\" e " +
                                     "distância de 31,15 m até o vértice P4; deste, segue com azimute de 10°11'29\" e distância de 18,09 m até o vértice P1.");
        var v = SynthCorpusTests.Plugin(d, 23).Verdict;
        Assert.Equal(VerdictLevel.Parcial, v.Level);
        Assert.Contains(v.Items, i => i.Name == "Fechamento" && i.Status == CheckStatus.Ok);
        var com = CertidaoParser.Parse(d.Description + " Área de " + DrawingCheck.Shoelace(DrawingCheck.TraversePoints(d.Courses)).ToString("0.00", new System.Globalization.CultureInfo("pt-BR")) + " m².");
        Assert.Equal(VerdictLevel.Conferido, SynthCorpusTests.Plugin(com, 23).Verdict.Level);
    }

    [Fact]
    public void MedidasDosLados_PelaDireitaEsquerda()
    {
        var d = CertidaoParser.Parse("IMÓVEL: Lote nº 18, com a área de 360,00 mº (trezentos e sessenta metros quadrados), Limitando-se pela frente, com " +
                                     "Rua D medindo 12 metros; pelo fundo, com lotes nº 14 e 15 medindo 12 metros; pela direita com o lote nº 19, medindo " +
                                     "30 metros, pela esquerda com o lote nº 17, medindo 30 metros; situada à Quadra C. PROPRIETÁRIO: Fulano de Tal, " +
                                     "portador da C.I.RG nº 213 139-SSP-GO, inscrito no CPF/MF sob nº 020 127 313-68, domiciliado à Rua B nº 385.");
        Assert.False(d.HasCoordinates);
        Assert.True(d.FromSides);
        Assert.Equal(new[] { 12.0, 30.0, 12.0, 30.0 }, d.Courses.Select(c => Math.Round(c.Distance, 6)));
        Assert.Equal(360.0, d.Area);
        var v = SynthCorpusTests.Plugin(d, 23).Verdict;
        Assert.Equal(VerdictLevel.SemConferencia, v.Level);
    }

    [Fact]
    public void GiroDaImagem()
    {
        var g = new GrayImage(3, 2, new byte[] { 1, 2, 3, 4, 5, 6 });
        var r1 = g.Rotate(1);
        Assert.Equal((2, 3), (r1.Width, r1.Height));
        Assert.Equal(new byte[] { 4, 1, 5, 2, 6, 3 }, r1.Px);
        Assert.Equal(new byte[] { 6, 5, 4, 3, 2, 1 }, g.Rotate(2).Px);
        Assert.Equal(g.Px, g.Rotate(1).Rotate(3).Px);
        Assert.Equal(g.Px, g.Rotate(2).Rotate(2).Px);
    }

    [Fact]
    public void PaginaDeLado()
    {
        var em_pe = Enumerable.Range(0, 20).Select(i => new DocWord(0, "palavra", 10, i * 50, 22, i * 50 + 45)).ToList();
        var deitadas = Enumerable.Range(0, 20).Select(i => new DocWord(0, "palavra", 10, i * 20, 80, i * 20 + 14)).ToList();
        Assert.True(PageOrientation.VerticalShare(em_pe) > 0.9);
        Assert.True(PageOrientation.VerticalShare(deitadas) < 0.1);
        var nada = new CertidaoData();
        Assert.True(PageOrientation.ShouldTryRotations(deitadas, nada));
        var ok = CertidaoParser.Parse(Retificacao);
        Assert.False(PageOrientation.ShouldTryRotations(deitadas, ok));
        Assert.True(PageOrientation.ShouldTryRotations(em_pe, ok));
    }

    [Fact]
    public void DuplaLeitura()
    {
        var a = CertidaoParser.Parse(Retificacao);
        var b = CertidaoParser.Parse(Retificacao.Replace("15,48 metros", "15,43 metros"));
        Assert.Empty(ReadingAgreement.Compare(a, CertidaoParser.Parse(Retificacao)));
        var doubts = ReadingAgreement.Compare(a, b);
        Assert.Single(doubts);
        Assert.Contains("Trecho 3", doubts[0]);
        // Com dúvida da dupla leitura, o desenho pede confirmação mesmo conferindo.
        Assert.Equal(VerdictLevel.Parcial, SynthCorpusTests.Plugin(a, 23, doubts).Verdict.Level);
    }

    [Fact]
    public void OcrPlan_GiraEConfere()
    {
        // OCR simulado: só lê a página na orientação certa (girada 90°); a segunda leitura lê um número diferente.
        var page = new GrayImage(4, 2);
        int calls = 0;
        List<DocWord> Ocr(GrayImage g, int idx, bool flatten, bool single)
        {
            calls++;
            if (g.Width != 2) return new List<DocWord> { new(0, "xx", 0, 0, 5, 30) };
            string text = single ? Retificacao.Replace("12,00 metros, até o vértice P05", "12,06 metros, até o vértice P05") : Retificacao;
            return text.Split(' ').Select((w, i) => new DocWord(0, w, i * 30, 10, i * 30 + 25, 22)).ToList();
        }
        var o = OcrPlan.Run(new[] { (0, page) }, Array.Empty<DocWord>(), Ocr);
        Assert.Equal(1, o.Best!.Turns);
        Assert.True(o.Best.Data.HasCoordinates);
        Assert.Contains(o.Doubts, x => x.Contains("Trecho 4"));
        Assert.Contains(o.Attempts, x => x.Contains("girada 90°"));
    }

    [Fact]
    public void NumerosSoltosDeCertidaoNaoViramVertices()
    {
        // Texto de certidão com palavras seguidas de dois números ("com 360,00 18,00"): não é tabela de coordenadas locais.
        var d = CertidaoParser.Parse("E N\ncom 360,00 18,00\nRua 14,00 12,00\nnº 30,00 19,00\nportador 20,00 213,00\nresidente 2,29 385,00");
        Assert.False(d.HasCoordinates);
    }
}
