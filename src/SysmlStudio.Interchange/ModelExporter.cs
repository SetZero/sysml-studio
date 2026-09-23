using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using SysML2.NET.Common;
using SysML2.NET.Core.POCO.Root.Elements;
using SysML2.NET.Core.POCO.Root.Namespaces;
using SysML2.NET.Serializer.Json;
using SysML2.NET.Serializer.Xmi.Writers;
using SysmlStudio.Model;

namespace SysmlStudio.Interchange;

/// <summary>The file formats a model can be exported to.</summary>
public enum ExportFormat
{
    /// <summary>SysML v2 JSON, as the OMG Systems Modeling API exchanges it: one array of elements.</summary>
    Json,

    /// <summary>SysML v2 XMI, as the OMG pilot implementation reads and writes it: one containment tree.</summary>
    Xmi,
}

/// <summary>
/// Writes a whole workspace — every file, as one model — in a standard SysML v2
/// interchange format, so that other SysML v2 tools can read what this one
/// edits. Element ids are derived from qualified names, so exporting the same
/// model twice gives byte-identical files.
/// </summary>
public static class ModelExporter
{
    /// <summary>Writes <paramref name="workspace"/> to <paramref name="path"/>, creating its folder if need be.</summary>
    public static void Write(SysmlWorkspace workspace, ExportFormat format, string path)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrEmpty(path);

        // Serialize in memory first: a failure then leaves no half-written file behind.
        using var buffer = new MemoryStream();
        var root = ModelBuilder.Build(workspace);
        switch (format)
        {
            case ExportFormat.Json:
                WriteJson(root, buffer);
                break;
            case ExportFormat.Xmi:
                WriteXmi(root, buffer);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "unknown export format");
        }

        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        File.WriteAllBytes(path, buffer.ToArray());
    }

    /// <summary>
    /// A file name for the export: the name the top-level packages share, such
    /// as "ferrix" for FerrixBoot, FerrixMemory and the rest, or else the
    /// folder's name, lower-cased, with the format's extension.
    /// </summary>
    public static string DefaultFileName(SysmlWorkspace workspace, ExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var names = workspace.Root.Children.Select(c => c.Name).OfType<string>().Where(n => n.Length > 0).ToList();
        var stem = SharedPrefix(names);
        if (stem.Length == 0)
            stem = Path.GetFileName(Path.TrimEndingDirectorySeparator(workspace.Directory));

        var invalid = Path.GetInvalidFileNameChars();
        stem = new string([.. stem.Where(c => !invalid.Contains(c))]).Trim('.', ' ');
        if (stem.Length == 0)
            stem = "model";

        return stem.ToLowerInvariant() + (format == ExportFormat.Xmi ? ".xmi" : ".json");
    }

    /// <summary>
    /// The longest prefix all names share that ends between two words of a
    /// camel-case name: "Ferrix" for FerrixStructure and FerrixStorage, not "FerrixSt".
    /// </summary>
    private static string SharedPrefix(List<string> names)
    {
        if (names.Count == 0)
            return string.Empty;

        var length = names.Min(n => n.Length);
        for (var i = 0; i < length; i++)
        {
            if (names.Any(n => n[i] != names[0][i]))
            {
                length = i;
                break;
            }
        }

        while (length > 0 && names.Any(n => n.Length > length && char.IsLower(n[length])))
            length--;

        return names[0][..length].TrimEnd('_', '-', '.');
    }

    // ---- JSON ----

    private static void WriteJson(INamespace root, Stream stream)
    {
        var items = new List<IData>();
        Collect(root, items);

        var options = new JsonWriterOptions
        {
            Indented = true,

            // The same bytes on every platform, not Environment.NewLine.
            NewLine = "\n",

            // A file, not a web page: keep "—" and "ä" readable instead of \u escapes.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        new Serializer().Serialize(items, SerializationModeKind.JSON, includeDerivedProperties: false, stream, options);
    }

    /// <summary>Every element of the tree, owners before what they own, as the DTOs the JSON serializer writes.</summary>
    private static void Collect(IElement element, List<IData> items)
    {
        items.Add(ToDto(element));
        foreach (var relationship in element.OwnedRelationship)
            Collect(relationship, items);

        if (element is IRelationship owning)
        {
            foreach (var owned in owning.OwnedRelatedElement)
                Collect(owned, items);
        }
    }

    /// <summary>
    /// SysML2.NET.Dal has a generated POCO-to-DTO mapping per metaclass, each an
    /// extension method "ToDto(this X poco, bool includeDerivedProperties)".
    /// They are looked up once by the POCO type they take.
    /// </summary>
    private static readonly Dictionary<Type, MethodInfo> ToDtoMethods = FindToDtoMethods();

    private static Dictionary<Type, MethodInfo> FindToDtoMethods()
    {
        var methods = new Dictionary<Type, MethodInfo>();
        var candidates = typeof(SysML2.NET.Dal.Assembler).Assembly.GetExportedTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.Name == "ToDto" && typeof(IData).IsAssignableFrom(m.ReturnType));
        foreach (var method in candidates)
        {
            if (method.GetParameters() is [var poco, var derived] && derived.ParameterType == typeof(bool))
                methods.TryAdd(poco.ParameterType, method);
        }

        return methods;
    }

    /// <summary>
    /// The element as a DTO. A relationship gets its derived properties
    /// computed, because SysML2.NET's JSON writer takes a membership's
    /// memberElement, source and target from them and writes empty ids
    /// without. Other elements do not: some of theirs, such as a literal's
    /// result, cannot be computed without the standard library.
    /// </summary>
    private static IData ToDto(IElement element)
    {
        if (!ToDtoMethods.TryGetValue(element.GetType(), out var method))
            throw new InvalidOperationException($"SysML2.NET has no DTO mapping for {element.GetType().Name}");

        return (IData)method.Invoke(null, [element, element is IRelationship])!;
    }

    // ---- XMI ----

    private static readonly XNamespace XmiNamespace = "http://www.omg.org/spec/XMI/20131001";

    private static void WriteXmi(INamespace root, Stream stream)
    {
        using var written = new MemoryStream();
        var options = new XmiWriterOptions { IncludeDerivedProperties = false, IncludeImplied = false };
        new SysML2.NET.Serializer.Xmi.Serializer(NullLoggerFactory.Instance).Serialize(root, options, written);

        written.Position = 0;
        var document = XDocument.Load(written);
        ReferencesAsAttributes(document);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        using var writer = XmlWriter.Create(stream, settings);
        document.Save(writer);
    }

    /// <summary>
    /// SysML2.NET 0.23 writes a many-valued reference — a dependency's clients
    /// and suppliers — as one &lt;client xmi:idref="…"/&gt; element per value,
    /// but its own reader, like the pilot implementation, takes only the
    /// attribute form client="id id". Both are valid XMI; this rewrites the
    /// first into the second so that the file reads back.
    /// </summary>
    private static void ReferencesAsAttributes(XDocument document)
    {
        var idref = XmiNamespace + "idref";
        foreach (var owner in document.Descendants().ToList())
        {
            var references = owner.Elements()
                .Where(e => !e.HasElements && e.Attributes().Count() == 1 && e.Attribute(idref) is not null)
                .GroupBy(e => e.Name)
                .Where(g => g.Key.Namespace == XNamespace.None && owner.Attribute(g.Key) is null)
                .ToList();
            foreach (var group in references)
            {
                owner.SetAttributeValue(group.Key, string.Join(" ", group.Select(e => e.Attribute(idref)!.Value)));
                group.Remove();
            }
        }
    }
}
