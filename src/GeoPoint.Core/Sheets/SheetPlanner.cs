using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;

namespace GeoPoint.Core.Sheets;

public enum SheetKind { Atual, Proposta, IndividualAtual, IndividualProposta }

/// <summary>O que fazer quando a prancha já foi gerada antes.</summary>
public enum SheetReplace { Substituir = 0, Manter = 1, NovaVersao = 2 }

/// <summary>Uma prancha a gerar: quais lotes aparecem anotados, título e quadro.</summary>
public sealed class SheetSpec
{
    public SheetKind Kind { get; init; }
    /// <summary>Identifica a prancha entre gerações (fase/operação, tipo e lotes).</summary>
    public string Key { get; set; } = "";
    /// <summary>Operação de origem (vazio = lotes sem operação).</summary>
    public string OperationId { get; set; } = "";
    /// <summary>Prancha de um lote só: vértices com numeração própria (P-01…), todos os azimutes e quadro de coordenadas.
    /// Pranchas com vários lotes são de visualização: distâncias, confrontantes, áreas e proprietários.</summary>
    public bool Detailed => LotIds.Count == 1;
    /// <summary>Ponto (coordenadas do desenho, m) cuja coordenada vai no carimbo. Null = padrão.</summary>
    public Vec2? RefPoint { get; set; }
    public SheetReplace Replace { get; set; } = SheetReplace.Substituir;
    public string Title { get; init; } = "";
    public string Subtitle { get; set; } = "";
    public List<string> LotIds { get; init; } = new();
    /// <summary>Lotes listados no quadro de áreas (vazio = sem quadro).</summary>
    public List<string> QuadroIds { get; init; } = new();
    public bool Hatch { get; init; }
    /// <summary>Quadro de coordenadas do lote (pranchas de um lote só).</summary>
    public bool CoordTable { get; init; }
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
    public bool Coordenadas { get; set; } = true;
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
                QuadroIds = r.Quadro && parents.Count > 1 ? parents.Select(l => l.Id).ToList() : new(), Hatch = r.Hachuras, Colors = colors,
                CoordTable = r.Coordenadas && parents.Count == 1,
            });
        if (r.IndividualAtual && parents.Count > 1)
            foreach (var l in parents)
                res.Add(new SheetSpec
                {
                    Kind = SheetKind.IndividualAtual, Title = "SITUAÇÃO ATUAL – " + l.DisplayName.ToUpperInvariant(), Subtitle = subParents,
                    LotIds = { l.Id }, Hatch = r.Hachuras, Colors = colors, CoordTable = r.Coordenadas,
                });
        if (r.Proposta && children.Count > 0)
            res.Add(new SheetSpec
            {
                Kind = SheetKind.Proposta, Title = "SITUAÇÃO PROPOSTA", Subtitle = subParents, LotIds = children.Select(l => l.Id).ToList(),
                QuadroIds = r.Quadro && children.Count > 1 ? children.Select(l => l.Id).ToList() : new(), Hatch = r.Hachuras, Colors = colors,
                CoordTable = r.Coordenadas && children.Count == 1,
            });
        if (r.IndividualProposta && children.Count > 1)
            foreach (var l in children)
                res.Add(new SheetSpec
                {
                    Kind = SheetKind.IndividualProposta, Title = "SITUAÇÃO PROPOSTA – " + l.DisplayName.ToUpperInvariant(), Subtitle = subParents,
                    LotIds = { l.Id }, Hatch = r.Hachuras, Colors = colors, CoordTable = r.Coordenadas,
                });
        foreach (var x in res)
        {
            x.Key = $"op:{op.Id}|{x.Kind}|{string.Join(",", x.LotIds.OrderBy(i => i, StringComparer.Ordinal))}";
            x.OperationId = op.Id;
        }
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
                QuadroIds = r.Quadro && sorted.Count > 1 ? sorted.Select(l => l.Id).ToList() : new(), Hatch = r.Hachuras, Colors = colors,
                CoordTable = r.Coordenadas && sorted.Count == 1,
            });
        if (r.IndividualAtual && sorted.Count > 1)
            foreach (var l in sorted)
                res.Add(new SheetSpec
                {
                    Kind = SheetKind.IndividualAtual, Title = "PLANTA DE SITUAÇÃO – " + l.DisplayName.ToUpperInvariant(), Subtitle = sub,
                    LotIds = { l.Id }, Hatch = r.Hachuras, Colors = colors, CoordTable = r.Coordenadas,
                });
        string fase = sorted.FirstOrDefault()?.FaseId ?? "";
        foreach (var x in res) x.Key = $"lots:{fase}|{x.Kind}|{string.Join(",", x.LotIds.OrderBy(i => i, StringComparer.Ordinal))}";
        return res;
    }
}
