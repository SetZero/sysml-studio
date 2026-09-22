using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using AvaloniaEdit.Document;

namespace SysmlStudio.App.Views;

/// <summary>
/// Maturity keyword to colour. One meaning per colour: what the element's
/// "#implemented" / "#planned" keyword says, and nothing else.
/// </summary>
public sealed class MaturityBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as string) switch
        {
            "implemented" => "MaturityImplemented",
            "inProgress" => "MaturityInProgress",
            "writtenAhead" => "MaturityWrittenAhead",
            "planned" => "MaturityPlanned",
            _ => "MaturityNone",
        };

        if (Application.Current?.Resources.TryGetResource(key, null, out var brush) == true && brush is IBrush found)
            return found;

        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Text to an AvaloniaEdit document, so the source pane can bind to a string.</summary>
public sealed class TextDocumentConverter : IValueConverter
{
    public static TextDocumentConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new TextDocument(value as string ?? string.Empty);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is TextDocument document ? document.Text : string.Empty;
}
