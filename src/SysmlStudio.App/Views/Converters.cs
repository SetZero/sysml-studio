using System.Globalization;
using Avalonia.Data.Converters;

namespace SysmlStudio.App.Views;

/// <summary>True when the bound value equals the converter parameter: "is this the active diagram kind?"</summary>
public sealed class EqualsConverter : IValueConverter
{
    public static EqualsConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Equals(value, parameter);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
