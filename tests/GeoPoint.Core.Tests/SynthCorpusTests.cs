using System.Text;
using System.Text.Json;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Documents;

namespace GeoPoint.Core.Tests;

/// <summary>
/// Memoriais FICTÍCIOS em vários modelos (UTM, SIGEF, tabelas, rumos, coordenadas locais, graus decimais, números com espaços ou
/// no formato americano…), cada um em texto limpo e lido por OCR (Tesseract) de uma folha limpa, escaneada e fotografada. A
/// verdade (vértices e trechos) está no .json de cada caso. Gerados por um script (coordenadas aleatórias): não há dados reais.
/// </summary>
public class SynthCorpusTests
{
    private static string Dir => Environment.GetEnvironmentVariable("GEOPOINT_SYNTH") is { Length: > 0 } d ? d
        : Path.Combine(AppContext.BaseDirectory, "TestData", "memoriais");

    public sealed class TruthVertex { public string name { get; set; } = ""; public double a { get; set; } public double b { get; set; } }
    public sealed class TruthCourse { public double az { get; set; } public double dist { get; set; } }
    public sealed class Truth
    {
        public string model { get; set; } = "";
        public string? kind { get; set; }
        public int? zone { get; set; }
        public List<TruthVertex> vertices { get; set; } = new();
        public List<TruthCourse> courses { get; set; } = new();
        public double? area { get; set; }
    }

    public static IEnumerable<(string Id, Truth T)> Cases() =>
        Directory.GetFiles(Dir, "*.json").OrderBy(f => f).Select(f => (Path.GetFileNameWithoutExtension(f), JsonSerializer.Deserialize<Truth>(File.ReadAllText(f))!));

    /// <summary>Compara a leitura com a verdade; devolve "" quando confere, senão o motivo.</summary>
    public static string Compare(Truth t, CertidaoData d)
    {
        if (t.vertices.Count > 0)
        {
            var v = d.Coordinates.Vertices;
            var kind = t.kind switch { "geo" => PerimeterCoordKind.Geographic, "local" => PerimeterCoordKind.Local, _ => PerimeterCoordKind.Utm };
            if (v.Count != t.vertices.Count) return $"vértices: {v.Count} de {t.vertices.Count}";
            if (d.Coordinates.Kind != kind) return $"sistema: {d.Coordinates.Kind} (esperado {kind})";
            double tol = kind == PerimeterCoordKind.Geographic ? 2e-7 : 0.0015;
            for (int i = 0; i < v.Count; i++)
                if (Math.Abs(v[i].A - t.vertices[i].a) > tol || Math.Abs(v[i].B - t.vertices[i].b) > tol)
                    return $"vértice {i + 1}: {v[i].A}; {v[i].B} (esperado {t.vertices[i].a}; {t.vertices[i].b})";
            return "";
        }
        var c = d.Courses;
        if (c.Count != t.courses.Count) return $"trechos: {c.Count} de {t.courses.Count}";
        for (int i = 0; i < c.Count; i++)
        {
            if (PerimeterText.AngleDiff(c[i].AzimuthDeg, t.courses[i].az) > 1.5 / 3600) return $"trecho {i + 1}: azimute {c[i].AzimuthDeg:0.0000} (esperado {t.courses[i].az:0.0000})";
            if (Math.Abs(c[i].Distance - t.courses[i].dist) > 0.006) return $"trecho {i + 1}: distância {c[i].Distance} (esperado {t.courses[i].dist})";
        }
        return "";
    }

    public static (int Ok, int Total, string Report) Run(Func<string, CertidaoData> read)
    {
        var sb = new StringBuilder();
        int ok = 0, total = 0;
        var byVariant = new Dictionary<string, (int Ok, int N)>();
        foreach (var (id, t) in Cases())
        {
            // "texto": o texto original; "limpo/scan/foto": OCR com as linhas montadas como no GeoPoint (pela posição das palavras);
            // "…-blocos": o texto do OCR na ordem dos blocos (como ao copiar de um PDF: colunas de tabela uma depois da outra).
            foreach (var variant in new[] { "texto", "limpo", "scan", "foto", "limpo-blocos", "scan-blocos", "foto-blocos" })
            {
                string file = Path.Combine(Dir, variant == "texto" ? id + ".txt"
                    : variant.EndsWith("-blocos") ? $"{id}.{variant[..^7]}.ocr.txt" : $"{id}.{variant}.linhas.txt");
                if (!File.Exists(file)) continue;
                string why;
                try { why = Compare(t, read(File.ReadAllText(file))); }
                catch (Exception ex) { why = "erro: " + ex.Message; }
                total++;
                var bv = byVariant.GetValueOrDefault(variant);
                if (why.Length == 0) { ok++; byVariant[variant] = (bv.Ok + 1, bv.N + 1); }
                else { byVariant[variant] = (bv.Ok, bv.N + 1); sb.AppendLine($"✗ {id} [{variant}] {why}"); }
            }
        }
        var head = new StringBuilder($"Acertos: {ok} de {total}\n");
        foreach (var (k, v) in byVariant) head.AppendLine($"  {k}: {v.Ok} de {v.N}");
        return (ok, total, head + sb.ToString());
    }

    /// <summary>Só a leitura inteligente (sem o leitor antigo), no formato da certidão.</summary>
    public static CertidaoData FromSmart(string text)
    {
        var r = SmartReader.Read(text);
        var d = new CertidaoData { Coordinates = r.Coordinates };
        if (!r.HasCoordinates) d.Courses.AddRange(r.Courses);
        return d;
    }

    [Fact]
    public void LeituraInteligente()
    {
        if (!Directory.Exists(Dir)) return;
        var (ok, total, report) = Run(FromSmart);
        if (Environment.GetEnvironmentVariable("GEOPOINT_OUT") is { Length: > 0 } outDir)
            File.WriteAllText(Path.Combine(outDir, "memoriais-smart.txt"), report);
        Assert.True(total > 0);
        // Texto sem OCR: todos os modelos; com OCR (inclusive foto e colunas em blocos): pelo menos 89% (era 47% na 1.8).
        Assert.DoesNotContain("[texto]", report);
        Assert.True(ok >= total * 0.89, report);
    }

    /// <summary>Depuração: GEOPOINT_DEBUG_CASE=005_table_EN.limpo.ocr (arquivo sem .txt) grava os números e papéis lidos.</summary>
    [Fact]
    public void Depurar()
    {
        if (Environment.GetEnvironmentVariable("GEOPOINT_DEBUG_CASE") is not { Length: > 0 } id) return;
        var outDir = Environment.GetEnvironmentVariable("GEOPOINT_OUT") ?? Path.GetTempPath();
        var sb = new StringBuilder();
        foreach (var one in id.Split(','))
        {
            var r = SmartReader.Read(File.ReadAllText(Path.Combine(Dir, one + ".txt")));
            sb.AppendLine($"===== {one}: {r.Model} | tabela={r.Table} | {string.Join("; ", r.Evidence)}");
            sb.AppendLine(r.Text);
            sb.AppendLine("--- números: " + string.Join("  ", r.Tokens.Select(t => t.ToString())));
            sb.AppendLine("--- vértices: " + string.Join("  ", r.Coordinates.Vertices.Select(v => $"{v.Name}({v.A};{v.B})")) + $" [{r.Coordinates.Kind}]");
            sb.AppendLine("--- trechos: " + string.Join("  ", r.Courses.Select(c => $"{c.AzimuthDeg:0.0000}/{c.Distance}")));
            sb.AppendLine("--- avisos: " + string.Join(" | ", r.Coordinates.Warnings.Concat(r.Warnings)) + $" | área {r.Area}");
        }
        File.WriteAllText(Path.Combine(outDir, "depurar.txt"), sb.ToString());
    }

    /// <summary>Depuração: GEOPOINT_DEBUG_TEXT=arquivo.txt grava a leitura da certidão e a conferência de cada trecho.</summary>
    [Fact]
    public void DepurarCertidao()
    {
        if (Environment.GetEnvironmentVariable("GEOPOINT_DEBUG_TEXT") is not { Length: > 0 } path) return;
        var outDir = Environment.GetEnvironmentVariable("GEOPOINT_OUT") ?? Path.GetTempPath();
        var d = CertidaoParser.Parse(File.ReadAllText(path));
        var sb = new StringBuilder();
        sb.AppendLine($"modelo: {d.Model}; área {d.Area}; perímetro {d.Perimetro}; fuso {d.UtmZone}");
        var c = d.Coordinates;
        foreach (var v in c.Vertices) sb.AppendLine($"  {v.Name}: {v.A} ; {v.B}  (arred. {v.Rounding}) [{v.Source}]");
        if (c.Vertices.Count >= 3)
        {
            var r = PerimeterText.Build(c.Vertices, c.Kind, c.Datum ?? Geodesy.DatumId.Sirgas2000, d.UtmZone ?? 23, d.South, true, c.StatedCourses);
            foreach (var sg in r.Segments)
                sb.AppendLine($"  {sg.From}->{sg.To}: calc {sg.Azimuth:0.0000}° {sg.Distance:0.0000} m | grid {sg.GridAzimuth:0.0000} {sg.GridDistance:0.0000} | texto {sg.Stated?.AzimuthDeg:0.0000} {sg.Stated?.Distance} " +
                              $"(passo {sg.Stated?.DistanceStep}) | dif {sg.AzimuthDiffDeg * 3600:0.0}\" {sg.DistanceDiff:0.0000} m | tol {sg.AzimuthTolDeg * 3600:0.0}\" {sg.DistanceTol:0.0000} m | {sg.Matches}");
            sb.AppendLine($"  área calc {r.Area:0.00}");
        }
        foreach (var w in d.Warnings.Concat(c.Warnings)) sb.AppendLine("  ! " + w);
        File.WriteAllText(Path.Combine(outDir, "depurar-certidao.txt"), sb.ToString());
    }

    [Fact]
    public void LeituraDosModelos()
    {
        if (!Directory.Exists(Dir)) return;
        var (ok, total, report) = Run(CertidaoParser.Parse);
        if (Environment.GetEnvironmentVariable("GEOPOINT_OUT") is { Length: > 0 } outDir)
            File.WriteAllText(Path.Combine(outDir, "memoriais-relatorio.txt"), report);
        Assert.True(total > 0);
        Assert.DoesNotContain("[texto]", report);
        Assert.True(ok >= total * 0.89, report);
    }
}
