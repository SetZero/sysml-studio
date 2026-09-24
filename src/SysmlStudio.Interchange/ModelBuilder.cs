using System.Globalization;
using System.Text.RegularExpressions;
using SysML2.NET.Core.Core.Types;
using SysML2.NET.Core.POCO.Core.Classifiers;
using SysML2.NET.Core.POCO.Core.Features;
using SysML2.NET.Core.POCO.Core.Types;
using SysML2.NET.Core.POCO.Kernel.Expressions;
using SysML2.NET.Core.POCO.Kernel.FeatureValues;
using SysML2.NET.Core.POCO.Kernel.Functions;
using SysML2.NET.Core.POCO.Kernel.Multiplicities;
using SysML2.NET.Core.POCO.Root.Annotations;
using SysML2.NET.Core.POCO.Root.Dependencies;
using SysML2.NET.Core.POCO.Root.Namespaces;
using SysML2.NET.Core.POCO.Systems.Cases;
using SysML2.NET.Core.POCO.Systems.Connections;
using SysML2.NET.Core.POCO.Systems.DefinitionAndUsage;
using SysML2.NET.Core.POCO.Systems.Metadata;
using SysML2.NET.Core.POCO.Systems.Occurrences;
using SysML2.NET.Core.POCO.Systems.Requirements;
using SysML2.NET.Core.POCO.Systems.States;
using SysML2.NET.Core.POCO.Systems.VerificationCases;
using SysML2.NET.Core.POCO.Systems.Views;
using SysML2.NET.Core.Root.Namespaces;
using SysML2.NET.Core.Systems.Requirements;
using SysML2.NET.Core.Systems.States;
using SysML2.NET.Extensions;
using SysmlStudio.Model;
using ModelElement = SysmlStudio.Model.Element;
using SysmlElement = SysML2.NET.Core.POCO.Root.Elements.IElement;
using SysmlRelationship = SysML2.NET.Core.POCO.Root.Elements.IRelationship;

namespace SysmlStudio.Interchange;

/// <summary>
/// <para>
/// Turns a workspace's element tree into a SysML v2 model: SysML2.NET's POCO
/// objects, owned the way the metamodel owns them.
/// Both exports write this one tree, so the JSON and the XMI say the same thing.
/// </para>
/// <para>
/// It works in three passes. The first creates an element for every declared
/// one, with its membership, doc comment, multiplicity and literal value. The
/// second draws the edges and the metadata, now that both ends of every edge exist.
/// The third adds the imports, which name memberships the first pass made.
/// Edges whose target did not resolve are left out: the export says only what
/// the model says for certain.
/// </para>
/// </summary>
internal sealed partial class ModelBuilder
{
    private readonly SysmlWorkspace _workspace;
    private readonly StableIds _ids = new();
    private readonly Dictionary<ModelElement, SysmlElement> _elements = [];
    private readonly Dictionary<ModelElement, string> _keys = [];
    private readonly Dictionary<ModelElement, IMembership> _memberships = [];

    private ModelBuilder(SysmlWorkspace workspace) => _workspace = workspace;

    /// <summary>The whole workspace as one root namespace, every file's top-level elements in it.</summary>
    public static INamespace Build(SysmlWorkspace workspace) => new ModelBuilder(workspace).Run();

    private Namespace Run()
    {
        var root = new Namespace { Id = _ids.Next("(root)").Id };
        foreach (var child in _workspace.Root.Children)
            Declare(root, string.Empty, child);

        foreach (var element in _workspace.Elements)
            Relate(element);

        foreach (var element in _workspace.Elements)
            Import(element);

        StampElementIds(root);
        return root;
    }

    /// <summary>
    /// Sets every element's elementId to its id, as the pilot implementation
    /// does: the API names an element by elementId, the files by id.
    /// </summary>
    private static void StampElementIds(SysmlElement element)
    {
        element.ElementId = element.Id.ToString();
        foreach (var relationship in element.OwnedRelationship)
            StampElementIds(relationship);

        if (element is SysmlRelationship owning)
        {
            foreach (var owned in owning.OwnedRelatedElement)
                StampElementIds(owned);
        }
    }

    // ---- Pass 1: elements, owned by their memberships ----

    private void Declare(SysmlElement owner, string ownerKey, ModelElement element)
    {
        var segment = element.Name is { Length: > 0 } name ? name : "<" + element.Kind + ">";
        var (key, id) = _ids.Next(ownerKey.Length == 0 ? segment : ownerKey + "::" + segment);

        var created = Metaclasses.Create(element.Kind, element.IsDefinition);
        created.Id = id;
        created.DeclaredName = element.Name;
        created.DeclaredShortName = element.ShortName;

        var membership = MembershipFor(owner, element, created);
        if (membership is null)
            return; // nothing may own it here, and so neither it nor anything inside it is written

        membership.Id = NextId(key + "/membership");
        membership.Visibility = VisibilityBefore(element.File.Text, element.Span.Start, VisibilityKind.Public);
        owner.AssignOwnership(membership, created);

        if (created is IFeature feature)
        {
            feature.Direction = DirectionOf(element);

            // A part, item or action inside a definition is part of it; a
            // "ref" or an attribute only points at something.
            if (membership is IFeatureMembership && created is IOccurrenceUsage && created is not IReferenceUsage)
                feature.IsComposite = true;
        }

        _elements[element] = created;
        _keys[element] = key;
        _memberships[element] = membership;

        Document(created, key, element.Documentation);
        Bound(created, key, element.Multiplicity);
        if (created is IFeature valued && element.Kind != "transition")
            Value(valued, key, element.Value);

        foreach (var child in element.Children)
            Declare(created, key, child);
    }

    /// <summary>
    /// The membership that owns <paramref name="created"/> inside <paramref name="owner"/>:
    /// the special one its kind calls for ("subject" is a SubjectMembership),
    /// else a FeatureMembership for a feature of a type, else a plain
    /// OwningMembership. Null when not even that may own it here.
    /// </summary>
    private static IOwningMembership? MembershipFor(SysmlElement owner, ModelElement element, SysmlElement created)
    {
        IOwningMembership? special = element.Kind switch
        {
            "subject" => new SubjectMembership(),
            "actor" => new ActorMembership(),
            "stakeholder" => new StakeholderMembership(),
            "objective requirement" => new ObjectiveMembership(),
            "verify" => new RequirementVerificationMembership(),
            "framed concern" => new FramedConcernMembership(),
            "view rendering" => new ViewRenderingMembership(),
            "end" => new EndFeatureMembership(),
            "requirement constraint" => new RequirementConstraintMembership
            {
                Kind = WordBefore(element.File.Text, element.Span.Start) == "assume"
                    ? RequirementConstraintKind.Assumption
                    : RequirementConstraintKind.Requirement,
            },
            "state action" => new StateSubactionMembership
            {
                Kind = WordBefore(element.File.Text, element.Span.Start) switch
                {
                    "entry" => StateSubactionKind.Entry,
                    "exit" => StateSubactionKind.Exit,
                    _ => StateSubactionKind.Do,
                },
            },
            _ => null,
        };

        IOwningMembership?[] candidates = [special, created is IFeature ? new FeatureMembership() : null, new OwningMembership()];
        return candidates.OfType<IOwningMembership>()
            .FirstOrDefault(m => m.QueryIsValidContainmentOwner(owner) && m.QueryIsValidForContainment(created));
    }

    /// <summary>The doc comment, as a Documentation the element owns through a membership.</summary>
    private void Document(SysmlElement owner, string key, string? text)
    {
        if (string.IsNullOrEmpty(text) || owner is not INamespace)
            return;

        var documentation = new Documentation { Id = NextId(key + "/doc"), Body = text };
        Own(owner, key + "/doc", documentation);
    }

    /// <summary>"[4]", "[0..1]", "[1..*]", "[*]" as a MultiplicityRange; anything cleverer is left out.</summary>
    private void Bound(SysmlElement owner, string key, string? multiplicity)
    {
        if (multiplicity is null || owner is not IType)
            return;

        var match = MultiplicityPattern().Match(multiplicity);
        if (!match.Success)
            return;

        var range = new MultiplicityRange { Id = NextId(key + "/multiplicity") };
        Own(owner, key + "/multiplicity", range);
        if (match.Groups["lower"].Success)
            Own(range, key + "/multiplicity/lower", BoundLiteral(key + "/multiplicity/lower", match.Groups["lower"].Value));
        Own(range, key + "/multiplicity/upper", BoundLiteral(key + "/multiplicity/upper", match.Groups["upper"].Value));
    }

    private IExpression BoundLiteral(string key, string text)
        => text == "*"
            ? new LiteralInfinity { Id = NextId(key) }
            : new LiteralInteger { Id = NextId(key), Value = int.Parse(text, CultureInfo.InvariantCulture) };

    /// <summary>
    /// Prefix metadata ("#implemented") and metadata usages ("@stage { number = 3; }")
    /// as MetadataUsages typed by the metadata definition they name. Simple
    /// "name = literal;" assignments in the body become redefinitions of the
    /// definition's features with that value.
    /// </summary>
    private void Annotate(SysmlElement owner, string key, ModelElement element)
    {
        if (owner is not INamespace)
            return;

        foreach (var written in element.Metadata)
        {
            var match = MetadataPattern().Match(written);
            if (!match.Success)
                continue;

            var name = match.Groups["name"].Value;
            if (_workspace.ResolveReference(name, element) is not { } definition
                || !_elements.TryGetValue(definition, out var type) || type is not IMetadataDefinition metaclass)
            {
                continue;
            }

            var usageKey = _ids.Claim(key + "/metadata/" + name);
            var usage = new MetadataUsage { Id = StableIds.For(usageKey) };
            Own(owner, usageKey, usage);
            var typing = new FeatureTyping { Id = NextId(usageKey + "/typing"), Type = metaclass, TypedFeature = usage };
            usage.AssignOwnership(typing);

            foreach (var groups in AssignmentPattern().Matches(match.Groups["body"].Value).Select(m => m.Groups))
                Assign(usage, usageKey, definition, groups["feature"].Value, groups["value"].Value);
        }
    }

    /// <summary>"number = 3" inside a metadata body: a feature redefining the definition's "number", valued 3.</summary>
    private void Assign(MetadataUsage usage, string usageKey, ModelElement definition, string featureName, string value)
    {
        var redefined = definition.Children.FirstOrDefault(c => c.Name == featureName);
        if (redefined is null || !_elements.TryGetValue(redefined, out var target) || target is not IFeature feature
            || Literal(usageKey + "/" + featureName + "/value", value) is not { } literal)
        {
            return;
        }

        var featureKey = _ids.Claim(usageKey + "/" + featureName);
        var redefining = new ReferenceUsage { Id = StableIds.For(featureKey) };
        var membership = new FeatureMembership { Id = NextId(featureKey + "/membership"), Visibility = VisibilityKind.Public };
        usage.AssignOwnership(membership, redefining);
        redefining.AssignOwnership(new Redefinition
        {
            Id = NextId(featureKey + "/redefinition"),
            RedefiningFeature = redefining,
            RedefinedFeature = feature,
        });
        redefining.AssignOwnership(new FeatureValue { Id = NextId(featureKey + "/value") }, literal);
    }

    /// <summary>"= 3", "= 2.5", "= true", "= \"text\"" as a FeatureValue; an expression is left out.</summary>
    private void Value(IFeature feature, string key, string? value)
    {
        if (value is null || Literal(key + "/value/literal", value) is not { } literal)
            return;

        feature.AssignOwnership(new FeatureValue { Id = NextId(key + "/value") }, literal);
    }

    private IExpression? Literal(string key, string text)
    {
        text = text.Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            return new LiteralString { Id = NextId(key), Value = text[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal) };
        if (text is "true" or "false")
            return new LiteralBoolean { Id = NextId(key), Value = text == "true" };
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            return new LiteralInteger { Id = NextId(key), Value = integer };
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
            return new LiteralRational { Id = NextId(key), Value = real };
        return null;
    }

    /// <summary>Owns <paramref name="owned"/> through a new public OwningMembership.</summary>
    private void Own(SysmlElement owner, string key, SysmlElement owned)
    {
        var membership = new OwningMembership { Id = NextId(key + "/membership"), Visibility = VisibilityKind.Public };
        owner.AssignOwnership(membership, owned);
    }

    // ---- Pass 2: edges between elements ----

    private void Relate(ModelElement element)
    {
        if (!_elements.TryGetValue(element, out var source))
            return;

        var key = _keys[element];

        // Metadata names a metadata definition, which may be declared further down.
        Annotate(source, key, element);

        var ends = new List<IFeature>();
        foreach (var relation in element.Relations)
        {
            var target = Resolved(relation.Target);
            var origin = Resolved(relation.Origin);
            switch (relation.Kind)
            {
                case RelationKind.Typing:
                    Type(source, key, target);
                    break;

                case RelationKind.Specialization:
                    Specialize(source, key, target);
                    break;

                case RelationKind.Redefinition when source is IFeature redefining && target is IFeature redefined:
                    source.AssignOwnership(new Redefinition
                    {
                        Id = NextId(key + "/redefinition/" + redefined.Id),
                        RedefiningFeature = redefining,
                        RedefinedFeature = redefined,
                    });
                    break;

                case RelationKind.Satisfy or RelationKind.Verify:
                    Reference(source, key, target);
                    break;

                case RelationKind.Connect or RelationKind.Interface or RelationKind.Allocate or RelationKind.Flow:
                    AddEnds(ends, origin, target);
                    break;

                case RelationKind.Succession when element.Kind is "succession" or "succession flow":
                    AddEnds(ends, origin, target);
                    break;

                case RelationKind.Succession:
                    Succession(source, key, origin, target);
                    break;

                case RelationKind.Transition:
                    Transition(source, key, origin, target);
                    break;

                case RelationKind.Dependency when source is IDependency dependency && origin is not null && target is not null:
                    if (!dependency.Client.Contains(origin))
                        dependency.Client.Add(origin);
                    if (!dependency.Supplier.Contains(target))
                        dependency.Supplier.Add(target);
                    break;

                default:
                    // Composition is the ownership pass 1 already wrote;
                    // imports come in pass 3; the rest did not resolve.
                    break;
            }
        }

        if (source is IType connector && ends.Count >= 2)
            WriteEnds(connector, key, ends);
    }

    private SysmlElement? Resolved(ModelElement? element)
        => element is not null && _elements.TryGetValue(element, out var resolved) ? resolved : null;

    private void Type(SysmlElement source, string key, SysmlElement? target)
    {
        if (source is not IFeature typed || target is not IType type)
            return;

        source.AssignOwnership(new FeatureTyping { Id = NextId(key + "/typing/" + target.Id), Type = type, TypedFeature = typed });
    }

    /// <summary>":&gt;" between definitions is subclassification, between usages subsetting.</summary>
    private void Specialize(SysmlElement source, string key, SysmlElement? target)
    {
        var id = NextId(key + "/specialization/" + target?.Id);
        SysmlRelationship? relationship = (source, target) switch
        {
            (IClassifier specific, IClassifier general) => new Subclassification { Id = id, Subclassifier = specific, Superclassifier = general },
            (IFeature specific, IFeature general) => new Subsetting { Id = id, SubsettingFeature = specific, SubsettedFeature = general },
            (IType specific, IType general) => new Specialization { Id = id, Specific = specific, General = general },
            _ => null,
        };

        if (relationship is not null)
            source.AssignOwnership(relationship);
    }

    /// <summary>
    /// What a "satisfy" or "verify" names: a requirement usage by reference
    /// subsetting, as the notation means it, or a requirement definition by
    /// typing, which is the nearest the metamodel has for "satisfy SomeDef".
    /// </summary>
    private void Reference(SysmlElement source, string key, SysmlElement? target)
    {
        if (source is not IFeature)
            return;

        if (target is IFeature referenced)
            source.AssignOwnership(new ReferenceSubsetting { Id = NextId(key + "/reference/" + target.Id), ReferencedFeature = referenced });
        else
            Type(source, key, target);
    }

    /// <summary>Adds the ends of one "a to b" hop, skipping an end the previous hop already added.</summary>
    private static void AddEnds(List<IFeature> ends, SysmlElement? origin, SysmlElement? target)
    {
        if (origin is not IFeature from || target is not IFeature to)
            return;

        if (ends.Count == 0 || !ReferenceEquals(ends[^1], from))
            ends.Add(from);
        ends.Add(to);
    }

    /// <summary>
    /// A connector's ends, as the pilot implementation writes them: an end
    /// feature per end, owned through an EndFeatureMembership, that
    /// reference-subsets the feature it connects.
    /// </summary>
    private void WriteEnds(IType connector, string key, List<IFeature> ends)
    {
        foreach (var end in ends)
        {
            var endKey = _ids.Claim(key + "/end/" + end.Id);
            var feature = new ReferenceUsage { Id = StableIds.For(endKey), IsEnd = true };
            connector.AssignOwnership(new EndFeatureMembership { Id = NextId(endKey + "/membership"), Visibility = VisibilityKind.Public }, feature);
            feature.AssignOwnership(new ReferenceSubsetting { Id = NextId(endKey + "/reference"), ReferencedFeature = end });
        }
    }

    /// <summary>"first a then b" written as shorthand in an action body: a succession the action owns.</summary>
    private void Succession(SysmlElement owner, string key, SysmlElement? origin, SysmlElement? target)
    {
        if (owner is not IType || origin is not IFeature from || target is not IFeature to)
            return;

        var successionKey = _ids.Claim(key + "/succession/" + origin.Id + "/" + target.Id);
        var succession = new SuccessionAsUsage { Id = StableIds.For(successionKey) };
        var membership = new FeatureMembership { Id = NextId(successionKey + "/membership"), Visibility = VisibilityKind.Public };
        owner.AssignOwnership(membership, succession);
        WriteEnds(succession, successionKey, [from, to]);
    }

    /// <summary>
    /// "transition first a then b": the transition's source as a membership
    /// of the state it leaves, and a succession from that state to the next.
    /// Triggers, guards and effects are not written.
    /// </summary>
    private void Transition(SysmlElement source, string key, SysmlElement? origin, SysmlElement? target)
    {
        if (source is not ITransitionUsage transition || origin is not IFeature from || target is not IFeature)
            return;

        transition.AssignOwnership(new Membership
        {
            Id = NextId(key + "/source"),
            MemberElement = from,
            MemberName = from.DeclaredName,
            Visibility = VisibilityKind.Public,
        });
        Succession(transition, key, origin, target);
    }

    // ---- Pass 3: imports ----

    /// <summary>
    /// "import P::*" as a NamespaceImport of P and "import P::x" as a
    /// MembershipImport of x's membership, when P or x is in this workspace.
    /// Imports of the standard library name nothing here and are left out.
    /// </summary>
    private void Import(ModelElement element)
    {
        if (!_elements.TryGetValue(element, out var scope) || scope is not INamespace)
            return;

        var key = _keys[element];
        foreach (var relation in element.Relations.Where(r => r.Kind == RelationKind.Import))
        {
            var written = relation.TargetReference.Trim();
            var isRecursive = written.EndsWith("::**", StringComparison.Ordinal);
            var isAll = isRecursive || written.EndsWith("::*", StringComparison.Ordinal);
            var name = isAll ? written[..written.LastIndexOf("::", StringComparison.Ordinal)] : written;
            var visibility = relation.TargetSpan is { } span
                ? VisibilityBefore(element.File.Text, ImportKeywordBefore(element.File.Text, span.Start), VisibilityKind.Public)
                : VisibilityKind.Public;

            var resolved = _workspace.ResolveReference(name, element);
            if (resolved is null)
                continue;

            var id = NextId(key + "/import/" + written);
            if (isAll && Resolved(resolved) is INamespace imported)
                scope.AssignOwnership(new NamespaceImport { Id = id, ImportedNamespace = imported, IsRecursive = isRecursive, Visibility = visibility });
            else if (!isAll && _memberships.TryGetValue(resolved, out var membership))
                scope.AssignOwnership(new MembershipImport { Id = id, ImportedMembership = membership, Visibility = visibility });
        }
    }

    // ---- Reading the text around an element ----

    private Guid NextId(string key) => _ids.Next(key).Id;

    private static FeatureDirectionKind? DirectionOf(ModelElement element) => element.Context.Start.Text switch
    {
        "in" => FeatureDirectionKind.In,
        "out" => FeatureDirectionKind.Out,
        "inout" => FeatureDirectionKind.Inout,
        _ => null,
    };

    /// <summary>"private" or "protected" written just before <paramref name="start"/>, else <paramref name="otherwise"/>.</summary>
    private static VisibilityKind VisibilityBefore(string text, int start, VisibilityKind otherwise) => WordBefore(text, start) switch
    {
        "private" => VisibilityKind.Private,
        "protected" => VisibilityKind.Protected,
        "public" => VisibilityKind.Public,
        _ => otherwise,
    };

    /// <summary>Where the "import" keyword before an imported name starts, or the name's start if there is none.</summary>
    private static int ImportKeywordBefore(string text, int start)
    {
        var at = text.LastIndexOf("import", Math.Max(0, start - 1), StringComparison.Ordinal);
        return at < 0 ? start : at;
    }

    /// <summary>The identifier that ends just before <paramref name="start"/>, whitespace skipped.</summary>
    private static string WordBefore(string text, int start)
    {
        var end = Math.Min(start, text.Length);
        while (end > 0 && char.IsWhiteSpace(text[end - 1]))
            end--;

        var begin = end;
        while (begin > 0 && (char.IsLetterOrDigit(text[begin - 1]) || text[begin - 1] == '_'))
            begin--;

        return text[begin..end];
    }

    [GeneratedRegex(@"^\[\s*(?:(?<lower>\d+)\s*\.\.\s*)?(?<upper>\d+|\*)\s*\]$")]
    private static partial Regex MultiplicityPattern();

    [GeneratedRegex(@"^[#@]\s*(?<name>[A-Za-z_][\w:']*)\s*(?:\{(?<body>.*)\})?\s*;?$", RegexOptions.Singleline)]
    private static partial Regex MetadataPattern();

    [GeneratedRegex(@"(?::>>\s*|redefines\s+)?(?<feature>[A-Za-z_]\w*)\s*=\s*(?<value>[^;]+);")]
    private static partial Regex AssignmentPattern();
}
