using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SysmlStudio.App.ViewModels;

/// <summary>
/// The one modal dialog every edit operation uses: a caption, a title, the
/// fields that operation needs, a preview of the text it will write, and a
/// message when the input will not do. Awaiting <see cref="Result"/> gives
/// true for the confirm button, false for cancel.
/// </summary>
public sealed partial class DialogViewModel(string caption, string title, string confirm) : ObservableObject
{
    private readonly TaskCompletionSource<bool> _result = new();

    public string Caption { get; } = caption;
    public string Title { get; } = title;
    public string ConfirmText { get; } = confirm;

    /// <summary>False for a plain message, which only needs OK.</summary>
    public bool ShowCancel { get; init; } = true;

    /// <summary>A red confirm button, for deletes.</summary>
    public bool IsDanger { get; init; }

    public string? Explanation { get; init; }

    // ----- name ---------------------------------------------------------------

    public bool ShowName { get; init; }
    public string NameLabel { get; init; } = "NAME";
    public string? CurrentName { get; init; }
    public bool ShowCurrentName => CurrentName is not null;

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
    public string TypeLabel { get; init; } = "TYPED BY";
    public ObservableCollection<string> TypeChoices { get; init; } = [];

    [ObservableProperty]
    public partial string TypeText { get; set; } = string.Empty;

    // ----- free text --------------------------------------------------------------

    public bool ShowText { get; init; }
    public string TextLabel { get; init; } = "TEXT";

    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    // ----- preview and message -------------------------------------------------

    /// <summary>Recomputes the preview from the current input; set by whoever opens the dialog.</summary>
    public Func<DialogViewModel, (IReadOnlyList<string> Lines, string? Error)>? Previewer { get; init; }

    public ObservableCollection<string> Preview { get; } = [];
    public bool ShowPreview => Previewer is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial string? Message { get; set; }

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public Task<bool> Result => _result.Task;

    public void RefreshPreview()
    {
        if (Previewer is null)
            return;

        var (lines, error) = Previewer(this);
        Preview.Clear();
        foreach (var line in lines)
            Preview.Add(line);
        Message = error;
    }

    partial void OnNameChanged(string value) => RefreshPreview();
    partial void OnKindChanged(string? value) => RefreshPreview();
    partial void OnTypeTextChanged(string value) => RefreshPreview();
    partial void OnTextChanged(string value) => RefreshPreview();

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => _result.TrySetResult(true);

    private bool CanConfirm() => !HasMessage;

    [RelayCommand]
    private void Cancel() => _result.TrySetResult(false);
}
