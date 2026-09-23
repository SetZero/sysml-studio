using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nodify.Avalonia;
using SysmlStudio.App.ViewModels;

namespace SysmlStudio.App.Views;

/// <summary>A diagram: toolbox, canvas, breadcrumb and zoom. The canvas is Nodify's.</summary>
public sealed partial class DiagramDocumentView : UserControl
{
    static DiagramDocumentView()
    {
        // Nodify's drag optimisation moves a preview during a drag and writes
        // Location only on release, so the arrows, which follow Location,
        // jumped after the drop instead of following the box. Diagrams here
        // are small enough to move the real thing.
        NodifyEditor.EnableDraggingContainersOptimizations = false;
    }

    public DiagramDocumentView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Wire();
    }

    private ShellViewModel? Shell => this.FindAncestorOfType<Window>()?.DataContext as ShellViewModel;

    /// <summary>A toolbox entry adds an element to what the diagram is of, or starts drawing a relation.</summary>
    private void OnToolboxClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ToolboxItem item || Shell is not { } shell)
            return;

        if (item.Relation is { } relation)
            shell.StartRelationCommand.Execute(relation);
        else if (item.Kind is { } kind)
            shell.AddToDiagramCommand.Execute(kind);
    }

    /// <summary>Right-click on a box opens the element's menu; on the empty canvas, the diagram's.</summary>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (Shell is not { } shell || DataContext is not DiagramDocumentViewModel diagram || e.Source is not Control source)
            return;

        var menu = source.FindAncestorOfType<ItemContainer>(includeSelf: true) is { DataContext: DiagramNodeViewModel { IsPseudoNode: false } node }
            ? ElementMenu.For(shell, node.Element)
            : ElementMenu.ForCanvas(shell, diagram);
        menu.Open(source);
        e.Handled = true;
    }

    /// <summary>Hands the view model the two things only the view can do: frame and render.</summary>
    private void Wire()
    {
        if (DataContext is not DiagramDocumentViewModel vm)
            return;

        vm.FitRequested = () => FitToScreen.Fit(Editor);
        vm.RenderPng = RenderPng;
        MeasureWhenDrawn(vm, attemptsLeft: 20);
    }

    /// <summary>
    /// Waits until every box has been drawn, reads the sizes it was drawn at,
    /// and gives them to the view model, which lays the diagram out again if
    /// any box came out bigger than the estimate it was placed with.
    /// </summary>
    private void MeasureWhenDrawn(DiagramDocumentViewModel vm, int attemptsLeft)
    {
        var containers = Editor.GetVisualDescendants().OfType<ItemContainer>()
            .Where(c => c.DataContext is DiagramNodeViewModel && c.Bounds.Height > 0)
            .ToList();

        if (containers.Count < vm.Nodes.Count)
        {
            if (attemptsLeft > 0)
                DispatcherTimer.RunOnce(() => MeasureWhenDrawn(vm, attemptsLeft - 1), TimeSpan.FromMilliseconds(50));
            return;
        }

        var sizes = containers.ToDictionary(c => (DiagramNodeViewModel)c.DataContext!, c => c.Bounds.Size);
        if (vm.ApplyMeasuredSizes(sizes))
            FitToScreen.Fit(Editor);
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
