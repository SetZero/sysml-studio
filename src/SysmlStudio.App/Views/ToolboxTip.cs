using System.Globalization;
using Avalonia.Data.Converters;

namespace SysmlStudio.App.Views;

/// <summary>The toolbox's tooltip: what clicking an entry does.</summary>
public sealed class ToolboxTip : IValueConverter
{
    public static ToolboxTip Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? "Draw this relation: click the source box, then the target (Esc cancels)"
            : "Add one of these to what the diagram is of";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
