using SysmlStudio.Model;

namespace SysmlStudio.App.ViewModels;

/// <summary>One suggestion: the text it inserts, what it is, and how far up the list it goes.</summary>
public sealed record CompletionItem(string Text, string Detail, int Priority);

/// <summary>What to offer at a caret: where the word being typed starts, what is typed of it, and the suggestions.</summary>
public sealed record CompletionRequest(int Start, string Prefix, IReadOnlyList<CompletionItem> Items)
{
    /// <summary>Whether anything offered starts with what is typed, so a list that pops up by itself is not empty.</summary>
    public bool Matches => Items.Any(i => i.Text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// What the source editor suggests while typing. After <c>Owner::</c> it
/// offers what the owner holds. After a typing or specialization
/// (<c>:</c>, <c>:&gt;</c>, <c>:&gt;&gt;</c>, <c>defined by</c>, <c>specializes</c>, …)
/// it offers the model's definitions first. Anywhere else it offers the
/// grammar's keywords and the names the model declares. Nothing is offered
/// inside a comment or a string.
/// </summary>
public static class SourceCompletion
{
    private static readonly string[] TypingWords = ["specializes", "subsets", "redefines", "references", "conjugates"];

    public static CompletionRequest? At(string text, int caret, SysmlWorkspace? workspace, IReadOnlyList<string> keywords)
    {
        if (caret < 0 || caret > text.Length || InCommentOrString(text, caret))
            return null;

        var start = caret;
        while (start > 0 && IsWordChar(text[start - 1]))
            start--;
        var prefix = text[start..caret];
        if (prefix.Length > 0 && char.IsDigit(prefix[0]))
            return null;

        if (Qualifier(text, start) is { } qualifier)
        {
            if (Resolve(workspace, qualifier) is not { } owner)
                return null;

            var members = owner.Children
                .Where(c => c.Name is not null)
                .GroupBy(c => c.Name!, StringComparer.Ordinal)
                .Select(g => new CompletionItem(Insertable(g.Key), g.First().Kind, g.First().IsDefinition ? 1 : 0))
                .ToList();
            return members.Count == 0 ? null : new CompletionRequest(start, prefix, members);
        }

        var typing = AfterTyping(text, start);
        var items = new List<CompletionItem>();
        var named = workspace?.Elements
            .Where(e => e.Name is not null && (!typing || e.IsDefinition))
            .GroupBy(e => e.Name!, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(e => e.IsDefinition).First()) ?? [];
        foreach (var element in named)
            items.Add(new CompletionItem(Insertable(element.Name!), element.Kind, element.IsDefinition ? 1 : 0));

        if (!typing)
        {
            var taken = items.Select(i => i.Text).ToHashSet(StringComparer.Ordinal);
            items.AddRange(keywords.Where(k => !taken.Contains(k)).Select(k => new CompletionItem(k, "keyword", 0)));
        }

        items.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Text, b.Text));
        return items.Count == 0 ? null : new CompletionRequest(start, prefix, items);
    }

    /// <summary>Whether the caret sits in a comment or a string, reading the text from its start.</summary>
    public static bool InCommentOrString(string text, int caret)
    {
        var i = 0;
        while (i < caret)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                var end = text.IndexOf('\n', i);
                if (end < 0 || end >= caret)
                    return true;
                i = end + 1;
            }
            else if (c == '/' && next == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0 || end + 2 > caret)
                    return true;
                i = end + 2;
            }
            else if (c is '"' or '\'')
            {
                var end = i + 1;
                while (end < text.Length && text[end] != c && text[end] != '\n')
                    end += text[end] == '\\' ? 2 : 1;
                if (end >= caret)
                    return true;
                i = end + 1;
            }
            else
            {
                i++;
            }
        }

        return false;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>A name as it has to be written: bare when it is a plain word, quoted otherwise.</summary>
    private static string Insertable(string name)
        => name.Length > 0 && !char.IsDigit(name[0]) && name.All(IsWordChar) ? name : $"'{name}'";

    /// <summary>"A::B" when the word at <paramref name="start"/> follows "A::B::"; null otherwise.</summary>
    private static string? Qualifier(string text, int start)
    {
        if (start < 2 || text[start - 1] != ':' || text[start - 2] != ':')
            return null;

        var end = start - 2;
        var begin = end;
        while (begin > 0 && (IsWordChar(text[begin - 1]) || text[begin - 1] == ':'))
            begin--;
        var qualifier = text[begin..end].Trim(':');
        return qualifier.Length == 0 ? null : qualifier;
    }

    /// <summary>The element a written qualifier names: by its full name, or by the end of it.</summary>
    private static Element? Resolve(SysmlWorkspace? workspace, string qualifier)
    {
        if (workspace is null)
            return null;

        return workspace.Find(qualifier)
               ?? workspace.Elements.FirstOrDefault(e => e.QualifiedName.EndsWith("::" + qualifier, StringComparison.Ordinal))
               ?? workspace.Elements.FirstOrDefault(e => e.Name == qualifier);
    }

    /// <summary>Whether the word at <paramref name="start"/> names a type: after ":", ":>", ":>>", "defined by", "specializes" and the like.</summary>
    private static bool AfterTyping(string text, int start)
    {
        var i = start;
        while (i > 0 && text[i - 1] is ' ' or '\t')
            i--;

        if (i > 0 && text[i - 1] == '>')
        {
            var arrow = i - 1;
            while (arrow > 0 && text[arrow - 1] == '>')
                arrow--;
            return arrow > 0 && text[arrow - 1] == ':';
        }

        if (i > 0 && text[i - 1] == ':')
            return i < 2 || text[i - 2] != ':';

        var wordEnd = i;
        while (i > 0 && IsWordChar(text[i - 1]))
            i--;
        var word = text[i..wordEnd];
        if (TypingWords.Contains(word, StringComparer.Ordinal))
            return true;
        if (word != "by")
            return false;

        while (i > 0 && text[i - 1] is ' ' or '\t')
            i--;
        var before = i;
        while (i > 0 && IsWordChar(text[i - 1]))
            i--;
        return text[i..before] is "defined" or "typed";
    }
}
