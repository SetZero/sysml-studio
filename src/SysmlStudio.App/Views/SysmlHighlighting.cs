using System.Globalization;
using System.Security;
using System.Text;
using System.Xml;
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using SysmlStudio.Syntax.Generated;

namespace SysmlStudio.App.Views;

/// <summary>
/// Syntax colouring for the source editor, built from the lexer the parser
/// uses: every keyword the grammar defines is a keyword here, so a grammar
/// re-vendored with new keywords colours them with no change to this file.
/// </summary>
public static class SysmlHighlighting
{
    private static readonly Lazy<IReadOnlyList<string>> KeywordList = new(ReadKeywords);

    /// <summary>The grammar's keywords: every literal token that is a plain word.</summary>
    public static IReadOnlyList<string> Keywords => KeywordList.Value;

    /// <summary>
    /// The colouring, in the theme's colours. <paramref name="type"/> colours
    /// the name after a typing or specialization (": Motor", ":> Sensor"),
    /// the way an IDE colours the types in a declaration.
    /// </summary>
    public static IHighlightingDefinition Create(Color keyword, Color comment, Color text, Color number, Color metadata, Color type)
    {
        var xshd = new StringBuilder();
        xshd.Append("""<SyntaxDefinition name="SysML" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">""");
        xshd.Append(CultureInfo.InvariantCulture, $"""<Color name="Keyword" foreground="{Hex(keyword)}" />""");
        xshd.Append(CultureInfo.InvariantCulture, $"""<Color name="Comment" foreground="{Hex(comment)}" fontStyle="italic" />""");
        xshd.Append(CultureInfo.InvariantCulture, $"""<Color name="String" foreground="{Hex(text)}" />""");
        xshd.Append(CultureInfo.InvariantCulture, $"""<Color name="Number" foreground="{Hex(number)}" />""");
        xshd.Append(CultureInfo.InvariantCulture, $"""<Color name="Metadata" foreground="{Hex(metadata)}" />""");
        xshd.Append(CultureInfo.InvariantCulture, $"""<Color name="Type" foreground="{Hex(type)}" />""");
        xshd.Append("<RuleSet>");
        xshd.Append("""<Span color="Comment" begin="//" />""");
        xshd.Append("""<Span color="Comment" multiline="true" begin="/\*" end="\*/" />""");
        xshd.Append("""<Span color="String" begin="&quot;" end="&quot;" />""");
        xshd.Append("""<Span color="String" begin="'" end="'" />""");
        xshd.Append("""<Keywords color="Keyword">""");
        foreach (var word in Keywords)
            xshd.Append(CultureInfo.InvariantCulture, $"<Word>{SecurityElement.Escape(word)}</Word>");
        xshd.Append("</Keywords>");
        xshd.Append("""<Rule color="Metadata">[#@][A-Za-z_][A-Za-z0-9_]*</Rule>""");
        xshd.Append("""<Rule color="Type">(?&lt;=(?&lt;!:):&gt;?&gt;?\s*)[A-Za-z_][A-Za-z0-9_]*(::[A-Za-z_][A-Za-z0-9_]*)*</Rule>""");
        xshd.Append("""<Rule color="Number">\b[0-9]+(\.[0-9]+)?\b</Rule>""");
        xshd.Append("</RuleSet></SyntaxDefinition>");

        using var reader = XmlReader.Create(new StringReader(xshd.ToString()));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    private static List<string> ReadKeywords()
    {
        var vocabulary = (Antlr4.Runtime.Vocabulary)SysMLv2Lexer.DefaultVocabulary;
        var words = new SortedSet<string>(StringComparer.Ordinal);
        for (var type = 1; type <= vocabulary.getMaxTokenType(); type++)
        {
            var literal = vocabulary.GetLiteralName(type);
            if (literal is null || literal.Length < 3)
                continue;

            var word = literal.Trim('\'');
            if (word.All(char.IsLetter))
                words.Add(word);
        }

        return [.. words];
    }

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
