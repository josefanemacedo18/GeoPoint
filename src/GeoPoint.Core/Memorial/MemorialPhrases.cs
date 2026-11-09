using System.Text.Json;
using System.Text.RegularExpressions;
using GeoPoint.Core.Settings;

namespace GeoPoint.Core.Memorial;

/// <summary>
/// Frases-modelo do memorial (editáveis em JSON). Marcadores entre chaves são substituídos;
/// trechos entre colchetes [ ... ] são omitidos quando algum marcador interno ficar vazio.
/// </summary>
public sealed class MemorialPhrases
{
    public string Inicio { get; set; } = "Inicia-se a descrição deste perímetro no vértice {VERTICE}, de coordenadas {COORDENADAS}";
    public string Reta { get; set; } = "; deste, segue[ confrontando com {CONFRONTANTE},] com {DIRECAO} e distância de {DISTANCIA}, até o vértice {VERTICE_SEGUINTE}, de coordenadas {COORDENADAS_SEGUINTE}";
    public string RetaFinal { get; set; } = "; deste, segue[ confrontando com {CONFRONTANTE},] com {DIRECAO} e distância de {DISTANCIA}, até o vértice {VERTICE_SEGUINTE}, ponto inicial da descrição deste perímetro";
    public string Arco { get; set; } = "; deste, segue em curva circular[ confrontando com {CONFRONTANTE},] com raio de {RAIO}, ângulo central de {ANGULO_CENTRAL} e desenvolvimento de {DESENVOLVIMENTO} (corda de {CORDA}, com {DIRECAO}), até o vértice {VERTICE_SEGUINTE}, de coordenadas {COORDENADAS_SEGUINTE}";
    public string ArcoFinal { get; set; } = "; deste, segue em curva circular[ confrontando com {CONFRONTANTE},] com raio de {RAIO}, ângulo central de {ANGULO_CENTRAL} e desenvolvimento de {DESENVOLVIMENTO} (corda de {CORDA}, com {DIRECAO}), até o vértice {VERTICE_SEGUINTE}, ponto inicial da descrição deste perímetro";
    public string Fechamento { get; set; } = ", fechando assim o polígono acima descrito, com área de {AREA} e perímetro de {PERIMETRO}.";
    public string FuroInicio { get; set; } = "Excluída a área interna ({NOME_FURO}), com área de {AREA_FURO}, assim descrita: ";
    public string ConfrontanteVazio { get; set; } = "";

    public static string DefaultPath => Path.Combine(SettingsStore.DefaultFolder, "frases-memorial.json");

    public static MemorialPhrases Load(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                var p = JsonSerializer.Deserialize<MemorialPhrases>(File.ReadAllText(path), JsonOptions.Default);
                if (p is not null) return p.Normalize();
            }
        }
        catch { }
        return new MemorialPhrases();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions.Default));
    }

    private MemorialPhrases Normalize()
    {
        var d = new MemorialPhrases();
        Inicio = string.IsNullOrWhiteSpace(Inicio) ? d.Inicio : Inicio;
        Reta = string.IsNullOrWhiteSpace(Reta) ? d.Reta : Reta;
        RetaFinal = string.IsNullOrWhiteSpace(RetaFinal) ? d.RetaFinal : RetaFinal;
        Arco = string.IsNullOrWhiteSpace(Arco) ? d.Arco : Arco;
        ArcoFinal = string.IsNullOrWhiteSpace(ArcoFinal) ? d.ArcoFinal : ArcoFinal;
        Fechamento ??= d.Fechamento;
        FuroInicio ??= d.FuroInicio;
        ConfrontanteVazio ??= "";
        return this;
    }

    private static readonly Regex Optional = new(@"\[([^\[\]]*)\]", RegexOptions.CultureInvariant);
    private static readonly Regex Token = new(@"\{([A-Z0-9_]+)\}", RegexOptions.CultureInvariant);

    /// <summary>Aplica valores a uma frase-modelo.</summary>
    public static string Apply(string phrase, IReadOnlyDictionary<string, string> values)
    {
        string s = Optional.Replace(phrase, m =>
        {
            var inner = m.Groups[1].Value;
            foreach (Match t in Token.Matches(inner))
                if (!values.TryGetValue(t.Groups[1].Value, out var v) || string.IsNullOrWhiteSpace(v)) return "";
            return inner;
        });
        return Token.Replace(s, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }
}
