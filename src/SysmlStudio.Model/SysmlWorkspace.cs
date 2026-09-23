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

    /// <summary>Each file's text as it was last read from or written to disk.</summary>
    private readonly Dictionary<string, string> _onDisk = new(StringComparer.OrdinalIgnoreCase);

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
        {
            var file = SourceFile.Parse(path);
            workspace._files[path] = file;
            workspace._onDisk[path] = file.Text;
        }
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

    /// <summary>Replaces several files' text at once — one edit may touch many — and re-indexes once.</summary>
    public void Update(IReadOnlyDictionary<string, string> texts)
    {
        foreach (var (path, text) in texts)
            _files[path] = SourceFile.ParseText(path, text);
        Reindex();
    }

    public SourceFile this[string path] => _files[path];

    /// <summary>The files whose text differs from what is on disk.</summary>
    public IEnumerable<string> DirtyFiles
        => _files.Values.Where(f => !_onDisk.TryGetValue(f.Path, out var disk) || disk != f.Text).Select(f => f.Path);

    public bool IsDirty(string path)
        => _files.TryGetValue(path, out var file) && (!_onDisk.TryGetValue(path, out var disk) || disk != file.Text);

    /// <summary>Writes one file's text to disk as it stands.</summary>
    public void Save(string path)
    {
        var text = _files[path].Text;
        File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
        _onDisk[path] = text;
    }

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
    private Dictionary<string, Element> _byQualifiedName = new(StringComparer.Ordinal);
    private Dictionary<string, Element> _byShortName = new(StringComparer.Ordinal);

    /// <summary>What <paramref name="reference"/> names when written inside <paramref name="scope"/>.</summary>
    public Element? ResolveReference(string reference, Element scope)
        => Resolve(reference, scope, _byQualifiedName, _byShortName);

    private void Resolve()
    {
        var byQualifiedName = new Dictionary<string, Element>(StringComparer.Ordinal);
        var byShortName = new Dictionary<string, Element>(StringComparer.Ordinal);
        _byQualifiedName = byQualifiedName;
        _byShortName = byShortName;
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

        // Two passes: a feature chain ("ferrix.kernel.mm") walks through what
        // parts are typed by, so every typing has to be resolved before any
        // chain is followed to the end.
        var relations = Elements.SelectMany(e => e.Relations).Where(r => r.Kind != RelationKind.Import).ToList();
        foreach (var relation in relations)
            relation.Target = Lookup(relation.TargetReference, relation.Source, byQualifiedName, byShortName);

        foreach (var element in Elements)
        {
            foreach (var relation in element.Relations)
            {
                if (relation.Kind == RelationKind.Import)
                    continue;

                relation.Target = Resolve(relation.TargetReference, element, byQualifiedName, byShortName);
                if (relation.OriginReference is { } origin)
                    relation.Origin = Resolve(origin, element, byQualifiedName, byShortName);

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
        // Every action has a start and a done; they come from the Actions library.
        if (reference.Trim() is "start" or "done")
            return true;

        // "FerrixBoot::Missing" names a package this folder holds: no excuse.
        var head = Normalize(reference).Split("::")[0];
        if (reference.Contains("::", StringComparison.Ordinal) && byQualifiedName.ContainsKey(head))
            return false;

        var name = Normalize(reference).Split("::")[^1];
        for (var scope = from; scope is not null; scope = scope.Parent)
        {
            foreach (var import in scope.Relations.Where(r => r.Kind == RelationKind.Import))
            {
                var imported = import.TargetReference.Trim();
                var package = imported.Split("::")[0].Trim();
                if (package.Length == 0 || byQualifiedName.ContainsKey(package))
                    continue;

                // A package of the standard library excuses only the names it
                // defines; any other package the folder lacks excuses anything,
                // since there is no telling what is in it.
                if (!StandardLibrary.TryGetValue(package, out var defined) || defined.Contains(name))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What the most-imported packages of the SysML v2 standard library define,
    /// so that a missing name is not excused merely because one of them is
    /// imported. Names only; the library itself is not loaded.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> StandardLibrary = new(StringComparer.Ordinal)
    {
        ["ScalarValues"] =
        [
            "ScalarValue", "Boolean", "String", "NumericalValue", "Number", "Complex", "Real", "Rational",
            "Integer", "Natural", "Positive",
        ],
        ["Base"] = ["Anything", "DataValue", "things", "dataValues"],
        ["Actions"] = ["start", "done", "Action", "actions"],
        ["RealFunctions"] = ["abs", "sqrt", "floor", "round", "max", "min", "sum", "product"],
        ["ISQ"] =
        [
            "LengthValue", "MassValue", "DurationValue", "TimeValue", "SpeedValue", "AccelerationValue", "ForceValue",
            "EnergyValue", "PowerValue", "TemperatureValue", "ElectricCurrentValue", "VoltageValue", "FrequencyValue",
            "AngularMeasureValue", "AreaValue", "VolumeValue", "PressureValue", "TorqueValue",
        ],
    };

    /// <summary>
    /// Resolves a name, following a feature chain as far as the model goes:
    /// "ferrix.kernel.mm" is the part ferrix, then the part kernel inside what
    /// ferrix is typed by, then mm inside that. It stops at the deepest element
    /// found rather than failing, so a chain into the standard library still
    /// points at the part it starts from.
    /// </summary>
    private static Element? Resolve(string reference, Element from,
                                    Dictionary<string, Element> byQualifiedName,
                                    Dictionary<string, Element> byShortName)
    {
        var head = Lookup(reference, from, byQualifiedName, byShortName) ?? Inherited(reference, from);
        var dot = reference.IndexOf('.');
        if (head is null || dot < 0)
            return head;

        var current = head;
        var segments = reference[(dot + 1)..].Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            var next = FeatureNamed(current, segment.Trim('\''), depth: 0);
            if (next is null)
                break;
            current = next;
        }

        return current;
    }

    /// <summary>
    /// A bare name that is none of the enclosing scopes' own members may be a
    /// member they inherit: ":>> number" inside a requirement typed by Stage
    /// redefines Stage's number.
    /// </summary>
    private static Element? Inherited(string reference, Element from)
    {
        var name = Normalize(reference);
        if (name.Length == 0 || name.Contains("::", StringComparison.Ordinal))
            return null;

        for (var scope = from; scope is not null; scope = scope.Parent)
        {
            if (FeatureNamed(scope, name, depth: 0) is { } found && !ReferenceEquals(found, from))
                return found;
        }

        return null;
    }

    /// <summary>A feature by name: owned, or owned by what the element is typed by or specializes.</summary>
    private static Element? FeatureNamed(Element element, string name, int depth)
    {
        if (depth > 8)
            return null;

        var owned = element.Children.FirstOrDefault(c => c.Name == name);
        if (owned is not null)
            return owned;

        // A redefinition inherits what it redefines: "part :>> kernel { part :>> arch }".
        foreach (var relation in element.Relations.Where(r => r.Kind is RelationKind.Typing or RelationKind.Specialization or RelationKind.Redefinition))
        {
            if (relation.Target is { } type && !ReferenceEquals(type, element) && FeatureNamed(type, name, depth + 1) is { } inherited)
                return inherited;
        }

        return null;
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
