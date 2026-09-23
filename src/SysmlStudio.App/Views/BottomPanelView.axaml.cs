using Avalonia.Controls;
using SysmlStudio.App.ViewModels;

namespace SysmlStudio.App.Views;

/// <summary>Problems, usages and output. Clicking a row jumps to where it is in the text.</summary>
public sealed partial class BottomPanelView : UserControl
{
    public BottomPanelView() => InitializeComponent();

    private void OnProblemSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is BottomPanelViewModel vm && e.AddedItems.Count > 0 && e.AddedItems[0] is ProblemRow row)
            vm.OpenProblemCommand.Execute(row);
    }

    private void OnUsageSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is BottomPanelViewModel vm && e.AddedItems.Count > 0 && e.AddedItems[0] is UsageRow row)
            vm.OpenUsageCommand.Execute(row);
    }
}
