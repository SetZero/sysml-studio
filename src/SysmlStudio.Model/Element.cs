using Antlr4.Runtime;
using SysmlStudio.Syntax;

namespace SysmlStudio.Model;

/// <summary>
/// A stretch of a file's text by character offset, both ends included, as
/// ANTLR reports tokens. Edits are patches over these.
/// </summary>
public readonly record struct TextSpan(int Start, int Stop)
{
    public int Length => Stop - Start + 1;
}

/// <summary>What kind of edge one element draws to another.</summary>
public enum RelationKind
{
    /// <summary>":&gt;" / "specializes" / "subsets" between definitions or usages.</summary>
    Specialization,

    /// <summary>":" / "defined by": a usage typed by a definition.</summary>
    Typing,

    /// <summary>":&gt;&gt;" / "redefines".</summary>
    Redefinition,

    /// <summary>A part nested in another part or definition.</summary>
    Composition,

    /// <summary>"connect a to b".</summary>
    Connect,

    /// <summary>"interface a to b".</summary>
    Interface,

    /// <summary>"flow a to b".</summary>
    Flow,

    /// <summary>"allocate a to b".</summary>
    Allocate,

    /// <summary>"satisfy R by x".</summary>
    Satisfy,

    /// <summary>"verify R".</summary>
    Verify,

    /// <summary>"first a then b" / "succession".</summary>
    Succession,

    /// <summary>"transition first s then t".</summary>
    Transition,

    /// <summary>"dependency a to b".</summary>
    Dependency,

    /// <summary>"import P::*".</summary>
    Import,
}

/// <summary>
/// One edge of the model. <see cref="TargetReference"/> is the text as written;
/// <see cref="Target"/> is what the name resolver made of it, or null when the
/// reference points outside the loaded model.
/// </summary>
public sealed class Relation(RelationKind kind, Element source, string targetReference, string? label = null,
                             string? originReference = null)
{
    public RelationKind Kind { get; } = kind;

    /// <summary>The element that declares the relation: the part, the "dependency", the "satisfy".</summary>
    public Element Source { get; } = source;

    public string TargetReference { get; } = targetReference;
    public string? Label { get; } = label;
    public Element? Target { get; internal set; }

    /// <summary>
    /// The other end, for relations written as their own statement: the client
    /// of "dependency from a to b", the subject of "satisfy R by x", the first
    /// end of "connect a to b". Null where the declaring element is the end.
    /// </summary>
    public string? OriginReference { get; } = originReference;

    public Element? Origin { get; internal set; }

    /// <summary>Where the target reference is written, for rename; null where nothing is written.</summary>
    public TextSpan? TargetSpan { get; init; }

    /// <summary>Where the origin reference is written, for rename.</summary>
    public TextSpan? OriginSpan { get; init; }

    /// <summary>Where the arrow starts: the resolved origin, or else the element that declares it.</summary>
    public Element? From => OriginReference is null ? Source : Origin;

    public override string ToString() => $"{Source.QualifiedName} -{Kind}-> {TargetReference}";
}

/// <summary>
/// One declared element: a package, a definition, a usage or a relationship.
/// It knows where it came from because every edit
/// is a patch over that interval, not a re-print of a model.
/// </summary>
public sealed class Element
{
    private readonly List<Element> _children = [];
    private readonly List<Relation> _relations = [];

    internal Element(string kind, SourceFile file, ParserRuleContext context)
    {
        Kind = kind;
        File = file;
        Context = context;
    }

    /// <summary>As the notation writes it: "part def", "part", "requirement", "satisfy".</summary>
    public string Kind { get; }

    public string? Name { get; internal set; }

    /// <summary>The name in angle brackets: &lt;'G.4'&gt;.</summary>
    public string? ShortName { get; internal set; }

    /// <summary>The doc comment's text, comment markers stripped, one paragraph per blank line.</summary>
    public string? Documentation { get; internal set; }

    /// <summary>Prefix keywords such as "#implemented" and bodies such as "@stage { number = 3; }".</summary>
    public List<string> Metadata { get; } = [];

    /// <summary>Where the declared name is written, for rename.</summary>
    public TextSpan? NameSpan { get; internal set; }

    /// <summary>Where the first doc comment's comment token is written.</summary>
    public TextSpan? DocSpan { get; internal set; }

    /// <summary>Where each "#keyword" prefix is written, "#" included.</summary>
    public List<(string Keyword, TextSpan Span)> PrefixSpans { get; } = [];

    /// <summary>The whole declaration, first character to last.</summary>
    public TextSpan Span => new(Context.Start.StartIndex, Math.Max(Context.Start.StartIndex, (Context.Stop ?? Context.Start).StopIndex));

    /// <summary>The multiplicity as written, e.g. "[1..*]", or null.</summary>
    public string? Multiplicity { get; internal set; }

    /// <summary>The value after "=" or the requirement text, as written.</summary>
    public string? Value { get; internal set; }

    public Element? Parent { get; internal set; }
    public IReadOnlyList<Element> Children => _children;
    public IReadOnlyList<Relation> Relations => _relations;

    public SourceFile File { get; }
    public ParserRuleContext Context { get; }

    public int Line => Context.Start.Line;
    public int StartTokenIndex => Context.Start.TokenIndex;
    /// <summary>
    /// The last token. An empty rule (an anonymous "entry;") has its stop before
    /// its start in ANTLR's terms; such an element is one token wide here.
    /// </summary>
    public int StopTokenIndex => Math.Max(StartTokenIndex, (Context.Stop ?? Context.Start).TokenIndex);

    /// <summary>"::"-joined names of the named elements from the root down to this one.</summary>
    public string QualifiedName
    {
        get
        {
            var names = new List<string>();
            for (var e = this; e is not null; e = e.Parent)
            {
                if (e.Name is { Length: > 0 } name)
                    names.Insert(0, name);
            }

            return string.Join("::", names);
        }
    }

    /// <summary>What the browser and a diagram node show.</summary>
    public string DisplayName => Name ?? ShortName ?? Kind;

    /// <summary>The maturity keyword this element carries, if any: "implemented", "planned", …</summary>
    public string? Maturity => Metadata.FirstOrDefault(m => m.StartsWith('#'))?.TrimStart('#');

    public bool IsDefinition => Kind.EndsWith(" def", StringComparison.Ordinal);

    internal void Add(Element child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    internal void Add(Relation relation) => _relations.Add(relation);

    public IEnumerable<Element> Descendants()
    {
        foreach (var child in _children)
        {
            yield return child;
            foreach (var d in child.Descendants())
                yield return d;
        }
    }

    public override string ToString() => $"{Kind} {DisplayName}";
}
