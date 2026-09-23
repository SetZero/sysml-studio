using SysmlStudio.Model;
using Xunit;

namespace SysmlStudio.Tests;

/// <summary>What the Problems pane lists, and what it leaves out.</summary>
public sealed class DiagnosticsTests
{
    private static SysmlWorkspace Fixture() =>
        SysmlWorkspace.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures"));

    [Fact]
    public void AReferenceIntoAPackageTheFolderHoldsIsReported()
    {
        var warning = Assert.Single(Fixture().Diagnostics, d => d.Severity == Severity.Warning);
        Assert.Contains("Sample::NoSuchThing", warning.Message, StringComparison.Ordinal);
        Assert.True(warning.Line > 0);
    }

    [Fact]
    public void ASyntaxErrorIsAnErrorWithAPlace()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sysml-studio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "bad.sysml"), "package P {\n    part def A\n}\n");
            var error = Assert.Single(SysmlWorkspace.Load(folder).Diagnostics, d => d.Severity == Severity.Error);
            Assert.Equal(3, error.Line);
            Assert.EndsWith("bad.sysml", error.File, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void UsagesFindEveryRelationPointingAtAnElement()
    {
        var workspace = Fixture();
        var engine = workspace.Find("Sample::Engine")!;

        var usage = Assert.Single(workspace.Usages(engine));
        Assert.Equal(RelationKind.Typing, usage.Kind);
        Assert.Equal("engine", usage.Source.Name);
    }
}
