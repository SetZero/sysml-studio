using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SysmlStudio.App.ViewModels;
using SysmlStudio.App.Views;
using SysmlStudio.Diagrams;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(SysmlStudio.App.Tests.TestApp))]

namespace SysmlStudio.App.Tests;

/// <summary>The real application, rendered by Skia into memory instead of onto a screen.</summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        // Recent folders go to a throwaway place, never the user's own list.
        Environment.SetEnvironmentVariable("SYSML_STUDIO_HOME",
            Path.Combine(Path.GetTempPath(), "sysml-studio-tests", "home"));

        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}

/// <summary>
/// Opens the window on a model and drives it the way a user would, through
/// the view models. Each test saves what it rendered when
/// SYSML_STUDIO_SHOTS names a folder, so the look can be checked by eye.
/// </summary>
public sealed class WindowTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static string ModelFolder
        => Environment.GetEnvironmentVariable("SYSML_STUDIO_MODEL") is { Length: > 0 } model && Directory.Exists(model)
            ? model
            : Fixture;

    private static (MainWindow Window, ShellViewModel Shell) Open(string folder)
    {
        var shell = new ShellViewModel();
        var window = new MainWindow { DataContext = shell, Width = 1600, Height = 1000 };
        shell.Attach(window);
        window.Show();
        shell.Open(folder);
        Settle();
        return (window, shell);
    }

    /// <summary>Lets layout, bindings and the fit-to-view retries run.</summary>
    private static void Settle()
    {
        for (var i = 0; i < 30; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(10);
        }
    }

    private static void Shoot(MainWindow window, string name)
    {
        if (Environment.GetEnvironmentVariable("SYSML_STUDIO_SHOTS") is not { Length: > 0 } folder)
            return;

        Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame();
        if (frame is null)
            return;

        using var file = File.Create(Path.Combine(folder, name + ".png"));
        frame.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    [AvaloniaFact]
    public void TheStartPageShowsWhenNothingIsOpen()
    {
        var shell = new ShellViewModel();
        var window = new MainWindow { DataContext = shell, Width = 1600, Height = 1000 };
        window.Show();
        Settle();
        Shoot(window, "empty");

        Assert.False(shell.HasWorkspace);
        Assert.Same(shell.Welcome, shell.ActiveDocument ?? shell.Welcome);
    }

    [AvaloniaFact]
    public void OpeningAFolderFillsTheBrowserAndTheStatusBar()
    {
        var (window, shell) = Open(ModelFolder);
        Shoot(window, "workspace");

        Assert.True(shell.HasWorkspace);
        Assert.NotEmpty(shell.Browser.Roots);
        Assert.StartsWith("indexed", shell.IndexedSummary, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void EveryDiagramKindOpensAsADocument()
    {
        var (window, shell) = Open(Fixture);
        var workspace = shell.Workspace!;

        foreach (var (kind, root) in new[]
                 {
                     (DiagramKind.Definition, "Sample"),
                     (DiagramKind.Requirements, "Sample"),
                     (DiagramKind.Interconnection, "Sample::Vehicle"),
                     (DiagramKind.ActionFlow, "Sample::StartUp"),
                     (DiagramKind.StateMachine, "Sample::Running"),
                 })
        {
            shell.Select(workspace.Find(root)!);
            Assert.True(shell.OpenDiagramCommand.CanExecute(kind), $"{kind} of {root}");
            shell.OpenDiagramCommand.Execute(kind);
            Settle();

            Assert.Equal(kind, shell.ActiveKind);
            Shoot(window, $"diagram-{kind}");
        }
    }

    [AvaloniaFact]
    public void SelectingAnElementFillsThePropertiesAndFindsItsUsages()
    {
        var (window, shell) = Open(Fixture);
        shell.Select(shell.Workspace!.Find("Sample::Engine")!);
        shell.FindUsagesCommand.Execute(null);
        Settle();
        Shoot(window, "properties");

        Assert.Equal("Engine", shell.Properties.Name);
        Assert.Contains(shell.Properties.Rows, r => r.Name == "Short name");
        Assert.Equal(2, shell.Bottom.Usages.Count); // the declaration, and "engine : Engine"
    }

    [AvaloniaFact]
    public void ASourceTabShowsTheFileAndItsErrors()
    {
        var (window, shell) = Open(Fixture);
        var path = shell.Workspace!.Files.First().Path;
        shell.OpenSource(path, 10);
        Settle();

        var source = Assert.IsType<SourceDocumentViewModel>(shell.ActiveDocument);
        Assert.False(source.HasErrors);

        source.Text.Insert(source.Text.TextLength, "\npackage Broken {");
        Assert.True(source.IsDirty);
        source.FlushPendingReparse(); // the real editor waits a quarter of a second after the last keystroke
        Settle();
        Shoot(window, "source");

        Assert.True(source.HasErrors);
        Assert.True(source.IsDirty);
        Assert.True(shell.ErrorCount > 0);
    }

    [AvaloniaFact]
    public void TheSourceColoursEveryKeywordTheGrammarDefines()
    {
        var keywords = SysmlHighlighting.Keywords;
        Assert.Contains("part", keywords);
        Assert.Contains("requirement", keywords);
        Assert.Contains("satisfy", keywords);
        Assert.True(keywords.Count > 100, $"only {keywords.Count} keywords");
    }

    [AvaloniaFact]
    public void TheDarkThemeRendersToo()
    {
        var (window, shell) = Open(Fixture);
        shell.Select(shell.Workspace!.Find("Sample")!);
        shell.OpenDiagramCommand.Execute(DiagramKind.Definition);
        shell.IsDark = true;
        Settle();
        Shoot(window, "dark");

        Assert.True(shell.IsDark);
        shell.IsDark = false;
    }
}
