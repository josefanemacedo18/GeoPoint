using System.Globalization;
using System.Text.RegularExpressions;
using GeoPoint.Core.Calc;

namespace GeoPoint.Core.Documents;

/// <summary>
/// Precisão com que os números foram escritos no documento: a última casa escrita define o arredondamento (metade dela). Ex.:
/// "9.299.292,99" → passo 0,01 m (±0,005 m); "06°20'12.79"" → passo 0,01" (±0,005", cerca de ±15 cm no terreno);
/// "12,00 metros" → passo 0,01 m; "30 metros" → passo 1 m; "208°37'12"" → passo 1". As conferências do desenho usam isso como
/// tolerância: um documento só "não confere" quando a diferença passa do que o próprio arredondamento explica.
/// </summary>
public static class WrittenPrecision
{
    /// <summary>Metros por grau de latitude (média; a variação no Brasil é menor que 0,2%).</summary>
    public const double MetersPerDegree = 111_320;
    /// <summary>Passo padrão das distâncias quando o texto não mostra (centímetro).</summary>
    public const double DefaultDistanceStep = 0.01;
    /// <summary>Passo padrão dos azimutes quando o texto não mostra (1 segundo).</summary>
    public const double DefaultAzimuthStep = 1.0 / 3600;

    /// <summary>Passo da última casa de um número escrito ("12,00" → 0,01; "30" → 1; "9.299.292,99" → 0,01).</summary>
    public static double NumberStep(string raw, bool ptDoc = true)
    {
        var (_, _, dec) = SmartReader.ParseNumber(raw.Trim(), ptDoc);
        return Math.Pow(10, -Math.Max(0, dec));
    }

    private static readonly Regex AngleRx = new(
        @"(?<![\d.,])-?\s*(?:(?<dd>\d{1,3}[.,]\d+)\s*°|(?<d>\d{1,3})\s*°\s*(?:(?<m>\d{1,2}(?:[.,]\d+)?)\s*['’′]\s*(?:(?<s>\d{1,2}(?:[.,]\d+)?)\s*(?:""|”|″|'')?)?)?)", RegexOptions.CultureInvariant);

    /// <summary>Passo da última casa de um ângulo escrito, em graus ("208°37'12"" → 1/3600; "45°30'" → 1/60; "120,6839°" → 0,0001).</summary>
    public static double AngleStep(string raw)
    {
        var m = AngleRx.Match(raw ?? "");
        if (!m.Success) return DefaultAzimuthStep;
        static int Dec(string v) { int k = v.IndexOfAny(new[] { ',', '.' }); return k < 0 ? 0 : v.Length - k - 1; }
        if (m.Groups["dd"].Success) return Math.Pow(10, -Dec(m.Groups["dd"].Value));
        if (m.Groups["s"].Success) return Math.Pow(10, -Dec(m.Groups["s"].Value)) / 3600;
        if (m.Groups["m"].Success) return Math.Pow(10, -Dec(m.Groups["m"].Value)) / 60;
        return m.Groups["d"].Success ? 1 : DefaultAzimuthStep;
    }

    /// <summary>Passo do primeiro número de um trecho de texto ("distância de 12,00 m" → 0,01); null = nenhum número.</summary>
    public static double? FirstNumberStep(string text)
    {
        var m = Regex.Match(text ?? "", @"(?<![\d.,])\d{1,3}(?:\.\d{3})+(?:,\d+)?|\d+(?:[.,]\d+)?");
        return m.Success ? NumberStep(m.Value) : null;
    }

    /// <summary>Arredondamento (m) de um vértice geográfico: metade do passo de cada coordenada convertido em metros.</summary>
    public static double GeoRounding(double latDeg, double latStepDeg, double lonStepDeg) =>
        Math.Max(latStepDeg / 2 * MetersPerDegree, lonStepDeg / 2 * MetersPerDegree * Math.Cos(latDeg * Math.PI / 180));

    /// <summary>
    /// Arredondamento suposto quando não se sabe como as coordenadas foram escritas (digitadas na tabela): o de um documento
    /// cuidadoso – UTM ao centímetro, locais ao milímetro, geográficas com 3 casas nos segundos. Supor fino é o lado seguro: no
    /// máximo o desenho pede confirmação à toa; nunca aceita um erro.
    /// </summary>
    public static double DefaultRounding(PerimeterCoordKind kind) => kind switch
    {
        PerimeterCoordKind.Geographic => 0.0005 / 3600 * MetersPerDegree,
        PerimeterCoordKind.Local => 0.0005,
        _ => 0.005,
    };

    /// <summary>Arredondamento de um vértice (o escrito, ou o suposto para o sistema).</summary>
    public static double RoundingOf(PerimeterVertex v, PerimeterCoordKind kind) => v.Rounding is double r && r > 0 ? r : DefaultRounding(kind);

    public static double DistanceStepOf(Course c) => c.DistanceStep is double s && s > 0 ? s : DefaultDistanceStep;
    public static double AzimuthStepOf(Course c) => c.AzimuthStep is double s && s > 0 ? s : DefaultAzimuthStep;

    /// <summary>
    /// Maior erro de posição (m) que o arredondamento do trecho pode causar: metade do passo da distância mais o deslocamento
    /// lateral de meio passo do azimute.
    /// </summary>
    public static double CourseError(Course c) =>
        DistanceStepOf(c) / 2 + Math.Abs(c.Distance) * Math.Sin(AzimuthStepOf(c) / 2 * Math.PI / 180);

    /// <summary>Texto curto da resolução ("±0,5 cm", "±15 cm").</summary>
    public static string Describe(double meters) =>
        meters < 0.01 ? $"±{(meters * 1000).ToString("0.#", new CultureInfo("pt-BR"))} mm" : $"±{(meters * 100).ToString("0.#", new CultureInfo("pt-BR"))} cm";
}
