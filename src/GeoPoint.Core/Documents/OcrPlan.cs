using System.Globalization;
using GeoPoint.Core.Calc;

namespace GeoPoint.Core.Documents;

/// <summary>Uma leitura de OCR do documento (com o giro da página usado).</summary>
public sealed class OcrCandidate
{
    public string Name { get; init; } = "";
    /// <summary>Giro da página em quartos de volta no sentido horário (0 = como veio).</summary>
    public int Turns { get; init; }
    public bool Flatten { get; init; }
    public bool SingleBlock { get; init; }
    public List<DocWord> Words { get; init; } = new();
    public string Text { get; init; } = "";
    public CertidaoData Data { get; init; } = new();
    public ReadingScore Score { get; init; } = new();
}

/// <summary>Resultado das tentativas de OCR: a melhor leitura, as tentativas (para o diagnóstico) e as dúvidas da dupla leitura.</summary>
public sealed class OcrOutcome
{
    public OcrCandidate? Best { get; set; }
    public List<OcrCandidate> All { get; } = new();
    public List<string> Attempts { get; } = new();
    /// <summary>Números lidos diferente na segunda leitura (pedem confirmação).</summary>
    public List<string> Doubts { get; } = new();
}

/// <summary>
/// Ordem das tentativas de OCR (a mesma no Revit e nos testes): a página como veio; se as palavras saem em pé ou nenhum perímetro é
/// encontrado, a página girada 90°, 270° e 180°; depois, na melhor orientação, com correção de sombra e em bloco único. Fica a
/// leitura de melhor nota. No fim, uma segunda leitura independente (outra configuração do OCR) confere os números da escolhida:
/// número lido diferente nas duas vira dúvida para você confirmar.
/// </summary>
public static class OcrPlan
{
    /// <param name="pages">Páginas para o OCR (índice da página e a imagem em cinza).</param>
    /// <param name="baseWords">Palavras das páginas que já têm texto (PDF misto).</param>
    /// <param name="ocr">O OCR de uma página: (imagem, índice da página, correção de sombra, bloco único) → palavras.</param>
    public static OcrOutcome Run(IReadOnlyList<(int Index, GrayImage Gray)> pages, IReadOnlyList<DocWord> baseWords,
        Func<GrayImage, int, bool, bool, List<DocWord>> ocr, Action<string>? progress = null)
    {
        var res = new OcrOutcome();
        var rotated = new Dictionary<(int Page, int Turns), GrayImage>();
        GrayImage Page(int k, int turns)
        {
            if (turns == 0) return pages[k].Gray;
            if (!rotated.TryGetValue((k, turns), out var g)) rotated[(k, turns)] = g = pages[k].Gray.Rotate(turns);
            return g;
        }
        OcrCandidate Consider(string name, int turns, bool flatten, bool single)
        {
            progress?.Invoke($"Lendo a imagem ({name})…");
            var words = new List<DocWord>(baseWords);
            for (int k = 0; k < pages.Count; k++) words.AddRange(ocr(Page(k, turns), pages[k].Index, flatten, single));
            string text = OcrCleanup.Normalize(DocumentText.BuildText(words));
            var d = CertidaoParser.Parse(text);
            var sc = ReadingScore.Of(d);
            var c = new OcrCandidate { Name = name, Turns = turns, Flatten = flatten, SingleBlock = single, Words = words, Text = text, Data = d, Score = sc };
            res.All.Add(c);
            res.Attempts.Add($"{name}: {sc.Summary} (nota {sc.Score.ToString("0", CultureInfo.InvariantCulture)})");
            if (res.Best is null || sc.Score > res.Best.Score.Score) res.Best = c;
            return c;
        }
        static string Suffix(int t) => t == 0 ? "" : $", página girada {t * 90}°";

        var first = Consider("Tesseract", 0, false, false);
        if (!first.Score.Complete && PageOrientation.ShouldTryRotations(first.Words.Skip(baseWords.Count).ToList(), first.Data))
            foreach (int t in PageOrientation.Turns)
                if (Consider($"Tesseract{Suffix(t)}", t, false, false).Score.Complete) break;
        int bt = res.Best!.Turns;
        if (!res.Best.Score.Complete) Consider($"Tesseract com correção de sombra{Suffix(bt)}", bt, true, false);
        if (!res.Best.Score.Complete) Consider($"Tesseract em bloco único{Suffix(bt)}", bt, false, true);

        // Dupla leitura: outra configuração do OCR na mesma orientação confere cada número da leitura escolhida.
        var best = res.Best!;
        var second = res.All.FirstOrDefault(c => c != best && c.Turns == best.Turns)
                     ?? Consider(best.SingleBlock ? $"Tesseract com correção de sombra{Suffix(best.Turns)}" : $"Tesseract em bloco único{Suffix(best.Turns)}",
                                 best.Turns, best.SingleBlock, !best.SingleBlock);
        if (res.Best != best)
        {
            // A segunda leitura saiu melhor que a primeira: ela vira a escolhida e a outra confere.
            (best, second) = (res.Best!, best);
        }
        res.Doubts.AddRange(ReadingAgreement.Compare(best.Data, second.Data));
        best.Data.Doubts.AddRange(res.Doubts);
        return res;
    }
}

/// <summary>Comparação de duas leituras do mesmo documento: o número que sai diferente em cada uma é duvidoso.</summary>
public static class ReadingAgreement
{
    private static readonly CultureInfo Pt = new("pt-BR");

    /// <summary>
    /// Números da leitura <paramref name="a"/> (a escolhida) que a leitura <paramref name="b"/> leu diferente. Só compara quando as
    /// duas leram a mesma estrutura (mesma quantidade de vértices ou de trechos): aí cada número tem o seu par.
    /// </summary>
    public static List<string> Compare(CertidaoData a, CertidaoData b)
    {
        var res = new List<string>();
        if (a.HasCoordinates && b.HasCoordinates && a.Coordinates.Kind == b.Coordinates.Kind && a.Coordinates.Vertices.Count == b.Coordinates.Vertices.Count)
        {
            bool geo = a.Coordinates.Kind == PerimeterCoordKind.Geographic;
            double tol = geo ? 1e-9 : 1e-6;
            for (int i = 0; i < a.Coordinates.Vertices.Count; i++)
            {
                var x = a.Coordinates.Vertices[i];
                var y = b.Coordinates.Vertices[i];
                if (Math.Abs(x.A - y.A) > tol || Math.Abs(x.B - y.B) > tol)
                    res.Add($"Vértice {x.Name}: lido {Coord(x, geo)} numa leitura do OCR e {Coord(y, geo)} na outra – confira no documento.");
            }
            CompareCourses(a.Coordinates.StatedCourses, b.Coordinates.StatedCourses, res);
        }
        else if (!a.HasCoordinates && !b.HasCoordinates && a.HasCourses && b.HasCourses)
            CompareCourses(a.Courses, b.Courses, res);
        return res;
    }

    private static void CompareCourses(IReadOnlyList<Course> a, IReadOnlyList<Course> b, List<string> res)
    {
        if (a.Count != b.Count) return;
        for (int i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b[i];
            bool azDiff = !(double.IsNaN(x.AzimuthDeg) && double.IsNaN(y.AzimuthDeg)) && !(PerimeterText.AngleDiff(x.AzimuthDeg, y.AzimuthDeg) < 1e-7);
            bool dDiff = !(double.IsNaN(x.Distance) && double.IsNaN(y.Distance)) && !(Math.Abs(x.Distance - y.Distance) < 1e-7);
            if (azDiff || dDiff)
                res.Add($"Trecho {i + 1}: lido {Course(x)} numa leitura do OCR e {Course(y)} na outra – confira no documento.");
        }
    }

    private static string Course(Course c) =>
        (double.IsNaN(c.AzimuthDeg) ? "—" : Geodesy.AngleText.FormatDms(c.AzimuthDeg, 0, Geodesy.AngleKind.Azimuth)) + " e " +
        (double.IsNaN(c.Distance) ? "—" : c.Distance.ToString("N2", Pt) + " m");

    private static string Coord(PerimeterVertex v, bool geo) => geo
        ? $"{Geodesy.AngleText.FormatDms(v.A, 3, Geodesy.AngleKind.Latitude)} / {Geodesy.AngleText.FormatDms(v.B, 3, Geodesy.AngleKind.Longitude)}"
        : $"{v.A.ToString("N2", Pt)} / {v.B.ToString("N2", Pt)}";
}
