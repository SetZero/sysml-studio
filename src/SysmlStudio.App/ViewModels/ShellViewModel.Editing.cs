using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysmlStudio.Diagrams;
using SysmlStudio.Editing;
using SysmlStudio.Model;

namespace SysmlStudio.App.ViewModels;

/// <summary>
/// The editing half of the window. Every command builds a plan of text
/// patches, shows what it will write when there is something to decide, and
/// applies it; the applier refuses a plan that would break a file. After any
/// change the browser, the open diagrams and the source tabs are rebuilt from
/// the model, keeping what the user was looking at.
/// </summary>
public sealed partial class ShellViewModel
{
    private readonly EditHistory _history = new();
    private Element? _relationFrom;
    private bool _selecting;

    /// <summary>The dialog on screen, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDialog))]
    public partial ModalViewModel? Dialog { get; set; }

    public bool HasDialog => Dialog is not null;

    /// <summary>The relation the toolbox is drawing, while it waits for two clicks.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTool))]
    public partial NewRelation? PendingRelation { get; set; }

    public bool HasTool => PendingRelation is not null;

    partial void OnPendingRelationChanged(NewRelation? value) => ActiveDiagram?.ShowActiveTool(value?.ToString());

    [ObservableProperty]
    public partial string? ToolHint { get; set; }

    /// <summary>The metadata definitions the model declares: its maturity keywords.</summary>
    public ObservableCollection<string> MaturityKeywords { get; } = [];

    // ----- applying -----------------------------------------------------------

    /// <summary>
    /// Applies a plan, records it for undo, and refreshes everything from the
    /// model. <paramref name="mapName"/> tells the refresh where renamed
    /// elements went, so selection, expansion and open diagrams follow them.
    /// </summary>
    public bool ApplyEdit(EditPlan plan, Func<string, string>? mapName = null)
    {
        if (Workspace is not { } workspace)
            return false;

        try
        {
            _history.Record(EditApplier.Apply(workspace, plan));
        }
        catch (EditException refused)
        {
            Bottom.Log("refused: " + refused.Message);
            _ = ShowMessage("Edit refused", refused.Message);
            return false;
        }

        Bottom.Log(plan.Description);
        AfterModelChange(mapName);
        return true;
    }

    /// <summary>Rebuilds the views from the model, following renames through <paramref name="mapName"/>.</summary>
    private void AfterModelChange(Func<string, string>? mapName = null)
    {
        if (Workspace is not { } workspace)
            return;

        string Map(string name) => mapName?.Invoke(name) ?? name;

        var selected = SelectedElement?.QualifiedName is { Length: > 0 } s ? Map(s) : null;
        var expanded = Browser.ExpandedPaths().Select(Map).ToHashSet(StringComparer.Ordinal);
        Browser.Show(workspace, expanded);

        foreach (var document in OpenDiagrams().ToList())
        {
            document.PushPositions();
            var stored = DiagramStore.Capture(document.Diagram);
            var positions = stored.Positions.ToDictionary(p => Map(p.Key), p => p.Value, StringComparer.Ordinal);
            stored.Positions = positions;

            var root = workspace.Find(Map(document.Diagram.Root.QualifiedName));
            if (root is null || !DiagramBuilder.KindsFor(root).Contains(document.Diagram.Kind))
            {
                CloseDocument(document);
                continue;
            }

            var diagram = DiagramBuilder.Build(document.Diagram.Kind, root);
            DiagramLayout.Apply(diagram);
            DiagramStore.Restore(diagram, stored);
            ReplaceDocument(document, CreateDiagram(diagram, laidOut: true));
        }

        foreach (var source in OpenSources())
        {
            var text = workspace.Files.FirstOrDefault(f => string.Equals(f.Path, source.Path, StringComparison.OrdinalIgnoreCase))?.Text;
            if (text is not null && text != source.Text.Text)
                source.ReplaceText(text);
            source.IsDirty = workspace.IsDirty(source.Path);
        }

        SearchItems.Clear();
        foreach (var element in workspace.Elements.Where(e => e.Name is not null))
            SearchItems.Add(element.QualifiedName);
        RefreshMaturityKeywords();

        if (selected is not null && workspace.Find(selected) is { } still)
        {
            Select(still);
            Properties.Refresh();
        }
        else
        {
            SelectedElement = null;
            Properties.Element = null;
        }

        RefreshProblems();
        RefreshStatus();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void RefreshMaturityKeywords()
    {
        MaturityKeywords.Clear();
        // Keywords are the metadata definitions with nothing inside: #implemented, not @stage { number = 3; }.
        foreach (var name in Workspace?.Elements
                     .Where(e => e.Kind == "metadata def" && e.Name is not null && e.Children.Count == 0)
                     .Select(e => e.Name!).Distinct() ?? [])
        {
            MaturityKeywords.Add(name);
        }
    }

    /// <summary>The element as the model holds it now; typing in a source tab re-indexes behind the views.</summary>
    private Element? Current(Element? element)
        => element is null || Workspace is null ? null : Workspace.Find(element.QualifiedName) ?? element;

    private DiagramDocumentViewModel CreateDiagram(Diagram diagram, bool laidOut = false)
        => new(diagram, laidOut) { ElementClicked = OnCanvasClicked };

    // ----- dialogs --------------------------------------------------------------

    private async Task<bool> ShowDialog(ModalViewModel dialog)
    {
        Dialog = dialog;
        (dialog as DialogViewModel)?.RefreshPreview();
        try
        {
            return await dialog.Result;
        }
        finally
        {
            Dialog = null;
        }
    }

    private Task<bool> ShowMessage(string title, string message)
        => ShowDialog(new DialogViewModel(title, "OK") { ShowCancel = false, Explanation = message });

    private IEnumerable<string> Definitions()
        => Workspace is null
            ? []
            : Workspace.Elements.Where(e => e.IsDefinition && e.Name is not null).Select(e => e.QualifiedName).Order();

    /// <summary>A reference to what the user picked, written as it resolves from <paramref name="scope"/>.</summary>
    private string ReferenceFrom(string picked, Element scope)
        => Workspace?.Find(picked.Trim()) is { } target ? ModelEditor.Reference(target, scope) : picked.Trim();

    // ----- commands -------------------------------------------------------------

    [RelayCommand]
    private async Task Rename(Element? element)
    {
        if (Current(element ?? SelectedElement) is not { Name: { } oldName } target || Workspace is not { } workspace)
            return;

        var dialog = new DialogViewModel($"Rename {oldName}", "Rename")
        {
            ShowName = true,
            ShowNameLabel = false,
            Name = oldName,
            ShowsFiles = true,
            Summarize = files =>
            {
                var references = files.Sum(f => f.Count);
                var r = references == 1 ? "1 reference" : $"{references} references";
                var f = files.Count == 1 ? "1 file" : $"{files.Count} files";
                return $"Updates {r} in {f}. Formatting and comments stay as they are.";
            },
            Previewer = d => d.Name.Trim() == oldName
                ? new DialogPreview([], [], null)
                : Preview(() => ModelEditor.Rename(workspace, target, d.Name)),
        };

        if (!await ShowDialog(dialog))
            return;

        var oldPath = target.QualifiedName;
        var newPath = oldPath[..^oldName.Length] + dialog.Name.Trim();
        ApplyEdit(ModelEditor.Rename(workspace, target, dialog.Name), name =>
            name == oldPath || name.StartsWith(oldPath + "::", StringComparison.Ordinal) ? newPath + name[oldPath.Length..] : name);
    }

    [RelayCommand]
    private async Task Delete(Element? element)
    {
        if (Current(element ?? SelectedElement) is not { } target || Workspace is not { } workspace)
            return;

        EditPlan plan;
        try
        {
            plan = ModelEditor.Delete(workspace, target);
        }
        catch (EditException refused)
        {
            await ShowMessage("Edit refused", refused.Message);
            return;
        }

        var dialog = new DialogViewModel($"Delete {target.DisplayName}", "Delete")
        {
            IsDanger = true,
            Explanation = plan.Description,
            Previewer = _ => Preview(() => plan),
        };
        if (await ShowDialog(dialog))
            ApplyEdit(plan);
    }

    /// <summary>Adds an element inside the selection (or the given parent), asking for its name, kind and type.</summary>
    [RelayCommand]
    private async Task Add(string? kind) => await AddTo(Current(SelectedElement), kind);

    /// <summary>Adds an element inside the element the diagram in front is of.</summary>
    [RelayCommand]
    private async Task AddToDiagram(string? kind) => await AddTo(Current(ActiveDiagram?.Diagram.Root), kind);

    public async Task AddTo(Element? parent, string? kind)
    {
        if (parent is null || Workspace is not { } workspace)
            return;

        var kinds = ModelEditor.ChildKinds(parent);
        if (kinds.Count == 0)
        {
            await ShowMessage("Edit refused", $"Nothing can be added inside a {parent.Kind}.");
            return;
        }

        if (kind is not null && !kinds.Contains(kind))
        {
            await ShowMessage("Edit refused", $"A {kind} cannot go inside a {parent.Kind}.");
            return;
        }

        var dialog = new DialogViewModel($"Add to {parent.DisplayName}", "Add")
        {
            ShowName = true,
            Kinds = new ObservableCollection<string>(kind is null ? kinds : [kind]),
            Kind = kind ?? kinds[0],
            KindTakesType = k => k is not null && ModelEditor.IsTyped(k),
            TypeChoices = new ObservableCollection<string>(Definitions()),
            Previewer = d => Preview(() => ModelEditor.AddChild(parent, d.Kind!, d.Name,
                string.IsNullOrWhiteSpace(d.TypeText) ? null : ReferenceFrom(d.TypeText, parent))),
        };

        if (!await ShowDialog(dialog))
            return;

        var type = string.IsNullOrWhiteSpace(dialog.TypeText) ? null : ReferenceFrom(dialog.TypeText, parent);
        if (ApplyEdit(ModelEditor.AddChild(parent, dialog.Kind ?? kinds[0], dialog.Name, type))
            && workspace.Find($"{parent.QualifiedName}::{dialog.Name.Trim()}") is { } added)
        {
            Select(added);
        }
    }

    [RelayCommand]
    private async Task SetType(Element? element)
    {
        if (Current(element ?? SelectedElement) is not { } target || Workspace is null)
            return;

        var current = target.Relations.FirstOrDefault(r => r.Kind == RelationKind.Typing)?.TargetReference.Trim() ?? string.Empty;
        var dialog = new DialogViewModel($"Type of {target.DisplayName}", "Set type")
        {
            AlwaysShowType = true,
            TypeText = current,
            TypeChoices = new ObservableCollection<string>(Definitions()),
            Previewer = d => Preview(() => ModelEditor.SetTyping(target, ReferenceFrom(d.TypeText, target))),
        };
        if (await ShowDialog(dialog))
            ApplyEdit(ModelEditor.SetTyping(target, ReferenceFrom(dialog.TypeText, target)));
    }

    [RelayCommand]
    private async Task Specialize(Element? element)
    {
        if (Current(element ?? SelectedElement) is not { } target || Workspace is null)
            return;

        var dialog = new DialogViewModel($"{target.DisplayName} specializes", "Add")
        {
            AlwaysShowType = true,
            TypeLabel = "Specializes",
            TypeChoices = new ObservableCollection<string>(Definitions().Where(q => q != target.QualifiedName)),
            Previewer = d => Preview(() => ModelEditor.AddSpecialization(target, ReferenceFrom(d.TypeText, target))),
        };
        if (await ShowDialog(dialog))
            ApplyEdit(ModelEditor.AddSpecialization(target, ReferenceFrom(dialog.TypeText, target)));
    }

    [RelayCommand]
    private async Task EditDoc(Element? element)
    {
        if (Current(element ?? SelectedElement) is not { } target || Workspace is null)
            return;

        var dialog = new DialogViewModel($"Description of {target.DisplayName}", "Save")
        {
            ShowText = true,
            TextLabel = "Doc comment",
            Text = target.Documentation ?? string.Empty,
            Previewer = d => Preview(() => ModelEditor.SetDocumentation(target, d.Text)),
        };
        if (await ShowDialog(dialog))
            ApplyEdit(ModelEditor.SetDocumentation(target, dialog.Text));
    }

    [RelayCommand]
    private void SetMaturity(string? keyword)
    {
        if (Current(SelectedElement) is not { } target || Workspace is null)
            return;

        try
        {
            ApplyEdit(ModelEditor.SetMaturity(target, string.IsNullOrEmpty(keyword) ? null : keyword, MaturityKeywords));
        }
        catch (EditException refused)
        {
            _ = ShowMessage("Edit refused", refused.Message);
        }
    }

    [RelayCommand]
    private async Task AddSatisfy(Element? element)
    {
        if (Current(element ?? SelectedElement) is not { } subject || Workspace is not { } workspace)
            return;

        var requirements = workspace.Elements.Where(e => e.Kind is "requirement" or "requirement def" && e.Name is not null)
            .Select(e => e.QualifiedName).Order();
        var dialog = new DialogViewModel($"{subject.DisplayName} satisfies", "Add satisfy")
        {
            AlwaysShowType = true,
            TypeLabel = "Requirement",
            TypeChoices = new ObservableCollection<string>(requirements),
            Previewer = d => Preview(() => ModelEditor.AddRelation(NewRelation.Satisfy, subject,
                workspace.Find(d.TypeText.Trim()) ?? throw new EditException("Pick a requirement from the list."), subject)),
        };

        if (await ShowDialog(dialog) && workspace.Find(dialog.TypeText.Trim()) is { } requirement)
            ApplyEdit(ModelEditor.AddRelation(NewRelation.Satisfy, subject, requirement, subject));
    }

    /// <summary>Starts drawing a relation on the diagram in front: click the source box, then the target.</summary>
    [RelayCommand]
    private void StartRelation(string kind)
    {
        if (ActiveDiagram is null || !Enum.TryParse<NewRelation>(kind, out var relation))
            return;

        PendingRelation = relation;
        _relationFrom = null;
        ToolHint = $"Click the {SourceWord(relation)} to {Verb(relation)} · Esc to cancel";
    }

    [RelayCommand]
    private void CancelTool()
    {
        PendingRelation = null;
        _relationFrom = null;
        ToolHint = null;
        Dialog?.CancelCommand.Execute(null);
    }

    /// <summary>A box was clicked on a canvas: select it, or feed the relation tool.</summary>
    private void OnCanvasClicked(Element element)
    {
        if (PendingRelation is { } relation && Workspace is not null && ActiveDiagram is { } diagram)
        {
            if (_relationFrom is null)
            {
                _relationFrom = element;
                ToolHint = $"Click the {TargetWord(relation)} {element.DisplayName} should {Verb(relation)} · Esc to cancel";
                return;
            }

            var from = Current(_relationFrom)!;
            var to = Current(element)!;
            var root = Current(diagram.Diagram.Root)!;
            CancelTool();
            try
            {
                ApplyEdit(ModelEditor.AddRelation(relation, from, to, root));
            }
            catch (EditException refused)
            {
                _ = ShowMessage("Edit refused", refused.Message);
            }

            return;
        }

        if (_selecting)
            return;

        _selecting = true;
        try
        {
            Browser.Reveal(element);
            SelectedElement = element;
            Properties.Element = element;
        }
        finally
        {
            _selecting = false;
        }
    }

    /// <summary>A plan's preview, or the reason it cannot be made, for a dialog to show as the user types.</summary>
    private DialogPreview Preview(Func<EditPlan> plan)
    {
        try
        {
            var built = plan();
            var files = built.Edits
                .GroupBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => new FileChange(Workspace!.RelativePath(g.Key), g.Count()))
                .OrderByDescending(f => f.Count).ThenBy(f => f.File, StringComparer.Ordinal)
                .ToList();
            return new DialogPreview(built.Preview(Workspace!), files, null);
        }
        catch (EditException refused)
        {
            return new DialogPreview([], [], refused.Message);
        }
    }

    // ----- words for the relation tool's hint ------------------------------------

    private static string Verb(NewRelation relation) => relation switch
    {
        NewRelation.Connect => "connect",
        NewRelation.Flow => "flow to",
        NewRelation.Succession => "come before",
        NewRelation.Transition => "transition to",
        NewRelation.Satisfy => "satisfy",
        NewRelation.Dependency => "depend on",
        NewRelation.Allocate => "allocate to",
        NewRelation.Specialization => "specialize",
        _ => "own",
    };

    private static string SourceWord(NewRelation relation) => relation switch
    {
        NewRelation.Connect or NewRelation.Flow => "part or port",
        NewRelation.Succession => "first action",
        NewRelation.Transition => "state to leave",
        NewRelation.Satisfy => "element that should",
        NewRelation.Specialization => "definition that should",
        NewRelation.Composition => "owner that should",
        _ => "element that should",
    };

    private static string TargetWord(NewRelation relation) => relation switch
    {
        NewRelation.Connect or NewRelation.Flow => "part or port",
        NewRelation.Succession => "action",
        NewRelation.Transition => "state",
        NewRelation.Satisfy => "requirement",
        NewRelation.Specialization => "definition",
        _ => "element",
    };
}
