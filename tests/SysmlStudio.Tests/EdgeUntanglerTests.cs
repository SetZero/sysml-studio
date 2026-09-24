using SysmlStudio.Diagrams;
using Xunit;

namespace SysmlStudio.Tests;

/// <summary>
/// A crossing is worth a detour of up to <see cref="EdgeUntangler.CrossingPenalty"/>:
/// an edge goes round another when that is cheap and crosses it when it is not.
/// </summary>
public sealed class EdgeUntanglerTests
{
    // Box 0 sits above box 1; boxes 2 and 3 sit either side of the gap between
    // them, and the edge from 2 to 3 runs straight across the way from 0 to 1.
    // The further the boxes reach, the longer the way round either edge.
    private static (List<(double X, double Y, double Width, double Height)> Boxes, List<EdgeUntangler.Route> Routes) Crossing(double reach)
    {
        List<(double X, double Y, double Width, double Height)> boxes =
        [
            (0, -reach, 40, 40 + reach),
            (0, 200, 40, 40 + reach),
            (-reach - 40, 100, 40, 40),
            (40 + reach, 100, 40, 40),
        ];
        List<EdgeUntangler.Route> routes =
        [
            new(0, 1, [(20, 40), (20, 200)]),
            new(2, 3, [(-reach, 120), (40 + reach, 120)]),
        ];
        return (boxes, routes);
    }

    [Fact]
    public void AnEdgeGoesRoundAShortEdgeItWouldCross()
    {
        var (boxes, routes) = Crossing(reach: 30);
        Assert.Equal(1, EdgeUntangler.Crossings(routes));

        EdgeUntangler.Untangle(boxes, routes);

        Assert.Equal(0, EdgeUntangler.Crossings(routes));
        Assert.Contains(routes, r => r.Changed);
        foreach (var route in routes)
        {
            var (source, target) = (boxes[route.Source], boxes[route.Target]);
            Assert.True(OnEdge(route.Points[0], source), "the route starts on its source box's edge");
            Assert.True(OnEdge(route.Points[^1], target), "the route ends on its target box's edge");
            foreach (var (box, i) in boxes.Select((b, i) => (b, i)).Where(b => b.i != route.Source && b.i != route.Target))
                Assert.False(Through(route.Points, box), $"route {route.Source}->{route.Target} runs through box {i}");
        }
    }

    [Fact]
    public void AnEdgeCrossesALongEdgeRatherThanGoRoundIt()
    {
        var (boxes, routes) = Crossing(reach: 400);

        EdgeUntangler.Untangle(boxes, routes);

        Assert.Equal(1, EdgeUntangler.Crossings(routes));
        Assert.DoesNotContain(routes, r => r.Changed);
    }

    private static bool OnEdge((double X, double Y) p, (double X, double Y, double Width, double Height) box)
    {
        const double slack = 0.5;
        var within = p.X >= box.X - slack && p.X <= box.X + box.Width + slack && p.Y >= box.Y - slack && p.Y <= box.Y + box.Height + slack;
        var onSide = Math.Abs(p.X - box.X) <= slack || Math.Abs(p.X - box.X - box.Width) <= slack
                     || Math.Abs(p.Y - box.Y) <= slack || Math.Abs(p.Y - box.Y - box.Height) <= slack;
        return within && onSide;
    }

    private static bool Through(List<(double X, double Y)> points, (double X, double Y, double Width, double Height) box)
    {
        for (var i = 0; i + 1 < points.Count; i++)
        {
            for (var s = 0; s <= 20; s++)
            {
                var x = points[i].X + ((points[i + 1].X - points[i].X) * s / 20);
                var y = points[i].Y + ((points[i + 1].Y - points[i].Y) * s / 20);
                if (x > box.X && x < box.X + box.Width && y > box.Y && y < box.Y + box.Height)
                    return true;
            }
        }

        return false;
    }
}
