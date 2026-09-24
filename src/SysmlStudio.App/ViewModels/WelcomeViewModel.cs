using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using SysmlStudio.App.Services;

namespace SysmlStudio.App.ViewModels;

/// <summary>The document shown when no model is open: open a folder, or one opened before.</summary>
public sealed partial class WelcomeViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private readonly ShellViewModel _shell;

    public WelcomeViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Refresh();
    }

    public ObservableCollection<RecentFolder> Recent { get; } = [];

    /// <summary>The folder open now, if any; the page then says what to do next instead.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(IsModelOpen), nameof(IsEmpty))]
    public partial string? OpenFolderName { get; set; }

    public bool IsModelOpen => OpenFolderName is not null;

    public bool IsEmpty => OpenFolderName is null;

    public bool HasRecent => Recent.Count > 0;

    /// <summary>Whether a sample model was shipped beside the application.</summary>
    public static bool HasSample => Samples.Has(Samples.Default);

    public void Refresh()
    {
        Recent.Clear();
        foreach (var folder in RecentFolders.Load())
            Recent.Add(folder);
        OnPropertyChanged(nameof(HasRecent));
    }

    [RelayCommand]
    private Task OpenFolder() => _shell.OpenFolderCommand.ExecuteAsync(null);

    [RelayCommand]
    private void OpenRecent(RecentFolder folder) => _shell.Open(folder.Path);

    [RelayCommand]
    private void OpenSample() => _shell.Open(Samples.Prepare(Samples.Default));
}
