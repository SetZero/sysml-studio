using SysmlStudio.App.Services;
using Xunit;

namespace SysmlStudio.App.Tests;

/// <summary>
/// The start page's sample opens from a copy the user can write to, since
/// an installed application's own folder is read-only.
/// </summary>
public sealed class SamplesTests
{
    [Fact]
    public void TheSampleIsCopiedOnceAndTheCopyKeepsItsEdits()
    {
        var home = Path.Combine(Path.GetTempPath(), "sysml-studio-tests", "samples-" + Guid.NewGuid().ToString("N"));
        var before = Environment.GetEnvironmentVariable("SYSML_STUDIO_HOME");
        Environment.SetEnvironmentVariable("SYSML_STUDIO_HOME", home);
        try
        {
            Assert.True(Samples.Has(Samples.Default));

            var copy = Samples.Prepare(Samples.Default);
            Assert.StartsWith(home, copy, StringComparison.Ordinal);
            var file = Path.Combine(copy, "structure.sysml");
            Assert.Equal(
                File.ReadAllText(Path.Combine(Samples.ShippedRoot, Samples.Default, "structure.sysml")),
                File.ReadAllText(file));

            File.AppendAllText(file, "\n// mine\n");
            Assert.Equal(copy, Samples.Prepare(Samples.Default));
            Assert.EndsWith("// mine\n", File.ReadAllText(file), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SYSML_STUDIO_HOME", before);
            if (Directory.Exists(home))
                Directory.Delete(home, recursive: true);
        }
    }
}
