using SysML2.NET.Core.POCO.Kernel.Packages;
using SysML2.NET.Core.POCO.Root.Dependencies;
using SysML2.NET.Core.POCO.Root.Elements;
using SysML2.NET.Core.POCO.Root.Namespaces;
using SysML2.NET.Core.POCO.Systems.Actions;
using SysML2.NET.Core.POCO.Systems.Allocations;
using SysML2.NET.Core.POCO.Systems.AnalysisCases;
using SysML2.NET.Core.POCO.Systems.Attributes;
using SysML2.NET.Core.POCO.Systems.Calculations;
using SysML2.NET.Core.POCO.Systems.Cases;
using SysML2.NET.Core.POCO.Systems.Connections;
using SysML2.NET.Core.POCO.Systems.Constraints;
using SysML2.NET.Core.POCO.Systems.DefinitionAndUsage;
using SysML2.NET.Core.POCO.Systems.Enumerations;
using SysML2.NET.Core.POCO.Systems.Flows;
using SysML2.NET.Core.POCO.Systems.Interfaces;
using SysML2.NET.Core.POCO.Systems.Items;
using SysML2.NET.Core.POCO.Systems.Metadata;
using SysML2.NET.Core.POCO.Systems.Occurrences;
using SysML2.NET.Core.POCO.Systems.Parts;
using SysML2.NET.Core.POCO.Systems.Ports;
using SysML2.NET.Core.POCO.Systems.Requirements;
using SysML2.NET.Core.POCO.Systems.States;
using SysML2.NET.Core.POCO.Systems.UseCases;
using SysML2.NET.Core.POCO.Systems.VerificationCases;
using SysML2.NET.Core.POCO.Systems.Views;

namespace SysmlStudio.Interchange;

/// <summary>
/// Which SysML v2 metaclass each kind the indexer reports becomes. The kinds
/// are the notation's keywords ("part def", "use case", "satisfy"), so most map
/// one to one; the rest are named after the grammar rule they came from and
/// map to what that rule produces in the pilot implementation.
/// </summary>
internal static class Metaclasses
{
    private static readonly Dictionary<string, Func<IElement>> ByKind = new(StringComparer.Ordinal)
    {
        ["package"] = () => new Package(),
        ["library package"] = () => new LibraryPackage(),
        ["namespace"] = () => new Namespace(),
        ["dependency"] = () => new Dependency(),

        ["part def"] = () => new PartDefinition(),
        ["part"] = () => new PartUsage(),
        ["port def"] = () => new PortDefinition(),
        ["port"] = () => new PortUsage(),
        ["attribute def"] = () => new AttributeDefinition(),
        ["attribute"] = () => new AttributeUsage(),
        ["item def"] = () => new ItemDefinition(),
        ["item"] = () => new ItemUsage(),
        ["enumeration def"] = () => new EnumerationDefinition(),
        ["enumeration"] = () => new EnumerationUsage(),
        ["occurrence def"] = () => new OccurrenceDefinition(),
        ["occurrence"] = () => new OccurrenceUsage(),
        ["individual def"] = () => new OccurrenceDefinition { IsIndividual = true },
        ["individual"] = () => new OccurrenceUsage { IsIndividual = true },
        ["portion"] = () => new OccurrenceUsage(),
        ["event occurrence"] = () => new EventOccurrenceUsage(),
        ["ref"] = () => new ReferenceUsage(),
        ["end"] = () => new ReferenceUsage { IsEnd = true },
        ["subject"] = () => new ReferenceUsage(),
        ["metadata body"] = () => new ReferenceUsage(),
        ["actor"] = () => new PartUsage(),
        ["stakeholder"] = () => new PartUsage(),

        ["connection def"] = () => new ConnectionDefinition(),
        ["connection"] = () => new ConnectionUsage(),
        ["interface def"] = () => new InterfaceDefinition(),
        ["interface"] = () => new InterfaceUsage(),
        ["allocation def"] = () => new AllocationDefinition(),
        ["allocation"] = () => new AllocationUsage(),
        ["flow def"] = () => new FlowDefinition(),
        ["flow"] = () => new FlowUsage(),
        ["succession flow"] = () => new SuccessionFlowUsage(),
        ["succession"] = () => new SuccessionAsUsage(),
        ["binding connector as"] = () => new BindingConnectorAsUsage(),

        ["action def"] = () => new ActionDefinition(),
        ["action"] = () => new ActionUsage(),
        ["perform action"] = () => new PerformActionUsage(),
        ["effect behavior"] = () => new ActionUsage(),
        ["state def"] = () => new StateDefinition(),
        ["state"] = () => new StateUsage(),
        ["exhibit state"] = () => new ExhibitStateUsage(),
        ["transition"] = () => new TransitionUsage(),
        ["target transition"] = () => new TransitionUsage(),
        ["state action"] = () => new ActionUsage(),
        ["state perform action"] = () => new PerformActionUsage(),
        ["state accept action"] = () => new AcceptActionUsage(),
        ["state send action"] = () => new SendActionUsage(),
        ["state assignment action"] = () => new AssignmentActionUsage(),
        ["transition perform action"] = () => new PerformActionUsage(),
        ["transition accept action"] = () => new AcceptActionUsage(),
        ["transition send action"] = () => new SendActionUsage(),
        ["transition assignment action"] = () => new AssignmentActionUsage(),

        ["calculation def"] = () => new CalculationDefinition(),
        ["calculation"] = () => new CalculationUsage(),
        ["constraint def"] = () => new ConstraintDefinition(),
        ["constraint"] = () => new ConstraintUsage(),
        ["assert constraint"] = () => new AssertConstraintUsage(),
        ["requirement constraint"] = () => new ConstraintUsage(),
        ["requirement def"] = () => new RequirementDefinition(),
        ["requirement"] = () => new RequirementUsage(),
        ["objective requirement"] = () => new RequirementUsage(),
        ["verify"] = () => new RequirementUsage(),
        ["satisfy"] = () => new SatisfyRequirementUsage(),
        ["concern def"] = () => new ConcernDefinition(),
        ["concern"] = () => new ConcernUsage(),
        ["framed concern"] = () => new ConcernUsage(),

        ["case def"] = () => new CaseDefinition(),
        ["case"] = () => new CaseUsage(),
        ["use case def"] = () => new UseCaseDefinition(),
        ["use case"] = () => new UseCaseUsage(),
        ["include use case"] = () => new IncludeUseCaseUsage(),
        ["verification case def"] = () => new VerificationCaseDefinition(),
        ["verification case"] = () => new VerificationCaseUsage(),
        ["analysis case def"] = () => new AnalysisCaseDefinition(),
        ["analysis case"] = () => new AnalysisCaseUsage(),

        ["view def"] = () => new ViewDefinition(),
        ["view"] = () => new ViewUsage(),
        ["viewpoint def"] = () => new ViewpointDefinition(),
        ["viewpoint"] = () => new ViewpointUsage(),
        ["rendering def"] = () => new RenderingDefinition(),
        ["rendering"] = () => new RenderingUsage(),
        ["view rendering"] = () => new RenderingUsage(),

        ["metadata def"] = () => new MetadataDefinition(),
        ["metadata"] = () => new MetadataUsage(),
        ["prefix metadata"] = () => new MetadataUsage(),
    };

    /// <summary>
    /// A new, empty element for <paramref name="kind"/>. A kind this table does
    /// not know still becomes something: a plain Definition or Usage, which is
    /// what every SysML definition and usage specializes.
    /// </summary>
    public static IElement Create(string kind, bool isDefinition)
    {
        if (ByKind.TryGetValue(kind, out var create))
            return create();

        return isDefinition ? new Definition() : new Usage();
    }
}
