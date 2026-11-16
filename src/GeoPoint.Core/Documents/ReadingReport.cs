using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GeoPoint.Core.Calc;

namespace GeoPoint.Core.Documents;

/// <summary>Proteção de dados: mascara CPF, CNPJ, RG/CNH, e-mail, telefone e nomes de pessoas em relatórios e registros.</summary>
public static class Privacy
{
    private static readonly Regex Cpf = new(@"\b\d{3}\.?\s?\d{3}\.?\s?\d{3}\s?[-/.]?\s?\d{2}\b");
    private static readonly Regex Cnpj = new(@"\b\d{2}\.?\d{3}\.?\d{3}\s?/\s?\d{4}\s?-?\s?\d{2}\b");
    private static readonly Regex Email = new(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+");
    private static readonly Regex Phone = new(@"\(?\b\d{2}\)?\s?9?\d{4}[-\s]?\d{4}\b");
    private static readonly Regex DocNumber = new(@"\b(?<k>RG|CNH|C\.?I\.?\s?RG|CI|CFT|CREA|CAU|CRT)\b\s*(?:n[ºo°.]?\s*:?\s*)?[\w./-]*\d[\w./-]*", RegexOptions.IgnoreCase);
    /// <summary>Palavras que não são nomes de pessoas (ficam no relatório).</summary>
    private static readonly HashSet<string> Keep = new(StringComparer.OrdinalIgnoreCase)
    {
        "rua", "avenida", "av", "travessa", "estrada", "rodovia", "alameda", "praça", "lote", "lotes", "quadra", "qd", "residencial", "loteamento",
        "jardim", "conjunto", "setor", "bairro", "área", "area", "pública", "publica", "municipal", "remanescente", "frente", "fundos",
        "lado", "direito", "esquerdo", "vértice", "vertice", "ponto", "marco", "sirgas", "utm", "datum", "fuso", "norte", "sul", "leste",
        "oeste", "memorial", "descritivo", "matrícula", "matricula", "certidão", "certidao", "inteiro", "teor", "ltda", "me", "eireli",
        "empreendimentos", "imóvel", "imovel", "urbano", "rural", "confrontando", "com", "azimute", "distância", "distancia", "coordenadas",
        "longitude", "latitude", "e", "de", "da", "do", "das", "dos",
    };

    public static string Mask(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string s = Cnpj.Replace(text, "**.***.***/****-**");
        s = Cpf.Replace(s, "***.***.***-**");
        s = Email.Replace(s, "[e-mail]");
        s = DocNumber.Replace(s, m => m.Groups["k"].Value + " [nº]");
        s = Phone.Replace(s, "[telefone]");
        return s;
    }

    /// <summary>
    /// Também troca nomes de pessoas por "[nome]": duas ou mais palavras seguidas com inicial maiúscula que não sejam de endereço,
    /// lote ou descrição ("Lote 09 – João dos Santos" → "Lote 09 – [nome]").
    /// </summary>
    public static string MaskNames(string? text)
    {
        string s = Mask(text);
        return Regex.Replace(s, @"\b\p{Lu}[\p{L}'.]+(?:\s+(?:d[aeo]s?\s+|e\s+)?\p{Lu}[\p{L}'.]+)+", m =>
        {
            var words = m.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var kept = words.Where(w => Keep.Contains(w.Trim('.', ','))).ToList();
            if (kept.Count == words.Length) return m.Value;
            // Palavras de endereço no começo ficam ("Rua [nome]"); o resto vira [nome].
            var lead = new List<string>();
            foreach (var w in words) { if (Keep.Contains(w.Trim('.', ','))) lead.Add(w); else break; }
            while (lead.Count > 0 && Regex.IsMatch(lead[^1], @"^(?:d[aeo]s?|e)$", RegexOptions.IgnoreCase)) lead.RemoveAt(lead.Count - 1);
            return (lead.Count > 0 ? string.Join(" ", lead) + " " : "") + "[nome]";
        });
    }
}

public enum CheckStatus { Ok, Aviso, Falha }

/// <summary>Uma etapa da leitura ou um item da conferência.</summary>
public sealed record CheckItem(string Name, CheckStatus Status, string Detail)
{
    public string Icon => Status switch { CheckStatus.Ok => "✓", CheckStatus.Aviso => "⚠", _ => "✗" };
}

/// <summary>Divisa do perímetro como está na tabela (o usuário pode ter corrigido).</summary>
public sealed record Divisa(string From, string To, double Length, string Confrontante, string Lado);

/// <summary>Diagnóstico da leitura por etapa e conferência de consistência do perímetro lido.</summary>
public static class ReadingReport
{
    private static string N(double v, int d = 2) => v.ToString("N" + d, CultureInfo.GetCultureInfo("pt-BR"));

    /// <summary>
    /// Etapas: foto → texto → descrição → vértices → trechos → confrontantes. A primeira que falha é onde a leitura parou.
    /// </summary>
    public static List<CheckItem> Steps(CertidaoData d, int textLength, string engine, ImageQualityReport? quality)
    {
        var res = new List<CheckItem>();
        if (quality is not null)
            res.Add(new("Foto", quality.Good ? CheckStatus.Ok : CheckStatus.Aviso,
                quality.Good ? $"boa ({quality.ShortSide} px no lado menor)" : quality.Summary));
        res.Add(new("Texto", textLength >= 200 ? CheckStatus.Ok : textLength > 0 ? CheckStatus.Aviso : CheckStatus.Falha,
            textLength == 0 ? "nenhum texto reconhecido" : $"{textLength} caracteres ({(engine.Length > 0 ? engine : "texto do próprio PDF")})"));
        res.Add(new("Descrição do perímetro", d.Description.Length > 0 ? CheckStatus.Ok : CheckStatus.Aviso,
            d.Description.Length > 0 ? "encontrada" : "início da descrição não encontrado: o texto todo foi usado"));
        var c = d.Coordinates;
        if (d.HasCoordinates)
        {
            string kind = c.Kind switch { PerimeterCoordKind.Geographic => "geográficas", PerimeterCoordKind.Local => "locais", _ => "UTM" };
            res.Add(new("Vértices", CheckStatus.Ok, $"{c.Vertices.Count} com coordenadas {kind}" +
                (c.Datum is null ? ", datum não informado" : "") + (d.UtmZone is int z ? $", fuso {z}" : "") +
                (c.Warnings.Count > 0 ? " – " + string.Join(" ", c.Warnings.Take(2)) : "")));
        }
        else if (d.HasCourses)
            res.Add(new("Vértices", CheckStatus.Aviso, d.FromSides ? "sem coordenadas: só as medidas dos lados" : "sem coordenadas: desenho pelos azimutes e distâncias"));
        else res.Add(new("Vértices", CheckStatus.Falha, "nenhuma coordenada nem azimute/distância encontrados"));
        int total = d.HasCoordinates ? c.Vertices.Count : d.Trechos.Count;
        int pairs = d.Trechos.Count(t => t.Azimuth is not null && t.Distance is not null);
        int dist = d.Trechos.Count(t => t.Distance is not null);
        if (d.Trechos.Count > 0 || d.HasCourses)
            res.Add(new("Trechos", pairs >= total && total > 0 ? CheckStatus.Ok : dist > 0 || d.HasCourses ? CheckStatus.Aviso : CheckStatus.Falha,
                d.FromSides ? $"{d.Courses.Count} lados" : $"{d.Trechos.Count} trecho(s): {pairs} com azimute e distância, {dist} com distância"));
        int confs = d.Confrontantes.Count(x => x.Length > 0);
        int need = Math.Max(d.Confrontantes.Count, total);
        res.Add(new("Confrontantes", need > 0 && confs >= need ? CheckStatus.Ok : confs > 0 ? CheckStatus.Aviso : CheckStatus.Falha,
            need == 0 ? "nenhuma divisa" : $"{confs} de {need} divisa(s)" + (d.Trechos.Any(t => t.Inherited) ? $" ({d.Trechos.Count(t => t.Inherited)} herdado(s) do trecho anterior)" : "") +
            (d.Confrontacoes.Count > 0 ? $"; quadro de confrontações com {d.Confrontacoes.Count} linha(s)" : "")));
        return res;
    }

    /// <summary>
    /// Conferência de consistência do perímetro (como está na tabela): fechamento e precisão, trechos × texto, área e perímetro ×
    /// documento, medidas por divisa × quadro de confrontações, confrontante em todas as divisas, fuso/datum.
    /// </summary>
    /// <param name="misclosure">Erro de fechamento (m) – desenho por azimutes; null = por coordenadas (fecha por construção).</param>
    public static List<CheckItem> Consistency(CertidaoData? d, IReadOnlyList<Divisa> divisas, double area, double perimeter,
        double? misclosure, int checkedOk, int checkedTotal, bool zoneAssumed, bool datumAssumed)
    {
        var res = new List<CheckItem>();
        if (misclosure is double mc)
        {
            double prec = mc < 1e-9 ? double.PositiveInfinity : perimeter / mc;
            var st = prec >= 5000 ? CheckStatus.Ok : prec >= 1000 ? CheckStatus.Aviso : CheckStatus.Falha;
            res.Add(new("Fechamento", st, double.IsInfinity(prec) ? "fecha exatamente" : $"erro de {N(mc, 3)} m (precisão 1:{N(prec, 0)})" +
                (st == CheckStatus.Ok ? "" : st == CheckStatus.Aviso ? " – abaixo de 1:5000, confira os trechos" : " – abaixo de 1:1000: algum azimute ou distância está errado")));
        }
        else res.Add(new("Fechamento", CheckStatus.Ok, "pelas coordenadas (o polígono fecha no primeiro vértice)"));
        if (checkedTotal > 0)
            res.Add(new("Trechos × texto", checkedOk == checkedTotal ? CheckStatus.Ok : CheckStatus.Falha,
                checkedOk == checkedTotal ? $"os {checkedTotal} trechos conferem" : $"{checkedTotal - checkedOk} de {checkedTotal} trecho(s) não conferem (em vermelho)"));
        if (d?.Area is double da && da > 0 && area > 0)
        {
            double pct = Math.Abs(area - da) / da * 100;
            // "600.000 m²" pode ser 600,000 (ponto no lugar da vírgula).
            bool thousand = pct > 50 && Math.Abs(area - da / 1000) / (da / 1000) < 0.01;
            res.Add(new("Área", pct <= 1 ? CheckStatus.Ok : pct <= 3 ? CheckStatus.Aviso : CheckStatus.Falha,
                thousand ? $"documento {N(da)} m² – provavelmente {N(da / 1000)} m² (ponto no lugar da vírgula); desenho {N(area)} m²"
                    : $"documento {N(da)} m², desenho {N(area)} m² (diferença {N(pct, 1)}%)"));
        }
        if (d?.Perimetro is double dp && dp > 0 && perimeter > 0)
        {
            double pct = Math.Abs(perimeter - dp) / dp * 100;
            res.Add(new("Perímetro", pct <= 0.5 ? CheckStatus.Ok : pct <= 2 ? CheckStatus.Aviso : CheckStatus.Falha,
                $"documento {N(dp)} m, desenho {N(perimeter)} m" + (d.PerimetroOutro is double po ? $" (o documento também diz {N(po)} m em outro lugar – provável erro de leitura)" : "")));
        }
        // Medidas por divisa × quadro de confrontações ("Frente - Rua 07 - 24,00 m" = 12,00 + 12,00).
        if (d is not null && d.Confrontacoes.Count > 0 && divisas.Count > 0)
        {
            var bad = new List<string>();
            int okN = 0;
            foreach (var g in d.Confrontacoes.GroupBy(q => q.Lado.Length > 0 ? q.Lado : q.Confrontante))
            {
                double stated = g.Sum(q => q.Medida);
                string lado = g.First().Lado;
                var mine = lado.Length > 0 ? divisas.Where(x => x.Lado == lado).ToList()
                    : divisas.Where(x => CertidaoParser.BestConfrontacao(g, x.Confrontante) is not null).ToList();
                if (mine.Count == 0) { bad.Add($"{g.Key}: nenhuma divisa na tabela"); continue; }
                double sum = mine.Sum(x => x.Length);
                if (Math.Abs(sum - stated) <= Math.Max(0.10, stated * 0.005)) okN++;
                else bad.Add($"{g.Key}: quadro {N(stated)} m, desenho {N(sum)} m");
            }
            res.Add(new("Quadro de confrontações", bad.Count == 0 ? CheckStatus.Ok : CheckStatus.Aviso,
                bad.Count == 0 ? $"as {okN} divisas do quadro conferem com as medidas" : string.Join("; ", bad)));
        }
        int empty = divisas.Count(x => x.Confrontante.Trim().Length == 0);
        if (divisas.Count > 0)
            res.Add(new("Confrontantes", empty == 0 ? CheckStatus.Ok : CheckStatus.Aviso,
                empty == 0 ? $"as {divisas.Count} divisas têm confrontante" : $"{empty} divisa(s) sem confrontante: preencha na tabela"));
        if (zoneAssumed || datumAssumed)
            res.Add(new("Sistema", CheckStatus.Aviso, $"o documento não informa {(zoneAssumed && datumAssumed ? "o fuso nem o datum" : zoneAssumed ? "o fuso" : "o datum")}: " +
                "foi usado o do projeto (SIRGAS 2000) – confira se o imóvel é da região"));
        return res;
    }

    /// <summary>
    /// Documentos de conferência (importados junto com o principal, ex.: as matrículas de uma fusão): soma das áreas × desenho e
    /// vértices em comum com o desenho.
    /// </summary>
    public static List<CheckItem> CompareDocs(CertidaoData main, double drawnArea, IReadOnlyList<(string Name, CertidaoData D)> others)
    {
        var res = new List<CheckItem>();
        if (others.Count == 0) return res;
        var areas = others.Where(o => o.D.Area is > 0).ToList();
        if (areas.Count > 0)
        {
            double sum = areas.Sum(o => o.D.Area!.Value);
            double refA = drawnArea > 0 ? drawnArea : main.Area ?? 0;
            double pct = refA > 0 ? Math.Abs(sum - refA) / refA * 100 : 0;
            res.Add(new("Soma das áreas", refA <= 0 ? CheckStatus.Aviso : pct <= 1 ? CheckStatus.Ok : pct <= 3 ? CheckStatus.Aviso : CheckStatus.Falha,
                $"{string.Join(" + ", areas.Select(o => N(o.D.Area!.Value)))} = {N(sum)} m² nos outros documentos" +
                (refA > 0 ? $"; desenho {N(refA)} m² (diferença {N(pct, 1)}%)" : "")));
        }
        foreach (var (name, d) in others)
        {
            if (!d.HasCoordinates || !main.HasCoordinates || d.Coordinates.Kind != main.Coordinates.Kind) continue;
            double tol = d.Coordinates.Kind == PerimeterCoordKind.Geographic ? 2e-6 : 0.05;
            int common = d.Coordinates.Vertices.Count(v => main.Coordinates.Vertices.Any(m => Math.Abs(m.A - v.A) <= tol && Math.Abs(m.B - v.B) <= tol));
            res.Add(new($"Vértices em comum – {name}", common >= 2 ? CheckStatus.Ok : CheckStatus.Aviso,
                common == 0 ? "nenhum vértice coincide com o desenho" : $"{common} vértice(s) coincidem com o desenho (divisa comum)"));
        }
        return res;
    }

    /// <summary>Relatório de texto para copiar e enviar (sem a foto, com CPF, documentos e nomes mascarados).</summary>
    public static string Text(string fileName, IEnumerable<CheckItem> steps, IEnumerable<CheckItem> checks, IEnumerable<string> attempts, CertidaoData? d)
    {
        var sb = new StringBuilder();
        sb.AppendLine("GeoPoint – diagnóstico da leitura");
        sb.AppendLine("Arquivo: " + Path.GetExtension(fileName).ToLowerInvariant() + " (nome omitido)");
        sb.AppendLine();
        sb.AppendLine("Etapas:");
        foreach (var s in steps) sb.AppendLine($"  {s.Icon} {s.Name}: {Privacy.MaskNames(s.Detail)}");
        var ch = checks.ToList();
        if (ch.Count > 0)
        {
            sb.AppendLine("Conferência:");
            foreach (var s in ch) sb.AppendLine($"  {s.Icon} {s.Name}: {Privacy.MaskNames(s.Detail)}");
        }
        var at = attempts.ToList();
        if (at.Count > 0)
        {
            sb.AppendLine("Tentativas de OCR:");
            foreach (var a in at) sb.AppendLine("  " + Privacy.MaskNames(a));
        }
        if (d is not null)
        {
            sb.AppendLine("Trechos lidos:");
            foreach (var t in d.Trechos)
                sb.AppendLine($"  {t.From} → {t.To}: azimute {(t.Azimuth is double az ? az.ToString("0.0000", CultureInfo.InvariantCulture) : "—")}, " +
                              $"distância {(t.Distance is double di ? di.ToString("0.00", CultureInfo.InvariantCulture) : "—")}, divisa {(t.Lado.Length > 0 ? t.Lado : "—")}, " +
                              $"confrontante {(t.Confrontante.Length > 0 ? Privacy.MaskNames(t.Confrontante) : "—")}{(t.Inherited ? " (herdado)" : "")}");
            if (d.Description.Length > 0)
            {
                sb.AppendLine("Descrição (dados pessoais mascarados):");
                sb.AppendLine("  " + Privacy.MaskNames(d.Description));
            }
        }
        return sb.ToString();
    }
}
