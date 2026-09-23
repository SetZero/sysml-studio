using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using SysmlStudio.Model;

namespace SysmlStudio.App.ViewModels;

/// <summary>One name/value row. A row with a target is a link: clicking it goes there.</summary>
public sealed class PropertyRow(string name, string value, bool isChip = false, Element? target = null, string? sourcePath = null, int line = 0)
{
    public string Name { get; } = name;
    public string Value { get; } = value;
    public bool IsChip { get; } = isChip;
    public Element? Target { get; } = target;
    public string? SourcePath { get; } = sourcePath;
    public int Line { get; } = line;
    public bool IsLink => Target is not null || SourcePath is not null;
    public bool IsPlain => !IsChip && !IsLink;
}

/// <summary>
/// What the selected element is, as the model says it: its kind, names,
/// relations, maturity, where it is written, and its doc comment.
/// </summary>
public sealed partial class PropertiesViewModel : Tool
{
    private readonly ShellViewModel _shell;

    public PropertiesViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Id = "Properties";
        Title = "PROPERTIES";
        CanClose = false;
        CanPin = false;
    }

    [ObservableProperty]
    public partial Element? Element { get; set; }

    public bool HasElement => Element is not null;

    public string KindCaption => Element?.Kind.ToUpperInvariant() ?? string.Empty;

    public string Name => Element?.DisplayName ?? string.Empty;

    public string QualifiedName => Element?.QualifiedName ?? string.Empty;

    public string? Documentation => Element?.Documentation;

    public bool HasDocumentation => !string.IsNullOrWhiteSpace(Documentation);

    public ObservableCollection<PropertyRow> Rows { get; } = [];

    partial void OnElementChanged(Element? value)
    {
        OnPropertyChanged(nameof(HasElement));
        OnPropertyChanged(nameof(KindCaption));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(QualifiedName));
        OnPropertyChanged(nameof(Documentation));
        OnPropertyChanged(nameof(HasDocumentation));

        Rows.Clear();
        if (value is null)
            return;

        foreach (var row in RowsFor(value))
            Rows.Add(row);
    }

    [RelayCommand]
    private void Follow(PropertyRow row)
    {
        if (row.Target is { } target)
            _shell.Select(target);
        else if (row.SourcePath is { } path)
            _shell.OpenSource(path, row.Line);
    }

    private IEnumerable<PropertyRow> RowsFor(Element e)
    {
        yield return new PropertyRow("Name", e.Name ?? "—");
        if (e.ShortName is { } shortName)
            yield return new PropertyRow("Short name", $"'{shortName}'");

        foreach (var relation in e.Relations.Where(r => r.Kind == RelationKind.Specialization))
            yield return new PropertyRow("Specializes", relation.TargetReference.Trim(), target: relation.Target);

        foreach (var relation in e.Relations.Where(r => r.Kind == RelationKind.Typing))
            yield return new PropertyRow("Typed by", relation.TargetReference.Trim(), target: relation.Target);

        foreach (var relation in e.Relations.Where(r => r.Kind == RelationKind.Redefinition))
            yield return new PropertyRow("Redefines", relation.TargetReference.Trim(), target: relation.Target);

        if (e.Multiplicity is { } multiplicity)
            yield return new PropertyRow("Multiplicity", multiplicity);

        if (e.Value is { Length: > 0 } value)
            yield return new PropertyRow("Value", value);

        yield return new PropertyRow("Maturity", e.Maturity is { } m ? "#" + m : "—", isChip: e.Maturity is not null);

        foreach (var meta in e.Metadata.Where(m => m.StartsWith('@')))
            yield return new PropertyRow("Metadata", meta);

        var ports = e.Children.Where(c => c.Kind == "port" && c.Name is not null).Select(c => c.Name!).ToList();
        if (ports.Count > 0)
            yield return new PropertyRow("Ports", string.Join(", ", ports));

        var parts = e.Children.Where(c => c.Kind is "part" or "ref" && c.Name is not null)
            .Select(c => c.Name + (c.Multiplicity ?? string.Empty)).ToList();
        if (parts.Count > 0)
            yield return new PropertyRow("Parts", string.Join(", ", parts));

        foreach (var relation in e.Relations.Where(r => r.Kind is RelationKind.Satisfy or RelationKind.Verify
                     or RelationKind.Allocate or RelationKind.Dependency or RelationKind.Transition
                     or RelationKind.Succession or RelationKind.Connect or RelationKind.Flow or RelationKind.Interface))
        {
            var label = relation.Label is { Length: > 0 } l ? $"{l} → " : string.Empty;
            yield return new PropertyRow(relation.Kind.ToString(), label + relation.TargetReference.Trim(), target: relation.Target);
        }

        if (e.File is { } file && _shell.Workspace is { } workspace)
        {
            var stop = (e.Context.Stop ?? e.Context.Start).Line;
            var range = stop > e.Line ? $"{e.Line}-{stop}" : e.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
            yield return new PropertyRow("Source", $"{workspace.RelativePath(file.Path)}:{range}", sourcePath: file.Path, line: e.Line);
        }
    }
}
