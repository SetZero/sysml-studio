using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Core.Routing;
using Microsoft.Msagl.Layout.Layered;
using Microsoft.Msagl.Miscellaneous;

namespace SysmlStudio.Diagrams;

/// <summary>
/// <para>
/// Places a diagram's nodes and routes its edges, the way Graphviz does it for
/// PlantUML: boxes in layers, edges routed around the boxes rather than
/// through them, labels placed where no box or other label is.
/// </para>
/// <para>
/// All of that is MSAGL's work. This class sizes the boxes, hands them over,
/// wraps a layer too wide to read, and turns MSAGL's y-up coordinates into the
/// y-down ones every canvas uses.
/// </para>
/// </summary>
public static class DiagramLayout
{
    // Box geometry, matching the canvas's node template: header with the kind
    // caption and the title, then a compartment of feature lines.
    private const double HeaderHeight = 56;
    private const double CompartmentChrome = 17;
    private const double FeatureLine = 19;
    private const double BoxChrome = 5 + 24;
    private const double TitleCharacter = 7.6;
    private const double MonoCharacter = 7.3;
    private const double CaptionCharacter = 8.0;

    // Spacing, generous enough that a routed edge has room between boxes.
    private const double NodeSeparation = 50;
    private const double LayerSeparation = 70;
    private const double RowGap = 50;
    private const double BoxGap = 36;
    private const double Clearance = 24;

    /// <summary>
    /// Lays the diagram out: sizes the boxes from their text unless
    /// <paramref name="measure"/> is false (a view that has measured them for
    /// real passes its sizes in), places them in layers, wraps layers too wide
    /// to read, and routes every edge around the boxes.
    /// </summary>
    public static void Apply(Diagram diagram, bool measure = true)
    {
        if (measure)
        {
            foreach (var node in diagram.Nodes)
                Measure(node);
        }

        if (diagram.Nodes.Count == 0)
            return;

        var graph = new GeometryGraph();
        var shapes = new Dictionary<DiagramNode, Node>();
        foreach (var node in diagram.Nodes)
        {
            var shape = new Node(CurveFactory.CreateRectangle(node.Width, node.Height, new Point()), node);
            shapes[node] = shape;
            graph.Nodes.Add(shape);
        }

        foreach (var edge in diagram.Edges)
            graph.Edges.Add(new Edge(shapes[edge.Source], shapes[edge.Target]) { UserData = edge });

        var settings = new SugiyamaLayoutSettings
        {
            NodeSeparation = NodeSeparation,
            LayerSeparation = LayerSeparation,
            EdgeRoutingSettings = { EdgeRoutingMode = EdgeRoutingMode.Spline },
        };
        LayoutHelpers.CalculateLayout(graph, settings, null);

        var left = graph.BoundingBox.Left;
        var top = graph.BoundingBox.Top;
        foreach (var (node, shape) in shapes)
        {
            node.X = shape.Center.X - (node.Width / 2) - left;
            node.Y = top - shape.Center.Y - (node.Height / 2);
        }

        WrapWideLayers(diagram);
        RemoveOverlaps(diagram);
        Route(diagram);
    }

    /// <summary>
    /// Routes every edge around the boxes where they stand now, and places the
    /// labels. Called after a layout, and again whenever boxes are moved by
    /// hand, so an edge never runs behind a box it does not belong to.
    /// </summary>
    public static void Route(Diagram diagram)
    {
        foreach (var edge in diagram.Edges)
        {
            edge.Waypoints.Clear();
            edge.LabelCentre = null;
        }

        if (diagram.Nodes.Count == 0 || diagram.Edges.Count == 0)
            return;

        // MSAGL's y grows upwards; flip on the way in and back on the way out.
        var graph = new GeometryGraph();
        var shapes = new Dictionary<DiagramNode, Node>();
        foreach (var node in diagram.Nodes)
        {
            var centre = new Point(node.X + (node.Width / 2), -(node.Y + (node.Height / 2)));
            var shape = new Node(CurveFactory.CreateRectangle(node.Width, node.Height, centre), node);
            shapes[node] = shape;
            graph.Nodes.Add(shape);
        }

        foreach (var edge in diagram.Edges)
        {
            if (ReferenceEquals(edge.Source, edge.Target))
                continue;

            var geometryEdge = new Edge(shapes[edge.Source], shapes[edge.Target]) { UserData = edge };
            if (edge.Label is { Length: > 0 } text)
                geometryEdge.Label = new Label(LabelWidth(text), 14, geometryEdge);
            graph.Edges.Add(geometryEdge);
        }

        var settings = new SugiyamaLayoutSettings
        {
            EdgeRoutingSettings =
            {
                EdgeRoutingMode = EdgeRoutingMode.Spline,
                Padding = 6,
                PolylinePadding = 10,
            },
        };
        LayoutHelpers.RouteAndLabelEdges(graph, settings, graph.Edges, 0, null);

        foreach (var geometryEdge in graph.Edges)
        {
            if (geometryEdge.UserData is not DiagramEdge edge || geometryEdge.Curve is null)
                continue;

            var curve = geometryEdge.Curve;
            var samples = Math.Clamp((int)(curve.Length / 12), 8, 80);
            for (var i = 0; i <= samples; i++)
            {
                var point = curve[curve.ParStart + ((curve.ParEnd - curve.ParStart) * i / samples)];
                edge.Waypoints.Add((point.X, -point.Y));
            }

            if (geometryEdge.Label is { } label)
                edge.LabelCentre = (label.Center.X, -label.Center.Y);
        }
    }

    /// <summary>
    /// Pushes boxes apart until none overlaps another. A fresh layout needs no
    /// help; this is for boxes that grew after they were placed — measured
    /// larger than estimated, or restored from a sidecar written before a box
    /// gained a compartment. A box is moved down past whatever it overlaps,
    /// in reading order, so the picture keeps its shape.
    /// </summary>
    public static void RemoveOverlaps(Diagram diagram)
    {
        var placed = new List<DiagramNode>();
        foreach (var node in diagram.Nodes.OrderBy(n => n.Y).ThenBy(n => n.X))
        {
            for (var guard = 0; guard < diagram.Nodes.Count; guard++)
            {
                var blocker = placed.Find(p => Overlap(p, node));
                if (blocker is null)
                    break;
                node.Y = blocker.Y + blocker.Height + Clearance;
            }

            placed.Add(node);
        }
    }

    private static bool Overlap(DiagramNode a, DiagramNode b)
        => a.X < b.X + b.Width + Clearance && b.X < a.X + a.Width + Clearance
           && a.Y < b.Y + b.Height + Clearance && b.Y < a.Y + a.Height + Clearance;

    /// <summary>
    /// A layered layout puts every child of a hub in one layer, so a definition
    /// with thirty parts becomes one row thirty boxes wide and has to be shown
    /// at a tenth of its size. Any layer wider than a landscape picture of the
    /// diagram's content would be is wrapped into several rows, in the order
    /// the layout chose, and the layers below move down to make room.
    /// </summary>
    private static void WrapWideLayers(Diagram diagram)
    {
        var area = diagram.Nodes.Sum(n => (n.Width + BoxGap) * (n.Height + RowGap));
        var maxWidth = Math.Max(1400, Math.Sqrt(area * 1.8));

        // A layer is the nodes sharing a centre line; boxes of different
        // heights in one layer are still one layer.
        var layers = diagram.Nodes
            .GroupBy(n => Math.Round(n.Y + (n.Height / 2)))
            .OrderBy(g => g.Key)
            .Select(g => g.OrderBy(n => n.X).ToList())
            .ToList();

        var rows = new List<List<DiagramNode>>();
        var wrapped = false;
        var shift = 0.0;
        foreach (var layer in layers)
        {
            foreach (var node in layer)
                node.Y += shift;

            var width = layer.Sum(n => n.Width) + (BoxGap * (layer.Count - 1));
            if (width <= maxWidth)
            {
                rows.Add(layer);
                continue;
            }

            wrapped = true;
            var top = layer.Min(n => n.Y);
            var oldBottom = layer.Max(n => n.Y + n.Height);
            var row = new List<DiagramNode>();
            var x = 0.0;
            var y = top;
            var rowHeight = 0.0;
            foreach (var node in layer)
            {
                if (row.Count > 0 && x + node.Width > maxWidth)
                {
                    rows.Add(row);
                    row = [];
                    x = 0;
                    y += rowHeight + RowGap;
                    rowHeight = 0;
                }

                node.X = x;
                node.Y = y;
                row.Add(node);
                x += node.Width + BoxGap;
                rowHeight = Math.Max(rowHeight, node.Height);
            }

            rows.Add(row);
            shift += y + rowHeight - oldBottom;
        }

        if (!wrapped)
            return;

        // Centre every row, as a unit, on the new and narrower picture, so a
        // hub sits above the rows it fans out to.
        var centre = diagram.Nodes.Max(n => n.X + n.Width) / 2;
        foreach (var row in rows)
        {
            var rowCentre = (row.Min(n => n.X) + row.Max(n => n.X + n.Width)) / 2;
            foreach (var node in row)
                node.X += centre - rowCentre;
        }

        var minX = diagram.Nodes.Min(n => n.X);
        foreach (var node in diagram.Nodes)
            node.X -= minX;
    }

    /// <summary>A box as the canvas will draw it: header, then one line per feature.</summary>
    public static void Measure(DiagramNode node)
    {
        if (node.IsPseudo)
        {
            node.Width = node.Height = 26;
            return;
        }

        var caption = (node.Stereotype ?? string.Empty).Trim('«', '»').Length * CaptionCharacter;
        var title = node.Label.Length * TitleCharacter;
        var features = node.Features.Count == 0 ? 0 : node.Features.Max(f => f.Length) * MonoCharacter;

        node.Width = Math.Clamp(Math.Max(Math.Max(caption, title), features) + BoxChrome, 150, 460);
        node.Height = 2 + HeaderHeight + (node.Features.Count == 0 ? 0 : CompartmentChrome + (node.Features.Count * FeatureLine));
    }

    private static double LabelWidth(string text) => (text.Length * 6.7) + 8;
}
