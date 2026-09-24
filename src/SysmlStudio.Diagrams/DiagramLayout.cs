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
/// Most of that is MSAGL's work. This class sizes the boxes, hands them over,
/// wraps a layer too wide to read, re-routes edges that cross others where a
/// short detour avoids it (<see cref="EdgeUntangler"/>), and turns MSAGL's
/// y-up coordinates into the y-down ones every canvas uses.
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

        // Each group of connected boxes is laid out on its own and the groups
        // are packed side by side, the way Graphviz does it. Laid out as one
        // graph, unrelated boxes share layers with related ones, and wrapping a
        // long layer can put a box a screen away from the box it points at.
        var components = ConnectedComponents(diagram);
        var blocks = new List<List<DiagramNode>>();
        var laid = new Dictionary<DiagramEdge, LaidRoute>();
        foreach (var component in components.Where(c => c.Count > 1))
        {
            LayOutComponent(diagram, component, laid);
            blocks.Add(component);
        }

        // Boxes related to nothing on the diagram go in one tidy grid at the end.
        var loners = components.Where(c => c.Count == 1).SelectMany(c => c).ToList();
        if (loners.Count > 0)
        {
            ArrangeGrid(loners);
            blocks.Add(loners);
        }

        var before = diagram.Nodes.ToDictionary(n => n, n => (n.X, n.Y));
        Pack(blocks);
        RemoveOverlaps(diagram);
        Route(diagram, KeepLaidRoutes(blocks, before, laid));
    }

    /// <summary>An edge's route and label as the layered layout left them, at the group's origin.</summary>
    private sealed record LaidRoute(List<(double X, double Y)> Points, (double X, double Y)? Label);

    /// <summary>
    /// The layered layout routes each edge through a slot it keeps free in
    /// every row the edge passes, and gives each label a place of its own, the
    /// way Graphviz does: those routes are kept, moved with their group. A
    /// group whose boxes did not all move together is routed afresh.
    /// </summary>
    private static HashSet<DiagramEdge> KeepLaidRoutes(
        List<List<DiagramNode>> blocks,
        Dictionary<DiagramNode, (double X, double Y)> before,
        Dictionary<DiagramEdge, LaidRoute> laid)
    {
        var kept = new HashSet<DiagramEdge>();
        foreach (var block in blocks)
        {
            var dx = block[0].X - before[block[0]].X;
            var dy = block[0].Y - before[block[0]].Y;
            if (block.Exists(n => Math.Abs(n.X - before[n].X - dx) > 0.5 || Math.Abs(n.Y - before[n].Y - dy) > 0.5))
                continue;

            var members = block.ToHashSet();
            foreach (var (edge, route) in laid.Where(r => members.Contains(r.Key.Source)))
            {
                edge.Waypoints.Clear();
                edge.Waypoints.AddRange(route.Points.Select(p => (p.X + dx, p.Y + dy)));
                edge.LabelCentre = route.Label is { } label ? (label.X + dx, label.Y + dy) : null;
                kept.Add(edge);
            }
        }

        return kept;
    }

    /// <summary>
    /// Lays out one connected group with MSAGL, at the origin, and keeps the
    /// routes and label places it chose unless a wide layer had to be wrapped.
    /// </summary>
    private static void LayOutComponent(Diagram diagram, List<DiagramNode> component, Dictionary<DiagramEdge, LaidRoute> laid)
    {
        var members = component.ToHashSet();
        var graph = new GeometryGraph();
        var shapes = new Dictionary<DiagramNode, Node>();
        foreach (var node in component)
        {
            var shape = new Node(CurveFactory.CreateRectangle(node.Width, node.Height, new Point()), node);
            shapes[node] = shape;
            graph.Nodes.Add(shape);
        }

        var edges = diagram.Edges.Where(e => members.Contains(e.Source) && members.Contains(e.Target)
                                             && !ReferenceEquals(e.Source, e.Target));
        foreach (var edge in edges)
            graph.Edges.Add(WithLabel(new Edge(shapes[edge.Source], shapes[edge.Target]) { UserData = edge }, edge));

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

        if (WrapWideLayers(component, [.. edges]))
            return;

        foreach (var geometryEdge in graph.Edges)
        {
            if (geometryEdge.UserData is not DiagramEdge edge || geometryEdge.Curve is null)
                continue;

            var points = Sample(geometryEdge.Curve).Select(p => (p.X - left, top - p.Y)).ToList();
            (double X, double Y)? label = geometryEdge.Label is { } l ? (l.Center.X - left, top - l.Center.Y) : null;
            laid[edge] = new LaidRoute(points, label);
        }
    }

    /// <summary>The edge with room for its label, when it has one.</summary>
    private static Edge WithLabel(Edge geometryEdge, DiagramEdge edge)
    {
        if (edge.Label is { Length: > 0 } text)
            geometryEdge.Label = new Label(LabelWidth(text), LabelHeight, geometryEdge);
        return geometryEdge;
    }

    /// <summary>The diagram's boxes grouped by the edges between them.</summary>
    private static List<List<DiagramNode>> ConnectedComponents(Diagram diagram)
    {
        var parent = diagram.Nodes.ToDictionary(n => n, n => n);
        DiagramNode Find(DiagramNode n)
        {
            while (!ReferenceEquals(parent[n], n))
                n = parent[n] = parent[parent[n]];
            return n;
        }

        foreach (var edge in diagram.Edges)
            parent[Find(edge.Source)] = Find(edge.Target);

        // Keep the diagram's own order inside each group, biggest group first.
        return [.. diagram.Nodes.GroupBy(Find).Select(g => g.ToList()).OrderByDescending(g => g.Count)];
    }

    /// <summary>Unrelated boxes in rows, as close to square as their sizes allow.</summary>
    private static void ArrangeGrid(List<DiagramNode> nodes)
    {
        var area = nodes.Sum(n => (n.Width + BoxGap) * (n.Height + RowGap));
        var maxWidth = Math.Max(600, Math.Sqrt(area * 1.6));
        double x = 0, y = 0, rowHeight = 0;
        foreach (var node in nodes)
        {
            if (x > 0 && x + node.Width > maxWidth)
            {
                x = 0;
                y += rowHeight + RowGap;
                rowHeight = 0;
            }

            node.X = x;
            node.Y = y;
            x += node.Width + BoxGap;
            rowHeight = Math.Max(rowHeight, node.Height);
        }
    }

    /// <summary>
    /// Packs the laid-out groups onto shelves: left to right, a new shelf when
    /// the picture would grow wider than a landscape page of this much content.
    /// </summary>
    private static void Pack(List<List<DiagramNode>> blocks)
    {
        const double blockGap = 80;
        var area = blocks.Sum(b => Width(b) * Height(b));
        var maxWidth = Math.Max(1400, Math.Sqrt(area * 1.8));

        double x = 0, y = 0, shelfHeight = 0;
        foreach (var block in blocks)
        {
            var width = Width(block);
            if (x > 0 && x + width > maxWidth)
            {
                x = 0;
                y += shelfHeight + blockGap;
                shelfHeight = 0;
            }

            var left = block.Min(n => n.X);
            var top = block.Min(n => n.Y);
            foreach (var node in block)
            {
                node.X += x - left;
                node.Y += y - top;
            }

            x += width + blockGap;
            shelfHeight = Math.Max(shelfHeight, Height(block));
        }

        static double Width(List<DiagramNode> b) => b.Max(n => n.X + n.Width) - b.Min(n => n.X);
        static double Height(List<DiagramNode> b) => b.Max(n => n.Y + n.Height) - b.Min(n => n.Y);
    }

    /// <summary>
    /// Routes every edge around the boxes where they stand now, and places the
    /// labels. Called after a layout, and again whenever boxes are moved by
    /// hand, so an edge never runs behind a box it does not belong to.
    /// </summary>
    public static void Route(Diagram diagram) => Route(diagram, []);

    /// <summary>Routes every edge but the <paramref name="kept"/> ones, which already have a route.</summary>
    private static void Route(Diagram diagram, HashSet<DiagramEdge> kept)
    {
        foreach (var edge in diagram.Edges.Where(e => !kept.Contains(e)))
        {
            edge.Waypoints.Clear();
            edge.LabelCentre = null;
        }

        if (diagram.Nodes.Count == 0 || diagram.Edges.Count == 0)
            return;

        RouteAround(diagram, kept);
        SpreadParallelEdges(diagram);
        KeepLabelsClear(diagram);
    }

    /// <summary>MSAGL's obstacle-avoiding router, for the edges that have no route.</summary>
    private static void RouteAround(Diagram diagram, HashSet<DiagramEdge> kept)
    {
        if (diagram.Edges.TrueForAll(kept.Contains))
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

        foreach (var edge in diagram.Edges.Where(e => !kept.Contains(e) && !ReferenceEquals(e.Source, e.Target)))
            graph.Edges.Add(WithLabel(new Edge(shapes[edge.Source], shapes[edge.Target]) { UserData = edge }, edge));

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
        Untangle(diagram, graph);

        foreach (var geometryEdge in graph.Edges)
        {
            if (geometryEdge.UserData is not DiagramEdge edge || geometryEdge.Curve is null)
                continue;

            foreach (var (x, y) in Sample(geometryEdge.Curve))
                edge.Waypoints.Add((x, -y));

            if (geometryEdge.Label is { } label)
                edge.LabelCentre = (label.Center.X, -label.Center.Y);
        }
    }

    /// <summary>
    /// Edges between the same two boxes leave both routers a few pixels
    /// apart, one line to the eye with their labels in a heap. They are fanned
    /// out to a readable distance in the middle, keeping their order and
    /// their ends, and their labels are placed again.
    /// </summary>
    private static void SpreadParallelEdges(Diagram diagram)
    {
        const double gap = 16;
        var index = new Dictionary<DiagramNode, int>();
        for (var i = 0; i < diagram.Nodes.Count; i++)
            index[diagram.Nodes[i]] = i;

        var groups = diagram.Edges
            .Where(e => !ReferenceEquals(e.Source, e.Target) && e.Waypoints.Count > 2)
            .GroupBy(e => index[e.Source] < index[e.Target] ? (index[e.Source], index[e.Target]) : (index[e.Target], index[e.Source]))
            .Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            // One normal for the whole group, from the first box to the second.
            var from = diagram.Nodes[group.Key.Item1];
            var to = diagram.Nodes[group.Key.Item2];
            var (ax, ay) = (from.X + (from.Width / 2), from.Y + (from.Height / 2));
            var (bx, by) = (to.X + (to.Width / 2), to.Y + (to.Height / 2));
            var length = Math.Max(1, Math.Sqrt(Math.Pow(bx - ax, 2) + Math.Pow(by - ay, 2)));
            var (nx, ny) = (-(by - ay) / length, (bx - ax) / length);

            double Side(DiagramEdge e)
            {
                var (mx, my) = PointAt(e.Waypoints, Lengths(e.Waypoints), Lengths(e.Waypoints)[^1] / 2);
                return ((mx - ax) * nx) + ((my - ay) * ny);
            }

            var ordered = group.OrderBy(Side).ToList();
            var centre = ordered.Average(Side);
            for (var i = 0; i < ordered.Count; i++)
            {
                var edge = ordered[i];
                var shift = centre + ((i - ((ordered.Count - 1) / 2.0)) * gap) - Side(edge);
                var lengths = Lengths(edge.Waypoints);
                var total = Math.Max(1, lengths[^1]);
                for (var p = 1; p < edge.Waypoints.Count - 1; p++)
                {
                    // Full in the middle, nothing at the ends, where the edge meets its boxes.
                    var bulge = Math.Sin(Math.PI * lengths[p] / total);
                    var (x, y) = edge.Waypoints[p];
                    edge.Waypoints[p] = (x + (nx * shift * bulge), y + (ny * shift * bulge));
                }

                edge.LabelCentre = null;
            }
        }
    }

    /// <summary>
    /// Places every label where it hides nothing: not on a box, where the box
    /// would cover it, not on another label, and where it can be, not across
    /// another edge. A label keeps the place a router gave it when that place
    /// is clear; otherwise it moves along its own edge, nearest the middle
    /// first. Where no place is clear of every edge, one clear of boxes and
    /// labels does.
    /// </summary>
    private static void KeepLabelsClear(Diagram diagram)
    {
        const double margin = 4;
        var boxes = diagram.Nodes.ConvertAll(n => new Box(n.X - margin, n.Y - margin, n.X + n.Width + margin, n.Y + n.Height + margin));
        var placed = new List<Box>();

        // Labels with a place first, so they keep it when they can.
        var labelled = diagram.Edges
            .Where(e => e.Label is { Length: > 0 } && e.Waypoints.Count > 1)
            .OrderBy(e => e.LabelCentre is null ? 1 : 0)
            .ToList();

        foreach (var edge in labelled)
        {
            var text = edge.Label!;
            var candidates = LabelCandidates(edge, text).ToList();
            if (edge.LabelCentre is { } current)
                candidates.Insert(0, current);

            var chosen = Pick(strict: true) ?? Pick(strict: false) ?? edge.LabelCentre ?? candidates[0];
            edge.LabelCentre = chosen;
            placed.Add(LabelBox(text, chosen));

            (double X, double Y)? Pick(bool strict)
            {
                foreach (var centre in candidates)
                {
                    var box = LabelBox(text, centre);
                    if (boxes.Exists(b => b.Overlaps(box)) || placed.Exists(p => p.Overlaps(box)))
                        continue;
                    if (strict && diagram.Edges.Exists(e => !ReferenceEquals(e, edge) && Crosses(e.Waypoints, box)))
                        continue;
                    return centre;
                }

                return null;
            }
        }
    }

    /// <summary>Whether any leg of the polyline passes through the box.</summary>
    private static bool Crosses(List<(double X, double Y)> points, Box box)
    {
        for (var i = 1; i < points.Count; i++)
        {
            if (SegmentHits(points[i - 1], points[i], box))
                return true;
        }

        return false;
    }

    /// <summary>Liang–Barsky: whether the segment from a to b meets the box.</summary>
    private static bool SegmentHits((double X, double Y) a, (double X, double Y) b, Box box)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        double enter = 0, leave = 1;
        foreach (var (p, q) in new[] { (-dx, a.X - box.Left), (dx, box.Right - a.X), (-dy, a.Y - box.Top), (dy, box.Bottom - a.Y) })
        {
            if (Math.Abs(p) < 1e-9)
            {
                if (q < 0)
                    return false;
                continue;
            }

            var t = q / p;
            if (p < 0)
                enter = Math.Max(enter, t);
            else
                leave = Math.Min(leave, t);
            if (enter > leave)
                return false;
        }

        return true;
    }

    /// <summary>The distance along the polyline to each of its points.</summary>
    private static double[] Lengths(List<(double X, double Y)> points)
    {
        var lengths = new double[points.Count];
        for (var i = 1; i < points.Count; i++)
            lengths[i] = lengths[i - 1] + Math.Sqrt(Math.Pow(points[i].X - points[i - 1].X, 2) + Math.Pow(points[i].Y - points[i - 1].Y, 2));
        return lengths;
    }

    /// <summary>Places along an edge for its label: on the line, then beside it, from the middle outwards.</summary>
    private static IEnumerable<(double X, double Y)> LabelCandidates(DiagramEdge edge, string text)
    {
        var points = edge.Waypoints;
        var lengths = Lengths(points);

        var total = lengths[^1];
        var aside = (LabelWidth(text) / 2) + 6;
        const double above = (LabelHeight / 2) + 5;
        foreach (var fraction in new[] { 0.5, 0.4, 0.6, 0.3, 0.7, 0.2, 0.8, 0.12, 0.88 })
        {
            var (x, y) = PointAt(points, lengths, total * fraction);
            yield return (x, y);
            yield return (x + aside, y);
            yield return (x - aside, y);
            yield return (x, y - above);
            yield return (x, y + above);
        }
    }

    private static (double X, double Y) PointAt(List<(double X, double Y)> points, double[] lengths, double distance)
    {
        var i = 1;
        while (i < points.Count - 1 && lengths[i] < distance)
            i++;

        var span = lengths[i] - lengths[i - 1];
        var t = span <= 0 ? 0 : (distance - lengths[i - 1]) / span;
        return (points[i - 1].X + ((points[i].X - points[i - 1].X) * t), points[i - 1].Y + ((points[i].Y - points[i - 1].Y) * t));
    }

    private static Box LabelBox(string text, (double X, double Y) centre)
    {
        var halfWidth = LabelWidth(text) / 2;
        const double halfHeight = LabelHeight / 2;
        return new Box(centre.X - halfWidth, centre.Y - halfHeight, centre.X + halfWidth, centre.Y + halfHeight);
    }

    /// <summary>An axis-aligned rectangle on the canvas, y down.</summary>
    private readonly record struct Box(double Left, double Top, double Right, double Bottom)
    {
        public bool Overlaps(Box other)
            => Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;
    }

    /// <summary>
    /// MSAGL routes every edge its own shortest way, crossings or not. Edges
    /// that cross others are routed again where a crossing costs extra length,
    /// and the labels are placed again along the routes that changed.
    /// </summary>
    private static void Untangle(Diagram diagram, GeometryGraph graph)
    {
        var index = new Dictionary<DiagramNode, int>();
        for (var i = 0; i < diagram.Nodes.Count; i++)
            index[diagram.Nodes[i]] = i;

        var routed = graph.Edges
            .Where(e => e.UserData is DiagramEdge && e.Curve is not null)
            .ToList();
        var routes = routed.ConvertAll(e =>
        {
            var edge = (DiagramEdge)e.UserData;
            return new EdgeUntangler.Route(index[edge.Source], index[edge.Target], [.. Sample(e.Curve).Select(p => (p.X, -p.Y))]);
        });

        EdgeUntangler.Untangle([.. diagram.Nodes.Select(n => (n.X, n.Y, n.Width, n.Height))], routes);
        if (!routes.Any(r => r.Changed))
            return;

        for (var i = 0; i < routes.Count; i++)
        {
            if (routes[i].Changed)
                routed[i].Curve = Rounded(routes[i].Points);
        }

        new EdgeLabelPlacement(graph).Run();
    }

    /// <summary>A polyline in the canvas's coordinates as an MSAGL curve with rounded corners.</summary>
    private static Curve Rounded(List<(double X, double Y)> points)
    {
        var polyline = SmoothedPolyline.FromPoints(points.Select(p => new Point(p.X, -p.Y)));
        for (var site = polyline.HeadSite.Next; site?.Next is not null; site = site.Next)
        {
            // Round each corner over at most this much of the legs either side,
            // so the curve stays clear of the box the corner goes round.
            const double reach = 16;
            site.PreviousBezierSegmentFitCoefficient = Math.Min(0.45, reach / (site.Point - site.Previous.Point).Length);
            site.NextBezierSegmentFitCoefficient = Math.Min(0.45, reach / (site.Next.Point - site.Point).Length);
        }

        return polyline.CreateCurve();
    }

    private static IEnumerable<(double X, double Y)> Sample(ICurve curve)
    {
        var samples = Math.Clamp((int)(curve.Length / 12), 8, 80);
        for (var i = 0; i <= samples; i++)
        {
            var point = curve[curve.ParStart + ((curve.ParEnd - curve.ParStart) * i / samples)];
            yield return (point.X, point.Y);
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
    /// Returns whether it moved anything.
    /// </summary>
    private static bool WrapWideLayers(List<DiagramNode> nodes, List<DiagramEdge> edges)
    {
        var area = nodes.Sum(n => (n.Width + BoxGap) * (n.Height + RowGap));
        var maxWidth = Math.Max(1400, Math.Sqrt(area * 1.8));

        // A layer is the nodes sharing a centre line; boxes of different
        // heights in one layer are still one layer.
        var layers = nodes
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

            // Only a hub's fan-out is wrapped: every box in the layer hangs off
            // one common box, so wrapping keeps each box next to its partner.
            // A layer mixing several parents keeps the order MSAGL chose, which
            // is what keeps related boxes near each other.
            var width = layer.Sum(n => n.Width) + (BoxGap * (layer.Count - 1));
            if (width <= maxWidth || !SharesAHub(layer, edges))
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
            return false;

        // Centre every row, as a unit, on the new and narrower picture, so a
        // hub sits above the rows it fans out to.
        var centre = nodes.Max(n => n.X + n.Width) / 2;
        foreach (var row in rows)
        {
            var rowCentre = (row.Min(n => n.X) + row.Max(n => n.X + n.Width)) / 2;
            foreach (var node in row)
                node.X += centre - rowCentre;
        }

        var minX = nodes.Min(n => n.X);
        foreach (var node in nodes)
            node.X -= minX;
        return true;
    }

    /// <summary>Whether one box outside the layer is connected to every box in it.</summary>
    private static bool SharesAHub(List<DiagramNode> layer, List<DiagramEdge> edges)
    {
        HashSet<DiagramNode>? common = null;
        foreach (var node in layer)
        {
            var neighbours = edges
                .Where(e => ReferenceEquals(e.Source, node) || ReferenceEquals(e.Target, node))
                .Select(e => ReferenceEquals(e.Source, node) ? e.Target : e.Source)
                .Where(n => !layer.Contains(n))
                .ToHashSet();
            if (common is null)
                common = neighbours;
            else
                common.IntersectWith(neighbours);
            if (common.Count == 0)
                return false;
        }

        return common is { Count: > 0 };
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

    private const double LabelHeight = 14;

    private static double LabelWidth(string text) => (text.Length * 6.7) + 8;
}
