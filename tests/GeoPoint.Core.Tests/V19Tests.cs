using GeoPoint.Core.Calc;
using GeoPoint.Core.Documents;

namespace GeoPoint.Core.Tests;

/// <summary>1.9: leitor inteligente (papel de cada número) e conferência cruzada (correção de números lidos errados). Dados fictícios.</summary>
public class V19Tests
{
    [Fact]
    public void RotuloDepoisDoNumero_E_MaiusculoMinusculo()
    {
        var r = SmartReader.Read("vértice V1 (584239,25 mE; 8295860,18 mN), vértice V2 (584236,89 ME; 8295845,63 mN), " +
                                 "vértice V3 (584225,26 mE; 8295847,32 mN), vértice V4 (584206,89 mE; 8295850,51 mN).");
        Assert.Equal(PerimeterCoordKind.Utm, r.Coordinates.Kind);
        Assert.Equal(new[] { "V1", "V2", "V3", "V4" }, r.Coordinates.Vertices.Select(v => v.Name));
        Assert.Equal(584236.89, r.Coordinates.Vertices[1].A, 3);
        Assert.Equal(8295845.63, r.Coordinates.Vertices[1].B, 3);
    }

    [Fact]
    public void MilharComEspaco_E_FormatoAmericano()
    {
        var a = SmartReader.Read("P-01 (N 8 828 696,86; E 273 747,97); P-02 (N 8 828 692,64; E 273 766,51); P-03 (N 8 828 671,65; E 273 761,74)");
        Assert.Equal(3, a.Coordinates.Vertices.Count);
        Assert.Equal(8828692.64, a.Coordinates.Vertices[1].B, 3);
        var b = SmartReader.Read("P-01: E 619,938.87 / N 8,105,458.48\nP-02: E 619,937.26 / N 8,105,449.70\nP-03: E 619,913.11 / N 8,105,454.02");
        Assert.Equal(619937.26, b.Coordinates.Vertices[1].A, 3);
        Assert.Equal(8105449.70, b.Coordinates.Vertices[1].B, 3);
    }

    [Fact]
    public void NumeroPartidoPeloOcr()
    {
        var r = SmartReader.Read("vértice A-1, de coordenadas N 8.487.544,36m e E 729.870,57m; vértice A-2, coordenadas N 8.487.591,25m e E\n729.830, 10m; " +
                                 "vértice A-3, N 8.487.642,81m e E 729.889,\n84m");
        Assert.Equal(3, r.Coordinates.Vertices.Count);
        Assert.Equal(729830.10, r.Coordinates.Vertices[1].A, 3);
        Assert.Equal(729889.84, r.Coordinates.Vertices[2].A, 3);
    }

    [Fact]
    public void TabelaComColunasDeAzimuteEDistancia()
    {
        var r = SmartReader.Read("Vértice  Este (X)  Norte (Y)  Azimute  Distância (m)  Confrontante\n" +
                                 "P-01  709.598,39  9.142.612,10  90°00'00\"  30,00  Lote 12\n" +
                                 "P-02  709.628,39  9.142.612,10  180°00'00\"  20,00  Rua 3\n" +
                                 "P-03  709.628,39  9.142.592,10  270°00'00\"  30,00  Lote 05\n" +
                                 "P-04  709.598,39  9.142.592,10  0°00'00\"  20,00  Rua 4");
        Assert.True(r.Table);
        Assert.Equal(PerimeterCoordKind.Utm, r.Coordinates.Kind);
        Assert.Equal(4, r.Coordinates.Vertices.Count);
        Assert.Equal(4, r.Coordinates.StatedCourses.Count);
        Assert.Equal(4, ReadingChoice.MatchCount(r.Coordinates));
    }

    [Fact]
    public void TabelaGeograficaComLetrasDoHemisferio()
    {
        var r = SmartReader.Read("Código   Latitude   Longitude   Altitude (m)\n" +
                                 "AB-M-1  9°09'51,101\" S  42°51'07,520\" W  817,99\n" +
                                 "AB-P-2  9°09'54,853\" S  42°51'09,580\" W  731,57\n" +
                                 "AB-M-3  9°09'48,800\" S  42°51'20,745\" W  431,79\n" +
                                 "AB-P-4  9°09'45,048\" S  42°51'18,684\" W  222,15");
        Assert.Equal(PerimeterCoordKind.Geographic, r.Coordinates.Kind);
        Assert.Equal(4, r.Coordinates.Vertices.Count);
        Assert.True(r.Coordinates.Vertices.All(v => v.A < -9 && v.B < -42));
    }

    [Fact]
    public void ColunasLidasEmBlocos()
    {
        var r = SmartReader.Read("Vértice Este (X) Norte (Y)\n\nP-01\nP-02\nP-03\nP-04\n\n709.598,39\n709.628,39\n709.628,39\n709.598,39\n\n" +
                                 "9.142.612,10\n9.142.612,10\n9.142.592,10\n9.142.592,10");
        Assert.Equal(new[] { "P-01", "P-02", "P-03", "P-04" }, r.Coordinates.Vertices.Select(v => v.Name));
        Assert.Equal(709628.39, r.Coordinates.Vertices[1].A, 3);
        Assert.Equal(9142592.10, r.Coordinates.Vertices[2].B, 3);
    }

    [Theory]
    [InlineData("rumo de 45°00'00\" NE e 10,00 metros", 45.0)]
    [InlineData("rumo de 45°00'00\" SE e 10,00 metros", 135.0)]
    [InlineData("rumo de SW 45°00'00\" e 10,00 metros", 225.0)]
    [InlineData("rumo de N 45°00'00\" W e 10,00 metros", 315.0)]
    [InlineData("rumo de 45°00'00\" Nw e 10,00 metros", 315.0)]
    public void Rumos(string clause, double az)
    {
        var r = SmartReader.Read($"do marco 1 segue com {clause} até o marco 2; daí segue com rumo de 45°00'00\" SE e 10,00 metros até o marco 3; " +
                                 "daí segue com rumo de 45°00'00\" SW e 10,00 metros até o marco 4; daí segue com rumo de 45°00'00\" NW e 10,00 metros", check: false);
        Assert.Equal(az, r.Courses[0].AzimuthDeg, 6);
    }

    // Quadrilátero irregular (nenhum lado paralelo a outro: um erro num lado não se confunde com o mesmo erro no lado oposto).
    private static string Poly(string d1 = "31,44", string a3 = "273°18'45\"", string d2 = "22,03") =>
        $"Partindo do vértice P1, segue com azimute de 86°04'45\" e distância de {d1} m até o vértice P2; deste, segue com azimute de " +
        $"189°03'53\" e distância de {d2} m até o vértice P3; deste, segue com azimute de {a3} e distância de 31,15 m até o vértice P4; " +
        "deste, segue com azimute de 10°11'29\" e distância de 18,09 m até o vértice P1.";

    private static string Square() =>
        "Partindo do vértice P1, segue com azimute de 90°00'00\" e distância de 30,00 m até o vértice P2; deste, segue com azimute de " +
        "180°00'00\" e distância de 20,00 m até o vértice P3; deste, segue com azimute de 270°00'00\" e distância de 30,00 m até o vértice P4; " +
        "deste, segue com azimute de 0°00'00\" e distância de 20,00 m até o vértice P1.";

    [Theory]
    [InlineData("36,44", "273°18'45\"", 31.44, 273.3125)]   // um dígito lido errado na distância
    [InlineData("3144", "273°18'45\"", 31.44, 273.3125)]    // vírgula perdida
    [InlineData("31,44", "273°48'45\"", 31.44, 273.3125)]   // minutos lidos errado
    [InlineData("31,44", "93°18'45\"", 31.44, 273.3125)]    // quadrante (azimute invertido)
    public void FechamentoCorrigeNumeroLidoErrado(string d1, string a3, double fixedD1, double fixedA3)
    {
        var r = SmartReader.Read(Poly(d1, a3));
        Assert.Equal(4, r.Courses.Count);
        Assert.Equal(fixedD1, r.Courses[0].Distance, 3);
        Assert.Equal(fixedA3, r.Courses[2].AzimuthDeg, 4);
        Assert.Single(r.Fixes);
        Assert.Contains("corrigido", string.Join(" ", r.Warnings));
    }

    [Fact]
    public void RetanguloSoAvisa()
    {
        // Num retângulo, 36 m na frente ou nos fundos explicam igual: não corrige sozinho, só avisa onde pode estar o erro.
        var r = SmartReader.Read(Square().Replace("30,00 m até o vértice P2", "36,00 m até o vértice P2"));
        Assert.Empty(r.Fixes);
        Assert.Contains(r.Warnings, w => w.Contains("trecho(s) 1, 3"));
    }

    [Fact]
    public void DistanciaIlegivelCalculadaPeloFechamento()
    {
        var r = SmartReader.Read(Poly().Replace("distância de 22,03 m até o vértice P3", "distância de ?? m até o vértice P3"));
        Assert.Equal(4, r.Courses.Count);
        Assert.Equal(22.03, r.Courses[1].Distance, 1);
        Assert.Contains(r.Fixes, f => f.From == "ilegível");
    }

    [Fact]
    public void CoordenadaLidaErradaCorrigidaPelosTrechos()
    {
        // N do P-03 lido 9.142.582,10 (o certo é 9.142.592,10): os trechos vizinhos mostram o valor certo.
        string t = "vértice P-01, de coordenadas N 9.142.612,10 m e E 709.598,39 m; deste, segue com azimute de 90°00'00\" e distância de 30,00 m " +
                   "até o vértice P-02, de coordenadas N 9.142.612,10 m e E 709.628,39 m; deste, segue com azimute de 180°00'00\" e distância de 20,00 m " +
                   "até o vértice P-03, de coordenadas N 9.142.582,10 m e E 709.628,39 m; deste, segue com azimute de 270°00'00\" e distância de 30,00 m " +
                   "até o vértice P-04, de coordenadas N 9.142.592,10 m e E 709.598,39 m; deste, segue com azimute de 0°00'00\" e distância de 20,00 m " +
                   "até o vértice P-01.";
        var r = SmartReader.Read(t);
        Assert.Equal(9142592.10, r.Coordinates.Vertices[2].B, 3);
        Assert.Contains(r.Fixes, f => f.What.Contains("P-03"));
    }

    [Fact]
    public void DigitoTrocadoNaCoordenadaPelaAreaEPelosOutrosVertices()
    {
        string t = "vértice V1 (698356,34 mE; 8275523,57 mN), vértice V2 (698361,75 mE; 8275532,03 mN), vértice V3 (698370,62 mE; 8275540,57 mN), " +
                   "vértice V4 (698379,28 mE; 8275533,21 mN), vértice V5 (098388,14 mE; 8275525,85 mN), vértice V6 (698373,85 mE; 8275508,85 mN).";
        var r = SmartReader.Read(t);
        Assert.Equal(PerimeterCoordKind.Utm, r.Coordinates.Kind);
        Assert.Equal(698388.14, r.Coordinates.Vertices[4].A, 3);
    }

    [Fact]
    public void NumerosDeDocumentosNaoViramCoordenadas()
    {
        var r = SmartReader.Read("Matrícula nº 1.234.567, CPF 123.456.789-09, CEP 65000-000, R$ 1.250.000,00, protocolo 9.876.543 em 01/02/2024. " +
                                 "SIRGAS 2000, fuso 23 S, meridiano central 45° W.");
        Assert.Empty(r.Coordinates.Vertices);
        Assert.Empty(r.Courses);
    }

    [Fact]
    public void ModeloEscolhido_SoTrechos()
    {
        string t = "Inicia-se no vértice P-01, de coordenadas N 9.299.292,99 m e E 235.142,50 m; " + Square().Replace("Partindo do vértice P1, ", "");
        var auto = SmartReader.Read(t);
        Assert.Single(auto.Coordinates.Vertices);
        Assert.Equal(4, auto.Courses.Count);
        Assert.Equal("Azimutes e distâncias, com a coordenada do ponto inicial", auto.Model);
        var forced = SmartReader.Read(t, ReadingModel.Courses);
        Assert.Equal(4, forced.Courses.Count);
    }

    [Fact]
    public void CertidaoUsaOLeitorInteligente()
    {
        var d = CertidaoParser.Parse("DESCRIÇÃO DO IMÓVEL: O imóvel tem os seguintes vértices: vértice V1 (584239,25 mE; 8295860,18 mN), " +
                                     "vértice V2 (584236,89 mE; 8295845,63 mN), vértice V3 (584225,26 mE; 8295847,32 mN), vértice V4 (584206,89 mE; 8295850,51 mN).");
        Assert.True(d.HasCoordinates);
        Assert.Equal(PerimeterCoordKind.Utm, d.Coordinates.Kind);
        Assert.StartsWith("Memorial com coordenadas UTM", d.Model);
        var steps = ReadingReport.Steps(d, 300, "", null);
        Assert.Contains(steps, s => s.Name == "Modelo do texto" && s.Status == CheckStatus.Ok);
    }
}
