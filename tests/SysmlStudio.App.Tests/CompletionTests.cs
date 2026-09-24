using SysmlStudio.App.ViewModels;
using SysmlStudio.App.Views;
using SysmlStudio.Model;
using Xunit;

namespace SysmlStudio.App.Tests;

/// <summary>What the source editor offers where the caret is.</summary>
public sealed class CompletionTests
{
    private static readonly SysmlWorkspace Workspace = SysmlWorkspace.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures"));

    private static CompletionRequest? At(string textWithCaret)
    {
        var caret = textWithCaret.IndexOf('|', StringComparison.Ordinal);
        var text = textWithCaret.Remove(caret, 1);
        return SourceCompletion.At(text, caret, Workspace, SysmlHighlighting.Keywords);
    }

    private static List<string> Texts(CompletionRequest? request) => request?.Items.Select(i => i.Text).ToList() ?? [];

    [Fact]
    public void AStatementIsOfferedKeywordsAndNames()
    {
        var request = At("package P {\n    pa|");

        Assert.NotNull(request);
        Assert.Equal("pa", request.Prefix);
        Assert.Equal(16, request.Start);
        Assert.True(request.Matches);
        Assert.Contains("part", Texts(request));
        Assert.Contains("package", Texts(request));
        Assert.Contains("Vehicle", Texts(request));
    }

    [Theory]
    [InlineData("part e : |")]
    [InlineData("part e : Eng|")]
    [InlineData("part def W :> |")]
    [InlineData("part w :>> |")]
    [InlineData("part e defined by |")]
    [InlineData("part def W specializes |")]
    public void AfterATypingOnlyDefinitionsAreOffered(string text)
    {
        var offered = Texts(At(text));

        Assert.Contains("Engine", offered);
        Assert.Contains("Vehicle", offered);
        Assert.DoesNotContain("part", offered);   // a keyword
        Assert.DoesNotContain("engine", offered); // a usage, not a definition
    }

    [Fact]
    public void AfterAQualifierWhatItHoldsIsOffered()
    {
        var offered = Texts(At("part e : Sample::|"));

        Assert.Contains("Engine", offered);
        Assert.Contains("Vehicle", offered);
        Assert.DoesNotContain("part", offered);
        Assert.DoesNotContain("SampleLifecycle", offered);
    }

    [Fact]
    public void AShortNameIsInsertedQuotedWhenItIsNotAPlainWord()
        => Assert.DoesNotContain(Texts(At("pa|")), t => t.Contains('.', StringComparison.Ordinal) && !t.StartsWith('\''));

    [Theory]
    [InlineData("// part |")]
    [InlineData("/* part |")]
    [InlineData("doc /* pa| */")]
    [InlineData("part def <'P.|")]
    [InlineData("x = \"pa|")]
    [InlineData("part x[4|")]
    public void NothingIsOfferedInCommentsStringsOrNumbers(string text) => Assert.Null(At(text));

    [Fact]
    public void AClosedCommentDoesNotStopCompletion()
        => Assert.Contains("part", Texts(At("/* note */\n// line\npa|")));
}
