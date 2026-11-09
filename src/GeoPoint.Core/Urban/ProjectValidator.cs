using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;
using GeoPoint.Core.Topology;

namespace GeoPoint.Core.Urban;

public sealed class ValidationIssue
{
    public DiagnosticSeverity Severity { get; init; }
    public string Category { get; init; } = "";
    public string Message { get; init; } = "";
    public string? LotId { get; init; }
    public string LotName { get; init; } = "";
    public Vec2? Location { get; init; }
}

/// <summary>Verificação geral do projeto (geometria, confrontantes, regras urbanísticas, títulos, sobreposições).</summary>
public static class ProjectValidator
{
    public static List<ValidationIssue> Validate(ProjectContext ctx)
    {
        var res = new List<ValidationIssue>();
        var project = ctx.Project;
        if (!project.Reference.IsSet)
            res.Add(new ValidationIssue { Severity = DiagnosticSeverity.Warning, Category = "Georreferência", Message = "Ponto de referência não definido: memorial e KML usarão apenas coordenadas locais." });
        else if (project.Reference.Precision is PrecisionClass.GoogleMapsEarth or PrecisionClass.GpsNavegacao or PrecisionClass.Desconhecida)
            res.Add(new ValidationIssue { Severity = DiagnosticSeverity.Info, Category = "Georreferência", Message = $"Coordenada de referência de {PrecisionInfo.Label(project.Reference.Precision)} (precisão de metros). Adequada para estudos; para registro use levantamento geodésico." });

        if (!project.ActiveLots.Any())
            res.Add(new ValidationIssue { Severity = DiagnosticSeverity.Info, Category = "Lotes", Message = "Nenhum lote ativo no projeto." });

        foreach (var lot in ProjectContext.SortLots(project.ActiveLots))
        {
            Region region;
            try
            {
                region = lot.ToRegion();
                if (!region.Outer.IsClosed(0.002)) throw new InvalidOperationException("contorno aberto");
            }
            catch (Exception ex)
            {
                res.Add(new ValidationIssue { Severity = DiagnosticSeverity.Error, Category = "Geometria", LotId = lot.Id, LotName = lot.DisplayName, Message = "Geometria inválida: " + ex.Message });
                continue;
            }
            if (region.Area < 0.01)
                res.Add(Issue(lot, DiagnosticSeverity.Error, "Geometria", "Área nula ou muito pequena."));
            foreach (var e in region.Outer.Edges)
                if (e.Length < 0.01)
                    res.Add(new ValidationIssue { Severity = DiagnosticSeverity.Warning, Category = "Geometria", LotId = lot.Id, LotName = lot.DisplayName, Message = $"Divisa muito curta ({e.Length * 1000:0.#} mm).", Location = e.Mid });

            LotReport rep;
            try { rep = ctx.Report(lot); }
            catch (Exception ex)
            {
                res.Add(Issue(lot, DiagnosticSeverity.Error, "Cálculo", "Falha no cálculo: " + ex.Message));
                continue;
            }
            int semConf = rep.Segments.Count(s => string.IsNullOrWhiteSpace(s.Confrontante));
            if (semConf > 0)
                res.Add(Issue(lot, DiagnosticSeverity.Warning, "Confrontantes", $"{semConf} divisa(s) sem confrontante informado."));
            if (string.IsNullOrWhiteSpace(lot.Nome) && lot.Uso == LotUse.Lote)
                res.Add(Issue(lot, DiagnosticSeverity.Info, "Dados", "Lote sem nome/número."));
        }

        var quadro = QuadroAreas.Build(ctx);
        foreach (var line in quadro.Lotes)
            foreach (var p in line.Problemas)
                res.Add(Issue(line.Lot, DiagnosticSeverity.Warning, "Parâmetros urbanísticos", p));
        foreach (var p in quadro.Problemas)
            res.Add(new ValidationIssue { Severity = DiagnosticSeverity.Warning, Category = "Quadro de áreas", Message = p });

        foreach (var (a, b, area) in ParcelOperations.FindOverlaps(project.ActiveLots))
            res.Add(new ValidationIssue { Severity = DiagnosticSeverity.Error, Category = "Sobreposição", LotId = a.Id, LotName = a.DisplayName, Message = $"{a.DisplayName} sobrepõe {b.DisplayName} em {area:0.00} m²." });

        foreach (var op in project.PhaseOperations)
        {
            var missing = op.ParentIds.Concat(op.ChildIds).Where(id => project.FindLot(id) is null).ToList();
            if (missing.Count > 0)
                res.Add(new ValidationIssue { Severity = DiagnosticSeverity.Warning, Category = "Operações", Message = $"{op.Label}: {missing.Count} lote(s) da operação foram excluídos." });
        }
        return res;
    }

    private static ValidationIssue Issue(LotRecord lot, DiagnosticSeverity sev, string cat, string msg) =>
        new() { Severity = sev, Category = cat, LotId = lot.Id, LotName = lot.DisplayName, Message = msg };
}
