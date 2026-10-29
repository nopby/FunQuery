using System.Data;
using FunQuery.Enums;
using FunQuery.Execution;
using FunQuery.SemanticTypes;

namespace FunQuery.PostgreSql.Tests;

/// <summary>M6.3: reader results become FunQuery ObjectValue/scalar values.</summary>
public class PostgreSqlRowReaderTests
{
    private static readonly ObjectType Schema = new(
        new Dictionary<string, SemanticType>(StringComparer.Ordinal)
        {
            ["id"] = SemanticTypeOptions.Int,
            ["customer"] = SemanticTypeOptions.String,
            ["total"] = SemanticTypeOptions.Decimal,
            ["active"] = SemanticTypeOptions.Boolean,
            ["ordered_on"] = SemanticTypeOptions.Date,
            ["nickname"] = SemanticTypeOptions.String,
        });

    private static DataTable Orders()
    {
        var table = new DataTable();
        table.Columns.Add("id", typeof(int));
        table.Columns.Add("customer", typeof(string));
        table.Columns.Add("total", typeof(decimal));
        table.Columns.Add("active", typeof(bool));
        table.Columns.Add("ordered_on", typeof(DateTime));

        table.Rows.Add(
            1,
            "Ana",
            10.5m,
            true,
            new DateTime(2024, 1, 15));

        table.Rows.Add(
            2,
            "Budi",
            20m,
            false,
            new DateTime(2024, 2, 3));

        return table;
    }

    [Fact]
    public async Task ReadsObjectsInSchemaOrder()
    {
        using var reader = Orders().CreateDataReader();

        var result = await PostgreSqlRowReader.ReadAsync(
            reader,
            new ArrayType(Schema),
            CancellationToken.None);

        var rows = Assert.IsAssignableFrom<IReadOnlyList<object?>>(result);
        Assert.Equal(2, rows.Count);

        var first = Assert.IsType<ObjectValue>(rows[0]);
        Assert.Equal(
            new[] { "id", "customer", "total", "active", "ordered_on", "nickname" },
            first.Shape.Keys);

        Assert.True(first.TryGetValue("id", out var id));
        Assert.Equal(1, id);
        Assert.True(first.TryGetValue("customer", out var customer));
        Assert.Equal("Ana", customer);
        Assert.True(first.TryGetValue("total", out var total));
        Assert.Equal(10.5m, total);
        Assert.True(first.TryGetValue("ordered_on", out var orderedOn));
        Assert.Equal(new DateOnly(2024, 1, 15), orderedOn);
        Assert.True(first.TryGetValue("nickname", out var nickname));
        Assert.Null(nickname);
    }

    [Fact]
    public async Task ReadsAScalarAndReturnsNullWhenThereAreNoRows()
    {
        var table = new DataTable();
        table.Columns.Add("value", typeof(long));
        table.Rows.Add(42L);

        using (var reader = table.CreateDataReader())
        {
            var result = await PostgreSqlRowReader.ReadAsync(
                reader,
                SemanticTypeOptions.Long,
                CancellationToken.None);

            Assert.Equal(42L, result);
        }

        table.Rows.Clear();

        using (var reader = table.CreateDataReader())
        {
            var result = await PostgreSqlRowReader.ReadAsync(
                reader,
                SemanticTypeOptions.Long,
                CancellationToken.None);

            Assert.Null(result);
        }
    }

    [Fact]
    public async Task ReadsADateStoredAsText()
    {
        var table = new DataTable();
        table.Columns.Add("ordered_on", typeof(string));
        table.Rows.Add("2024-01-15");

        using var reader = table.CreateDataReader();

        var result = await PostgreSqlRowReader.ReadAsync(
            reader,
            SemanticTypeOptions.Date,
            CancellationToken.None);

        Assert.Equal(new DateOnly(2024, 1, 15), result);
    }

    [Fact]
    public async Task ATypeMismatchIsReportedWithTheColumnName()
    {
        var table = new DataTable();
        table.Columns.Add("id", typeof(string));
        table.Rows.Add("not-an-int");

        using var reader = table.CreateDataReader();

        var error = await Assert.ThrowsAsync<QueryException>(
            () => PostgreSqlRowReader.ReadAsync(
                reader,
                SemanticTypeOptions.Int,
                CancellationToken.None));

        Assert.Equal(QueryErrorCode.TypeMismatch, error.Code);
        Assert.Contains("'id'", error.Message);
    }

    [Fact]
    public async Task TheReaderEnforcesTheResultItemLimit()
    {
        using var reader = Orders().CreateDataReader();

        var error = await Assert.ThrowsAsync<QueryException>(
            () => PostgreSqlRowReader.ReadAsync(
                reader,
                new ArrayType(Schema),
                maxItems: 1,
                CancellationToken.None));

        Assert.Equal(QueryErrorCode.ResultTooLarge, error.Code);
    }
}
