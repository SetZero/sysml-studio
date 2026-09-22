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
    }

    /// <summary>A node is as wide as its widest line and as tall as its lines.</summary>
    private static void Measure(DiagramNode node)
    {
        var lines = new List<string> { node.Label };
        if (node.Stereotype is { Length: > 0 } stereotype)
            lines.Add(stereotype);
        lines.AddRange(node.Features);

        var widest = lines.Max(l => l.Length);
        node.Width = Math.Clamp((widest * CharacterWidth) + (Padding * 2), 120, 420);
        node.Height = (lines.Count * LineHeight) + Padding;
    }
}
