using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using SysmlStudio.Diagrams;
using SysmlStudio.Model;

namespace SysmlStudio.App.ViewModels;

/// <summary>One box on the canvas.</summary>
public sealed partial class DiagramNodeViewModel(DiagramNode node) : ObservableObject
{
    private readonly DiagramNode _node = node;

    [ObservableProperty]
    public partial Point Location { get; set; } = new(node.X, node.Y);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public Element Element => _node.Element;
    public string Title => _node.Label;
    public bool IsPseudoNode => _node.IsPseudo;

    /// <summary>The element's kind in capitals, as the node's header shows it: "PART DEF".</summary>
    public string KindCaption => _node.Element.Kind.ToUpperInvariant();

    public string? ShortName => _node.Element.ShortName is { Length: > 0 } s ? $"‹{s}›" : null;
    public ObservableCollection<string> Features { get; } = new(node.Features);
    public bool HasFeatures => Features.Count > 0;
    public double Width => _node.Width;
    public double Height => _node.Height;

    public string Maturity => _node.Maturity ?? "none";

    public bool IsPseudo => _node.IsPseudo;
    public bool IsBox => !_node.IsPseudo;
    public bool IsStart => _node.Pseudo == "start";
    public bool IsDone => _node.Pseudo == "done";

    /// <summary>Where a connection aims: the node's centre.</summary>
    public Point Anchor => new(Location.X + (Width / 2), Location.Y + (Height / 2));

    /// <summary>Half the node's size; connections are cut back by it so heads land on the border.</summary>
    public Size HalfSize => new(Width / 2, Height / 2);

    partial void OnLocationChanged(Point value)
    {
        _node.X = value.X;
        _node.Y = value.Y;
        OnPropertyChanged(nameof(Anchor));
    }

    /// <summary>The box as drawn, where it differs from the estimate the layout started from.</summary>
    public void Resize(double width, double height)
    {
        _node.Width = width;
        _node.Height = height;
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(HalfSize));
        OnPropertyChanged(nameof(Anchor));
    }

    /// <summary>Takes the position the layout gave the node.</summary>
    public void SyncFromLayout() => Location = new Point(_node.X, _node.Y);

    /// <summary>The box's outline, for cutting a straight line back to its border.</summary>
    public Rect Bounds => new(Location.X, Location.Y, Width, Height);
}

/// <summary>
/// One arrow on the canvas. It is drawn along the route MSAGL found around the
/// boxes; while one of its boxes is being dragged the route is out of date, so
/// it is drawn straight from border to border until the drop re-routes it.
/// </summary>
public sealed class DiagramConnectionViewModel : ObservableObject, IDisposable
{
    private readonly DiagramEdge _edge;
    private bool _routeIsCurrent = true;

    public DiagramConnectionViewModel(DiagramEdge edge, DiagramNodeViewModel source, DiagramNodeViewModel target)
    {
        _edge = edge;
        Source = source;
        Target = target;
        source.PropertyChanged += OnEndMoved;
        target.PropertyChanged += OnEndMoved;
    }

    public DiagramNodeViewModel Source { get; }
    public DiagramNodeViewModel Target { get; }
    public string? Label => _edge.Label;
    public bool HasLabel => !string.IsNullOrWhiteSpace(_edge.Label);

    /// <summary>Whether the label is drawn; the diagram turns labels off when it is crowded.</summary>
    public bool ShowsLabel { get; set; }

    public RelationKind Kind => _edge.Kind;

    public string Description => _edge.Label is { Length: > 0 } label ? $"{_edge.Kind}: {label}" : _edge.Kind.ToString();

    /// <summary>Trace relations are drawn dashed, the way UML draws a dependency.</summary>
    public bool IsDashed => Kind is RelationKind.Satisfy or RelationKind.Verify
        or RelationKind.Allocate or RelationKind.Dependency or RelationKind.Typing or RelationKind.Flow;

    /// <summary>Specialization and redefinition get UML's hollow triangle.</summary>
    public bool IsGeneralization => Kind is RelationKind.Specialization or RelationKind.Redefinition;

    /// <summary>Composition is marked with a filled diamond at the owner's end.</summary>
    public bool IsComposition => Kind == RelationKind.Composition;

    /// <summary>An open arrowhead: the trace relations.</summary>
    public bool IsOpenHead => IsDashed;

    /// <summary>A filled arrowhead: everything else.</summary>
    public bool IsFilledHead => !IsGeneralization && !IsOpenHead;

    /// <summary>The points the line passes through, in canvas coordinates.</summary>
    private IReadOnlyList<Point> Points
    {
        get
        {
            if (_routeIsCurrent && _edge.Waypoints.Count >= 2)
                return _edge.Waypoints.ConvertAll(p => new Point(p.X, p.Y));

            var from = Source.Anchor;
            var to = Target.Anchor;
            return [Clip(Source.Bounds, to, from), Clip(Target.Bounds, from, to)];
        }
    }

    public Geometry Line
    {
        get
        {
            var points = Points;
            var geometry = new StreamGeometry();
            using var context = geometry.Open();
            context.BeginFigure(points[0], isFilled: false);
            for (var i = 1; i < points.Count; i++)
                context.LineTo(points[i]);
            context.EndFigure(isClosed: false);
            return geometry;
        }
    }

    /// <summary>The arrowhead at the target: a triangle, hollow or filled, or an open V.</summary>
    public Geometry Head
    {
        get
        {
            var points = Points;
            var tip = points[^1];
            var direction = Direction(points[points.Count - 2], tip);
            var size = IsGeneralization ? 13.0 : 10.0;
            var width = IsGeneralization ? 7.0 : 5.0;
            var back = tip - (direction * size);
            var normal = new Vector(-direction.Y, direction.X) * width;

            var geometry = new StreamGeometry();
            using var context = geometry.Open();
            context.BeginFigure(back + normal, isFilled: !IsOpenHead);
            context.LineTo(tip);
            context.LineTo(back - normal);
            context.EndFigure(isClosed: !IsOpenHead);
            return geometry;
        }
    }

    /// <summary>The composition diamond at the owner's end; empty for every other relation.</summary>
    public Geometry? Tail
    {
        get
        {
            if (!IsComposition)
                return null;

            var points = Points;
            var start = points[0];
            var direction = Direction(points[1], start);
            var normal = new Vector(-direction.Y, direction.X) * 5;
            var middle = start - (direction * 8);

            var geometry = new StreamGeometry();
            using var context = geometry.Open();
            context.BeginFigure(start, isFilled: true);
            context.LineTo(middle + normal);
            context.LineTo(start - (direction * 16));
            context.LineTo(middle - normal);
            context.EndFigure(isClosed: true);
            return geometry;
        }
    }

    /// <summary>The label's top left: where MSAGL placed it, or beside the middle of the line.</summary>
    public Point LabelLocation
    {
        get
        {
            var width = ((_edge.Label?.Length ?? 0) * 6.7) + 8;
            if (_routeIsCurrent && _edge.LabelCentre is { } centre)
                return new Point(centre.X - (width / 2), centre.Y - 8);

            var points = Points;
            var middle = points[points.Count / 2];
            return new Point(middle.X + 6, middle.Y - 16);
        }
    }

    /// <summary>The route was recomputed: draw along it again.</summary>
    public void Rerouted()
    {
        _routeIsCurrent = true;
        Refresh();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Line));
        OnPropertyChanged(nameof(Head));
        OnPropertyChanged(nameof(Tail));
        OnPropertyChanged(nameof(LabelLocation));
    }

    public void Dispose()
    {
        Source.PropertyChanged -= OnEndMoved;
        Target.PropertyChanged -= OnEndMoved;
    }

    private void OnEndMoved(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(DiagramNodeViewModel.Anchor))
            return;

        _routeIsCurrent = false;
        Refresh();
    }

    private static Vector Direction(Point from, Point to)
    {
        var vector = to - from;
        var length = Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y));
        return length < 0.001 ? new Vector(1, 0) : vector / length;
    }

    /// <summary>Where the line from <paramref name="outside"/> to the box's centre crosses its border.</summary>
    private static Point Clip(Rect box, Point outside, Point centre)
    {
        var dx = outside.X - centre.X;
        var dy = outside.Y - centre.Y;
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
            return centre;

        var scaleX = Math.Abs(dx) < 0.001 ? double.MaxValue : box.Width / 2 / Math.Abs(dx);
        var scaleY = Math.Abs(dy) < 0.001 ? double.MaxValue : box.Height / 2 / Math.Abs(dy);
        var scale = Math.Min(Math.Min(scaleX, scaleY), 1);
        return new Point(centre.X + (dx * scale), centre.Y + (dy * scale));
    }
}

/// <summary>A diagram open as a document tab.</summary>
public sealed partial class DiagramDocumentViewModel : Document
{
    public DiagramDocumentViewModel(Diagram diagram, bool laidOut = false)
    {
        Diagram = diagram;
        _restored = laidOut;
        if (!laidOut)
        {
            DiagramLayout.Apply(diagram);
        }
        else
        {
            // Positions came from the sidecar: route around them where they are.
            DiagramLayout.RemoveOverlaps(diagram);
            DiagramLayout.Route(diagram);
        }

        Id = $"diagram:{diagram.Kind}:{diagram.Root.QualifiedName}";
        Title = $"{diagram.Root.DisplayName} · {KindName(diagram.Kind)}";
        CanFloat = true;

        var nodes = diagram.Nodes.ConvertAll(n => new DiagramNodeViewModel(n));
        Nodes = new ObservableCollection<DiagramNodeViewModel>(nodes);

        var byNode = new Dictionary<DiagramNode, DiagramNodeViewModel>();
        for (var i = 0; i < diagram.Nodes.Count; i++)
            byNode[diagram.Nodes[i]] = nodes[i];

        Connections = new ObservableCollection<DiagramConnectionViewModel>(
            diagram.Edges.ConvertAll(e => new DiagramConnectionViewModel(e, byNode[e.Source], byNode[e.Target])));

        foreach (var node in nodes)
        {
            node.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DiagramNodeViewModel.IsSelected) && node.IsSelected && !node.IsPseudoNode)
                    ElementClicked?.Invoke(node.Element);
            };
        }

        // Past two dozen edges the labels crowd each other; the tooltip still names each one.
        var labelled = Connections.Count <= 24;
        foreach (var connection in Connections)
            connection.ShowsLabel = labelled && connection.HasLabel;
    }

    public Diagram Diagram { get; }

    /// <summary>Told when a box on the canvas is selected; the window selects its element everywhere.</summary>
    public Action<Element>? ElementClicked { get; init; }

    public ObservableCollection<DiagramNodeViewModel> Nodes { get; }
    public ObservableCollection<DiagramConnectionViewModel> Connections { get; }


    /// <summary>After a drag, every edge is routed again around the boxes where they now stand.</summary>
    [RelayCommand]
    private void DragCompleted()
    {
        PushPositions();
        DiagramLayout.Route(Diagram);
        foreach (var connection in Connections)
            connection.Rerouted();
    }

    private readonly bool _restored;
    private bool _measured;

    /// <summary>
    /// The view has drawn the boxes and knows their real sizes. Where they are
    /// bigger than the layout's estimate the diagram is laid out again with the
    /// real sizes, or, for positions the user left in the sidecar, pushed apart
    /// just enough; either way the edges are routed again. Runs once.
    /// </summary>
    public bool ApplyMeasuredSizes(IReadOnlyDictionary<DiagramNodeViewModel, Size> sizes)
    {
        if (_measured)
            return false;
        _measured = true;

        var changed = false;
        foreach (var (node, size) in sizes)
        {
            if (node.IsPseudoNode || (Math.Abs(size.Height - node.Height) < 3 && Math.Abs(size.Width - node.Width) < 3))
                continue;
            node.Resize(Math.Max(size.Width, node.Width), Math.Max(size.Height, node.Height));
            changed = true;
        }

        if (!changed)
            return false;

        PushPositions();
        if (_restored)
        {
            DiagramLayout.RemoveOverlaps(Diagram);
            DiagramLayout.Route(Diagram);
        }
        else
        {
            DiagramLayout.Apply(Diagram, measure: false);
        }

        foreach (var node in Nodes)
            node.SyncFromLayout();
        foreach (var connection in Connections)
            connection.Rerouted();
        return true;
    }

    /// <summary>The line above the canvas: what the diagram is of and how much is in it.</summary>
    public string Breadcrumb
    {
        get
        {
            var (nodes, edges) = Diagram.Kind switch
            {
                DiagramKind.Interconnection => ("parts", "connections"),
                DiagramKind.Requirements => ("elements", "trace links"),
                DiagramKind.ActionFlow => ("actions", "successions"),
                DiagramKind.StateMachine => ("states", "transitions"),
                _ => ("definitions", "relations"),
            };
            return $"{KindName(Diagram.Kind).ToLowerInvariant()}  ›  {Diagram.Root.QualifiedName}  ·  "
                + $"{Nodes.Count} {nodes}, {Connections.Count} {edges}";
        }
    }

    /// <summary>What the toolbox adds to, or draws on, this kind of diagram.</summary>
    public IReadOnlyList<ToolboxItem> Toolbox => Diagram.Kind switch
    {
        DiagramKind.Interconnection => [new("Part", "part"), new("Port", "port"), new("Item", "item"), new("Connect", Relation: "Connect"), new("Flow", Relation: "Flow")],
        DiagramKind.Requirements => [new("Requirement", "requirement"), new("Requirement def", "requirement def"), new("Satisfy", Relation: "Satisfy"), new("Dependency", Relation: "Dependency"), new("Allocate", Relation: "Allocate")],
        DiagramKind.ActionFlow => [new("Action", "action"), new("Succession", Relation: "Succession")],
        DiagramKind.StateMachine => [new("State", "state"), new("Transition", Relation: "Transition")],
        _ => [new("Part def", "part def"), new("Part", "part"), new("Port def", "port def"), new("Attribute", "attribute"), new("Specialization", Relation: "Specialization"), new("Composition", Relation: "Composition"), new("Dependency", Relation: "Dependency")],
    };

    [ObservableProperty]
    public partial double Zoom { get; set; } = 1;

    public string ZoomText => $"{Math.Round(Zoom * 100)} %";

    /// <summary>Set by the view: frames every node.</summary>
    public Action? FitRequested { get; set; }

    /// <summary>Set by the view: renders the canvas to a PNG file.</summary>
    public Action<string>? RenderPng { get; set; }

    partial void OnZoomChanged(double value) => OnPropertyChanged(nameof(ZoomText));

    [RelayCommand]
    private void ZoomIn() => Zoom = Math.Min(4, Zoom * 1.2);

    [RelayCommand]
    private void ZoomOut() => Zoom = Math.Max(0.1, Zoom / 1.2);

    [RelayCommand]
    public void Fit() => FitRequested?.Invoke();

    /// <summary>Copies wherever the nodes were dragged to back into the diagram.</summary>
    public void PushPositions()
    {
        foreach (var node in Nodes)
        {
            if (Diagram.NodeFor(node.Element) is { } target)
            {
                target.X = node.Location.X;
                target.Y = node.Location.Y;
            }
        }
    }

    /// <summary>Lays the diagram out again, discarding wherever nodes were dragged to.</summary>
    [RelayCommand]
    public void Relayout()
    {
        DiagramLayout.Apply(Diagram, measure: !_measured);
        foreach (var node in Nodes)
            node.SyncFromLayout();
        foreach (var connection in Connections)
            connection.Rerouted();

        Fit();
    }

    public static string KindName(DiagramKind kind) => kind switch
    {
        DiagramKind.Interconnection => "Interconnection",
        DiagramKind.Requirements => "Requirements",
        DiagramKind.ActionFlow => "Action flow",
        DiagramKind.StateMachine => "State machine",
        _ => "Definition",
    };
}

/// <summary>A toolbox entry: an element kind to add, or a relation to draw between two boxes.</summary>
public sealed record ToolboxItem(string Label, string? Kind = null, string? Relation = null)
{
    public bool IsRelation => Relation is not null;
}
