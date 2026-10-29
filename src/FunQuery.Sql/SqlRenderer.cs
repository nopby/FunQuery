using System.Globalization;
using FunQuery.Enums;
using FunQuery.SemanticTypes;
using FunQuery.Sql.Plan;
using FunQuery.Sql.Sql;

namespace FunQuery.Sql;

/// <summary>
/// Menerjemahkan <see cref="RelationalPlan"/> menjadi SQL parameterized. Dialect hanya mengisi
/// bagian yang berbeda; renderer menangani null dua-nilai, collation, dan bentuk query.
/// </summary>
public sealed class SqlRenderer
{
    private readonly ISqlDialect _dialect;
    private readonly IReadOnlyDictionary<string, SqlTable> _tables;
    private readonly SqlWriter _writer;
    private readonly List<SqlParameter> _parameters = [];
    private QueryEnvironment _environment = QueryEnvironment.Empty;
    private int _subqueryIndex;

    public SqlRenderer(
        ISqlDialect dialect,
        IReadOnlyDictionary<string, SqlTable> tables)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        ArgumentNullException.ThrowIfNull(tables);

        _dialect = dialect;
        _tables = tables;
        _writer = new SqlWriter(dialect);
    }

    public SqlStatement Render(
        RelationalPlan plan,
        QueryEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(environment);

        PlanValidator.Validate(plan, _dialect.Capabilities);

        _environment = environment;
        _parameters.Clear();
        _subqueryIndex = 0;

        var query = BuildPlan(plan);
        var sql = _writer.Write(query);

        return new SqlStatement(sql, _parameters.ToArray());
    }

    private SqlQuery BuildPlan(RelationalPlan plan) =>
        plan switch
        {
            Scan scan => BuildScan(scan),
            Values => throw NotSupported("Inline sources are not supported by this provider."),
            Filter filter => ApplyFilter(filter),
            Project project => ApplyProject(project),
            Sort sort => ApplySort(sort),
            Take take => ApplyTake(take),
            Skip skip => ApplySkip(skip),
            Distinct distinct => ApplyDistinct(distinct),
            Aggregate aggregate => ApplyAggregate(aggregate),
            Any any => ApplyAny(any),
            First first => ApplyFirst(first),
            _ => throw new QueryException(
                QueryErrorCode.NotSupported,
                "The provider does not support this plan node."),
        };

    private SqlQuery BuildScan(Scan scan)
    {
        if (!_tables.TryGetValue(scan.SourceName, out var table))
        {
            throw new QueryException(
                QueryErrorCode.UnknownSource,
                $"No SQL table is registered for source '{scan.SourceName}'.");
        }

        return new SqlQuery(
            [new SqlProjection(SqlStar.Instance, null)],
            new SqlTableSource(table.Name),
            null,
            [],
            null,
            null,
            Distinct: false,
            DistinctOn: []);
    }

    private SqlQuery ApplyFilter(Filter filter)
    {
        var input = BuildPlan(filter.Input);
        var predicate = RenderBoolean(filter.Predicate);

        if (CanExtend(input))
        {
            return input with
            {
                Where = input.Where is null
                    ? predicate
                    : new SqlBinary("AND", input.Where, predicate),
            };
        }

        return Wrap(input) with { Where = predicate };
    }

    private SqlQuery ApplyProject(Project project)
    {
        var input = BuildPlan(project.Input);

        var projections = project.Projections
            .Select(projection => new SqlProjection(
                RenderValue(projection.Expression),
                AliasFor(projection)))
            .ToArray();

        if (CanExtend(input))
            return input with { Projections = projections };

        return Wrap(input) with { Projections = projections };
    }

    private SqlQuery ApplySort(Sort sort)
    {
        var input = BuildPlan(sort.Input);
        var key = ApplyCollation(RenderValue(sort.Key), sort.Key.Type);
        var order = new SqlOrder(key, sort.Descending, NullsFirst: !sort.Descending);

        if (input.DistinctOn.Count == 0 &&
            input.Limit is null &&
            input.Offset is null)
        {
            return input with { OrderBy = [order] };
        }

        return Wrap(input) with { OrderBy = [order] };
    }

    private SqlQuery ApplyTake(Take take)
    {
        var input = BuildPlan(take.Input);
        var count = RenderScalar(take.Count);

        RequireOrder(input);

        if (input.Limit is null && input.Offset is null)
            return input with { Limit = count };

        return Wrap(input) with
        {
            OrderBy = input.OrderBy,
            Limit = count,
        };
    }

    private SqlQuery ApplySkip(Skip skip)
    {
        var input = BuildPlan(skip.Input);
        var count = RenderScalar(skip.Count);

        RequireOrder(input);

        if (input.Limit is null && input.Offset is null)
            return input with { Offset = count };

        return Wrap(input) with
        {
            OrderBy = input.OrderBy,
            Offset = count,
        };
    }

    private SqlQuery ApplyDistinct(Distinct distinct)
    {
        var input = BuildPlan(distinct.Input);

        if (distinct.Key is null)
        {
            if (CanExtend(input))
                return input with { Distinct = true };

            return Wrap(input) with
            {
                OrderBy = input.OrderBy,
                Distinct = true,
            };
        }

        var key = ApplyCollation(RenderValue(distinct.Key), distinct.Key.Type);
        var keyed = Wrap(input);

        return keyed with
        {
            DistinctOn = [key],
            OrderBy = [new SqlOrder(key, Descending: false, NullsFirst: true)],
        };
    }

    private SqlQuery ApplyAggregate(Aggregate aggregate)
    {
        var input = Wrap(BuildPlan(aggregate.Input));

        return input with
        {
            Projections =
            [
                new SqlProjection(
                    new SqlFunction("COUNT", [SqlStar.Instance]),
                    null),
            ],
        };
    }

    private SqlQuery ApplyAny(Any any)
    {
        var inner = Wrap(BuildPlan(any.Input));

        if (any.Predicate is not null)
            inner = inner with { Where = RenderBoolean(any.Predicate) };

        return new SqlQuery(
            [new SqlProjection(new SqlExists(inner), null)],
            null,
            null,
            [],
            null,
            null,
            Distinct: false,
            DistinctOn: []);
    }

    private SqlQuery ApplyFirst(First first)
    {
        var input = BuildPlan(first.Input);

        var inner = Wrap(input) with { OrderBy = input.OrderBy };

        if (first.Predicate is not null)
            inner = inner with { Where = RenderBoolean(first.Predicate) };

        return inner with { Limit = new SqlLiteral("1") };
    }

    private SqlQuery Wrap(SqlQuery query)
    {
        var alias = "t" + _subqueryIndex.ToString(CultureInfo.InvariantCulture);
        _subqueryIndex++;

        return new SqlQuery(
            [new SqlProjection(SqlStar.Instance, null)],
            new SqlSubquerySource(query, alias),
            null,
            [],
            null,
            null,
            Distinct: false,
            DistinctOn: []);
    }

    private static bool CanExtend(SqlQuery query) =>
        !query.Distinct &&
        query.DistinctOn.Count == 0 &&
        query.Limit is null &&
        query.Offset is null &&
        IsStar(query);

    private static bool IsStar(SqlQuery query) =>
        query.Projections.Count == 1 &&
        query.Projections[0].Expression is SqlStar &&
        query.Projections[0].Alias is null;

    /// <summary>Kolom dengan nama yang sama tidak perlu alias; ekspresi lain perlu.</summary>
    private static string? AliasFor(Projection projection)
    {
        if (projection.Key is null)
            return null;

        if (projection.Expression is Column column)
        {
            var lastSegment = column.Path[
                (column.Path.LastIndexOf('.') + 1)..];

            if (lastSegment == projection.Key)
                return null;
        }

        return projection.Key;
    }

    private void RequireOrder(SqlQuery input)
    {
        if (input.OrderBy.Count == 0)
        {
            throw new QueryException(
                QueryErrorCode.OrderRequired,
                "Pagination requires an explicit $sort before $take/$skip.");
        }
    }

    private SqlExpression RenderValue(PlanExpression expression) =>
        expression.Type is BooleanType
            ? RenderBoolean(expression)
            : RenderScalar(expression);

    private SqlExpression RenderScalar(PlanExpression expression) =>
        expression switch
        {
            Column column => new SqlColumn(column.Path),
            Literal literal => RenderLiteral(literal),
            Parameter parameter => RenderParameter(parameter),
            Call call => RenderCall(call),
            Binary binary => RenderBoolean(binary),
            Logical logical => RenderBoolean(logical),
            Not not => RenderBoolean(not),
            Subquery subquery => new SqlSubquery(BuildPlan(subquery.Plan)),
            CurrentRow => throw NotSupported(
                "The current row is not a SQL scalar; use $field or a projection."),
            ObjectConstruct or ArrayConstruct => throw NotSupported(
                "JSON projection is not supported by this provider."),
            IndexExpression => throw NotSupported(
                "$index requires a window function, which this provider does not support yet."),
            Field => throw NotSupported(
                "Field access on a computed value is not supported by this provider."),
            _ => throw NotSupported("Unsupported plan expression."),
        };

    private SqlExpression RenderBoolean(PlanExpression expression) =>
        expression switch
        {
            Literal { Value: bool value } =>
                new SqlLiteral(value ? "TRUE" : "FALSE"),

            Binary
            {
                Operator: ComparisonOperator.Equal or ComparisonOperator.NotEqual,
            } binary => RenderEquality(binary),

            Binary binary when binary.Operator is
                ComparisonOperator.GreaterThan or
                ComparisonOperator.GreaterThanOrEqual or
                ComparisonOperator.LessThan or
                ComparisonOperator.LessThanOrEqual =>
                Coalesce(RenderOrderedComparison(binary)),

            Binary { Operator: ComparisonOperator.In } binary =>
                Coalesce(RenderIn(binary)),

            Binary binary when binary.Operator is
                ComparisonOperator.Contains or
                ComparisonOperator.StartsWith or
                ComparisonOperator.EndsWith =>
                Coalesce(RenderTextComparison(binary)),

            Logical logical => new SqlBinary(
                logical.Operator == LogicalOperator.And ? "AND" : "OR",
                RenderBoolean(logical.Left),
                RenderBoolean(logical.Right)),

            Not not => new SqlUnary("NOT", RenderBoolean(not.Operand)),

            Subquery subquery =>
                Coalesce(new SqlSubquery(BuildPlan(subquery.Plan))),

            _ => Coalesce(RenderScalar(expression)),
        };

    private SqlExpression RenderEquality(Binary binary)
    {
        var left = ApplyCollation(RenderScalar(binary.Left), binary.Left.Type);
        var right = ApplyCollation(RenderScalar(binary.Right), binary.Right.Type);

        return new SqlRaw(_dialect.RenderEquality(
            _writer.WriteExpression(left),
            _writer.WriteExpression(right),
            binary.Operator == ComparisonOperator.NotEqual));
    }

    private SqlExpression RenderOrderedComparison(Binary binary)
    {
        var op = binary.Operator switch
        {
            ComparisonOperator.GreaterThan => ">",
            ComparisonOperator.GreaterThanOrEqual => ">=",
            ComparisonOperator.LessThan => "<",
            ComparisonOperator.LessThanOrEqual => "<=",
            _ => throw NotSupported("Unsupported comparison operator."),
        };

        var left = ApplyCollation(RenderScalar(binary.Left), binary.Left.Type);
        var right = ApplyCollation(RenderScalar(binary.Right), binary.Right.Type);

        return new SqlBinary(op, left, right);
    }

    private SqlExpression RenderTextComparison(Binary binary)
    {
        var text = RenderScalar(binary.Left);
        var part = RenderScalar(binary.Right);

        var sql = binary.Operator switch
        {
            ComparisonOperator.Contains => _dialect.RenderContains(
                _writer.WriteExpression(text),
                _writer.WriteExpression(part)),

            ComparisonOperator.StartsWith => _dialect.RenderStartsWith(
                _writer.WriteExpression(text),
                _writer.WriteExpression(part)),

            ComparisonOperator.EndsWith => _dialect.RenderEndsWith(
                _writer.WriteExpression(text),
                _writer.WriteExpression(part)),

            _ => throw NotSupported("Unsupported text operator."),
        };

        return new SqlRaw(sql);
    }

    private SqlExpression RenderIn(Binary binary)
    {
        if (binary.Right is not ArrayConstruct array)
        {
            throw NotSupported(
                "The provider supports 'in' only with an array literal in v1.");
        }

        if (array.Elements.Count == 0)
            return new SqlLiteral("FALSE");

        var left = ApplyCollation(RenderScalar(binary.Left), binary.Left.Type);
        var values = new List<string>();
        var hasNull = false;

        foreach (var element in array.Elements)
        {
            if (element is Literal { Value: null })
            {
                hasNull = true;
                continue;
            }

            var value = ApplyCollation(RenderScalar(element), element.Type);
            values.Add(_writer.WriteExpression(value));
        }

        SqlExpression? result = null;

        if (values.Count > 0)
        {
            result = new SqlBinary(
                "IN",
                left,
                new SqlRaw("(" + string.Join(", ", values) + ")"));
        }

        if (hasNull)
        {
            var isNull = new SqlBinary("IS", left, new SqlLiteral("NULL"));

            result = result is null
                ? isNull
                : new SqlBinary("OR", result, isNull);
        }

        return result ?? new SqlLiteral("FALSE");
    }

    private SqlExpression RenderLiteral(Literal literal)
    {
        if (literal.Value is null)
            return new SqlLiteral("NULL");

        if (literal.Value is bool value)
            return new SqlLiteral(value ? "TRUE" : "FALSE");

        return AddParameter(literal.Value, literal.Type);
    }

    private SqlExpression RenderParameter(Parameter parameter)
    {
        if (!_environment.Parameters.TryGetValue(parameter.Name, out var value))
        {
            throw new QueryException(
                QueryErrorCode.UndefinedVariable,
                $"Variable '@{parameter.Name}' is not defined.");
        }

        return AddParameter(value.Value, parameter.Type);
    }

    private SqlExpression AddParameter(object? value, SemanticType type)
    {
        var name = _dialect.ParameterName(_parameters.Count);
        _parameters.Add(new SqlParameter(name, value, type));

        return new SqlParameterRef(name);
    }

    private SqlExpression RenderCall(Call call) =>
        call.Name switch
        {
            "$lower" => RenderCaseFunction("lower", call),
            "$upper" => RenderCaseFunction("upper", call),
            "$length" => RenderLength(call),
            "$coalesce" => new SqlFunction(
                "COALESCE",
                call.Arguments.Select(RenderValue).ToArray()),
            "$date" => RenderDate(call),
            _ => throw NotSupported(
                $"Function '{call.Name}' is not supported by the SQL provider."),
        };

    private SqlExpression RenderCaseFunction(string name, Call call)
    {
        if (call.Arguments[0] is Literal { Value: string text } &&
            text.Any(character => character > 0x7F))
        {
            throw NotSupported(
                "The provider cannot guarantee invariant non-ASCII casing.");
        }

        var argument = ApplyCollation(
            RenderScalar(call.Arguments[0]),
            SemanticTypeOptions.String);

        return new SqlFunction(name, [argument]);
    }

    private SqlExpression RenderLength(Call call)
    {
        if (call.Arguments[0] is Literal { Value: string text } &&
            text.Any(char.IsSurrogate))
        {
            throw NotSupported(
                "The provider counts Unicode code points, not UTF-16 code units.");
        }

        return new SqlFunction("length", [RenderScalar(call.Arguments[0])]);
    }

    private SqlExpression RenderDate(Call call)
    {
        var argument = call.Arguments[0];

        if (argument is Literal { Value: null })
            return new SqlLiteral("NULL");

        if (argument is Literal { Value: string text })
        {
            if (!DateFormat.TryParse(text, out var date))
            {
                throw new QueryException(
                    QueryErrorCode.InvalidDate,
                    $"'{text}' is not a valid date. Use the ISO format yyyy-MM-dd.");
            }

            return AddParameter(date, SemanticTypeOptions.Date);
        }

        var rendered = RenderScalar(argument);

        return argument.Type is DateType
            ? rendered
            : new SqlRaw(_dialect.RenderDateCast(_writer.WriteExpression(rendered)));
    }

    private SqlExpression ApplyCollation(
        SqlExpression expression,
        SemanticType type) =>
        type is StringType
            ? new SqlRaw(_dialect.ApplyStringCollation(
                _writer.WriteExpression(expression)))
            : expression;

    private static SqlExpression Coalesce(SqlExpression expression) =>
        new SqlFunction("COALESCE", [expression, new SqlLiteral("FALSE")]);

    private static QueryException NotSupported(string message) =>
        new(QueryErrorCode.NotSupported, message);
}
