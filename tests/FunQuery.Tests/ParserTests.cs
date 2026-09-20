using FunQuery.Enums;
using FunQuery.Expressions;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

public class ParserTests
{
    private static void AssertParseFails(QueryErrorCode expected, string input) =>
        QueryAssert.Fails(expected, () => QueryPipeline.Parse(input));

    // ------------------------------------------------------------------
    // Structure
    // ------------------------------------------------------------------

    [Fact]
    public void FirstCallInAChain_HasNoTarget()
    {
        var call = Assert.IsType<CallExpression>(QueryPipeline.Parse("$source([1])"));

        Assert.Null(call.Target);
        Assert.Single(call.Arguments);
    }

    [Fact]
    public void ChainedCalls_AreNestedThroughTarget()
    {
        var filter = Assert.IsType<CallExpression>(
            QueryPipeline.Parse("$source([1]).$filter(id eq 1)"));

        var source = Assert.IsType<CallExpression>(filter.Target);
        Assert.Null(source.Target);
        Assert.IsType<ComparisonExpression>(Assert.Single(filter.Arguments));
    }

    [Fact]
    public void And_BindsTighterThanOr()
    {
        var call = Assert.IsType<CallExpression>(
            QueryPipeline.Parse("$filter(id eq 1 or id eq 2 and id eq 3)"));

        var root = Assert.IsType<LogicalExpression>(Assert.Single(call.Arguments));
        Assert.IsType<ComparisonExpression>(root.Left);

        var right = Assert.IsType<LogicalExpression>(root.Right);
        Assert.IsType<ComparisonExpression>(right.Left);
        Assert.IsType<ComparisonExpression>(right.Right);
    }

    [Fact]
    public void Parentheses_OverridePrecedence()
    {
        var call = Assert.IsType<CallExpression>(
            QueryPipeline.Parse("$filter((id eq 1 or id eq 2) and id eq 3)"));

        var root = Assert.IsType<LogicalExpression>(Assert.Single(call.Arguments));
        Assert.IsType<LogicalExpression>(root.Left);
        Assert.IsType<ComparisonExpression>(root.Right);
    }

    [Fact]
    public void ArrayAndObjectLiterals_AreParsed()
    {
        var call = Assert.IsType<CallExpression>(
            QueryPipeline.Parse("$source([{id: 1}, {id: 2}])"));

        var array = Assert.IsType<ArrayExpression>(Assert.Single(call.Arguments));
        Assert.Equal(2, array.Elements.Count);
        Assert.All(array.Elements, element => Assert.IsType<BlockExpression>(element));
    }

    [Fact]
    public void EmptyArrayAndEmptyObject_AreAccepted()
    {
        QueryPipeline.Parse("$source([])");
        QueryPipeline.Parse("$source([{}])");
    }

    // ------------------------------------------------------------------
    // Errors
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("$source(")]
    [InlineData("$source([1")]
    [InlineData("$source([1]")]
    [InlineData("$source([]).")]
    [InlineData("$source([{id:")]
    public void InputThatEndsTooEarly_ReportsUnexpectedEndOfInput(string input)
    {
        AssertParseFails(QueryErrorCode.UnexpectedEndOfInput, input);
    }

    [Theory]
    [InlineData("$source([]))")]
    [InlineData("$source([1,])")]
    [InlineData("$source([{id: 1,}])")]
    [InlineData("$source([{1}])")]
    [InlineData("$source([{id 1}])")]
    public void MisplacedTokens_ReportUnexpectedToken(string input)
    {
        AssertParseFails(QueryErrorCode.UnexpectedToken, input);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("'text'")]
    [InlineData("id")]
    [InlineData("[1, 2]")]
    [InlineData("{id: 1}")]
    [InlineData("id eq 1")]
    [InlineData("1.$filter(id eq 1)")]
    public void TopLevelExpressionThatIsNotAFunctionCall_IsRejected(string input)
    {
        AssertParseFails(QueryErrorCode.ExpressionOutsideFunction, input);
    }

    // ------------------------------------------------------------------
    // Depth
    // ------------------------------------------------------------------

    private static string NestedArrays(int depth) =>
        "$source(" + new string('[', depth) + "1" + new string(']', depth) + ")";

    [Fact]
    public void DeeplyNestedArrays_ExceedDefaultMaxDepth()
    {
        AssertParseFails(QueryErrorCode.MaxDepthExceeded, NestedArrays(100));
    }

    [Fact]
    public void NestingWithinACustomLimit_IsAccepted()
    {
        var limits = new QueryLimits { MaxDepth = 20 };

        QueryPipeline.Parse(NestedArrays(10), limits);
    }

    [Fact]
    public void NestingBeyondACustomLimit_IsRejected()
    {
        var limits = new QueryLimits { MaxDepth = 20 };

        QueryAssert.Fails(
            QueryErrorCode.MaxDepthExceeded,
            () => QueryPipeline.Parse(NestedArrays(30), limits));
    }
}
