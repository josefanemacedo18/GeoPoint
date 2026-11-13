using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;

namespace GeoPoint.Core.Sheets;

/// <summary>Símbolos das convenções topográficas.</summary>
public enum LegendKind { AreaImovel, AreaEdificada, Limite, Vertice, PontoReferencia, MudancaConfrontante, MeioFio, Calcada, Remanescente }

public readonly record struct LegendItem(LegendKind Kind, string Text, int ColorIndex = 0);

/// <summary>Campo "Rótulo: valor" de um bloco do carimbo.</summary>
public readonly record struct SheetField(string Label, string Value);

/// <summary>Conteúdo dos blocos do carimbo para uma prancha (independente do Revit).</summary>
public sealed class SheetContent
{
    public List<SheetField> Identificacao { get; } = new();
    public List<SheetField> Registro { get; } = new();
    public List<SheetField> Quadro { get; } = new();
    public List<LegendItem> Legenda { get; } = new();
    public List<string> Orientacao { get; } = new();
    public string Titulo { get; set; } = "";
    public string Subtitulo { get; set; } = "";
    /// <summary>Assinaturas: (nome, linha 2, linha 3).</summary>
    public List<string[]> Assinaturas { get; } = new();
    /// <summary>Pendências encontradas (campo vazio, área diferente da matrícula...).</summary>
    public List<string> Pendencias { get; } = new();

    public static SheetContent Build(ProjectContext ctx, ValueFormatter fmt, SheetSpec spec, IReadOnlyList<LotRecord> lots, CrsDef tableCrs,
        bool vias = false, bool edificacoes = false)
    {
        var c = new SheetContent { Titulo = spec.Title, Subtitulo = spec.Subtitle };
        var p = ctx.Project;
        var info = p.Info;
        var tech = ctx.Settings.Technician;
        var op = p.Operations.FirstOrDefault(o => o.Id == spec.OperationId);

        string Distinct(Func<LotRecord, string> f, string fallback)
        {
            var vals = lots.Select(f).Select(x => string.IsNullOrWhiteSpace(x) ? fallback : x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return string.Join("; ", vals);
        }
        string owners = Distinct(l => l.Proprietario, info.Proprietario);
        string docs = Distinct(l => l.Documento, info.Documento);
        string matr = Distinct(l => l.Matricula, info.Matricula);

        c.Identificacao.Add(new("Proprietário", owners));
        c.Identificacao.Add(new("CPF/CNPJ", docs));
        c.Identificacao.Add(new("Matrícula", matr));
        if (!string.IsNullOrWhiteSpace(info.InscricaoMunicipal)) c.Identificacao.Add(new("Inscrição municipal", info.InscricaoMunicipal));
        if (string.Equals(info.TipoImovel, "Rural", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(info.Ccir)) c.Identificacao.Add(new("CCIR", info.Ccir));
            if (!string.IsNullOrWhiteSpace(info.Car)) c.Identificacao.Add(new("CAR", info.Car));
        }

        string procedimento = op is null ? (string.IsNullOrWhiteSpace(p.Situacao) ? info.Situacao : p.Situacao)
            : op.Type switch { OperationType.Desmembramento => "Desmembramento", OperationType.Remembramento => "Remembramento", _ => "Parcelamento" };
        c.Registro.Add(new("Procedimento", procedimento));
        string transcricao = Transcricao(p, op, info);
        if (transcricao.Length > 0) c.Registro.Add(new("Transcrição", transcricao));
        if (!string.IsNullOrWhiteSpace(info.Cartorio)) c.Registro.Add(new("Cartório", info.Cartorio));
        c.Registro.Add(new("Município", string.Join("/", new[] { info.Municipio, info.UF }.Where(x => !string.IsNullOrWhiteSpace(x))).ToUpperInvariant()));
        string endereco = lots.Select(l => l.Endereco).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? info.Endereco;
        if (!string.IsNullOrWhiteSpace(info.Bairro) && !endereco.Contains(info.Bairro, StringComparison.OrdinalIgnoreCase))
            endereco = string.Join(", ", new[] { endereco, info.Bairro }.Where(x => !string.IsNullOrWhiteSpace(x)));
        c.Registro.Add(new("Endereço", endereco.ToUpperInvariant()));
        c.Registro.Add(new("CEP", info.Cep));

        // Quadro de áreas e perímetros.
        var reps = new List<LotReport>();
        foreach (var l in lots) { try { reps.Add(ctx.Report(l)); } catch { } }
        if (reps.Count == 1)
        {
            c.Quadro.Add(new("ÁREA", fmt.Area(reps[0].AreaLocal)));
            c.Quadro.Add(new("PERÍMETRO", fmt.Distance(reps[0].PerimeterLocal)));
        }
        else if (reps.Count > 1)
        {
            c.Quadro.Add(new("LOTES", reps.Count.ToString()));
            c.Quadro.Add(new("ÁREA TOTAL", fmt.Area(reps.Sum(r => r.AreaLocal))));
        }

        // Convenções (só o que aparece no desenho).
        bool hasRem = lots.Any(l => l.IsRemanescente);
        c.Legenda.Add(new(LegendKind.AreaImovel, lots.Count > 1 ? "ÁREA DOS LOTES" : "ÁREA DO IMÓVEL", spec.Colors.TryGetValue(lots.FirstOrDefault()?.Id ?? "", out int ci) ? ci : 0));
        if (hasRem && spec.Hatch) c.Legenda.Add(new(LegendKind.Remanescente, "LOTE REMANESCENTE"));
        if (edificacoes) c.Legenda.Add(new(LegendKind.AreaEdificada, "ÁREA EDIFICADA"));
        c.Legenda.Add(new(LegendKind.Limite, "LIMITE DO IMÓVEL"));
        if (spec.Detailed) c.Legenda.Add(new(LegendKind.Vertice, "VÉRTICES"));
        c.Legenda.Add(new(LegendKind.PontoReferencia, "COORDENADA DE REFERÊNCIA"));
        c.Legenda.Add(new(LegendKind.MudancaConfrontante, "MUDANÇA DE CONFRONTAÇÃO"));
        if (vias) { c.Legenda.Add(new(LegendKind.MeioFio, "MEIO-FIO")); }

        c.Orientacao.AddRange(SheetTexts.OrientationLines(ctx.Coords, tableCrs));

        // Assinaturas: responsável técnico e proprietário(s).
        string reg = string.Join(" ", new[] { tech.Conselho, tech.Registro }.Where(x => !string.IsNullOrWhiteSpace(x)));
        string rrt = string.IsNullOrWhiteSpace(info.RrtArt) ? "" : (tech.Conselho == "CAU" ? "RRT " : "ART ") + info.RrtArt;
        c.Assinaturas.Add(new[] { tech.Nome.ToUpperInvariant(), string.Join(" – ", new[] { "Responsável Técnico", reg }.Where(x => x.Length > 0)), rrt });
        if (owners.Length > 0) c.Assinaturas.Add(new[] { owners.ToUpperInvariant(), "Proprietário(s)", "" });

        // Conferência antes de emitir.
        if (owners.Length == 0) c.Pendencias.Add("Proprietário não informado (Dados do Imóvel ou Gerenciar Lotes).");
        if (matr.Length == 0) c.Pendencias.Add("Matrícula não informada.");
        if (string.IsNullOrWhiteSpace(info.Municipio)) c.Pendencias.Add("Município não informado (Dados do Imóvel).");
        if (string.IsNullOrWhiteSpace(tech.Nome)) c.Pendencias.Add("Responsável técnico não informado (Configurações).");
        if (string.IsNullOrWhiteSpace(info.RrtArt)) c.Pendencias.Add("RRT/ART não informado (Dados do Imóvel).");
        foreach (var r in reps)
        {
            if (r.Lot.AreaTitulo is double at && at > 0 && Math.Abs(r.AreaLocal - at) / at > 0.05)
                c.Pendencias.Add($"{r.Lot.DisplayName}: área medida {fmt.Area(r.AreaLocal)} difere mais de 5% da área da matrícula ({fmt.Area(at)}).");
            if (r.Lot.PerimetroTitulo is double pt && pt > 0 && Math.Abs(r.PerimeterLocal - pt) / pt > 0.05)
                c.Pendencias.Add($"{r.Lot.DisplayName}: perímetro medido {fmt.Distance(r.PerimeterLocal)} difere mais de 5% do da matrícula ({fmt.Distance(pt)}).");
            int sem = r.Segments.Count(s => string.IsNullOrWhiteSpace(s.Confrontante));
            if (sem > 0) c.Pendencias.Add($"{r.Lot.DisplayName}: {sem} divisa(s) sem confrontante.");
        }
        if (!ctx.Coords.HasGeoreference) c.Pendencias.Add("Projeto sem georreferência (use Definir Referência): as coordenadas são locais.");
        return c;
    }

    /// <summary>Conteúdo de exemplo (prévia dos modelos de prancha).</summary>
    public static SheetContent Sample()
    {
        var c = new SheetContent { Titulo = "SITUAÇÃO ATUAL", Subtitulo = "REMEMBRAMENTO DOS LOTES 01 E 02" };
        c.Identificacao.Add(new("Proprietário", "Fulano de Tal"));
        c.Identificacao.Add(new("CPF/CNPJ", "000.000.000-00"));
        c.Identificacao.Add(new("Matrícula", "7607"));
        c.Registro.Add(new("Procedimento", "Remembramento"));
        c.Registro.Add(new("Transcrição", "UNIFICAÇÃO DOS LOTES DAS MATRÍCULAS Nº 7607 E Nº 7033, COM CONSTITUIÇÃO DE LOTE URBANO RESULTANTE DO REMEMBRAMENTO."));
        c.Registro.Add(new("Município", "PORTO FRANCO/MA"));
        c.Registro.Add(new("Endereço", "RUA E, Nº SN, JARDIM SÃO MANOEL"));
        c.Registro.Add(new("CEP", "65970-000"));
        c.Quadro.Add(new("ÁREA", "276,22 m²"));
        c.Quadro.Add(new("PERÍMETRO", "131,46 m"));
        c.Legenda.Add(new(LegendKind.AreaImovel, "ÁREA DO IMÓVEL", 3));
        c.Legenda.Add(new(LegendKind.AreaEdificada, "ÁREA EDIFICADA"));
        c.Legenda.Add(new(LegendKind.Limite, "LIMITE DO IMÓVEL"));
        c.Legenda.Add(new(LegendKind.Vertice, "VÉRTICES"));
        c.Legenda.Add(new(LegendKind.PontoReferencia, "COORDENADA DE REFERÊNCIA"));
        c.Legenda.Add(new(LegendKind.MudancaConfrontante, "MUDANÇA DE CONFRONTAÇÃO"));
        c.Orientacao.AddRange(new[] { "SISTEMA GEODÉSICO DE REFERÊNCIA:", "SIRGAS 2000", "PROJEÇÃO UTM – FUSO 23S", "MC 45° WGr", "EPSG:31983" });
        c.Assinaturas.Add(new[] { "NOME DO RESPONSÁVEL", "Responsável Técnico – CAU A000000-0", "RRT 0000000" });
        c.Assinaturas.Add(new[] { "FULANO DE TAL", "Proprietário(s)", "" });
        return c;
    }

    /// <summary>Tabela de exemplo (prévia dos modelos).</summary>
    public static SheetTable SampleTable(SheetTextStyles st)
    {
        var t = new SheetTable { Header = new[] { "Vértice", "Azimute", "Dist. (m)", "E (m)", "N (m)" }, TextMm = st.TabelaMm, TitleMm = st.TabelaMm };
        t.TitleLines.Add("TABELA DE COORDENADAS DOS VÉRTICES");
        t.TitleLines.Add("SISTEMA GEODÉSICO DE REFERÊNCIA: SIRGAS 2000");
        t.TitleLines.Add("PROJEÇÃO UTM – FUSO 23S – EPSG:31983");
        t.Rows.Add(new[] { "P-01", "297°08'50\"", "32,87", "235.142,50", "9.299.292,99" });
        t.Rows.Add(new[] { "P-02", "60°11'09\"", "53,55", "235.113,25", "9.299.307,98" });
        t.Rows.Add(new[] { "P-03", "202°28'12\"", "45,04", "235.159,71", "9.299.334,61" });
        return t.Measure();
    }

    /// <summary>Texto da transcrição (o que a operação faz com as matrículas).</summary>
    public static string Transcricao(ParcelProject p, OperationRecord? op, ProjectInfo info)
    {
        if (op is null) return "";
        var parents = op.ParentIds.Select(p.FindLot).Where(l => l is not null).Select(l => l!).ToList();
        var children = op.ChildIds.Select(p.FindLot).Where(l => l is not null).Select(l => l!).ToList();
        string Mat(LotRecord l) => string.IsNullOrWhiteSpace(l.Matricula) ? "" : l.Matricula.Trim();
        string tipo = string.Equals(info.TipoImovel, "Rural", StringComparison.OrdinalIgnoreCase) ? "imóvel rural" : "lote urbano";
        static string Join(IReadOnlyList<string> xs) => xs.Count <= 1 ? string.Join("", xs) : string.Join(", ", xs.Take(xs.Count - 1)) + " e " + xs[^1];
        switch (op.Type)
        {
            case OperationType.Remembramento:
            {
                var mats = parents.Select(Mat).Where(x => x.Length > 0).Distinct().ToList();
                string what = mats.Count == parents.Count && mats.Count > 0
                    ? "das matrículas " + Join(mats.Select(m => "nº " + m).ToList())
                    : Join(parents.Select(l => l.DisplayName).ToList());
                return $"UNIFICAÇÃO DOS LOTES {what}, COM CONSTITUIÇÃO DE {tipo} RESULTANTE DO REMEMBRAMENTO.".ToUpperInvariant();
            }
            case OperationType.Desmembramento:
            {
                var parent = parents.FirstOrDefault();
                if (parent is null) return "";
                string m = Mat(parent).Length > 0 ? $"DA MATRÍCULA Nº {Mat(parent)}" : parent.DisplayName.ToUpperInvariant();
                return $"DESMEMBRAMENTO DO {tipo.ToUpperInvariant()} {m} EM {children.Count} LOTES ({Join(children.Select(l => l.DisplayName).ToList()).ToUpperInvariant()}).";
            }
            default:
                return $"PARCELAMENTO EM {children.Count} LOTES.";
        }
    }
}
