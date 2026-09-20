using FunQuery.Enums;
using FunQuery.Expressions;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

/// <summary>
/// The parser records where every node came from. Error positions in the analyzer rely on it.
/// </summary>
public class SourceSpanTests
{
    //           0         1         2         3
    //           0123456789012345678901234567890123
    private const string Chain = "$source([{id:1}]).$filter(id eq 1)";

    [Fact]
    public void SourceSpan_ExposesLengthAndCanBeBuiltFromATokenOrTwoSpans()
    {
        var span = new SourceSpan(4, 9);

        Assert.Equal(5, span.Length);
        Assert.Equal(new SourceSpan(4, 9), SourceSpan.From(new Token(TokenType.Identifier, 4, 9)));
        Assert.Equal(new SourceSpan(2, 9), SourceSpan.Between(new SourceSpan(2, 3), new SourceSpan(7, 9)));
    }

    [Fact]
    public void Calls_SpanFromTheirTargetToTheClosingParenthesis()
    {
        var filter = Assert.IsType<CallExpression>(QueryPipeline.Parse(Chain));
        var source = Assert.IsType<CallExpression>(filter.Target);

        Assert.Equal(new SourceSpan(0, 34), filter.Span);
        Assert.Equal(new SourceSpan(0, 17), source.Span);
        Assert.Equal(new Token(TokenType.Call, 18, 25), filter.Function);
    }

    [Fact]
    public void ArrayObjectAndNamedNodes_HaveTheirBracketsIncluded()
    {
        var filter = Assert.IsType<CallExpression>(QueryPipeline.Parse(Chain));
        var source = Assert.IsType<CallExpression>(filter.Target);
        var array = Assert.IsType<ArrayExpression>(Assert.Single(source.Arguments));
        var block = Assert.IsType<BlockExpression>(Assert.Single(array.Elements));
        var named = Assert.IsType<NamedExpression>(Assert.Single(block.Expressions));

        Assert.Equal(new SourceSpan(8, 16), array.Span);
        Assert.Equal(new SourceSpan(9, 15), block.Span);
        Assert.Equal(new SourceSpan(10, 14), named.Span);
        Assert.Equal(new SourceSpan(13, 14), named.Value.Span);
    }

    [Fact]
    public void Comparison_SpansFromItsLeftToItsRightOperand()
    {
        var filter = Assert.IsType<CallExpression>(QueryPipeline.Parse(Chain));
        var comparison = Assert.IsType<ComparisonExpression>(Assert.Single(filter.Arguments));

        Assert.Equal(new SourceSpan(26, 33), comparison.Span);
        Assert.Equal(new SourceSpan(26, 28), comparison.Left.Span);
        Assert.Equal(new SourceSpan(32, 33), comparison.Right.Span);
    }

    [Fact]
    public void LogicalExpression_SpansFromItsFirstToItsLastOperand()
    {
        var filter = Assert.IsType<CallExpression>(
            QueryPipeline.Parse("$filter(id eq 1 and id eq 2)"));
        var logical = Assert.IsType<LogicalExpression>(Assert.Single(filter.Arguments));

        Assert.Equal(new SourceSpan(8, 27), logical.Span);
        Assert.Equal(new SourceSpan(8, 15), logical.Left.Span);
        Assert.Equal(new SourceSpan(20, 27), logical.Right.Span);
    }

    [Fact]
    public void EmptyArrayAndEmptyObject_StillHaveTheirBrackets()
    {
        var source = Assert.IsType<CallExpression>(QueryPipeline.Parse("$source([{}, []])"));
        var array = Assert.IsType<ArrayExpression>(Assert.Single(source.Arguments));

        Assert.Equal(new SourceSpan(8, 16), array.Span);
        Assert.Equal(new SourceSpan(9, 11), array.Elements[0].Span);
        Assert.Equal(new SourceSpan(13, 15), array.Elements[1].Span);
    }

    [Fact]
    public void ChainOfThreeCalls_EachSpansEverythingBeforeItsClosingParenthesis()
    {
        var last = Assert.IsType<CallExpression>(
            QueryPipeline.Parse("$source([1]).$filter(a eq 1).$filter(b eq 2)"));
        var middle = Assert.IsType<CallExpression>(last.Target);
        var source = Assert.IsType<CallExpression>(middle.Target);

        Assert.Equal(new SourceSpan(0, 12), source.Span);
        Assert.Equal(new SourceSpan(0, 28), middle.Span);
        Assert.Equal(new SourceSpan(0, 44), last.Span);
    }

    [Fact]
    public void SemanticErrors_PointAtTheNodeThatCausedThem()
    {
        // The comparison "id eq 'a'" is at 26..35 in this query.
        var error = QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => QueryPipeline.Analyze("$source([{id:1}]).$filter(id eq 'a')"));

        Assert.Equal(new SourceSpan(26, 35), new SourceSpan(error.Position, error.Position + error.Length));
    }
}
