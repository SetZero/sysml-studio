using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SysmlStudio.App.ViewModels;

namespace SysmlStudio.App.Views;

/// <summary>
/// The settings dialog. While a shortcut is recording, every key pressed in
/// the window is caught here — wherever the focus is — before the window's
/// own shortcuts can act on it.
/// </summary>
public sealed partial class SettingsView : UserControl
{
    private TopLevel? _window;

    public SettingsView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this);
        // The window's key bindings see the keys first and mark them handled
        // (they do nothing while a shortcut records), so handled keys count too.
        _window?.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _window?.RemoveHandler(KeyDownEvent, OnKeyDown);
        _window = null;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsViewModel { Recording: not null } settings)
            return;

        e.Handled = true;
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.System)
        {
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Escape)
            settings.StopRecording();
        else if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Back)
            settings.Recorded(string.Empty);
        else
            settings.Recorded(new KeyGesture(e.Key, e.KeyModifiers).ToString());
    }
}
