using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using SysmlStudio.Syntax;

namespace SysmlStudio.App.Views;

/// <summary>
/// Draws a wavy line under each syntax error: from the error's column to the
/// end of the word there, or one character where there is no word.
/// </summary>
public sealed class ErrorSquiggles(TextDocument document, IBrush brush) : IBackgroundRenderer
{
    private IReadOnlyList<SyntaxError> _errors = [];

    public KnownLayer Layer => KnownLayer.Selection;

    public IBrush Brush { get; set; } = brush;

    public void Show(IReadOnlyList<SyntaxError> errors) => _errors = errors;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_errors.Count == 0 || !textView.VisualLinesValid)
            return;

        var pen = new Pen(Brush, 1.2);
        foreach (var error in _errors)
        {
            if (error.Line < 1 || error.Line > document.LineCount)
                continue;

            var line = document.GetLineByNumber(error.Line);
            var start = line.Offset + Math.Min(error.Column, line.Length);
            var end = start;
            while (end < line.EndOffset && !char.IsWhiteSpace(document.GetCharAt(end)))
                end++;
            if (end == start)
                end = Math.Min(start + 1, document.TextLength);
            if (end <= start)
                continue;

            var segment = new TextSegment { StartOffset = start, EndOffset = end };
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                drawingContext.DrawGeometry(null, pen, Wave(rect));
        }
    }

    private static StreamGeometry Wave(Rect rect)
    {
        const double step = 2.5;
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        var y = rect.Bottom - 1;
        context.BeginFigure(new Point(rect.Left, y), isFilled: false);
        var up = true;
        for (var x = rect.Left + step; x <= rect.Right + step; x += step)
        {
            context.LineTo(new Point(x, up ? y - 2 : y));
            up = !up;
        }

        context.EndFigure(isClosed: false);
        return geometry;
    }
}
