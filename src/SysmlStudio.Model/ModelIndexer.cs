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
                element.Add(new Relation(ck, element, ends[i + 1], ends[i]));
        }

        switch (element.Kind)
        {
            case "satisfy":
                {
                    var requirement = FindShallow(ctx, "ownedReferenceSubsetting").FirstOrDefault();
                    if (requirement is not null)
                        element.Add(new Relation(RelationKind.Satisfy, element, Written(requirement)));
                    var subject = FindShallow(ctx, "satisfactionSubjectMember").FirstOrDefault();
                    if (subject is not null)
                        element.Add(new Relation(RelationKind.Satisfy, element, Written(subject), "by"));
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
                            label.Length == 0 ? null : label));
                        element.Value = Written(source);
                    }

                    break;
                }

            case "succession":
                {
                    var ends = FindShallow(ctx, "connectorEndMember").ConvertAll(Written);
                    for (var i = 0; i + 1 < ends.Count; i++)
                        element.Add(new Relation(RelationKind.Succession, element, ends[i + 1], ends[i]));
                    break;
                }

            case "dependency":
                {
                    foreach (var target in DependencyTargets(ctx))
                        element.Add(new Relation(RelationKind.Dependency, element, target));
                    break;
                }
        }
    }

    /// <summary>The qualified names a dependency names after its "to".</summary>
    private static List<string> DependencyTargets(ParserRuleContext ctx)
    {
        var targets = new List<string>();
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

            if (afterTo && child is ParserRuleContext q && RuleName(q) == "qualifiedName")
                targets.Add(q.GetText());
        }

        return targets;
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
