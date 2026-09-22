using SysmlStudio.Diagrams;
using SysmlStudio.Model;

var dir = args[0];
var workspace = SysmlWorkspace.Load(dir);

foreach (var file in workspace.Files.OrderBy(f => f.Path))
    Console.WriteLine($"{(file.IsValid ? "OK   " : "FAIL ")} {Path.GetFileName(file.Path),-24} {file.Errors.Count} errors");

Console.WriteLine();
Console.WriteLine($"elements: {workspace.Elements.Count()}");
foreach (var group in workspace.Elements.GroupBy(e => e.Kind).OrderByDescending(g => g.Count()).Take(15))
    Console.WriteLine($"  {group.Count(),5}  {group.Key}");

Console.WriteLine();
var relations = workspace.Elements.SelectMany(e => e.Relations).ToList();
Console.WriteLine($"relations: {relations.Count}");
foreach (var group in relations.GroupBy(r => r.Kind).OrderByDescending(g => g.Count()))
    Console.WriteLine($"  {group.Count(),5}  {group.Key}  ({group.Count(r => r.Target is not null)} resolved)");

Console.WriteLine();
Console.WriteLine($"unresolved: {workspace.UnresolvedReferences.Count}");
foreach (var u in workspace.UnresolvedReferences.Take(15))
    Console.WriteLine("  " + u);

Console.WriteLine();
Console.WriteLine("maturity:");
foreach (var group in workspace.Elements.Where(e => e.Maturity is not null).GroupBy(e => e.Maturity))
    Console.WriteLine($"  {group.Count(),5}  {group.Key}");

Console.WriteLine();
Console.WriteLine("diagrams:");
foreach (var element in workspace.Elements)
{
    foreach (var kind in DiagramBuilder.KindsFor(element))
    {
        var diagram = DiagramBuilder.Build(kind, element);
        if (diagram.Nodes.Count == 0)
            continue;

        DiagramLayout.Apply(diagram);
        if (diagram.Edges.Count > 0)
            Console.WriteLine($"  {kind,-16} {element.DisplayName,-28} {diagram.Nodes.Count,3} nodes {diagram.Edges.Count,3} edges");
    }
}

return workspace.Errors.Count == 0 ? 0 : 1;
