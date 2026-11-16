using System.Collections.Generic;
using System.Linq;
using GeoPoint.Core.Operations;
using Xunit;

namespace GeoPoint.Core.Tests;

/// <summary>Remover e reordenar trechos na tabela do Desenhar Perímetro.</summary>
public class RowOrderTests
{
    private static readonly string[] Rows = { "A", "B", "C", "D", "E" };

    private static string Moved(int[] sel, int delta, out List<int> now)
    {
        var (order, s) = RowOrder.Move(Rows.Length, sel, delta);
        now = s;
        return string.Concat(RowOrder.Apply(Rows, order));
    }

    [Fact]
    public void SobeUmTrecho()
    {
        Assert.Equal("ACBDE", Moved(new[] { 2 }, -1, out var now));
        Assert.Equal(new[] { 1 }, now);
    }

    [Fact]
    public void DesceUmTrecho()
    {
        Assert.Equal("ABDCE", Moved(new[] { 2 }, +1, out var now));
        Assert.Equal(new[] { 3 }, now);
    }

    [Fact]
    public void BlocoSobeJunto()
    {
        Assert.Equal("ACDBE", Moved(new[] { 2, 3 }, -1, out var now));
        Assert.Equal(new[] { 1, 2 }, now);
    }

    [Fact]
    public void SelecaoSalteadaDesce()
    {
        Assert.Equal("BADCE", Moved(new[] { 0, 2 }, +1, out var now));
        Assert.Equal(new[] { 1, 3 }, now);
    }

    [Fact]
    public void NoComecoNaoSobe()
    {
        // A já está no topo; B encosta em A e também fica; D sobe.
        Assert.Equal("ABDCE", Moved(new[] { 0, 1, 3 }, -1, out var now));
        Assert.Equal(new[] { 0, 1, 2 }, now);
    }

    [Fact]
    public void NoFimNaoDesce()
    {
        Assert.Equal("ABCDE", Moved(new[] { 3, 4 }, +1, out var now));
        Assert.Equal(new[] { 3, 4 }, now);
    }

    [Fact]
    public void RemoveSelecionados()
    {
        Assert.Equal("ACE", string.Concat(RowOrder.Without(Rows, new[] { 3, 1 })));
        Assert.Equal("ABCDE", string.Concat(RowOrder.Without(Rows, new int[0])));
        Assert.Empty(RowOrder.Without(Rows, Enumerable.Range(0, 5)));
    }

    [Fact]
    public void IndicesForaDaTabelaSaoIgnorados()
    {
        Assert.Equal("ABCDE", Moved(new[] { -1, 7 }, -1, out var now));
        Assert.Empty(now);
    }
}
