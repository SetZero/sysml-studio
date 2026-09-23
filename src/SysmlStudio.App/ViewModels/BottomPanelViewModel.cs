using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using SysmlStudio.Model;

namespace SysmlStudio.App.ViewModels;

/// <summary>One row of the problems table.</summary>
public sealed class ProblemRow(Diagnostic diagnostic, string relativeFile)
{
    public Diagnostic Diagnostic { get; } = diagnostic;
    public string Severity => Diagnostic.Severity == Model.Severity.Error ? "error" : "warning";
    public bool IsError => Diagnostic.Severity == Model.Severity.Error;
    public string File { get; } = relativeFile;
    public string Position => string.Create(CultureInfo.InvariantCulture, $"{Diagnostic.Line}:{Diagnostic.Column}");
    public string Message => Diagnostic.Message;
}

/// <summary>One row of the usages table: where an element is named, and how.</summary>
public sealed class UsageRow(string usage, string path, string relativeFile, int line, int column, string snippet)
{
    public string Usage { get; } = usage;
    public string Path { get; } = path;
    public string File { get; } = relativeFile;
    public int Line { get; } = line;
    public string Position { get; } = string.Create(CultureInfo.InvariantCulture, $"{line}:{column}");
    public string Snippet { get; } = snippet;
}

/// <summary>The panel under the documents: problems, usages and the output log.</summary>
public sealed partial class BottomPanelViewModel : Tool
{
    private readonly ShellViewModel _shell;

    public BottomPanelViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Id = "Output";
        Title = "PROBLEMS";
        CanClose = false;
        CanPin = false;
    }

    public ObservableCollection<ProblemRow> Problems { get; } = [];

    public ObservableCollection<UsageRow> Usages { get; } = [];

    public ObservableCollection<string> Output { get; } = [];

    [ObservableProperty]
    public partial string UsagesOf { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsProblems), nameof(ShowsUsages), nameof(ShowsOutput), nameof(Hint))]
    public partial int SelectedTab { get; set; }

    public bool ShowsProblems { get => SelectedTab == 0; set { if (value) SelectedTab = 0; } }

    public bool ShowsUsages { get => SelectedTab == 1; set { if (value) SelectedTab = 1; } }

    public bool ShowsOutput { get => SelectedTab == 2; set { if (value) SelectedTab = 2; } }

    public int ErrorCount => Problems.Count(p => p.IsError);

    public int WarningCount => Problems.Count - ErrorCount;

    public string Hint => SelectedTab switch
    {
        1 when UsagesOf.Length > 0 => $"Find usages: {UsagesOf}",
        2 => string.Empty,
        _ => "click a row to jump to source",
    };

    public void ShowProblems(SysmlWorkspace? workspace, IEnumerable<Diagnostic> diagnostics)
    {
        Problems.Clear();
        foreach (var diagnostic in diagnostics)
            Problems.Add(new ProblemRow(diagnostic, workspace?.RelativePath(diagnostic.File) ?? System.IO.Path.GetFileName(diagnostic.File)));

        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WarningCount));
    }

    /// <summary>The declaration first, then every relation that names the element.</summary>
    public void ShowUsages(SysmlWorkspace workspace, Element element)
    {
        Usages.Clear();
        UsagesOf = element.QualifiedName;

        if (element.File is { } declared)
            Usages.Add(MakeRow(workspace, "declares", declared.Path, element.Line, element.Context.Start.Column + 1, declared.Text));

        foreach (var relation in workspace.Usages(element))
        {
            var source = relation.Source;
            if (source.File is not { } file)
                continue;

            Usages.Add(MakeRow(workspace, UsageWord(relation.Kind), file.Path, source.Line, source.Context.Start.Column + 1, file.Text));
        }

        OnPropertyChanged(nameof(Hint));
        SelectedTab = 1;
    }

    public void Log(string line)
    {
        Output.Add($"{DateTime.Now:HH:mm:ss}  {line}");
        if (Output.Count > 500)
            Output.RemoveAt(0);
    }

    [RelayCommand]
    private void OpenProblem(ProblemRow? row)
    {
        if (row is not null)
            _shell.OpenSource(row.Diagnostic.File, row.Diagnostic.Line);
    }

    [RelayCommand]
    private void OpenUsage(UsageRow? row)
    {
        if (row is not null)
            _shell.OpenSource(row.Path, row.Line);
    }

    private static UsageRow MakeRow(SysmlWorkspace workspace, string usage, string path, int line, int column, string text)
    {
        var lines = text.Split('\n');
        var snippet = line - 1 < lines.Length ? lines[line - 1].Trim() : string.Empty;
        return new UsageRow(usage, path, workspace.RelativePath(path), line, column, snippet);
    }

    private static string UsageWord(RelationKind kind) => kind switch
    {
        RelationKind.Typing => "types",
        RelationKind.Specialization => "specializes",
        RelationKind.Redefinition => "redefines",
        RelationKind.Satisfy => "satisfy",
        RelationKind.Verify => "verify",
        RelationKind.Allocate => "allocate",
        RelationKind.Dependency => "depends",
        _ => kind.ToString().ToLowerInvariant(),
    };
}
