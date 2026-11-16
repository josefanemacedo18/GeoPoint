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
        /// <summary>"courses": o desenho certo é pelos trechos (coordenadas grossas no documento).</summary>
        public string? draw { get; set; }
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

    // ------------------------------------------------------------------ erro silencioso (o requisito principal)

    /// <summary>Resultado do desenho de uma leitura: o polígono que o GeoPoint desenharia, o veredito e o primeiro vértice.</summary>
    public sealed record Drawn(List<GeoPoint.Core.Geometry.Vec2> Pts, DrawingVerdict Verdict, PerimeterVertex? First);

    /// <summary>O que o GeoPoint desenharia com esta leitura (mesma lógica da janela) e o veredito da conferência.</summary>
    public static Drawn Plugin(CertidaoData d, int zone, IReadOnlyList<string>? doubts = null)
    {
        int fixes = d.Fixes.Count(f => !f.Derived), derived = d.Fixes.Count(f => f.Derived);
        if (d.HasCoordinates)
        {
            var c = d.Coordinates;
            var res = PerimeterText.Build(c.Vertices, c.Kind, c.Datum ?? Geodesy.DatumId.Sirgas2000, zone, true, true, c.StatedCourses);
            if (DrawingCheck.CoursesMorePrecise(c.Vertices, c.Kind, c.StatedCourses))
            {
                var v = DrawingCheck.ForCoursesWithCoordinates(res, c.Vertices, c.Kind, c.StatedCourses, d.Figures, fixes + derived, doubts);
                return new Drawn(DrawingCheck.TraversePoints(DrawingCheck.ToTrueNorth(c.StatedCourses, res)), v, c.Vertices[0]);
            }
            return new Drawn(res.Points.ToList(), DrawingCheck.ForCoordinates(res, c.Vertices, c.Kind, d.Figures, fixes + derived, doubts), c.Vertices[0]);
        }
        if (d.HasCourses)
            return new Drawn(DrawingCheck.TraversePoints(d.Courses), DrawingCheck.ForCourses(d.Courses, d.Figures, fixes, doubts, d.FromSides, derived), null);
        return new Drawn(new List<GeoPoint.Core.Geometry.Vec2>(), new DrawingVerdict { Level = VerdictLevel.SemConferencia }, null);
    }

    /// <summary>O polígono certo (pelos trechos ou pelas coordenadas verdadeiras) e a tolerância do desenho.</summary>
    public static (List<GeoPoint.Core.Geometry.Vec2> Pts, double Tol) TruthPolygon(Truth t)
    {
        if (t.vertices.Count == 0 || t.draw == "courses")
            return (DrawingCheck.TraversePoints(t.courses.Select(c => new Course { AzimuthDeg = c.az, Distance = c.dist }).ToList()), 0.03);
        var kind = t.kind switch { "geo" => PerimeterCoordKind.Geographic, "local" => PerimeterCoordKind.Local, _ => PerimeterCoordKind.Utm };
        var vs = t.vertices.Select(v => new PerimeterVertex { Name = v.name, A = v.a, B = v.b }).ToList();
        var r = PerimeterText.Build(vs, kind, Geodesy.DatumId.Sirgas2000, t.zone ?? 23, true, true);
        return (r.Points.ToList(), kind == PerimeterCoordKind.Geographic ? 0.05 : 0.03);
    }

    /// <summary>O desenho bate com o certo (forma, medidas e – com coordenadas – a posição do primeiro vértice)?</summary>
    public static string DrawingError(Truth t, Drawn dr)
    {
        var (truth, tol) = TruthPolygon(t);
        if (dr.Pts.Count != truth.Count) return $"desenho com {dr.Pts.Count} vértices (certo: {truth.Count})";
        double worst = dr.Pts.Zip(truth).Max(p => p.First.DistanceTo(p.Second));
        if (worst > tol) return $"desenho até {worst:0.000} m fora do certo";
        if (t.vertices.Count > 0 && dr.First is { } f)
        {
            bool geo = t.kind == "geo";
            double da = Math.Abs(f.A - t.vertices[0].a), db = Math.Abs(f.B - t.vertices[0].b);
            if (geo ? Math.Max(da, db) > 2e-7 : Math.Max(da, db) > 0.0015) return "posição do primeiro vértice errada";
        }
        return "";
    }

    [Fact]
    public void ErroSilenciosoZero()
    {
        if (!Directory.Exists(Dir)) return;
        var sb = new StringBuilder();
        int okConf = 0, okAsk = 0, badAsk = 0, silent = 0, total = 0;
        var detail = new StringBuilder();
        foreach (var (id, t) in Cases())
            foreach (var variant in new[] { "texto", "limpo", "scan", "foto", "limpo-blocos", "scan-blocos", "foto-blocos" })
            {
                string file = Path.Combine(Dir, variant == "texto" ? id + ".txt"
                    : variant.EndsWith("-blocos") ? $"{id}.{variant[..^7]}.ocr.txt" : $"{id}.{variant}.linhas.txt");
                if (!File.Exists(file)) continue;
                total++;
                var d = CertidaoParser.Parse(File.ReadAllText(file));
                var dr = Plugin(d, t.zone ?? 23, d.Doubts);
                string err = DrawingError(t, dr);
                bool conf = dr.Verdict.Level == VerdictLevel.Conferido;
                detail.AppendLine($"{id,-22} {variant,-13} {dr.Verdict.Level,-15} {(err.Length == 0 ? "certo" : "ERRADO: " + err)}");
                if (err.Length == 0 && conf) okConf++;
                else if (err.Length == 0) okAsk++;
                else if (!conf) badAsk++;
                else
                {
                    silent++;
                    sb.AppendLine($"✗✗ ERRO SILENCIOSO {id} [{variant}]: {err}");
                    foreach (var it in dr.Verdict.Items) sb.AppendLine($"     {it.Icon} {it.Name}: {it.Detail}");
                    if (d.HasCoordinates)
                    {
                        sb.AppendLine("     vértices: " + string.Join(" | ", d.Coordinates.Vertices.Select(v => $"{v.Name} {v.A:0.0000000} {v.B:0.0000000} r={v.Rounding:0.000}")));
                        sb.AppendLine("     trechos: " + string.Join(" | ", d.Coordinates.StatedCourses.Select(c => $"{c.AzimuthDeg:0.0000} {c.Distance}")));
                    }
                    else sb.AppendLine("     trechos: " + string.Join(" | ", d.Courses.Select(c => $"{c.AzimuthDeg:0.0000} {c.Distance}")));
                    sb.AppendLine("     certo: " + string.Join(" | ", t.courses.Select(c => $"{c.az:0.0000} {c.dist}")));
                }
            }
        string head = $"Leituras: {total}\n  certas e conferidas (desenha direto): {okConf}\n  certas, mas pede confirmação: {okAsk}\n" +
                      $"  erradas e avisadas (pede confirmação): {badAsk}\n  ERRADAS SEM AVISO: {silent}\n";
        if (Environment.GetEnvironmentVariable("GEOPOINT_OUT") is { Length: > 0 } outDir)
            File.WriteAllText(Path.Combine(outDir, "memoriais-silencioso.txt"), head + sb + "\n" + detail);
        Assert.True(total > 0);
        Assert.True(silent == 0, head + sb);
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
