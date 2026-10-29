using FunQuery.Enums;
using FunQuery.Execution;
using FunQuery.SemanticTypes;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

/// <summary>
/// Schema-driven materialization of source rows. Contract in docs/Sources.md.
/// </summary>
public class SourceRowsTests
{
    private static readonly ObjectType Schema = new(
        new Dictionary<string, SemanticType>(StringComparer.Ordinal)
        {
            ["id"] = SemanticTypeOptions.Int,
            ["name"] = SemanticTypeOptions.String,
            ["joined"] = SemanticTypeOptions.Date,
            ["address"] = new ObjectType(
                new Dictionary<string, SemanticType>(StringComparer.Ordinal)
                {
                    ["city"] = SemanticTypeOptions.String,
                }),
            ["tags"] = new ArrayType(SemanticTypeOptions.String),
        });

    private static IReadOnlyDictionary<string, object?> Row(
        params (string Name, object? Value)[] fields)
    {
        var row = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var (name, value) in fields)
            row[name] = value;

        return row;
    }

    [Fact]
    public void MaterializesRowsInSchemaOrderAndConvertsNestedValues()
    {
        var rows = SourceRows.Materialize(
            "people",
            Schema,
            [
                Row(
                    ("id", 1),
                    ("name", "Ana"),
                    ("joined", new DateOnly(2024, 1, 15)),
                    ("address", Row(("city", "Bandung"))),
                    ("tags", new object?[] { "a", "b" })),
            ]);

        var row = Assert.IsType<ObjectValue>(Assert.Single(rows));

        Assert.Equal(new[] { "id", "name", "joined", "address", "tags" }, row.Shape.Keys);
        Assert.True(row.TryGetValue("id", out var id));
        Assert.Equal(1, id);
        Assert.True(row.TryGetValue("joined", out var joined));
        Assert.Equal(new DateOnly(2024, 1, 15), joined);

        Assert.True(row.TryGetValue("address", out var address));
        var addressValue = Assert.IsType<ObjectValue>(address);
        Assert.True(addressValue.TryGetValue("city", out var city));
        Assert.Equal("Bandung", city);

        Assert.True(row.TryGetValue("tags", out var tags));
        var tagList = Assert.IsAssignableFrom<IReadOnlyList<object?>>(tags);
        Assert.Equal(new object?[] { "a", "b" }, tagList);
    }

    [Fact]
    public void MissingAndNullFieldsReadAsNull()
    {
        var rows = SourceRows.Materialize(
            "people",
            Schema,
            [Row(("id", 1), ("name", null))]);

        var row = Assert.IsType<ObjectValue>(Assert.Single(rows));

        Assert.Null(row.GetValueAt(1));
        Assert.Null(row.GetValueAt(2));
        Assert.Null(row.GetValueAt(3));
        Assert.Null(row.GetValueAt(4));
    }

    [Fact]
    public void ExtraRowKeysAreIgnored()
    {
        var rows = SourceRows.Materialize(
            "people",
            new ObjectType(
                new Dictionary<string, SemanticType>(StringComparer.Ordinal)
                {
                    ["id"] = SemanticTypeOptions.Int,
                }),
            [Row(("id", 1), ("secret", "not exposed"))]);

        var row = Assert.IsType<ObjectValue>(Assert.Single(rows));

        Assert.Equal(new[] { "id" }, row.Shape.Keys);
    }

    [Fact]
    public void AWrongScalarTypeIsRejectedWithTheRowAndField()
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => SourceRows.Materialize(
                "people",
                Schema,
                [Row(("id", "1"))]));

        Assert.Contains("row 0", error.Message);
        Assert.Contains("'id'", error.Message);
        Assert.Contains("expects int", error.Message);
        Assert.Contains("got string", error.Message);
    }

    [Fact]
    public void AWrongNestedTypeIsRejectedWithItsPath()
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => SourceRows.Materialize(
                "people",
                Schema,
                [Row(("id", 1), ("address", Row(("city", 7))))]));

        Assert.Contains("address.city", error.Message);
        Assert.Contains("expects string", error.Message);
    }

    [Fact]
    public void AWrongArrayElementTypeIsRejectedWithItsIndex()
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => SourceRows.Materialize(
                "people",
                Schema,
                [Row(("id", 1), ("tags", new object?[] { "ok", 7 }))]));

        Assert.Contains("tags[1]", error.Message);
        Assert.Contains("expects string", error.Message);
    }
}
