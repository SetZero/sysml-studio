using Avalonia.Controls;
using Avalonia.Threading;

namespace SysmlStudio.App.Views;

/// <summary>The edit dialog. It puts the caret in the first field when it opens.</summary>
public sealed partial class EditDialogView : UserControl
{
    public EditDialogView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (NameBox.IsEffectivelyVisible)
            {
                NameBox.Focus();
                NameBox.SelectAll();
            }
        }, DispatcherPriority.Background);
    }
}
