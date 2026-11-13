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
    /// <summary>Lotes que têm pendência (para selecionar no desenho).</summary>
    public HashSet<string> PendenciaLotIds { get; } = new();

    public static SheetContent Build(ProjectContext ctx, ValueFormatter fmt, SheetSpec spec, IReadOnlyList<LotRecord> lots, CrsDef tableCrs,
        bool vias = false, bool edificacoes = false)
    {
        var c = new SheetContent { Titulo = spec.Title, Subtitulo = spec.Subtitle };
        var p = ctx.Project;
        var info = p.Info;
        var tech = ctx.Settings.Technician;
        var op = p.Operations.FirstOrDefault(o => o.Id == spec.OperationId);

        static string Short(LotRecord l) => l.DisplayName.Split(" – ")[0];
        // Proprietários (com o CPF/CNPJ de cada um) e matrículas efetivas: lotes resultantes de operação não herdam a
        // matrícula do imóvel (no desmembramento só o remanescente fica com a matrícula do original).
        var ownerList = lots.Select(l => (Owner: p.EffectiveOwner(l), Doc: p.EffectiveDocumento(l), Lot: l))
            .Where(x => x.Owner.Length > 0).GroupBy(x => x.Owner, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Owner: g.First().Owner, Doc: g.Select(x => x.Doc).FirstOrDefault(d => d.Length > 0) ?? "", Lots: g.Select(x => x.Lot).ToList())).ToList();
        string owners = string.Join("; ", ownerList.Select(x => x.Owner));
        if (ownerList.Count <= 1)
        {
            c.Identificacao.Add(new("Proprietário", owners));
            c.Identificacao.Add(new("CPF/CNPJ", ownerList.FirstOrDefault().Doc ?? ""));
        }
        else
            c.Identificacao.Add(new("Proprietários", string.Join("; ", ownerList.Select(x => x.Doc.Length > 0 ? $"{x.Owner} ({x.Doc})" : x.Owner))));
        var mats = lots.Select(l => (Lot: l, Mat: p.EffectiveMatricula(l))).ToList();
        string matr = lots.Count == 1
            ? mats[0].Mat
            : string.Join("; ", mats.Where(x => x.Mat.Length > 0).Select(x => $"{Short(x.Lot)}: {x.Mat}"));
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

        // Convenções: só o que aparece no desenho, com as mesmas cores e hachuras.
        c.SetLegend(spec, lots, spec.Hatch, vertices: true, callouts: spec.Detailed, ticks: HasConfrontanteChange(reps),
            edificacoes: edificacoes, vias: vias);

        c.Orientacao.AddRange(SheetTexts.OrientationLines(ctx.Coords, tableCrs));

        // Assinaturas: responsável técnico e proprietário(s).
        string reg = string.Join(" ", new[] { tech.Conselho, tech.Registro }.Where(x => !string.IsNullOrWhiteSpace(x)));
        string rrt = string.IsNullOrWhiteSpace(info.RrtArt) ? "" : (tech.Conselho == "CAU" ? "RRT " : "ART ") + info.RrtArt;
        c.Assinaturas.Add(new[] { tech.Nome.ToUpperInvariant(), string.Join(" – ", new[] { "Responsável Técnico", reg }.Where(x => x.Length > 0)), rrt });
        if (owners.Length > 0) c.Assinaturas.Add(new[] { owners.ToUpperInvariant(), "Proprietário(s)", "" });

        // Conferência antes de emitir.
        if (owners.Length == 0) c.Pendencias.Add("Proprietário não informado (Dados do Imóvel ou Gerenciar Lotes).");
        foreach (var x in ownerList.Where(x => x.Doc.Length == 0))
        {
            c.Pendencias.Add($"CPF/CNPJ de {x.Owner} não informado ({string.Join(", ", x.Lots.Select(Short))}).");
            foreach (var l in x.Lots) c.PendenciaLotIds.Add(l.Id);
        }
        foreach (var x in mats.Where(x => x.Mat.Length == 0 && !p.IsOperationResult(x.Lot)))
        {
            c.Pendencias.Add($"{x.Lot.DisplayName}: matrícula não informada.");
            c.PendenciaLotIds.Add(x.Lot.Id);
        }
        if (string.IsNullOrWhiteSpace(info.Municipio)) c.Pendencias.Add("Município não informado (Dados do Imóvel).");
        if (string.IsNullOrWhiteSpace(tech.Nome)) c.Pendencias.Add("Responsável técnico não informado (Configurações).");
        if (string.IsNullOrWhiteSpace(info.RrtArt)) c.Pendencias.Add("RRT/ART não informado (Dados do Imóvel).");
        foreach (var r in reps)
        {
            int before = c.Pendencias.Count;
            if (r.Lot.AreaTitulo is double at && at > 0 && Math.Abs(r.AreaLocal - at) / at > 0.05)
                c.Pendencias.Add($"{r.Lot.DisplayName}: área medida {fmt.Area(r.AreaLocal)} difere mais de 5% da área da matrícula ({fmt.Area(at)}).");
            if (r.Lot.PerimetroTitulo is double pt && pt > 0 && Math.Abs(r.PerimeterLocal - pt) / pt > 0.05)
                c.Pendencias.Add($"{r.Lot.DisplayName}: perímetro medido {fmt.Distance(r.PerimeterLocal)} difere mais de 5% do da matrícula ({fmt.Distance(pt)}).");
            int sem = r.Segments.Count(s => string.IsNullOrWhiteSpace(s.Confrontante));
            if (sem > 0) c.Pendencias.Add($"{r.Lot.DisplayName}: {sem} divisa(s) sem confrontante.");
            // Confrontante que é lote do projeto sem proprietário: aparece pelo nome do lote.
            if (ctx.Settings.Naming.ConfrontanteNome == GeoPoint.Core.Settings.ConfrontanteNome.Proprietario)
                foreach (var nb in r.Segments.Where(s => s.ConfrontanteLotId.Length > 0).Select(s => p.FindLot(s.ConfrontanteLotId)).Where(l => l is not null).Distinct())
                    if (p.ConfrontanteOwner(nb!).Length == 0)
                    {
                        c.Pendencias.Add($"{r.Lot.DisplayName}: o confrontante {nb!.DisplayName} está sem proprietário (informe em Gerenciar Lotes; até lá aparece o nome do lote).");
                        c.PendenciaLotIds.Add(nb.Id);
                    }
            if (c.Pendencias.Count > before) c.PendenciaLotIds.Add(r.Lot.Id);
        }
        if (!ctx.Coords.HasGeoreference) c.Pendencias.Add("Projeto sem georreferência (use Definir Referência): as coordenadas são locais.");
        return c;
    }

    /// <summary>
    /// Convenções da prancha conforme o desenho: cada lote com a sua cor (até 6 lotes; acima disso uma entrada geral),
    /// o remanescente com a hachura cruzada na cor dele, e só os símbolos que aparecem.
    /// </summary>
    public void SetLegend(SheetSpec spec, IReadOnlyList<LotRecord> lots, bool hatch, bool vertices, bool callouts, bool ticks, bool edificacoes, bool vias)
    {
        Legenda.Clear();
        var ordered = ProjectContext.SortLots(lots).ToList();
        int Color(LotRecord l, int i) => spec.Colors.TryGetValue(l.Id, out int ci) ? ci : i;
        static string Short(LotRecord l) => l.DisplayName.Split(" – ")[0].ToUpperInvariant();
        if (hatch && ordered.Count > 0)
        {
            if (ordered.Count == 1)
            {
                var l = ordered[0];
                Legenda.Add(new(l.IsRemanescente ? LegendKind.Remanescente : LegendKind.AreaImovel, l.IsRemanescente ? "LOTE REMANESCENTE" : "ÁREA DO IMÓVEL", Color(l, 0)));
            }
            else if (ordered.Count <= 6)
                for (int i = 0; i < ordered.Count; i++)
                    Legenda.Add(new(ordered[i].IsRemanescente ? LegendKind.Remanescente : LegendKind.AreaImovel, Short(ordered[i]), Color(ordered[i], i)));
            else
            {
                var firstLot = ordered.FirstOrDefault(l => !l.IsRemanescente) ?? ordered[0];
                Legenda.Add(new(LegendKind.AreaImovel, "ÁREA DOS LOTES", Color(firstLot, 0)));
                var rem = ordered.FirstOrDefault(l => l.IsRemanescente);
                if (rem is not null) Legenda.Add(new(LegendKind.Remanescente, "LOTE REMANESCENTE", Color(rem, ordered.IndexOf(rem))));
            }
        }
        if (edificacoes) Legenda.Add(new(LegendKind.AreaEdificada, "ÁREA EDIFICADA"));
        Legenda.Add(new(LegendKind.Limite, "LIMITE DO IMÓVEL"));
        if (vertices) Legenda.Add(new(LegendKind.Vertice, "VÉRTICES"));
        if (callouts) Legenda.Add(new(LegendKind.PontoReferencia, "COORDENADA DE REFERÊNCIA"));
        if (ticks) Legenda.Add(new(LegendKind.MudancaConfrontante, "MUDANÇA DE CONFRONTAÇÃO"));
        if (vias) Legenda.Add(new(LegendKind.MeioFio, "MEIO-FIO"));
    }

    /// <summary>Algum lote tem confrontantes diferentes numa mesma linha (há ponto de mudança de confrontação)?</summary>
    public static bool HasConfrontanteChange(IEnumerable<LotReport> reps)
    {
        foreach (var r in reps)
        {
            int n = r.Segments.Count;
            for (int i = 0; i < n; i++)
            {
                var a = r.Segments[(i - 1 + n) % n]; var b = r.Segments[i];
                if (a.Curve.TangentAt(1).Dot(b.Curve.TangentAt(0)) > Math.Cos(Math.PI / 90) &&
                    !string.Equals((a.Confrontante ?? "").Trim(), (b.Confrontante ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
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
