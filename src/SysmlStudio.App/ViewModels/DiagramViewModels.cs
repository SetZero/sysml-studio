using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
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
    public string? Stereotype => _node.Stereotype;
    public ObservableCollection<string> Features { get; } = new ObservableCollection<string>(node.Features);
    public double Width => _node.Width;
    public double Height => _node.Height;

    /// <summary>What the maturity keyword paints: one meaning, one colour.</summary>
    public string Maturity => _node.Maturity ?? "none";

    /// <summary>Where a connection meets this node: its centre.</summary>
    public Point Anchor => new(Location.X + (Width / 2), Location.Y + (Height / 2));

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
    public RelationKind Kind => _edge.Kind;

    /// <summary>What the arrow says it is, shown on hover and in the legend.</summary>
    public string Description => _edge.Label is { Length: > 0 } label
        ? $"{_edge.Kind}: {label}"
        : _edge.Kind.ToString();

    /// <summary>Trace relations are drawn dashed, the way UML draws a dependency.</summary>
    public bool IsDashed => Kind is RelationKind.Satisfy or RelationKind.Verify
        or RelationKind.Allocate or RelationKind.Dependency or RelationKind.Typing;

    public void Dispose()
    {
        Source.PropertyChanged -= OnEndMoved;
        Target.PropertyChanged -= OnEndMoved;
    }

    private void OnEndMoved(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DiagramNodeViewModel.Anchor))
        {
            OnPropertyChanged(nameof(SourceAnchor));
            OnPropertyChanged(nameof(TargetAnchor));
        }
    }
}

/// <summary>A diagram open in a tab.</summary>
public sealed class DiagramDocumentViewModel : ObservableObject
{
    public DiagramDocumentViewModel(Diagram diagram)
    {
        Diagram = diagram;
        DiagramLayout.Apply(diagram);

        var nodes = diagram.Nodes.ConvertAll(n => new DiagramNodeViewModel(n));
        Nodes = new ObservableCollection<DiagramNodeViewModel>(nodes);

        var byNode = new Dictionary<DiagramNode, DiagramNodeViewModel>();
        for (var i = 0; i < diagram.Nodes.Count; i++)
            byNode[diagram.Nodes[i]] = nodes[i];

        Connections = new ObservableCollection<DiagramConnectionViewModel>(
            diagram.Edges.ConvertAll(e => new DiagramConnectionViewModel(e, byNode[e.Source], byNode[e.Target])));
    }

    public Diagram Diagram { get; }
    public string Title => Diagram.Title;
    public ObservableCollection<DiagramNodeViewModel> Nodes { get; }
    public ObservableCollection<DiagramConnectionViewModel> Connections { get; }

    /// <summary>Lays the diagram out again, discarding wherever nodes were dragged to.</summary>
    public void Relayout()
    {
        DiagramLayout.Apply(Diagram);
        foreach (var node in Nodes)
            node.Location = new Point(FindX(node), FindY(node));
    }

    private double FindX(DiagramNodeViewModel node) => Diagram.NodeFor(node.Element)?.X ?? node.Location.X;

    private double FindY(DiagramNodeViewModel node) => Diagram.NodeFor(node.Element)?.Y ?? node.Location.Y;
}
