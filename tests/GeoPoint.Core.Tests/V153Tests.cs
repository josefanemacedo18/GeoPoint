using GeoPoint.Core.Calc;
using GeoPoint.Core.Export;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Model;
using GeoPoint.Core.Settings;

namespace GeoPoint.Core.Tests;

/// <summary>Regras da versão 1.5.3: migração de configurações, chave protegida e cópias de segurança.</summary>
public class V153Tests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "gp153-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void Settings_V1File_MigratesToCurrent_AndMovesGoogleKeyOutOfFile()
    {
        var dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "configuracoes.json");
            // Configurações da versão 1 (pontos pequenos, vias 12 m, numeração do projeto, chave do Google em texto, opção antiga).
            File.WriteAllText(path, """
            {
              "Version": 1,
              "Annotation": { "DotMm": 1.2, "TextSizeMm": 2.5 },
              "Naming": { "Numbering": "Projeto", "VertexPrefix": "V-" },
              "Streets": { "Largura": 12, "Calcada": 2.5, "RaioEsquina": 3 },
              "Memorial": { "IncludeSystemDeclaration": true, "IncludeSide": false },
              "Imagery": { "Provider": "Google", "GoogleKey": "AIza-teste-153" }
            }
            """);
            var s = SettingsStore.Load(path);
            Assert.Equal(3, s.Version);
            Assert.Equal(2.2, s.Annotation.DotMm, 6);
            Assert.Equal(2.5, s.Annotation.TextSizeMm, 6);
            Assert.Equal(VertexNumbering.Lote, s.Naming.Numbering);
            Assert.Equal(7, s.Streets.Largura, 6);
            Assert.Equal(2, s.Streets.Calcada, 6);
            Assert.Equal(2, s.Streets.RaioEsquina, 6);
            Assert.False(s.Memorial.IncludeSide);
            // A chave continua disponível, mas saiu do arquivo (foi para o cofre).
            Assert.Equal("AIza-teste-153", s.Imagery.GoogleKey);
            Assert.DoesNotContain("AIza-teste-153", File.ReadAllText(path));
            Assert.DoesNotContain("IncludeSystemDeclaration", File.ReadAllText(path));
            Assert.Equal("AIza-teste-153", Secrets.Store.Read(Secrets.GoogleKeyName));
            // Ler de novo: a chave vem do cofre; a cópia das configurações também leva a chave.
            var again = SettingsStore.Load(path);
            Assert.Equal("AIza-teste-153", again.Imagery.GoogleKey);
            Assert.Equal("AIza-teste-153", again.Clone().Imagery.GoogleKey);
            Assert.DoesNotContain("AIza", SettingsStore.Serialize(again));
            // Apagar a chave.
            again.Imagery.GoogleKey = "";
            SettingsStore.Save(again, path);
            Assert.Null(Secrets.Store.Read(Secrets.GoogleKeyName));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Settings_KeepsKeyInFile_WhenVaultFails()
    {
        var dir = TempDir();
        var old = Secrets.Store;
        try
        {
            Secrets.Store = new BrokenStore();
            string path = Path.Combine(dir, "c.json");
            var s = new GeoPointSettings();
            s.Imagery.GoogleKey = "k-sem-cofre";
            SettingsStore.Save(s, path);
            Assert.Contains("k-sem-cofre", File.ReadAllText(path));
            Assert.Equal("k-sem-cofre", SettingsStore.Load(path).Imagery.GoogleKey);
        }
        finally { Secrets.Store = old; Directory.Delete(dir, true); }
    }

    private sealed class BrokenStore : ISecretStore
    {
        public string? Read(string name) => throw new InvalidOperationException();
        public void Write(string name, string? value) => throw new InvalidOperationException();
    }

    [Fact]
    public void Backups_Compressed_Deduplicated_Pruned_AndReadOldJson()
    {
        var dir = TempDir();
        try
        {
            var p = TestProjects.Project(TestProjects.Rect(0, 0, 10, 20, "01"));
            var t0 = new DateTime(2026, 11, 13, 10, 0, 0);
            string? a = ProjectBackups.Save(dir, p, "reconhecer lotes", t0);
            Assert.NotNull(a);
            Assert.EndsWith(".json.gz", a);
            // Mesmos dados: não grava outra cópia.
            Assert.Null(ProjectBackups.Save(dir, p, "anotar", t0.AddMinutes(1)));
            // Cópia antiga (.json, até 1.5.2) continua na lista e pode ser restaurada.
            File.WriteAllText(Path.Combine(dir, "20261112-090000 - antiga.json"),
                System.Text.Json.JsonSerializer.Serialize(TestProjects.Project(TestProjects.Rect(0, 0, 5, 5, "A"), TestProjects.Rect(5, 0, 5, 5, "B")), JsonOptions.Default));
            p.Lots.Add(TestProjects.Rect(10, 0, 10, 20, "02"));
            string? b = ProjectBackups.Save(dir, p, "gerar lote", t0.AddMinutes(2));
            Assert.NotNull(b);
            var list = ProjectBackups.List(dir);
            Assert.Equal(3, list.Count);
            Assert.Equal(new[] { "gerar lote", "reconhecer lotes", "antiga" }, list.Select(e => e.Reason));
            Assert.Equal(new[] { 2, 1, 2 }, list.Select(e => e.Lots));
            Assert.Equal(t0.AddMinutes(2), list[0].When);
            Assert.Equal(2, ProjectBackups.Load(list[2].Path).Lots.Count);
            // Mesmo segundo: não sobrescreve.
            p.Lots.RemoveAt(1);
            string? c = ProjectBackups.Save(dir, p, "gerar lote", t0.AddMinutes(2));
            Assert.NotEqual(b, c);
            // Guarda só as mais recentes.
            for (int i = 0; i < 6; i++) { p.Info.Matricula = "M" + i; ProjectBackups.Save(dir, p, "x", t0.AddHours(1 + i), keep: 4); }
            Assert.Equal(4, ProjectBackups.List(dir).Count);
            Assert.Equal("M5", ProjectBackups.Load(ProjectBackups.List(dir)[0].Path).Info.Matricula);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CoordinateTable_DetectsDatum_AndConverts()
    {
        Assert.Equal(DatumId.Sad69, CoordinateTableReader.DetectDatum("Coordenadas UTM – Datum: SAD-69, fuso 23"));
        Assert.Equal(DatumId.Sad69, CoordinateTableReader.DetectDatum("sistema sad69"));
        Assert.Equal(DatumId.CorregoAlegre, CoordinateTableReader.DetectDatum("Datum Córrego Alegre"));
        Assert.Equal(DatumId.Sirgas2000, CoordinateTableReader.DetectDatum("EPSG:31983"));
        Assert.Equal(DatumId.Wgs84, CoordinateTableReader.DetectDatum("WGS 84"));
        Assert.Equal(DatumId.Sad69, CoordinateTableReader.DetectDatum("EPSG 29193"));
        Assert.Null(CoordinateTableReader.DetectDatum("Vértice;E;N"));
        // Primeiro citado vale (ex.: "SAD 69 convertido de SIRGAS" é raro; a planilha costuma citar só um).
        Assert.Equal(DatumId.Sirgas2000, CoordinateTableReader.DetectDatum("SIRGAS 2000 (antes SAD 69)"));

        const string body = "P-01;235142,50;9299292,99\nP-02;235113,25;9299307,98\n";
        var sirgas = CoordinateTableReader.Parse("Vértice;E;N\n" + body, 23, true);
        Assert.False(sirgas.DatumFromFile);
        Assert.Equal(DatumId.Sirgas2000, sirgas.Datum);
        Assert.Contains("considerado SIRGAS 2000", sirgas.DatumText);
        var sad = CoordinateTableReader.Parse("Datum: SAD 69 – UTM 23S\nVértice;E;N\n" + body, 23, true);
        Assert.True(sad.DatumFromFile);
        Assert.Equal(DatumId.Sad69, sad.Datum);
        Assert.Contains("convertido para SIRGAS 2000", sad.DatumText);
        Assert.Equal(2, sad.Points.Count);
        // SAD 69 → SIRGAS 2000 no Maranhão: a posição muda algumas dezenas de metros (não é igual nem absurda).
        double dLat = (sad.Points[0].Lat - sirgas.Points[0].Lat) * 111_320, dLon = (sad.Points[0].Lon - sirgas.Points[0].Lon) * 111_320 * Math.Cos(sad.Points[0].Lat * Math.PI / 180);
        double shift = Math.Sqrt(dLat * dLat + dLon * dLon);
        Assert.InRange(shift, 20, 120);
        // Escolha explícita prevalece sobre o texto.
        var forced = CoordinateTableReader.Parse("Datum: SAD 69\n" + body, 23, true, DatumId.Sirgas2000);
        Assert.Equal(sirgas.Points[0].Lat, forced.Points[0].Lat, 9);
        // Geográficas em SAD 69 também são convertidas; WGS 84 fica igual a SIRGAS (diferença centimétrica).
        var gSad = CoordinateTableReader.Parse("SAD69\nM1\t-6,333656\t-47,394191", 23, true);
        var gWgs = CoordinateTableReader.Parse("WGS84\nM1\t-6,333656\t-47,394191", 23, true);
        Assert.NotEqual(-6.333656, gSad.Points[0].Lat, 5);
        Assert.Equal(-6.333656, gWgs.Points[0].Lat, 7);
        Assert.Equal(-47.394191, gWgs.Points[0].Lon, 7);
    }

    /// <summary>Desmembramento: só o remanescente leva a matrícula; os outros lotes saem sem (rótulo, KML, GeoJSON e anuência).</summary>
    private static (ProjectContext ctx, LotRecord a1, LotRecord a2, LotRecord a3) Desmembrado()
    {
        var a1 = TestProjects.Rect(0, 0, 12, 30, "01"); var a2 = TestProjects.Rect(12, 0, 12, 30, "02"); var a3 = TestProjects.Rect(24, 0, 12, 30, "03");
        a3.IsRemanescente = true;
        a3.Matricula = "1234";
        var p = TestProjects.Project(a1, a2, a3);
        p.Info.Proprietario = "Maria da Silva"; p.Info.Documento = "111.222.333-44"; p.Info.Matricula = "1234";
        p.Operations.Add(new OperationRecord { Type = OperationType.Desmembramento, ChildIds = { a1.Id, a2.Id, a3.Id } });
        a1.EdgeInfos.Add(new EdgeInfo { MX = 6, MY = 0, Confrontante = "Rua Projetada 1", Testada = true });
        a1.EdgeInfos.Add(new EdgeInfo { MX = 0, MY = 15, Confrontante = "João Pereira" });
        return (TestProjects.Context(p), a1, a2, a3);
    }

    [Fact]
    public void EffectiveData_SameInLabel_Gis_And_Anuencia()
    {
        var (ctx, a1, a2, a3) = Desmembrado();
        var fmt = new ValueFormatter(ctx.Settings.Format);
        // Rótulo único (vista e prancha).
        string l1 = LotLabels.Text(ctx.Report(a1), ctx.Project, fmt);
        string l3 = LotLabels.Text(ctx.Report(a3), ctx.Project, fmt);
        Assert.Contains("MARIA DA SILVA", l1);
        Assert.DoesNotContain("MATRÍCULA", l1);
        Assert.Contains("MATRÍCULA: 1234", l3);
        Assert.StartsWith(a3.DisplayName.ToUpperInvariant(), l3);
        Assert.DoesNotContain("PERÍMETRO", LotLabels.Text(ctx.Report(a1), ctx.Project, fmt, perimeter: false));
        Assert.Equal(l1, GeoPoint.Core.Sheets.SheetPreview.DefaultLabel(ctx.Report(a1), ctx, fmt));
        // GeoJSON e KML.
        var gj = System.Text.Json.JsonDocument.Parse(GisExport.BuildGeoJson(ctx, new[] { a1, a2, a3 }, false));
        var props = gj.RootElement.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("properties")).Where(x => x.GetProperty("tipo").GetString() == "lote")
            .ToDictionary(x => x.GetProperty("lote").GetString()!, x => (M: x.GetProperty("matricula").GetString(), O: x.GetProperty("proprietario").GetString()));
        Assert.Equal(("", "Maria da Silva"), props["01"]);
        Assert.Equal(("", "Maria da Silva"), props["02"]);
        Assert.Equal(("1234", "Maria da Silva"), props["03"]);
        var kml = System.Xml.Linq.XDocument.Parse(GisExport.BuildKml(ctx, new[] { a1, a3 }, "Teste"));
        var data = kml.Descendants().Where(e => e.Name.LocalName == "Data" && (string?)e.Attribute("name") == "matricula").Select(e => e.Value).ToList();
        Assert.Equal(new[] { "", "1234" }, data);
        // Anuência do lote 01: proprietário do imóvel, sem a matrícula do imóvel original.
        var groups = AnuenciaGenerator.Groups(ctx, a1, includeVias: false);
        var text = ReportAndDocumentTests.DocText(new AnuenciaGenerator(ctx).Generate(a1, groups, new DateTime(2026, 11, 13)), out _);
        Assert.Contains("Maria da Silva", text);
        Assert.Contains("João Pereira", text);
        Assert.DoesNotContain("1234", text);
        var text3 = ReportAndDocumentTests.DocText(new AnuenciaGenerator(ctx).Generate(a3, AnuenciaGenerator.Groups(ctx, a3, false), new DateTime(2026, 11, 13)), out _);
        Assert.Contains("1234", text3);
    }
}
