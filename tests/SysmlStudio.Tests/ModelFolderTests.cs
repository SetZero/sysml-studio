using SysmlStudio.Model;
using Xunit;

namespace SysmlStudio.Tests;

/// <summary>
/// A whole folder of real models, when one is pointed at. SYSML_STUDIO_MODEL
/// names it; the gate script points it at Ferrix's docs/sysml when that
/// checkout is beside this one, and the tests skip otherwise so that a clone
/// on its own still passes.
/// </summary>
public sealed class ModelFolderTests
{
    private static string? ModelFolder
    {
        get
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("SYSML_STUDIO_MODEL");
            if (!string.IsNullOrEmpty(fromEnvironment) && Directory.Exists(fromEnvironment))
                return fromEnvironment;
            return null;
        }
    }

    [SkippableFact]
    public void EveryFileParses()
    {
        var folder = ModelFolder ?? throw new SkipException("SYSML_STUDIO_MODEL is not set");
        var workspace = SysmlWorkspace.Load(folder);
        Assert.Empty(workspace.Errors);
        Assert.NotEmpty(workspace.Elements);
    }

    [SkippableFact]
    public void EveryElementHasAKindAndAPlaceInTheText()
    {
        var folder = ModelFolder ?? throw new SkipException("SYSML_STUDIO_MODEL is not set");
        var workspace = SysmlWorkspace.Load(folder);
        foreach (var element in workspace.Elements)
        {
            Assert.False(string.IsNullOrWhiteSpace(element.Kind));
            Assert.True(element.StartTokenIndex <= element.StopTokenIndex);
        }
    }
}
