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
}
