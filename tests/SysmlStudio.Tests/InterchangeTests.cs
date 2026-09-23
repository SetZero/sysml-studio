using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SysML2.NET.Common;
using SysML2.NET.Serializer.Json;
using SysmlStudio.Interchange;
using SysmlStudio.Model;
using Xunit;
using Dto = SysML2.NET.Core.DTO;
using Poco = SysML2.NET.Core.POCO;

namespace SysmlStudio.Tests;

/// <summary>
/// What the SysML v2 exports hold, judged by reading them back with
/// SysML2.NET's own readers rather than by looking at the text.
/// </summary>
public sealed class InterchangeTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "sysml-studio-tests", Guid.NewGuid().ToString("N"));

    public InterchangeTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static SysmlWorkspace Fixture() =>
        SysmlWorkspace.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures"));

    private string Export(SysmlWorkspace workspace, ExportFormat format, string name = "export")
    {
        // A folder that does not exist yet: Write has to make it.
        var path = Path.Combine(_folder, name, ModelExporter.DefaultFileName(workspace, format));
        ModelExporter.Write(workspace, format, path);
        return path;
    }

    private static List<IData> ReadJson(string path)
    {
        using var stream = File.OpenRead(path);
        return [.. new DeSerializer(NullLoggerFactory.Instance)
            .DeSerialize(stream, SerializationModeKind.JSON, SerializationTargetKind.CORE, deserializeDerivedProperties: false)];
    }

    private static Poco.Root.Namespaces.INamespace ReadXmi(string path)
        => new SysML2.NET.Serializer.Xmi.DeSerializer(NullLoggerFactory.Instance).DeSerialize(new Uri(path)).RootNamespace;

    private static T Named<T>(IEnumerable<IData> data, string name) where T : Dto.Root.Elements.IElement
        => data.OfType<T>().Single(e => e.DeclaredName == name);

    [Fact]
    public void TheJsonHoldsTheModelsElementsAndEdges()
    {
        var data = ReadJson(Export(Fixture(), ExportFormat.Json));

        Named<Dto.Kernel.Packages.Package>(data, "Sample");
        var engineDef = Named<Dto.Systems.Parts.PartDefinition>(data, "Engine");
        Assert.Equal("P.1", engineDef.DeclaredShortName);

        // A requirement's short name is its reqId, which redefines declaredShortName.
        Assert.Equal("R.1", Named<Dto.Systems.Requirements.RequirementDefinition>(data, "MustStart").ReqId);
        Named<Dto.Systems.Ports.PortUsage>(data, "shaft");
        Named<Dto.Systems.States.StateDefinition>(data, "Running");

        // "part engine : Engine;"
        var engine = Named<Dto.Systems.Parts.PartUsage>(data, "engine");
        Assert.Contains(data.OfType<Dto.Core.Features.FeatureTyping>(), t => t.TypedFeature == engine.Id && t.Type == engineDef.Id);

        // "part def Wheel :> RollingThing;"
        var wheel = Named<Dto.Systems.Parts.PartDefinition>(data, "Wheel");
        var rolling = Named<Dto.Systems.Parts.PartDefinition>(data, "RollingThing");
        Assert.Contains(data.OfType<Dto.Core.Classifiers.Subclassification>(),
            s => s.Subclassifier == wheel.Id && s.Superclassifier == rolling.Id);

        // The doc comment, owned by what it documents.
        var doc = data.OfType<Dto.Root.Annotations.Documentation>().Single(d => d.Body == "Turns fuel into torque.");
        var docMembership = data.OfType<Dto.Root.Namespaces.OwningMembership>().Single(m => m.Id == doc.OwningRelationship);
        Assert.Equal(engineDef.Id, docMembership.OwningRelatedElement);

        // "dependency from MustStopToo to MustStart;"
        var dependency = data.OfType<Dto.Root.Dependencies.Dependency>().Single();
        Assert.Equal([Named<Dto.Systems.Requirements.RequirementDefinition>(data, "MustStopToo").Id], dependency.Client);
        Assert.Equal([Named<Dto.Systems.Requirements.RequirementDefinition>(data, "MustStart").Id], dependency.Supplier);

        // "#implemented part def Vehicle": metadata typed by the definition it names.
        var implemented = Named<Dto.Systems.Metadata.MetadataDefinition>(data, "implemented");
        var tagged = data.OfType<Dto.Systems.Metadata.MetadataUsage>()
            .Where(u => data.OfType<Dto.Core.Features.FeatureTyping>().Any(t => t.TypedFeature == u.Id && t.Type == implemented.Id))
            .Select(u => data.OfType<Dto.Root.Namespaces.OwningMembership>().Single(m => m.Id == u.OwningRelationship).OwningRelatedElement);
        Assert.Equal([Named<Dto.Systems.Parts.PartDefinition>(data, "Vehicle").Id], tagged);

        // "part x : Sample::NoSuchThing;" names nothing, and so is not typed at all.
        var x = Named<Dto.Systems.Parts.PartUsage>(data, "x");
        Assert.DoesNotContain(data.OfType<Dto.Core.Features.FeatureTyping>(), t => t.TypedFeature == x.Id);
    }

    /// <summary>
    /// Read as plain JSON, not through the DTOs: a DTO fills a membership's
    /// source and target from properties the reader leaves unset, so only the
    /// file says whether those ends were written.
    /// </summary>
    [Fact]
    public void EveryIdTheJsonMentionsIsAnElementInIt()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Export(Fixture(), ExportFormat.Json)));
        var elements = document.RootElement.EnumerateArray().ToList();
        var ids = elements.Select(e => e.GetProperty("@id").GetGuid()).ToHashSet();
        Assert.Equal(elements.Count, ids.Count);

        var references = elements
            .SelectMany(e => e.EnumerateObject())
            .SelectMany(p => p.Value.ValueKind == JsonValueKind.Array ? p.Value.EnumerateArray() : Enumerable.Repeat(p.Value, 1))
            .Where(v => v.ValueKind == JsonValueKind.Object)
            .Select(v => v.GetProperty("@id").GetGuid())
            .ToList();
        Assert.NotEmpty(references);
        Assert.All(references, id => Assert.Contains(id, ids));
    }

    [Fact]
    public void TheXmiReadsBackIntoTheSameTree()
    {
        var workspace = Fixture();
        var root = ReadXmi(Export(workspace, ExportFormat.Xmi));
        var all = Flatten(root).ToList();

        // The same elements as the JSON, one for one.
        Assert.Equal(ReadJson(Export(workspace, ExportFormat.Json)).Count, all.Count);

        // References come back as the objects they point at, not as ids.
        var engine = all.OfType<Poco.Systems.Parts.IPartUsage>().Single(p => p.DeclaredName == "engine");
        var typing = engine.OwnedRelationship.OfType<Poco.Core.Features.IFeatureTyping>().Single();
        Assert.Equal("Engine", typing.Type.DeclaredName);

        var dependency = all.OfType<Poco.Root.Dependencies.IDependency>().Single();
        Assert.Equal("MustStopToo", Assert.Single(dependency.Client).DeclaredName);
        Assert.Equal("MustStart", Assert.Single(dependency.Supplier).DeclaredName);

        var connection = all.OfType<Poco.Systems.Connections.IConnectionUsage>().Single();
        var ends = connection.OwnedRelationship.OfType<Poco.Core.Features.IEndFeatureMembership>()
            .SelectMany(m => m.OwnedRelatedElement)
            .SelectMany(e => e.OwnedRelationship.OfType<Poco.Core.Features.IReferenceSubsetting>())
            .Select(r => r.ReferencedFeature.DeclaredName);
        Assert.Equal(["shaft", "wheels"], ends);

        Assert.Contains(all.OfType<Poco.Root.Annotations.IDocumentation>(), d => d.Body == "The vehicle starts within two seconds.");
    }

    private static IEnumerable<Poco.Root.Elements.IElement> Flatten(Poco.Root.Elements.IElement element)
    {
        yield return element;
        var owned = element.OwnedRelationship.Cast<Poco.Root.Elements.IElement>();
        if (element is Poco.Root.Elements.IRelationship relationship)
            owned = owned.Concat(relationship.OwnedRelatedElement);
        foreach (var child in owned.SelectMany(Flatten))
            yield return child;
    }

    [Theory]
    [InlineData(ExportFormat.Json)]
    [InlineData(ExportFormat.Xmi)]
    public void ExportingTwiceGivesTheSameBytes(ExportFormat format)
    {
        // Two workspaces read separately, so that nothing carries over in memory.
        var first = File.ReadAllBytes(Export(Fixture(), format, "first"));
        var second = File.ReadAllBytes(Export(Fixture(), format, "second"));
        Assert.Equal(first, second);
    }

    [Fact]
    public void TheFileIsNamedAfterWhatTheTopLevelPackagesShare()
    {
        var model = Path.Combine(_folder, "model folder");
        Directory.CreateDirectory(model);
        File.WriteAllText(Path.Combine(model, "a.sysml"), "package FerrixStructure;\n");
        File.WriteAllText(Path.Combine(model, "b.sysml"), "package FerrixStorage;\n");

        Assert.Equal("ferrix.json", ModelExporter.DefaultFileName(SysmlWorkspace.Load(model), ExportFormat.Json));
        Assert.Equal("ferrix.xmi", ModelExporter.DefaultFileName(SysmlWorkspace.Load(model), ExportFormat.Xmi));

        // The fixture's packages share nothing, so its folder names it.
        Assert.Equal("fixtures.xmi", ModelExporter.DefaultFileName(Fixture(), ExportFormat.Xmi));
    }

    /// <summary>The real model, when SYSML_STUDIO_MODEL points at one: both exports read back whole.</summary>
    [SkippableFact]
    public void TheRealModelExportsAndReadsBack()
    {
        var folder = Environment.GetEnvironmentVariable("SYSML_STUDIO_MODEL");
        Skip.If(string.IsNullOrEmpty(folder) || !Directory.Exists(folder), "SYSML_STUDIO_MODEL is not set");

        var workspace = SysmlWorkspace.Load(folder);
        var json = ReadJson(Export(workspace, ExportFormat.Json));
        var xmi = Flatten(ReadXmi(Export(workspace, ExportFormat.Xmi))).ToList();

        Assert.Equal(json.Count, xmi.Count);
        Assert.True(json.Count > workspace.Elements.Count());
    }
}
