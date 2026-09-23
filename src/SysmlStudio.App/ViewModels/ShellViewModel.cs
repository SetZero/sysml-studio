using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using SysmlStudio.App.Docking;
using SysmlStudio.App.Services;
using SysmlStudio.Diagrams;
using SysmlStudio.Model;
using SysmlStudio.Syntax;

namespace SysmlStudio.App.ViewModels;

/// <summary>
/// The window: one model folder, the panes over it, the documents open on it,
/// and every command the ribbon offers. Commands that edit the model through
/// the diagram are declared but disabled until the editing layer exists; text
/// edits in a source tab already work.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly StudioDockFactory _factory;
    private IShellDialogs? _dialogs;

    public ShellViewModel()
    {
        Browser = new BrowserViewModel();
        Properties = new PropertiesViewModel(this);
        Bottom = new BottomPanelViewModel(this);
        Welcome = new WelcomeViewModel(this);

        _factory = new StudioDockFactory(Browser, Properties, Bottom, Welcome);
        Layout = _factory.CreateLayout();
        _factory.InitLayout(Layout);
        _factory.ActiveDockableChanged += (_, e) => OnDockActiveChanged(e.Dockable);

        Browser.SelectionChanged += OnBrowserSelection;
        Bottom.Log("SysML Studio started");
    }

    public IRootDock Layout { get; }
    public BrowserViewModel Browser { get; }
    public PropertiesViewModel Properties { get; }
    public BottomPanelViewModel Bottom { get; }
    public WelcomeViewModel Welcome { get; }

    public SysmlWorkspace? Workspace { get; private set; }

    public bool HasWorkspace => Workspace is not null;

    /// <summary>The line in the middle of the title strip.</summary>
    [ObservableProperty]
    public partial string WindowTitle { get; set; } = "no workspace";

    [ObservableProperty]
    public partial bool IsRibbonCollapsed { get; set; }

    [ObservableProperty]
    public partial int RibbonTab { get; set; } = 1;

    [ObservableProperty]
    public partial bool IsDark { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>Qualified names the search box offers.</summary>
    public ObservableCollection<string> SearchItems { get; } = [];

    // ----- status bar -------------------------------------------------------

    [ObservableProperty]
    public partial string IndexedSummary { get; set; } = "idle";

    [ObservableProperty]
    public partial string Caret { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SaveState { get; set; } = "saved";

    [ObservableProperty]
    public partial string Branch { get; set; } = string.Empty;

    public static string Runtime => $"net{Environment.Version.Major}.{Environment.Version.Minor}";

    public int ErrorCount => Bottom.ErrorCount;

    public int WarningCount => Bottom.WarningCount;

    public string ErrorText => ErrorCount == 1 ? "1 error" : $"{ErrorCount} errors";

    public string WarningText => WarningCount == 1 ? "1 warning" : $"{WarningCount} warnings";

    // ----- what is active ---------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDiagram), nameof(ActiveSource), nameof(ActiveKind))]
    [NotifyCanExecuteChangedFor(nameof(ExportSvgCommand), nameof(ExportPngCommand), nameof(RelayoutCommand),
        nameof(FitCommand), nameof(UndoCommand), nameof(RedoCommand))]
    public partial IDockable? ActiveDocument { get; set; }

    public DiagramDocumentViewModel? ActiveDiagram => ActiveDocument as DiagramDocumentViewModel;

    public SourceDocumentViewModel? ActiveSource => ActiveDocument as SourceDocumentViewModel;

    public DiagramKind? ActiveKind => ActiveDiagram?.Diagram.Kind;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDiagramCommand), nameof(FindUsagesCommand))]
    public partial Element? SelectedElement { get; set; }

    public void Attach(IShellDialogs dialogs) => _dialogs = dialogs;

    // ----- opening ----------------------------------------------------------

    [RelayCommand]
    private async Task OpenFolder()
    {
        if (_dialogs is not null && await _dialogs.PickFolderAsync() is { } folder)
            Open(folder);
    }

    /// <summary>Reads a folder of .sysml files and shows what is in it.</summary>
    public void Open(string folder)
    {
        if (!Directory.Exists(folder))
            return;

        CloseAllDocuments();
        Workspace = SysmlWorkspace.Load(folder);
        _history.Clear();
        RefreshMaturityKeywords();

        Browser.Show(Workspace);
        Welcome.OpenFolderName = ShortFolder(folder);
        WindowTitle = ShortFolder(folder);
        Branch = GitBranch.Of(folder) ?? string.Empty;
        RecentFolders.Remember(folder, Workspace.Files.Count);
        Welcome.Refresh();

        SearchItems.Clear();
        foreach (var element in Workspace.Elements.Where(e => e.Name is not null))
            SearchItems.Add(element.QualifiedName);

        RefreshProblems();
        Bottom.Log($"opened {folder}: {Workspace.Files.Count} files, {Workspace.Elements.Count()} elements");

        // Whatever was open last time, where it was left.
        foreach (var diagram in DiagramStore.Reopen(DiagramStore.Load(folder), Workspace))
            _factory.Show(CreateDiagram(diagram, laidOut: true));

        if (_factory.Documents.VisibleDockables?.Count > 1)
            HideWelcome();

        OnPropertyChanged(nameof(HasWorkspace));
        RefreshStatus();
    }

    // ----- documents --------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanOpenDiagram))]
    private void OpenDiagram(DiagramKind kind)
    {
        if (SelectedElement is not { } element)
            return;

        var id = $"diagram:{kind}:{element.QualifiedName}";
        var existing = _factory.Documents.VisibleDockables?.FirstOrDefault(d => d.Id == id);
        _factory.Show(existing ?? CreateDiagram(DiagramBuilder.Build(kind, element)));
        HideWelcome();
    }

    private bool CanOpenDiagram(DiagramKind kind)
        => SelectedElement is { } e && DiagramBuilder.KindsFor(e).Contains(kind);

    /// <summary>Opens a file in a source tab, at a line when one is given.</summary>
    public void OpenSource(string path, int line = 0)
    {
        if (Workspace is not { } workspace || !File.Exists(path))
            return;

        var id = "source:" + path;
        if (_factory.Documents.VisibleDockables?.FirstOrDefault(d => d.Id == id) is not SourceDocumentViewModel document)
        {
            var text = workspace.Files.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))?.Text
                ?? File.ReadAllText(path);
            var folderName = Path.GetFileName(workspace.Directory.TrimEnd('\\', '/'));
            document = new SourceDocumentViewModel(this, path, $"{folderName}/{workspace.RelativePath(path)}", text);
        }

        _factory.Show(document);
        HideWelcome();
        if (line > 0)
            document.GoToLine(line);
    }

    /// <summary>Selects an element everywhere: browser, properties, and the canvas if it is on it.</summary>
    public void Select(Element element)
    {
        Browser.Reveal(element);
        SelectedElement = element;
        Properties.Element = element;
        FollowSelection(element);
    }

    private void OnBrowserSelection(Element? element)
    {
        SelectedElement = element;
        Properties.Element = element;
        if (element is not null && !_selecting)
            FollowSelection(element);
    }

    /// <summary>
    /// The diagram in front follows the selection, as Enterprise Architect's
    /// does. An element already on it is selected there; any other element is
    /// drawn in the same tab — as the same kind of diagram when it can be, as
    /// its first kind otherwise — so browsing does not leave a trail of tabs.
    /// An element no diagram can be drawn of leaves the canvas as it is.
    /// </summary>
    private void FollowSelection(Element element)
    {
        if (ActiveDiagram is not { } current)
            return;

        if (current.Nodes.FirstOrDefault(n => ReferenceEquals(n.Element, element) && !n.IsPseudoNode) is { } onCanvas)
        {
            foreach (var node in current.Nodes)
                node.IsSelected = ReferenceEquals(node, onCanvas);
            return;
        }

        var kinds = DiagramBuilder.KindsFor(element);
        if (kinds.Count == 0)
            return;

        var kind = kinds.Contains(current.Diagram.Kind) ? current.Diagram.Kind : kinds[0];
        var id = $"diagram:{kind}:{element.QualifiedName}";
        var open = _factory.Documents.VisibleDockables?.FirstOrDefault(d => d.Id == id);
        if (open is not null)
        {
            _factory.Show(open);
            return;
        }

        var replacement = CreateDiagram(DiagramBuilder.Build(kind, element));
        _factory.Replace(current, replacement);
    }

    private void OnDockActiveChanged(IDockable? dockable)
    {
        if (dockable is not (DiagramDocumentViewModel or SourceDocumentViewModel or WelcomeViewModel))
            return;

        ActiveDocument = dockable;
        var folder = Workspace is null ? "no workspace" : ShortFolder(Workspace.Directory);
        WindowTitle = dockable switch
        {
            DiagramDocumentViewModel d => $"{folder}  —  {d.Diagram.Root.DisplayName}  ·  {DiagramDocumentViewModel.KindName(d.Diagram.Kind)}",
            SourceDocumentViewModel s => $"{folder}  —  {s.Title}",
            _ => folder,
        };
        RefreshStatus();
    }

    private void HideWelcome()
    {
        if (_factory.Documents.VisibleDockables?.Contains(Welcome) == true && _factory.Documents.VisibleDockables.Count > 1)
            _factory.RemoveDockable(Welcome, collapse: false);
    }

    private void CloseAllDocuments()
    {
        var open = _factory.Documents.VisibleDockables?.ToList() ?? [];
        foreach (var document in open.Where(d => !ReferenceEquals(d, Welcome)))
            _factory.CloseDockable(document);
    }

    // ----- saving -----------------------------------------------------------

    /// <summary>Writes every changed file, and the diagram layout.</summary>
    [RelayCommand]
    private void Save() => SaveAll();

    [RelayCommand]
    private void SaveAll()
    {
        if (Workspace is not { } workspace)
            return;

        foreach (var source in OpenSources())
            source.FlushPendingReparse();

        var written = workspace.DirtyFiles.ToList();
        foreach (var path in written)
        {
            workspace.Save(path);
            Bottom.Log($"saved {workspace.RelativePath(path)}");
        }

        SaveLayout();
        AfterModelChange();
    }

    /// <summary>Writes which diagrams are open and where their nodes sit. Nothing of it goes into the model.</summary>
    private void SaveLayout()
    {
        if (Workspace is not { } workspace)
            return;

        var stored = new StoredDiagrams();
        foreach (var diagram in OpenDiagrams())
        {
            diagram.PushPositions();
            stored.Diagrams.Add(DiagramStore.Capture(diagram.Diagram));
        }

        DiagramStore.Save(workspace.Directory, stored);
        Bottom.Log($"layout saved to {DiagramStore.PathFor(workspace.Directory)}");
        RefreshStatus();
    }

    /// <summary>
    /// A source tab re-parsed after typing. Its text becomes the model's text
    /// for that file, so edits made from a diagram start from what was typed;
    /// the views are rebuilt on Save, not on every pause in typing.
    /// </summary>
    public void OnSourceReparsed(SourceDocumentViewModel source, SourceFile parsed)
    {
        if (Workspace is not { } workspace)
            return;

        var current = workspace.Files.FirstOrDefault(f => string.Equals(f.Path, source.Path, StringComparison.OrdinalIgnoreCase));
        if (current is not null && current.Text != parsed.Text)
        {
            workspace.Update(source.Path, parsed.Text);

            // Model undo restores whole files; after typing it would undo the typing too.
            _history.Clear();
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        }

        source.IsDirty = workspace.IsDirty(source.Path);
        RefreshProblems();
        RefreshStatus();
    }

    // ----- history ----------------------------------------------------------

    /// <summary>In a source tab, the editor's own undo; anywhere else, the last model edit.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (ActiveSource is { } source)
        {
            source.Text.UndoStack.Undo();
            return;
        }

        if (Workspace is { } workspace && _history.Undo(workspace) is { } record)
        {
            Bottom.Log("undo: " + record.Description);
            AfterModelChange();
        }
    }

    private bool CanUndo() => ActiveSource is not null || _history.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (ActiveSource is { } source)
        {
            source.Text.UndoStack.Redo();
            return;
        }

        if (Workspace is { } workspace && _history.Redo(workspace) is { } record)
        {
            Bottom.Log("redo: " + record.Description);
            AfterModelChange();
        }
    }

    private bool CanRedo() => ActiveSource is not null || _history.CanRedo;

    // ----- element ----------------------------------------------------------

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void FindUsages()
    {
        if (Workspace is { } workspace && SelectedElement is { } element)
            Bottom.ShowUsages(workspace, element);
    }

    private bool HasSelection() => SelectedElement is not null && Workspace is not null;


    // ----- arrange and export -----------------------------------------------

    [RelayCommand(CanExecute = nameof(HasDiagram))]
    private void Relayout() => ActiveDiagram?.Relayout();

    [RelayCommand(CanExecute = nameof(HasDiagram))]
    private void Fit() => ActiveDiagram?.Fit();

    private bool HasDiagram() => ActiveDiagram is not null;

    [RelayCommand(CanExecute = nameof(HasDiagram))]
    private async Task ExportSvg()
    {
        if (ActiveDiagram is not { } diagram || _dialogs is null)
            return;

        if (await _dialogs.PickSaveFileAsync(FileNameFor(diagram), "svg") is not { } path)
            return;

        diagram.PushPositions();
        SvgExporter.Write(diagram.Diagram, path);
        Bottom.Log($"exported {path}");
    }

    [RelayCommand(CanExecute = nameof(HasDiagram))]
    private async Task ExportPng()
    {
        if (ActiveDiagram is not { } diagram || _dialogs is null)
            return;

        if (await _dialogs.PickSaveFileAsync(FileNameFor(diagram), "png") is not { } path)
            return;

        diagram.RenderPng?.Invoke(path);
        Bottom.Log($"exported {path}");
    }

    // ----- view -------------------------------------------------------------

    [RelayCommand]
    private void ToggleRibbon() => IsRibbonCollapsed = !IsRibbonCollapsed;

    [RelayCommand]
    private void ToggleTheme() => IsDark = !IsDark;

    partial void OnIsDarkChanged(bool value)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = value ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    partial void OnSearchTextChanged(string value)
    {
        if (Workspace?.Find(value) is { } element)
            Select(element);
    }

    // ----- status -----------------------------------------------------------

    public void RefreshStatus()
    {
        IndexedSummary = Workspace is { } w
            ? string.Create(CultureInfo.InvariantCulture, $"indexed {w.Files.Count} files  ·  {w.Elements.Count():N0} elements").Replace(',', ' ')
            : "idle";
        SaveState = Workspace?.DirtyFiles.Any() == true || OpenSources().Any(s => s.IsDirty) ? "modified" : "saved";
    }

    public void SetCaret(int line, int column)
        => Caret = string.Create(CultureInfo.InvariantCulture, $"Ln {line}, Col {column}");

    private void RefreshProblems()
    {
        // The workspace holds what source tabs typed, so its diagnostics are the live ones.
        var diagnostics = Workspace?.Diagnostics.ToList() ?? [];
        Bottom.ShowProblems(Workspace, diagnostics.OrderBy(d => d.Severity).ThenBy(d => d.File, StringComparer.Ordinal).ThenBy(d => d.Line));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(WarningText));
    }

    private IEnumerable<SourceDocumentViewModel> OpenSources()
        => _factory.Documents.VisibleDockables?.OfType<SourceDocumentViewModel>() ?? [];

    private IEnumerable<DiagramDocumentViewModel> OpenDiagrams()
        => _factory.Documents.VisibleDockables?.OfType<DiagramDocumentViewModel>() ?? [];

    private static string ShortFolder(string folder)
    {
        var trimmed = folder.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        var parent = Path.GetFileName(Path.GetDirectoryName(trimmed) ?? string.Empty);
        var grand = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(trimmed) ?? string.Empty) ?? string.Empty);
        return string.Join('/', new[] { grand, parent, name }.Where(p => p.Length > 0));
    }

    private static string FileNameFor(DiagramDocumentViewModel diagram)
    {
        var name = $"{diagram.Diagram.Root.DisplayName}-{diagram.Diagram.Kind}";
        return string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
    }
}
