using System.Windows.Input;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using SysmlStudio.Diagrams;

namespace SysmlStudio.App.ViewModels;

/// <summary>Something the keyboard can do, with the gesture it has until the user changes it.</summary>
/// <param name="Id">The key it is saved under.</param>
/// <param name="Group">The heading it is listed under in the settings.</param>
/// <param name="Label">What it does, in the words of the settings and the tooltips.</param>
/// <param name="DefaultGesture">Avalonia's gesture syntax, e.g. "Ctrl+Shift+S"; empty for none.</param>
/// <param name="Run">What it does.</param>
/// <param name="InDialogs">True for the few that still act while a dialog is open.</param>
public sealed record ShortcutAction(string Id, string Group, string Label, string DefaultGesture, Action<ShellViewModel> Run,
    bool InDialogs = false);

/// <summary>Every shortcut the window offers.</summary>
public static class ShortcutCatalog
{
    public static IReadOnlyList<ShortcutAction> All { get; } =
    [
        // File
        new("open", "File", "Open folder", "Ctrl+O", s => Run(s.OpenFolderCommand)),
        new("save", "File", "Save", "Ctrl+S", s => Run(s.SaveCommand)),
        new("saveAll", "File", "Save all", "Ctrl+Shift+S", s => Run(s.SaveAllCommand)),
        new("export", "File", "Export model", "Ctrl+E", s => Run(s.ExportCommand)),
        new("exportSvg", "File", "Export diagram as SVG", "Ctrl+Shift+E", s => Run(s.ExportSvgCommand)),
        new("exportPng", "File", "Export diagram as PNG", "Ctrl+Alt+E", s => Run(s.ExportPngCommand)),
        new("closeTab", "File", "Close tab", "Ctrl+W", s => Run(s.CloseDocumentCommand, s.ActiveDocument)),
        new("nextTab", "File", "Next tab", "Ctrl+Tab", s => s.CycleTab(1)),
        new("previousTab", "File", "Previous tab", "Ctrl+Shift+Tab", s => s.CycleTab(-1)),

        // Edit
        new("undo", "Edit", "Undo", "Ctrl+Z", s => Run(s.UndoCommand)),
        new("redo", "Edit", "Redo", "Ctrl+Y", s => Run(s.RedoCommand)),
        new("rename", "Edit", "Rename", "F2", s => Run(s.RenameCommand)),
        new("delete", "Edit", "Delete", "Delete", s => Run(s.DeleteCommand)),
        new("add", "Edit", "Add inside the selection", "Insert", s => Run(s.AddCommand)),
        new("setType", "Edit", "Set type", "Ctrl+T", s => Run(s.SetTypeCommand)),
        new("specialize", "Edit", "Add specialization", "Ctrl+Shift+T", s => Run(s.SpecializeCommand)),
        new("description", "Edit", "Edit description", "Ctrl+D", s => Run(s.EditDocCommand)),
        new("satisfy", "Edit", "Satisfies a requirement", "Ctrl+Shift+R", s => Run(s.AddSatisfyCommand)),
        new("cancel", "Edit", "Cancel the tool or dialog", "Escape", s => Run(s.CancelToolCommand), InDialogs: true),

        // Navigate
        new("search", "Navigate", "Search", "Ctrl+K", s => s.RequestSearchFocus()),
        new("findUsages", "Navigate", "Find usages", "Shift+F12", s => Run(s.FindUsagesCommand)),
        new("goToSource", "Navigate", "Go to source", "F12", s => Run(s.GoToSourceCommand)),
        new("showModel", "Navigate", "Show the model", "Ctrl+Shift+D1", s => Run(s.ShowSideCommand, SidePanel.Model)),
        new("showSearch", "Navigate", "Show search", "Ctrl+Shift+F", s => Run(s.ShowSideCommand, SidePanel.Search)),
        new("problems", "Navigate", "Show or hide problems", "Ctrl+Shift+M", s => Run(s.ToggleProblemsCommand)),

        // Diagram
        new("kindText", "Diagram", "Text", "Alt+D0", s => s.SwitchTo(null)),
        new("kindDefinition", "Diagram", "Definition diagram", "Alt+D1", s => s.SwitchTo(DiagramKind.Definition)),
        new("kindInterconnection", "Diagram", "Interconnection diagram", "Alt+D2", s => s.SwitchTo(DiagramKind.Interconnection)),
        new("kindRequirements", "Diagram", "Requirements diagram", "Alt+D3", s => s.SwitchTo(DiagramKind.Requirements)),
        new("kindAction", "Diagram", "Action flow", "Alt+D4", s => s.SwitchTo(DiagramKind.ActionFlow)),
        new("kindState", "Diagram", "State machine", "Alt+D5", s => s.SwitchTo(DiagramKind.StateMachine)),
        new("autoLayout", "Diagram", "Auto-layout", "Ctrl+L", s => Run(s.RelayoutCommand)),
        new("fit", "Diagram", "Fit to view", "Ctrl+D0", s => Run(s.FitCommand)),
        new("zoomIn", "Diagram", "Zoom in", "Ctrl+OemPlus", s => s.ActiveDiagram?.ZoomInCommand.Execute(null)),
        new("zoomOut", "Diagram", "Zoom out", "Ctrl+OemMinus", s => s.ActiveDiagram?.ZoomOutCommand.Execute(null)),
        new("tool1", "Diagram", "Toolbar tool 1", "Ctrl+Alt+D1", s => s.UseTool(0)),
        new("tool2", "Diagram", "Toolbar tool 2", "Ctrl+Alt+D2", s => s.UseTool(1)),
        new("tool3", "Diagram", "Toolbar tool 3", "Ctrl+Alt+D3", s => s.UseTool(2)),
        new("tool4", "Diagram", "Toolbar tool 4", "Ctrl+Alt+D4", s => s.UseTool(3)),
        new("tool5", "Diagram", "Toolbar tool 5", "Ctrl+Alt+D5", s => s.UseTool(4)),

        // View
        new("toggleModel", "View", "Show or hide the model panel", "Ctrl+B", s => s.IsSideOpen = !s.IsSideOpen),
        new("toggleInspector", "View", "Show or hide the inspector", "Ctrl+Alt+B", s => s.IsInspectorOpen = !s.IsInspectorOpen),
        new("theme", "View", "Light or dark", "Ctrl+Shift+L", s => Run(s.ToggleThemeCommand)),
        new("settings", "View", "Settings", "Ctrl+OemComma", s => Run(s.OpenSettingsCommand)),
    ];

    public static ShortcutAction? Find(string id) => All.FirstOrDefault(a => a.Id == id);

    /// <summary>A gesture as people write it: "Ctrl+Shift+1", not "Ctrl+Shift+D1"; "Ctrl++", not "Ctrl+OemPlus".</summary>
    public static string Display(string gesture)
    {
        if (gesture.Length == 0)
            return string.Empty;

        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries);
        var key = parts[^1] switch
        {
            "OemPlus" or "Add" => "+",
            "OemMinus" or "Subtract" => "-",
            "OemComma" => ",",
            "OemPeriod" => ".",
            "Escape" => "Esc",
            "Delete" => "Del",
            "Insert" => "Ins",
            var k when k.Length == 2 && k[0] == 'D' && char.IsDigit(k[1]) => k[1..],
            var k => k,
        };
        return string.Join("+", parts[..^1].Append(key));
    }

    private static void Run(ICommand command, object? parameter = null)
    {
        if (command.CanExecute(parameter))
            command.Execute(parameter);
    }
}

/// <summary>
/// The gestures in force: each shortcut's default unless the user set another.
/// Its indexer gives the tooltip text for a shortcut, so
/// the views can bind to <c>Keys[undo]</c> and follow a change.
/// </summary>
public sealed class ShortcutMap : ObservableObject
{
    private readonly Dictionary<string, string> _gestures = [];

    public ShortcutMap(IReadOnlyDictionary<string, string> overrides) => Apply(overrides);

    /// <summary>Raised when a gesture changes, so the window can bind its keys again.</summary>
    public event Action? Changed;

    /// <summary>"Undo (Ctrl+Z)", or just "Undo" when the shortcut has no gesture.</summary>
    public string this[string id]
    {
        get
        {
            if (ShortcutCatalog.Find(id) is not { } action)
                return id;
            return Gesture(id) is { Length: > 0 } g ? $"{action.Label} ({ShortcutCatalog.Display(g)})" : action.Label;
        }
    }

    /// <summary>The gesture in Avalonia's syntax; empty when there is none.</summary>
    public string Gesture(string id) => _gestures.TryGetValue(id, out var g) ? g : string.Empty;

    /// <summary>The gesture for a menu item, or null.</summary>
    public KeyGesture? KeyGesture(string id) => Parse(Gesture(id));

    /// <summary>Only the gestures that differ from the defaults, for the settings file.</summary>
    public Dictionary<string, string> Overrides()
        => ShortcutCatalog.All.Where(a => Gesture(a.Id) != a.DefaultGesture).ToDictionary(a => a.Id, a => Gesture(a.Id));

    public void Apply(IReadOnlyDictionary<string, string> overrides)
    {
        _gestures.Clear();
        foreach (var action in ShortcutCatalog.All)
        {
            var gesture = overrides.TryGetValue(action.Id, out var chosen) ? chosen : action.DefaultGesture;
            _gestures[action.Id] = gesture.Length == 0 || Parse(gesture) is not null ? gesture : action.DefaultGesture;
        }

        OnPropertyChanged("Item[]");
        Changed?.Invoke();
    }

    public static KeyGesture? Parse(string gesture)
    {
        if (gesture.Length == 0)
            return null;
        try
        {
            return Avalonia.Input.KeyGesture.Parse(gesture);
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            return null;
        }
    }
}
