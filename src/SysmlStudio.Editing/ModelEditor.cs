using System.Text;
using System.Text.RegularExpressions;
using SysmlStudio.Model;
using SysmlStudio.Syntax;

namespace SysmlStudio.Editing;

/// <summary>The relations a user can draw between two elements.</summary>
public enum NewRelation
{
    Connect,
    Flow,
    Succession,
    Transition,
    Satisfy,
    Dependency,
    Allocate,
    Specialization,
    Composition,
}

/// <summary>
/// The edit operations. Each one looks at where things are written and
/// returns a plan of text patches; nothing is changed until the plan is
/// applied, and the applier refuses a plan that would break a file.
/// </summary>
public static partial class ModelEditor
{
    private const string Indent = "    ";

    // ----- rename -------------------------------------------------------------

    /// <summary>
    /// Renames an element: its declaration and every reference that resolves to
    /// it, in every file. References are rewritten in place, one name segment at
    /// a time, so "Kernel::mm" stays qualified and "ferrix.kernel" stays a chain.
    /// </summary>
    public static EditPlan Rename(SysmlWorkspace workspace, Element element, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0)
            throw new EditException("A name cannot be empty.");
        if (element.Name is not { } oldName || element.NameSpan is not { } nameSpan || element.File is null)
            throw new EditException($"This {element.Kind} has no name to rename.");
        if (newName == oldName)
            throw new EditException("That is already its name.");
        if (element.Parent?.Children.Any(c => !ReferenceEquals(c, element) && c.Name == newName) == true)
            throw new EditException($"{element.Parent.DisplayName} already has an element called {newName}.");

        var written = NameText(newName);
        var edits = new List<TextEdit> { new(element.File.Path, nameSpan.Start, nameSpan.Length, written) };

        foreach (var relation in workspace.Elements.SelectMany(e => e.Relations))
        {
            if (relation.Source.File is not { } file)
                continue;

            // Imports name packages by path; follow a renamed namespace into them.
            if (relation.Kind == RelationKind.Import)
            {
                if (relation.TargetSpan is { } import
                    && ImportPath(relation.TargetReference).StartsWith(element.QualifiedName, StringComparison.Ordinal))
                {
                    AddSegmentEdit(edits, file, import, oldName, written, first: true);
                }

                continue;
            }

            foreach (var span in new[] { relation.TargetSpan, relation.OriginSpan }.OfType<TextSpan>())
                AddReferenceEdits(workspace, edits, relation.Source, file, span, element, oldName, written);
        }

        var files = edits.Select(e => e.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var references = edits.Count - 1;
        return new EditPlan($"Rename {oldName} to {newName} — {references} reference{(references == 1 ? "" : "s")} in {files} file{(files == 1 ? "" : "s")}", Dedupe(edits));
    }

    private static string ImportPath(string reference)
        => reference.Trim().Replace("::**", string.Empty, StringComparison.Ordinal).Replace("::*", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Rewrites every segment of a written reference that names the element:
    /// the segment whose prefix — "Vehicle::engine" in "Vehicle::engine.shaft" —
    /// resolves to it. A chain through a renamed part is followed as well as a
    /// reference that ends at it.
    /// </summary>
    private static void AddReferenceEdits(SysmlWorkspace workspace, List<TextEdit> edits, Element scope, SourceFile file,
                                          TextSpan span, Element element, string oldName, string newText)
    {
        var text = file.Text.Substring(span.Start, span.Length);
        foreach (var (segment, offset) in Segments(text))
        {
            if (Unquote(segment) != oldName)
                continue;

            var prefix = text[..(offset + segment.Length)];
            if (ReferenceEquals(workspace.ResolveReference(prefix, scope), element))
                edits.Add(new TextEdit(file.Path, span.Start + offset, segment.Length, newText));
        }
    }

    /// <summary>Replaces the named segment inside a written reference.</summary>
    private static void AddSegmentEdit(List<TextEdit> edits, SourceFile file, TextSpan span, string oldName, string newText, bool first = false)
    {
        var text = file.Text.Substring(span.Start, span.Length);
        var segments = Segments(text).Where(s => Unquote(s.Text) == oldName).ToList();
        if (segments.Count == 0)
            return;

        var (Text, Offset) = first ? segments[0] : segments[^1];
        edits.Add(new TextEdit(file.Path, span.Start + Offset, Text.Length, newText));
    }

    /// <summary>The name segments of a reference, split at "::" and ".", with their offsets.</summary>
    private static IEnumerable<(string Text, int Offset)> Segments(string reference)
    {
        foreach (Match match in SegmentPattern().Matches(reference))
            yield return (match.Value, match.Index);
    }

    [GeneratedRegex(@"'(?:[^'\\]|\\.)*'|[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex SegmentPattern();

    // ----- delete -------------------------------------------------------------

    /// <summary>Removes an element's declaration, and its line when nothing else is on it.</summary>
    public static EditPlan Delete(SysmlWorkspace workspace, Element element)
    {
        if (element.File is not { } file || element.Parent is null)
            throw new EditException("Only declared elements can be deleted.");

        var text = file.Text;
        var start = element.Span.Start;
        var stop = element.Span.Stop + 1;

        var lineStart = LineStart(text, start);
        if (string.IsNullOrWhiteSpace(text[lineStart..start]))
        {
            var lineEnd = text.IndexOf('\n', stop);
            if (lineEnd < 0)
                lineEnd = text.Length;
            if (string.IsNullOrWhiteSpace(text[stop..lineEnd]))
            {
                start = lineStart;
                stop = Math.Min(text.Length, lineEnd + 1);
            }
        }

        var usages = workspace.Usages(element).Count();
        var warning = usages switch
        {
            0 => string.Empty,
            1 => " — 1 reference will no longer resolve",
            _ => $" — {usages} references will no longer resolve",
        };
        return new EditPlan($"Delete {element.Kind} {element.DisplayName}{warning}", [new TextEdit(file.Path, start, stop - start, string.Empty)]);
    }

    // ----- add ----------------------------------------------------------------

    /// <summary>The kinds of element that can be added inside <paramref name="parent"/>, in menu order.</summary>
    public static IReadOnlyList<string> ChildKinds(Element parent) => parent.Kind switch
    {
        "package" or "library package" => ["part def", "part", "port def", "item def", "attribute def", "action def", "state def", "requirement def", "requirement", "enum def", "package"],
        "part def" or "part" or "item def" or "item" => ["part", "port", "attribute", "item", "ref", "action", "state"],
        "port def" or "port" => ["attribute", "item", "port"],
        "action def" or "action" => ["action", "attribute"],
        "state def" or "state" => ["state"],
        "requirement def" or "requirement" => ["requirement", "attribute"],
        "attribute def" or "attribute" => ["attribute"],
        "enumeration def" => ["enum"],
        _ => [],
    };

    /// <summary>Whether a kind takes a type: "part x : Engine".</summary>
    public static bool IsTyped(string kind) => kind is "part" or "port" or "attribute" or "item" or "ref" or "requirement";

    /// <summary>Adds a new element at the end of <paramref name="parent"/>'s body.</summary>
    public static EditPlan AddChild(Element parent, string kind, string name, string? typeReference = null)
    {
        name = name.Trim();
        if (name.Length == 0)
            throw new EditException("A new element needs a name.");
        if (parent.Children.Any(c => c.Name == name))
            throw new EditException($"{parent.DisplayName} already has an element called {name}.");

        var keyword = kind switch
        {
            "enum def" => "enum def",
            "enum" => "enum",
            _ => kind,
        };
        var type = string.IsNullOrWhiteSpace(typeReference) || !IsTyped(kind) ? string.Empty : $" : {typeReference.Trim()}";
        var declaration = kind == "enum" ? $"enum {NameText(name)};" : $"{keyword} {NameText(name)}{type};";

        return new EditPlan($"Add {kind} {name} to {parent.DisplayName}", [InsertInBody(parent, declaration)]);
    }

    /// <summary>
    /// A declaration appended to an element's body, indented like its
    /// siblings. A body written as ";" becomes a braced one.
    /// </summary>
    private static TextEdit InsertInBody(Element parent, string declaration, bool atStart = false)
    {
        if (parent.File is not { } file)
            throw new EditException("Choose a package or a definition to add to.");

        var text = file.Text;
        var stop = parent.Span.Stop;
        var parentIndent = IndentOf(text, parent.Span.Start);
        var childIndent = parent.Children.FirstOrDefault(c => c.File is not null) is { } sibling
            ? IndentOf(text, sibling.Span.Start)
            : parentIndent + Indent;

        if (text[stop] == ';')
            return new TextEdit(file.Path, stop, 1, $" {{\n{childIndent}{declaration}\n{parentIndent}}}");

        if (text[stop] != '}')
            throw new EditException($"Cannot find the body of {parent.DisplayName}.");

        if (atStart && BodyOpen(text, parent) is { } open)
            return new TextEdit(file.Path, open + 1, 0, $"\n{childIndent}{declaration}");

        var closeLine = LineStart(text, stop);
        return string.IsNullOrWhiteSpace(text[closeLine..stop])
            ? new TextEdit(file.Path, closeLine, 0, $"{childIndent}{declaration}\n")
            : new TextEdit(file.Path, stop, 0, $"\n{childIndent}{declaration}\n{parentIndent}");
    }

    /// <summary>The body's opening brace: the first "{" after the name, outside comments and strings.</summary>
    private static int? BodyOpen(string text, Element element)
    {
        var from = (element.NameSpan ?? element.Span).Stop + 1;
        var inComment = false;
        for (var i = from; i < element.Span.Stop; i++)
        {
            if (inComment)
            {
                if (text[i] == '*' && text[i + 1] == '/')
                    inComment = false;
                continue;
            }

            if (text[i] == '/' && text[i + 1] == '*')
                inComment = true;
            else if (text[i] == '{')
                return i;
        }

        return null;
    }

    // ----- type, specialization, maturity, doc ----------------------------------

    /// <summary>Sets what a usage is typed by: replaces the type, or adds " : Type" after the name.</summary>
    public static EditPlan SetTyping(Element usage, string typeReference)
    {
        typeReference = typeReference.Trim();
        if (typeReference.Length == 0)
            throw new EditException("Name a type.");
        if (usage.File is not { } file)
            throw new EditException("Only declared elements can be typed.");

        if (usage.Relations.FirstOrDefault(r => r.Kind == RelationKind.Typing && r.TargetSpan is not null) is { TargetSpan: { } span })
            return new EditPlan($"Type {usage.DisplayName} by {typeReference}", [new TextEdit(file.Path, span.Start, span.Length, typeReference)]);

        if (usage.NameSpan is not { } name)
            throw new EditException("Only a named element can be typed.");
        return new EditPlan($"Type {usage.DisplayName} by {typeReference}", [new TextEdit(file.Path, name.Stop + 1, 0, $" : {typeReference}")]);
    }

    /// <summary>Adds a supertype: ", Super" after the existing ones, or " :> Super" after the name.</summary>
    public static EditPlan AddSpecialization(Element element, string superReference)
    {
        superReference = superReference.Trim();
        if (superReference.Length == 0)
            throw new EditException("Name what it specializes.");
        if (element.File is not { } file || element.NameSpan is not { } name)
            throw new EditException("Only a named element can specialize something.");

        var existing = element.Relations.Where(r => r.Kind == RelationKind.Specialization && r.TargetSpan is not null)
            .Select(r => r.TargetSpan!.Value).OrderBy(s => s.Start).LastOrDefault();
        var edit = existing.Length > 0 && existing.Start > 0
            ? new TextEdit(file.Path, existing.Stop + 1, 0, $", {superReference}")
            : new TextEdit(file.Path, name.Stop + 1, 0, $" :> {superReference}");
        return new EditPlan($"{element.DisplayName} specializes {superReference}", [edit]);
    }

    /// <summary>
    /// Sets the element's maturity keyword: replaces the "#keyword" among
    /// <paramref name="maturityKeywords"/> it carries, adds one in front of the
    /// declaration, or removes it when <paramref name="keyword"/> is null.
    /// </summary>
    public static EditPlan SetMaturity(Element element, string? keyword, IReadOnlyCollection<string> maturityKeywords)
    {
        if (element.File is not { } file)
            throw new EditException("Only declared elements carry a maturity.");

        var current = element.PrefixSpans.FirstOrDefault(p => maturityKeywords.Contains(p.Keyword));
        if (current.Keyword is not null)
        {
            if (keyword is null)
            {
                var text = file.Text;
                var stop = current.Span.Stop + 1;
                while (stop < text.Length && text[stop] == ' ')
                    stop++;
                return new EditPlan($"Remove #{current.Keyword} from {element.DisplayName}", [new TextEdit(file.Path, current.Span.Start, stop - current.Span.Start, string.Empty)]);
            }

            return new EditPlan($"Mark {element.DisplayName} #{keyword}", [new TextEdit(file.Path, current.Span.Start, current.Span.Length, "#" + keyword)]);
        }

        if (keyword is null)
            throw new EditException($"{element.DisplayName} carries no maturity keyword.");
        return new EditPlan($"Mark {element.DisplayName} #{keyword}", [new TextEdit(file.Path, element.Span.Start, 0, $"#{keyword} ")]);
    }

    /// <summary>Sets the doc comment: replaces the first one, or adds one at the top of the body.</summary>
    public static EditPlan SetDocumentation(Element element, string documentation)
    {
        if (element.File is not { } file)
            throw new EditException("Only declared elements have doc comments.");

        var indent = IndentOf(file.Text, element.Span.Start) + Indent;
        var comment = Comment(documentation, indent);
        if (element.DocSpan is { } span)
            return new EditPlan($"Edit the doc comment of {element.DisplayName}", [new TextEdit(file.Path, span.Start, span.Length, comment)]);

        return new EditPlan($"Add a doc comment to {element.DisplayName}", [InsertInBody(element, $"doc {comment}", atStart: true)]);
    }

    /// <summary>A block comment, wrapped at 72 columns, continuation lines starting " * ".</summary>
    private static string Comment(string text, string indent)
    {
        var words = text.Replace("*/", "* /", StringComparison.Ordinal).Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (var word in words)
        {
            if (line.Length > 0 && line.Length + word.Length + 1 > 66)
            {
                lines.Add(line.ToString());
                line.Clear();
            }

            if (line.Length > 0)
                line.Append(' ');
            line.Append(word);
        }

        if (line.Length > 0)
            lines.Add(line.ToString());
        if (lines.Count <= 1)
            return $"/* {(lines.Count == 0 ? string.Empty : lines[0])} */";

        var result = new StringBuilder("/* ").Append(lines[0]);
        foreach (var next in lines.Skip(1))
            result.Append('\n').Append(indent).Append("    * ").Append(next);
        return result.Append(" */").ToString();
    }

    // ----- relations ------------------------------------------------------------

    /// <summary>
    /// Draws a relation from <paramref name="from"/> to <paramref name="to"/>,
    /// written where SysML puts it: a connect or succession inside the diagram's
    /// owner, a satisfy or dependency in the package, a specialization on the
    /// element, a composition as a new part.
    /// </summary>
    public static EditPlan AddRelation(NewRelation kind, Element from, Element to, Element diagramRoot)
    {
        if (ReferenceEquals(from, to))
            throw new EditException("A relation needs two different elements.");

        switch (kind)
        {
            case NewRelation.Specialization:
                return AddSpecialization(from, Reference(to, from));

            case NewRelation.Composition:
                {
                    var name = UniqueName(from, LowerFirst(to.Name ?? "part"));
                    return AddChild(from, "part", name, Reference(to, from)) with
                    {
                        Description = $"{from.DisplayName} is made of {to.DisplayName}",
                    };
                }

            case NewRelation.Connect:
            case NewRelation.Flow:
            case NewRelation.Succession:
            case NewRelation.Transition:
                {
                    var a = Reference(from, diagramRoot);
                    var b = Reference(to, diagramRoot);
                    var statement = kind switch
                    {
                        NewRelation.Connect => $"connect {a} to {b};",
                        NewRelation.Flow => $"flow from {a} to {b};",
                        NewRelation.Succession => $"first {a} then {b};",
                        _ => $"transition first {a} then {b};",
                    };
                    return new EditPlan($"{kind} {from.DisplayName} → {to.DisplayName}", [InsertInBody(diagramRoot, statement)]);
                }

            case NewRelation.Satisfy:
                {
                    var package = PackageOf(to);
                    return new EditPlan($"{from.DisplayName} satisfies {to.DisplayName}",
                        [InsertInBody(package, $"satisfy {Reference(to, package)} by {Reference(from, package)};")]);
                }

            case NewRelation.Dependency:
                {
                    var package = PackageOf(from);
                    return new EditPlan($"{from.DisplayName} depends on {to.DisplayName}",
                        [InsertInBody(package, $"dependency from {Reference(from, package)} to {Reference(to, package)};")]);
                }

            case NewRelation.Allocate:
                {
                    var package = PackageOf(from);
                    return new EditPlan($"{from.DisplayName} allocated to {to.DisplayName}",
                        [InsertInBody(package, $"allocate {Reference(from, package)} to {Reference(to, package)};")]);
                }

            default:
                throw new EditException($"{kind} cannot be drawn yet.");
        }
    }

    /// <summary>
    /// How to name <paramref name="target"/> from inside <paramref name="scope"/>:
    /// its bare name when it is declared in the scope or around it, its
    /// qualified name otherwise, which resolves from anywhere.
    /// </summary>
    public static string Reference(Element target, Element scope)
    {
        for (var s = scope; s is not null; s = s.Parent)
        {
            if (ReferenceEquals(target.Parent, s) && target.Name is { } name)
                return NameText(name);
        }

        var segments = new List<string>();
        for (var e = target; e is not null; e = e.Parent)
        {
            if (e.Name is { } segment)
                segments.Insert(0, NameText(segment));
        }

        return string.Join("::", segments);
    }

    private static Element PackageOf(Element element)
    {
        for (var e = element; e is not null; e = e.Parent)
        {
            if (e.Kind is "package" or "library package" && e.File is not null)
                return e;
        }

        throw new EditException($"{element.DisplayName} is not inside a package.");
    }

    private static string UniqueName(Element parent, string wanted)
    {
        var taken = parent.Children.Select(c => c.Name).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var name = wanted;
        var suffix = 2;
        while (taken.Contains(name))
            name = wanted + suffix++;
        return name;
    }

    private static string LowerFirst(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    // ----- text helpers ---------------------------------------------------------

    /// <summary>A name as SysML writes it: bare when it is a plain identifier and no keyword, quoted otherwise.</summary>
    public static string NameText(string name)
        => IdentifierPattern().IsMatch(name) && !Keywords.Contains(name)
            ? name
            : "'" + name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex IdentifierPattern();

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "part", "def", "port", "attribute", "item", "ref", "action", "state", "requirement", "package", "import",
        "private", "public", "protected", "connect", "to", "from", "first", "then", "transition", "satisfy", "by",
        "verify", "dependency", "allocate", "flow", "doc", "comment", "abstract", "variation", "variant", "enum",
        "in", "out", "inout", "end", "true", "false", "null", "and", "or", "not", "xor", "all", "specializes",
        "subsets", "redefines", "references", "if", "else", "entry", "exit", "do", "accept", "send", "via",
    };

    private static string Unquote(string text)
        => text.Length >= 2 && text[0] == '\'' && text[^1] == '\'' ? text[1..^1] : text;

    private static int LineStart(string text, int offset)
    {
        var index = offset > 0 ? text.LastIndexOf('\n', offset - 1) : -1;
        return index + 1;
    }

    private static string IndentOf(string text, int offset)
    {
        var start = LineStart(text, offset);
        var end = start;
        while (end < text.Length && text[end] is ' ' or '\t')
            end++;
        return text[start..end];
    }

    private static List<TextEdit> Dedupe(List<TextEdit> edits)
        => [.. edits.DistinctBy(e => (e.Path.ToUpperInvariant(), e.Start))];
}
