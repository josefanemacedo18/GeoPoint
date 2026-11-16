using System.Collections.Generic;
using System.Linq;

namespace GeoPoint.Core.Operations;

/// <summary>
/// Reordenar e remover linhas de uma tabela (trechos do Desenhar Perímetro): sobe ou desce as linhas selecionadas uma posição,
/// juntas, mantendo a ordem entre elas.
/// </summary>
public static class RowOrder
{
    /// <summary>
    /// Nova ordem (índices antigos, na ordem nova) e as posições novas das linhas selecionadas depois de mover cada uma
    /// <paramref name="delta"/> posição (−1 sobe, +1 desce). Uma linha já no começo (ou no fim), ou encostada numa selecionada que
    /// não pôde andar, fica onde está.
    /// </summary>
    public static (int[] Order, List<int> Selected) Move(int count, IEnumerable<int> selected, int delta)
    {
        var order = Enumerable.Range(0, count).ToArray();
        var sel = selected.Where(i => i >= 0 && i < count).Distinct().ToList();
        var now = new HashSet<int>();
        if (delta < 0)
        {
            foreach (int i in sel.OrderBy(i => i))
            {
                if (i == 0 || now.Contains(i - 1)) { now.Add(i); continue; }
                (order[i - 1], order[i]) = (order[i], order[i - 1]);
                now.Add(i - 1);
            }
        }
        else if (delta > 0)
        {
            foreach (int i in sel.OrderByDescending(i => i))
            {
                if (i == count - 1 || now.Contains(i + 1)) { now.Add(i); continue; }
                (order[i + 1], order[i]) = (order[i], order[i + 1]);
                now.Add(i + 1);
            }
        }
        else now.UnionWith(sel);
        return (order, now.OrderBy(i => i).ToList());
    }

    /// <summary>Os itens na ordem nova.</summary>
    public static List<T> Apply<T>(IReadOnlyList<T> items, int[] order) => order.Select(i => items[i]).ToList();

    /// <summary>Os itens sem as posições removidas.</summary>
    public static List<T> Without<T>(IReadOnlyList<T> items, IEnumerable<int> removed)
    {
        var r = removed.ToHashSet();
        return items.Where((_, i) => !r.Contains(i)).ToList();
    }
}
