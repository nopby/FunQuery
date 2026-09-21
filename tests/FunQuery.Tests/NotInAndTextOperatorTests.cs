using FunQuery.Enums;
using FunQuery.Expressions;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

/// <summary>not, in, contains, startswith, and endswith.</summary>
public class NotInAndTextOperatorTests
{
    private static readonly QueryEngine Engine = new();

    private static string Run(string query) => Engine.Execute(query).ToJson();

    // ------------------------------------------------------------------
    // Lexer
    // ------------------------------------------------------------------

    [Fact]
    public void Not_IsItsOwnToken()
    {
        Assert.Equal(TokenType.NotOperator, QueryPipeline.Tokenize("not")[0].Type);
    }

    [Theory]
    [InlineData("in")]
    [InlineData("contains")]
    [InlineData("startswith")]
    [InlineData("endswith")]
    public void TheNewInfixWords_AreComparisonOperators(string word)
    {
        Assert.Equal(TokenType.ComparisonOperator, QueryPipeline.Tokenize(word)[0].Type);
    }

    [Theory]
    [InlineData("NOT")]
    [InlineData("In")]
    [InlineData("Contains")]
    [InlineData("STARTSWITH")]
    [InlineData("nothing")]
    [InlineData("inside")]
    [InlineData("contained")]
    [InlineData("startswith2")]
    [InlineData("endswith_")]
    public void TheNewWords_AreCaseSensitiveAndOnlyWholeWords(string text)
    {
        Assert.Equal(TokenType.Identifier, QueryPipeline.Tokenize(text)[0].Type);
    }

    [Theory]
    [InlineData("not")]
    [InlineData("in")]
    [InlineData("contains")]
    [InlineData("startswith")]
    [InlineData("endswith")]
    public void TheNewWords_CannotBeUnquotedFieldNames_ButCanBeQuotedOnes(string word)
    {
        QueryAssert.Fails(
            QueryErrorCode.UnexpectedToken,
            () => QueryPipeline.Parse($"$source([{{{word}: 1}}])"));

        Assert.NotNull(QueryPipeline.Analyze($"$source([{{'{word}': 1}}])").SemanticType);
    }

    // ------------------------------------------------------------------
    // Parser
    // ------------------------------------------------------------------

    private static BaseExpression PredicateOf(string predicate)
    {
        var call = Assert.IsType<CallExpression>(QueryPipeline.Parse("$filter(" + predicate + ")"));

        return Assert.Single(call.Arguments);
    }

    [Fact]
    public void Not_WrapsTheWholeComparison()
    {
        var not = Assert.IsType<NotExpression>(PredicateOf("not id eq 1"));

        var comparison = Assert.IsType<ComparisonExpression>(not.Operand);
        Assert.Equal(ComparisonOperator.Equal, comparison.Operator);
    }

    [Fact]
    public void Not_BindsTighterThanAndAndOr()
    {
        var and = Assert.IsType<LogicalExpression>(PredicateOf("not a eq 1 and b eq 2"));

        Assert.IsType<NotExpression>(and.Left);
        Assert.IsType<ComparisonExpression>(and.Right);

        var or = Assert.IsType<LogicalExpression>(PredicateOf("a eq 1 or not b eq 2 and c eq 3"));

        Assert.Equal(LogicalOperator.Or, or.Operator);
        var right = Assert.IsType<LogicalExpression>(or.Right);
        Assert.IsType<NotExpression>(right.Left);
    }

    [Fact]
    public void Not_CanBeRepeated_AndGroupedWithParentheses()
    {
        var outer = Assert.IsType<NotExpression>(PredicateOf("not not a eq 1"));
        Assert.IsType<NotExpression>(outer.Operand);

        var grouped = Assert.IsType<NotExpression>(PredicateOf("not (a eq 1 or b eq 2)"));
        Assert.IsType<LogicalExpression>(grouped.Operand);
    }

    [Fact]
    public void Not_SpansFromItsKeywordToTheEndOfItsOperand()
    {
        // "$filter(" is 8 characters, so "not id eq 1" is 8..19.
        var not = Assert.IsType<NotExpression>(PredicateOf("not id eq 1"));

        Assert.Equal(new SourceSpan(8, 19), not.Span);
        Assert.Equal(new SourceSpan(12, 19), not.Operand.Span);
    }

    [Theory]
    [InlineData("id in [1, 2]", ComparisonOperator.In)]
    [InlineData("name contains 'a'", ComparisonOperator.Contains)]
    [InlineData("name startswith 'a'", ComparisonOperator.StartsWith)]
    [InlineData("name endswith 'a'", ComparisonOperator.EndsWith)]
    public void TheNewInfixWords_ParseAsComparisonExpressions(string predicate, ComparisonOperator expected)
    {
        var comparison = Assert.IsType<ComparisonExpression>(PredicateOf(predicate));

        Assert.Equal(expected, comparison.Operator);
    }

    [Theory]
    [InlineData("a contains 'x' contains 'y'")]
    [InlineData("a in [1] in [2]")]
    [InlineData("a eq 1 startswith 'x'")]
    public void TheNewInfixWords_DoNotChain(string predicate)
    {
        QueryAssert.Fails(
            QueryErrorCode.UnexpectedToken,
            () => QueryPipeline.Parse("$filter(" + predicate + ")"));
    }

    [Theory]
    [InlineData("id eq not 1")]
    [InlineData("id in not [1]")]
    public void Not_IsNotAnOperandOfAComparison(string predicate)
    {
        QueryAssert.Fails(
            QueryErrorCode.UnexpectedToken,
            () => QueryPipeline.Parse("$filter(" + predicate + ")"));
    }

    [Fact]
    public void Not_WithoutAnOperand_IsRejected()
    {
        QueryAssert.Fails(QueryErrorCode.UnexpectedToken, 11, 1, () => QueryPipeline.Parse("$filter(not)"));
        QueryAssert.Fails(QueryErrorCode.UnexpectedEndOfInput, () => QueryPipeline.Parse("$filter(not"));
    }

    // ------------------------------------------------------------------
    // Analysis errors
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("$source([{id:1}]).$filter(not id)", 30, 2)]
    [InlineData("$source([{id:1}]).$filter(not 'a')", 30, 3)]
    public void Not_NeedsABoolean(string query, int position, int length)
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            position,
            length,
            () => QueryPipeline.Analyze(query));

        Assert.Contains("Operand of 'not' must be Boolean", error.Message);
    }

    [Theory]
    [InlineData("id in 1", "needs an array on the right side, got int")]
    [InlineData("id in [[1]]", "cannot be applied to an array of array")]
    [InlineData("[1] in [1]", "cannot be applied to array")]
    [InlineData("id in ['a']", "Cannot compare int with string")]
    [InlineData("id in [true]", "Cannot compare int with bool")]
    public void In_ChecksBothSides(string predicate, string message)
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => QueryPipeline.Analyze("$source([{id:1}]).$filter(" + predicate + ")"));

        Assert.Contains(message, error.Message);
    }

    [Theory]
    [InlineData("id contains 'x'", "got int")]
    [InlineData("name contains 1", "got int")]
    [InlineData("name startswith true", "got bool")]
    [InlineData("name endswith [1]", "got array")]
    public void TextOperators_NeedStrings(string predicate, string message)
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => QueryPipeline.Analyze("$source([{id:1, name:'a'}]).$filter(" + predicate + ")"));

        Assert.Contains("requires string operands", error.Message);
        Assert.Contains(message, error.Message);
    }

    [Theory]
    [InlineData("id in []")]
    [InlineData("id in [1, 2]")]
    [InlineData("id in [1.5, 2.0]")]
    [InlineData("null in [1]")]
    [InlineData("id in [1, null]")]
    [InlineData("name contains 'a'")]
    [InlineData("name contains null")]
    [InlineData("null startswith name")]
    [InlineData("not id eq 1")]
    [InlineData("not name contains 'a' and id in [1]")]
    public void ValidUses_AreAccepted(string predicate)
    {
        Assert.NotNull(
            QueryPipeline.Analyze("$source([{id:1, name:'a'}]).$filter(" + predicate + ")").SemanticType);
    }

    // ------------------------------------------------------------------
    // Execution: not
    // ------------------------------------------------------------------

    private const string Three = "[{id: 1}, {id: 2}, {id: 3}]";

    [Theory]
    [InlineData("not id eq 2", """[{"id":1},{"id":3}]""")]
    [InlineData("not not id eq 2", """[{"id":2}]""")]
    [InlineData("not id eq 1 and not id eq 2", """[{"id":3}]""")]
    [InlineData("not (id eq 1 or id eq 2)", """[{"id":3}]""")]
    [InlineData("not id gt 5", """[{"id":1},{"id":2},{"id":3}]""")]
    [InlineData("not true", "[]")]
    [InlineData("not false", """[{"id":1},{"id":2},{"id":3}]""")]
    public void Not_NegatesItsOperand(string predicate, string expected)
    {
        Assert.Equal(expected, Run($"$source({Three}).$filter({predicate})"));
    }

    [Fact]
    public void Not_OfANullBoolean_IsTrue()
    {
        // A null boolean counts as false, so its negation is true.
        Assert.Equal(
            """[{"on":null}]""",
            Run("$source([{on: true}, {on: null}]).$filter(not on)"));
    }

    // ------------------------------------------------------------------
    // Execution: in
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("id in [1, 3]", """[{"id":1},{"id":3}]""")]
    [InlineData("not id in [1, 3]", """[{"id":2}]""")]
    [InlineData("id in []", "[]")]
    [InlineData("id in [2]", """[{"id":2}]""")]
    [InlineData("id in [1.0, 2.5]", """[{"id":1}]""")]
    [InlineData("id in [4, 5]", "[]")]
    [InlineData("null in [1]", "[]")]
    public void In_TestsMembership(string predicate, string expected)
    {
        Assert.Equal(expected, Run($"$source({Three}).$filter({predicate})"));
    }

    [Fact]
    public void In_OverStrings_IsCaseSensitive()
    {
        Assert.Equal(
            """[{"name":"apple"}]""",
            Run("$source([{name: 'apple'}, {name: 'Banana'}]).$filter(name in ['apple', 'banana'])"));
    }

    [Fact]
    public void In_CanSearchAnArrayFieldOfTheRow()
    {
        Assert.Equal(
            """[{"id":1,"tags":["a","b"]}]""",
            Run("$source([{id: 1, tags: ['a', 'b']}, {id: 2, tags: ['c']}]).$filter('a' in tags)"));
    }

    [Fact]
    public void In_TreatsNullLikeEq_SoNullIsAMemberOnlyIfTheArrayHasNull()
    {
        const string data = "[{id: 1}, {id: null}]";

        Assert.Equal("""[{"id":null}]""", Run($"$source({data}).$filter(id in [null])"));
        Assert.Equal("""[{"id":1},{"id":null}]""", Run($"$source({data}).$filter(id in [1, null])"));
        Assert.Equal("""[{"id":1}]""", Run($"$source({data}).$filter(id in [1])"));
    }

    // ------------------------------------------------------------------
    // Execution: contains, startswith, endswith
    // ------------------------------------------------------------------

    private const string Names = "[{name: 'apple'}, {name: 'Banana'}, {name: null}]";

    [Theory]
    [InlineData("name contains 'an'", """[{"name":"Banana"}]""")]
    [InlineData("name contains 'AN'", "[]")]
    [InlineData("name startswith 'ap'", """[{"name":"apple"}]""")]
    [InlineData("name startswith 'AP'", "[]")]
    [InlineData("name endswith 'e'", """[{"name":"apple"}]""")]
    [InlineData("name endswith 'a'", """[{"name":"Banana"}]""")]
    [InlineData("name contains 'zzz'", "[]")]
    [InlineData("name contains ''", """[{"name":"apple"},{"name":"Banana"}]""")]
    [InlineData("name startswith ''", """[{"name":"apple"},{"name":"Banana"}]""")]
    [InlineData("name contains null", "[]")]
    [InlineData("'apple pie' contains name", """[{"name":"apple"}]""")]
    public void TextOperators_AreOrdinalAndCaseSensitive_AndNullGivesFalse(string predicate, string expected)
    {
        Assert.Equal(expected, Run($"$source({Names}).$filter({predicate})"));
    }

    [Fact]
    public void TextOperators_CanTakeTheFieldOnTheRightSide()
    {
        Assert.Equal(
            """[{"text":"apple pie","part":"pie"}]""",
            Run("$source([{text: 'apple pie', part: 'pie'}, {text: 'apple', part: 'pie'}]).$filter(text endswith part)"));
    }

    [Fact]
    public void Not_OverATextOperator_IsTrueForNull()
    {
        Assert.Equal(
            """[{"name":"apple"},{"name":null}]""",
            Run("$source([{name: 'apple'}, {name: null}]).$filter(not name contains 'x')"));
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    public void WildcardCharacters_AreMatchedLiterally(string special)
    {
        // A provider that translates to SQL LIKE must escape these; in memory they are ordinary text.
        var query = $"$source([{{name: 'a{special}b'}}, {{name: 'axb'}}]).$filter(name contains '{special}')";

        Assert.Equal($$"""[{"name":"a{{special}}b"}]""", Run(query));
    }

    [Fact]
    public void ABackslash_IsAnOrdinaryCharacter()
    {
        var query = """$source([{name: 'a\b'}, {name: 'axb'}]).$filter(name contains '\')""";

        Assert.Equal("""[{"name":"a\\b"}]""", Run(query));
    }

    [Fact]
    public void SeveralOperatorsCombine()
    {
        Assert.Equal(
            """[{"id":2,"name":"Banana"}]""",
            Run("$source([{id: 1, name: 'apple'}, {id: 2, name: 'Banana'}, {id: 3, name: 'cherry'}])" +
                ".$filter(not name startswith 'a' and id in [2, 3] and name endswith 'a')"));
    }
}
