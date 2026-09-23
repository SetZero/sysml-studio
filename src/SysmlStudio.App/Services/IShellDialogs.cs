namespace SysmlStudio.App.Services;

/// <summary>What the view models need from the window: pickers.</summary>
public interface IShellDialogs
{
    Task<string?> PickFolderAsync();

    Task<string?> PickSaveFileAsync(string suggestedName, string extension);
}
