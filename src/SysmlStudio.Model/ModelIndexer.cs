using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using SysmlStudio.Syntax;
using SysmlStudio.Syntax.Generated;

namespace SysmlStudio.Model;

/// <summary>
/// <para>
/// Turns a parse tree into the element tree the browser, the properties pane
/// and the diagrams read.
/// </para>
/// <para>
/// It works off rule names rather than generated context types on purpose: the
/// grammar has ~450 rules, almost all of them shaped the same way — an element
/// rule named "&lt;something&gt;Definition" or "&lt;something&gt;Usage" holding an
/// "identification", an optional specialization part and a body of further
/// element rules. Recognising that shape costs one pass and keeps working when
/// the grammar is re-vendored with rules this file has never heard of.
/// </para>
/// </summary>
public static class ModelIndexer
{
    /// <summary>Element rules whose names do not follow the Definition/Usage pattern.</summary>
    private static readonly Dictionary<string, string> ExtraElementRules = new()
    {
        ["package"] = "package",
        ["libraryPackage"] = "library package",
        ["dependency"] = "dependency",
        ["namespace"] = "namespace",
    };

    /// <summary>Rules that look like element rules by name but are not.</summary>
    private static readonly HashSet<string> NotElementRules =
    [
        "definition", "usage", "definitionElement", "usageElement",
        "nonOccurrenceUsageElement", "occurrenceUsageElement", "structureUsageElement",
        "behaviorUsageElement", "variantUsageElement", "endOccurrenceUsageElement",
        "extendedDefinition", "extendedUsage",
    ];

    /// <summary>Kind labels for element rules whose rule name does not read well.</summary>
    private static readonly Dictionary<string, string> KindOverrides = new()
    {
        ["referenceUsage"] = "ref",
        ["defaultReferenceUsage"] = "ref",
        ["endFeatureUsage"] = "end",
        ["satisfyRequirementUsage"] = "satisfy",
        ["requirementVerificationUsage"] = "verify",
        ["requirementConstraintUsage"] = "requirement constraint",
        ["eventOccurrenceUsage"] = "event occurrence",
        ["performActionUsage"] = "perform action",
        ["exhibitStateUsage"] = "exhibit state",
        ["includeUseCaseUsage"] = "include use case",
        ["assertConstraintUsage"] = "assert constraint",
        ["successionFlowUsage"] = "succession flow",
        ["successionAsUsage"] = "succession",
        ["emptySuccessionMember"] = "succession",
    };

    /// <summary>Indexes one file under <paramref name="root"/>.</summary>
    public static void Index(SourceFile file, Element root)
    {
        Walk(file.Tree, root, file);
    }

    private static void Walk(IParseTree node, Element parent, SourceFile file)
    {
        for (var i = 0; i < node.ChildCount; i++)
        {
            var child = node.GetChild(i);
            if (child is not ParserRuleContext ctx)
                continue;

            var rule = RuleName(ctx);
            if (rule == "importRule")
            {
                var imported = FindShallow(ctx, "importDeclaration").FirstOrDefault();
                if (imported is not null)
                    parent.Add(new Relation(RelationKind.Import, parent, Written(imported)));
                continue;
            }

            if (rule == "actionBody")
            {
                WalkActionBody(ctx, parent, file);
                continue;
            }

            if (TryKind(rule, out var kind))
            {
                var element = new Element(kind, file, ctx);
                Describe(element, ctx);
                parent.Add(element);
                Walk(ctx, element, file);
            }
            else
            {
                Walk(child, parent, file);
            }
        }
    }

    /// <summary>
    /// An action's body, read in order so that the shorthand successions become
    /// relations: "first start; then action a; then action b; then done;" is
    /// start, a, b, done in that order, though only "first" and "then" say so.
    /// </summary>
    private static void WalkActionBody(ParserRuleContext body, Element owner, SourceFile file)
    {
        string? previous = null;
        for (var i = 0; i < body.ChildCount; i++)
        {
            if (body.GetChild(i) is not ParserRuleContext item)
                continue;

            var before = owner.Children.Count;
            WalkOne(item, owner, file);
            var added = owner.Children.Skip(before).LastOrDefault(c => c.Name is not null);

            var initial = FindShallow(item, "initialNodeMember").FirstOrDefault();
            if (initial is not null)
            {
                previous = FindShallow(initial, "qualifiedName").FirstOrDefault()?.GetText();
                continue;
            }

            var isThen = FindShallow(item, "sourceSuccessionMember").Count > 0;
            if (added?.Name is { } addedName)
            {
                if (isThen && previous is not null)
                    owner.Add(new Relation(RelationKind.Succession, owner, addedName, originReference: previous));
                previous = addedName;
            }

            var targetSuccession = FindShallow(item, "actionTargetSuccessionMember").FirstOrDefault();
            var end = targetSuccession is null ? null : FindShallow(targetSuccession, "connectorEndMember").FirstOrDefault();
            if (end is not null && previous is not null)
            {
                var target = Written(end);
                owner.Add(new Relation(RelationKind.Succession, owner, target, originReference: previous));
                previous = target;
            }
        }
    }

    /// <summary>Walks one context as if it were a node's only child.</summary>
    private static void WalkOne(ParserRuleContext ctx, Element parent, SourceFile file)
    {
        if (TryKind(RuleName(ctx), out var kind))
        {
            var element = new Element(kind, file, ctx);
            Describe(element, ctx);
            parent.Add(element);
            Walk(ctx, element, file);
        }
        else
        {
            Walk(ctx, parent, file);
        }
    }

    private static bool TryKind(string rule, out string kind)
    {
        if (ExtraElementRules.TryGetValue(rule, out var extra))
        {
            kind = extra;
            return true;
        }

        kind = string.Empty;
        if (NotElementRules.Contains(rule))
            return false;
        if (!rule.EndsWith("Definition", StringComparison.Ordinal) && !rule.EndsWith("Usage", StringComparison.Ordinal))
            return false;

        if (KindOverrides.TryGetValue(rule, out var over))
        {
            kind = over;
            return true;
        }

        var isDefinition = rule.EndsWith("Definition", StringComparison.Ordinal);
        var stem = rule[..^(isDefinition ? "Definition".Length : "Usage".Length)];
        kind = SplitCamel(stem) + (isDefinition ? " def" : string.Empty);
        return true;
    }

    /// <summary>"partDefinition" -> "part", "useCaseDefinition" -> "use case".</summary>
    private static string SplitCamel(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (char.IsUpper(c) && sb.Length > 0)
                sb.Append(' ');
            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    /// <summary>Fills in name, documentation, metadata and the element's own relations.</summary>
    private static void Describe(Element element, ParserRuleContext ctx)
    {
        var identification = FindShallow(ctx, "identification").FirstOrDefault();
        if (identification is not null)
            ReadIdentification(element, identification);

        foreach (var meta in FindShallow(ctx, "prefixMetadataMember", "prefixMetadataAnnotation"))
            element.Metadata.Add("#" + Written(meta).TrimStart('#').Trim());

        foreach (var meta in FindShallow(ctx, "metadataFeature"))
            element.Metadata.Add(Written(meta).Trim());

        foreach (var doc in FindShallow(ctx, "documentation"))
            element.Documentation = AppendDoc(element.Documentation, DocText(doc));

        var multiplicity = FindShallow(ctx, "ownedMultiplicity").FirstOrDefault();
        if (multiplicity is not null)
            element.Multiplicity = Written(multiplicity);

        var value = FindShallow(ctx, "valuePart").FirstOrDefault();
        if (value is not null)
            element.Value = Written(value).TrimStart('=').Trim();

        ReadRelations(element, ctx);
    }

    private static void ReadIdentification(Element element, ParserRuleContext identification)
    {
        var names = FindShallow(identification, "name").ToList();
        var hasShort = identification.GetChild(0) is ITerminalNode t && t.GetText() == "<";
        if (hasShort)
        {
            element.ShortName = Unquote(names.ElementAtOrDefault(0)?.GetText());
            element.Name = Unquote(names.ElementAtOrDefault(1)?.GetText());
        }
        else
        {
            element.Name = Unquote(names.ElementAtOrDefault(0)?.GetText());
        }
    }

    private static void ReadRelations(Element element, ParserRuleContext ctx)
    {
        // Specialization: ":>" on a definition, ":>" / "subsets" on a usage.
        foreach (var part in FindShallow(ctx, "subclassificationPart"))
        {
            foreach (var target in FindShallow(part, "ownedSubclassification"))
                element.Add(new Relation(RelationKind.Specialization, element, Written(target)));
        }

        foreach (var part in FindShallow(ctx, "featureSpecializationPart"))
        {
            foreach (var typing in FindShallow(part, "ownedFeatureTyping"))
                element.Add(new Relation(RelationKind.Typing, element, Written(typing)));
            foreach (var subset in FindShallow(part, "ownedSubsetting"))
                element.Add(new Relation(RelationKind.Specialization, element, Written(subset)));
            foreach (var redef in FindShallow(part, "ownedRedefinition"))
                element.Add(new Relation(RelationKind.Redefinition, element, Written(redef)));
        }

        // Connectors: connect / interface / allocate / flow all carry end members.
        var connectorKind = element.Kind switch
        {
            "connection" => RelationKind.Connect,
            "interface" => RelationKind.Interface,
            "allocation" => RelationKind.Allocate,
            "flow" or "succession flow" => RelationKind.Flow,
            _ => (RelationKind?)null,
        };

        if (connectorKind is { } ck)
        {
            var ends = FindShallow(ctx, "connectorEndMember", "interfaceEndMember", "messageEventMember")
                .ConvertAll(Written);
            for (var i = 0; i + 1 < ends.Count; i++)
                element.Add(new Relation(ck, element, ends[i + 1], element.Name, ends[i]));
        }

        switch (element.Kind)
        {
            case "satisfy":
                {
                    // "satisfy R by x": one arrow, from what satisfies to what is satisfied.
                    var requirement = FindShallow(ctx, "ownedReferenceSubsetting").FirstOrDefault();
                    var subject = FindShallow(ctx, "satisfactionSubjectMember").FirstOrDefault();
                    if (requirement is not null)
                    {
                        element.Add(new Relation(RelationKind.Satisfy, element, Written(requirement),
                            originReference: subject is null ? null : Written(subject)));
                    }

                    break;
                }

            case "verify":
                {
                    var requirement = FindShallow(ctx, "ownedReferenceSubsetting").FirstOrDefault();
                    if (requirement is not null)
                        element.Add(new Relation(RelationKind.Verify, element, Written(requirement)));
                    break;
                }

            case "transition":
                {
                    var source = FindShallow(ctx, "featureChainMember").FirstOrDefault();
                    var target = FindShallow(ctx, "transitionSuccessionMember").FirstOrDefault();
                    var guard = FindShallow(ctx, "guardExpressionMember").FirstOrDefault();
                    var trigger = FindShallow(ctx, "triggerActionMember").FirstOrDefault();
                    if (source is not null && target is not null)
                    {
                        var label = string.Join(" ", new[] { trigger, guard }
                            .OfType<ParserRuleContext>()
                            .Select(Written));
                        element.Add(new Relation(RelationKind.Transition, element, Written(target),
                            label.Length == 0 ? null : label, Written(source)));
                        element.Value = Written(source);
                    }

                    break;
                }

            case "succession":
                {
                    var ends = FindShallow(ctx, "connectorEndMember").ConvertAll(Written);
                    for (var i = 0; i + 1 < ends.Count; i++)
                        element.Add(new Relation(RelationKind.Succession, element, ends[i + 1], element.Name, ends[i]));
                    break;
                }

            case "dependency":
                {
                    // "dependency from a, b to c, d": an arrow from each client to each supplier.
                    var (clients, suppliers) = DependencyEnds(ctx);
                    foreach (var supplier in suppliers)
                    {
                        if (clients.Count == 0)
                            element.Add(new Relation(RelationKind.Dependency, element, supplier));
                        foreach (var client in clients)
                            element.Add(new Relation(RelationKind.Dependency, element, supplier, originReference: client));
                    }

                    break;
                }
        }
    }

    /// <summary>The names a dependency lists after "from" and after "to".</summary>
    private static (List<string> Clients, List<string> Suppliers) DependencyEnds(ParserRuleContext ctx)
    {
        var clients = new List<string>();
        var suppliers = new List<string>();
        var afterTo = false;
        for (var i = 0; i < ctx.ChildCount; i++)
        {
            var child = ctx.GetChild(i);
            if (child is ITerminalNode terminal)
            {
                if (terminal.GetText() == "to")
                    afterTo = true;
                continue;
            }

            if (child is ParserRuleContext q && RuleName(q) == "qualifiedName")
                (afterTo ? suppliers : clients).Add(q.GetText());
        }

        return (clients, suppliers);
    }

    /// <summary>
    /// Descendants with one of the given rule names, without crossing into a
    /// nested element — a part's own name must not be taken from a part inside it.
    /// </summary>
    private static List<ParserRuleContext> FindShallow(ParserRuleContext ctx, params string[] ruleNames)
    {
        var wanted = new HashSet<string>(ruleNames);
        var found = new List<ParserRuleContext>();
        Collect(ctx, true);
        return found;

        void Collect(IParseTree node, bool isRoot)
        {
            for (var i = 0; i < node.ChildCount; i++)
            {
                if (node.GetChild(i) is not ParserRuleContext child)
                    continue;

                var rule = RuleName(child);
                if (wanted.Contains(rule))
                {
                    found.Add(child);
                    continue;
                }

                if (!isRoot && TryKind(rule, out _))
                    continue; // a nested element owns whatever is inside it

                if (rule is "metadataFeature" or "prefixMetadataMember" or "prefixMetadataAnnotation")
                    continue; // metadata is read on its own, never as the element's value or type

                Collect(child, false);
            }
        }
    }

    private static string RuleName(ParserRuleContext ctx) => SysMLv2Parser.ruleNames[ctx.RuleIndex];

    /// <summary>The source text of a context, exactly as written.</summary>
    private static string Written(ParserRuleContext ctx)
    {
        var stop = ctx.Stop ?? ctx.Start;
        return ctx.Start.InputStream.GetText(new Antlr4.Runtime.Misc.Interval(ctx.Start.StartIndex, stop.StopIndex));
    }

    private static string DocText(ParserRuleContext documentation)
    {
        var comment = documentation.GetChild(documentation.ChildCount - 1).GetText();
        return CleanComment(comment);
    }

    /// <summary>Strips "/*", "*/" and the leading "*" of continuation lines.</summary>
    public static string CleanComment(string comment)
    {
        var text = comment.Trim();
        if (text.StartsWith("/*", StringComparison.Ordinal))
            text = text[2..];
        if (text.EndsWith("*/", StringComparison.Ordinal))
            text = text[..^2];

        var lines = text.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.TrimStart().TrimStart('*').Trim());
        return string.Join(" ", lines.Where(l => l.Length > 0)).Trim();
    }

    private static string AppendDoc(string? existing, string added)
        => string.IsNullOrEmpty(existing) ? added : existing + " " + added;

    private static string? Unquote(string? name)
    {
        if (name is null)
            return null;
        if (name.Length >= 2 && name[0] == '\'' && name[^1] == '\'')
            return name[1..^1];
        return name;
    }
}
