using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using SysmlStudio.App.ViewModels;
using SysmlStudio.Diagrams;
using SysmlStudio.Editing;
using SysmlStudio.Model;

namespace SysmlStudio.App.Views;

/// <summary>
/// The right-click menu for an element, the same in the project browser and
/// on a diagram: open it as a diagram, add inside it, rename, type, specialize,
/// document, mark, trace, find, go to its text, delete. Items that cannot
/// apply to this kind of element are left out rather than greyed.
/// </summary>
public static class ElementMenu
{
    public static ContextMenu For(ShellViewModel shell, Element element)
    {
        var items = new List<Control>();

        var kinds = DiagramBuilder.KindsFor(element);
        if (kinds.Count > 0)
        {
            items.Add(Submenu("Open diagram", kinds.Select(k => Item(DiagramDocumentViewModel.KindName(k), () =>
            {
                shell.Select(element);
                shell.OpenDiagramCommand.Execute(k);
            }))));
        }

        var childKinds = ModelEditor.ChildKinds(element);
        if (childKinds.Count > 0)
            items.Add(Submenu("Add", childKinds.Select(k => AsyncItem(k, () => shell.AddTo(element, k)))));

        items.Add(new Separator());
        if (element.Name is not null)
            items.Add(Item("Rename…", () => shell.RenameCommand.Execute(element), "F2"));
        if (!element.IsDefinition && ModelEditor.IsTyped(element.Kind))
            items.Add(Item("Set type…", () => shell.SetTypeCommand.Execute(element)));
        if (element.IsDefinition)
            items.Add(Item("Specializes…", () => shell.SpecializeCommand.Execute(element)));
        items.Add(Item("Doc comment…", () => shell.EditDocCommand.Execute(element)));

        if (shell.MaturityKeywords.Count > 0)
        {
            var marks = shell.MaturityKeywords.Select(k => Item("#" + k, () =>
            {
                shell.Select(element);
                shell.SetMaturityCommand.Execute(k);
            })).ToList();
            marks.Add(Item("none", () =>
            {
                shell.Select(element);
                shell.SetMaturityCommand.Execute(string.Empty);
            }));
            items.Add(Submenu("Maturity", marks));
        }

        if (element.Kind is not ("package" or "library package"))
            items.Add(Item("Satisfies a requirement…", () => shell.AddSatisfyCommand.Execute(element)));

        items.Add(new Separator());
        items.Add(Item("Find usages", () =>
        {
            shell.Select(element);
            shell.FindUsagesCommand.Execute(null);
        }, "Shift+F12"));
        if (element.File is { } file)
            items.Add(Item("Go to source", () => shell.OpenSource(file.Path, element.Line)));

        items.Add(new Separator());
        items.Add(Item("Delete", () => shell.DeleteCommand.Execute(element), "Delete"));

        return new ContextMenu { ItemsSource = items };
    }

    /// <summary>The menu for the empty canvas: add to what the diagram is of, draw a relation, arrange.</summary>
    public static ContextMenu ForCanvas(ShellViewModel shell, DiagramDocumentViewModel diagram)
    {
        var root = diagram.Diagram.Root;
        var items = new List<Control>();

        var childKinds = ModelEditor.ChildKinds(root);
        if (childKinds.Count > 0)
            items.Add(Submenu($"Add to {root.DisplayName}", childKinds.Select(k => AsyncItem(k, () => shell.AddTo(root, k)))));

        var relations = diagram.Toolbox.Where(t => t.IsRelation).ToList();
        if (relations.Count > 0)
            items.Add(Submenu("Draw relation", relations.Select(t => Item(t.Label, () => shell.StartRelationCommand.Execute(t.Relation)))));

        items.Add(new Separator());
        items.Add(Item("Relayout", () => diagram.Relayout()));
        items.Add(Item("Fit to view", () => diagram.Fit(), "Ctrl+0"));
        return new ContextMenu { ItemsSource = items };
    }

    private static MenuItem Item(string header, Action action, string? gesture = null)
        => new() { Header = header, Command = new RelayCommand(action), InputGesture = gesture is null ? null : Avalonia.Input.KeyGesture.Parse(gesture) };

    private static MenuItem AsyncItem(string header, Func<Task> action)
        => new() { Header = header, Command = new AsyncRelayCommand(action) };

    private static MenuItem Submenu(string header, IEnumerable<MenuItem> children)
        => new() { Header = header, ItemsSource = children.ToList() };
}
