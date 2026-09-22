using SysmlStudio.Model;

namespace SysmlStudio.Diagrams;

/// <summary>
/// <para>Turns part of the model into a diagram.</para>
/// <para>
/// Every node is an element the model declares and every edge is a relation it
/// writes down — a box that is not in the model is a picture of nothing. The
/// builders find their content by shape rather than by name, so an element
/// added to the model turns up in its diagram with no change here.
/// </para>
/// </summary>
public static class DiagramBuilder
{
    /// <summary>The diagrams that can be drawn of <paramref name="element"/>, in menu order.</summary>
    public static IReadOnlyList<DiagramKind> KindsFor(Element element)
    {
        var kinds = new List<DiagramKind>();
        if (element.Kind is "package" or "library package" or "model")
        {
            kinds.Add(DiagramKind.Definition);
            if (element.Descendants().Any(e => e.Kind is "requirement" or "requirement def" or "satisfy" or "verify"))
                kinds.Add(DiagramKind.Requirements);
        }

        if (element.IsDefinition || element.Kind is "part")
        {
            if (element.Children.Any(c => c.Kind is "part" or "ref" or "port" or "item"))
                kinds.Add(DiagramKind.Interconnection);
            kinds.Add(DiagramKind.Definition);
        }

        if (element.Kind is "action def" or "action" && HasFlow(element))
            kinds.Add(DiagramKind.ActionFlow);

        if (element.Kind is "state def" or "state")
            kinds.Add(DiagramKind.StateMachine);

        return [.. kinds.Distinct()];
    }

    private static bool HasFlow(Element element)
        => element.Children.Any(c => c.Kind is "action" or "succession" or "perform action");

    public static Diagram Build(DiagramKind kind, Element root) => kind switch
    {
        DiagramKind.Definition => Definition(root),
        DiagramKind.Interconnection => Interconnection(root),
        DiagramKind.Requirements => Requirements(root),
        DiagramKind.ActionFlow => ActionFlow(root),
        DiagramKind.StateMachine => StateMachine(root),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// The definitions directly inside a package (or the one definition asked
    /// for, with what it specializes and what specializes it), their features
    /// in a compartment, and the specialization and typing edges between them.
    /// </summary>
    private static Diagram Definition(Element root)
    {
        var diagram = new Diagram(DiagramKind.Definition, root);
        var wanted = root.IsDefinition
            ? Neighbourhood(root)
            : [.. root.Children.Where(c => c.IsDefinition)];

        foreach (var element in wanted)
            diagram.Nodes.Add(DefinitionNode(element));

        AddEdges(diagram, RelationKind.Specialization, RelationKind.Typing, RelationKind.Redefinition);
        return diagram;
    }

    /// <summary>A definition, what it specializes, and everything that specializes it.</summary>
    private static List<Element> Neighbourhood(Element definition)
    {
        var model = Root(definition);
        var neighbours = new List<Element> { definition };

        foreach (var relation in definition.Relations.Where(r => r.Kind == RelationKind.Specialization))
        {
            if (relation.Target is { } target)
                neighbours.Add(target);
        }

        var specializers = model.Descendants().Where(e => e.IsDefinition && e.Relations.Any(
            r => r.Kind == RelationKind.Specialization && ReferenceEquals(r.Target, definition)));
        neighbours.AddRange(specializers);

        return [.. neighbours.Distinct()];
    }

    private static DiagramNode DefinitionNode(Element element)
    {
        var node = new DiagramNode(element, element.DisplayName, $"«{element.Kind}»");
        foreach (var feature in element.Children.Where(c => c.Kind is "attribute" or "port" or "part" or "ref" or "item"))
        {
            var typing = feature.Relations.FirstOrDefault(r => r.Kind == RelationKind.Typing);
            var type = typing is null ? string.Empty : " : " + typing.TargetReference;
            node.Features.Add($"{feature.DisplayName}{type}{feature.Multiplicity}");
        }

        return node;
    }

    /// <summary>The parts and ports inside one definition, with the connections between them.</summary>
    private static Diagram Interconnection(Element root)
    {
        var diagram = new Diagram(DiagramKind.Interconnection, root);
        foreach (var child in root.Children.Where(c => c.Kind is "part" or "ref" or "port" or "item"))
        {
            var typing = child.Relations.FirstOrDefault(r => r.Kind == RelationKind.Typing);
            var label = typing is null ? child.DisplayName : $"{child.DisplayName} : {typing.TargetReference}";
            diagram.Nodes.Add(new DiagramNode(child, label, $"«{child.Kind}»"));
        }

        // Connections are written inside the definition and name their ends by
        // feature chain: "engine.shaft to wheels" hangs off the part "engine".
        foreach (var connector in root.Children.Where(c => c.Kind is "connection" or "interface" or "flow" or "allocation"))
        {
            foreach (var relation in connector.Relations)
            {
                var source = FindEnd(diagram, relation.Label);
                var target = FindEnd(diagram, relation.TargetReference);
                if (source is not null && target is not null)
                    diagram.Edges.Add(new DiagramEdge(source, target, relation.Kind, connector.Name));
            }
        }

        return diagram;
    }

    /// <summary>Matches a connector end — possibly a feature chain — to a node.</summary>
    private static DiagramNode? FindEnd(Diagram diagram, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        var head = reference.Trim().Split('.')[0].Split("::")[^1].Trim();
        return diagram.Nodes.Find(n => n.Element.Name == head);
    }

    /// <summary>Requirements, and everything that satisfies, verifies or is allocated to them.</summary>
    private static Diagram Requirements(Element root)
    {
        var diagram = new Diagram(DiagramKind.Requirements, root);
        var traceKinds = new[] { RelationKind.Satisfy, RelationKind.Verify, RelationKind.Allocate };

        foreach (var element in root.Descendants().Where(e => e.Kind is "requirement" or "requirement def"))
        {
            var node = new DiagramNode(element, element.ShortName ?? element.DisplayName, $"«{element.Kind}»");
            if (element.Documentation is { Length: > 0 } doc)
                node.Features.Add(Shorten(doc));
            diagram.Nodes.Add(node);
        }

        foreach (var element in root.Descendants())
        {
            foreach (var relation in element.Relations.Where(r => traceKinds.Contains(r.Kind)))
            {
                if (relation.Target is not { } target)
                    continue;

                var targetNode = diagram.NodeFor(target) ?? Add(diagram, target);
                var owner = TraceOwner(element);
                var sourceNode = diagram.NodeFor(owner) ?? Add(diagram, owner);
                diagram.Edges.Add(new DiagramEdge(sourceNode, targetNode, relation.Kind, relation.Label));
            }
        }

        return diagram;
    }

    /// <summary>
    /// The element a trace edge is drawn from: "satisfy R by X" is written as a
    /// usage whose own name says nothing, so the picture shows what owns it.
    /// </summary>
    private static Element TraceOwner(Element element)
        => element.Kind is "satisfy" or "verify" or "allocation" && element.Parent is { } parent ? parent : element;

    private static DiagramNode Add(Diagram diagram, Element element)
    {
        var node = new DiagramNode(element, element.DisplayName, $"«{element.Kind}»");
        diagram.Nodes.Add(node);
        return node;
    }

    /// <summary>An action's sub-actions, ordered by the successions between them.</summary>
    private static Diagram ActionFlow(Element root)
    {
        var diagram = new Diagram(DiagramKind.ActionFlow, root);
        foreach (var child in root.Children.Where(c => c.Kind is "action" or "perform action" or "event occurrence"))
            diagram.Nodes.Add(new DiagramNode(child, child.DisplayName, $"«{child.Kind}»"));

        foreach (var succession in root.Children.Where(c => c.Kind is "succession" or "succession flow"))
        {
            foreach (var relation in succession.Relations.Where(r => r.Kind is RelationKind.Succession or RelationKind.Flow))
            {
                var source = FindEnd(diagram, relation.Label);
                var target = FindEnd(diagram, relation.TargetReference);
                if (source is not null && target is not null)
                    diagram.Edges.Add(new DiagramEdge(source, target, RelationKind.Succession, succession.Name));
            }
        }

        return diagram;
    }

    /// <summary>A state definition's states and the transitions between them.</summary>
    private static Diagram StateMachine(Element root)
    {
        var diagram = new Diagram(DiagramKind.StateMachine, root);
        foreach (var child in root.Children.Where(c => c.Kind is "state" or "exhibit state"))
            diagram.Nodes.Add(new DiagramNode(child, child.DisplayName, $"«{child.Kind}»"));

        foreach (var transition in root.Children.Where(c => c.Kind == "transition"))
        {
            foreach (var relation in transition.Relations.Where(r => r.Kind == RelationKind.Transition))
            {
                var source = FindEnd(diagram, transition.Value);
                var target = FindEnd(diagram, relation.TargetReference);
                if (source is not null && target is not null)
                    diagram.Edges.Add(new DiagramEdge(source, target, RelationKind.Transition, relation.Label));
            }
        }

        return diagram;
    }

    /// <summary>Draws the relations between nodes the diagram already holds.</summary>
    private static void AddEdges(Diagram diagram, params RelationKind[] kinds)
    {
        foreach (var node in diagram.Nodes.ToList())
        {
            foreach (var relation in node.Element.Relations.Where(r => kinds.Contains(r.Kind)))
            {
                if (relation.Target is not { } target)
                    continue;

                var targetNode = diagram.NodeFor(target);
                if (targetNode is not null && !ReferenceEquals(targetNode, node))
                    diagram.Edges.Add(new DiagramEdge(node, targetNode, relation.Kind, relation.Label));
            }
        }
    }

    private static Element Root(Element element)
    {
        var root = element;
        while (root.Parent is { } parent)
            root = parent;
        return root;
    }

    private static string Shorten(string text, int length = 70)
        => text.Length <= length ? text : text[..length].TrimEnd() + "…";
}
