using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using SysmlStudio.Syntax;

namespace SysmlStudio.App.ViewModels;

/// <summary>
/// A .sysml file open as text. Typing re-parses the file a quarter of a second
/// after the last keystroke, so errors appear while writing; saving writes the
/// text as it stands and re-indexes the model.
/// </summary>
public sealed partial class SourceDocumentViewModel : DocumentViewModel
{
    private readonly ShellViewModel _shell;
    private readonly DispatcherTimer _reparse;

    public SourceDocumentViewModel(ShellViewModel shell, string path, string relativePath, string text)
    {
        _shell = shell;
        Path = path;
        RelativePath = relativePath;
        Id = "source:" + path;
        Title = System.IO.Path.GetFileName(path);
        ToolTip = relativePath;

        Text = new TextDocument(text);
        Text.TextChanged += (_, _) => OnTextChanged();

        _reparse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _reparse.Tick += (_, _) =>
        {
            _reparse.Stop();
            Reparse();
        };

        Reparse();
    }

    public string Path { get; }

    public override bool IsSource => true;

    /// <summary>The path relative to the model folder.</summary>
    public string RelativePath { get; }

    public TextDocument Text { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors), nameof(FirstError), nameof(ErrorLine), nameof(ErrorMessage))]
    public partial IReadOnlyList<SyntaxError> Errors { get; set; } = [];

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    /// <summary>Set by the view: moves the caret to a line, scrolls it into view, and takes the focus when asked to.</summary>
    public Action<int, bool>? GoToLineRequested { get; set; }

    /// <summary>A line asked for before the view was there to show it; the view goes to it when it attaches.</summary>
    public (int Line, bool Focus)? PendingLine { get; set; }

    public bool HasErrors => Errors.Count > 0;

    public SyntaxError? FirstError => Errors.Count > 0 ? Errors[0] : null;

    /// <summary>The line the box under the first error hangs from; 0 when the file parses.</summary>
    public int ErrorLine => FirstError?.Line ?? 0;

    /// <summary>The first error in the parser's words, shortened to what it expected where it can be.</summary>
    public string ErrorMessage => FirstError is { } error ? Shorten(error.Message) : string.Empty;

    /// <summary>
    /// ANTLR lists every token it would have taken ("mismatched input 'x'
    /// expecting {';', '{', …}"). A short list reads as "Expected ';'"; a long
    /// one says nothing useful, so the message names what was found instead.
    /// </summary>
    public static string Shorten(string message)
    {
        var expecting = message.IndexOf(" expecting ", StringComparison.Ordinal);
        if (expecting < 0)
            return message;

        var what = message[(expecting + " expecting ".Length)..].Trim();
        var choices = what.StartsWith('{') ? what.Trim('{', '}').Split(", ") : [what];
        if (choices.Length <= 3)
            return "Expected " + string.Join(" or ", choices);

        var found = message[..expecting];
        var quote = found.IndexOf('\'');
        var token = quote < 0 ? found : found[quote..];
        return token == "'<EOF>'" ? "Unexpected end of file" : "Unexpected " + token;
    }

    /// <summary>
    /// Shows a line. Browsing the model passes <paramref name="focus"/> false,
    /// so the arrow keys stay with the tree.
    /// </summary>
    public void GoToLine(int line, bool focus = true)
    {
        if (GoToLineRequested is { } go)
            go(line, focus);
        else
            PendingLine = (line, focus);
    }

    /// <summary>Runs a re-parse that is waiting for the typing pause, now.</summary>
    public void FlushPendingReparse()
    {
        if (!_reparse.IsEnabled)
            return;

        _reparse.Stop();
        Reparse();
    }

    /// <summary>Takes text an edit wrote to the model, without counting it as typing.</summary>
    public void ReplaceText(string text)
    {
        _replacing = true;
        try
        {
            Text.Text = text;
        }
        finally
        {
            _replacing = false;
        }

        _reparse.Stop();
        Reparse();
    }

    private bool _replacing;

    private void OnTextChanged()
    {
        if (_replacing)
            return;

        IsDirty = true;
        _reparse.Stop();
        _reparse.Start();
    }

    private void Reparse()
    {
        var parsed = SourceFile.ParseText(Path, Text.Text);
        Errors = parsed.Errors;
        _shell.OnSourceReparsed(this, parsed);
    }

    partial void OnIsDirtyChanged(bool value)
    {
        HasDot = value || HasErrors;
        _shell.RefreshStatus();
    }

    partial void OnErrorsChanged(IReadOnlyList<SyntaxError> value) => HasDot = IsDirty || value.Count > 0;
}
