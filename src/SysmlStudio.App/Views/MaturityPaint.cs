using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace SysmlStudio.App.Views;

/// <summary>
/// Paints a control in its element's maturity colour. The brush is a dynamic
/// resource reference, not a lookup, so switching between light and dark
/// repaints every bar and border without anything having to be rebound.
/// </summary>
public static class MaturityPaint
{
    public static readonly AttachedProperty<string?> BackgroundProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Background", typeof(MaturityPaint));

    public static readonly AttachedProperty<string?> BorderProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Border", typeof(MaturityPaint));

    static MaturityPaint()
    {
        BackgroundProperty.Changed.AddClassHandler<Control>((c, e) => Apply(c, e.NewValue as string, background: true));
        BorderProperty.Changed.AddClassHandler<Control>((c, e) => Apply(c, e.NewValue as string, background: false));
    }

    public static string? GetBackground(Control control) => control.GetValue(BackgroundProperty);

    public static void SetBackground(Control control, string? value) => control.SetValue(BackgroundProperty, value);

    public static string? GetBorder(Control control) => control.GetValue(BorderProperty);

    public static void SetBorder(Control control, string? value) => control.SetValue(BorderProperty, value);

    /// <summary>The resource key for a maturity keyword.</summary>
    public static string KeyFor(string? maturity) => maturity switch
    {
        "implemented" => "MaturityImplemented",
        "inProgress" => "MaturityInProgress",
        "writtenAhead" => "MaturityWrittenAhead",
        "planned" => "MaturityPlanned",
        _ => "MaturityNone",
    };

    private static void Apply(Control control, string? maturity, bool background)
    {
        var property = background
            ? control switch
            {
                Border => Border.BackgroundProperty,
                Panel => Panel.BackgroundProperty,
                TemplatedControl => TemplatedControl.BackgroundProperty,
                _ => null,
            }
            : control switch
            {
                Border => Border.BorderBrushProperty,
                TemplatedControl => TemplatedControl.BorderBrushProperty,
                _ => null,
            };

        if (property is null)
            return;

        control.Bind(property, control.GetResourceObservable(KeyFor(maturity), o => o as IBrush));
    }
}
