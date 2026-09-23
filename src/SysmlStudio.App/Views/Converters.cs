using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SysmlStudio.App.Views;

/// <summary>The few conversions the views need that bindings cannot express.</summary>
public static class Converters
{
    /// <summary>True → semibold, false → normal: the workspace row at the top of the tree.</summary>
    public static IValueConverter BoldWhen { get; } =
        new FuncValueConverter<bool, FontWeight>(bold => bold ? FontWeight.SemiBold : FontWeight.Normal);

    /// <summary>"Search (Ctrl+K)" → "Ctrl+K": the keys of a shortcut tooltip, for a hint inside a control.</summary>
    public static IValueConverter KeysOnly { get; } = new FuncValueConverter<string?, string>(tip =>
        tip is { Length: > 0 } && tip.EndsWith(')') && tip.LastIndexOf('(') is var open and >= 0
            ? tip[(open + 1)..^1]
            : string.Empty);
}
