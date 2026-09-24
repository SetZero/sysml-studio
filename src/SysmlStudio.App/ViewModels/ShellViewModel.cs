using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysmlStudio.App.Services;
using SysmlStudio.Diagrams;
using SysmlStudio.Model;
using SysmlStudio.Syntax;

namespace SysmlStudio.App.ViewModels;

/// <summary>A document in the tab strip: a diagram or a source file.</summary>
public abstract partial class DocumentViewModel : ObservableObject
{
    public string Id { get; protected init; } = string.Empty;

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>The dot after the title: unsaved changes, or a file that does not parse.</summary>
    [ObservableProperty]
    public partial bool HasDot { get; set; }

    /// <summary>The tab in front.</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>Source tabs name a file, so their title is set in the mono face.</summary>
    public virtual bool IsSource => false;

    /// <summary>What the tab shows when hovered: the diagram kind, or the file's path.</summary>
    public string ToolTip { get; protected init; } = string.Empty;
}

/// <summary>One segment of the title bar's switcher: a kind of diagram, or the element's text when <see cref="Kind"/> is null.</summary>
public sealed partial class DiagramKindOption(DiagramKind? kind, string label) : ObservableObject
{
    public DiagramKind? Kind { get; } = kind;
    public string Label { get; } = label;

    [ObservableProperty]
    public partial bool IsAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }
}

/// <summary>Which list the side panel shows.</summary>
public enum SidePanel
{
    Model,
    Search,
}

/// <summary>
/// The window: one model folder, the tree over it, the documents open on it,
/// the inspector for what is selected, and the problems and usages panel.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private IShellDialogs? _dialogs;

    public ShellViewModel()
    {
        Browser = new BrowserViewModel();
        Properties = new PropertiesViewModel(this);
        Bottom = new BottomPanelViewModel(this);
        Welcome = new WelcomeViewModel(this);

        DiagramKinds =
        [
            new(null, "Text"),
            new(DiagramKind.Definition, "Definition"),
            new(DiagramKind.Interconnection, "Interconnection"),
            new(DiagramKind.Requirements, "Requirements"),
            new(DiagramKind.ActionFlow, "Action"),
            new(DiagramKind.StateMachine, "State"),
        ];

        Browser.SelectionChanged += OnBrowserSelection;
        LoadSettings();
        Bottom.Log("SysML Studio started");
    }

    public BrowserViewModel Browser { get; }
    public PropertiesViewModel Properties { get; }
    public BottomPanelViewModel Bottom { get; }
    public WelcomeViewModel Welcome { get; }

    public SysmlWorkspace? Workspace { get; private set; }

    public bool HasWorkspace => Workspace is not null;

    public ObservableCollection<DocumentViewModel> Documents { get; } = [];

    public bool HasDocuments => Documents.Count > 0;

    /// <summary>A model is open but nothing is drawn yet: the centre says what to do.</summary>
    public bool ShowsHint => HasWorkspace && !HasDocuments;

    public ObservableCollection<DiagramKindOption> DiagramKinds { get; }

    /// <summary>The width of the model panel that floats over the left of the documents.</summary>
    public static double SideWidth => 232;

    /// <summary>The width of the inspector that floats over the right of the documents.</summary>
    public static double InspectorWidth => 272;

    /// <summary>
    /// How much of the document area the floating side panels cover. The
    /// canvas runs on underneath them; tabs, panels and controls keep clear.
    /// </summary>
    public Thickness CanvasInsets => new(ShowsSide ? SideWidth : 0, 0, ShowsInspector ? InspectorWidth : 0, 0);

    /// <summary>The switcher shows whenever a model is open: over diagrams, over text, and over an empty centre.</summary>
    public bool ShowsKinds => HasWorkspace;

    // ----- title bar ----------------------------------------------------------

    /// <summary>"ferrix": the workspace, first part of the breadcrumb.</summary>
    [ObservableProperty]
    public partial string WorkspaceName { get; set; } = "No workspace";

    /// <summary>"Scheduling · Requirements", or the file name: what is in front.</summary>
    [ObservableProperty]
    public partial string DocumentCaption { get; set; } = string.Empty;

    public bool HasDocumentCaption => DocumentCaption.Length > 0;

    [ObservableProperty]
    public partial bool IsDark { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>Qualified names the search box offers.</summary>
    public ObservableCollection<string> SearchItems { get; } = [];

    /// <summary>What the side panel's search finds.</summary>
    public ObservableCollection<Element> SearchResults { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsModel), nameof(ShowsSearch))]
    public partial SidePanel Side { get; set; }

    public bool ShowsModel => Side == SidePanel.Model;

    public bool ShowsSearch => Side == SidePanel.Search;

    // ----- status bar -----------------------------------------------------------

    [ObservableProperty]
    public partial string SaveState { get; set; } = "Ready";

    [ObservableProperty]
    public partial string Caret { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Branch { get; set; } = string.Empty;

    public int ErrorCount => Bottom.ErrorCount;

    public int WarningCount => Bottom.WarningCount;

    public bool HasProblems => ErrorCount + WarningCount > 0;

    public bool HasErrors => ErrorCount > 0;

    /// <summary>"1 error, 2 warnings".</summary>
    public string ProblemSummary
    {
        get
        {
            var errors = ErrorCount == 1 ? "1 error" : $"{ErrorCount} errors";
            var warnings = WarningCount == 1 ? "1 warning" : $"{WarningCount} warnings";
            return HasProblems ? $"{errors}, {warnings}" : "No problems";
        }
    }

    // ----- what is active -------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDiagram), nameof(ActiveSource), nameof(ActiveKind))]
    [NotifyCanExecuteChangedFor(nameof(ExportSvgCommand), nameof(ExportPngCommand), nameof(RelayoutCommand),
        nameof(FitCommand), nameof(UndoCommand), nameof(RedoCommand))]
    public partial DocumentViewModel? ActiveDocument { get; set; }

    public DiagramDocumentViewModel? ActiveDiagram => ActiveDocument as DiagramDocumentViewModel;

    public SourceDocumentViewModel? ActiveSource => ActiveDocument as SourceDocumentViewModel;

    public DiagramKind? ActiveKind => ActiveDiagram?.Diagram.Kind;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDiagramCommand), nameof(FindUsagesCommand))]
    public partial Element? SelectedElement { get; set; }

    public void Attach(IShellDialogs dialogs) => _dialogs = dialogs;

    // ----- opening --------------------------------------------------------------

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

        Documents.Clear();
        ActiveDocument = null;
        Workspace = SysmlWorkspace.Load(folder);
        _history.Clear();
        RefreshMaturityKeywords();

        Browser.Show(Workspace);
        WorkspaceName = DisplayName(folder);
        Welcome.OpenFolderName = WorkspaceName;
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
            ShowDocument(CreateDiagram(diagram, laidOut: true));

        OnPropertyChanged(nameof(HasWorkspace));
        OnPropertyChanged(nameof(ShowsSide));
        OnPropertyChanged(nameof(ShowsInspector));
        OnPropertyChanged(nameof(CanvasInsets));
        OnPropertyChanged(nameof(ShowsKinds));
        OnDocumentsChanged();
        RefreshDiagramKinds();
        RefreshStatus();
    }

    // ----- documents --------------------------------------------------------------

    /// <summary>Shows a document, adding it to the tabs first if it is not open.</summary>
    public void ShowDocument(DocumentViewModel document)
    {
        if (!Documents.Contains(document))
            Documents.Add(document);
        ActiveDocument = document;
        OnDocumentsChanged();
    }

    [RelayCommand]
    private void Activate(DocumentViewModel document) => ShowDocument(document);

    /// <summary>Puts <paramref name="replacement"/> in <paramref name="current"/>'s tab.</summary>
    public void ReplaceDocument(DocumentViewModel current, DocumentViewModel replacement)
    {
        var index = Documents.IndexOf(current);
        if (index < 0)
        {
            ShowDocument(replacement);
            return;
        }

        var wasActive = ReferenceEquals(ActiveDocument, current);
        Documents[index] = replacement;
        if (wasActive)
            ActiveDocument = replacement;
        OnDocumentsChanged();
    }

    [RelayCommand]
    private void CloseDocument(DocumentViewModel? document)
    {
        if (document is null)
            return;

        var index = Documents.IndexOf(document);
        Documents.Remove(document);
        if (ReferenceEquals(ActiveDocument, document))
            ActiveDocument = Documents.Count == 0 ? null : Documents[Math.Clamp(index - 1, 0, Documents.Count - 1)];
        OnDocumentsChanged();
    }

    private DocumentViewModel? FindDocument(string id) => Documents.FirstOrDefault(d => d.Id == id);

    private void OnDocumentsChanged()
    {
        OnPropertyChanged(nameof(HasDocuments));
        OnPropertyChanged(nameof(ShowsHint));
    }

    /// <summary>Draws the selection as a diagram, in the tab in front when that is text nobody has typed in.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenDiagram))]
    private void OpenDiagram(DiagramKind kind)
    {
        if (SelectedElement is not { } element)
            return;

        if (FindDocument($"diagram:{kind}:{element.QualifiedName}") is { } open)
            ShowDocument(open);
        else if (ActiveSource is { IsDirty: false } text)
            ReplaceDocument(text, CreateDiagram(DiagramBuilder.Build(kind, element)));
        else
            ShowDocument(CreateDiagram(DiagramBuilder.Build(kind, element)));
    }

    private bool CanOpenDiagram(DiagramKind kind)
        => SelectedElement is { } e && DiagramBuilder.KindsFor(e).Contains(kind);

    /// <summary>
    /// The title bar's switcher: shows the selection as its text or as another
    /// kind of diagram, in the same tab.
    /// </summary>
    [RelayCommand]
    private void SwitchKind(DiagramKindOption option)
    {
        var target = SelectedElement ?? ActiveDiagram?.Diagram.Root;
        if (target is null || !ViewsFor(target).Contains(option.Kind))
            return;

        ShowView(target, option.Kind);
    }

    /// <summary>What the switcher offers for an element, in its order: the text it is written in, then its diagrams.</summary>
    private static List<DiagramKind?> ViewsFor(Element element)
        => [null, .. DiagramBuilder.KindsFor(element).Select(k => (DiagramKind?)k)];

    /// <summary>
    /// Shows an element as text (<paramref name="kind"/> null) or as a
    /// diagram: in its own tab when that is open already, otherwise in the tab
    /// in front, so browsing does not leave a trail of tabs.
    /// </summary>
    private void ShowView(Element element, DiagramKind? kind)
    {
        if (kind is not { } diagramKind)
        {
            ShowText(element);
            return;
        }

        if (FindDocument($"diagram:{diagramKind}:{element.QualifiedName}") is { } open)
        {
            ShowDocument(open);
            return;
        }

        var diagram = CreateDiagram(DiagramBuilder.Build(diagramKind, element));
        if (Replaceable is { } current)
            ReplaceDocument(current, diagram);
        else
            ShowDocument(diagram);
    }

    /// <summary>The file an element is written in, scrolled to it, without taking the focus from the tree.</summary>
    private void ShowText(Element element)
    {
        if (SourceFor(element.File.Path) is not { } document)
            return;

        if (Documents.Contains(document) || Replaceable is not { } current)
            ShowDocument(document);
        else
            ReplaceDocument(current, document);
        document.GoToLine(element.Line, focus: false);
    }

    /// <summary>The tab in front, when showing something else may take it over: a diagram, or text with no unsaved typing.</summary>
    private DocumentViewModel? Replaceable => ActiveDocument switch
    {
        DiagramDocumentViewModel diagram => diagram,
        SourceDocumentViewModel { IsDirty: false } text => text,
        _ => null,
    };

    private void RefreshDiagramKinds()
    {
        var target = SelectedElement ?? ActiveDiagram?.Diagram.Root;
        var available = target is null ? [] : ViewsFor(target);
        foreach (var option in DiagramKinds)
        {
            // What is in front is never greyed out, even with nothing selected to switch.
            option.IsActive = option.Kind is null ? ActiveSource is not null : ActiveKind == option.Kind;
            option.IsAvailable = option.IsActive || available.Contains(option.Kind);
        }
    }

    /// <summary>Opens a file in a source tab, at a line when one is given.</summary>
    public void OpenSource(string path, int line = 0)
    {
        if (SourceFor(path) is not { } document)
            return;

        ShowDocument(document);
        if (line > 0)
            document.GoToLine(line);
    }

    /// <summary>The source tab for a file: the open one, or a new one not yet shown.</summary>
    private SourceDocumentViewModel? SourceFor(string path)
    {
        if (Workspace is not { } workspace || !File.Exists(path))
            return null;

        if (FindDocument("source:" + path) is SourceDocumentViewModel open)
            return open;

        var text = workspace.Files.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))?.Text
            ?? File.ReadAllText(path);
        return new SourceDocumentViewModel(this, path, workspace.RelativePath(path), text);
    }

    /// <summary>Selects an element everywhere: tree, inspector, and the canvas if it is on it.</summary>
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
    /// The centre follows the selection, so browsing always shows something.
    /// Over a diagram, an element already on it is selected there; any other
    /// element is drawn in the same tab, as the same kind of diagram when it
    /// can be and as its first kind otherwise, and an element with no diagram
    /// leaves the diagram as it is. Over text, or over nothing, the element's
    /// text is shown.
    /// </summary>
    private void FollowSelection(Element element)
    {
        if (ActiveDiagram is not { } current)
        {
            ShowText(element);
            return;
        }

        if (current.Nodes.FirstOrDefault(n => ReferenceEquals(n.Element, element) && !n.IsPseudoNode) is { } onCanvas)
        {
            foreach (var node in current.Nodes)
                node.IsSelected = ReferenceEquals(node, onCanvas);
            return;
        }

        var kinds = DiagramBuilder.KindsFor(element);
        if (kinds.Count == 0)
            return;

        ShowView(element, kinds.Contains(current.Diagram.Kind) ? current.Diagram.Kind : kinds[0]);
    }

    partial void OnActiveDocumentChanged(DocumentViewModel? oldValue, DocumentViewModel? newValue)
    {
        oldValue?.IsActive = false;
        newValue?.IsActive = true;
        OnActiveDocumentChanged(newValue);
    }

    partial void OnActiveDocumentChanged(DocumentViewModel? value)
    {
        OnPropertyChanged(nameof(ShowsKinds));
        value?.IsActive = true;
        DocumentCaption = value switch
        {
            DiagramDocumentViewModel d => $"{d.Diagram.Root.DisplayName} · {DiagramDocumentViewModel.KindName(d.Diagram.Kind)}",
            SourceDocumentViewModel s => s.Title,
            _ => string.Empty,
        };
        OnPropertyChanged(nameof(HasDocumentCaption));
        RefreshDiagramKinds();
        RefreshStatus();
    }

    partial void OnSelectedElementChanged(Element? value) => RefreshDiagramKinds();

    // ----- saving ---------------------------------------------------------------------

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

        foreach (var path in workspace.DirtyFiles.ToList())
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

    // ----- history ----------------------------------------------------------------------

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

    // ----- problems and usages ------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void FindUsages()
    {
        if (Workspace is { } workspace && SelectedElement is { } element)
            Bottom.ShowUsages(workspace, element);
    }

    private bool HasSelection() => SelectedElement is not null && Workspace is not null;

    [RelayCommand]
    private void ToggleProblems()
    {
        if (Bottom.IsOpen && Bottom.ShowsProblems)
            Bottom.IsOpen = false;
        else
            Bottom.OpenProblems();
    }

    // ----- side panel ------------------------------------------------------------------

    [RelayCommand]
    private void ShowSide(SidePanel panel) => Side = panel;

    /// <summary>A search result was clicked.</summary>
    [RelayCommand]
    private void Reveal(Element element) => Select(element);

    partial void OnSearchTextChanged(string value)
    {
        SearchResults.Clear();
        if (Workspace is null || value.Trim().Length < 2)
            return;

        foreach (var element in Workspace.Elements
                     .Where(e => e.Name is not null && e.QualifiedName.Contains(value.Trim(), StringComparison.OrdinalIgnoreCase))
                     .Take(80))
            SearchResults.Add(element);

        if (Workspace.Find(value) is { } exact)
            Select(exact);
    }

    // ----- arrange and export -------------------------------------------------------------

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

    // ----- view ---------------------------------------------------------------------------

    [RelayCommand]
    private void ToggleTheme() => IsDark = !IsDark;

    partial void OnIsDarkChanged(bool value)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = value ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    // ----- status -----------------------------------------------------------------------

    public void RefreshStatus()
    {
        var dirty = Workspace?.DirtyFiles.Any() == true || OpenSources().Any(s => s.IsDirty);
        if (Workspace is null)
            SaveState = "Ready";
        else
            SaveState = dirty ? "Unsaved changes · Ctrl+S saves" : "Saved";
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
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(ProblemSummary));
    }

    private IEnumerable<SourceDocumentViewModel> OpenSources() => Documents.OfType<SourceDocumentViewModel>();

    private IEnumerable<DiagramDocumentViewModel> OpenDiagrams() => Documents.OfType<DiagramDocumentViewModel>();

    /// <summary>
    /// What to call a workspace: its folder, or the folder above when the
    /// folder has a generic name.
    /// </summary>
    public static string DisplayName(string folder)
    {
        string[] generic = ["sysml", "model", "models", "docs", "doc", "src", "spec", "specs"];
        var dir = new DirectoryInfo(folder.TrimEnd('\\', '/'));
        while (dir.Parent is not null && generic.Contains(dir.Name.ToLowerInvariant()))
            dir = dir.Parent;
        return dir.Name;
    }

    private static string FileNameFor(DiagramDocumentViewModel diagram)
    {
        var name = $"{diagram.Diagram.Root.DisplayName}-{diagram.Diagram.Kind}";
        return string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
    }
}
