namespace GeoPoint.Core.Documents;

/// <summary>
/// Página fotografada de lado ou de cabeça para baixo (foto encaminhada pelo celular perde a orientação): o OCR lê as palavras "em
/// pé" (caixas mais altas que largas) ou lê lixo. Aí vale ler de novo com a página girada e ficar com a melhor leitura.
/// </summary>
public static class PageOrientation
{
    /// <summary>Giros a tentar, em quartos de volta no sentido horário: 90°, 270° e 180°.</summary>
    public static readonly int[] Turns = { 1, 3, 2 };

    /// <summary>Fração das palavras (de 3 letras ou mais) que estão em pé – texto girado 90°.</summary>
    public static double VerticalShare(IEnumerable<DocWord> words)
    {
        var l = words.Where(w => w.Text.Trim().Length >= 3).ToList();
        if (l.Count == 0) return 0;
        return l.Count(w => (w.Y1 - w.Y0) > 1.3 * (w.X1 - w.X0)) / (double)l.Count;
    }

    /// <summary>Vale tentar a página girada: muitas palavras em pé, ou a leitura não achou perímetro nenhum.</summary>
    public static bool ShouldTryRotations(IReadOnlyList<DocWord> words, CertidaoData d) =>
        VerticalShare(words) >= 0.35 || (!d.HasCoordinates && !d.HasCourses);
}
