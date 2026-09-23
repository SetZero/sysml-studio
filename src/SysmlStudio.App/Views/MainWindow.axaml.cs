using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SysmlStudio.App.Services;

namespace SysmlStudio.App.Views;

/// <summary>The one window. It owns the pickers; everything else is the view model's.</summary>
public sealed partial class MainWindow : Window, IShellDialogs
{
    public MainWindow() => InitializeComponent();

    public async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open a folder of .sysml files",
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string suggestedName, string extension)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export as {extension.ToUpperInvariant()}",
            SuggestedFileName = suggestedName + "." + extension,
            DefaultExtension = extension,
        });

        return file?.TryGetLocalPath();
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();
}
