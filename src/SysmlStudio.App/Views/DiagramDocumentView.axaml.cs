using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Nodify.Avalonia;
using SysmlStudio.App.ViewModels;

namespace SysmlStudio.App.Views;

/// <summary>A diagram: toolbox, canvas, breadcrumb and zoom. The canvas is Nodify's.</summary>
public sealed partial class DiagramDocumentView : UserControl
{
    public DiagramDocumentView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Wire();
    }

    /// <summary>Hands the view model the two things only the view can do: frame and render.</summary>
    private void Wire()
    {
        if (DataContext is not DiagramDocumentViewModel vm)
            return;

        vm.FitRequested = () => FitToScreen.Fit(Editor);
        vm.RenderPng = RenderPng;
    }

    private void RenderPng(string path)
    {
        var editor = Editor;
        var size = new PixelSize((int)Math.Ceiling(editor.Bounds.Width), (int)Math.Ceiling(editor.Bounds.Height));
        if (size.Width <= 0 || size.Height <= 0)
            return;

        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(editor);
        using var file = File.Create(path);
        bitmap.Save(file, new PngBitmapEncoderOptions());
    }
}
