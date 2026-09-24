using SysmlStudio.Model;
using SysmlStudio.Syntax;

namespace SysmlStudio.Editing;

/// <summary>One replacement in one file: <see cref="Length"/> characters at <see cref="Start"/> become <see cref="Text"/>.</summary>
public sealed record TextEdit(string Path, int Start, int Length, string Text);

/// <summary>What an edit operation wants to change, before anything is changed.</summary>
public sealed record EditPlan(string Description, IReadOnlyList<TextEdit> Edits)
{
    /// <summary>The files the plan touches.</summary>
    public IEnumerable<string> Files => Edits.Select(e => e.Path).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The plan as the lines it changes: for each patch, the lines it touches
    /// as they are ("-") and as they will be ("+"), and nothing in between.
    /// </summary>
    public IReadOnlyList<string> Preview(SysmlWorkspace workspace)
    {
        var lines = new List<string>();
        foreach (var path in Files)
        {
            var text = workspace[path].Text;
            var name = workspace.RelativePath(path);
            var edits = Edits.Where(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.Start).ToList();

            // Group patches by the block of whole lines they touch.
            var i = 0;
            while (i < edits.Count)
            {
                var blockStart = LineStart(text, edits[i].Start);
                var blockEnd = LineEnd(text, edits[i].Start + edits[i].Length);
                var group = new List<TextEdit> { edits[i] };
                while (++i < edits.Count && edits[i].Start <= blockEnd)
                {
                    group.Add(edits[i]);
                    blockEnd = Math.Max(blockEnd, LineEnd(text, edits[i].Start + edits[i].Length));
                }

                var before = text[blockStart..blockEnd];
                var after = EditApplier.ApplyToText(before, group.Select(e => e with { Start = e.Start - blockStart }));
                var firstLine = text.AsSpan(0, blockStart).Count('\n') + 1;

                foreach (var (line, n) in before.Split('\n').Select((l, k) => (l, k)))
                    lines.Add($"{name}:{firstLine + n} - {line.Trim()}");
                foreach (var (line, n) in after.Split('\n').Select((l, k) => (l, k)))
                {
                    if (after.Length > 0 || n > 0)
                        lines.Add($"{name}:{firstLine + n} + {line.Trim()}");
                }
            }
        }

        return lines;
    }

    private static int LineStart(string text, int offset)
        => offset <= 0 ? 0 : text.LastIndexOf('\n', Math.Min(offset, text.Length) - 1) + 1;

    private static int LineEnd(string text, int offset)
    {
        // From the offset itself: a patch that takes a whole line with its
        // newline ends at the next line's start, and the block must reach it.
        var end = text.IndexOf('\n', Math.Min(offset, text.Length));
        return end < 0 ? text.Length : end;
    }
}

/// <summary>An edit that cannot be made, with the reason in words a user can act on.</summary>
public sealed class EditException(string message) : Exception(message);

/// <summary>What an applied edit changed, so that it can be undone and redone.</summary>
public sealed record EditRecord(string Description,
                                IReadOnlyDictionary<string, string> Before,
                                IReadOnlyDictionary<string, string> After);

/// <summary>
/// Applies plans to a workspace. The gate: a file that parsed before an edit
/// must still parse after it, or nothing is changed at all. Every edit is a
/// patch over the text, so whatever it does not touch stays exactly as it was.
/// </summary>
public static class EditApplier
{
    public static EditRecord Apply(SysmlWorkspace workspace, EditPlan plan)
    {
        if (plan.Edits.Count == 0)
            throw new EditException("Nothing to change.");

        var before = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var after = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in plan.Files)
        {
            var current = workspace[path];
            var text = ApplyToText(current.Text, plan.Edits.Where(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)));
            var parsed = SourceFile.ParseText(path, text);
            if (parsed.Errors.Count > current.Errors.Count)
            {
                var error = parsed.Errors[0];
                throw new EditException(
                    $"The edit would break {Path.GetFileName(path)} at {error.Line}:{error.Column + 1} ({error.Message}); nothing was changed.");
            }

            before[path] = current.Text;
            after[path] = text;
        }

        workspace.Update(after);
        return new EditRecord(plan.Description, before, after);
    }

    /// <summary>Applies edits to one text, last first, so earlier offsets stay valid.</summary>
    public static string ApplyToText(string text, IEnumerable<TextEdit> edits)
    {
        var ordered = edits.OrderByDescending(e => e.Start).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Start + ordered[i].Length > ordered[i - 1].Start)
                throw new EditException("Two changes in the same place; nothing was changed.");
        }

        var result = new System.Text.StringBuilder(text);
        foreach (var edit in ordered)
        {
            result.Remove(edit.Start, edit.Length);
            result.Insert(edit.Start, edit.Text);
        }

        return result.ToString();
    }
}

/// <summary>Undo and redo over applied edits, by restoring whole file texts.</summary>
public sealed class EditHistory
{
    private readonly Stack<EditRecord> _undo = new();
    private readonly Stack<EditRecord> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => _undo.TryPeek(out var r) ? r.Description : null;
    public string? RedoDescription => _redo.TryPeek(out var r) ? r.Description : null;

    public void Record(EditRecord record)
    {
        _undo.Push(record);
        _redo.Clear();
    }

    public EditRecord? Undo(SysmlWorkspace workspace)
    {
        if (!_undo.TryPop(out var record))
            return null;
        workspace.Update(record.Before);
        _redo.Push(record);
        return record;
    }

    public EditRecord? Redo(SysmlWorkspace workspace)
    {
        if (!_redo.TryPop(out var record))
            return null;
        workspace.Update(record.After);
        _undo.Push(record);
        return record;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
