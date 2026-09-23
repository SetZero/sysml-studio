using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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

    /// <summary>"deferred", shown at the right of the row when the element carries @deferred.</summary>
    public string? Tag => IsDeferred ? "deferred" : null;

    public bool IsDeferred => Element?.Metadata.Any(m => m.StartsWith("@deferred", StringComparison.Ordinal)) == true;

    /// <summary>The workspace row at the top, drawn bold.</summary>
    public bool IsRoot => Element is null;

    public bool IsPackage => Element?.Kind is "package" or "library package";

    /// <summary>The glyph before the name: a square for a definition, a circle for a usage, dashed when deferred.</summary>
    public bool ShowsSquare => Element is { IsDefinition: true } && !IsDeferred;

    public bool ShowsCircle => Element is { IsDefinition: false } && !IsPackage && !IsDeferred;

    public bool ShowsDashed => IsDeferred && !IsPackage;

    public string ToolTip => Element is { } e ? $"{e.Kind} {e.QualifiedName}" : Label;

    public ObservableCollection<ElementViewModel> Children => _materialised ??= _children();

    /// <summary>The children already built, without building any.</summary>
    public IEnumerable<ElementViewModel> ChildrenIfShown => _materialised ?? [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }
}

/// <summary>
/// The project browser: the model as a tree, filtered by what is typed into
/// the box above it. A filter keeps every element whose name matches, with
/// the path down to it expanded, so a match is never shown out of context.
/// </summary>
public sealed partial class BrowserViewModel : ObservableObject
{
    private SysmlWorkspace? _workspace;

    public ObservableCollection<ElementViewModel> Roots { get; } = [];

    [ObservableProperty]
    public partial string Filter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ElementViewModel? Selected { get; set; }

    /// <summary>Raised when the selection changes to a model element.</summary>
    public event Action<Element?>? SelectionChanged;

    public bool HasWorkspace => _workspace is not null;

    /// <summary>"13 files", beside the panel's caption.</summary>
    public string FilesText => _workspace?.Files.Count switch
    {
        null => string.Empty,
        1 => "1 file",
        var n => $"{n} files",
    };

    private IReadOnlySet<string> _expanded = new HashSet<string>();

    /// <summary>Shows a workspace; rows whose qualified names are in <paramref name="expanded"/> open again.</summary>
    public void Show(SysmlWorkspace? workspace, IReadOnlySet<string>? expanded = null)
    {
        _workspace = workspace;
        _expanded = expanded ?? new HashSet<string>();
        OnPropertyChanged(nameof(HasWorkspace));
        OnPropertyChanged(nameof(FilesText));
        Rebuild();
    }

    /// <summary>The qualified names of the rows open now, for a rebuild to open again.</summary>
    public IEnumerable<string> ExpandedPaths()
    {
        var stack = new Stack<ElementViewModel>(Roots);
        while (stack.Count > 0)
        {
            var row = stack.Pop();
            if (!row.IsExpanded)
                continue;
            if (row.Element?.QualifiedName is { Length: > 0 } name)
                yield return name;
            foreach (var child in row.ChildrenIfShown)
                stack.Push(child);
        }
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
        var label = ShellViewModel.DisplayName(workspace.Directory);

        var expanded = _expanded;
        var root = new ElementViewModel(null, string.Empty, label, () => ChildrenOf(workspace.Root, filter, depth: 0, expanded))
        {
            IsExpanded = true,
        };
        Roots.Add(root);
    }

    private static ObservableCollection<ElementViewModel> ChildrenOf(Element parent, string filter, int depth, IReadOnlySet<string> expanded)
    {
        var rows = new ObservableCollection<ElementViewModel>();
        foreach (var child in parent.Children.Where(IsBrowsable))
        {
            if (filter.Length > 0 && !Matches(child, filter))
                continue;

            var captured = child;
            var row = new ElementViewModel(child, child.Kind, child.DisplayName,
                () => ChildrenOf(captured, filter, depth + 1, expanded))
            {
                // What was open stays open; a filter opens the way to every match.
                IsExpanded = filter.Length > 0
                    ? HasMatchingDescendant(child, filter)
                    : expanded.Contains(child.QualifiedName) || (depth == 0 && parent.Children.Count(IsBrowsable) == 1),
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
}
