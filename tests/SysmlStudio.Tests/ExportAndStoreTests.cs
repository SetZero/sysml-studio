using System.Xml.Linq;
using SysmlStudio.Diagrams;
using SysmlStudio.Model;
using Xunit;

namespace SysmlStudio.Tests;

/// <summary>What the sidecar remembers, and what an exported picture says.</summary>
public sealed class ExportAndStoreTests
{
    private static SysmlWorkspace Fixture() =>
        SysmlWorkspace.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures"));

    private static Diagram SampleDiagram()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Definition, Fixture().Find("Sample")!);
        DiagramLayout.Apply(diagram);
        return diagram;
    }

    [Fact]
    public void TheSvgIsWellFormedAndHoldsEveryNode()
    {
        var diagram = SampleDiagram();
        var svg = XDocument.Parse(SvgExporter.ToSvg(diagram));

        var texts = svg.Descendants().Where(e => e.Name.LocalName == "text").Select(e => e.Value).ToList();
        foreach (var node in diagram.Nodes)
            Assert.Contains(node.Label, texts);

        Assert.Equal(diagram.Edges.Count, svg.Descendants()
            .Count(e => e.Name.LocalName == "path" && e.Attribute("marker-end") is not null));
    }

    [Fact]
    public void SpecializationGetsAHollowTriangle()
    {
        var paths = EdgePathsOf(DiagramKind.Definition, "Sample");
        Assert.Contains(paths, p => p.Attribute("marker-end")!.Value == "url(#triangle)");
    }

    [Fact]
    public void ATraceEdgeIsDashedAndOpenHeaded()
    {
        var paths = EdgePathsOf(DiagramKind.Requirements, "Sample");
        Assert.Contains(paths, p => p.Attribute("stroke-dasharray") is not null
            && p.Attribute("marker-end")!.Value == "url(#open)");
    }

    private static List<XElement> EdgePathsOf(DiagramKind kind, string root)
    {
        var diagram = DiagramBuilder.Build(kind, Fixture().Find(root)!);
        DiagramLayout.Apply(diagram);
        var svg = XDocument.Parse(SvgExporter.ToSvg(diagram));
        return [.. svg.Descendants().Where(e => e.Name.LocalName == "path" && e.Attribute("marker-end") is not null)];
    }

    [Fact]
    public void ADraggedNodeComesBackWhereItWasLeft()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sysml-studio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var diagram = SampleDiagram();
            var moved = diagram.Nodes[0];
            moved.X = 1234;
            moved.Y = 567;

            DiagramStore.Save(folder, new StoredDiagrams { Diagrams = { DiagramStore.Capture(diagram) } });

            var reopened = SampleDiagram();
            var stored = DiagramStore.Load(folder);
            DiagramStore.Restore(reopened, stored.Diagrams[0]);

            var same = reopened.Nodes.Single(n => n.Element.QualifiedName == moved.Element.QualifiedName);
            Assert.Equal(1234, same.X);
            Assert.Equal(567, same.Y);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ASidecarNobodyCanReadIsNotFatal()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sysml-studio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, ".sysml-studio"));
        try
        {
            File.WriteAllText(DiagramStore.PathFor(folder), "{ this is not json");
            Assert.Empty(DiagramStore.Load(folder).Diagrams);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ReopeningSkipsDiagramsWhoseElementIsGone()
    {
        var stored = new StoredDiagrams
        {
            Diagrams =
            {
                new StoredDiagram { Kind = DiagramKind.Definition, Root = "Sample" },
                new StoredDiagram { Kind = DiagramKind.Definition, Root = "NoSuchPackage" },
            },
        };

        var reopened = DiagramStore.Reopen(stored, Fixture()).ToList();
        var diagram = Assert.Single(reopened);
        Assert.Equal("Sample", diagram.Root.QualifiedName);
    }
}
