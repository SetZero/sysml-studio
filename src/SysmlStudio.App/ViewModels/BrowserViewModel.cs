using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using SysmlStudio.Model;

namespace SysmlStudio.App.ViewModels;

/// <summary>One row of the project browser.</summary>
public sealed partial class ElementViewModel(Element? element, string kind, string label,
                        Func<ObservableCollection<ElementViewModel>> children) : ObservableObject
{
    private readonly Func<ObservableCollection<ElementViewModel>> _children = children;
    private ObservableCollection<ElementViewModel>? _materialised;

    /// <summary>Null for the folder row at the top.</summary>
    public Element? Element { get; } = element;

    /// <summary>The keyword shown before the name: "part def", "pkg", "port".</summary>
    public string Kind { get; } = kind;

    public string Label { get; } = label;

    public string? ShortName => Element?.ShortName is { Length: > 0 } s ? $"‹{s}›" : null;

    /// <summary>"@deferred", shown after the name when the element carries it.</summary>
    public string? Tag => Element?.Metadata.Any(m => m.StartsWith("@deferred", StringComparison.Ordinal)) == true
        ? "@deferred"
        : null;

    /// <summary>The maturity keyword, or "none"; the row's left bar is coloured by it.</summary>
    public string Maturity => Element?.Maturity ?? "none";

    public bool HasMaturity => Element?.Maturity is not null || Tag is not null;

    public bool IsDefinitionLike => Element is { } e && (e.IsDefinition || e.Kind is "package" or "library package");

    public ObservableCollection<ElementViewModel> Children => _materialised ??= _children();

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }
}

/// <summary>
/// The project browser: the model as a tree, filtered by what is typed into
/// the box above it. A filter keeps every element whose name matches, with
/// the path down to it expanded, so a match is never shown out of context.
/// </summary>
public sealed partial class BrowserViewModel : Tool
{
    private SysmlWorkspace? _workspace;

    public BrowserViewModel()
    {
        Id = "Browser";
        Title = "PROJECT BROWSER";
        CanClose = false;
        CanPin = false;
    }

    public ObservableCollection<ElementViewModel> Roots { get; } = [];

    [ObservableProperty]
    public partial string Filter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ElementViewModel? Selected { get; set; }

    /// <summary>Raised when the selection changes to a model element.</summary>
    public event Action<Element?>? SelectionChanged;

    public bool HasWorkspace => _workspace is not null;

    public void Show(SysmlWorkspace? workspace)
    {
        _workspace = workspace;
        OnPropertyChanged(nameof(HasWorkspace));
        Rebuild();
    }

    /// <summary>Selects the row for <paramref name="element"/>, expanding the way down to it.</summary>
    public void Reveal(Element element)
    {
        var path = new Stack<Element>();
        for (var e = element; e?.Parent is not null; e = e.Parent)
            path.Push(e);

        var level = Roots.FirstOrDefault()?.Children;
        ElementViewModel? found = null;
        while (level is not null && path.Count > 0)
        {
            var next = path.Pop();
            found = level.FirstOrDefault(r => ReferenceEquals(r.Element, next));
            if (found is null)
                break;
            found.IsExpanded = true;
            level = found.Children;
        }

        if (found is not null)
            Selected = found;
    }

    partial void OnFilterChanged(string value) => Rebuild();

    partial void OnSelectedChanged(ElementViewModel? value) => SelectionChanged?.Invoke(value?.Element);

    private void Rebuild()
    {
        Roots.Clear();
        if (_workspace is not { } workspace)
            return;

        var filter = Filter.Trim();
        var folder = Path.GetFileName(workspace.Directory.TrimEnd('\\', '/'));
        var parent = Path.GetFileName(Path.GetDirectoryName(workspace.Directory.TrimEnd('\\', '/')) ?? string.Empty);
        var label = $"{parent}/{folder} · {workspace.Files.Count} files";

        var root = new ElementViewModel(null, string.Empty, label, () => ChildrenOf(workspace.Root, filter, depth: 0))
        {
            IsExpanded = true,
        };
        Roots.Add(root);
    }

    private static ObservableCollection<ElementViewModel> ChildrenOf(Element parent, string filter, int depth)
    {
        var rows = new ObservableCollection<ElementViewModel>();
        foreach (var child in parent.Children.Where(IsBrowsable))
        {
            if (filter.Length > 0 && !Matches(child, filter))
                continue;

            var captured = child;
            var row = new ElementViewModel(child, KindLabel(child, depth), child.DisplayName,
                () => ChildrenOf(captured, filter, depth + 1))
            {
                // Top-level packages open, and so does everything on the way to a filter match.
                IsExpanded = filter.Length > 0 ? HasMatchingDescendant(child, filter) : depth == 0 && parent.Children.Count(IsBrowsable) == 1,
            };
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Relationships and anonymous usages clutter a browser; named things belong in it.</summary>
    private static bool IsBrowsable(Element element)
        => element.Name is not null || element.ShortName is not null;

    private static bool Matches(Element element, string filter)
        => NameMatches(element, filter) || HasMatchingDescendant(element, filter);

    private static bool NameMatches(Element element, string filter)
        => element.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
           || (element.ShortName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool HasMatchingDescendant(Element element, string filter)
        => element.Descendants().Any(d => IsBrowsable(d) && NameMatches(d, filter));

    private static string KindLabel(Element element, int depth) => element.Kind switch
    {
        "package" when depth > 0 => "pkg",
        "library package" => "library",
        _ => element.Kind,
    };
}
