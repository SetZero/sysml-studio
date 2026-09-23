using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysmlStudio.Diagrams;
using SysmlStudio.Model;

namespace SysmlStudio.App.ViewModels;

/// <summary>One row of the model browser.</summary>
public sealed partial class ElementViewModel(Element element) : ObservableObject
{
    private readonly Lazy<ObservableCollection<ElementViewModel>> _children = new(
            () => new ObservableCollection<ElementViewModel>(element.Children.Select(c => new ElementViewModel(c))));

    public Element Element { get; } = element;

    public ObservableCollection<ElementViewModel> Children => _children.Value;

    public string Label => Element.ShortName is { Length: > 0 } shortName
        ? $"{Element.DisplayName} <{shortName}>"
        : Element.DisplayName;

    public string Kind => Element.Kind;

    public string Maturity => Element.Maturity ?? "none";

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }
}

/// <summary>The window: a model, a browser over it, open documents and the problem list.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Folder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ElementViewModel? SelectedElement { get; set; }

    [ObservableProperty]
    public partial DiagramDocumentViewModel? SelectedDocument { get; set; }

    [ObservableProperty]
    public partial string SourceText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; set; } = "No model open.";
    public SysmlWorkspace? Workspace { get; private set; }

    public ObservableCollection<ElementViewModel> Roots { get; } = [];

    public ObservableCollection<DiagramDocumentViewModel> Documents { get; } = [];

    public ObservableCollection<string> Problems { get; } = [];

    /// <summary>The diagrams that can be drawn of whatever is selected.</summary>
    public ObservableCollection<DiagramKind> AvailableDiagrams { get; } = [];

    /// <summary>Reads a folder of .sysml files and shows what is in it.</summary>
    public void Open(string folder)
    {
        Workspace = SysmlWorkspace.Load(folder);
        Folder = folder;

        Roots.Clear();
        foreach (var root in Workspace.Root.Children)
            Roots.Add(new ElementViewModel(root) { IsExpanded = true });

        Problems.Clear();
        foreach (var error in Workspace.Errors)
            Problems.Add(error.ToString());
        foreach (var unresolved in Workspace.UnresolvedReferences)
            Problems.Add(unresolved);

        // Whatever was open last time, where it was left.
        Documents.Clear();
        foreach (var diagram in DiagramStore.Reopen(DiagramStore.Load(folder), Workspace))
            Documents.Add(new DiagramDocumentViewModel(diagram, laidOut: true));
        SelectedDocument = Documents.FirstOrDefault();

        Status = $"{Workspace.Files.Count} files, {Workspace.Elements.Count()} elements, "
            + $"{Workspace.Errors.Count} syntax errors, {Workspace.UnresolvedReferences.Count} unresolved references";
    }

    /// <summary>
    /// Writes the sidecar: which diagrams are open and where their nodes sit.
    /// Nothing about this goes into the .sysml files.
    /// </summary>
    [RelayCommand]
    public void SaveLayout()
    {
        if (Folder.Length == 0)
            return;

        var stored = new StoredDiagrams();
        foreach (var document in Documents)
        {
            document.PushPositions();
            stored.Diagrams.Add(DiagramStore.Capture(document.Diagram));
        }

        DiagramStore.Save(Folder, stored);
        Status = $"Layout saved to {DiagramStore.PathFor(Folder)}";
    }

    /// <summary>Writes the open diagram as SVG.</summary>
    public void ExportSvg(string path)
    {
        if (SelectedDocument is not { } document)
            return;

        document.PushPositions();
        SvgExporter.Write(document.Diagram, path);
        Status = $"Exported {path}";
    }

    [RelayCommand]
    private void OpenDiagram(DiagramKind kind)
    {
        if (SelectedElement is not { } selected)
            return;

        var diagram = DiagramBuilder.Build(kind, selected.Element);
        var document = new DiagramDocumentViewModel(diagram);
        Documents.Add(document);
        SelectedDocument = document;
    }

    [RelayCommand]
    private void CloseDocument(DiagramDocumentViewModel document)
    {
        Documents.Remove(document);
        SelectedDocument = Documents.LastOrDefault();
    }

    [RelayCommand]
    private void Relayout() => SelectedDocument?.Relayout();

    partial void OnSelectedElementChanged(ElementViewModel? value)
    {
        AvailableDiagrams.Clear();
        if (value is null)
        {
            SourceText = string.Empty;
            return;
        }

        foreach (var kind in DiagramBuilder.KindsFor(value.Element))
            AvailableDiagrams.Add(kind);

        SourceText = SourceOf(value.Element);
    }

    /// <summary>The element as it is written, taken straight out of the file.</summary>
    private static string SourceOf(Element element)
    {
        if (element.File is not { } file)
            return string.Empty;

        var start = element.Context.Start.StartIndex;
        var stop = (element.Context.Stop ?? element.Context.Start).StopIndex;
        if (start < 0 || stop < start || stop >= file.Text.Length)
            return string.Empty;

        return file.Text[start..(stop + 1)];
    }
}
