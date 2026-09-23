using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace SysmlStudio.App.Views;

/// <summary>
/// Puts a box under the line with the first syntax error, inside the text:
/// "Expected ';' · Diagram edits are paused until this file parses." The box
/// is an inline object at the end of that line, as wide as the view, so word
/// wrapping moves it onto a row of its own directly below the line.
/// </summary>
public sealed class ErrorBox : VisualLineElementGenerator
{
    private readonly TextDocument _document;
    private readonly Border _box;
    private readonly Run _message = new();
    private int _line;

    public ErrorBox(TextDocument document, FontFamily font, IBrush background, IBrush accent, IBrush muted)
    {
        _document = document;
        _message.Foreground = accent;
        var text = new TextBlock
        {
            FontSize = 13,
            FontFamily = font,
            TextWrapping = TextWrapping.Wrap,
            Inlines = [_message, new Run("    Diagram edits are paused until this file parses.") { Foreground = muted }],
        };
        _box = new Border
        {
            Background = background,
            BorderBrush = accent,
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(10, 6),
            Margin = new Thickness(0, 5, 0, 7),
            Child = text,
        };
    }

    /// <summary>Shows the box under <paramref name="line"/> with <paramref name="message"/>; line 0 hides it.</summary>
    public void Show(int line, string message)
    {
        _line = line;
        _message.Text = message;
    }

    /// <summary>The box spans the view from the line's indentation to the right edge.</summary>
    public void Fit(double viewWidth, double indent)
    {
        _box.Margin = new Thickness(indent, 5, 0, 7);
        _box.Width = Math.Max(120, viewWidth - indent - 28);
    }

    /// <summary>The indentation of the error line, in characters, for <see cref="Fit"/>.</summary>
    public int IndentColumns
    {
        get
        {
            if (_line < 1 || _line > _document.LineCount)
                return 0;
            var line = _document.GetLineByNumber(_line);
            var text = _document.GetText(line);
            return text.Length - text.TrimStart().Length;
        }
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (_line < 1 || _line > _document.LineCount)
            return -1;

        var end = _document.GetLineByNumber(_line).EndOffset;
        return end >= startOffset && end <= CurrentContext.VisualLine.LastDocumentLine.EndOffset ? end : -1;
    }

    public override VisualLineElement ConstructElement(int offset) => new InlineObjectElement(0, _box);
}
