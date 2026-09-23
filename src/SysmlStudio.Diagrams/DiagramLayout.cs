using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Core.Routing;
using Microsoft.Msagl.Layout.Layered;
using Microsoft.Msagl.Miscellaneous;

namespace SysmlStudio.Diagrams;

/// <summary>
/// Places a diagram's nodes and routes its edges with MSAGL's layered
/// (Sugiyama) layout — the same shape of picture a block diagram wants, and
/// nothing anybody has to position by hand.
/// </summary>
public static class DiagramLayout
{
    private const double CharacterWidth = 7.2;
    private const double LineHeight = 18;
    private const double Padding = 16;

    /// <summary>Sizes every node from its text, then lays the diagram out in place.</summary>
    public static void Apply(Diagram diagram)
    {
        foreach (var node in diagram.Nodes)
            Measure(node);

        if (diagram.Nodes.Count == 0)
            return;

        var graph = new GeometryGraph();
        var nodes = new Dictionary<DiagramNode, Node>();

        foreach (var node in diagram.Nodes)
        {
            var shape = new Node(CurveFactory.CreateRectangle(node.Width, node.Height, new Point()), node);
            nodes[node] = shape;
            graph.Nodes.Add(shape);
        }

        foreach (var edge in diagram.Edges)
            graph.Edges.Add(new Edge(nodes[edge.Source], nodes[edge.Target]) { UserData = edge });

        var settings = new SugiyamaLayoutSettings
        {
            NodeSeparation = 40,
            LayerSeparation = 60,
            EdgeRoutingSettings = { EdgeRoutingMode = EdgeRoutingMode.Spline },
        };

        LayoutHelpers.CalculateLayout(graph, settings, null);

        // MSAGL works in its own coordinates with y up; move the graph so the
        // top left corner is the origin and y grows downwards, which is what
        // every canvas this is drawn on expects.
        var left = graph.BoundingBox.Left;
        var top = graph.BoundingBox.Top;

        foreach (var (node, shape) in nodes)
        {
            node.X = shape.Center.X - (node.Width / 2) - left;
            node.Y = top - shape.Center.Y - (node.Height / 2);
        }

        foreach (var edge in graph.Edges)
        {
            if (edge.UserData is not DiagramEdge diagramEdge)
                continue;

            diagramEdge.Waypoints.Clear();
            if (edge.Curve is null)
                continue;

            const int samples = 12;
            for (var i = 0; i <= samples; i++)
            {
                var t = edge.Curve.ParStart + ((edge.Curve.ParEnd - edge.Curve.ParStart) * i / samples);
                var point = edge.Curve[t];
                diagramEdge.Waypoints.Add((point.X - left, top - point.Y));
            }
        }

        WrapWideLayers(diagram);
    }

    /// <summary>
    /// A layered layout puts every child of a hub in one layer, so a definition
    /// with thirty parts becomes one row thirty boxes wide and has to be shown
    /// at a tenth of its size. Any layer wider than a landscape picture of the
    /// diagram's content would be is wrapped into several rows, in the order the layout chose, and the
    /// layers below move down to make room. Wrapped edges lose their routes and
    /// are drawn straight, which is how the canvas draws them anyway.
    /// </summary>
    private static void WrapWideLayers(Diagram diagram)
    {
        const double rowGap = 40;
        const double nodeGap = 30;

        // Rows as wide as a landscape picture of this much content would be.
        var area = diagram.Nodes.Sum(n => (n.Width + nodeGap) * (n.Height + rowGap));
        var maxWidth = Math.Max(1400, Math.Sqrt(area * 1.8));

        var layers = diagram.Nodes
            .GroupBy(n => Math.Round(n.Y + (n.Height / 2)))
            .OrderBy(g => g.Key)
            .Select(g => g.OrderBy(n => n.X).ToList())
            .ToList();

        var wrapped = false;
        var shift = 0.0;
        foreach (var layer in layers)
        {
            foreach (var node in layer)
                node.Y += shift;

            var width = layer.Sum(n => n.Width) + (nodeGap * (layer.Count - 1));
            if (width <= maxWidth)
                continue;

            wrapped = true;
            var top = layer.Min(n => n.Y);
            var left = layer.Min(n => n.X);
            var x = left;
            var y = top;
            var rowHeight = 0.0;
            foreach (var node in layer)
            {
                if (x > left && x + node.Width > left + maxWidth)
                {
                    x = left;
                    y += rowHeight + rowGap;
                    rowHeight = 0;
                }

                node.X = x;
                node.Y = y;
                x += node.Width + nodeGap;
                rowHeight = Math.Max(rowHeight, node.Height);
            }

            var oldBottom = top + layer.Max(n => n.Height);
            shift += (y + rowHeight) - oldBottom;
        }

        if (!wrapped)
            return;

        // The hub sat above the middle of the long row; centre every layer on
        // the new, narrower picture so it sits above its rows again.
        var centre = diagram.Nodes.Max(n => n.X + n.Width) / 2;
        foreach (var row in diagram.Nodes.GroupBy(n => Math.Round(n.Y)))
        {
            var rowCentre = (row.Min(n => n.X) + row.Max(n => n.X + n.Width)) / 2;
            foreach (var node in row)
                node.X += centre - rowCentre;
        }

        var minX = diagram.Nodes.Min(n => n.X);
        foreach (var node in diagram.Nodes)
            node.X -= minX;

        foreach (var edge in diagram.Edges)
            edge.Waypoints.Clear();
    }

    /// <summary>A node is as wide as its widest line and as tall as its lines.</summary>
    private static void Measure(DiagramNode node)
    {
        if (node.IsPseudo)
        {
            node.Width = node.Height = 26;
            return;
        }

        var lines = new List<string> { node.Label };
        if (node.Stereotype is { Length: > 0 } stereotype)
            lines.Add(stereotype);
        lines.AddRange(node.Features);

        var widest = lines.Max(l => l.Length);
        node.Width = Math.Clamp((widest * CharacterWidth) + (Padding * 2), 120, 420);
        node.Height = (lines.Count * LineHeight) + Padding;
    }
}
