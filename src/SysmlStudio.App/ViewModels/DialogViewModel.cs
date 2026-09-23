using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SysmlStudio.App.ViewModels;

/// <summary>
/// A modal over the window. Awaiting <see cref="Result"/> gives true for the
/// confirm button, false for cancel or Escape.
/// </summary>
public abstract partial class ModalViewModel(string title, string confirm) : ObservableObject
{
    private readonly TaskCompletionSource<bool> _result = new();

    public string Title { get; } = title;
    public string ConfirmText { get; } = confirm;

    public Task<bool> Result => _result.Task;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => _result.TrySetResult(true);

    protected virtual bool CanConfirm() => true;

    [RelayCommand]
    private void Cancel() => _result.TrySetResult(false);
}

/// <summary>How many of a plan's patches land in one file.</summary>
public sealed record FileChange(string File, int Count);

/// <summary>What a dialog shows of a plan while the user types: its lines, its files, or why it cannot be made.</summary>
public sealed record DialogPreview(IReadOnlyList<string> Lines, IReadOnlyList<FileChange> Files, string? Error);

/// <summary>
/// The one dialog every edit operation uses: a title, the fields that
/// operation needs, what it will change, and a message when the input will
/// not do. A rename shows how many references each file gets; the others show
/// the lines they will write.
/// </summary>
public sealed partial class DialogViewModel(string title, string confirm) : ModalViewModel(title, confirm)
{
    /// <summary>False for a plain message, which only needs OK.</summary>
    public bool ShowCancel { get; init; } = true;

    /// <summary>A red confirm button, for deletes.</summary>
    public bool IsDanger { get; init; }

    /// <summary>A line of plain text under the fields.</summary>
    public string? Explanation { get; init; }

    public bool HasExplanation => !string.IsNullOrEmpty(Explanation) || Summary.Length > 0;

    // ----- name ---------------------------------------------------------------

    public bool ShowName { get; init; }
    public string NameLabel { get; init; } = "Name";
    public bool ShowNameLabel { get; init; } = true;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    // ----- kind ---------------------------------------------------------------

    public bool ShowKind => Kinds.Count > 1;
    public ObservableCollection<string> Kinds { get; init; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowType))]
    public partial string? Kind { get; set; }

    // ----- type (a reference to another element) --------------------------------

    public Func<string?, bool>? KindTakesType { get; init; }
    public bool AlwaysShowType { get; init; }
    public bool ShowType => AlwaysShowType || (KindTakesType?.Invoke(Kind) ?? false);
    public string TypeLabel { get; init; } = "Typed by";
    public ObservableCollection<string> TypeChoices { get; init; } = [];

    [ObservableProperty]
    public partial string TypeText { get; set; } = string.Empty;

    // ----- free text --------------------------------------------------------------

    public bool ShowText { get; init; }
    public string TextLabel { get; init; } = "Text";

    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    // ----- preview and message -------------------------------------------------

    /// <summary>Recomputes the preview from the current input; set by whoever opens the dialog.</summary>
    public Func<DialogViewModel, DialogPreview>? Previewer { get; init; }

    /// <summary>Words for the files a plan touches, e.g. "Updates 5 references in 4 files."; empty when not wanted.</summary>
    public Func<IReadOnlyList<FileChange>, string>? Summarize { get; init; }

    /// <summary>True: list the files and their counts. False: show the changed lines.</summary>
    public bool ShowsFiles { get; init; }

    public ObservableCollection<string> Preview { get; } = [];
    public ObservableCollection<FileChange> Files { get; } = [];
    public bool ShowPreview => Previewer is not null && !ShowsFiles && Preview.Count > 0;
    public bool ShowFiles => ShowsFiles && Files.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExplanation))]
    public partial string Summary { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial string? Message { get; set; }

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public void RefreshPreview()
    {
        if (Previewer is null)
            return;

        var preview = Previewer(this);
        Preview.Clear();
        foreach (var line in preview.Lines)
            Preview.Add(line);
        Files.Clear();
        foreach (var file in preview.Files)
            Files.Add(file);

        Summary = preview.Error is null && Summarize is not null ? Summarize(preview.Files) : string.Empty;
        Message = preview.Error;
        OnPropertyChanged(nameof(ShowPreview));
        OnPropertyChanged(nameof(ShowFiles));
    }

    protected override bool CanConfirm() => !HasMessage;

    partial void OnNameChanged(string value) => RefreshPreview();
    partial void OnKindChanged(string? value) => RefreshPreview();
    partial void OnTypeTextChanged(string value) => RefreshPreview();
    partial void OnTextChanged(string value) => RefreshPreview();
}

/// <summary>
/// The export dialog: which format, and where the file goes. The path is shown
/// relative to the workspace folder when it lies inside it.
/// </summary>
public sealed partial class ExportDialogViewModel : ModalViewModel
{
    private readonly string _folder;
    private readonly string _name;
    private readonly Func<bool, string, Task<string?>>? _pick;
    private bool _pathChosen;

    /// <summary>Starts on "export/name.json" in the workspace folder.</summary>
    /// <param name="folder">The workspace folder, which relative paths start from.</param>
    /// <param name="name">The file name without extension.</param>
    /// <param name="pick">Asks the user for a file, starting from the given one; null when they cancel.</param>
    /// <param name="scope">"Whole workspace · 13 files".</param>
    public ExportDialogViewModel(string folder, string name, Func<bool, string, Task<string?>>? pick, string scope)
        : base("Export model", "Export")
    {
        _folder = folder;
        _name = name;
        _pick = pick;
        Scope = scope;
        Path = DefaultPath(false);
    }

    /// <summary>Where the file will be written.</summary>
    public string FullPath => System.IO.Path.GetFullPath(Path, _folder);

    private string DefaultPath(bool xmi) => $"export/{_name}{(xmi ? ".xmi" : ".json")}";

    public string Scope { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsJson))]
    public partial bool IsXmi { get; set; }

    public bool IsJson
    {
        get => !IsXmi;
        set => IsXmi = !value;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial string Path { get; set; }

    partial void OnIsXmiChanged(bool value)
    {
        // A path the user picked keeps its folder and name; only the extension follows the format.
        var extension = value ? ".xmi" : ".json";
        Path = _pathChosen ? System.IO.Path.ChangeExtension(Path, extension) : DefaultPath(value);
    }

    [RelayCommand]
    private async Task Change()
    {
        if (_pick is not null && await _pick(IsXmi, FullPath) is { Length: > 0 } picked)
        {
            _pathChosen = true;
            var relative = System.IO.Path.GetRelativePath(_folder, picked);
            Path = relative.StartsWith("..", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative)
                ? picked
                : relative.Replace('\\', '/');
        }
    }

    protected override bool CanConfirm() => !string.IsNullOrWhiteSpace(Path);
}
