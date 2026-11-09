using GeoPoint.Core.Calc;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;

namespace GeoPoint.Core.Urban;

public sealed class QuadroLinha
{
    public string Categoria { get; init; } = "";
    public LotUse Uso { get; init; }
    public int Quantidade { get; set; }
    public double Area { get; set; }
    public double Percentual { get; set; }
}

public sealed class LoteLinha
{
    public LotRecord Lot { get; init; } = null!;
    public string Nome => Lot.DisplayName;
    public string Uso => LotUseInfo.Label(Lot.Uso);
    public double Area { get; init; }
    public double Perimetro { get; init; }
    public double Testada { get; init; }
    public double AreaLiquida { get; init; }
    public double? AreaEdificavel { get; init; }
    public double? ProjecaoMaxima { get; init; }
    public double? AreaConstruidaMaxima { get; init; }
    public double? AreaTitulo { get; init; }
    public double? DiferencaTitulo { get; init; }
    public string Situacao { get; set; } = "OK";
    public List<string> Problemas { get; } = new();
}

public sealed class QuadroAreas
{
    public List<QuadroLinha> Categorias { get; } = new();
    public List<LoteLinha> Lotes { get; } = new();
    public double AreaTotal { get; set; }
    public double? AreaGleba { get; set; }
    public double AreaPublica { get; set; }
    public double PercentualPublico { get; set; }
    public List<string> Verificacoes { get; } = new();
    public List<string> Problemas { get; } = new();

    public static QuadroAreas Build(ProjectContext ctx)
    {
        var q = new QuadroAreas();
        var rules = ctx.Rules;
        var lots = ProjectContext.SortLots(ctx.Project.ActiveLots).ToList();

        // Descontos dentro de lotes não contam no total (já estão incluídos no lote que os contém).
        var reports = lots.ToDictionary(l => l.Id, l => ctx.Report(l));
        var deductionInside = new HashSet<string>();
        foreach (var l in lots)
            foreach (var (d, _) in reports[l.Id].Deducoes) deductionInside.Add(d.Id);

        var gleba = lots.Where(l => l.Uso == LotUse.Gleba).ToList();
        if (gleba.Count > 0) q.AreaGleba = gleba.Sum(g => reports[g.Id].AreaLocal);

        foreach (var uso in LotUseInfo.All)
        {
            if (uso == LotUse.Gleba) continue;
            var sel = lots.Where(l => l.Uso == uso && !deductionInside.Contains(l.Id)).ToList();
            if (sel.Count == 0) continue;
            q.Categorias.Add(new QuadroLinha
            {
                Categoria = LotUseInfo.Label(uso), Uso = uso, Quantidade = sel.Count,
                Area = sel.Sum(l => reports[l.Id].AreaLocal),
            });
        }
        q.AreaTotal = q.Categorias.Sum(c => c.Area);
        double baseArea = q.AreaGleba ?? q.AreaTotal;
        foreach (var c in q.Categorias) c.Percentual = baseArea > 0 ? c.Area / baseArea * 100 : 0;

        q.AreaPublica = q.Categorias.Where(c => c.Uso is LotUse.SistemaViario or LotUse.AreaVerde or LotUse.AreaInstitucional).Sum(c => c.Area);
        q.PercentualPublico = baseArea > 0 ? q.AreaPublica / baseArea * 100 : 0;

        if (q.AreaGleba is double ag && Math.Abs(ag - q.AreaTotal) > ParcelOperations.AreaTolerance && q.Categorias.Count > 0)
            q.Problemas.Add($"A soma das áreas ({q.AreaTotal:0.00} m²) difere da área da gleba ({ag:0.00} m²) em {q.AreaTotal - ag:0.00} m².");

        foreach (var l in lots)
        {
            if (l.Uso == LotUse.Gleba) continue;
            var r = reports[l.Id];
            double? proj = rules is { TaxaOcupacaoMax: > 0 } ? r.AreaLiquida * rules.TaxaOcupacaoMax / 100 : null;
            double? constr = rules is { CoeficienteAproveitamentoMax: > 0 } ? r.AreaLiquida * rules.CoeficienteAproveitamentoMax : null;
            var line = new LoteLinha
            {
                Lot = l, Area = r.AreaLocal, Perimetro = r.PerimeterLocal, Testada = r.Testada, AreaLiquida = r.AreaLiquida,
                AreaEdificavel = l.AreaEdificavel, ProjecaoMaxima = proj, AreaConstruidaMaxima = constr,
                AreaTitulo = l.AreaTitulo, DiferencaTitulo = l.AreaTitulo is double t ? r.AreaLocal - t : null,
            };
            if (rules is not null) RuleChecker.CheckLot(line, r, rules, ctx.Project.Info.TipoImovel);
            line.Situacao = line.Problemas.Count == 0 ? "OK" : "Verificar";
            q.Lotes.Add(line);
        }

        if (rules is not null)
        {
            if (rules.PercentualMinAreasPublicas > 0)
            {
                bool ok = q.PercentualPublico + 1e-9 >= rules.PercentualMinAreasPublicas;
                (ok ? q.Verificacoes : q.Problemas).Add($"Áreas públicas: {q.PercentualPublico:0.00}% (mínimo {rules.PercentualMinAreasPublicas:0.##}%).");
            }
            void CheckPct(LotUse uso, double min, string label)
            {
                if (min <= 0) return;
                double pct = q.Categorias.Where(c => c.Uso == uso).Sum(c => c.Percentual);
                bool ok = pct + 1e-9 >= min;
                (ok ? q.Verificacoes : q.Problemas).Add($"{label}: {pct:0.00}% (mínimo {min:0.##}%).");
            }
            CheckPct(LotUse.AreaVerde, rules.PercentualMinAreaVerde, "Áreas verdes");
            CheckPct(LotUse.AreaInstitucional, rules.PercentualMinInstitucional, "Áreas institucionais");
        }
        return q;
    }
}

public static class RuleChecker
{
    public static void CheckLot(LoteLinha line, LotReport r, RuleProfile rules, string tipoImovel)
    {
        var l = line.Lot;
        if (l.Uso == LotUse.Lote)
        {
            if (rules.AreaMinimaLote > 0 && r.AreaLocal + 1e-9 < rules.AreaMinimaLote)
                line.Problemas.Add($"Área {r.AreaLocal:0.00} m² menor que o mínimo ({rules.AreaMinimaLote:0.##} m²).");
            if (rules.TestadaMinima > 0)
            {
                if (r.Testada <= 0) line.Problemas.Add("Testada não definida (marque as divisas de frente).");
                else if (r.Testada + 1e-9 < rules.TestadaMinima)
                    line.Problemas.Add($"Testada {r.Testada:0.00} m menor que o mínimo ({rules.TestadaMinima:0.##} m).");
            }
            if (rules.FracaoMinimaRuralHa > 0 && string.Equals(tipoImovel, "Rural", StringComparison.OrdinalIgnoreCase) &&
                r.AreaLocal / 10000.0 + 1e-12 < rules.FracaoMinimaRuralHa)
                line.Problemas.Add($"Área {r.AreaLocal / 10000.0:0.0000} ha menor que a fração mínima ({rules.FracaoMinimaRuralHa:0.####} ha).");
        }
        if (l.AreaTitulo is double t && t > 0 && rules.ToleranciaAreaTitulo > 0)
        {
            double pct = Math.Abs(r.AreaLocal - t) / t * 100;
            if (pct > rules.ToleranciaAreaTitulo)
                line.Problemas.Add($"Diferença de {pct:0.00}% em relação à área do título ({t:0.00} m²), acima da tolerância de {rules.ToleranciaAreaTitulo:0.##}%.");
        }
    }
}
