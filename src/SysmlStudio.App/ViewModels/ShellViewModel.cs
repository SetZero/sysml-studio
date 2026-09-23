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
    private const string NotYet = "Graphical editing is the next milestone; edit the source tab for now.";

    private readonly StudioDockFactory _factory;
    private readonly Dictionary<string, IReadOnlyList<SyntaxError>> _liveErrors = new(StringComparer.OrdinalIgnoreCase);
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

    public static string NotYetHint => NotYet;

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
        _liveErrors.Clear();

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
            _factory.Show(new DiagramDocumentViewModel(diagram, laidOut: true));

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
        _factory.Show(existing ?? new DiagramDocumentViewModel(DiagramBuilder.Build(kind, element)));
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
    }

    private void OnBrowserSelection(Element? element)
    {
        SelectedElement = element;
        Properties.Element = element;
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

    [RelayCommand]
    private void Save()
    {
        if (ActiveSource is { IsDirty: true } source)
            source.Save();
        SaveLayout();
    }

    [RelayCommand]
    private void SaveAll()
    {
        foreach (var source in OpenSources().Where(s => s.IsDirty))
            source.Save();
        SaveLayout();
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

    /// <summary>A source tab re-parsed after typing: its errors replace the file's in the Problems pane.</summary>
    public void OnSourceReparsed(SourceDocumentViewModel source, SourceFile parsed)
    {
        _liveErrors[source.Path] = parsed.Errors;
        RefreshProblems();
    }

    /// <summary>A source tab was written to disk: the model is re-indexed from it.</summary>
    public void OnSourceSaved(SourceDocumentViewModel source, string text)
    {
        if (Workspace is not { } workspace)
            return;

        workspace.Update(source.Path, text);
        _liveErrors.Remove(source.Path);
        Browser.Show(workspace);
        RefreshProblems();
        Bottom.Log($"saved {workspace.RelativePath(source.Path)}");
    }

    // ----- history ----------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => ActiveSource?.Text.UndoStack.Undo();

    private bool CanUndo() => ActiveSource is not null;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Redo() => ActiveSource?.Text.UndoStack.Redo();

    // ----- element ----------------------------------------------------------

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void FindUsages()
    {
        if (Workspace is { } workspace && SelectedElement is { } element)
            Bottom.ShowUsages(workspace, element);
    }

    private bool HasSelection() => SelectedElement is not null && Workspace is not null;

    /// <summary>The graphical edit operations: present, and honest that they are not wired yet.</summary>
    [RelayCommand(CanExecute = nameof(CanEditGraphically))]
    private void EditOperation(string _)
    {
        // Never runs: CanEditGraphically is false until the editing layer lands.
    }

    private static bool CanEditGraphically(string _) => false;

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
        SaveState = OpenSources().Any(s => s.IsDirty) ? "modified" : "saved";
    }

    public void SetCaret(int line, int column)
        => Caret = string.Create(CultureInfo.InvariantCulture, $"Ln {line}, Col {column}");

    private void RefreshProblems()
    {
        var diagnostics = Workspace?.Diagnostics.ToList() ?? [];

        // A source tab's live parse replaces the file's saved errors.
        foreach (var (path, errors) in _liveErrors)
        {
            diagnostics.RemoveAll(d => d.Severity == Severity.Error && string.Equals(d.File, path, StringComparison.OrdinalIgnoreCase));
            diagnostics.AddRange(errors.Select(e => new Diagnostic(Severity.Error, path, e.Line, e.Column + 1, e.Message)));
        }

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
