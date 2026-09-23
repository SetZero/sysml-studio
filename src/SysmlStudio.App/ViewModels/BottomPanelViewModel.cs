using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysmlStudio.Model;

namespace SysmlStudio.App.ViewModels;

/// <summary>One row of the problems list.</summary>
public sealed class ProblemRow(Diagnostic diagnostic, string relativeFile)
{
    public Diagnostic Diagnostic { get; } = diagnostic;
    public bool IsError => Diagnostic.Severity == Severity.Error;
    public string File { get; } = relativeFile;
    public string Location => string.Create(CultureInfo.InvariantCulture, $"{File}:{Diagnostic.Line}");
    public string Message => SourceDocumentViewModel.Shorten(Diagnostic.Message);
}

/// <summary>One row of the usages list: how an element is named somewhere, the line, and where it is.</summary>
public sealed class UsageRow(string usage, string path, string relativeFile, int line, string snippet)
{
    public string Usage { get; } = usage;
    public string Path { get; } = path;
    public string File { get; } = relativeFile;
    public int Line { get; } = line;
    public string Location { get; } = string.Create(CultureInfo.InvariantCulture, $"{relativeFile}:{line}");
    public string Snippet { get; } = snippet;
}

/// <summary>
/// The panel that opens under the documents: problems and usages. It stays
/// closed until one of them is asked for, and closes again with its ×.
/// </summary>
public sealed partial class BottomPanelViewModel(ShellViewModel shell) : ObservableObject
{
    private readonly ShellViewModel _shell = shell;

    public ObservableCollection<ProblemRow> Problems { get; } = [];

    public ObservableCollection<UsageRow> Usages { get; } = [];

    /// <summary>What the window did, newest last.</summary>
    public ObservableCollection<string> Output { get; } = [];

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsProblems), nameof(ShowsUsages))]
    public partial int SelectedTab { get; set; }

    public bool ShowsProblems { get => SelectedTab == 0; set { if (value) SelectedTab = 0; } }

    public bool ShowsUsages { get => SelectedTab == 1; set { if (value) SelectedTab = 1; } }

    public int ErrorCount => Problems.Count(p => p.IsError);

    public int WarningCount => Problems.Count - ErrorCount;

    public string ProblemsHeader => $"Problems · {Problems.Count}";

    public string UsagesHeader => $"Usages · {Usages.Count}";

    public bool HasNoProblems => Problems.Count == 0;

    public void ShowProblems(SysmlWorkspace? workspace, IEnumerable<Diagnostic> diagnostics)
    {
        Problems.Clear();
        foreach (var diagnostic in diagnostics)
            Problems.Add(new ProblemRow(diagnostic, workspace?.RelativePath(diagnostic.File) ?? System.IO.Path.GetFileName(diagnostic.File)));

        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(ProblemsHeader));
        OnPropertyChanged(nameof(HasNoProblems));
    }

    public void OpenProblems()
    {
        SelectedTab = 0;
        IsOpen = true;
    }

    /// <summary>The declaration first, then every relation that names the element.</summary>
    public void ShowUsages(SysmlWorkspace workspace, Element element)
    {
        Usages.Clear();

        if (element.File is { } declared)
            Usages.Add(MakeRow(workspace, "declared", declared.Path, element.Line, declared.Text));

        foreach (var relation in workspace.Usages(element))
        {
            var source = relation.Source;
            if (source.File is not { } file)
                continue;

            Usages.Add(MakeRow(workspace, UsageWord(relation.Kind), file.Path, source.Line, file.Text));
        }

        OnPropertyChanged(nameof(UsagesHeader));
        SelectedTab = 1;
        IsOpen = true;
    }

    public void Log(string line)
    {
        Output.Add($"{DateTime.Now:HH:mm:ss}  {line}");
        if (Output.Count > 500)
            Output.RemoveAt(0);
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

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

    private static UsageRow MakeRow(SysmlWorkspace workspace, string usage, string path, int line, string text)
    {
        var lines = text.Split('\n');
        var snippet = line - 1 < lines.Length ? lines[line - 1].Trim() : string.Empty;
        return new UsageRow(usage, path, workspace.RelativePath(path), line, snippet);
    }

    /// <summary>How the naming element relates to the named one, in the words of the usages list.</summary>
    public static string UsageWord(RelationKind kind) => kind switch
    {
        RelationKind.Typing => "typed by",
        RelationKind.Specialization => "specialized",
        RelationKind.Redefinition => "redefined",
        RelationKind.Satisfy => "satisfies",
        RelationKind.Verify => "verifies",
        RelationKind.Allocate => "allocated",
        RelationKind.Dependency => "depends",
        RelationKind.Connect or RelationKind.Interface => "connected",
        RelationKind.Flow => "flows",
        RelationKind.Succession => "follows",
        RelationKind.Transition => "transition",
        RelationKind.Import => "imported",
        RelationKind.Composition => "part of",
        _ => kind.ToString().ToLowerInvariant(),
    };
}
