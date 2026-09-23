using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SysmlStudio.App.ViewModels;

/// <summary>One shortcut in the settings: what it does, its gesture, and whether it clashes with another.</summary>
public sealed partial class ShortcutRow(ShortcutAction action, string gesture) : ObservableObject
{
    public ShortcutAction Action { get; } = action;
    public string Label => Action.Label;

    /// <summary>Avalonia's syntax; empty for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display), nameof(IsChanged), nameof(HasGesture))]
    public partial string Gesture { get; set; } = gesture;

    /// <summary>Waiting for the keys to press.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    public partial bool IsRecording { get; set; }

    [ObservableProperty]
    public partial bool IsConflict { get; set; }

    public string Display
    {
        get
        {
            if (IsRecording)
                return "Press keys…";
            return HasGesture ? ShortcutCatalog.Display(Gesture) : "—";
        }
    }

    public bool HasGesture => Gesture.Length > 0;

    public bool IsChanged => Gesture != Action.DefaultGesture;
}

/// <summary>A heading and the shortcuts under it.</summary>
public sealed class ShortcutGroup(string name, IReadOnlyList<ShortcutRow> rows)
{
    public string Name { get; } = name;
    public IReadOnlyList<ShortcutRow> Rows { get; } = rows;
}

/// <summary>
/// The settings dialog: light or dark, and every keyboard shortcut. Clicking
/// a shortcut records the next keys pressed; two shortcuts on one gesture are
/// named, and the dialog will not save until they are apart.
/// </summary>
public sealed partial class SettingsViewModel : ModalViewModel
{
    private readonly List<ShortcutRow> _rows;

    public SettingsViewModel(bool isDark, ShortcutMap keys) : base("Settings", "Save")
    {
        IsDark = isDark;
        _rows = [.. ShortcutCatalog.All.Select(a => new ShortcutRow(a, keys.Gesture(a.Id)))];
        foreach (var row in _rows)
        {
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ShortcutRow.Gesture))
                    CheckConflicts();
            };
        }

        Rebuild();
        CheckConflicts();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLight))]
    public partial bool IsDark { get; set; }

    public bool IsLight
    {
        get => !IsDark;
        set => IsDark = !value;
    }

    /// <summary>Narrows the list to shortcuts whose name or keys contain it.</summary>
    [ObservableProperty]
    public partial string Filter { get; set; } = string.Empty;

    public ObservableCollection<ShortcutGroup> Groups { get; } = [];

    /// <summary>The row waiting for keys, if any.</summary>
    public ShortcutRow? Recording => _rows.FirstOrDefault(r => r.IsRecording);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflict))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial string? Conflict { get; set; }

    public bool HasConflict => Conflict is not null;

    /// <summary>The gestures as they stand, by shortcut id.</summary>
    public Dictionary<string, string> Gestures() => _rows.ToDictionary(r => r.Action.Id, r => r.Gesture);

    [RelayCommand]
    private void Record(ShortcutRow row)
    {
        foreach (var other in _rows)
            other.IsRecording = ReferenceEquals(other, row) && !row.IsRecording;
    }

    /// <summary>Keys were pressed while a row was recording: they become its gesture.</summary>
    public void Recorded(string gesture)
    {
        if (Recording is not { } row)
            return;

        row.IsRecording = false;
        row.Gesture = gesture;
    }

    public void StopRecording()
    {
        foreach (var row in _rows)
            row.IsRecording = false;
    }

    [RelayCommand]
    private static void Reset(ShortcutRow row) => row.Gesture = row.Action.DefaultGesture;

    [RelayCommand]
    private static void Clear(ShortcutRow row) => row.Gesture = string.Empty;

    [RelayCommand]
    private void ResetAll()
    {
        foreach (var row in _rows)
            row.Gesture = row.Action.DefaultGesture;
    }

    protected override bool CanConfirm() => !HasConflict;

    partial void OnFilterChanged(string value) => Rebuild();

    private void Rebuild()
    {
        Groups.Clear();
        var filter = Filter.Trim();
        foreach (var group in _rows.GroupBy(r => r.Action.Group))
        {
            var rows = group.Where(r => filter.Length == 0
                || r.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || r.Display.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            if (rows.Count > 0)
                Groups.Add(new ShortcutGroup(group.Key, rows));
        }
    }

    private void CheckConflicts()
    {
        var clashes = _rows.Where(r => r.HasGesture)
            .GroupBy(r => r.Gesture, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var row in _rows)
            row.IsConflict = clashes.Any(g => g.Contains(row));

        Conflict = clashes.Count == 0
            ? null
            : $"{ShortcutCatalog.Display(clashes[0].Key)} is used by {string.Join(" and ", clashes[0].Select(r => r.Label))}.";
    }
}
