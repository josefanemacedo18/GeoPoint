using GeoPoint.Core.Calc;
using GeoPoint.Core.Export;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;

namespace GeoPoint.Core.Tests;

/// <summary>Gera arquivos de exemplo quando a variável GP_SAMPLES aponta para uma pasta (inspeção visual).</summary>
public class SampleOutputTests
{
    [Fact]
    public void WriteSamples()
    {
        var dir = Environment.GetEnvironmentVariable("GP_SAMPLES");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);

        // Lote do memorial do usuário (azimutes e distâncias), redesenhado e fechado (ajuste de Bowditch).
        var courses = MemorialTextParser.Parse("distância 50,00m de Azimute 299°36’55’’; distância 48,63m de Azimute 180°28’14’’; distância 50,00m de Azimute 61°22’21’’");
        var tr = Traverse.Compute(courses, adjust: true);
        var lot = TestProjects.FromCurves(tr.Curves, "01");
        lot.Quadra = "";
        lot.Nome = "Lote Urbano";
        var mids = tr.Curves.Select(c => c.Mid).ToList();
        lot.EdgeInfos.Add(new EdgeInfo { MX = mids[0].X, MY = mids[0].Y, Confrontante = "Rua E", Testada = true });
        lot.EdgeInfos.Add(new EdgeInfo { MX = mids[1].X, MY = mids[1].Y, Confrontante = "Valtenes dos Santos Almeida" });
        lot.EdgeInfos.Add(new EdgeInfo { MX = mids[2].X, MY = mids[2].Y, Confrontante = "Jacy Rodrigues Aguiar" });

        var p = TestProjects.Project(lot);
        // Referência: P-01 com as coordenadas UTM do memorial original (23M, E 235096,80 / N 9299316,16).
        var g = CoordinateService.CrsToGeodetic(new CrsCoord(235096.80, 9299316.16, false), new CrsDef { Kind = CrsKind.Utm, UtmZone = 23, UtmSouth = true }, 150);
        p.Reference.Lat = g.LatDeg; p.Reference.Lon = g.LonDeg;
        p.Info.Proprietario = "Lucas Araújo Marinho Brasil";
        p.Info.Documento = "000.000.000-00";
        p.Info.Endereco = "Rua E, s/n";
        p.Info.Bairro = "Jardim São Manoel";
        var ctx = TestProjects.Context(p, s =>
        {
            s.Technician.Nome = "Nome do Responsável Técnico";
            s.Technician.Profissao = "Arquiteto e Urbanista";
            s.Technician.Registro = "A000000-0";
            s.Naming.Clockwise = false;
            s.Format.ZoneStyle = Settings.ZoneLabelStyle.ZonaLetra;
            s.Format.PlaneCoordDecimals = 2;
        });
        p.Info.RrtArt = "0000000";
        var gen = new MemorialGenerator(ctx, new MemorialPhrases());
        File.WriteAllBytes(Path.Combine(dir, "memorial-exemplo.docx"), gen.Generate(new MemorialRequest { Kind = MemorialKind.Lotes, Lots = new() { lot }, Date = new DateTime(2026, 7, 7) }));
        File.WriteAllBytes(Path.Combine(dir, "modelo-padrao.docx"), MemorialGenerator.DefaultTemplate());
        File.WriteAllBytes(Path.Combine(dir, "planilha-exemplo.xlsx"), ExcelReport.Build(ctx, new[] { lot }));
        File.WriteAllText(Path.Combine(dir, "exemplo.kml"), GisExport.BuildKml(ctx, new[] { lot }, "Exemplo"));

        // Loteamento de exemplo.
        var lots = new List<LotRecord>();
        var gleba = TestProjects.Rect(0, 0, 60, 70, "Gleba", "", LotUse.Gleba);
        gleba.Status = LotStatus.Ativo;
        lots.Add(gleba);
        for (int i = 0; i < 4; i++)
        {
            var l = TestProjects.Rect(i * 15, 0, 15, 30, (i + 1).ToString("00"));
            l.EdgeInfos.Add(new EdgeInfo { MX = i * 15 + 7.5, MY = 0, Confrontante = "Rua A", Testada = true });
            lots.Add(l);
        }
        lots.Add(TestProjects.Rect(0, 30, 60, 12, "Rua B", "", LotUse.SistemaViario));
        lots.Add(TestProjects.Rect(0, 42, 40, 28, "Praça", "", LotUse.AreaVerde));
        lots.Add(TestProjects.Rect(40, 42, 20, 28, "Creche", "", LotUse.AreaInstitucional));
        var p2 = TestProjects.Project(lots.ToArray());
        p2.Info.RuleProfileName = "Personalizado (exemplo municipal)";
        var ctx2 = TestProjects.Context(p2);
        var gen2 = new MemorialGenerator(ctx2, new MemorialPhrases());
        File.WriteAllBytes(Path.Combine(dir, "memorial-loteamento.docx"), gen2.Generate(new MemorialRequest { Kind = MemorialKind.Loteamento, Lots = lots }));
        File.WriteAllBytes(Path.Combine(dir, "loteamento.kmz"), GisExport.BuildKmz(GisExport.BuildKml(ctx2, lots, "Loteamento")));
    }
}
