using FunQuery.Enums;
using FunQuery.Execution;
using FunQuery.Expressions;
using FunQuery.SemanticTypes;
using FunQuery.Sql.Plan;

namespace FunQuery.Sql;

/// <summary>
/// Menurunkan AST yang sudah dianalisis menjadi <see cref="RelationalPlan"/>. Tidak ada SQL,
/// dialect, atau driver database di sini; plan bisa diuji tanpa database.
/// </summary>
public sealed class PlanBuilder
{
    private readonly ReadOnlyMemory<char> _source;
    private readonly IReadOnlyDictionary<string, PlanSource> _sources;
    private readonly QueryEnvironment _environment;
    private Dictionary<string, PlanBinding> _bindings =
        new(StringComparer.Ordinal);
    private List<CteDefinition> _ctes = [];

    public PlanBuilder(
        string query,
        IReadOnlyDictionary<string, PlanSource> sources,
        QueryEnvironment? environment = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(sources);

        _source = query.AsMemory();
        _sources = sources;
        _environment = environment ?? QueryEnvironment.Empty;
    }

    public static RelationalPlan Build(
        string query,
        BaseExpression expression,
        IReadOnlyDictionary<string, PlanSource> sources,
        QueryEnvironment? environment = null) =>
        new PlanBuilder(query, sources, environment).Build(expression);

    public RelationalPlan Build(BaseExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        return BuildChain(expression)
            ?? throw new QueryException(
                QueryErrorCode.NotSupported,
                "A provider plan must start with a data source.");
    }

    private RelationalPlan? BuildChain(BaseExpression expression)
    {
        var calls = new List<CallExpression>();
        BaseExpression? current = expression;

        while (current is CallExpression call)
        {
            calls.Add(call);
            current = call.Target;
        }

        if (current is not null)
        {
            throw new QueryException(
                QueryErrorCode.NotSupported,
                "A provider plan must be a function chain.");
        }

        calls.Reverse();

        var outerCtes = _ctes;
        _ctes = [];

        try
        {
            RelationalPlan? plan = null;

            foreach (var call in calls)
                plan = Apply(call, plan);

            if (plan is null || _ctes.Count == 0)
                return plan;

            return new With(_ctes.ToArray(), plan);
        }
        finally
        {
            _ctes = outerCtes;
        }
    }

    private RelationalPlan? Apply(CallExpression call, RelationalPlan? input)
    {
        var name = Text(call.Function);

        return name switch
        {
            "$let" => ApplyLet(call, input),

            "$source" => ApplySource(call, input),

            "$filter" => new Filter(
                RequireInput(call, input),
                BuildExpression(call.Arguments[0])),

            "$map" => ApplyMap(call, RequireInput(call, input)),

            "$select" => ApplySelect(call, RequireInput(call, input)),

            "$sort" => ApplySort(call, RequireInput(call, input)),

            "$take" => new Take(
                RequireInput(call, input),
                BuildExpression(call.Arguments[0])),

            "$skip" => new Skip(
                RequireInput(call, input),
                BuildExpression(call.Arguments[0])),

            "$count" => new Aggregate(
                RequireInput(call, input),
                AggregateFunction.Count),

            "$any" => new Any(
                RequireInput(call, input),
                BuildOptionalExpression(call)),

            "$first" => new First(
                RequireInput(call, input),
                BuildOptionalExpression(call)),

            "$distinct" => new Distinct(
                RequireInput(call, input),
                BuildOptionalExpression(call)),

            _ => throw new QueryException(
                QueryErrorCode.NotSupported,
                $"Function '{name}' cannot be translated to a relational plan."),
        };
    }

    private RelationalPlan? ApplyLet(CallExpression call, RelationalPlan? input)
    {
        if (call.Arguments.Count != 2 ||
            call.Arguments[0] is not VariableExpression variable)
        {
            throw new QueryException(
                QueryErrorCode.InternalError,
                "$let must have a variable and a value. Was the query analyzed?");
        }

        var name = VariableName(variable);
        var value = call.Arguments[1];

        if (value.SemanticType is ArrayType { Type: ObjectType schema })
        {
            RelationalPlan plan;

            // A value has its own detached scope: bindings declared inside it must not leak
            // into the chain that follows the outer $let.
            using (EnterScope())
                plan = BuildRelational(value);

            var sqlName = CteName(name);
            _bindings[name] = new RelationalBinding(sqlName, schema);
            _ctes.Add(new CteDefinition(sqlName, schema, plan));
            return input;
        }

        if (value.SemanticType is ArrayType)
        {
            throw new QueryException(
                QueryErrorCode.NotSupported,
                $"The SQL provider cannot bind '@{name}' as a CTE: " +
                "the result set must contain objects with named fields.");
        }

        _bindings[name] = new ScalarBinding(BuildExpression(value));
        return input;
    }

    private RelationalPlan BuildRelational(BaseExpression value)
    {
        if (value is not CallExpression)
        {
            throw new QueryException(
                QueryErrorCode.NotSupported,
                "A $let result set must be a function chain in the SQL provider.");
        }

        return BuildChain(value)
            ?? throw new QueryException(
                QueryErrorCode.NotSupported,
                "A $let result set must start with a data source.");
    }

    private RelationalPlan ApplySource(CallExpression call, RelationalPlan? input)
    {
        if (input is not null)
        {
            throw new QueryException(
                QueryErrorCode.InvalidTarget,
                "$source cannot be chained on data.");
        }

        return call.Arguments[0] switch
        {
            ArrayExpression array => new Values(
                array.Elements.Select(BuildExpression).ToArray(),
                ElementType(array)),

            VariableExpression variable => BuildSourceVariable(variable),

            _ => throw new QueryException(
                QueryErrorCode.NotSupported,
                "$source must be an inline array or a registered source variable."),
        };
    }

    private RelationalPlan BuildSourceVariable(VariableExpression variable)
    {
        var name = VariableName(variable);

        if (_sources.TryGetValue(name, out var source))
            return new Scan(source.Name, source.Schema);

        if (_bindings.TryGetValue(name, out var binding))
        {
            if (binding is RelationalBinding relational)
                return new CteScan(relational.SqlName, relational.Schema);

            throw new QueryException(
                QueryErrorCode.NotSupported,
                $"Variable '@{name}' is not a result set.");
        }

        throw new QueryException(
            QueryErrorCode.NotSupported,
            $"Source variable '@{name}' is not registered; " +
            "only registered sources and $let result sets can be planned.");
    }

    private Project ApplyMap(CallExpression call, RelationalPlan input)
    {
        var argument = call.Arguments[0];

        if (argument is BlockExpression block)
            return new Project(input, BuildObjectProjections(block), ProducesObject: true);

        return new Project(
            input,
            [new Projection(null, BuildExpression(argument))],
            ProducesObject: false);
    }

    private Project ApplySelect(CallExpression call, RelationalPlan input)
    {
        if (call.Arguments is [BlockExpression block])
            return new Project(input, BuildObjectProjections(block), ProducesObject: true);

        var projections = new Projection[call.Arguments.Count];

        for (int i = 0; i < projections.Length; i++)
        {
            var argument = call.Arguments[i];

            var key = argument switch
            {
                IdentifierExpression identifier => Text(identifier.Token),
                FieldAccessExpression access => Text(access.Field),
                _ => throw new QueryException(
                    QueryErrorCode.InvalidSelectArgument,
                    "Each argument of '$select' must be a plain field, " +
                    "e.g. id or address.city."),
            };

            projections[i] = new Projection(key, BuildExpression(argument));
        }

        return new Project(input, projections, ProducesObject: true);
    }

    private Sort ApplySort(CallExpression call, RelationalPlan input)
    {
        var descending = call.Arguments.Count == 2 &&
            call.Arguments[1] is IdentifierExpression direction &&
            Text(direction.Token) == "desc";

        return new Sort(input, BuildExpression(call.Arguments[0]), descending);
    }

    private Projection[] BuildObjectProjections(BlockExpression block)
    {
        var projections = new Projection[block.Expressions.Count];

        for (int i = 0; i < projections.Length; i++)
        {
            if (block.Expressions[i] is not NamedExpression named)
            {
                throw new QueryException(
                    QueryErrorCode.InternalError,
                    "An object literal contains an entry that is not 'name: value'.");
            }

            projections[i] = new Projection(
                Key(named.Name),
                BuildExpression(named.Value));
        }

        return projections;
    }

    private PlanExpression? BuildOptionalExpression(CallExpression call) =>
        call.Arguments.Count == 0 ? null : BuildExpression(call.Arguments[0]);

    private RelationalPlan RequireInput(
        CallExpression call,
        RelationalPlan? input) =>
        input ?? throw new QueryException(
            QueryErrorCode.NotSupported,
            $"Function '{Text(call.Function)}' requires a data source before it.");

    private PlanExpression BuildExpression(BaseExpression expression) =>
        expression switch
        {
            ValueExpression value =>
                new Literal(LiteralValues.Parse(value, _source), TypeOf(value)),

            IdentifierExpression identifier =>
                new Column(Text(identifier.Token), TypeOf(identifier)),

            VariableExpression variable =>
                BuildVariable(variable),

            TildeExpression tilde =>
                new CurrentRow(TypeOf(tilde)),

            FieldAccessExpression access =>
                BuildFieldAccess(access),

            ArrayExpression array =>
                new ArrayConstruct(
                    array.Elements.Select(BuildExpression).ToArray(),
                    TypeOf(array)),

            BlockExpression block =>
                BuildObjectConstruct(block),

            ComparisonExpression comparison =>
                new Binary(
                    comparison.Operator,
                    BuildExpression(comparison.Left),
                    BuildExpression(comparison.Right),
                    TypeOf(comparison)),

            LogicalExpression logical =>
                new Logical(
                    logical.Operator,
                    BuildExpression(logical.Left),
                    BuildExpression(logical.Right),
                    TypeOf(logical)),

            NotExpression not =>
                new Not(BuildExpression(not.Operand), TypeOf(not)),

            CallExpression call =>
                BuildCallExpression(call),

            _ => throw new QueryException(
                QueryErrorCode.InternalError,
                $"Cannot build a plan expression from {expression.GetType().Name}."),
        };

    private PlanExpression BuildFieldAccess(FieldAccessExpression access)
    {
        if (TryBuildColumnPath(access.Target, out var parent))
        {
            var path = parent.Length == 0
                ? Text(access.Field)
                : parent + "." + Text(access.Field);

            return new Column(path, TypeOf(access));
        }

        return new Field(
            BuildExpression(access.Target),
            Text(access.Field),
            TypeOf(access));
    }

    private bool TryBuildColumnPath(BaseExpression expression, out string path)
    {
        switch (expression)
        {
            case TildeExpression:
                path = string.Empty;
                return true;

            case IdentifierExpression identifier:
                path = Text(identifier.Token);
                return true;

            case FieldAccessExpression access
                when TryBuildColumnPath(access.Target, out var parent):
                path = parent.Length == 0
                    ? Text(access.Field)
                    : parent + "." + Text(access.Field);
                return true;

            default:
                path = string.Empty;
                return false;
        }
    }

    private ObjectConstruct BuildObjectConstruct(BlockExpression block)
    {
        var fields = new ObjectField[block.Expressions.Count];

        for (int i = 0; i < fields.Length; i++)
        {
            if (block.Expressions[i] is not NamedExpression named)
            {
                throw new QueryException(
                    QueryErrorCode.InternalError,
                    "An object literal contains an entry that is not 'name: value'.");
            }

            fields[i] = new ObjectField(Key(named.Name), BuildExpression(named.Value));
        }

        return new ObjectConstruct(fields, TypeOf(block));
    }

    private PlanExpression BuildCallExpression(CallExpression call)
    {
        var name = Text(call.Function);

        if (call.Target is null && IsScalarFunction(name))
        {
            return name switch
            {
                "$field" => BuildField(call),
                "$index" => BuildIndex(call),
                _ => new Call(
                    name,
                    call.Arguments.Select(BuildExpression).ToArray(),
                    TypeOf(call)),
            };
        }

        using var scope = EnterScope();
        var plan = BuildChain(call)
            ?? throw new QueryException(
                QueryErrorCode.NotSupported,
                "A nested chain must start with a data source.");

        return new Subquery(plan, TypeOf(call));
    }

    private PlanExpression BuildField(CallExpression call)
    {
        var path = call.Arguments[0] switch
        {
            ValueExpression value => LiteralValues.Parse(value, _source) as string,
            VariableExpression variable => ResolveString(variable),
            _ => null,
        };

        return path is null
            ? throw new QueryException(
                QueryErrorCode.NotSupported,
                "$field requires a field name known at plan time.")
            : new Column(path, TypeOf(call));
    }

    private PlanExpression BuildIndex(CallExpression call) =>
        new IndexExpression(
            call.Arguments.Count == 1 ? BuildExpression(call.Arguments[0]) : null,
            TypeOf(call));

    private PlanExpression BuildVariable(VariableExpression variable)
    {
        var name = VariableName(variable);

        if (_bindings.TryGetValue(name, out var bound))
        {
            if (bound is ScalarBinding scalar)
                return scalar.Expression;

            throw new QueryException(
                QueryErrorCode.NotSupported,
                $"Result-set variable '@{name}' cannot be used as a scalar value.");
        }

        if (_environment.Parameters.TryGetValue(name, out var parameter))
            return new Parameter(name, parameter.Type);

        throw new QueryException(
            QueryErrorCode.UndefinedVariable,
            $"Variable '@{name}' is not defined.");
    }

    private string ResolveString(VariableExpression variable)
    {
        var name = VariableName(variable);

        if (_bindings.TryGetValue(name, out var bound))
        {
            if (bound is ScalarBinding scalar)
                return ResolveString(scalar.Expression);

            throw new QueryException(
                QueryErrorCode.NotSupported,
                $"Result-set variable '@{name}' cannot be used as a field name.");
        }

        throw new QueryException(
            QueryErrorCode.NotSupported,
            $"Variable '@{name}' is a request parameter; " +
            "dynamic field names from request parameters are not supported by the SQL provider.");
    }

    private string ResolveString(PlanExpression expression) =>
        expression switch
        {
            Literal { Value: string value } => value,

            Parameter => throw new QueryException(
                QueryErrorCode.NotSupported,
                "Dynamic field names from request parameters are not supported by the SQL provider."),

            _ => throw new QueryException(
                QueryErrorCode.NotSupported,
                "The field name must be a literal known at plan time."),
        };

    private static bool IsScalarFunction(string name) =>
        name is "$lower" or "$upper" or "$length" or "$coalesce" or "$date" or
                "$field" or "$index";

    private static SemanticType ElementType(ArrayExpression array) =>
        array.SemanticType is ArrayType arrayType
            ? arrayType.Type
            : SemanticTypeOptions.Unknown;

    private static SemanticType TypeOf(BaseExpression expression) =>
        expression.SemanticType
        ?? throw new QueryException(
            QueryErrorCode.InternalError,
            "The expression has no semantic type. Was the query analyzed?");

    private string VariableName(VariableExpression variable) =>
        Text(variable.Token)[1..];

    private static string CteName(string variableName) =>
        "fq_" + variableName;

    private string Key(Token token) =>
        token.Type == TokenType.StringLiteral
            ? LiteralValues.Unquote(Text(token))
            : Text(token);

    private string Text(Token token) =>
        _source.Span[token.StartPosition..token.EndPosition].ToString();

    private IDisposable EnterScope()
    {
        var snapshot = new Dictionary<string, PlanBinding>(
            _bindings,
            StringComparer.Ordinal);

        return new ScopeGuard(this, snapshot);
    }

    private sealed class ScopeGuard(
        PlanBuilder builder,
        Dictionary<string, PlanBinding> snapshot) : IDisposable
    {
        public void Dispose() => builder._bindings = snapshot;
    }

    private abstract record PlanBinding;

    private sealed record ScalarBinding(PlanExpression Expression) : PlanBinding;

    private sealed record RelationalBinding(
        string SqlName,
        ObjectType Schema) : PlanBinding;
}
