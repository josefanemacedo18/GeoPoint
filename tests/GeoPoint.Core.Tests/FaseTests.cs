using System.Text.Json;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;
using GeoPoint.Core.Settings;

namespace GeoPoint.Core.Tests;

public class FaseTests
{
    [Fact]
    public void LegacyProject_MigratesToFase0()
    {
        // JSON de versão anterior: registro de vértices na raiz, lotes sem fase.
        const string json = """
        { "SchemaVersion": 1, "Lots": [ { "Id": "a", "Nome": "01", "Outer": [] } ], "Operations": [ { "Id": "op", "ParentIds": ["a"] } ],
          "Vertices": [ { "Number": 7, "X": 1, "Y": 2 } ], "QuadraVertexCounters": { "A": 3 } }
        """;
        var p = JsonSerializer.Deserialize<ParcelProject>(json, JsonOptions.Default)!;
        Assert.Single(p.Vertices);
        Assert.Equal(7, p.Vertices[0].Number);
        Assert.Equal(3, p.QuadraVertexCounters["A"]);
        Assert.Single(p.Fases);
        Assert.Equal(0, p.Fase.Numero);
        Assert.Equal(p.Fase.Id, p.Lots[0].FaseId);
        Assert.Equal(p.Fase.Id, p.Operations[0].FaseId);
        // Gravar e ler de novo mantém tudo (sem duplicar o registro antigo).
        var json2 = JsonSerializer.Serialize(p, JsonOptions.Compact);
        var p2 = JsonSerializer.Deserialize<ParcelProject>(json2, JsonOptions.Default)!;
        Assert.Single(p2.Fases);
        Assert.Equal(7, p2.Vertices.Single().Number);
        Assert.Null(p2.LegacyVertices);
    }

    [Fact]
    public void NewFase_CopiesActiveLots_RestartsNumbering_AndIsolates()
    {
        var a = TestProjects.Rect(0, 0, 10, 30, "01");
        var b = TestProjects.Rect(10, 0, 10, 30, "02");
        a.EdgeInfos.Add(new EdgeInfo { MX = 10, MY = 15, Tipo = ConfrontanteTipo.Lote, LotId = b.Id });
        var arch = TestProjects.Rect(50, 0, 10, 10, "99");
        arch.Status = LotStatus.Arquivado;
        var p = TestProjects.Project(a, b, arch);
        var ctx = TestProjects.Context(p, st => st.Naming.Numbering = VertexNumbering.Projeto);
        var f0 = p.Fase;
        Assert.Equal(new[] { "P-01", "P-02", "P-03", "P-04" }, ctx.Report(a).Vertices.Select(v => v.Name).OrderBy(x => x));
        Assert.Contains(ctx.Report(b).Vertices, v => v.Name == "P-06");

        var f1 = FaseService.Create(p, "Remembramento", "Remembramento");
        ctx.Invalidate();
        Assert.Equal(f1.Id, p.ActiveFaseId);
        var lots1 = p.ActiveLots.ToList();
        Assert.Equal(2, lots1.Count);                       // só os ativos
        Assert.All(lots1, l => Assert.DoesNotContain(l.Id, new[] { a.Id, b.Id }));
        var a1 = lots1.Single(l => l.Nome == "01");
        var b1 = lots1.Single(l => l.Nome == "02");
        // Confrontante aponta para a cópia do vizinho.
        Assert.Equal(b1.Id, a1.EdgeInfos.Single().LotId);
        // Numeração da fase nova começa do zero e não enxerga lotes da fase 0.
        Assert.DoesNotContain(f1.Vertices, v => v.Number > 6);
        var names = ctx.Report(b1).Vertices.Select(v => v.Name).OrderBy(x => x).ToList();
        Assert.Contains("P-01", ctx.Report(a1).Vertices.Select(v => v.Name));
        Assert.Equal("Lote 01 – Quadra A", ctx.Report(b1).Segments.Single(s => s.ConfrontanteLotId == a1.Id).Confrontante);
        Assert.DoesNotContain(ctx.Report(a1).Segments, s => s.ConfrontanteLotId == a.Id || s.ConfrontanteLotId == b.Id);
        Assert.Equal(6, f1.Vertices.Count);
        _ = names;

        // Voltar à fase 0: lotes e numeração originais.
        FaseService.Activate(p, f0.Id);
        ctx.Invalidate();
        Assert.Equal(new[] { a.Id, b.Id }, p.ActiveLots.Select(l => l.Id).OrderBy(x => x == a.Id ? 0 : 1));
        Assert.Equal(10, f0.Vertices.Count);   // 6 dos lotes ativos + 4 do lote arquivado

        // Excluir a fase 1 remove as cópias.
        var removed = FaseService.Delete(p, f1.Id);
        Assert.Equal(2, removed.Count);
        Assert.Single(p.Fases);
        Assert.Throws<InvalidOperationException>(() => FaseService.Delete(p, f0.Id));
    }

    [Fact]
    public void UserDefinedFases_EmptyPartialRenameDelete()
    {
        var a = TestProjects.Rect(0, 0, 10, 30, "01");
        var b = TestProjects.Rect(10, 0, 10, 30, "02");
        var p = TestProjects.Project(a, b);
        var f0 = p.Fase;
        f0.Nome = "Regularização"; f0.Situacao = "Regularização";
        // Fase vazia (sem lotes de partida).
        var vazia = FaseService.Create(p, "Loteamento novo", "Loteamento", "");
        Assert.Empty(p.ActiveLots);
        Assert.Equal("", vazia.BaseFaseId);
        // Fase com só o lote 02.
        var parcial = FaseService.Create(p, "Desmembramento do 02", "Desmembramento", f0.Id, new[] { b.Id });
        Assert.Equal("02", Assert.Single(p.ActiveLots).Nome);
        Assert.Equal(f0.Id, parcial.BaseFaseId);
        FaseService.Renumber(p, parcial, 5);
        Assert.Throws<InvalidOperationException>(() => FaseService.Renumber(p, vazia, 5));
        // A fase inicial pode ser excluída quando nenhuma outra parte dela e há outras fases.
        Assert.Throws<InvalidOperationException>(() => FaseService.Delete(p, f0.Id));   // a fase "parcial" parte dela
        FaseService.Delete(p, parcial.Id);
        FaseService.Delete(p, f0.Id);
        Assert.Single(p.Fases);
        Assert.Equal(vazia.Id, p.Fase.Id);
        Assert.DoesNotContain(p.Lots, l => l.Id == a.Id || l.Id == b.Id);
    }
}

public class IndividualMemorialTests
{
    [Fact]
    public void IndividualMemorial_OnlyOwnLot_AndLocalNumbering()
    {
        var parent = TestProjects.Rect(0, 0, 20, 30, "03");
        parent.Matricula = "12.345";
        parent.Status = LotStatus.Arquivado;
        var a = TestProjects.Rect(0, 0, 10, 30, "03-A");
        var b = TestProjects.Rect(10, 0, 10, 30, "03-B");
        b.IsRemanescente = true;
        var p = TestProjects.Project(parent, a, b);
        var op = new OperationRecord { Type = OperationType.Desmembramento, ParentIds = { parent.Id }, ChildIds = { a.Id, b.Id } };
        p.Operations.Add(op);
        var ctx = TestProjects.Context(p, st => st.Naming.Numbering = VertexNumbering.Lote);
        var gen = new GeoPoint.Core.Memorial.MemorialGenerator(ctx, GeoPoint.Core.Memorial.MemorialPhrases.Load(null));
        var text = ReportAndDocumentTests.DocText(gen.Generate(new GeoPoint.Core.Memorial.MemorialRequest
        {
            Kind = GeoPoint.Core.Memorial.MemorialKind.Lotes, Lots = new() { b }, Operation = op,
        }), out _);
        // Memorial focado só no próprio lote: sem origem e sem os demais lotes; o remanescente fica com a matrícula do original.
        GeoPoint.Core.Operations.ParcelOperations.AssignDesmembramentoMatriculas(p, parent, new[] { a, b });
        text = ReportAndDocumentTests.DocText(gen.Generate(new GeoPoint.Core.Memorial.MemorialRequest
        {
            Kind = GeoPoint.Core.Memorial.MemorialKind.Lotes, Lots = new() { b }, Operation = op,
        }), out _);
        Assert.Contains("DESMEMBRAMENTO – LOTE REMANESCENTE", text);
        Assert.DoesNotContain("ORIGEM", text);
        Assert.DoesNotContain("Lote 03-A", text.Replace("Lote 03-A – Quadra A (", ""));
        Assert.Contains("12.345", text);
        // O outro lote resultante não tem matrícula (não herda a do imóvel nem a do original).
        var textA = ReportAndDocumentTests.DocText(gen.Generate(new GeoPoint.Core.Memorial.MemorialRequest
        {
            Kind = GeoPoint.Core.Memorial.MemorialKind.Lotes, Lots = new() { a }, Operation = op,
        }), out _);
        Assert.Contains("DESMEMBRAMENTO", textA);
        Assert.DoesNotContain("12.345", textA);
        Assert.Equal("", p.EffectiveMatricula(a));
        // Numeração própria: P-01 a P-04, nenhum P-05 (que seria a numeração do projeto).
        Assert.Contains("P-01", text);
        Assert.DoesNotContain("P-05", text);
    }
}

public class ControlPointTests
{
    [Fact]
    public void ControlPoints_RoundTripAndNearestVertex()
    {
        var lot = TestProjects.Rect(0, 0, 10, 30, "01");
        var p = TestProjects.Project(lot);
        var ctx = TestProjects.Context(p);
        // Ponto a 0,40 m a leste do vértice (10, 30): coordenadas geodésicas calculadas pelo próprio projeto.
        var g = ctx.Coords.ToGeodetic(new Vec2(10.4, 30))!.Value;
        p.ControlPoints.Add(new ControlPoint { Nome = "Marco 1", Lat = g.LatDeg, Lon = g.LonDeg });
        var r = Assert.Single(ControlPointCheck.Compare(ctx));
        Assert.Equal(10.4, r.Position!.Value.X, 3);
        Assert.Equal(30, r.Position!.Value.Y, 3);
        Assert.Equal(0.4, r.Distance!.Value, 3);
        Assert.Equal(lot.Id, r.Lot!.Id);
        // KML exportado pelo próprio GeoPoint é lido de volta (nomes e coordenadas dos vértices).
        ctx.Settings.Kml.IncludeVertices = true;
        var kml = GeoPoint.Core.Export.GisExport.BuildKml(ctx, new[] { lot }, "Teste");
        var pts = GeoPoint.Core.Export.KmlReader.ParsePoints(kml);
        Assert.Contains(pts, x => x.Name.StartsWith("P-01"));
    }
}
