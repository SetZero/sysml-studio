using SysmlStudio.Editing;
using SysmlStudio.Model;
using Xunit;

namespace SysmlStudio.Tests;

/// <summary>
/// Every edit is a patch over the text: it changes what it says it changes,
/// leaves everything else byte for byte, and never leaves a file that no
/// longer parses.
/// </summary>
public sealed class EditingTests
{
    private static SysmlWorkspace Fixture() =>
        SysmlWorkspace.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures"));

    private static string Text(SysmlWorkspace workspace) => workspace.Files.Single().Text;

    private static void Apply(SysmlWorkspace workspace, EditPlan plan)
    {
        EditApplier.Apply(workspace, plan);
        Assert.Empty(workspace.Errors);
    }

    [Fact]
    public void RenameChangesTheDeclarationAndEveryReference()
    {
        var workspace = Fixture();
        var before = Text(workspace);

        Apply(workspace, ModelEditor.Rename(workspace, workspace.Find("Sample::Engine")!, "Motor"));
        var after = Text(workspace);

        Assert.Contains("part def <'P.1'> Motor {", after, StringComparison.Ordinal);
        Assert.Contains("part engine : Motor;", after, StringComparison.Ordinal);
        Assert.DoesNotContain("Engine", after.Replace("Sample::Vehicle::engine", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);

        // What was not about Engine is exactly as it was.
        Assert.Equal(before.Replace("Engine", "Motor", StringComparison.Ordinal), after);

        var motor = workspace.Find("Sample::Motor")!;
        Assert.Single(workspace.Usages(motor));
    }

    [Fact]
    public void RenameFollowsQualifiedReferencesAndChains()
    {
        var workspace = Fixture();
        Apply(workspace, ModelEditor.Rename(workspace, workspace.Find("Sample::Vehicle::engine")!, "motor"));
        var after = Text(workspace);

        Assert.Contains("part motor : Engine;", after, StringComparison.Ordinal);
        Assert.Contains("connect motor.shaft to wheels;", after, StringComparison.Ordinal);
        Assert.Contains("satisfy MustStart by Vehicle::motor;", after, StringComparison.Ordinal);
    }

    [Fact]
    public void ANameThatIsNotAnIdentifierIsQuoted()
    {
        var workspace = Fixture();
        Apply(workspace, ModelEditor.Rename(workspace, workspace.Find("Sample::Wheel")!, "Road Wheel"));

        Assert.Contains("part def 'Road Wheel' :> RollingThing;", Text(workspace), StringComparison.Ordinal);
        Assert.NotNull(workspace.Find("Sample::Road Wheel"));
    }

    [Fact]
    public void RenameRefusesANameTakenBySibling()
    {
        var workspace = Fixture();
        var error = Assert.Throws<EditException>(() => ModelEditor.Rename(workspace, workspace.Find("Sample::Wheel")!, "Engine"));
        Assert.Contains("already has", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeleteRemovesTheDeclarationAndItsLine()
    {
        var workspace = Fixture();
        var plan = ModelEditor.Delete(workspace, workspace.Find("Sample::RollingThing")!);
        Assert.Contains("1 reference will no longer resolve", plan.Description, StringComparison.Ordinal);

        Apply(workspace, plan);
        var after = Text(workspace);
        Assert.DoesNotContain("part def RollingThing;", after, StringComparison.Ordinal);
        Assert.DoesNotContain("\n    \n    requirement", after, StringComparison.Ordinal);
        Assert.Null(workspace.Find("Sample::RollingThing"));
    }

    [Fact]
    public void AddChildAppendsIndentedLikeItsSiblings()
    {
        var workspace = Fixture();
        Apply(workspace, ModelEditor.AddChild(workspace.Find("Sample::Vehicle")!, "part", "brakes", "RollingThing"));

        Assert.Contains("        connect engine.shaft to wheels;\n        part brakes : RollingThing;\n    }", Text(workspace), StringComparison.Ordinal);
        var brakes = workspace.Find("Sample::Vehicle::brakes")!;
        Assert.Equal("Sample::RollingThing", brakes.Relations.Single(r => r.Kind == RelationKind.Typing).Target?.QualifiedName);
    }

    [Fact]
    public void AddingToABodylessDefinitionGivesItABody()
    {
        var workspace = Fixture();
        Apply(workspace, ModelEditor.AddChild(workspace.Find("Sample::ShaftPort")!, "attribute", "torque"));

        Assert.Contains("port def ShaftPort {\n        attribute torque;\n    }", Text(workspace), StringComparison.Ordinal);
    }

    [Fact]
    public void SetTypingReplacesTheTypeOrAddsOne()
    {
        var workspace = Fixture();
        Apply(workspace, ModelEditor.SetTyping(workspace.Find("Sample::Vehicle::wheels")!, "RollingThing"));
        Assert.Contains("part wheels : RollingThing[4];", Text(workspace), StringComparison.Ordinal);

        Apply(workspace, ModelEditor.AddChild(workspace.Find("Sample::Vehicle")!, "part", "spare"));
        Apply(workspace, ModelEditor.SetTyping(workspace.Find("Sample::Vehicle::spare")!, "Wheel"));
        Assert.Contains("part spare : Wheel;", Text(workspace), StringComparison.Ordinal);
    }

    [Fact]
    public void AddSpecializationAppendsOrStartsTheList()
    {
        var workspace = Fixture();
        Apply(workspace, ModelEditor.AddSpecialization(workspace.Find("Sample::Wheel")!, "Engine"));
        Assert.Contains("part def Wheel :> RollingThing, Engine;", Text(workspace), StringComparison.Ordinal);

        Apply(workspace, ModelEditor.AddSpecialization(workspace.Find("Sample::RollingThing")!, "Engine"));
        Assert.Contains("part def RollingThing :> Engine;", Text(workspace), StringComparison.Ordinal);
    }

    [Fact]
    public void SetMaturityReplacesAddsAndRemoves()
    {
        var workspace = Fixture();
        string[] maturity = ["implemented", "planned"];

        Apply(workspace, ModelEditor.SetMaturity(workspace.Find("Sample::Vehicle")!, "planned", maturity));
        Assert.Contains("#planned\n    part def Vehicle", Text(workspace), StringComparison.Ordinal);

        Apply(workspace, ModelEditor.SetMaturity(workspace.Find("Sample::RollingThing")!, "implemented", maturity));
        Assert.Contains("#implemented part def RollingThing;", Text(workspace), StringComparison.Ordinal);
        Assert.Equal("implemented", workspace.Find("Sample::RollingThing")!.Maturity);

        Apply(workspace, ModelEditor.SetMaturity(workspace.Find("Sample::RollingThing")!, null, maturity));
        Assert.Contains("    part def RollingThing;", Text(workspace), StringComparison.Ordinal);
        Assert.Null(workspace.Find("Sample::RollingThing")!.Maturity);
    }

    [Fact]
    public void SetDocumentationReplacesOrAdds()
    {
        var workspace = Fixture();
        Apply(workspace, ModelEditor.SetDocumentation(workspace.Find("Sample::Engine")!, "Makes the car go."));
        Assert.Equal("Makes the car go.", workspace.Find("Sample::Engine")!.Documentation);

        Apply(workspace, ModelEditor.SetDocumentation(workspace.Find("Sample::Vehicle")!, "Four wheels and an engine."));
        Assert.Equal("Four wheels and an engine.", workspace.Find("Sample::Vehicle")!.Documentation);
        Assert.Contains("part def Vehicle {\n        doc /* Four wheels and an engine. */", Text(workspace), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NewRelation.Succession, "Sample::StartUp::idle", "Sample::StartUp::crank", "Sample::StartUp", "first idle then crank;")]
    [InlineData(NewRelation.Transition, "Sample::Running::on", "Sample::Running::off", "Sample::Running", "transition first on then off;")]
    [InlineData(NewRelation.Connect, "Sample::Vehicle::wheels", "Sample::Vehicle::engine", "Sample::Vehicle", "connect wheels to engine;")]
    [InlineData(NewRelation.Dependency, "Sample::MustStart", "Sample::MustStopToo", "Sample", "dependency from MustStart to MustStopToo;")]
    [InlineData(NewRelation.Satisfy, "Sample::Vehicle::wheels", "Sample::MustStopToo", "Sample", "satisfy MustStopToo by Sample::Vehicle::wheels;")]
    [InlineData(NewRelation.Specialization, "Sample::Engine", "Sample::RollingThing", "Sample", "part def <'P.1'> Engine :> RollingThing {")]
    [InlineData(NewRelation.Composition, "Sample::Engine", "Sample::Wheel", "Sample", "part wheel : Wheel;")]
    public void RelationsAreWrittenWhereSysmlPutsThem(NewRelation kind, string from, string to, string root, string expected)
    {
        var workspace = Fixture();
        Apply(workspace, ModelEditor.AddRelation(kind, workspace.Find(from)!, workspace.Find(to)!, workspace.Find(root)!));
        Assert.Contains(expected, Text(workspace), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEditThatWouldBreakTheFileIsRefusedAndChangesNothing()
    {
        var workspace = Fixture();
        var before = Text(workspace);
        var engine = workspace.Find("Sample::Engine")!;
        var broken = new EditPlan("break it", [new TextEdit(engine.File.Path, engine.Span.Stop, 1, string.Empty)]);

        var error = Assert.Throws<EditException>(() => EditApplier.Apply(workspace, broken));
        Assert.Contains("nothing was changed", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, Text(workspace));
    }

    [Fact]
    public void UndoAndRedoRestoreTheText()
    {
        var workspace = Fixture();
        var before = Text(workspace);
        var history = new EditHistory();

        history.Record(EditApplier.Apply(workspace, ModelEditor.Rename(workspace, workspace.Find("Sample::Engine")!, "Motor")));
        var renamed = Text(workspace);

        history.Undo(workspace);
        Assert.Equal(before, Text(workspace));
        Assert.NotNull(workspace.Find("Sample::Engine"));

        history.Redo(workspace);
        Assert.Equal(renamed, Text(workspace));
    }

    [Fact]
    public void EditsStayInMemoryUntilSaved()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sysml-studio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "model.sysml");
            File.WriteAllText(path, "package P {\n    part def A;\n}\n");
            var workspace = SysmlWorkspace.Load(folder);

            EditApplier.Apply(workspace, ModelEditor.Rename(workspace, workspace.Find("P::A")!, "B"));
            Assert.True(workspace.IsDirty(path));
            Assert.Contains("part def A;", File.ReadAllText(path), StringComparison.Ordinal);

            workspace.Save(path);
            Assert.False(workspace.IsDirty(path));
            Assert.Equal("package P {\n    part def B;\n}\n", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ThePreviewShowsTheChangedLines()
    {
        var workspace = Fixture();
        var preview = ModelEditor.Rename(workspace, workspace.Find("Sample::Engine")!, "Motor").Preview(workspace);

        Assert.Contains(preview, l => l.EndsWith("- part def <'P.1'> Engine {", StringComparison.Ordinal));
        Assert.Contains(preview, l => l.EndsWith("+ part def <'P.1'> Motor {", StringComparison.Ordinal));
    }
}
