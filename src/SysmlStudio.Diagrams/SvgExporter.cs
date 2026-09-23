using System.Globalization;
using System.Text;
using SysmlStudio.Model;

namespace SysmlStudio.Diagrams;

/// <summary>
/// Writes a laid-out diagram as SVG: one file, no fonts to embed, no network,
/// and the same picture the canvas shows. Colour is the maturity keyword and
/// the arrowheads are UML's, because a diagram that is exported has to keep
/// saying what it said on screen.
/// </summary>
public static class SvgExporter
{
    private const double Margin = 24;

    private static readonly Dictionary<string, string> MaturityColours = new(StringComparer.Ordinal)
    {
        ["implemented"] = "#2E7D32",
        ["inProgress"] = "#F9A825",
        ["writtenAhead"] = "#0277BD",
        ["planned"] = "#757575",
    };

    public static void Write(Diagram diagram, string path)
        => File.WriteAllText(path, ToSvg(diagram), new UTF8Encoding(false));

    public static string ToSvg(Diagram diagram)
    {
        var width = diagram.Nodes.Count == 0 ? 200 : diagram.Nodes.Max(n => n.X + n.Width) + (Margin * 2);
        var height = diagram.Nodes.Count == 0 ? 100 : diagram.Nodes.Max(n => n.Y + n.Height) + (Margin * 2);

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture,
            $"""<svg xmlns="http://www.w3.org/2000/svg" width="{N(width)}" height="{N(height)}" viewBox="0 0 {N(width)} {N(height)}" font-family="Segoe UI, Inter, sans-serif" font-size="12">""");
        svg.AppendLine();
        svg.AppendLine(Defs);
        svg.Append("""<rect width="100%" height="100%" fill="#ffffff"/>""");
        svg.AppendLine();
        svg.Append(CultureInfo.InvariantCulture, $"""<g transform="translate({N(Margin)},{N(Margin)})">""");
        svg.AppendLine();

        foreach (var edge in diagram.Edges)
            svg.AppendLine(Edge(edge));

        foreach (var node in diagram.Nodes)
            svg.AppendLine(Node(node));

        svg.AppendLine("</g>");
        svg.AppendLine("</svg>");
        return svg.ToString();
    }

    /// <summary>The three arrowheads UML asks for: hollow triangle, filled, open.</summary>
    private const string Defs =
        """
        <defs>
          <marker id="triangle" viewBox="0 0 10 10" refX="10" refY="5" markerWidth="10" markerHeight="10" orient="auto-start-reverse">
            <path d="M 0 0 L 10 5 L 0 10 z" fill="#ffffff" stroke="#455A64"/>
          </marker>
          <marker id="arrow" viewBox="0 0 10 10" refX="10" refY="5" markerWidth="8" markerHeight="8" orient="auto-start-reverse">
            <path d="M 0 0 L 10 5 L 0 10 z" fill="#455A64"/>
          </marker>
          <marker id="open" viewBox="0 0 10 10" refX="10" refY="5" markerWidth="9" markerHeight="9" orient="auto-start-reverse">
            <path d="M 0 0 L 10 5 L 0 10" fill="none" stroke="#455A64"/>
          </marker>
        </defs>
        """;

    private static string Node(DiagramNode node)
    {
        var colour = Colour(node.Maturity);
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture,
            $"""<g><rect x="{N(node.X)}" y="{N(node.Y)}" width="{N(node.Width)}" height="{N(node.Height)}" rx="4" fill="#ffffff" stroke="{colour}" stroke-width="2"/>""");

        var line = node.Y + 16;
        if (node.Stereotype is { Length: > 0 } stereotype)
        {
            svg.Append(CultureInfo.InvariantCulture,
                $"""<text x="{N(node.X + 8)}" y="{N(line)}" fill="#607D8B" font-size="10">{Escape(stereotype)}</text>""");
            line += 16;
        }

        svg.Append(CultureInfo.InvariantCulture,
            $"""<text x="{N(node.X + 8)}" y="{N(line)}" fill="#212121" font-weight="600">{Escape(node.Label)}</text>""");

        foreach (var feature in node.Features)
        {
            line += 16;
            svg.Append(CultureInfo.InvariantCulture,
                $"""<text x="{N(node.X + 8)}" y="{N(line)}" fill="#424242" font-size="10">{Escape(feature)}</text>""");
        }

        svg.Append("</g>");
        return svg.ToString();
    }

    private static string Edge(DiagramEdge edge)
    {
        var points = edge.Waypoints.Count >= 2
            ? edge.Waypoints
            : [Centre(edge.Source), Centre(edge.Target)];

        var path = string.Join(" ", points.Select((p, i) =>
            string.Create(CultureInfo.InvariantCulture, $"{(i == 0 ? "M" : "L")} {N(p.X)} {N(p.Y)}")));

        var dash = IsDashed(edge.Kind) ? """ stroke-dasharray="6 4" """ : " ";
        var marker = Marker(edge.Kind);
        var (X, Y) = points[points.Count / 2];
        var label = edge.Label is { Length: > 0 } text
            ? string.Create(CultureInfo.InvariantCulture,
                $"""<text x="{N(X)}" y="{N(Y - 4)}" fill="#546E7A" font-size="10" text-anchor="middle">{Escape(Shorten(text))}</text>""")
            : string.Empty;

        return string.Create(CultureInfo.InvariantCulture,
            $"""<path d="{path}" fill="none" stroke="#455A64"{dash}marker-end="url(#{marker})"/>{label}""");
    }

    /// <summary>A hollow triangle for specialization, an open arrow for a trace, filled otherwise.</summary>
    private static string Marker(RelationKind kind) => kind switch
    {
        RelationKind.Specialization or RelationKind.Redefinition => "triangle",
        RelationKind.Satisfy or RelationKind.Verify or RelationKind.Allocate
            or RelationKind.Dependency or RelationKind.Typing => "open",
        _ => "arrow",
    };

    private static bool IsDashed(RelationKind kind) => kind
        is RelationKind.Satisfy or RelationKind.Verify or RelationKind.Allocate
        or RelationKind.Dependency or RelationKind.Typing;

    private static (double X, double Y) Centre(DiagramNode node)
        => (node.X + (node.Width / 2), node.Y + (node.Height / 2));

    private static string Colour(string? maturity)
        => maturity is not null && MaturityColours.TryGetValue(maturity, out var colour) ? colour : "#455A64";

    /// <summary>An edge label is a hint, not the model: long ones are cut.</summary>
    private static string Shorten(string text, int length = 34)
        => text.Length <= length ? text : text[..length].TrimEnd() + "…";

    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}
