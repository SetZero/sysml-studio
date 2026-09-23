using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysmlStudio.App.Services;
using SysmlStudio.Diagrams;

namespace SysmlStudio.App.ViewModels;

/// <summary>
/// Settings and the keyboard: the user's theme and shortcuts, and the few
/// commands that exist mainly so that a key can reach them.
/// </summary>
public sealed partial class ShellViewModel
{
    private readonly StudioSettings _settings = StudioSettings.Load();
    private ShortcutMap? _keys;

    /// <summary>The gestures in force; <c>Keys[id]</c> is a tooltip naming the shortcut.</summary>
    public ShortcutMap Keys => _keys ??= new ShortcutMap(_settings.Shortcuts);

    /// <summary>Asks the window to put the caret in the search box.</summary>
    public event Action? SearchFocusRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSide), nameof(CanvasInsets))]
    public partial bool IsSideOpen { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsInspector), nameof(CanvasInsets))]
    public partial bool IsInspectorOpen { get; set; } = true;

    public bool ShowsSide => HasWorkspace && IsSideOpen;

    public bool ShowsInspector => HasWorkspace && IsInspectorOpen;

    /// <summary>
    /// Runs a shortcut. While a dialog is open only the ones meant for dialogs
    /// act, and while the settings record a shortcut none do: the keys are
    /// the new shortcut, not a command.
    /// </summary>
    public void RunShortcut(ShortcutAction action)
    {
        if (Dialog is SettingsViewModel { Recording: not null })
            return;
        if (!HasDialog || action.InDialogs)
            action.Run(this);
    }

    public void RequestSearchFocus() => SearchFocusRequested?.Invoke();

    /// <summary>Shows the tab <paramref name="step"/> places along, wrapping round.</summary>
    public void CycleTab(int step)
    {
        if (Documents.Count == 0)
            return;

        var index = ActiveDocument is null ? 0 : Documents.IndexOf(ActiveDocument);
        ShowDocument(Documents[(((index + step) % Documents.Count) + Documents.Count) % Documents.Count]);
    }

    /// <summary>The title bar's switcher, by kind: what the Alt+number keys press.</summary>
    public void SwitchTo(DiagramKind kind)
    {
        if (DiagramKinds.FirstOrDefault(k => k.Kind == kind) is { IsAvailable: true } option)
            SwitchKindCommand.Execute(option);
    }

    /// <summary>A button of the toolbar under the diagram, by position.</summary>
    public void UseTool(int index)
    {
        if (ActiveDiagram?.Toolbox is not { } tools || index >= tools.Count)
            return;

        var tool = tools[index];
        if (tool.Relation is { } relation)
            StartRelationCommand.Execute(relation);
        else if (tool.Kind is { } kind)
            AddToDiagramCommand.Execute(kind);
    }

    [RelayCommand]
    private void GoToSource()
    {
        if (SelectedElement is { File: { } file } element)
            OpenSource(file.Path, element.Line);
    }

    /// <summary>The settings dialog: theme and shortcuts. Confirming applies and saves them.</summary>
    [RelayCommand]
    private async Task OpenSettings()
    {
        var dialog = new SettingsViewModel(IsDark, Keys);
        if (!await ShowDialog(dialog))
            return;

        IsDark = dialog.IsDark;
        Keys.Apply(dialog.Gestures());
        _settings.Shortcuts = Keys.Overrides();
        _settings.Save();
        Bottom.Log("settings saved");
    }

    /// <summary>Takes the saved theme; the shortcuts are read when <see cref="Keys"/> is first used.</summary>
    private void LoadSettings() => IsDark = _settings.Theme == "dark";

    partial void OnIsDarkChanged(bool oldValue, bool newValue)
    {
        var theme = newValue ? "dark" : "light";
        if (_settings.Theme == theme)
            return;

        _settings.Theme = theme;
        _settings.Save();
    }
}
