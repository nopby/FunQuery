using FunQuery.Enums;
using FunQuery.SemanticTypes;
using FunQuery.Sql.Sql;
using Npgsql;
using NpgsqlTypes;

namespace FunQuery.PostgreSql.Tests;

/// <summary>M6.3: typed Npgsql parameters, no value ever becomes SQL text.</summary>
public class PostgreSqlParameterBinderTests
{
    private static NpgsqlParameter Bind(object? value, SemanticType type) =>
        PostgreSqlParameterBinder.Create(new SqlParameter("@p0", value, type));

    [Fact]
    public void BindsScalarTypes()
    {
        Assert.Equal(NpgsqlDbType.Integer, Bind(1, SemanticTypeOptions.Int).NpgsqlDbType);
        Assert.Equal(NpgsqlDbType.Bigint, Bind(1L, SemanticTypeOptions.Long).NpgsqlDbType);
        Assert.Equal(NpgsqlDbType.Numeric, Bind(1.5m, SemanticTypeOptions.Decimal).NpgsqlDbType);
        Assert.Equal(NpgsqlDbType.Text, Bind("a", SemanticTypeOptions.String).NpgsqlDbType);
        Assert.Equal(NpgsqlDbType.Boolean, Bind(true, SemanticTypeOptions.Boolean).NpgsqlDbType);
        Assert.Equal(
            NpgsqlDbType.Date,
            Bind(new DateOnly(2024, 1, 15), SemanticTypeOptions.Date).NpgsqlDbType);
    }

    [Fact]
    public void NullBecomesDbNull()
    {
        var parameter = Bind(null, SemanticTypeOptions.Int);

        Assert.Equal(NpgsqlDbType.Integer, parameter.NpgsqlDbType);
        Assert.Same(DBNull.Value, parameter.Value);
    }

    [Fact]
    public void AnyTypeInfersFromTheValue()
    {
        Assert.Equal(NpgsqlDbType.Integer, Bind(1, SemanticTypeOptions.AnyType).NpgsqlDbType);
        Assert.Equal(NpgsqlDbType.Text, Bind("a", SemanticTypeOptions.AnyType).NpgsqlDbType);
        Assert.Equal(
            NpgsqlDbType.Date,
            Bind(new DateOnly(2024, 1, 15), SemanticTypeOptions.AnyType).NpgsqlDbType);
    }

    [Fact]
    public void UnsupportedTypesFailExplicitly()
    {
        Assert.Throws<QueryException>(
            () => Bind(new List<object?>(), new ArrayType(SemanticTypeOptions.Int)));

        Assert.Throws<QueryException>(
            () => Bind(
                new Dictionary<string, SemanticType>(),
                new ObjectType(new Dictionary<string, SemanticType>())));
    }
}
