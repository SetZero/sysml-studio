using Antlr4.Runtime;
using SysmlStudio.Syntax.Generated;

namespace SysmlStudio.Syntax;

/// <summary>A syntax error, as the parser reported it.</summary>
public sealed record SyntaxError(string File, int Line, int Column, string Message)
{
    public override string ToString() => $"{File}:{Line}:{Column}: {Message}";
}

/// <summary>
/// One parsed .sysml file: its text, its token stream (comments and whitespace
/// included, on the hidden channel) and its parse tree. The text is the source
/// of truth; edits are patches over this token stream.
/// </summary>
public sealed class SourceFile
{
    private SourceFile(string path, string text, CommonTokenStream tokens,
                       SysMLv2Parser.RootNamespaceContext tree, IReadOnlyList<SyntaxError> errors)
    {
        Path = path;
        Text = text;
        Tokens = tokens;
        Tree = tree;
        Errors = errors;
    }

    public string Path { get; }
    public string Text { get; }
    public CommonTokenStream Tokens { get; }
    public SysMLv2Parser.RootNamespaceContext Tree { get; }
    public IReadOnlyList<SyntaxError> Errors { get; }
    public bool IsValid => Errors.Count == 0;

    public static SourceFile Parse(string path) => ParseText(path, File.ReadAllText(path));

    public static SourceFile ParseText(string path, string text)
    {
        var errors = new List<SyntaxError>();
        var listener = new CollectingErrorListener(path, errors);

        var lexer = new SysMLv2Lexer(CharStreams.fromString(text));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(listener);

        var tokens = new CommonTokenStream(lexer);
        var parser = new SysMLv2Parser(tokens);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(listener);

        var tree = parser.rootNamespace();
        return new SourceFile(path, text, tokens, tree, errors);
    }

    private sealed class CollectingErrorListener(string path, List<SyntaxError> errors) : BaseErrorListener, IAntlrErrorListener<int>
    {
        private readonly string _path = path;
        private readonly List<SyntaxError> _errors = errors;

        public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
                                         int line, int charPositionInLine, string msg, RecognitionException e)
            => _errors.Add(new SyntaxError(_path, line, charPositionInLine, msg));

        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol,
                                int line, int charPositionInLine, string msg, RecognitionException e)
            => _errors.Add(new SyntaxError(_path, line, charPositionInLine, msg));
    }
}
