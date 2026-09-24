using FunQuery.Enums;
using FunQuery.Expressions;
using FunQuery.SemanticTypes;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

/// <summary>The literals true, false, and null, and how null combines with other types.</summary>
public class NullAndBooleanLiteralTests
{
    // ------------------------------------------------------------------
    // Lexer and parser
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("true", TokenType.BooleanLiteral)]
    [InlineData("false", TokenType.BooleanLiteral)]
    [InlineData("null", TokenType.NullLiteral)]
    public void Keywords_AreLiterals(string text, TokenType expected)
    {
        Assert.Equal(expected, QueryPipeline.Tokenize(text)[0].Type);
    }

    [Theory]
    [InlineData("TRUE")]
    [InlineData("False")]
    [InlineData("Null")]
    [InlineData("nullable")]
    [InlineData("trueish")]
    [InlineData("null1")]
    [InlineData("true_")]
    public void KeywordsAreCaseSensitive_AndOnlyWholeWords(string text)
    {
        Assert.Equal(TokenType.Identifier, QueryPipeline.Tokenize(text)[0].Type);
    }

    [Fact]
    public void Literals_ParseAsValues()
    {
        var source = Assert.IsType<CallExpression>(QueryPipeline.Parse("$source([true, false, null])"));
        var array = Assert.IsType<ArrayExpression>(Assert.Single(source.Arguments));

        Assert.Equal(
            new[] { TokenType.BooleanLiteral, TokenType.BooleanLiteral, TokenType.NullLiteral },
            array.Elements.Select(e => Assert.IsType<ValueExpression>(e).Token.Type).ToArray());
    }

    [Theory]
    [InlineData("$source([{null: 1}])")]
    [InlineData("$source([{true: 1}])")]
    [InlineData("$source([{false: 1}])")]
    public void Literals_CannotBeUsedAsFieldNames(string query)
    {
        QueryAssert.Fails(QueryErrorCode.UnexpectedToken, () => QueryPipeline.Parse(query));
    }

    // ------------------------------------------------------------------
    // Types
    // ------------------------------------------------------------------

    private static string TypeOf(string elements)
    {
        var result = QueryPipeline.Analyze($"$source([{elements}])");
        var array = Assert.IsType<ArrayType>(result.SemanticType);

        return Describe(array.Type);
    }

    private static string Describe(SemanticType type) =>
        type switch
        {
            ArrayType array => $"array<{Describe(array.Type)}>",
            ObjectType obj => "{" + string.Join(",", obj.Fields.Select(f => $"{f.Key}:{Describe(f.Value)}")) + "}",
            _ => type.Name,
        };

    [Theory]
    [InlineData("true", "bool")]
    [InlineData("true, false", "bool")]
    [InlineData("null", "null")]
    [InlineData("null, null", "null")]
    public void Literals_HaveTheirOwnTypes(string elements, string expected)
    {
        Assert.Equal(expected, TypeOf(elements));
    }

    [Theory]
    [InlineData("null, 'a'", "string")]
    [InlineData("'a', null", "string")]
    [InlineData("1, null, 2", "int")]
    [InlineData("null, 1.5", "decimal")]
    [InlineData("true, null", "bool")]
    [InlineData("null, [1]", "array<int>")]
    [InlineData("[1], null", "array<int>")]
    [InlineData("[], [1]", "array<int>")]
    [InlineData("[1], []", "array<int>")]
    [InlineData("{id: 1}, {id: null}", "{id:int}")]
    [InlineData("{id: null}, {id: 1}", "{id:int}")]
    [InlineData("{id: 1, name: 'a'}, {id: null, name: null}", "{id:int,name:string}")]
    [InlineData("{a: null}", "{a:null}")]
    public void Null_UnifiesWithAnyOtherElementType(string elements, string expected)
    {
        Assert.Equal(expected, TypeOf(elements));
    }

    [Theory]
    [InlineData("1, 'a', null")]
    [InlineData("null, 1, 'a'")]
    [InlineData("{id: 1}, {id: 'x'}")]
    [InlineData("{id: 1, name: 'a'}, {id: 'x', name: null}")]
    [InlineData("[1], ['a']")]
    public void Null_DoesNotMakeIncompatibleElementsCompatible(string elements)
    {
        QueryAssert.Fails(
            QueryErrorCode.IncompatibleElementTypes,
            () => QueryPipeline.Analyze($"$source([{elements}])"));
    }

    [Fact]
    public void NullField_TakesTheTypeOfTheOtherRows_SoItCanBeCompared()
    {
        var result = QueryPipeline.Analyze(
            "$source([{name: null}, {name: 'a'}]).$filter(name gt 'a')");

        Assert.Equal("array", result.SemanticType!.Name);
    }

    // ------------------------------------------------------------------
    // Operators with null
    // ------------------------------------------------------------------

    private static BaseExpression Filter(string predicate) =>
        QueryPipeline.Analyze("$source([{id: 1, name: 'a', on: true}]).$filter(" + predicate + ")");

    [Theory]
    [InlineData("id eq null")]
    [InlineData("null neq id")]
    [InlineData("name eq null")]
    [InlineData("on eq null")]
    [InlineData("null eq null")]
    [InlineData("1 neq null")]
    public void NullCanBeTestedWithEqAndNeq_AgainstAnyScalar(string predicate)
    {
        Assert.NotNull(Filter(predicate).SemanticType);
    }

    [Theory]
    [InlineData("id gt null", "gt")]
    [InlineData("null lt id", "lt")]
    [InlineData("name gte null", "gte")]
    [InlineData("null lte null", "lte")]
    public void NullHasNoOrder_SoOrderingWithTheNullLiteralIsRejected(string predicate, string op)
    {
        // "$source([{id: 1, name: 'a', on: true}]).$filter(" is 48 characters long.
        var error = QueryAssert.Fails(QueryErrorCode.TypeMismatch, () => Filter(predicate));

        Assert.Contains($"Operator '{op}' cannot be applied to null", error.Message);
        Assert.Equal(48, error.Position);
        Assert.Equal(predicate.Length, error.Length);
    }

    [Fact]
    public void ANullPredicate_IsNotABoolean()
    {
        var error = QueryAssert.Fails(QueryErrorCode.TypeMismatch, () => Filter("null"));

        Assert.Contains("expects bool, got null", error.Message);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("on eq true")]
    [InlineData("on and true")]
    public void BooleanLiterals_AreValidPredicates(string predicate)
    {
        Assert.NotNull(Filter(predicate).SemanticType);
    }

    [Fact]
    public void ArrayAndObjectValues_CannotBeComparedWithNull()
    {
        var registry = QueryPipeline.CoreRegistry();

        QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => QueryPipeline.Analyze(
                "$source([{tags: ['a']}]).$filter(tags eq null)",
                functions: registry));
    }
}
