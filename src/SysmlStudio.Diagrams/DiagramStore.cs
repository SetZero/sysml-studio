using System.Text.Json;
using System.Text.Json.Serialization;
using SysmlStudio.Model;

namespace SysmlStudio.Diagrams;

/// <summary>One diagram as the sidecar file records it.</summary>
public sealed class StoredDiagram
{
    public DiagramKind Kind { get; set; }

    /// <summary>The qualified name of the element the diagram is of.</summary>
    public string Root { get; set; } = string.Empty;

    /// <summary>Qualified name to position, for the nodes that were moved by hand.</summary>
    public Dictionary<string, double[]> Positions { get; set; } = [];
}

/// <summary>The whole sidecar file.</summary>
public sealed class StoredDiagrams
{
    public List<StoredDiagram> Diagrams { get; set; } = [];
}

/// <summary>
/// <para>Remembers which diagrams were open and where their nodes were dragged to.</para>
/// <para>
/// None of this belongs in the model: SysML v2 has nowhere to put a coordinate
/// and a layout in a .sysml file would be a diff on every drag. It lives in
/// ".sysml-studio/diagrams.json" beside the model instead, which a project can
/// commit or ignore as it likes.
/// </para>
/// </summary>
public static class DiagramStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string PathFor(string modelFolder)
        => Path.Combine(modelFolder, ".sysml-studio", "diagrams.json");

    public static StoredDiagrams Load(string modelFolder)
    {
        var path = PathFor(modelFolder);
        if (!File.Exists(path))
            return new StoredDiagrams();

        try
        {
            return JsonSerializer.Deserialize<StoredDiagrams>(File.ReadAllText(path), Options) ?? new StoredDiagrams();
        }
        catch (JsonException)
        {
            // A sidecar nobody can read is worth less than the diagrams it
            // describes: start again rather than refuse to open the model.
            return new StoredDiagrams();
        }
    }

    public static void Save(string modelFolder, StoredDiagrams stored)
    {
        var path = PathFor(modelFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(stored, Options));
    }

    /// <summary>Records where a diagram's nodes sit now.</summary>
    public static StoredDiagram Capture(Diagram diagram)
    {
        var stored = new StoredDiagram { Kind = diagram.Kind, Root = diagram.Root.QualifiedName };
        foreach (var node in diagram.Nodes)
            stored.Positions[node.Key] = [node.X, node.Y];
        return stored;
    }

    /// <summary>
    /// Puts the nodes back where they were left. Nodes the file does not know
    /// about keep the position the layout gave them, so a diagram of a model
    /// that has grown opens with the new elements laid out around the old ones.
    /// </summary>
    public static void Restore(Diagram diagram, StoredDiagram stored)
    {
        foreach (var node in diagram.Nodes)
        {
            if (stored.Positions.TryGetValue(node.Key, out var position) && position.Length == 2)
            {
                node.X = position[0];
                node.Y = position[1];
            }
        }
    }

    /// <summary>Rebuilds the diagrams the file names, skipping any whose element is gone.</summary>
    public static IEnumerable<Diagram> Reopen(StoredDiagrams stored, SysmlWorkspace workspace)
    {
        foreach (var entry in stored.Diagrams)
        {
            if (workspace.Find(entry.Root) is not { } root)
                continue;

            var diagram = DiagramBuilder.Build(entry.Kind, root);
            DiagramLayout.Apply(diagram);
            Restore(diagram, entry);
            yield return diagram;
        }
    }
}
