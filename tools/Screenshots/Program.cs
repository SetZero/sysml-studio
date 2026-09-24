using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SysmlStudio.App;
using SysmlStudio.App.ViewModels;
using SysmlStudio.App.Views;
using SysmlStudio.Diagrams;

// Renders the real window, headless, over the drone sample and saves the
// pictures the README and the website show. Nothing is saved to the model:
// every dialog is cancelled and every edit stays in memory.
//
//     dotnet run --project tools/Screenshots -- docs/images

var output = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("docs", "images"));
var model = Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine("samples", "drone"));
Directory.CreateDirectory(output);

// Settings and recent folders go to a throwaway place, never the user's own.
Environment.SetEnvironmentVariable("SYSML_STUDIO_HOME",
    Path.Combine(Path.GetTempPath(), "sysml-studio-screenshots", "home"));

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

Shoot("definition", shell =>
{
    shell.Select(Find(shell, "DroneStructure"));
    shell.OpenDiagramCommand.Execute(DiagramKind.Definition);
    Settle();
    shell.Select(Find(shell, "DroneStructure::FlightController"));
});

Shoot("interconnection", shell =>
{
    shell.Select(Find(shell, "DroneStructure::Drone"));
    shell.OpenDiagramCommand.Execute(DiagramKind.Interconnection);
});

Shoot("requirements", shell =>
{
    shell.Select(Find(shell, "DroneRequirements"));
    shell.OpenDiagramCommand.Execute(DiagramKind.Requirements);
    Settle();
    shell.Select(Find(shell, "DroneRequirements::Stability"));
});

Shoot("action", shell =>
{
    shell.Select(Find(shell, "DroneBehaviour::Deliver"));
    shell.OpenDiagramCommand.Execute(DiagramKind.ActionFlow);
});

Shoot("state", shell =>
{
    shell.Select(Find(shell, "DroneBehaviour::FlightModes"));
    shell.OpenDiagramCommand.Execute(DiagramKind.StateMachine);
});

Shoot("rename", shell =>
{
    shell.Select(Find(shell, "DroneStructure"));
    shell.OpenDiagramCommand.Execute(DiagramKind.Definition);
    Settle();
    shell.RenameCommand.Execute(Find(shell, "DroneStructure::FlightController"));
    Settle();
    ((DialogViewModel)shell.Dialog!).Name = "Autopilot";
});

Shoot("source", shell =>
{
    var file = shell.Workspace!.Files.First(f => f.Path.EndsWith("structure.sysml", StringComparison.Ordinal));
    shell.OpenSource(file.Path, 20);
    Settle();
    var source = (SourceDocumentViewModel)shell.ActiveDocument!;
    source.Text.Insert(source.Text.TextLength, "\n    part def Winch {");
    source.FlushPendingReparse();
    source.GoToLine(source.ErrorLine);
    Settle();
    shell.ToggleProblemsCommand.Execute(null);
});

Shoot("export", shell =>
{
    shell.Select(Find(shell, "DroneRequirements"));
    shell.OpenDiagramCommand.Execute(DiagramKind.Requirements);
    Settle();
    _ = shell.ExportCommand.ExecuteAsync(null);
});

Shoot("settings", shell =>
{
    shell.Select(Find(shell, "DroneStructure::Drone"));
    shell.OpenDiagramCommand.Execute(DiagramKind.Interconnection);
    Settle();
    _ = shell.OpenSettingsCommand.ExecuteAsync(null);
});

Console.WriteLine($"Saved to {output}");

// Each view twice: name.png in the light theme, name-dark.png in the dark.
void Shoot(string name, Action<ShellViewModel> arrange)
{
    ShootIn(name, dark: false, arrange);
    ShootIn(name + "-dark", dark: true, arrange);
}

void ShootIn(string name, bool dark, Action<ShellViewModel> arrange)
{
    var shell = new ShellViewModel();
    var window = new MainWindow { DataContext = shell, Width = 1600, Height = 1000 };
    shell.Attach(window);
    window.Show();
    shell.Open(model);
    shell.IsDark = dark;
    Settle();

    arrange(shell);
    Settle();

    // The window's own frame: a RenderTargetBitmap leaves out the dialog
    // overlay and the frosted panels.
    using var frame = window.CaptureRenderedFrame()
        ?? throw new InvalidOperationException($"{name}: nothing was rendered");
    var path = Path.Combine(output, name + ".png");
    using (var file = File.Create(path))
        frame.Save(file, new PngBitmapEncoderOptions());

    Console.WriteLine(path);
    shell.IsDark = false;
    window.Close();
    Settle();
}

// Lets layout, bindings and the fit-to-view retries run.
static void Settle()
{
    for (var i = 0; i < 30; i++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(10);
    }
}

static SysmlStudio.Model.Element Find(ShellViewModel shell, string name)
    => shell.Workspace?.Find(name) ?? throw new InvalidOperationException($"the sample has no {name}");
