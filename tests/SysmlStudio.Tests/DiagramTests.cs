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
}
