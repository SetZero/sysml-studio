using System.Text;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using SysmlStudio.Syntax;

namespace SysmlStudio.App.ViewModels;

/// <summary>
/// A .sysml file open as text. Typing re-parses the file a quarter of a second
/// after the last keystroke, so errors appear while writing; saving writes the
/// text as it stands and re-indexes the model.
/// </summary>
public sealed partial class SourceDocumentViewModel : Document
{
    private readonly ShellViewModel _shell;
    private readonly DispatcherTimer _reparse;
    private string _savedText;

    public SourceDocumentViewModel(ShellViewModel shell, string path, string relativePath, string text)
    {
        _shell = shell;
        Path = path;
        RelativePath = relativePath;
        _savedText = text;
        Id = "source:" + path;
        Title = System.IO.Path.GetFileName(path);
        CanFloat = true;

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

    /// <summary>The path shown above the editor: relative to the model folder's parent.</summary>
    public string RelativePath { get; }

    public TextDocument Text { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderLine), nameof(HasErrors), nameof(FirstError))]
    public partial IReadOnlyList<SyntaxError> Errors { get; set; } = [];

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    /// <summary>Set by the view: moves the caret to a line and scrolls it into view.</summary>
    public Action<int>? GoToLineRequested { get; set; }

    public bool HasErrors => Errors.Count > 0;

    public SyntaxError? FirstError => Errors.Count > 0 ? Errors[0] : null;

    public string HeaderLine
    {
        get
        {
            var errors = Errors.Count switch
            {
                0 => "no errors",
                1 => "1 error",
                var n => $"{n} errors",
            };
            var endings = Text.Text.Contains("\r\n", StringComparison.Ordinal) ? "CRLF" : "LF";
            return $"{RelativePath}  ·  UTF-8  ·  {endings}  ·  {errors}";
        }
    }

    public void GoToLine(int line) => GoToLineRequested?.Invoke(line);

    /// <summary>Runs a re-parse that is waiting for the typing pause, now.</summary>
    public void FlushPendingReparse()
    {
        if (!_reparse.IsEnabled)
            return;

        _reparse.Stop();
        Reparse();
    }

    /// <summary>Writes the text to disk exactly as it stands.</summary>
    public void Save()
    {
        var text = Text.Text;
        File.WriteAllText(Path, text, new UTF8Encoding(false));
        _savedText = text;
        IsDirty = false;
        _shell.OnSourceSaved(this, text);
    }

    private void OnTextChanged()
    {
        IsDirty = !string.Equals(Text.Text, _savedText, StringComparison.Ordinal);
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
        Title = System.IO.Path.GetFileName(Path);
        _shell.RefreshStatus();
    }
}
