using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysmlStudio.Model;

namespace SysmlStudio.App.ViewModels;

/// <summary>One section of the inspector: a label over a value. A value with a target is a link.</summary>
public sealed class PropertyRow(string name, string value, Element? target = null)
{
    public string Name { get; } = name;
    public string Value { get; } = value;
    public Element? Target { get; } = target;
    public bool IsLink => Target is not null;
    public bool IsPlain => !IsLink;
}

/// <summary>One segment of the maturity switch: a keyword the model declares.</summary>
public sealed class MaturityOption(string keyword, bool isCurrent)
{
    public string Keyword { get; } = keyword;
    public string Label { get; } = Words(keyword);
    public bool IsCurrent { get; } = isCurrent;

    /// <summary>"writtenAhead" → "Written ahead".</summary>
    public static string Words(string keyword)
    {
        var text = new System.Text.StringBuilder();
        foreach (var c in keyword)
        {
            if (char.IsUpper(c) && text.Length > 0)
                text.Append(' ').Append(char.ToLowerInvariant(c));
            else
                text.Append(text.Length == 0 ? char.ToUpperInvariant(c) : c);
        }

        return text.ToString();
    }
}

/// <summary>
/// The inspector: what the selected element is — its kind, name and owner,
/// what it specializes and holds, its maturity, its doc comment — and how
/// many places name it.
/// </summary>
public sealed partial class PropertiesViewModel(ShellViewModel shell) : ObservableObject
{
    private readonly ShellViewModel _shell = shell;

    [ObservableProperty]
    public partial Element? Element { get; set; }

    public bool HasElement => Element is not null;

    /// <summary>"Part definition", "Requirement · S.1".</summary>
    public string KindCaption
    {
        get
        {
            if (Element is not { } e)
                return string.Empty;
            var kind = KindWords(e.Kind);
            return e.ShortName is { Length: > 0 } s ? $"{kind} · {s}" : kind;
        }
    }

    public string Name => Element?.DisplayName ?? string.Empty;

    /// <summary>Where the element lives: its owner's qualified name.</summary>
    public string Owner => Element?.Parent is { Parent: not null } parent ? parent.QualifiedName : string.Empty;

    public string? Documentation => Element?.Documentation;

    public bool HasDocumentation => !string.IsNullOrWhiteSpace(Documentation);

    public ObservableCollection<PropertyRow> Rows { get; } = [];

    public ObservableCollection<MaturityOption> Maturity { get; } = [];

    public bool HasMaturity => Maturity.Count > 0;

    /// <summary>Up to three keywords fit on one row of the switch; more go two to a row.</summary>
    public int MaturityColumns => Maturity.Count > 3 ? 2 : Math.Max(1, Maturity.Count);

    /// <summary>"Used in 5 places".</summary>
    [ObservableProperty]
    public partial string UsedIn { get; set; } = string.Empty;

    /// <summary>"scheduling.sysml:16".</summary>
    public string SourceLink => Element?.File is { } file && _shell.Workspace is { } workspace
        ? string.Create(CultureInfo.InvariantCulture, $"{workspace.RelativePath(file.Path)}:{Element.Line}")
        : string.Empty;

    /// <summary>Everything is read from the model again: after an edit, or when the maturity keywords change.</summary>
    public void Refresh() => OnElementChanged(Element);

    partial void OnElementChanged(Element? value)
    {
        OnPropertyChanged(nameof(HasElement));
        OnPropertyChanged(nameof(KindCaption));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Owner));
        OnPropertyChanged(nameof(Documentation));
        OnPropertyChanged(nameof(HasDocumentation));
        OnPropertyChanged(nameof(SourceLink));

        Rows.Clear();
        Maturity.Clear();
        UsedIn = string.Empty;
        if (value is null)
        {
            OnPropertyChanged(nameof(HasMaturity));
            OnPropertyChanged(nameof(MaturityColumns));
            return;
        }

        foreach (var row in RowsFor(value))
            Rows.Add(row);

        foreach (var keyword in _shell.MaturityKeywords)
            Maturity.Add(new MaturityOption(keyword, value.Maturity == keyword));
        OnPropertyChanged(nameof(HasMaturity));
        OnPropertyChanged(nameof(MaturityColumns));

        if (_shell.Workspace is { } workspace)
        {
            var count = workspace.Usages(value).Count();
            UsedIn = count == 1 ? "Used in 1 place" : $"Used in {count} places";
        }
    }

    [RelayCommand]
    private void Follow(PropertyRow row)
    {
        if (row.Target is { } target)
            _shell.Select(target);
    }

    [RelayCommand]
    private void OpenSource()
    {
        if (Element?.File is { } file)
            _shell.OpenSource(file.Path, Element.Line);
    }

    [RelayCommand]
    private void FindUsages()
    {
        if (Element is { } element && _shell.Workspace is { } workspace)
            _shell.Bottom.ShowUsages(workspace, element);
    }

    /// <summary>A maturity segment was clicked: that keyword, or none when it was the current one.</summary>
    [RelayCommand]
    private void PickMaturity(MaturityOption option)
        => _shell.SetMaturityCommand.Execute(option.IsCurrent ? string.Empty : option.Keyword);

    private static IEnumerable<PropertyRow> RowsFor(Element e)
    {
        foreach (var relation in e.Relations.Where(r => r.Kind == RelationKind.Specialization))
            yield return new PropertyRow("Specializes", relation.TargetReference.Trim(), relation.Target);

        foreach (var relation in e.Relations.Where(r => r.Kind == RelationKind.Typing))
            yield return new PropertyRow("Typed by", relation.TargetReference.Trim(), relation.Target);

        foreach (var relation in e.Relations.Where(r => r.Kind == RelationKind.Redefinition))
            yield return new PropertyRow("Redefines", relation.TargetReference.Trim(), relation.Target);

        if (e.Multiplicity is { } multiplicity)
            yield return new PropertyRow("Multiplicity", multiplicity);

        if (e.Value is { Length: > 0 } value)
            yield return new PropertyRow("Value", value);

        foreach (var (label, kinds) in new (string, string[])[]
                 {
                     ("Ports", ["port"]),
                     ("Parts", ["part", "ref"]),
                     ("Attributes", ["attribute"]),
                     ("Actions", ["action"]),
                     ("States", ["state"]),
                 })
        {
            var names = e.Children.Where(c => kinds.Contains(c.Kind) && c.Name is not null)
                .Select(c => c.Name + (c.Multiplicity ?? string.Empty)).ToList();
            if (names.Count > 0)
                yield return new PropertyRow(label, string.Join(", ", names));
        }

        foreach (var relation in e.Relations.Where(r => r.Kind is RelationKind.Satisfy or RelationKind.Verify
                     or RelationKind.Allocate or RelationKind.Dependency or RelationKind.Transition
                     or RelationKind.Connect or RelationKind.Flow or RelationKind.Interface))
        {
            var from = relation.OriginReference is { Length: > 0 } o ? $"{o.Trim()} → " : string.Empty;
            yield return new PropertyRow(MaturityOption.Words(relation.Kind.ToString()), from + relation.TargetReference.Trim(), relation.Target);
        }

        foreach (var meta in e.Metadata.Where(m => m.StartsWith('@')))
            yield return new PropertyRow("Metadata", meta);
    }

    /// <summary>"part def" → "Part definition".</summary>
    public static string KindWords(string kind)
    {
        var words = kind.EndsWith(" def", StringComparison.Ordinal) ? kind[..^4] + " definition" : kind;
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }
}
