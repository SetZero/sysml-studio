using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SysmlStudio.App.ViewModels;
using SysmlStudio.App.Views;

namespace SysmlStudio.App;

/// <summary>The application: one window over one model folder.</summary>
public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel();
            if (Program.StartupFolder is { Length: > 0 } folder && Directory.Exists(folder))
                viewModel.Open(folder);

            desktop.MainWindow = new MainWindow { DataContext = viewModel };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
