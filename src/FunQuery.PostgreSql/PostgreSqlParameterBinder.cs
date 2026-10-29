using FunQuery.Enums;
using FunQuery.SemanticTypes;
using FunQuery.Sql.Sql;
using Npgsql;
using NpgsqlTypes;

namespace FunQuery.PostgreSql;

/// <summary>Memetakan parameter FunQuery ke parameter Npgsql bertipe.</summary>
internal static class PostgreSqlParameterBinder
{
    public static NpgsqlParameter Create(SqlParameter parameter)
    {
        var type = ResolveType(parameter.Type, parameter.Value);

        return new NpgsqlParameter(parameter.Name, type)
        {
            Value = parameter.Value ?? DBNull.Value,
        };
    }

    private static NpgsqlDbType ResolveType(SemanticType type, object? value) =>
        type switch
        {
            IntType => NpgsqlDbType.Integer,
            LongType => NpgsqlDbType.Bigint,
            DecimalType => NpgsqlDbType.Numeric,
            StringType => NpgsqlDbType.Text,
            BooleanType => NpgsqlDbType.Boolean,
            DateType => NpgsqlDbType.Date,
            FloatType => NpgsqlDbType.Real,
            DoubleType => NpgsqlDbType.Double,
            NullType => NpgsqlDbType.Unknown,
            AnyType or UnknownType => InferType(value),
            _ => throw new QueryException(
                QueryErrorCode.NotSupported,
                $"Parameter type '{type.Name}' is not supported by PostgreSQL."),
        };

    private static NpgsqlDbType InferType(object? value) =>
        value switch
        {
            null => NpgsqlDbType.Unknown,
            int => NpgsqlDbType.Integer,
            long => NpgsqlDbType.Bigint,
            decimal => NpgsqlDbType.Numeric,
            string => NpgsqlDbType.Text,
            bool => NpgsqlDbType.Boolean,
            DateOnly => NpgsqlDbType.Date,
            float => NpgsqlDbType.Real,
            double => NpgsqlDbType.Double,
            _ => throw new QueryException(
                QueryErrorCode.NotSupported,
                $"Parameter value type '{value.GetType().Name}' is not supported by PostgreSQL."),
        };
}
