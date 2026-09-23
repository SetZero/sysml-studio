using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using SysmlStudio.App.Services;
using SysmlStudio.App.ViewModels;

namespace SysmlStudio.App.Views;

/// <summary>
/// The one window. It owns the pickers and the keyboard: its key bindings are
/// made from the shortcuts in force, and made again when the user changes them.
/// </summary>
public sealed partial class MainWindow : Window, IShellDialogs
{
    private ShellViewModel? _shell;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as ShellViewModel);
    }

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

    private void Attach(ShellViewModel? shell)
    {
        if (_shell is not null)
        {
            _shell.Keys.Changed -= BindKeys;
            _shell.SearchFocusRequested -= FocusSearch;
        }

        _shell = shell;
        if (shell is not null)
        {
            shell.Keys.Changed += BindKeys;
            shell.SearchFocusRequested += FocusSearch;
        }

        BindKeys();
    }

    private void BindKeys()
    {
        KeyBindings.Clear();
        if (_shell is not { } shell)
            return;

        foreach (var action in ShortcutCatalog.All)
        {
            if (shell.Keys.KeyGesture(action.Id) is { } gesture)
                KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = new RelayCommand(() => shell.RunShortcut(action)) });
        }
    }

    private void FocusSearch()
    {
        if (_shell is { HasWorkspace: false })
            return;

        if (_shell?.ShowsSearch == true)
            SideSearch.Focus();
        else
            SearchBox.Focus();
    }
}
