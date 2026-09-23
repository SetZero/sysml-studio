using SysmlStudio.Syntax;

namespace SysmlStudio.Model;

/// <summary>How much a diagnostic matters.</summary>
public enum Severity
{
    Error,
    Warning,
}

/// <summary>One problem in the model, placed where the text says it is.</summary>
public sealed record Diagnostic(Severity Severity, string File, int Line, int Column, string Message);

/// <summary>
/// A folder of .sysml files read as one model. The files stay the source of
/// truth: the workspace holds their text and parse trees, and the element tree
/// is an index over them that is thrown away and rebuilt whenever a file changes.
/// </summary>
public sealed class SysmlWorkspace
{
    private readonly Dictionary<string, SourceFile> _files = new(StringComparer.OrdinalIgnoreCase);

    private SysmlWorkspace(string directory)
    {
        Directory = directory;
        Root = new Element("model", null!, null!);
    }

    public string Directory { get; }

    /// <summary>The synthetic root every file's top-level elements hang under.</summary>
    public Element Root { get; private set; }

    public IReadOnlyCollection<SourceFile> Files => _files.Values;

    public IReadOnlyList<SyntaxError> Errors { get; private set; } = [];

    /// <summary>Every reference that did not resolve, as a message per relation.</summary>
    public IReadOnlyList<string> UnresolvedReferences { get; private set; } = [];

    /// <summary>Syntax errors and unresolved references together, in file order.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; private set; } = [];

    public static SysmlWorkspace Load(string directory, string pattern = "*.sysml")
    {
        var workspace = new SysmlWorkspace(directory);
        foreach (var path in System.IO.Directory.GetFiles(directory, pattern, SearchOption.AllDirectories).Order())
            workspace._files[path] = SourceFile.Parse(path);
        workspace.Reindex();
        return workspace;
    }

    /// <summary>Replaces one file's text — after an edit — and rebuilds the index.</summary>
    public SourceFile Update(string path, string text)
    {
        var file = SourceFile.ParseText(path, text);
        _files[path] = file;
        Reindex();
        return file;
    }

    public SourceFile this[string path] => _files[path];

    /// <summary>The file's path relative to the workspace folder, with forward slashes.</summary>
    public string RelativePath(string path)
        => Path.GetRelativePath(Directory, path).Replace('\\', '/');

    /// <summary>Rebuilds the element tree from the current parse trees.</summary>
    public void Reindex()
    {
        Root = new Element("model", null!, null!);
        foreach (var file in _files.Values.OrderBy(f => f.Path, StringComparer.Ordinal))
            ModelIndexer.Index(file, Root);

        Errors = [.. _files.Values.SelectMany(f => f.Errors)];
        Resolve();
    }

    /// <summary>All elements, roots first.</summary>
    public IEnumerable<Element> Elements => Root.Descendants();

    public Element? Find(string qualifiedName)
        => Elements.FirstOrDefault(e => e.QualifiedName == qualifiedName);

    /// <summary>Every relation in the model that points at <paramref name="element"/>.</summary>
    public IEnumerable<Relation> Usages(Element element)
        => Elements.SelectMany(e => e.Relations).Where(r => ReferenceEquals(r.Target, element));

    /// <summary>
    /// Points every relation at the element it names. Resolution is lexical:
    /// from the element outwards through enclosing scopes, then through the
    /// packages the enclosing scopes import, then by qualified name from the
    /// root. Short names ("&lt;'G.4'&gt;") resolve too, because the model cites them.
    /// </summary>
    private void Resolve()
    {
        var byQualifiedName = new Dictionary<string, Element>(StringComparer.Ordinal);
        var byShortName = new Dictionary<string, Element>(StringComparer.Ordinal);
        foreach (var element in Elements)
        {
            if (element.Name is not null)
                byQualifiedName.TryAdd(element.QualifiedName, element);
            if (element.ShortName is not null)
                byShortName.TryAdd(element.ShortName, element);
        }

        var unresolved = new List<string>();
        var diagnostics = Errors
            .Select(e => new Diagnostic(Severity.Error, e.File, e.Line, e.Column + 1, e.Message))
            .ToList();

        foreach (var element in Elements)
        {
            foreach (var relation in element.Relations)
            {
                if (relation.Kind == RelationKind.Import)
                    continue;

                relation.Target = Lookup(relation.TargetReference, element, byQualifiedName, byShortName);
                if (relation.Target is not null || MayComeFromOutside(relation.TargetReference, element, byQualifiedName))
                    continue;

                unresolved.Add($"{element.File?.Path}:{element.Line}: cannot resolve '{relation.TargetReference}'");
                diagnostics.Add(new Diagnostic(Severity.Warning, element.File?.Path ?? string.Empty,
                    element.Line, element.Context.Start.Column + 1,
                    $"unresolved reference '{relation.TargetReference}'"));
            }
        }

        UnresolvedReferences = unresolved;
        Diagnostics = [.. diagnostics
            .OrderBy(d => d.Severity)
            .ThenBy(d => d.File, StringComparer.Ordinal)
            .ThenBy(d => d.Line)];
    }

    /// <summary>
    /// Whether a scope around <paramref name="from"/> imports a package this
    /// folder does not hold — the standard library's ScalarValues, say. A name
    /// that does not resolve there most likely comes from that package, and
    /// calling it a problem would bury the real ones under hundreds of false ones.
    /// </summary>
    private static bool MayComeFromOutside(string reference, Element from, Dictionary<string, Element> byQualifiedName)
    {
        // "FerrixBoot::Missing" names a package this folder holds: no excuse.
        var head = Normalize(reference).Split("::")[0];
        if (reference.Contains("::", StringComparison.Ordinal) && byQualifiedName.ContainsKey(head))
            return false;

        for (var scope = from; scope is not null; scope = scope.Parent)
        {
            foreach (var import in scope.Relations.Where(r => r.Kind == RelationKind.Import))
            {
                var imported = import.TargetReference.Trim();
                var package = imported.Split("::")[0].Trim();
                if (package.Length > 0 && !byQualifiedName.ContainsKey(package))
                    return true;
            }
        }

        return false;
    }

    private static Element? Lookup(string reference, Element from,
                                   Dictionary<string, Element> byQualifiedName,
                                   Dictionary<string, Element> byShortName)
    {
        var name = Normalize(reference);
        if (name.Length == 0)
            return null;

        // From the element outwards: Scope::name, then the scope above it.
        for (var scope = from; scope is not null; scope = scope.Parent)
        {
            var prefix = scope.QualifiedName;
            var candidate = prefix.Length == 0 ? name : prefix + "::" + name;
            if (byQualifiedName.TryGetValue(candidate, out var hit))
                return hit;

            foreach (var import in scope.Relations.Where(r => r.Kind == RelationKind.Import))
            {
                var imported = import.TargetReference.TrimEnd();
                if (imported.EndsWith("::*", StringComparison.Ordinal))
                    imported = imported[..^3];
                if (byQualifiedName.TryGetValue(imported + "::" + name, out var viaImport))
                    return viaImport;
            }
        }

        if (byQualifiedName.TryGetValue(name, out var absolute))
            return absolute;

        // A bare name that is unique in the model, or a short name.
        var parts = name.Split("::");
        var last = parts[^1];
        if (byShortName.TryGetValue(last, out var shortHit))
            return shortHit;

        var matches = byQualifiedName.Where(kv => kv.Key.EndsWith("::" + name, StringComparison.Ordinal)).ToList();
        return matches.Count == 1 ? matches[0].Value : null;
    }

    /// <summary>Drops feature chains ("a.b"), "$::" and whitespace.</summary>
    private static string Normalize(string reference)
    {
        var name = reference.Trim();
        if (name.StartsWith("$::", StringComparison.Ordinal))
            name = name[3..];
        var dot = name.IndexOf('.');
        if (dot >= 0)
            name = name[..dot];
        return name.Trim().Trim('\'');
    }
}
