using SysmlStudio.Model;
using SysmlStudio.Syntax;
using Xunit;

namespace SysmlStudio.Tests;

/// <summary>
/// What the indexer must get right about a file. The fixture is small on
/// purpose; the full Ferrix model is checked by <see cref="ModelFolderTests"/>.
/// </summary>
public sealed class IndexTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.sysml");

    private static SysmlWorkspace Fixture() =>
        SysmlWorkspace.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures"));

    [Fact]
    public void FixtureParsesWithoutErrors()
    {
        var file = SourceFile.Parse(FixturePath);
        Assert.Empty(file.Errors);
    }

    [Fact]
    public void TextSurvivesAParseUntouched()
    {
        var text = File.ReadAllText(FixturePath);
        var file = SourceFile.ParseText(FixturePath, text);
        Assert.Equal(text, file.Text);
    }

    [Fact]
    public void KindsReadAsTheNotationWritesThem()
    {
        var workspace = Fixture();
        var engine = workspace.Find("Sample::Engine");

        Assert.NotNull(engine);
        Assert.Equal("part def", engine.Kind);
        Assert.Equal("P.1", engine.ShortName);
        Assert.Equal("Turns fuel into torque.", engine.Documentation);
    }

    [Fact]
    public void UsagesNestUnderTheirDefinition()
    {
        var workspace = Fixture();
        var vehicle = workspace.Find("Sample::Vehicle");

        Assert.NotNull(vehicle);
        Assert.Contains(vehicle.Children, c => c is { Kind: "part", Name: "engine" });
        var wheels = vehicle.Children.Single(c => c.Name == "wheels");
        Assert.Equal("[4]", wheels.Multiplicity);
    }

    [Fact]
    public void MaturityKeywordsAreRead()
    {
        var workspace = Fixture();

        Assert.Equal("implemented", workspace.Find("Sample::Vehicle")!.Maturity);
        Assert.Equal("planned", workspace.Find("Sample::Wheel")!.Maturity);
        Assert.Null(workspace.Find("Sample::RollingThing")!.Maturity);
    }

    [Theory]
    [InlineData("Sample::Wheel", RelationKind.Specialization, "Sample::RollingThing")]
    [InlineData("Sample::Engine::power", RelationKind.Typing, null)]
    public void RelationsPointWhereTheNotationDoes(string from, RelationKind kind, string? to)
    {
        var workspace = Fixture();
        var element = workspace.Find(from);

        Assert.NotNull(element);
        var relation = element.Relations.Single(r => r.Kind == kind);
        Assert.Equal(to, relation.Target?.QualifiedName);
    }

    [Fact]
    public void TypingResolvesToTheDefinition()
    {
        var workspace = Fixture();
        var engine = workspace.Find("Sample::Vehicle")!.Children.Single(c => c.Name == "engine");

        var typing = engine.Relations.Single(r => r.Kind == RelationKind.Typing);
        Assert.Equal("Sample::Engine", typing.Target?.QualifiedName);
    }

    [Fact]
    public void ConnectCarriesBothEnds()
    {
        var workspace = Fixture();
        var connect = workspace.Elements.Single(e => e.Kind == "connection");

        var relation = connect.Relations.Single(r => r.Kind == RelationKind.Connect);
        Assert.Equal("engine.shaft", relation.Label);
        Assert.Equal("wheels", relation.TargetReference);
    }

    [Fact]
    public void SatisfyNamesItsRequirement()
    {
        var workspace = Fixture();
        var satisfy = workspace.Elements.Single(e => e.Kind == "satisfy");

        var requirement = satisfy.Relations.First(r => r.Kind == RelationKind.Satisfy);
        Assert.Equal("Sample::MustStart", requirement.Target?.QualifiedName);
    }

    [Fact]
    public void TransitionsCarrySourceAndTarget()
    {
        var workspace = Fixture();
        var transition = workspace.Elements.Single(e => e is { Kind: "transition", Name: "off_to_on" });

        var relation = transition.Relations.Single(r => r.Kind == RelationKind.Transition);
        Assert.Equal("off", transition.Value);
        Assert.Equal("on", relation.TargetReference.Trim());
    }

    [Fact]
    public void ShortNamesResolveToo()
    {
        var workspace = Fixture();
        Assert.Equal("Sample::MustStart", workspace.Elements.First(e => e.ShortName == "R.1").QualifiedName);
    }
}
