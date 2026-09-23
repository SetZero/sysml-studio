using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SysmlStudio.App.ViewModels;
using SysmlStudio.App.Views;
using SysmlStudio.Diagrams;
using Xunit;

namespace SysmlStudio.App.Tests;

/// <summary>Editing through the window: dialogs, the relation tool, menus, undo and save.</summary>
public sealed class EditingWindowTests : IDisposable
{
    private readonly string _folder;

    public EditingWindowTests()
    {
        // Each test edits its own copy of the sample, so a save never touches the fixture.
        _folder = Path.Combine(Path.GetTempPath(), "sysml-studio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.sysml"), Path.Combine(_folder, "sample.sysml"));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string SamplePath => Path.Combine(_folder, "sample.sysml");

    private (MainWindow Window, ShellViewModel Shell) Open()
    {
        var shell = new ShellViewModel();
        var window = new MainWindow { DataContext = shell, Width = 1600, Height = 1000 };
        shell.Attach(window);
        window.Show();
        shell.Open(_folder);
        Settle();
        return (window, shell);
    }

    private static void Settle()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    [AvaloniaFact]
    public void RenameThroughTheDialogUpdatesTreeDiagramAndText()
    {
        var (window, shell) = Open();
        shell.Select(shell.Workspace!.Find("Sample")!);
        shell.OpenDiagramCommand.Execute(DiagramKind.Definition);
        Settle();

        shell.RenameCommand.Execute(shell.Workspace!.Find("Sample::Engine"));
        Settle();
        var dialog = Assert.IsType<DialogViewModel>(shell.Dialog);
        dialog.Name = "Motor";
        Settle();
        Shoot(window, "dialog-rename");
        Assert.Contains(dialog.Preview, l => l.Contains("+ part def <'P.1'> Motor {", StringComparison.Ordinal));
        dialog.ConfirmCommand.Execute(null);
        Settle();

        Assert.Null(shell.Dialog);
        Assert.NotNull(shell.Workspace!.Find("Sample::Motor"));
        Assert.Equal("Sample", shell.SelectedElement?.QualifiedName); // the selection stays where it was
        var diagram = Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument);
        Assert.Contains(diagram.Nodes, n => n.Title == "Motor");
        Assert.Equal("modified", shell.SaveState);

        // Nothing was written: that is Save's job.
        Assert.Contains("Engine", File.ReadAllText(SamplePath), StringComparison.Ordinal);

        shell.UndoCommand.Execute(null);
        Settle();
        Assert.NotNull(shell.Workspace!.Find("Sample::Engine"));
        Assert.Null(shell.Workspace!.Find("Sample::Motor"));
    }

    [AvaloniaFact]
    public void AddThroughTheDialogAndSave()
    {
        var (_, shell) = Open();
        _ = shell.AddTo(shell.Workspace!.Find("Sample::Vehicle"), "part");
        Settle();

        var dialog = Assert.IsType<DialogViewModel>(shell.Dialog);
        dialog.Name = "brakes";
        dialog.TypeText = "Sample::RollingThing";
        dialog.ConfirmCommand.Execute(null);
        Settle();

        var brakes = shell.Workspace!.Find("Sample::Vehicle::brakes");
        Assert.NotNull(brakes);
        Assert.Equal(brakes, shell.SelectedElement);

        shell.SaveAllCommand.Execute(null);
        Settle();
        Assert.Contains("part brakes : RollingThing;", File.ReadAllText(SamplePath), StringComparison.Ordinal);
        Assert.Equal("saved", shell.SaveState);
    }

    [AvaloniaFact]
    public void TheRelationToolDrawsAConnectBetweenTwoClickedBoxes()
    {
        var (_, shell) = Open();
        shell.Select(shell.Workspace!.Find("Sample::Vehicle")!);
        shell.OpenDiagramCommand.Execute(DiagramKind.Interconnection);
        Settle();

        shell.StartRelationCommand.Execute("Connect");
        Assert.True(shell.HasTool);

        var diagram = Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument);
        diagram.Nodes.Single(n => n.Element.Name == "wheels").IsSelected = true;
        diagram.Nodes.Single(n => n.Element.Name == "engine").IsSelected = true;
        Settle();

        Assert.False(shell.HasTool);
        Assert.Contains("connect wheels to engine;", shell.Workspace!.Files.Single().Text, StringComparison.Ordinal);
        var redrawn = Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument);
        Assert.Equal(2, redrawn.Connections.Count);
    }

    [AvaloniaFact]
    public void DeleteAsksFirstAndSaysWhatWillBreak()
    {
        var (_, shell) = Open();
        shell.DeleteCommand.Execute(shell.Workspace!.Find("Sample::RollingThing"));
        Settle();

        var dialog = Assert.IsType<DialogViewModel>(shell.Dialog);
        Assert.Contains("1 reference will no longer resolve", dialog.Title, StringComparison.Ordinal);
        Assert.True(dialog.IsDanger);
        dialog.ConfirmCommand.Execute(null);
        Settle();

        Assert.Null(shell.Workspace!.Find("Sample::RollingThing"));
        Assert.Contains(shell.Bottom.Problems, p => p.Message.Contains("RollingThing", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void AnInvalidNameIsExplainedBeforeAnythingIsWritten()
    {
        var (_, shell) = Open();
        shell.RenameCommand.Execute(shell.Workspace!.Find("Sample::Wheel"));
        Settle();

        var dialog = Assert.IsType<DialogViewModel>(shell.Dialog);
        dialog.Name = "Engine";
        Assert.True(dialog.HasMessage);
        Assert.False(dialog.ConfirmCommand.CanExecute(null));
        dialog.CancelCommand.Execute(null);
        Settle();
        Assert.NotNull(shell.Workspace!.Find("Sample::Wheel"));
    }

    [AvaloniaFact]
    public void TheContextMenuOffersTheEditsThatApply()
    {
        var (_, shell) = Open();
        var headers = Headers(ElementMenu.For(shell, shell.Workspace!.Find("Sample::Vehicle")!));

        Assert.Contains("Rename…", headers);
        Assert.Contains("Add", headers);
        Assert.Contains("Specializes…", headers);
        Assert.Contains("Delete", headers);
        Assert.DoesNotContain("Set type…", headers); // a definition is not typed

        var part = Headers(ElementMenu.For(shell, shell.Workspace!.Find("Sample::Vehicle::engine")!));
        Assert.Contains("Set type…", part);
    }

    [AvaloniaFact]
    public void TypingInASourceTabIsWhatTheNextEditStartsFrom()
    {
        var (_, shell) = Open();
        shell.OpenSource(SamplePath);
        Settle();
        var source = Assert.IsType<SourceDocumentViewModel>(shell.ActiveDocument);
        source.Text.Replace(source.Text.Text.IndexOf("part def RollingThing;", StringComparison.Ordinal), "part def RollingThing;".Length, "part def Roller;");
        source.FlushPendingReparse();
        Settle();

        Assert.NotNull(shell.Workspace!.Find("Sample::Roller"));
        Assert.True(source.IsDirty);
    }

    private static void Shoot(Window window, string name)
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

    private static List<string> Headers(ContextMenu menu)
        => [.. menu.ItemsSource!.OfType<MenuItem>().Select(m => m.Header as string ?? string.Empty)];
}
