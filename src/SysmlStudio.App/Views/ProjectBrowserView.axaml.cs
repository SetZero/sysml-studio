using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using SysmlStudio.App.ViewModels;

namespace SysmlStudio.App.Views;

/// <summary>The project browser. Right-click selects the row under the pointer and opens its menu.</summary>
public sealed partial class ProjectBrowserView : UserControl
{
    public ProjectBrowserView() => InitializeComponent();

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Control source
            || source.FindAncestorOfType<TreeViewItem>(includeSelf: true) is not { DataContext: ElementViewModel { Element: { } element } row }
            || this.FindAncestorOfType<Window>()?.DataContext is not ShellViewModel shell)
        {
            return;
        }

        if (DataContext is BrowserViewModel browser)
            browser.Selected = row;

        ElementMenu.For(shell, element).Open(source);
        e.Handled = true;
    }
}
