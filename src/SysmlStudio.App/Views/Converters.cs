using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SysmlStudio.App.Views;

/// <summary>The few conversions the views need that bindings cannot express.</summary>
public static class Converters
{
    /// <summary>True → semibold, false → normal: the workspace row at the top of the tree.</summary>
    public static IValueConverter BoldWhen { get; } =
        new FuncValueConverter<bool, FontWeight>(bold => bold ? FontWeight.SemiBold : FontWeight.Normal);
}
