using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Nodify.Avalonia;
using SysmlStudio.App.ViewModels;

namespace SysmlStudio.App.Views;

/// <summary>The one window: browser, diagrams, source and problems.</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void OnOpenFolder(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open a folder of .sysml files",
            AllowMultiple = false,
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { Length: > 0 } path)
            viewModel.Open(path);
    }

    private void OnExit(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnFit(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (this.FindDescendantOfType<NodifyEditor>() is { } editor)
            FitToScreen.Fit(editor);
    }

    private async void OnExportSvg(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedDocument is null)
            return;

        if (await PickSaveFile(viewModel.SelectedDocument.Title, "svg") is { } path)
            viewModel.ExportSvg(path);
    }

    /// <summary>
    /// The canvas itself, rendered at its own size — the same picture, pixels
    /// instead of shapes, for pasting somewhere that cannot read SVG.
    /// </summary>
    private async void OnExportPng(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedDocument is null)
            return;

        if (this.FindDescendantOfType<NodifyEditor>() is not { } editor)
            return;

        if (await PickSaveFile(viewModel.SelectedDocument.Title, "png") is not { } path)
            return;

        var size = new PixelSize((int)Math.Ceiling(editor.Bounds.Width), (int)Math.Ceiling(editor.Bounds.Height));
        if (size.Width <= 0 || size.Height <= 0)
            return;

        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(editor);
        await using var file = File.Create(path);
        bitmap.Save(file, new PngBitmapEncoderOptions());
        viewModel.Status = $"Exported {path}";
    }

    private async Task<string?> PickSaveFile(string suggestedName, string extension)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export as {extension.ToUpperInvariant()}",
            SuggestedFileName = Sanitise(suggestedName) + "." + extension,
            DefaultExtension = extension,
        });

        return file?.TryGetLocalPath();
    }

    /// <summary>A diagram title is prose; a file name cannot be.</summary>
    private static string Sanitise(string name)
    {
        var clean = name.Replace(" — ", "-", StringComparison.Ordinal).Replace(' ', '-');
        return string.Concat(clean.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
    }
}
