using GeoPoint.Core.Calc;
using GeoPoint.Core.Documents;
using GeoPoint.Core.Geodesy;

namespace GeoPoint.Core.Tests;

/// <summary>
/// 1.8: memorial SGL (coordenadas geográficas, "com azimute e distância: A e D", divisas Frente/Fundos/Lados, quadro de
/// confrontações) – o texto limpo e o texto do OCR (Tesseract) de uma foto de celular, com os erros reais. Nomes, documentos e
/// coordenadas trocados (deslocados) em relação ao documento real; a geometria é a mesma.
/// </summary>
public class V18Tests
{
    public const string SglLimpo = """"
MEMORIAL DESCRITIVO (SGL)
Imóvel: LOTE URBANO – RUA 07 Q-12 – LOTES 27 e 28 – LOTEAMENTO JARDIM EXEMPLO
Matrículas: Fusão/ Unificação - 10.001 e 10.002
Cartório (CNS): . 00.000-0
Município: U.F: - PORTO FRANCO - MA
Proprietário: FULANO DE TAL SOUZA
CPF nº: 000.000.000-00
Responsável Técnico: BELTRANO DA SILVA
Formação: Geomensor
Código Credenciamento CRA
CFT: 00000000000/MA
Área (Sistema Geodésico Local): 600,000 m²
Perímetro: 98,000 m

IMÓVEL DE DESCRIÇÃO DA PARCELA

Inicia-se a descrição deste perímetro no vértice M-01, georreferenciado no Sistema
Geodésico Brasileiro, DATUM - SIRGAS2000, MC-45°W, de coordenadas Longitude:-
47°19'51,247", Latitude:-6°18'34,773" de altitude 178,57m; deste segue Fundos
confrontando com LOTE 09- JOAO DOS SANTOS LIMA, com azimute e distância:
112°12'38" e 12,00m até o vértice M-02, de coordenadas Longitude:-47°19'50,886",
Latitude:-6°18'34,920" de altitude 178,60m; deste segue confrontando com LOTE 10 - ALFA
EMPREENDIMENTOS LTDA, com azimute e distância: 112°12'38" e 12,00m até o
vértice M-03, de coordenadas Longitude:-47°19'50,524", Latitude:-6°18'35,068" de altitude
178,60m; deste segue Lado Esquerdo confrontando com LOTE 26 - PEDRO ALVES
COSTA, com azimute e distância: 202°12'38" e 25,00m até o vértice M-04, de coordenadas
Longitude:-47°19'50,831", Latitude:-6°18'35,821" de altitude 178,60m; deste segue Frente
confrontando com RUA 07, com os seguintes azimutes e distâncias: 292°12'38" e 12,00m até
o vértice M-05, de coordenadas Longitude:-47°19'51,193", Latitude:-6°18'35,673" de altitude
178,59m; deste segue com 292°12'38" e 12,00m até o vértice M-06, de coordenadas
Longitude:-47°19'51,554", Latitude:-6°18'35,526" de altitude 178,59m; deste segue Lado
Direito confrontando com LOTE 29 MARCOS PEREIRA NUNES, com os seguintes azimutes e
distâncias: 22°12'38" e 25,00m até o vértice M-01, ponto inicial da descrição deste perímetro
de 98,00 m.

CONFRONTAÇÕES
Frente - RUA 07 - 24,00 m
Lado Direito - LOTE 29 MARCOS PEREIRA NUNES - 25,00 m
Fundos - LOTE 09- JOAO DOS SANTOS LIMA - 12,00m e
LOTE 10 - ALFA EMPREENDIMENTOS LTDA - 12,00m
Lado Esquerdo LOTE 26 - PEDRO ALVES COSTA - 25,00 m

Todas as coordenadas aqui descritas estão georreferenciadas ao Sistema Geodésico
Brasileiro tendo como Datum o SIRGAS2000. A área foi obtida pelas coordenadas cartesianas
locais referenciadas ao Sistema Geodésico Local. Todos os azimutes foram calculados pela
fórmula do Problema Geodésico Inverso (Puissant). Perímetro e Distâncias foram calculados
pelas coordenadas cartesianas geocêntricos.
Porto Franco -MA, 12 outubro 2026
FULANO DE TAL SOUZA
CPF nº: 000.000.000-00
Responsável Técnico: BELTRANO DA SILVA
Geomensor - CFT: 00000000000/MA
TRT: Nº CFT0000000000
"""";

    /// <summary>Texto do Tesseract sem preparar a foto: grau sumido ("1121238\""), grau como aspas e dois-pontos, sinal perdido,
    /// "&amp;" no lugar de "e", "m?" no lugar de "m²", perímetro do cabeçalho lido como 88 e "LOTE 40" no quadro.</summary>
    public const string SglOcr = """"
MEMORIAL DESCRITIVO (SGL)

imóvel: LOTE URBANO - RUA 07 Q.12
Matrículas: Fusão/ Unificação - 10 001 )
Cartório (CNS): . 00 000-0 nn

LOTES 27 e 28 - LOTEAMENTO JARDIM EXEMPLO

Municipio: UF. - PORTO FRANCO - MA
Propnetário: FULANO DE TAL SOUZA
CPF 000.000.000-00

Responsável Técnico: BELTRANO DA SILVA
Formação: Geomensor i
Cá Credenciamento CRA

CFT. 00000000000/MA

Área (Sistema Geodésico Local): 600,000 m?
Perimetro: 88,000 m

IMÓVEL DE DESCRIÇÃO DA PARCELA

o Inicia-se a descrição deste perímetro no vértice M-01, georreferenciado no Sistema
Geodésico Brasileiro, DATUM - SIRGAS2000, MC-45"W, de coordenadas Longitude:
47:19'51,247", Latitude:-6º18'34,773" de altitude 178,5/m, deste segue Fundos
confrontando com LOTE 09- JOAO DOS SANTOS LIMA, com azimute e distância
1121238" e 12,00m até o vértice M-02, de coordenadas Longitude:-47"19'50,886"
Latitude:-6"18'34,920" de altitude 178,60m; deste segue confrontando com LOTE 10 - ALFA
EMPREENDIMENTOS LTDA, com azimute e distância: 1121238" e 12,00m até o
vértice M-03, de coordenadas Longitude:-47º19'50,524", Latitude:-6"18'35,068” de altitude
478,60m; deste segue Lado Esquerdo confrontando com LOTE 26 - PEDRO ALVES
COSTA, com azimute e distância: 202º12'38" e 25,00m até o vértice M-04, de coordenadas
Longitude:-47º19'50,831", Latitude:-6º18'35,821" de altitude 178,60m; deste segue Frente
confrontando com RUA 07, com os seguintes azimutes e distâncias: 2921238" e 12,00m até
o vértice M-05, de coordenadas Longitude:-47"19'51,193", Latitude:-6º18'35,673" de altitude
478,59m: deste segue com 2921238" e 12,00m até o vértice M-06, de coordenadas
Longitude:-4719'51,554", Latitude:-6º18'35,526" de altitude 178,59m; deste segue Lado
Direito confrontando com LOTE 29 MARCOS PEREIRA NUNES, com os seguintes azimutes &
distâncias: 22º12'38" e 25,00m até o vértice M-01, ponto inicial da descrição deste perímetro

de 98,00 m.

CONFRONTAÇÕES

Frente - RUA 07 - 24,00m

Lado Direito - LOTE 29 MARCOS PEREIRA NUNES - 25,00 m
Fundos - LOTE 09- JOAO DOS SANTOS LIMA -12,00m e
LOTE 40 - ALFA EMPREENDIMENTOS LTDA - 12,00m

Lado Esquerdo LOTE 26 - PEDRO ALVES COSTA - 25,00 m

Todas as coordenadas aqui descritas estão georreferenciadas ao Sistema Geodésico
Brasileiro tendo como Datum o SIRGAS2000. A área foi obtida pelas coordenadas cartesianas
locais referenciadas ao Sistema Geodésico Local. Todos os azimutes foram calculados pela
fórmula do Problema Geodésico Inverso (Puissant). Perimetro e Distâncias foram calculados

pelas coordenadas cartesianas geocêntricos,

frança -MA, 12 outubro 2026

Vogaro An Lao du SB
FULANO DE TAL SOUZA “

CPF nº: 000.000.000-00

Z EA i á

Responsável Técnico:  BELTRANO DA SILVA
“Geomensor - CFT: 00000000000/MA

TRT: Nº CFT0000000000

qb!
"""";

    public static readonly string[] Confs =
    {
        "Lote 09 – Joao dos Santos Lima", "Lote 10 – Alfa Empreendimentos Ltda", "Lote 26 – Pedro Alves Costa", "Rua 07", "Rua 07",
        "Lote 29 Marcos Pereira Nunes",
    };

    [Theory]
    [InlineData(SglLimpo)]
    [InlineData(SglOcr)]
    public void SglMemorial_GeographicCoordinates_AzimuthsDistances_Confrontantes(string text)
    {
        var d = CertidaoParser.Parse(text);
        var c = d.Coordinates;
        Assert.Equal(PerimeterCoordKind.Geographic, c.Kind);
        Assert.Equal(new[] { "M-01", "M-02", "M-03", "M-04", "M-05", "M-06" }, c.Vertices.Select(v => v.Name));
        Assert.All(c.Vertices, v => Assert.True(v.A < 0 && v.B < 0));    // sul e oeste (o sinal perdido no OCR é corrigido)
        Assert.Equal(-(47 + 19 / 60.0 + 51.247 / 3600), c.Vertices[0].B, 9);
        Assert.Equal(-(6 + 18 / 60.0 + 34.773 / 3600), c.Vertices[0].A, 9);
        Assert.Equal(-(47 + 19 / 60.0 + 51.554 / 3600), c.Vertices[5].B, 9);
        Assert.Equal(DatumId.Sirgas2000, c.Datum);
        Assert.Equal(23, d.UtmZone);                                       // MC-45°W
        // Trechos: azimute e distância de cada um ("com azimute e distância: A e D" e "deste segue com A e D").
        Assert.Equal(6, d.Trechos.Count);
        Assert.Equal(new double?[] { 12, 12, 25, 12, 12, 25 }, d.Trechos.Select(t => t.Distance));
        double Az(double g) => g + 12 / 60.0 + 38 / 3600.0;
        Assert.Equal(new[] { Az(112), Az(112), Az(202), Az(292), Az(292), Az(22) }, d.Trechos.Select(t => Math.Round(t.Azimuth ?? -1, 9)).ToArray(), new RoundComparer());
        // Divisas e confrontantes (o trecho M-05 → M-06 não repete: herda "Rua 07", frente).
        Assert.Equal(Confs, d.Confrontantes);
        Assert.Equal(new[] { "Fundos", "Fundos", "Lado esquerdo", "Frente", "Frente", "Lado direito" }, d.Trechos.Select(t => t.Lado));
        Assert.True(d.Trechos[4].Inherited);
        Assert.True(CertidaoParser.IsStreet(d.Confrontantes[3]));
        // Área e perímetro do documento (o perímetro da descrição vale mais que o do cabeçalho).
        Assert.Equal(600, d.Area);
        Assert.Equal(98, d.Perimetro);
        // Quadro de confrontações.
        Assert.Equal(5, d.Confrontacoes.Count);
        Assert.Equal(new[] { "Frente", "Lado direito", "Fundos", "Fundos", "Lado esquerdo" }, d.Confrontacoes.Select(x => x.Lado));
        Assert.Equal(new double[] { 24, 25, 12, 12, 25 }, d.Confrontacoes.Select(x => x.Medida));
        // Desenho pelas coordenadas: fecha com 600 m² e os 6 trechos conferem com o texto (azimute verdadeiro: a convergência
        // meridiana aqui é de ~16', o azimute do memorial é o geodésico).
        var r = PerimeterText.Build(c.Vertices, c.Kind, DatumId.Sirgas2000, 23, true, true, c.StatedCourses);
        Assert.InRange(r.Area, 599, 601);
        Assert.InRange(r.Perimeter, 97.9, 98.1);
        Assert.Equal(6, r.Checked);
        Assert.Equal(6, r.CheckedOk);
    }

    [Fact]
    public void Confrontante_WithInitial_IsNotCutAtThePeriod()
    {
        var ts = CertidaoParser.ReadTrechos("Inicia-se no vértice M-01; deste segue confrontando com LOTE 10 - J. ALFA EMPREENDIMENTOS LTDA, com azimute e distância: 112°12'38\" e 12,00m até o vértice M-02; deste segue confrontando com RUA 07. Daí segue com 202°12'38\" e 25,00m até o vértice M-03.");
        Assert.Equal("Lote 10 – J. Alfa Empreendimentos Ltda", ts[0].Confrontante);
        Assert.Equal("Rua 07", ts[1].Confrontante);
        // Inicial lida como abreviação de duas letras pelo OCR ("J." → "Jd."): o nome continua.
        var t2 = CertidaoParser.ReadTrechos("Inicia-se no vértice M-01; deste segue confrontando com LOTE 10 - Jd. ALFA EMPREENDIMENTOS LTDA, com azimute e distância: 112°12'38\" e 12,00m até o vértice M-02.");
        Assert.Equal("Lote 10 – Jd. Alfa Empreendimentos Ltda", t2[0].Confrontante);
    }

    [Fact]
    public void SglOcr_HeaderPerimeterMisread_IsKeptAsOther()
    {
        var d = CertidaoParser.Parse(SglOcr);
        Assert.Equal(98, d.Perimetro);
        Assert.Equal(88, d.PerimetroOutro);
    }

    [Fact]
    public void AzimuthsTab_ReadsThePairsToo()
    {
        // Sem coordenadas (só o texto dos trechos): a aba de azimutes recebe os 6 trechos.
        var list = MemorialTextParser.Parse(CertidaoParser.Parse(SglLimpo).Description);
        Assert.Equal(6, list.Count);
        Assert.Equal(new double[] { 12, 12, 25, 12, 12, 25 }, list.Select(c => c.Distance));
        var t = Traverse.Compute(list, adjust: false);
        Assert.True(t.MisclosureLength < 0.01);
        Assert.InRange(t.Area, 599.9, 600.1);
    }

    [Theory]
    [InlineData("com azimute e distância 1121238\" e 12,00m", "com azimute e distância 112°12'38\" e 12,00m")]
    [InlineData("Longitude:-47\"22'50,886\"", "Longitude:-47°22'50,886\"")]
    [InlineData("Longitude: 47:22'51,247\"", "Longitude: 47°22'51,247\"")]
    [InlineData("Longitude:-4722'51,554\"", "Longitude:-47°22'51,554\"")]
    [InlineData("MC-45\"W", "MC-45°W")]
    [InlineData("600,000 m?", "600,000 m²")]
    [InlineData("e 12,/00m até", "e 12,00m até")]
    [InlineData("azimutes & distâncias", "azimutes e distâncias")]
    [InlineData("112º12'38\"” e 12,00m", "112°12'38\" e 12,00m")]
    [InlineData("292º12'38' e 12,00m", "292°12'38\" e 12,00m")]
    // Números comuns ficam como estão.
    [InlineData("CPF 123.456.789-09, 12,00 m, 178,57m", "CPF 123.456.789-09, 12,00 m, 178,57m")]
    [InlineData("Lote 1234\" ", "Lote 1234\" ")]
    public void OcrCleanup_RepairsAnglesAndUnits(string input, string expected) => Assert.Equal(expected, OcrCleanup.Normalize(input));

    private sealed class RoundComparer : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) < 1e-6;
        public int GetHashCode(double obj) => 0;
    }
}
