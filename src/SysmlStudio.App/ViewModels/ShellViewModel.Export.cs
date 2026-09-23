using CommunityToolkit.Mvvm.Input;
using SysmlStudio.Interchange;

namespace SysmlStudio.App.ViewModels;

/// <summary>Export: the whole workspace as SysML v2 JSON or XMI, for tools that read the standard formats.</summary>
public sealed partial class ShellViewModel
{
    [RelayCommand]
    private async Task Export()
    {
        if (Workspace is not { } workspace)
            return;

        Func<bool, string, Task<string?>>? pick = _dialogs is null
            ? null
            : (xmi, current) => _dialogs.PickSaveFileAsync(Path.GetFileNameWithoutExtension(current), xmi ? "xmi" : "json");

        var files = workspace.Files.Count == 1 ? "1 file" : $"{workspace.Files.Count} files";
        var name = Path.GetFileNameWithoutExtension(ModelExporter.DefaultFileName(workspace, ExportFormat.Json));
        var dialog = new ExportDialogViewModel(workspace.Directory, name, pick, $"Whole workspace · {files}");
        if (!await ShowDialog(dialog))
            return;

        try
        {
            WriteExport(dialog.IsXmi, dialog.FullPath);
            Bottom.Log($"exported {dialog.FullPath}");
            SaveState = $"Exported to {dialog.Path}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await ShowMessage("Export failed", e.Message);
        }
    }

    private void WriteExport(bool xmi, string path)
    {
        if (Workspace is { } workspace)
            ModelExporter.Write(workspace, xmi ? ExportFormat.Xmi : ExportFormat.Json, path);
    }
}
