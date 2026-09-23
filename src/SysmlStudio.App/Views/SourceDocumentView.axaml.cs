using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaEdit;
using SysmlStudio.App.ViewModels;

namespace SysmlStudio.App.Views;

/// <summary>A .sysml file as text: highlighted, squiggled where it does not parse, editable.</summary>
public sealed partial class SourceDocumentView : UserControl
{
    private SourceDocumentViewModel? _viewModel;
    private ErrorSquiggles? _squiggles;

    public SourceDocumentView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Wire();
        ActualThemeVariantChanged += (_, _) => Recolour();
        Editor.TextArea.Caret.PositionChanged += (_, _) => ReportCaret();
        Editor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x56, 0x75, 0x9B));
    }

    private void Wire()
    {
        _viewModel?.PropertyChanged -= OnViewModelChanged;

        _viewModel = DataContext as SourceDocumentViewModel;
        if (_viewModel is null)
            return;

        _viewModel.PropertyChanged += OnViewModelChanged;
        _viewModel.GoToLineRequested = GoToLine;

        if (_squiggles is not null)
            Editor.TextArea.TextView.BackgroundRenderers.Remove(_squiggles);
        _squiggles = new ErrorSquiggles(_viewModel.Text, Brush("ErrorBrush"));
        Editor.TextArea.TextView.BackgroundRenderers.Add(_squiggles);
        _squiggles.Show(_viewModel.Errors);

        Recolour();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SourceDocumentViewModel.Errors) && _viewModel is not null && _squiggles is not null)
        {
            _squiggles.Show(_viewModel.Errors);
            Editor.TextArea.TextView.InvalidateLayer(_squiggles.Layer);
        }
    }

    /// <summary>The keyword colours follow the theme, so the highlighting is rebuilt when it changes.</summary>
    private void Recolour()
    {
        Editor.SyntaxHighlighting = SysmlHighlighting.Create(
            Colour("SyntaxKeyword"), Colour("SyntaxComment"), Colour("SyntaxString"),
            Colour("SyntaxNumber"), Colour("SyntaxMetadata"));

        _squiggles?.Brush = Brush("ErrorBrush");
    }

    private void GoToLine(int line)
    {
        var editor = Editor;
        if (line < 1 || line > editor.Document.LineCount)
            return;

        editor.TextArea.Caret.Line = line;
        editor.TextArea.Caret.Column = 1;
        editor.ScrollToLine(line);
        editor.TextArea.Focus();
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
}
