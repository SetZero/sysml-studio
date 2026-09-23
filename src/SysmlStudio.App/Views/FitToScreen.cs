using Avalonia;
using Avalonia.Threading;
using Nodify.Avalonia;

namespace SysmlStudio.App.Views;

/// <summary>
/// Fits a diagram to its canvas whenever the canvas shows a new one. The tab
/// control reuses one editor for every tab, so "loaded" alone is not enough:
/// switching tabs changes the data context and needs the same treatment.
/// </summary>
public static class FitToScreen
{
    public static readonly AttachedProperty<bool> OnShowProperty =
        AvaloniaProperty.RegisterAttached<NodifyEditor, bool>("OnShow", typeof(FitToScreen));

    static FitToScreen()
    {
        OnShowProperty.Changed.AddClassHandler<NodifyEditor>((editor, e) =>
        {
            if (e.NewValue is not true)
                return;

            editor.Loaded += (_, _) => Fit(editor);
            editor.DataContextChanged += (_, _) => Fit(editor);
        });
    }

    public static bool GetOnShow(NodifyEditor editor) => editor.GetValue(OnShowProperty);

    public static void SetOnShow(NodifyEditor editor, bool value) => editor.SetValue(OnShowProperty, value);

    /// <summary>
    /// Waits for the nodes to be measured, then frames all of them. Until the
    /// item containers have been laid out the editor's extent is empty and a
    /// fit would frame nothing, so this retries a few frames before giving up.
    /// </summary>
    public static void Fit(NodifyEditor editor) => TryFit(editor, attemptsLeft: 20);

    private static void TryFit(NodifyEditor editor, int attemptsLeft)
    {
        var extent = editor.ItemsExtent;
        if (extent.Width > 0 && extent.Height > 0 && editor.Bounds.Width > 0)
        {
            editor.FitToScreen(null);

            // A small diagram fitted to a big canvas would be drawn at 300 per cent.
            // Nothing is shown larger than life: it is centred at 100 instead.
            if (editor.ViewportZoom > 1)
            {
                editor.ViewportZoom = 1;
                editor.BringIntoView(extent.Center);
            }

            return;
        }

        if (attemptsLeft > 0)
            DispatcherTimer.RunOnce(() => TryFit(editor, attemptsLeft - 1), TimeSpan.FromMilliseconds(50));
    }
}
