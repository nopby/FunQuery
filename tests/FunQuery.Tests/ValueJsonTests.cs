using FunQuery.Enums;
using FunQuery.Execution;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

public class ValueJsonTests
{
    [Fact]
    public void Scalars_AreWrittenAsJson()
    {
        Assert.Equal("null", ValueJson.Serialize(null));
        Assert.Equal("true", ValueJson.Serialize(true));
        Assert.Equal("false", ValueJson.Serialize(false));
        Assert.Equal("42", ValueJson.Serialize(42));
        Assert.Equal("9223372036854775807", ValueJson.Serialize(long.MaxValue));
        Assert.Equal("1.50", ValueJson.Serialize(1.50m));
        Assert.Equal("0.1", ValueJson.Serialize(0.1));
    }

    [Fact]
    public void Strings_AreQuotedAndEscaped()
    {
        Assert.Equal("\"a\\\"b\"", ValueJson.Serialize("a\"b"));
        Assert.Equal("\"line1\\nline2\"", ValueJson.Serialize("line1\nline2"));
        Assert.Equal("\"back\\\\slash\"", ValueJson.Serialize("back\\slash"));
    }

    [Fact]
    public void NonAsciiText_AndApostrophes_AreNotEscapedNeedlessly()
    {
        Assert.Equal("\"héllo ✓ it's\"", ValueJson.Serialize("héllo ✓ it's"));
    }

    [Fact]
    public void Sequences_AreWrittenAsArrays()
    {
        Assert.Equal("[]", ValueJson.Serialize(new List<object?>()));
        Assert.Equal("""[1,"a",null,true]""", ValueJson.Serialize(new List<object?> { 1, "a", null, true }));
        Assert.Equal("[[1],[]]", ValueJson.Serialize(new List<object?> { new List<object?> { 1 }, new List<object?>() }));
    }

    [Fact]
    public void Objects_KeepTheirFieldOrder()
    {
        var shape = new ObjectShape(["b", "a"]);
        var row = new ObjectValue(shape, [1, "x"]);

        Assert.Equal("""{"b":1,"a":"x"}""", ValueJson.Serialize(row));
        Assert.Equal("{}", ValueJson.Serialize(new ObjectValue(new ObjectShape([]), [])));
    }

    [Fact]
    public void AValueTheWriterDoesNotKnow_IsAnInternalError()
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.InternalError,
            () => ValueJson.Serialize(new object()));

        Assert.True(error.IsInternal);
    }

    [Fact]
    public void ObjectValue_ChecksThatValuesMatchTheShape()
    {
        Assert.Throws<ArgumentException>(
            () => { _ = new ObjectValue(new ObjectShape(["a", "b"]), [1]); });
    }

    [Fact]
    public void ObjectValue_ReadsMissingFieldsAsNull()
    {
        var row = new ObjectValue(new ObjectShape(["a"]), [1]);

        Assert.False(row.TryGetValue("b", out var value));
        Assert.Null(value);
    }
}
