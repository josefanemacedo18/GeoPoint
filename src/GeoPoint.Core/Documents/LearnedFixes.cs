using System.Text.RegularExpressions;

namespace GeoPoint.Core.Documents;

/// <summary>Correção aprendida: um nome lido de um jeito (pelo OCR ou como o cartório escreve) que você corrigiu para outro.</summary>
public sealed class LearnedFix
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public int Uses { get; set; }
    public DateTime LastUsed { get; set; } = DateTime.Now;
}

/// <summary>
/// Aprende com as correções de confrontantes: quando você troca um nome lido por outro, a troca fica guardada (nas Configurações do
/// GeoPoint, no seu computador) e é aplicada sozinha na próxima leitura em que o mesmo nome aparecer. Números (coordenadas, azimutes,
/// distâncias) não são "aprendidos": cada documento tem os seus.
/// </summary>
public static class LearnedFixes
{
    public const int Max = 300;

    public static string Key(string s) => Regex.Replace(s.Trim(), @"\s+", " ").ToLowerInvariant();

    /// <summary>Guarda (ou atualiza) a correção; ignora trocas vazias ou iguais.</summary>
    public static bool Learn(List<LearnedFix> fixes, string from, string to)
    {
        from = from.Trim(); to = to.Trim();
        if (from.Length == 0 || to.Length == 0 || Key(from) == Key(to)) return false;
        var ex = fixes.FirstOrDefault(f => Key(f.From) == Key(from));
        if (ex is null) fixes.Add(new LearnedFix { From = from, To = to });
        else { ex.To = to; ex.LastUsed = DateTime.Now; }
        if (fixes.Count > Max)
            foreach (var old in fixes.OrderBy(f => f.LastUsed).Take(fixes.Count - Max).ToList()) fixes.Remove(old);
        return true;
    }

    /// <summary>Aplica as correções guardadas aos confrontantes lidos; devolve quantos foram trocados.</summary>
    public static int Apply(CertidaoData d, List<LearnedFix> fixes)
    {
        if (fixes.Count == 0) return 0;
        var map = fixes.GroupBy(f => Key(f.From)).ToDictionary(g => g.Key, g => g.Last());
        int n = 0;
        string Fix(string s)
        {
            if (s.Length == 0 || !map.TryGetValue(Key(s), out var f)) return s;
            f.Uses++;
            f.LastUsed = DateTime.Now;
            n++;
            return f.To;
        }
        for (int i = 0; i < d.Confrontantes.Count; i++) d.Confrontantes[i] = Fix(d.Confrontantes[i]);
        foreach (var t in d.Trechos) t.Confrontante = Fix(t.Confrontante);
        return n;
    }
}
