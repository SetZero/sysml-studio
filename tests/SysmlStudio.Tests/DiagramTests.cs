using SysmlStudio.Diagrams;
using SysmlStudio.Model;
using Xunit;

namespace SysmlStudio.Tests;

/// <summary>
/// Every node is an element the model declares and every edge is a relation it
/// writes down, and after layout nothing sits on top of anything else.
/// </summary>
public sealed class DiagramTests
{
    private static SysmlWorkspace Fixture() =>
        SysmlWorkspace.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures"));

    [Fact]
    public void APackageOffersDefinitionAndRequirementDiagrams()
    {
        var workspace = Fixture();
        var kinds = DiagramBuilder.KindsFor(workspace.Find("Sample")!);

        Assert.Contains(DiagramKind.Definition, kinds);
        Assert.Contains(DiagramKind.Requirements, kinds);
    }

    [Fact]
    public void ADefinitionDiagramHoldsTheDefinitionsOfItsPackage()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Definition, Fixture().Find("Sample")!);

        Assert.Contains(diagram.Nodes, n => n.Label == "Engine");
        Assert.Contains(diagram.Nodes, n => n.Label == "Vehicle");
        Assert.All(diagram.Nodes, n => Assert.True(n.Element.IsDefinition));
        Assert.Contains(diagram.Edges, e =>
            e.Source.Label == "Wheel" && e.Target.Label == "RollingThing" && e.Kind == RelationKind.Specialization);
    }

    [Fact]
    public void ADefinitionNodeListsItsFeatures()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Definition, Fixture().Find("Sample")!);
        var engine = diagram.Nodes.Single(n => n.Label == "Engine");

        Assert.Contains("power : Real", engine.Features);
    }

    [Fact]
    public void AnInterconnectionDiagramDrawsTheConnection()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Interconnection, Fixture().Find("Sample::Vehicle")!);

        Assert.Equal(2, diagram.Nodes.Count);
        var edge = Assert.Single(diagram.Edges);
        Assert.Equal("engine : Engine", edge.Source.Label);
        Assert.Equal("wheels : Wheel[4]", edge.Target.Label);
    }

    [Fact]
    public void ARequirementsDiagramTracesSatisfyToItsRequirement()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Requirements, Fixture().Find("Sample")!);

        Assert.Contains(diagram.Nodes, n => n.Element.ShortName == "R.1");
        Assert.Contains(diagram.Edges, e => e.Kind == RelationKind.Satisfy
            && e.Target.Element.ShortName == "R.1"
            && e.Source.Element.QualifiedName == "Sample::Vehicle::engine");
    }

    [Fact]
    public void AnActionFlowFollowsItsSuccessions()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.ActionFlow, Fixture().Find("Sample::StartUp")!);

        Assert.Equal(2, diagram.Nodes.Count);
        var edge = Assert.Single(diagram.Edges);
        Assert.Equal("crank", edge.Source.Label);
        Assert.Equal("idle", edge.Target.Label);
    }

    [Fact]
    public void AStateMachineFollowsItsTransitions()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.StateMachine, Fixture().Find("Sample::Running")!);

        var edge = Assert.Single(diagram.Edges, e => e.Kind == RelationKind.Transition);
        Assert.Equal("off", edge.Source.Label);
        Assert.Equal("on", edge.Target.Label);
    }

    [Fact]
    public void LayoutGivesEveryNodeASizeAndNoOverlap()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Definition, Fixture().Find("Sample")!);
        DiagramLayout.Apply(diagram);

        Assert.All(diagram.Nodes, n =>
        {
            Assert.True(n.Width > 0, "node has a width");
            Assert.True(n.Height > 0, "node has a height");
        });

        foreach (var a in diagram.Nodes)
        {
            foreach (var b in diagram.Nodes.Where(other => !ReferenceEquals(other, a)))
            {
                var apart = a.X + a.Width <= b.X || b.X + b.Width <= a.X
                    || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y;
                Assert.True(apart, $"'{a.Label}' overlaps '{b.Label}'");
            }
        }
    }

    [Fact]
    public void LayoutRoutesEveryEdge()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Definition, Fixture().Find("Sample")!);
        DiagramLayout.Apply(diagram);

        Assert.All(diagram.Edges, e => Assert.NotEmpty(e.Waypoints));
    }

    [Fact]
    public void ADefinitionDiagramDrawsCompositionFromTheOwnerToThePartsType()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Definition, Fixture().Find("Sample")!);

        Assert.Contains(diagram.Edges, e => e.Kind == RelationKind.Composition
            && e.Source.Label == "Vehicle" && e.Target.Label == "Engine" && e.Label == "engine");
    }

    [Fact]
    public void OneDefinitionShowsWhatItIsMadeOfAndWhatIsMadeOfIt()
    {
        var workspace = Fixture();
        var engine = DiagramBuilder.Build(DiagramKind.Definition, workspace.Find("Sample::Engine")!);

        // Vehicle has a part typed by Engine, so it is one relation away.
        Assert.Contains(engine.Nodes, n => n.Label == "Vehicle");
        Assert.Contains(engine.Edges, e => e.Kind == RelationKind.Composition && e.Target.Label == "Engine");
    }

    [Fact]
    public void ARequirementOffersItsOwnDiagram()
    {
        var requirement = Fixture().Find("Sample::MustStart")!;
        Assert.Contains(DiagramKind.Requirements, DiagramBuilder.KindsFor(requirement));

        var diagram = DiagramBuilder.Build(DiagramKind.Requirements, requirement);
        Assert.Contains(diagram.Edges, e => e.Kind == RelationKind.Satisfy && ReferenceEquals(e.Target.Element, requirement));
    }

    [Fact]
    public void ThenShorthandBecomesAFlowFromStartToDone()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.ActionFlow, Fixture().Find("Sample::Boot")!);

        static string Label(DiagramNode n) => n.Label;
        var edges = diagram.Edges.ConvertAll(e => $"{Label(e.Source)}>{Label(e.Target)}");
        Assert.Equal(["start>check", "check>load", "load>done"], edges);
        Assert.Contains(diagram.Nodes, n => n.Pseudo == "start");
        Assert.Contains(diagram.Nodes, n => n.Pseudo == "done");
    }

    [Fact]
    public void DependenciesRunFromClientToSupplier()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Requirements, Fixture().Find("Sample")!);

        Assert.Contains(diagram.Edges, e => e.Kind == RelationKind.Dependency
            && e.Source.Element.Name == "MustStopToo" && e.Target.Element.Name == "MustStart");
    }

    [Fact]
    public void NoEdgeRunsThroughABoxItDoesNotConnect()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Definition, Fixture().Find("Sample")!);
        DiagramLayout.Apply(diagram);

        foreach (var edge in diagram.Edges)
        {
            Assert.NotEmpty(edge.Waypoints);
            foreach (var box in diagram.Nodes.Where(n => !ReferenceEquals(n, edge.Source) && !ReferenceEquals(n, edge.Target)))
            {
                var inside = edge.Waypoints.Any(p => p.X > box.X + 2 && p.X < box.X + box.Width - 2
                                                  && p.Y > box.Y + 2 && p.Y < box.Y + box.Height - 2);
                Assert.False(inside, $"{edge} runs through {box.Label}");
            }
        }
    }

    [Fact]
    public void BoxesThatGrewAfterPlacementArePushedApart()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Definition, Fixture().Find("Sample")!);
        DiagramLayout.Apply(diagram);

        // As if the canvas had drawn every box three times taller than estimated.
        foreach (var node in diagram.Nodes)
            node.Height *= 3;
        DiagramLayout.RemoveOverlaps(diagram);

        foreach (var a in diagram.Nodes)
        {
            foreach (var b in diagram.Nodes.Where(o => !ReferenceEquals(o, a)))
            {
                var apart = a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y;
                Assert.True(apart, $"'{a.Label}' overlaps '{b.Label}'");
            }
        }
    }

    [Fact]
    public void UnrelatedGroupsAreLaidOutApartAndNeverInterleave()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Definition, Fixture().Find("Sample")!);
        DiagramLayout.Apply(diagram);

        // Everything reachable from Vehicle along edges is one group; the rest is not connected to it.
        var group = new List<DiagramNode> { diagram.Nodes.Single(n => n.Label == "Vehicle") };
        for (var i = 0; i < group.Count; i++)
        {
            var node = group[i];
            group.AddRange(diagram.Edges
                .Where(e => ReferenceEquals(e.Source, node) || ReferenceEquals(e.Target, node))
                .Select(e => ReferenceEquals(e.Source, node) ? e.Target : e.Source)
                .Where(n => !group.Contains(n)).Distinct().ToList());
        }

        var others = diagram.Nodes.Except(group).ToList();
        Assert.NotEmpty(others);

        var left = group.Min(n => n.X);
        var right = group.Max(n => n.X + n.Width);
        var top = group.Min(n => n.Y);
        var bottom = group.Max(n => n.Y + n.Height);
        foreach (var other in others)
        {
            var outside = other.X >= right || other.X + other.Width <= left || other.Y >= bottom || other.Y + other.Height <= top;
            Assert.True(outside, $"'{other.Label}' sits inside the Vehicle group");
        }
    }
}
