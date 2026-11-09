namespace GeoPoint.Core.Urban;

/// <summary>Perfil de parâmetros urbanísticos (editável pelo usuário, um por município/zona).</summary>
public sealed class RuleProfile
{
    public string Name { get; set; } = "Novo perfil";
    public string Description { get; set; } = "";

    /// <summary>Área mínima de lote (m²). 0 = não verificar.</summary>
    public double AreaMinimaLote { get; set; }
    /// <summary>Testada (frente) mínima (m). 0 = não verificar.</summary>
    public double TestadaMinima { get; set; }
    /// <summary>Taxa de ocupação máxima (%). 0 = não informar.</summary>
    public double TaxaOcupacaoMax { get; set; }
    /// <summary>Coeficiente de aproveitamento máximo. 0 = não informar.</summary>
    public double CoeficienteAproveitamentoMax { get; set; }
    /// <summary>Percentual mínimo de áreas públicas (viário + verde + institucional) sobre a gleba (%).</summary>
    public double PercentualMinAreasPublicas { get; set; }
    public double PercentualMinAreaVerde { get; set; }
    public double PercentualMinInstitucional { get; set; }

    public double RecuoFrontal { get; set; }
    public double RecuoLateral { get; set; }
    public double RecuoFundos { get; set; }

    /// <summary>Tolerância aceitável entre área medida e área do título (%).</summary>
    public double ToleranciaAreaTitulo { get; set; } = 5;

    /// <summary>Fração mínima de parcelamento rural (ha). 0 = não verificar.</summary>
    public double FracaoMinimaRuralHa { get; set; }

    public RuleProfile Clone() => (RuleProfile)MemberwiseClone();

    public static List<RuleProfile> Defaults() => new()
    {
        new RuleProfile
        {
            Name = "Lei 6.766/79 (mínimos federais)",
            Description = "Art. 4º, II: área mínima de 125 m² e frente mínima de 5 m, salvo exigências maiores da legislação estadual ou municipal.",
            AreaMinimaLote = 125, TestadaMinima = 5, ToleranciaAreaTitulo = 5,
        },
        new RuleProfile
        {
            Name = "Personalizado (exemplo municipal)",
            Description = "Preencha com os parâmetros da lei de uso e ocupação do solo do município.",
            AreaMinimaLote = 250, TestadaMinima = 10, TaxaOcupacaoMax = 70, CoeficienteAproveitamentoMax = 1.5,
            PercentualMinAreasPublicas = 35, PercentualMinAreaVerde = 10, PercentualMinInstitucional = 5,
            RecuoFrontal = 5, RecuoLateral = 1.5, RecuoFundos = 3, ToleranciaAreaTitulo = 5,
        },
        new RuleProfile
        {
            Name = "Rural (fração mínima)",
            Description = "Informe a fração mínima de parcelamento (FMP) do município conforme o INCRA.",
            FracaoMinimaRuralHa = 2, ToleranciaAreaTitulo = 5,
        },
    };
}
