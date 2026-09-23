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
/// <para>
/// Relations are drawn where a reader expects them, which is not always where
/// they are written: "part kernel : Kernel" is written on the part but drawn as
/// a composition from the definition that owns the part to Kernel;
/// "dependency from a to b" is its own statement but drawn from a to b.
/// </para>
/// </summary>
public static class DiagramBuilder
{
    private static readonly HashSet<string> StructuralFeatures = ["part", "item", "ref", "port", "occurrence"];
    private static readonly HashSet<string> RequirementKinds = ["requirement", "requirement def", "concern", "concern def"];
    private static readonly HashSet<string> CaseKinds = ["verification case", "verification case def", "use case", "use case def", "analysis case", "analysis case def"];

    /// <summary>The diagrams that can be drawn of <paramref name="element"/>, most telling first.</summary>
    public static IReadOnlyList<DiagramKind> KindsFor(Element element)
    {
        var kinds = new List<DiagramKind>();

        if (IsPackage(element))
        {
            if (element.Children.Any(c => c.IsDefinition || IsTypedUsage(c)))
                kinds.Add(DiagramKind.Definition);
            if (element.Descendants().Any(e => RequirementKinds.Contains(e.Kind)))
                kinds.Add(DiagramKind.Requirements);
            return kinds;
        }

        if (RequirementKinds.Contains(element.Kind) || CaseKinds.Contains(element.Kind))
            kinds.Add(DiagramKind.Requirements);

        if (element.Kind is "state def" or "state" or "exhibit state" && element.Children.Any(IsState))
            kinds.Add(DiagramKind.StateMachine);

        if (element.Kind is "action def" or "action" or "perform action" && element.Children.Any(IsAction))
            kinds.Add(DiagramKind.ActionFlow);

        if ((element.IsDefinition || IsTypedUsage(element))
            && (element.Children.Any(IsConnector) || element.Children.Count(IsStructural) >= 2))
        {
            kinds.Add(DiagramKind.Interconnection);
        }

        if (element.IsDefinition || IsTypedUsage(element))
            kinds.Add(DiagramKind.Definition);

        return kinds;
    }

    public static Diagram Build(DiagramKind kind, Element root) => kind switch
    {
        DiagramKind.Definition => Definition(root),
        DiagramKind.Interconnection => Interconnection(root),
        DiagramKind.Requirements => Requirements(root),
        DiagramKind.ActionFlow => ActionFlow(root),
        DiagramKind.StateMachine => StateMachine(root),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    // ----- definition ---------------------------------------------------------

    /// <summary>
    /// For a package: its definitions and typed usages. For one definition: the
    /// definition, what it specializes and what specializes it, what it is made
    /// of and what is made of it. Edges are specialization, composition and typing.
    /// </summary>
    private static Diagram Definition(Element root)
    {
        var diagram = new Diagram(DiagramKind.Definition, root);

        IEnumerable<Element> wanted = IsPackage(root)
            ? root.Children.Where(c => c.IsDefinition || IsTypedUsage(c))
            : Neighbourhood(root);

        var onDiagram = wanted.Distinct().ToList();
        foreach (var element in onDiagram)
            diagram.Nodes.Add(DefinitionNode(element, onDiagram));

        foreach (var element in diagram.Nodes.ConvertAll(n => n.Element))
        {
            foreach (var relation in element.Relations.Where(r => r.Kind is RelationKind.Specialization or RelationKind.Typing))
                Connect(diagram, element, relation.Target, relation.Kind);

            // Composition: the part is written inside the definition; the arrow
            // runs from the definition to what the part is typed by.
            foreach (var feature in element.Children.Where(IsStructural))
            {
                foreach (var typing in feature.Relations.Where(r => r.Kind == RelationKind.Typing))
                    Connect(diagram, element, typing.Target, RelationKind.Composition, feature.DisplayName + feature.Multiplicity);
            }
        }

        return diagram;
    }

    /// <summary>One definition and everything one relation away from it.</summary>
    private static List<Element> Neighbourhood(Element centre)
    {
        var model = RootOf(centre);
        var set = new List<Element> { centre };

        // What it is typed by, specializes, and is made of.
        set.AddRange(centre.Relations
            .Where(r => r.Kind is RelationKind.Specialization or RelationKind.Typing)
            .Select(r => r.Target).OfType<Element>());
        set.AddRange(centre.Children.Where(IsStructural)
            .SelectMany(f => f.Relations.Where(r => r.Kind == RelationKind.Typing))
            .Select(r => r.Target).OfType<Element>());

        // What specializes it, and what is made of it.
        foreach (var other in model.Descendants().Where(e => e.IsDefinition))
        {
            if (other.Relations.Any(r => r.Kind == RelationKind.Specialization && ReferenceEquals(r.Target, centre)))
                set.Add(other);
            else if (other.Children.Where(IsStructural).Any(f => f.Relations.Any(r => r.Kind == RelationKind.Typing && ReferenceEquals(r.Target, centre))))
                set.Add(other);
        }

        return [.. set.Distinct()];
    }

    private static DiagramNode DefinitionNode(Element element, IReadOnlyCollection<Element> onDiagram)
    {
        var node = new DiagramNode(element, element.DisplayName, $"«{element.Kind}»");
        if (!element.IsDefinition && element.Relations.FirstOrDefault(r => r.Kind == RelationKind.Typing) is { } type)
            node = new DiagramNode(element, $"{element.DisplayName} : {type.TargetReference.Trim()}", $"«{element.Kind}»");

        foreach (var feature in element.Children.Where(c => c.Kind is "attribute" || StructuralFeatures.Contains(c.Kind)))
        {
            if (feature.Name is null)
                continue;
            var typing = feature.Relations.FirstOrDefault(r => r.Kind == RelationKind.Typing);

            // UML shows a property either in the compartment or as an arrow, not both.
            if (IsStructural(feature) && typing?.Target is { } shownAsArrow && onDiagram.Contains(shownAsArrow) && !ReferenceEquals(shownAsArrow, element))
                continue;

            var typeText = typing is null ? string.Empty : " : " + typing.TargetReference.Trim();
            node.Features.Add($"{feature.DisplayName}{typeText}{feature.Multiplicity}");
        }

        foreach (var behaviour in element.Children.Where(c => IsAction(c) || c.Kind is "state" or "exhibit state" or "perform action"))
            node.Features.Add($"{behaviour.DisplayName}()");

        return node;
    }

    // ----- interconnection ----------------------------------------------------

    /// <summary>The parts and ports inside one definition, with the connections between them.</summary>
    private static Diagram Interconnection(Element root)
    {
        var diagram = new Diagram(DiagramKind.Interconnection, root);
        foreach (var child in root.Children.Where(IsStructural))
        {
            var typing = child.Relations.FirstOrDefault(r => r.Kind == RelationKind.Typing);
            var label = typing is null ? child.DisplayName : $"{child.DisplayName} : {typing.TargetReference.Trim()}";
            diagram.Nodes.Add(new DiagramNode(child, label + child.Multiplicity, $"«{child.Kind}»"));
        }

        // Connections name their ends by feature chain: "engine.shaft to wheels"
        // is drawn between the parts engine and wheels.
        foreach (var connector in root.Children.Where(IsConnector))
        {
            foreach (var relation in connector.Relations)
            {
                var source = FindEnd(diagram, relation.OriginReference);
                var target = FindEnd(diagram, relation.TargetReference);
                if (source is not null && target is not null && !ReferenceEquals(source, target))
                    diagram.Edges.Add(new DiagramEdge(source, target, relation.Kind, connector.Name));
            }
        }

        return diagram;
    }

    /// <summary>Matches a connector end — possibly a feature chain — to a node by its head.</summary>
    private static DiagramNode? FindEnd(Diagram diagram, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        var head = reference.Trim().Split('.')[0].Split("::")[^1].Trim();
        return diagram.Nodes.Find(n => !n.IsPseudo && n.Element.Name == head);
    }

    // ----- requirements -------------------------------------------------------

    /// <summary>
    /// For a package: every requirement in it, and the dependency, satisfy,
    /// verify and allocate links between them and what traces to them. For one
    /// requirement or case: it, and everything one of those links away.
    /// </summary>
    private static Diagram Requirements(Element root)
    {
        var diagram = new Diagram(DiagramKind.Requirements, root);
        var model = RootOf(root);
        var traces = TraceLinks(model).ToList();

        if (IsPackage(root))
        {
            foreach (var element in root.Descendants().Where(e => RequirementKinds.Contains(e.Kind) && e.Name is not null && !IsNestedRequirement(e)))
                RequirementNode(diagram, element);

            foreach (var (kind, from, to, label) in traces)
            {
                var toNode = diagram.NodeFor(to);
                var fromNode = diagram.NodeFor(from);
                if (toNode is null && fromNode is null)
                    continue;

                // A link to a requirement on the diagram brings its other end with it.
                toNode ??= Contains(root, to) ? RequirementNode(diagram, to) : null;
                fromNode ??= RequirementNode(diagram, from);
                if (toNode is not null && !ReferenceEquals(fromNode, toNode))
                    diagram.Edges.Add(new DiagramEdge(fromNode, toNode, kind, label));
            }

            return diagram;
        }

        var centre = RequirementNode(diagram, root);
        foreach (var child in root.Children.Where(c => RequirementKinds.Contains(c.Kind) && c.Name is not null))
            diagram.Edges.Add(new DiagramEdge(centre, RequirementNode(diagram, child), RelationKind.Composition));

        foreach (var (kind, from, to, label) in traces)
        {
            if (!ReferenceEquals(from, root) && !ReferenceEquals(to, root))
                continue;

            var fromNode = diagram.NodeFor(from) ?? RequirementNode(diagram, from);
            var toNode = diagram.NodeFor(to) ?? RequirementNode(diagram, to);
            if (!ReferenceEquals(fromNode, toNode))
                diagram.Edges.Add(new DiagramEdge(fromNode, toNode, kind, label));
        }

        if (root.Relations.FirstOrDefault(r => r.Kind == RelationKind.Typing)?.Target is { } type)
            diagram.Edges.Add(new DiagramEdge(centre, RequirementNode(diagram, type), RelationKind.Typing));

        return diagram;
    }

    /// <summary>
    /// Every trace link in the model as (kind, from, to): dependencies from
    /// client to supplier, satisfy from the satisfying element to the
    /// requirement, verify from the verification case, allocate from end to end.
    /// </summary>
    private static IEnumerable<(RelationKind Kind, Element From, Element To, string? Label)> TraceLinks(Element model)
    {
        foreach (var element in model.Descendants())
        {
            foreach (var relation in element.Relations)
            {
                if (relation.Kind is not (RelationKind.Dependency or RelationKind.Satisfy or RelationKind.Verify or RelationKind.Allocate))
                    continue;
                if (relation.Target is not { } to)
                    continue;

                var from = relation.OriginReference is null ? TraceOwner(element) : relation.Origin;
                if (from is not null && !ReferenceEquals(from, to))
                    yield return (relation.Kind, from, to, element.Name);
            }
        }
    }

    /// <summary>
    /// The element a trace link is drawn from when the link names only its
    /// target: "verify R" inside a verification case's objective is drawn from
    /// the case, not from the anonymous objective.
    /// </summary>
    private static Element TraceOwner(Element element)
    {
        var owner = element;
        while (owner.Parent is { } parent
               && (owner.Name is null || owner.Kind is "satisfy" or "verify" or "allocation" or "objective requirement" or "subject"))
        {
            owner = parent;
        }

        return owner;
    }

    private static DiagramNode RequirementNode(Diagram diagram, Element element)
    {
        if (diagram.NodeFor(element) is { } existing)
            return existing;

        var label = element.ShortName is { } shortName && element.Name is { } name ? $"{name}  ‹{shortName}›" : element.DisplayName;
        var node = new DiagramNode(element, label, $"«{element.Kind}»");
        if (element.Documentation is { Length: > 0 } doc)
            node.Features.Add(Shorten(doc));
        diagram.Nodes.Add(node);
        return node;
    }

    /// <summary>A requirement inside another requirement is drawn inside its owner's diagram, not beside it.</summary>
    private static bool IsNestedRequirement(Element element)
        => element.Parent is { } parent && RequirementKinds.Contains(parent.Kind);

    // ----- action flow --------------------------------------------------------

    /// <summary>An action's steps, with start and done, in the order its successions give.</summary>
    private static Diagram ActionFlow(Element root)
    {
        var diagram = new Diagram(DiagramKind.ActionFlow, root);
        foreach (var child in root.Children.Where(IsAction))
            diagram.Nodes.Add(new DiagramNode(child, ActionLabel(child), $"«{child.Kind}»"));

        var successions = root.Relations.Where(r => r.Kind == RelationKind.Succession)
            .Concat(root.Children.Where(c => c.Kind is "succession" or "succession flow")
                .SelectMany(c => c.Relations.Where(r => r.Kind is RelationKind.Succession or RelationKind.Flow)));

        foreach (var relation in successions)
        {
            var from = ActionEnd(diagram, root, relation.Origin, relation.OriginReference);
            var to = ActionEnd(diagram, root, relation.Target, relation.TargetReference);
            if (from is not null && to is not null && !ReferenceEquals(from, to))
                diagram.Edges.Add(new DiagramEdge(from, to, RelationKind.Succession));
        }

        return diagram;
    }

    private static string ActionLabel(Element action)
        => action.Relations.FirstOrDefault(r => r.Kind == RelationKind.Typing) is { } type
            ? $"{action.DisplayName} : {type.TargetReference.Trim()}"
            : action.DisplayName;

    /// <summary>A succession end: a step on the diagram, or the start and done nodes.</summary>
    private static DiagramNode? ActionEnd(Diagram diagram, Element root, Element? resolved, string? reference)
    {
        if (resolved is not null && diagram.NodeFor(resolved) is { } node)
            return node;

        var name = reference?.Trim();
        if (name is "start" or "done")
        {
            if (diagram.PseudoNode(name) is { } pseudo)
                return pseudo;
            var created = new DiagramNode(root, name, null, name);
            diagram.Nodes.Add(created);
            return created;
        }

        return FindEnd(diagram, name);
    }

    // ----- state machine ------------------------------------------------------

    /// <summary>A state definition's states and the transitions between them.</summary>
    private static Diagram StateMachine(Element root)
    {
        var diagram = new Diagram(DiagramKind.StateMachine, root);
        foreach (var child in root.Children.Where(IsState))
            diagram.Nodes.Add(new DiagramNode(child, child.DisplayName, $"«{child.Kind}»"));

        foreach (var transition in root.Children.Where(c => c.Kind == "transition"))
        {
            foreach (var relation in transition.Relations.Where(r => r.Kind == RelationKind.Transition))
            {
                var source = (relation.Origin is { } o ? diagram.NodeFor(o) : null) ?? FindEnd(diagram, relation.OriginReference);
                var target = (relation.Target is { } t ? diagram.NodeFor(t) : null) ?? FindEnd(diagram, relation.TargetReference);
                if (source is not null && target is not null)
                    diagram.Edges.Add(new DiagramEdge(source, target, RelationKind.Transition, relation.Label));
            }
        }

        return diagram;
    }

    // ----- helpers ------------------------------------------------------------

    /// <summary>Draws an edge between two elements when both are on the diagram.</summary>
    private static void Connect(Diagram diagram, Element from, Element? to, RelationKind kind, string? label = null)
    {
        if (to is null)
            return;

        var fromNode = diagram.NodeFor(from);
        var toNode = diagram.NodeFor(to);
        if (fromNode is null || toNode is null || ReferenceEquals(fromNode, toNode))
            return;

        if (!diagram.Edges.Any(e => ReferenceEquals(e.Source, fromNode) && ReferenceEquals(e.Target, toNode) && e.Kind == kind && e.Label == label))
            diagram.Edges.Add(new DiagramEdge(fromNode, toNode, kind, label));
    }

    private static bool IsPackage(Element element) => element.Kind is "package" or "library package" or "model";

    private static bool IsStructural(Element element) => StructuralFeatures.Contains(element.Kind) && element.Name is not null;

    private static bool IsTypedUsage(Element element)
        => IsStructural(element) && element.Relations.Any(r => r.Kind == RelationKind.Typing);

    private static bool IsConnector(Element element) => element.Kind is "connection" or "interface" or "flow" or "allocation";

    private static bool IsAction(Element element)
        => element.Kind is "action" or "perform action" or "send action" or "accept action" or "event occurrence" && element.Name is not null;

    private static bool IsState(Element element) => element.Kind is "state" or "exhibit state" && element.Name is not null;

    private static bool Contains(Element ancestor, Element element)
    {
        for (var e = element; e is not null; e = e.Parent)
        {
            if (ReferenceEquals(e, ancestor))
                return true;
        }

        return false;
    }

    private static Element RootOf(Element element)
    {
        var root = element;
        while (root.Parent is { } parent)
            root = parent;
        return root;
    }

    private static string Shorten(string text, int length = 70)
        => text.Length <= length ? text : text[..length].TrimEnd() + "…";
}
