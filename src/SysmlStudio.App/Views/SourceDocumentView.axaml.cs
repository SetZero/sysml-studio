using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using SysmlStudio.App.ViewModels;

namespace SysmlStudio.App.Views;

/// <summary>
/// A .sysml file as text: highlighted, squiggled where it does not parse,
/// with the first error spelled out in a box under its line, editable, with
/// keywords and the model's names offered while typing (Ctrl+Space asks).
/// </summary>
public sealed partial class SourceDocumentView : UserControl
{
    private SourceDocumentViewModel? _viewModel;
    private ErrorSquiggles? _squiggles;
    private ErrorBox? _errorBox;
    private CompletionWindow? _completion;

    public SourceDocumentView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Wire();
        ActualThemeVariantChanged += (_, _) => Wire();
        Editor.TextArea.Caret.PositionChanged += (_, _) => ReportCaret();
        Editor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x64, 0x82, 0xA9));
        Editor.TextArea.TextView.SizeChanged += (_, _) => FitErrorBox();
        Editor.TextArea.TextEntered += (_, e) => OnTextEntered(e.Text);
        Editor.TextArea.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        // Room between the line numbers and the text, and no rule between them.
        foreach (var margin in Editor.TextArea.LeftMargins.OfType<AvaloniaEdit.Editing.LineNumberMargin>())
            margin.Margin = new Avalonia.Thickness(0, 0, 18, 0);
        foreach (var rule in Editor.TextArea.LeftMargins.OfType<Avalonia.Controls.Shapes.Line>())
            rule.IsVisible = false;
    }

    private void Wire()
    {
        _viewModel?.PropertyChanged -= OnViewModelChanged;
        _viewModel?.GoToLineRequested = null;

        _viewModel = DataContext as SourceDocumentViewModel;
        if (_viewModel is null)
            return;

        _viewModel.PropertyChanged += OnViewModelChanged;
        _viewModel.GoToLineRequested = GoToLine;

        var view = Editor.TextArea.TextView;
        if (_squiggles is not null)
            view.BackgroundRenderers.Remove(_squiggles);
        _squiggles = new ErrorSquiggles(_viewModel.Text, Brush("ErrorBrush"));
        view.BackgroundRenderers.Add(_squiggles);

        if (_errorBox is not null)
            view.ElementGenerators.Remove(_errorBox);
        _errorBox = new ErrorBox(_viewModel.Text, Font("UiFont"), Brush("ErrorSoftBrush"), Brush("ErrorBrush"), Brush("TextMutedBrush"));
        view.ElementGenerators.Add(_errorBox);

        Editor.SyntaxHighlighting = SysmlHighlighting.Create(
            Colour("SyntaxKeyword"), Colour("SyntaxComment"), Colour("SyntaxString"),
            Colour("SyntaxNumber"), Colour("SyntaxMetadata"), Colour("SyntaxType"));

        ShowErrors();

        if (_viewModel.PendingLine is { } pending)
        {
            _viewModel.PendingLine = null;
            GoToLine(pending.Line, pending.Focus);
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SourceDocumentViewModel.Errors))
            ShowErrors();
    }

    private void ShowErrors()
    {
        if (_viewModel is null || _squiggles is null || _errorBox is null)
            return;

        _squiggles.Show(_viewModel.Errors);
        _errorBox.Show(_viewModel.ErrorLine, _viewModel.ErrorMessage);
        FitErrorBox();
        Editor.TextArea.TextView.Redraw();
    }

    /// <summary>The box spans from the error line's indentation to the right edge of the text.</summary>
    private void FitErrorBox()
    {
        if (_errorBox is null)
            return;

        var view = Editor.TextArea.TextView;
        _errorBox.Fit(view.Bounds.Width, _errorBox.IndentColumns * view.WideSpaceWidth);
        view.Redraw();
    }

    private void GoToLine(int line, bool focus)
    {
        var editor = Editor;
        if (line < 1 || line > editor.Document.LineCount)
            return;

        editor.TextArea.Caret.Line = line;
        editor.TextArea.Caret.Column = 1;
        editor.ScrollToLine(line);
        if (focus)
            editor.TextArea.Focus();
    }

    /// <summary>
    /// Offers completions by itself once two letters of a word are typed, or
    /// straight after "::"; anything that ends a word closes the list.
    /// </summary>
    private void OnTextEntered(string? typed)
    {
        if (string.IsNullOrEmpty(typed))
            return;

        var c = typed[^1];
        if (_completion is not null)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                _completion.Close();
            return;
        }

        var offset = Editor.CaretOffset;
        var afterScope = c == ':' && offset >= 2 && Editor.Document.GetCharAt(offset - 2) == ':';
        if (!afterScope && !char.IsLetter(c) && c != '_')
            return;

        ShowCompletion(explicitly: false, minimumPrefix: afterScope ? 0 : 2);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ShowCompletion(explicitly: true, minimumPrefix: 0);
            e.Handled = true;
        }
    }

    private void ShowCompletion(bool explicitly, int minimumPrefix)
    {
        var shell = this.FindAncestorOfType<Window>()?.DataContext as ShellViewModel;
        var request = SourceCompletion.At(Editor.Document.Text, Editor.CaretOffset, shell?.Workspace, SysmlHighlighting.Keywords);
        if (request is null || request.Prefix.Length < minimumPrefix || (!explicitly && !request.Matches))
            return;

        _completion?.Close();
        var window = new CompletionWindow(Editor.TextArea)
        {
            StartOffset = request.Start,
            CloseWhenCaretAtBeginning = true,
            MinWidth = 260,
        };
        foreach (var item in request.Items)
            window.CompletionList.CompletionData.Add(new Suggestion(item));
        if (request.Prefix.Length > 0)
            window.CompletionList.SelectItem(request.Prefix);

        window.Closed += (_, _) => _completion = null;
        _completion = window;
        window.Show();
    }

    /// <summary>One entry of the completion list: the text, with what it is beside it.</summary>
    private sealed class Suggestion(CompletionItem item) : ICompletionData
    {
        public IImage? Image => null;

        public string Text => item.Text;

        public object Content => new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = item.Text },
                new TextBlock { Text = item.Detail, Opacity = 0.55 },
            },
        };

        // The row already says what it is; a description would float as a tooltip far from the list.
        public object? Description => null;

        public double Priority => item.Priority;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
            => textArea.Document.Replace(completionSegment, item.Text);
    }

    private void ReportCaret()
    {
        if (this.FindAncestorOfType<Window>()?.DataContext is ShellViewModel shell)
            shell.SetCaret(Editor.TextArea.Caret.Line, Editor.TextArea.Caret.Column);
    }

    private Color Colour(string key)
        => this.TryFindResource(key, ActualThemeVariant, out var value) && value is Color c ? c : Colors.Gray;

    private IBrush Brush(string key)
        => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush b ? b : Brushes.Red;

    private FontFamily Font(string key)
        => this.TryFindResource(key, ActualThemeVariant, out var value) && value is FontFamily f ? f : FontFamily.Default;
}
