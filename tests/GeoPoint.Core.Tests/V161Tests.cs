using GeoPoint.Core.Calc;
using GeoPoint.Core.Documents;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;

namespace GeoPoint.Core.Tests;

/// <summary>1.6.1: confrontantes da certidão guardados com as linhas e aplicados no Reconhecer Lotes; leitura do texto colado.</summary>
public class V161Tests
{
    private static List<Curve2> Rect(double x0, double y0, double w, double h)
    {
        var p = new[] { new Vec2(x0, y0), new Vec2(x0 + w, y0), new Vec2(x0 + w, y0 + h), new Vec2(x0, y0 + h) };
        return Enumerable.Range(0, 4).Select(i => (Curve2)new LineCurve(p[i], p[(i + 1) % 4])).ToList();
    }

    [Fact]
    public void PendingConfrontantes_GoToTheRecognizedLot()
    {
        var p = new ParcelProject();
        var lines = Rect(0, 0, 10, 30);
        var ids = new[] { "L0", "L1", "L2", "L3" };
        int stored = PendingConfrontantes.Store(p, lines, ids,
            new[] { ("Rua A", true), ("Lote 06", false), ("", false), ("Maria José Lima", false) }, "certidao.pdf");
        Assert.Equal(3, stored);
        Assert.Equal(3, p.PendingConfrontantes.Count);

        // O Reconhecer Lotes cria o lote com a divisa direita dividida em duas (vértice do vizinho no meio) e o lote vizinho.
        var a = TestProjects.Poly("01", new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 15), new Vec2(10, 30), new Vec2(0, 30));
        var b = TestProjects.Rect(10, 0, 10, 30, "02");
        // Divisa da esquerda já tinha confrontante informado: fica como está.
        a.EdgeInfos.Add(new EdgeInfo { MX = 0, MY = 15, Confrontante = "Já informado" });
        int applied = PendingConfrontantes.Apply(p, new[] { a, b });

        Assert.Equal(3, applied);   // frente + as duas partes da divisa direita
        Assert.Empty(p.PendingConfrontantes);
        Assert.Empty(b.EdgeInfos);  // o ponto de dentro do perímetro não cai no vizinho
        var front = a.EdgeInfos.Single(i => Math.Abs(i.MY) < 1e-9);
        Assert.Equal("Rua A", front.Confrontante);
        Assert.True(front.Testada);
        Assert.Equal(2, a.EdgeInfos.Count(i => i.Confrontante == "Lote 06"));
        Assert.Equal("Já informado", a.EdgeInfos.Single(i => Math.Abs(i.MX) < 1e-9).Confrontante);
    }

    [Fact]
    public void PendingConfrontantes_PrunedWhenLinesAreDeleted_AndReplacedWhenRedrawn()
    {
        var p = new ParcelProject();
        var lines = Rect(0, 0, 10, 30);
        PendingConfrontantes.Store(p, lines, new[] { "L0", "L1", "L2", "L3" },
            new[] { ("Rua A", true), ("Lote 06", false), ("Lote 10", false), ("Maria", false) }, "");
        // Mesma linha guardada de novo: substitui (não duplica).
        PendingConfrontantes.Store(p, lines, new[] { "L0", "X1", "X2", "X3" },
            new[] { ("Rua B", true), ("", false), ("", false), ("", false) }, "");
        Assert.Equal(4, p.PendingConfrontantes.Count);
        Assert.Equal("Rua B", p.PendingConfrontantes.Single(x => x.Line.Src.Contains("L0")).Confrontante);
        int removed = PendingConfrontantes.Prune(p, id => id != "L3" && id != "L2");
        Assert.Equal(2, removed);
        Assert.Equal(new[] { "L1", "L0" }, p.PendingConfrontantes.Select(x => x.Line.Src[0]));
        // Polígono aberto (menos de 3 linhas): nada guardado.
        Assert.Equal(0, PendingConfrontantes.Store(new ParcelProject(), lines.Take(2).ToList(), new[] { "a", "b" }, new[] { ("x", false), ("y", false) }, ""));
    }

    [Fact]
    public void PendingConfrontantes_SurviveProjectSaveAndLoad()
    {
        var p = new ParcelProject();
        PendingConfrontantes.Store(p, Rect(0, 0, 10, 30), new[] { "L0", "L1", "L2", "L3" },
            new[] { ("Rua A", true), ("", false), ("", false), ("", false) }, "certidao.pdf");
        var json = System.Text.Json.JsonSerializer.Serialize(p);
        var back = System.Text.Json.JsonSerializer.Deserialize<ParcelProject>(json)!;
        var pc = Assert.Single(back.PendingConfrontantes);
        Assert.Equal("Rua A", pc.Confrontante);
        Assert.True(pc.Testada);
        Assert.Equal("L0", pc.Line.Src.Single());
        Assert.Equal("certidao.pdf", pc.SourceFile);
    }

    [Fact]
    public void PastedDescription_GivesConfrontantesAndDistanceCheck()
    {
        // A janela põe só a descrição na aba Coordenadas; lendo de novo o texto colado, vêm os mesmos confrontantes e a conferência.
        var d = CertidaoParser.Parse(CertidaoTests.ModeloComum);
        var c = PerimeterText.ParseCoordinates(d.Description);
        var confs = CertidaoParser.ApplyTrechos(c, CertidaoParser.TrechosOf(d.Description));
        Assert.Equal(d.Confrontantes, confs);
        Assert.Equal(new[] { 7.35, 103.60, 7.35, 103.60 }, c.StatedCourses.Select(x => x.Distance));
        Assert.All(c.StatedCourses, x => Assert.True(double.IsNaN(x.AzimuthDeg)));

        // Memorial com azimutes: a conferência continua com azimute e distância (os trechos trazem os dois).
        var m = CertidaoParser.Parse(CertidaoTests.Moderna);
        var c2 = PerimeterText.ParseCoordinates(m.Description);
        CertidaoParser.ApplyTrechos(c2, CertidaoParser.TrechosOf(m.Description));
        Assert.Equal(new double[] { 90, 180, 270, 0 }, c2.StatedCourses.Select(x => x.AzimuthDeg));
    }

    [Theory]
    [InlineData("Coordenadas no SIRGAS 2000, projeção UTM, fuso 23 S.", 23, true)]
    [InlineData("Sistema UTM, Zona 24S, Datum SIRGAS 2000", 24, true)]
    [InlineData("Meridiano Central 45° W", 23, null)]
    [InlineData("MC -51, hemisfério norte", 22, false)]
    [InlineData("coordenadas E 236184,12/N 9296962,23 – UTM", null, null)]
    public void ReadZone_FromText(string text, int? zone, bool? south)
    {
        var (z, s) = CertidaoParser.ReadZone(text);
        Assert.Equal(zone, z);
        Assert.Equal(south, s);
    }

    [Fact]
    public void Trechos_ConfrontanteBeforeOrAfterTheArrival()
    {
        // Confrontante escrito antes ("segue confrontando com…, até o vértice") e depois ("até o vértice …, confrontando com").
        const string t =
            "Inicia-se no vértice V1; deste, segue confrontando com a Avenida Brasil, com azimute de 90°00'00\" e distância de 20,00 m, até o vértice V2; " +
            "deste, segue com azimute de 180°00'00\" e distância de 15,00 m até o vértice V3, confrontando com o lote 05 de propriedade de ANA LIMA; " +
            "daí, segue até o vértice V4, com azimute de 270°00'00\" e distância de 20,00 m, limitando-se com área remanescente; " +
            "finalmente, do vértice V4 segue com azimute de 0°00'00\" e distância de 15,00 m, confrontando com o lote 07, até o vértice V1, ponto inicial da descrição.";
        var ts = CertidaoParser.ReadTrechos(t);
        Assert.Equal(new[] { "V1", "V2", "V3", "V4" }, ts.Select(x => x.From));
        Assert.Equal(new[] { "V2", "V3", "V4", "V1" }, ts.Select(x => x.To));
        Assert.Equal(new[] { "Avenida Brasil", "Ana Lima", "área remanescente", "lote 07" }, ts.Select(x => x.Confrontante));
        Assert.Equal(new double?[] { 20, 15, 20, 15 }, ts.Select(x => x.Distance));
        Assert.Equal(new double?[] { 90, 180, 270, 0 }, ts.Select(x => x.Azimuth));
        // Sem coordenadas: os azimutes e distâncias vão para a aba Azimutes, com o confrontante de cada trecho.
        var d = CertidaoParser.Parse(t);
        Assert.False(d.HasCoordinates);
        Assert.Equal(4, d.Courses.Count);
        Assert.Equal(new[] { "Avenida Brasil", "Ana Lima", "área remanescente", "lote 07" }, d.Confrontantes);
    }

    [Fact]
    public void OldCertidao_FrenteFundosLados_WithCom()
    {
        var d = CertidaoParser.Parse("IMÓVEL: UM lote urbano na Rua G, n° 08, com a área de 360,00m² (trezentos e sessenta metros quadrados), limitando-se: Frente com a Rua G, medindo 12,00 metros; ao fundo limitando-se com o Lote 07, medindo 12,00 metros; pelo lado direito limitando-se com Ana Souza, medindo 30 metros, e por fim limitando-se pelo lado esquerdo com Bruno Lima, medindo 30 metros, totalizando um perímetro de 84,00 metros. PROPRIETÁRIAS: FULANA");
        Assert.True(d.FromSides);
        Assert.Equal(new double[] { 12, 30, 12, 30 }, d.Courses.Select(c => Math.Round(c.Distance, 3)));
        Assert.Equal(new[] { "Rua G", "Ana Souza", "Lote 07", "Bruno Lima" }, d.Confrontantes);
        Assert.Equal(360, d.Area);
        Assert.Equal(84, d.Perimetro);
    }

    [Fact]
    public void OldCertidao_Cardinal_Rectangle_AndTrapezoid()
    {
        var r = CertidaoParser.Parse("IMOVEL: - Um terreno que limita-se ao Norte, com Avenida Central, medindo 76 metros; ao Sul, com a Rua Um, medindo 76 metros, a Leste, com Lotes de Fulano e Beltrano, medindo 85 metros, e a Oeste, com a Rua Dois, medindo 85 metros, situado a Rua Dois, nesta cidade. PROPRIETÁRIO:- Alguém");
        Assert.True(r.FromSides);
        Assert.Equal(new double[] { 90, 0, 270, 180 }, r.Courses.Select(c => Math.Round(c.AzimuthDeg, 4)));
        Assert.Equal(new double[] { 76, 85, 76, 85 }, r.Courses.Select(c => c.Distance));
        // Ordem das divisas: sul, leste, norte, oeste.
        Assert.Equal(new[] { "Rua Um", "Lotes de Fulano e Beltrano", "Avenida Central", "Rua Dois" }, r.Confrontantes);
        var t = Traverse.Compute(r.Courses, adjust: false);
        Assert.Equal(76 * 85, t.Area, 6);

        // Lado sul citado duas vezes (erro comum de digitação): vale o primeiro, com aviso; o norte de 34,60 fecha com os lados de 15,50 e 16.
        var z = CertidaoParser.Parse("IMÓVEL: Um terreno que limite-se ao Norte, com a casa de Lourival, medindo 34,60 metros; ao sul, com a rua Hermes, medindo 34,60 metros, ao sul, com a rua Hermes, medindo 34 metros, a leste, com a casa de Hélbio, medindo 15,50 metros, e a Oeste, com a Travessa Tocantins, medindo 16 metros, situado à Travessa Tocantins, nesta cidade. Proprietária: Maria");
        Assert.True(z.FromSides);
        Assert.Contains(z.Warnings, w => w.Contains("duas vezes"));
        Assert.DoesNotContain(z.Warnings, w => w.Contains("não são retos"));
        Assert.Equal(34.6, z.Courses[2].Distance, 2);
    }

    [Fact]
    public void OldCertidao_OcrLookalikes_AreTolerated()
    {
        var d = CertidaoParser.Parse("Um terreno que limita-se ao Norte, com Avenida Central, mcdindo 76 mctros; ao Sul, com a Rua Um, medindo 76 metros, a Lcste, com Lotes de Fulano, medindo 85 mctros, e a Ocste, com a Rua Dois, medindo 85 metros, situado a Rua Dois, nesta cidade.");
        Assert.True(d.FromSides);
        Assert.Equal(4, d.Courses.Count);
    }
}
