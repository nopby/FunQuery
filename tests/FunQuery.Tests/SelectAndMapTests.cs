using FunQuery.Enums;
using FunQuery.SemanticTypes;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

/// <summary>$select (list and object form) and $map. Contract in docs/Functions.md.</summary>
public class SelectAndMapTests
{
    private static readonly QueryEngine Engine = new();

    private static string Run(string query) => Engine.Execute(query).ToJson();

    // ------------------------------------------------------------------
    // $map: analysis
    // ------------------------------------------------------------------

    [Fact]
    public void MapReturnsAnArrayOfTheExpressionsType()
    {
        var scalar = QueryPipeline.Analyze("$source([{id:1}]).$map(id)");
        var boolean = QueryPipeline.Analyze("$source([1,2]).$map(~ gt 1)");

        Assert.Equal("int", Assert.IsType<ArrayType>(scalar.SemanticType).Type.Name);
        Assert.Equal("bool", Assert.IsType<ArrayType>(boolean.SemanticType).Type.Name);
    }

    [Fact]
    public void MapRequiresAnArrayTarget()
    {
        QueryAssert.Fails(QueryErrorCode.InvalidTarget, () => QueryPipeline.Analyze("$map(~)"));
    }

    [Fact]
    public void MapWithAnObjectLiteral_BehavesLikeSelectsObjectForm()
    {
        var result = QueryPipeline.Analyze("$source([{id:1,name:'a'}]).$map({id: id, upper: name})");

        var element = Assert.IsType<ObjectType>(Assert.IsType<ArrayType>(result.SemanticType).Type);

        Assert.Equal("int", element.Fields["id"].Name);
        Assert.Equal("string", element.Fields["upper"].Name);
    }

    // ------------------------------------------------------------------
    // $map: execution
    // ------------------------------------------------------------------

    [Fact]
    public void MapProjectsEveryElementInOrder()
    {
        Assert.Equal("[false,true,true]", Run("$source([1, 2, 3]).$map(~ gt 1)"));
    }

    [Fact]
    public void MapOfAField_ProducesAnArrayOfScalars()
    {
        Assert.Equal(
            """["a","b"]""",
            Run("$source([{id: 1, name: 'a'}, {id: 2, name: 'b'}]).$map(name)"));
    }

    [Fact]
    public void MapCanProduceNullElements()
    {
        Assert.Equal(
            "[1,null,3]",
            Run("$source([{n: 1}, {n: null}, {n: 3}]).$map(n)"));
    }

    [Fact]
    public void MapOfAnEmptyArray_IsAnEmptyArray()
    {
        Assert.Equal("[]", Run("$source([]).$map(true)"));
    }

    [Fact]
    public void MapCanBeChainedWithFilter()
    {
        Assert.Equal(
            "[2,3]",
            Run("$source([1, 2, 3]).$filter(~ gt 1).$map(~)"));
    }

    // ------------------------------------------------------------------
    // $select: list form, analysis
    // ------------------------------------------------------------------

    [Fact]
    public void SelectListForm_ProducesAnObjectWithThoseFields()
    {
        var result = QueryPipeline.Analyze("$source([{id: 1, name: 'a'}]).$select(id, name)");

        var element = Assert.IsType<ObjectType>(Assert.IsType<ArrayType>(result.SemanticType).Type);

        Assert.Equal(2, element.Fields.Count);
        Assert.Equal("int", element.Fields["id"].Name);
        Assert.Equal("string", element.Fields["name"].Name);
    }

    [Fact]
    public void SelectListForm_TakesTheLastSegmentOfAPathAsTheKey()
    {
        var result = QueryPipeline.Analyze(
            "$source([{id: 1, addr: {city: 'x'}}]).$select(id, addr.city)");

        var element = Assert.IsType<ObjectType>(Assert.IsType<ArrayType>(result.SemanticType).Type);

        Assert.True(element.Fields.ContainsKey("city"));
        Assert.False(element.Fields.ContainsKey("addr"));
    }

    [Theory]
    [InlineData("~")]
    [InlineData("$field('id')")]
    [InlineData("1")]
    [InlineData("@x")]
    [InlineData("id eq 1")]
    public void SelectListForm_RejectsAnythingThatIsNotAPlainFieldReference(string argument)
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.InvalidSelectArgument,
            () => QueryPipeline.Analyze($"$let(@x, 1).$source([{{id:1}}]).$select(id, {argument})"));

        Assert.Contains("$select({...})", error.Message);
    }

    [Fact]
    public void SelectListForm_RejectsDuplicateFieldNames()
    {
        QueryAssert.Fails(
            QueryErrorCode.DuplicateField,
            () => QueryPipeline.Analyze("$source([{id:1}]).$select(id, id)"));
    }

    [Fact]
    public void SelectListForm_RejectsTwoPathsEndingInTheSameSegment()
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.DuplicateField,
            () => QueryPipeline.Analyze(
                "$source([{a: {city: 'x'}, b: {city: 'y'}}]).$select(a.city, b.city)"));

        Assert.Contains("'city'", error.Message);
    }

    [Fact]
    public void SelectRequiresAtLeastOneArgument()
    {
        QueryAssert.Fails(
            QueryErrorCode.InvalidArgumentCount,
            () => QueryPipeline.Analyze("$source([{id:1}]).$select()"));
    }

    [Fact]
    public void SelectRequiresAnArrayTarget()
    {
        QueryAssert.Fails(QueryErrorCode.InvalidTarget, () => QueryPipeline.Analyze("$select(id)"));
    }

    [Fact]
    public void MixingListAndObjectForms_IsRejectedAsListForm()
    {
        // The second argument, an object literal, is not a plain field reference.
        QueryAssert.Fails(
            QueryErrorCode.InvalidSelectArgument,
            () => QueryPipeline.Analyze("$source([{id:1,name:'a'}]).$select(id, {name: name})"));
    }

    // ------------------------------------------------------------------
    // $select: object form, analysis
    // ------------------------------------------------------------------

    [Fact]
    public void SelectObjectForm_LetsFieldsBeRenamed()
    {
        var result = QueryPipeline.Analyze(
            "$source([{id: 1, name: 'a'}]).$select({myId: id, label: name})");

        var element = Assert.IsType<ObjectType>(Assert.IsType<ArrayType>(result.SemanticType).Type);

        Assert.Equal("int", element.Fields["myId"].Name);
        Assert.Equal("string", element.Fields["label"].Name);
    }

    [Fact]
    public void SelectObjectForm_AcceptsTildeFieldAndFieldFunctionAsValues()
    {
        var result = QueryPipeline.Analyze(
            "$source([{id: 1, name: 'a'}]).$select({row: ~, id: id, viaField: $field('name')})");

        Assert.Equal("array", result.SemanticType!.Name);
    }

    [Fact]
    public void SelectObjectForm_CanReferenceAVariable()
    {
        var result = QueryPipeline.Analyze(
            "$let(@tag, 'x').$source([{id: 1}]).$select({id: id, tag: @tag})");

        var element = Assert.IsType<ObjectType>(Assert.IsType<ArrayType>(result.SemanticType).Type);

        Assert.Equal("string", element.Fields["tag"].Name);
    }

    [Fact]
    public void SelectObjectForm_RejectsDuplicateKeys()
    {
        QueryAssert.Fails(
            QueryErrorCode.DuplicateField,
            () => QueryPipeline.Analyze("$source([{id:1}]).$select({id: id, id: id})"));
    }

    // ------------------------------------------------------------------
    // $select: execution
    // ------------------------------------------------------------------

    [Fact]
    public void SelectListForm_ProjectsEachRow()
    {
        Assert.Equal(
            """[{"id":1,"name":"a"},{"id":2,"name":"b"}]""",
            Run("$source([{id: 1, name: 'a', extra: 'x'}, {id: 2, name: 'b', extra: 'y'}])" +
                ".$select(id, name)"));
    }

    [Fact]
    public void SelectListForm_KeepsTheOrderArgumentsWereGivenIn()
    {
        Assert.Equal(
            """[{"name":"a","id":1}]""",
            Run("$source([{id: 1, name: 'a'}]).$select(name, id)"));
    }

    [Fact]
    public void SelectListForm_FlattensANestedPath()
    {
        Assert.Equal(
            """[{"id":1,"city":"x"}]""",
            Run("$source([{id: 1, addr: {city: 'x'}}]).$select(id, addr.city)"));
    }

    [Fact]
    public void SelectListForm_MissingFieldReadsAsNull()
    {
        Assert.Equal(
            """[{"id":1,"name":null},{"id":2,"name":"b"}]""",
            Run("$source([{id: 1}, {id: 2, name: 'b'}]).$select(id, name)"));
    }

    [Fact]
    public void SelectObjectForm_ComputesEachValuePerRow()
    {
        Assert.Equal(
            """[{"myId":1,"label":"a"}]""",
            Run("$source([{id: 1, name: 'a'}]).$select({myId: id, label: name})"));
    }

    [Fact]
    public void SelectObjectForm_CanKeepTheWholeElementUnderOneKey()
    {
        Assert.Equal(
            """[{"row":1},{"row":2}]""",
            Run("$source([1, 2]).$select({row: ~})"));
    }

    [Fact]
    public void SelectObjectForm_CanNestAnObjectLiteralAsAValue()
    {
        Assert.Equal(
            """[{"id":1,"geo":{"lat":1,"lng":2}}]""",
            Run("$source([{id: 1, lat: 1, lng: 2}]).$select({id: id, geo: {lat: lat, lng: lng}})"));
    }

    [Fact]
    public void SelectOfAnEmptyArray_SucceedsWhenTheProjectorDoesNotNeedTheElementShape()
    {
        // Same principle as $filter on an empty array (docs/FieldAccess.md): [] has an
        // unknown element type, so a projector that only needs the element itself, not one
        // of its (undetermined) fields, still works.
        Assert.Equal("[]", Run("$source([]).$select({v: ~})"));
        Assert.Equal("[]", Run("$source([]).$map(~)"));
    }

    [Fact]
    public void SelectOfAnEmptyArray_StillFailsWhenTheProjectorNeedsAKnownField()
    {
        QueryAssert.Fails(QueryErrorCode.UnknownIdentifier, () => QueryPipeline.Analyze("$source([]).$select(id)"));
        QueryAssert.Fails(QueryErrorCode.UnknownIdentifier, () => QueryPipeline.Analyze("$source([]).$select({id: id})"));
    }

    [Fact]
    public void SelectCanBeChainedWithFilterOnTheProjectedShape()
    {
        Assert.Equal(
            """[{"id":1}]""",
            Run("$source([{id: 1, extra: 'x'}, {id: 2, extra: 'y'}]).$select(id).$filter(id eq 1)"));
    }

    [Fact]
    public void SelectCanBeAppliedTwice()
    {
        Assert.Equal(
            """[{"id":1}]""",
            Run("$source([{id: 1, name: 'a'}]).$select(id, name).$select(id)"));
    }

    [Fact]
    public void SelectWithADynamicFieldName_WorksInTheObjectForm()
    {
        Assert.Equal(
            """[{"id":1,"dyn":"a"}]""",
            Run("$let(@col, 'name').$source([{id: 1, name: 'a'}]).$select({id: id, dyn: $field(@col)})"));
    }
}
