using GeoPoint.Core.Calc;
using GeoPoint.Core.Model;

namespace GeoPoint.Core.Sheets;

public enum SheetKind { Atual, Proposta, IndividualAtual, IndividualProposta }

/// <summary>Uma prancha a gerar: quais lotes aparecem anotados, título e quadro.</summary>
public sealed class SheetSpec
{
    public SheetKind Kind { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public List<string> LotIds { get; init; } = new();
    /// <summary>Lotes listados no quadro de áreas (vazio = sem quadro).</summary>
    public List<string> QuadroIds { get; init; } = new();
    public bool Hatch { get; init; }
    /// <summary>Índice de cor de cada lote (o mesmo em todas as pranchas da operação).</summary>
    public Dictionary<string, int> Colors { get; init; } = new();
}

public sealed class SheetRequest
{
    public bool Atual { get; set; } = true;
    public bool Proposta { get; set; } = true;
    public bool IndividualAtual { get; set; }
    public bool IndividualProposta { get; set; } = true;
    public bool Quadro { get; set; } = true;
    public bool Hachuras { get; set; } = true;
}

public static class SheetPlanner
{
    public static List<SheetSpec> ForOperation(ParcelProject p, OperationRecord op, SheetRequest r)
    {
        var parents = op.ParentIds.Select(p.FindLot).Where(l => l is not null).Select(l => l!).ToList();
        var children = op.ChildIds.Select(p.FindLot).Where(l => l is not null).Select(l => l!).ToList();
        // Remanescente primeiro? Não: mantém a ordem de leitura dos lotes.
        children = ProjectContext.SortLots(children).ToList();
        parents = ProjectContext.SortLots(parents).ToList();
        var colors = new Dictionary<string, int>();
        foreach (var l in parents.Concat(children)) if (!colors.ContainsKey(l.Id)) colors[l.Id] = colors.Count;
        var res = new List<SheetSpec>();
        string opName = op.Type switch
        {
            OperationType.Desmembramento => "DESMEMBRAMENTO",
            OperationType.Remembramento => "REMEMBRAMENTO",
            _ => "PARCELAMENTO",
        };
        string Names(IEnumerable<LotRecord> ls) => string.Join(", ", ls.Select(l => l.DisplayName));
        string subParents = op.Type == OperationType.Desmembramento ? $"{opName} DO {Names(parents).ToUpperInvariant()}" : $"{opName} DOS LOTES {string.Join(", ", parents.Select(l => l.Nome))}".ToUpperInvariant();
        if (r.Atual && parents.Count > 0)
            res.Add(new SheetSpec
            {
                Kind = SheetKind.Atual, Title = "SITUAÇÃO ATUAL", Subtitle = subParents, LotIds = parents.Select(l => l.Id).ToList(),
                QuadroIds = r.Quadro && parents.Count > 1 ? parents.Select(l => l.Id).ToList() : new(), Hatch = r.Hachuras && parents.Count > 1, Colors = colors,
            });
        if (r.IndividualAtual && parents.Count > 1)
            foreach (var l in parents)
                res.Add(new SheetSpec
                {
                    Kind = SheetKind.IndividualAtual, Title = "SITUAÇÃO ATUAL – " + l.DisplayName.ToUpperInvariant(), Subtitle = subParents,
                    LotIds = { l.Id }, Hatch = r.Hachuras, Colors = colors,
                });
        if (r.Proposta && children.Count > 0)
            res.Add(new SheetSpec
            {
                Kind = SheetKind.Proposta, Title = "SITUAÇÃO PROPOSTA", Subtitle = subParents, LotIds = children.Select(l => l.Id).ToList(),
                QuadroIds = r.Quadro ? children.Select(l => l.Id).ToList() : new(), Hatch = r.Hachuras, Colors = colors,
            });
        if (r.IndividualProposta && children.Count > 1)
            foreach (var l in children)
                res.Add(new SheetSpec
                {
                    Kind = SheetKind.IndividualProposta, Title = "SITUAÇÃO PROPOSTA – " + l.DisplayName.ToUpperInvariant(), Subtitle = subParents,
                    LotIds = { l.Id }, Hatch = r.Hachuras, Colors = colors,
                });
        return res;
    }

    /// <summary>Pranchas sem operação (regularização, levantamento, usucapião...).</summary>
    public static List<SheetSpec> ForLots(IReadOnlyList<LotRecord> lots, SheetRequest r, string situacao)
    {
        var sorted = ProjectContext.SortLots(lots).ToList();
        var colors = new Dictionary<string, int>();
        foreach (var l in sorted) colors[l.Id] = colors.Count;
        string sub = string.IsNullOrWhiteSpace(situacao) ? "" : situacao.ToUpperInvariant();
        var res = new List<SheetSpec>();
        if (r.Atual || sorted.Count == 1)
            res.Add(new SheetSpec
            {
                Kind = SheetKind.Atual, Title = "PLANTA DE SITUAÇÃO", Subtitle = sub, LotIds = sorted.Select(l => l.Id).ToList(),
                QuadroIds = r.Quadro && sorted.Count > 1 ? sorted.Select(l => l.Id).ToList() : new(), Hatch = r.Hachuras && sorted.Count > 1, Colors = colors,
            });
        if (r.IndividualAtual && sorted.Count > 1)
            foreach (var l in sorted)
                res.Add(new SheetSpec
                {
                    Kind = SheetKind.IndividualAtual, Title = "PLANTA DE SITUAÇÃO – " + l.DisplayName.ToUpperInvariant(), Subtitle = sub,
                    LotIds = { l.Id }, Hatch = r.Hachuras, Colors = colors,
                });
        return res;
    }
}
