using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
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
}

/// <summary>One arrow on the canvas, following its two nodes as they move.</summary>
public sealed class DiagramConnectionViewModel : ObservableObject, IDisposable
{
    private readonly DiagramEdge _edge;

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
    public Point SourceAnchor => Source.Anchor;
    public Point TargetAnchor => Target.Anchor;
    public string? Label => _edge.Label;
    public bool HasLabel => !string.IsNullOrWhiteSpace(_edge.Label);

    /// <summary>Where the label sits: beside the middle of the line.</summary>
    public Point LabelLocation => new(((SourceAnchor.X + TargetAnchor.X) / 2) + 6, ((SourceAnchor.Y + TargetAnchor.Y) / 2) - 9);

    /// <summary>Tells the canvas both ends may have moved.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(SourceAnchor));
        OnPropertyChanged(nameof(TargetAnchor));
        OnPropertyChanged(nameof(LabelLocation));
    }
    public RelationKind Kind => _edge.Kind;

    public string Description => _edge.Label is { Length: > 0 } label ? $"{_edge.Kind}: {label}" : _edge.Kind.ToString();

    /// <summary>Trace relations are drawn dashed, the way UML draws a dependency.</summary>
    public bool IsDashed => Kind is RelationKind.Satisfy or RelationKind.Verify
        or RelationKind.Allocate or RelationKind.Dependency or RelationKind.Typing or RelationKind.Flow;

    /// <summary>Specialization and redefinition get UML's hollow triangle.</summary>
    public bool IsGeneralization => Kind is RelationKind.Specialization or RelationKind.Redefinition;

    /// <summary>Composition is marked at the owner's end.</summary>
    public bool IsComposition => Kind == RelationKind.Composition;

    public void Dispose()
    {
        Source.PropertyChanged -= OnEndMoved;
        Target.PropertyChanged -= OnEndMoved;
    }

    private void OnEndMoved(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DiagramNodeViewModel.Anchor))
            Refresh();
    }
}

/// <summary>A diagram open as a document tab.</summary>
public sealed partial class DiagramDocumentViewModel : Document
{
    public DiagramDocumentViewModel(Diagram diagram, bool laidOut = false)
    {
        Diagram = diagram;
        if (!laidOut)
            DiagramLayout.Apply(diagram);

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
    }

    public Diagram Diagram { get; }
    public ObservableCollection<DiagramNodeViewModel> Nodes { get; }
    public ObservableCollection<DiagramConnectionViewModel> Connections { get; }

    /// <summary>The connections that carry a label, drawn on the canvas's decorator layer.</summary>
    public IEnumerable<DiagramConnectionViewModel> Labels
        => Connections.Count <= 24 ? Connections.Where(c => c.HasLabel) : [];

    /// <summary>After a drag, every line is re-anchored, whatever the canvas reported on the way.</summary>
    [RelayCommand]
    private void DragCompleted()
    {
        foreach (var connection in Connections)
            connection.Refresh();
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

    /// <summary>What the toolbox would drop onto this kind of diagram.</summary>
    public IReadOnlyList<string> Toolbox => Diagram.Kind switch
    {
        DiagramKind.Interconnection => ["Part", "Port", "Interface", "Connect", "Flow"],
        DiagramKind.Requirements => ["Requirement", "Satisfy", "Verify", "Allocate"],
        DiagramKind.ActionFlow => ["Action", "Succession", "Flow"],
        DiagramKind.StateMachine => ["State", "Transition"],
        _ => ["Part def", "Part", "Port", "Interface", "Attribute", "Specialization"],
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
        DiagramLayout.Apply(Diagram);
        foreach (var node in Nodes)
        {
            if (Diagram.NodeFor(node.Element) is { } laidOut)
                node.Location = new Point(laidOut.X, laidOut.Y);
        }

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
