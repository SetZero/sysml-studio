using SysmlStudio.Diagrams;
using SysmlStudio.Model;
using Xunit;

namespace SysmlStudio.Tests;

/// <summary>Boxes in a row are reordered when that makes fewer edges cross, and left alone otherwise.</summary>
public sealed class LayerOrderTests
{
    private static readonly Element Anything =
        SysmlWorkspace.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures")).Find("Sample")!;

    private static DiagramNode Box(string label, double x, double y)
        => new(Anything, label) { X = x, Y = y, Width = 120, Height = 60 };

    [Fact]
    public void TwoEdgesThatCrossAreUncrossed()
    {
        // A over C and B over D, with A joined to D and B to C: an X.
        var (a, b, c, d) = (Box("A", 0, 0), Box("B", 200, 0), Box("C", 0, 200), Box("D", 200, 200));
        List<DiagramEdge> edges = [new(a, d, RelationKind.Composition), new(b, c, RelationKind.Composition)];
        Assert.Equal(1, LayerOrder.Cost([a, b, c, d], edges));

        Assert.True(LayerOrder.ReduceCrossings([a, b, c, d], edges));

        Assert.Equal(0, LayerOrder.Cost([a, b, c, d], edges));
        Assert.Equal(0, LayerOrder.Estimate([a, b, c, d], edges));
    }

    [Fact]
    public void RowsThatCrossNothingStayWhereTheyAre()
    {
        var (a, b, c, d) = (Box("A", 0, 0), Box("B", 250, 0), Box("C", 0, 200), Box("D", 250, 200));
        List<DiagramEdge> edges = [new(a, c, RelationKind.Composition), new(b, d, RelationKind.Composition)];

        Assert.False(LayerOrder.ReduceCrossings([a, b, c, d], edges));

        Assert.Equal([0.0, 250, 0, 250], [a.X, b.X, c.X, d.X]);
    }
}
