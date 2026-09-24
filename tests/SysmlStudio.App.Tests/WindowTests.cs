using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
        Assert.Null(shell.ActiveDocument);
        Assert.Equal("Ready", shell.SaveState);
    }

    [AvaloniaFact]
    public void OpeningAFolderFillsTheBrowserAndTheStatusBar()
    {
        var (window, shell) = Open(ModelFolder);
        Shoot(window, "workspace");

        Assert.True(shell.HasWorkspace);
        Assert.NotEmpty(shell.Browser.Roots);
        Assert.Equal("Saved", shell.SaveState);
        Assert.Contains("file", shell.Browser.FilesText, StringComparison.Ordinal);
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
        Assert.Equal("Part definition · P.1", shell.Properties.KindCaption);
        Assert.Equal("Used in 1 place", shell.Properties.UsedIn);
        Assert.True(shell.Bottom.IsOpen);
        Assert.Equal(2, shell.Bottom.Usages.Count); // the declaration, and "engine : Engine"
        Assert.Equal(["declared", "typed by"], shell.Bottom.Usages.Select(u => u.Usage));
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
        source.GoToLine(source.ErrorLine);
        Settle();
        Shoot(window, "source");

        Assert.True(source.HasErrors);
        Assert.True(source.IsDirty);
        Assert.True(source.HasDot);
        Assert.Equal("Unexpected end of file", source.ErrorMessage);
        Assert.True(shell.ErrorCount > 0);
        Assert.True(shell.DiagramKinds.Single(k => k.Kind is null).IsActive); // the switcher shows "Text" over a source tab

        shell.ToggleProblemsCommand.Execute(null);
        Settle();
        Shoot(window, "problems");
        Assert.True(shell.Bottom.IsOpen);
        Assert.True(shell.Bottom.ShowsProblems);
    }

    /// <summary>
    /// Browsing with nothing open shows each element's text, in one tab; the
    /// switcher draws it as a diagram in that same tab, and browsing then
    /// keeps drawing diagrams.
    /// </summary>
    [AvaloniaFact]
    public void BrowsingShowsTheTextThenTheSwitcherDrawsIt()
    {
        var (_, shell) = Open(Fixture);
        var switcher = shell.DiagramKinds;
        Assert.Equal("Text", switcher[0].Label);

        shell.Select(shell.Workspace!.Find("Sample::Engine")!);
        Settle();
        var text = Assert.IsType<SourceDocumentViewModel>(shell.ActiveDocument);
        Assert.Equal(shell.Workspace.Find("Sample::Engine")!.File.Path, text.Path);
        Assert.True(switcher[0].IsActive);
        Assert.True(switcher[0].IsAvailable);

        shell.Select(shell.Workspace.Find("Sample")!);
        Settle();
        Assert.Single(shell.Documents);
        Assert.IsType<SourceDocumentViewModel>(shell.ActiveDocument);

        shell.SwitchKindCommand.Execute(switcher.Single(k => k.Kind == DiagramKind.Definition));
        Settle();
        Assert.Single(shell.Documents);
        Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument);
        Assert.False(switcher[0].IsActive);

        shell.SwitchKindCommand.Execute(switcher[0]);
        Settle();
        Assert.Single(shell.Documents);
        Assert.IsType<SourceDocumentViewModel>(shell.ActiveDocument);
    }

    /// <summary>The parser's messages, as the error box and the problems list word them.</summary>
    [AvaloniaFact]
    public void ParserMessagesAreShortened()
    {
        Assert.Equal("Expected ';'", SourceDocumentViewModel.Shorten("missing ';' at '}' expecting ';'"));
        Assert.Equal("Expected ';' or '{'", SourceDocumentViewModel.Shorten("mismatched input 'x' expecting {';', '{'}"));
        Assert.Equal("Unexpected 'x'", SourceDocumentViewModel.Shorten("extraneous input 'x' expecting {'a', 'b', 'c', 'd'}"));
        Assert.Equal("unresolved reference 'X'", SourceDocumentViewModel.Shorten("unresolved reference 'X'"));
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

    /// <summary>Typing a type's first letters in the editor opens the list, and Tab takes the suggestion.</summary>
    [AvaloniaFact]
    public void TheEditorCompletesWhatIsTyped()
    {
        var (window, shell) = Open(Fixture);
        shell.OpenSource(shell.Workspace!.Files.First().Path);
        Settle();

        var editor = window.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single();
        var line = editor.Document.GetLineByNumber(23); // "        part engine : Engine;" in Vehicle
        const string typed = "\n        part spare : ";
        editor.Document.Insert(line.EndOffset, typed);
        editor.CaretOffset = line.EndOffset + typed.Length;
        editor.TextArea.Focus();
        window.KeyTextInput("V");
        window.KeyTextInput("e");
        window.KeyTextInput("h");
        Settle();
        Shoot(window, "completion");

        var list = window.GetVisualDescendants().OfType<AvaloniaEdit.CodeCompletion.CompletionList>().SingleOrDefault()
                   ?? Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(window)
                       .OfType<AvaloniaEdit.CodeCompletion.CompletionList>().SingleOrDefault();
        Assert.NotNull(list);
        Assert.Equal("Vehicle", list.SelectedItem?.Text);

        window.KeyPress(Avalonia.Input.Key.Tab, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Tab, "\t");
        Settle();
        Assert.Contains("part spare : Vehicle", editor.Document.Text, StringComparison.Ordinal);
    }

    /// <summary>Keywords, type names and numbers each get their own colour; a declared name keeps the text colour.</summary>
    [AvaloniaFact]
    public void TheSourceColoursKeywordsTypesAndNumbers()
    {
        Avalonia.Media.Color keyword = Avalonia.Media.Colors.Purple, comment = Avalonia.Media.Colors.Gray,
            text = Avalonia.Media.Colors.Brown, number = Avalonia.Media.Colors.Green,
            metadata = Avalonia.Media.Colors.Blue, type = Avalonia.Media.Colors.Teal;
        var definition = SysmlHighlighting.Create(keyword, comment, text, number, metadata, type);
        const string line = "part motors : Motor[4]; part def Hub :> Base::Part;";
        var document = new AvaloniaEdit.Document.TextDocument(line);
        var sections = new AvaloniaEdit.Highlighting.DocumentHighlighter(document, definition)
            .HighlightLine(1).Sections;

        Avalonia.Media.Color? ColourOf(string word)
        {
            var at = line.IndexOf(word, StringComparison.Ordinal);
            var section = sections.LastOrDefault(s => s.Offset <= at && at < s.Offset + s.Length);
            return (section?.Color.Foreground as AvaloniaEdit.Highlighting.SimpleHighlightingBrush)?.GetColor(null);
        }

        Assert.Equal(keyword, ColourOf("part"));
        Assert.Equal(keyword, ColourOf("def"));
        Assert.Equal(type, ColourOf("Motor"));
        Assert.Equal(number, ColourOf("4"));
        Assert.Equal(type, ColourOf("Base::Part"));
        Assert.Null(ColourOf("motors"));
        Assert.Null(ColourOf("Hub"));
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

    /// <summary>
    /// The diagrams a real model offers have something in them. Runs against
    /// SYSML_STUDIO_MODEL when it is set; the Ferrix model's names are used
    /// when they are there and skipped when they are not.
    /// </summary>
    [AvaloniaFact]
    public void RealModelDiagramsHaveContent()
    {
        if (Environment.GetEnvironmentVariable("SYSML_STUDIO_MODEL") is not { Length: > 0 } model || !Directory.Exists(model))
            return;

        var (window, shell) = Open(model);
        foreach (var (kind, name) in new[]
                 {
                     (DiagramKind.Requirements, "FerrixRoadmap::stage13Isolation"),
                     (DiagramKind.Requirements, "FerrixRoadmap"),
                     (DiagramKind.ActionFlow, "FerrixBoot::LoaderSequence"),
                     (DiagramKind.Definition, "FerrixStructure::Kernel"),
                     (DiagramKind.Definition, "FerrixDrivers::DevMgr"),
                     (DiagramKind.Definition, "FerrixBoot"),
                 })
        {
            if (shell.Workspace!.Find(name) is not { } element)
                continue;

            shell.Select(element);
            shell.OpenDiagramCommand.Execute(kind);
            Settle();
            Shoot(window, "real-" + name.Replace("::", "-", StringComparison.Ordinal) + "-" + kind);

            var diagram = Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument);
            Assert.NotEmpty(diagram.Nodes);
        }
    }

    /// <summary>
    /// While a box is dragged its arrows follow it, before the mouse is
    /// released, not only after the drop.
    /// </summary>
    [AvaloniaFact]
    public void ArrowsFollowABoxWhileItIsDragged()
    {
        var (window, shell) = Open(Fixture);
        shell.Select(shell.Workspace!.Find("Sample::Vehicle")!);
        shell.OpenDiagramCommand.Execute(DiagramKind.Interconnection);
        Settle();

        var diagram = Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument);
        var connection = Assert.Single(diagram.Connections);
        var container = window.GetVisualDescendants().OfType<Nodify.Avalonia.ItemContainer>()
            .First(c => ReferenceEquals(c.DataContext, connection.Source));

        var grab = container.TranslatePoint(new Point(container.Bounds.Width / 2, 12), window)!.Value;
        var before = connection.Source.Anchor;
        var lineBefore = connection.Line.Bounds;

        window.MouseMove(grab);
        window.MouseDown(grab, Avalonia.Input.MouseButton.Left);
        for (var step = 1; step <= 10; step++)
        {
            window.MouseMove(grab + new Point(step * 12, step * 8), Avalonia.Input.RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
        }

        Settle();
        var during = connection.Source.Anchor;
        var lineDuring = connection.Line.Bounds;
        window.MouseUp(grab + new Point(120, 80), Avalonia.Input.MouseButton.Left);
        Settle();

        Assert.NotEqual(before, during);
        Assert.NotEqual(lineBefore, lineDuring); // the drawn line followed, before the drop
    }

    /// <summary>Selecting another element redraws the diagram in front, in the same tab.</summary>
    [AvaloniaFact]
    public void TheDiagramFollowsTheSelection()
    {
        var (_, shell) = Open(Fixture);
        var workspace = shell.Workspace!;

        shell.Select(workspace.Find("Sample::Engine")!);
        shell.OpenDiagramCommand.Execute(DiagramKind.Definition);
        Settle();

        // RollingThing is not on Engine's diagram: the tab is redrawn for it.
        shell.Select(workspace.Find("Sample::RollingThing")!);
        Settle();
        var redrawn = Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument);
        Assert.Equal("Sample::RollingThing", redrawn.Diagram.Root.QualifiedName);
        Assert.Equal(DiagramKind.Definition, redrawn.Diagram.Kind);

        // Wheel is on RollingThing's diagram (it specializes it): selected there, not redrawn.
        shell.Select(workspace.Find("Sample::Wheel")!);
        Settle();
        Assert.Same(redrawn, shell.ActiveDocument);
        Assert.True(redrawn.Nodes.Single(n => n.Element.Name == "Wheel").IsSelected);

        // A requirement cannot be drawn as a definition diagram: it gets its own kind.
        shell.Select(workspace.Find("Sample::MustStart")!);
        Settle();
        Assert.Equal(DiagramKind.Requirements, Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument).Diagram.Kind);
    }

    /// <summary>The title bar's switcher redraws the tab in front as another kind of diagram.</summary>
    [AvaloniaFact]
    public void TheSwitcherRedrawsTheTabAsAnotherKind()
    {
        var (window, shell) = Open(Fixture);
        shell.Select(shell.Workspace!.Find("Sample")!);
        shell.OpenDiagramCommand.Execute(DiagramKind.Definition);
        Settle();

        var requirements = shell.DiagramKinds.Single(k => k.Kind == DiagramKind.Requirements);
        var state = shell.DiagramKinds.Single(k => k.Kind == DiagramKind.StateMachine);
        Assert.True(requirements.IsAvailable);
        Assert.False(state.IsAvailable); // a package has no states of its own
        Assert.True(shell.DiagramKinds.Single(k => k.Kind == DiagramKind.Definition).IsActive);

        shell.SwitchKindCommand.Execute(requirements);
        Settle();
        Shoot(window, "switched-requirements");

        Assert.Single(shell.Documents);
        Assert.Equal(DiagramKind.Requirements, shell.ActiveKind);
        Assert.True(requirements.IsActive);
        Assert.Equal("Sample · Requirements", shell.DocumentCaption);
        var diagram = Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument);
        Assert.Contains(diagram.Connections, c => c.Label == "satisfies");
    }

    /// <summary>Tabs: a source tab opens beside the diagram, and closing the one in front shows its neighbour.</summary>
    [AvaloniaFact]
    public void TabsOpenAndClose()
    {
        var (_, shell) = Open(Fixture);
        shell.Select(shell.Workspace!.Find("Sample")!);
        shell.OpenDiagramCommand.Execute(DiagramKind.Definition);
        shell.OpenSource(shell.Workspace!.Files.First().Path);
        Settle();

        Assert.Equal(2, shell.Documents.Count);
        var source = Assert.IsType<SourceDocumentViewModel>(shell.ActiveDocument);
        Assert.True(source.IsActive);
        Assert.False(shell.Documents[0].IsActive);

        shell.CloseDocumentCommand.Execute(source);
        Settle();
        Assert.Single(shell.Documents);
        Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument);
        Assert.True(shell.Documents[0].IsActive);
    }

    /// <summary>The export dialog offers both formats and follows the format with the file name.</summary>
    [AvaloniaFact]
    public void TheExportDialogOffersJsonAndXmi()
    {
        var (window, shell) = Open(Fixture);
        shell.Select(shell.Workspace!.Find("Sample")!);
        shell.OpenDiagramCommand.Execute(DiagramKind.Requirements);
        Settle();

        _ = shell.ExportCommand.ExecuteAsync(null);
        Settle();
        var dialog = Assert.IsType<ExportDialogViewModel>(shell.Dialog);
        Shoot(window, "export");

        Assert.True(dialog.IsJson);
        Assert.Equal("export/fixtures.json", dialog.Path);
        dialog.IsXmi = true;
        Assert.Equal("export/fixtures.xmi", dialog.Path);
        Assert.Equal(Path.Combine(Fixture, "export", "fixtures.xmi"), dialog.FullPath);
        Assert.Equal("Whole workspace · 1 file", dialog.Scope);

        dialog.CancelCommand.Execute(null);
        Settle();
        Assert.Null(shell.Dialog);
    }

    /// <summary>
    /// The side panels are glass over the canvas: a box panned under the model
    /// panel shows through it, blurred, instead of being hidden.
    /// </summary>
    [AvaloniaFact]
    public void TheSidePanelsShowTheCanvasThroughFrostedGlass()
    {
        var (window, shell) = Open(Fixture);
        shell.Select(shell.Workspace!.Find("Sample")!);
        shell.OpenDiagramCommand.Execute(DiagramKind.Definition);
        Settle();

        var editor = window.GetVisualDescendants().OfType<Nodify.Avalonia.NodifyEditor>().Single();
        var glass = window.GetVisualDescendants().OfType<FrostPanel>().First(p => p.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Left);
        using var before = window.CaptureRenderedFrame();

        // Drag the empty canvas to the left, so the diagram slides under the model panel.
        var start = new Point(1200, 800);
        var viewport = editor.ViewportLocation;
        window.MouseMove(start);
        window.MouseDown(start, Avalonia.Input.MouseButton.Right);
        for (var step = 1; step <= 14; step++)
        {
            window.MouseMove(start - new Point(step * 30, 0), Avalonia.Input.RawInputModifiers.RightMouseButton);
            Dispatcher.UIThread.RunJobs();
        }

        window.MouseUp(start - new Point(420, 0), Avalonia.Input.MouseButton.Right);
        Settle();
        Assert.NotEqual(viewport, editor.ViewportLocation);
        Shoot(window, "frosted");
        using var after = window.CaptureRenderedFrame();

        Assert.NotNull(before);
        Assert.NotNull(after);
        var region = glass.Bounds.Translate((Vector)glass.TranslatePoint(default, window)!.Value);
        Assert.NotEqual(Pixels(before!, region), Pixels(after!, region)); // what moved underneath shows through
    }

    /// <summary>The sum of the colour values in a region of a rendered frame.</summary>
    private static long Pixels(Avalonia.Media.Imaging.Bitmap frame, Rect region)
    {
        var width = frame.PixelSize.Width;
        var buffer = new byte[width * frame.PixelSize.Height * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new PixelRect(frame.PixelSize), handle.AddrOfPinnedObject(), buffer.Length, width * 4);
        }
        finally
        {
            handle.Free();
        }

        long sum = 0;
        for (var y = (int)region.Top; y < (int)region.Bottom; y += 2)
        {
            for (var x = (int)region.Left; x < (int)region.Right; x += 2)
                sum += buffer[((y * width) + x) * 4] + buffer[(((y * width) + x) * 4) + 1];
        }

        return sum;
    }

    /// <summary>No two default shortcuts share a gesture, and every default parses.</summary>
    [AvaloniaFact]
    public void TheDefaultShortcutsAreDistinct()
    {
        var gestures = ShortcutCatalog.All.Where(a => a.DefaultGesture.Length > 0).ToList();
        Assert.All(gestures, a => Assert.NotNull(ShortcutMap.Parse(a.DefaultGesture)));
        Assert.Empty(gestures.GroupBy(a => a.DefaultGesture).Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.True(ShortcutCatalog.All.Count >= 40, $"only {ShortcutCatalog.All.Count} shortcuts");
        Assert.Equal("Ctrl+Shift+1", ShortcutCatalog.Display("Ctrl+Shift+D1"));
        Assert.Equal("Ctrl++", ShortcutCatalog.Display("Ctrl+OemPlus"));
    }

    /// <summary>
    /// The cogwheel opens the settings; a shortcut is re-recorded by clicking
    /// it and pressing keys, a clash blocks saving, and a saved shortcut works.
    /// </summary>
    [AvaloniaFact]
    public void ShortcutsAreRecordedSavedAndUsed()
    {
        var settingsFile = Services.AppHome.PathOf("settings.json");
        try
        {
            var (window, shell) = Open(Fixture);
            _ = shell.OpenSettingsCommand.ExecuteAsync(null);
            Settle();
            var settings = Assert.IsType<SettingsViewModel>(shell.Dialog);
            Shoot(window, "settings");

            var toggle = settings.Groups.SelectMany(g => g.Rows).Single(r => r.Action.Id == "toggleModel");
            settings.RecordCommand.Execute(toggle);
            window.Focus(); // a click on the shortcut would have focused the window
            Settle();
            Assert.Same(toggle, settings.Recording);

            // A clash first: Ctrl+S is Save's.
            window.KeyPress(Avalonia.Input.Key.S, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.S, "s");
            Settle();
            Assert.Equal("Ctrl+S", toggle.Gesture);
            Assert.True(settings.HasConflict);
            Assert.False(settings.ConfirmCommand.CanExecute(null));
            Assert.True(shell.HasWorkspace); // and Save did not run: the dialog caught the keys

            settings.RecordCommand.Execute(toggle);
            window.KeyPress(Avalonia.Input.Key.J, Avalonia.Input.RawInputModifiers.Control | Avalonia.Input.RawInputModifiers.Shift,
                Avalonia.Input.PhysicalKey.J, "J");
            Settle();
            Assert.Equal("Ctrl+Shift+J", toggle.Gesture);
            Assert.False(settings.HasConflict);

            settings.ConfirmCommand.Execute(null);
            Settle();
            Assert.Null(shell.Dialog);
            Assert.Equal("Show or hide the model panel (Ctrl+Shift+J)", shell.Keys["toggleModel"]);
            Assert.Contains("Ctrl+Shift+J", File.ReadAllText(settingsFile), StringComparison.Ordinal);

            Assert.True(shell.ShowsSide);
            window.KeyPress(Avalonia.Input.Key.J, Avalonia.Input.RawInputModifiers.Control | Avalonia.Input.RawInputModifiers.Shift,
                Avalonia.Input.PhysicalKey.J, "J");
            Settle();
            Assert.False(shell.ShowsSide);
            Assert.Equal(0, shell.CanvasInsets.Left);
        }
        finally
        {
            File.Delete(settingsFile);
        }
    }
}
