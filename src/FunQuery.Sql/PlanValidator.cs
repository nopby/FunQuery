using FunQuery.Enums;
using FunQuery.SemanticTypes;
using FunQuery.Sql.Plan;

namespace FunQuery.Sql;

/// <summary>
/// Memvalidasi plan terhadap capability provider sebelum eksekusi. Fitur yang tidak didukung
/// ditolak dengan <c>NOT_SUPPORTED</c>, bukan dieksekusi dengan hasil yang berbeda.
/// </summary>
public static class PlanValidator
{
    public static void Validate(
        RelationalPlan plan,
        ProviderCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(plan);

        VisitPlan(plan, capabilities);
    }

    private static void VisitPlan(
        RelationalPlan plan,
        ProviderCapabilities capabilities)
    {
        switch (plan)
        {
            case Scan:
                return;

            case Values values:
                Require(capabilities, ProviderCapabilities.InlineSources, "inline sources");
                foreach (var row in values.Rows)
                    VisitExpression(row, capabilities);
                return;

            case Filter filter:
                Require(capabilities, ProviderCapabilities.Filtering, "filtering");
                VisitPlan(filter.Input, capabilities);
                VisitExpression(filter.Predicate, capabilities);
                return;

            case Project project:
                Require(capabilities, ProviderCapabilities.Projection, "projection");
                VisitPlan(project.Input, capabilities);

                foreach (var projection in project.Projections)
                {
                    if (project.ProducesObject &&
                        projection.Expression is CurrentRow current &&
                        current.Type is ObjectType or ArrayType)
                    {
                        Require(
                            capabilities,
                            ProviderCapabilities.JsonProjection,
                            "JSON projection");
                    }

                    VisitExpression(projection.Expression, capabilities);
                }

                return;

            case Sort sort:
                Require(capabilities, ProviderCapabilities.Sorting, "sorting");
                VisitPlan(sort.Input, capabilities);
                VisitExpression(sort.Key, capabilities);
                return;

            case Take take:
                Require(capabilities, ProviderCapabilities.Pagination, "pagination");
                VisitPlan(take.Input, capabilities);
                VisitExpression(take.Count, capabilities);
                return;

            case Skip skip:
                Require(capabilities, ProviderCapabilities.Pagination, "pagination");
                VisitPlan(skip.Input, capabilities);
                VisitExpression(skip.Count, capabilities);
                return;

            case Distinct distinct:
                Require(capabilities, ProviderCapabilities.Distinct, "distinct");
                if (distinct.Key is not null)
                    Require(capabilities, ProviderCapabilities.DistinctOn, "distinct on a key");
                VisitPlan(distinct.Input, capabilities);
                if (distinct.Key is not null)
                    VisitExpression(distinct.Key, capabilities);
                return;

            case Aggregate aggregate:
                Require(capabilities, ProviderCapabilities.Aggregation, "aggregation");
                VisitPlan(aggregate.Input, capabilities);
                return;

            case Any any:
                Require(capabilities, ProviderCapabilities.Aggregation, "aggregation");
                VisitPlan(any.Input, capabilities);
                if (any.Predicate is not null)
                {
                    Require(capabilities, ProviderCapabilities.Filtering, "filtering");
                    VisitExpression(any.Predicate, capabilities);
                }
                return;

            case First first:
                Require(capabilities, ProviderCapabilities.Aggregation, "aggregation");
                VisitPlan(first.Input, capabilities);
                if (first.Predicate is not null)
                {
                    Require(capabilities, ProviderCapabilities.Filtering, "filtering");
                    VisitExpression(first.Predicate, capabilities);
                }
                return;

            default:
                throw new QueryException(
                    QueryErrorCode.NotSupported,
                    "The provider does not support this plan node.");
        }
    }

    private static void VisitExpression(
        PlanExpression expression,
        ProviderCapabilities capabilities)
    {
        switch (expression)
        {
            case Column or CurrentRow or Literal or Parameter:
                return;

            case Binary binary:
                if (binary.Operator is
                    ComparisonOperator.Contains or
                    ComparisonOperator.StartsWith or
                    ComparisonOperator.EndsWith)
                {
                    Require(capabilities, ProviderCapabilities.StringOperations, "string operations");
                }

                // A membership list is not a JSON array; IN (1, 2) must not require JSON.
                if (binary.Operator is ComparisonOperator.In &&
                    binary.Right is ArrayConstruct inList)
                {
                    VisitExpression(binary.Left, capabilities);

                    foreach (var element in inList.Elements)
                        VisitExpression(element, capabilities);

                    return;
                }

                VisitExpression(binary.Left, capabilities);
                VisitExpression(binary.Right, capabilities);
                return;

            case Logical logical:
                VisitExpression(logical.Left, capabilities);
                VisitExpression(logical.Right, capabilities);
                return;

            case Not not:
                VisitExpression(not.Operand, capabilities);
                return;

            case Call call:
                if (call.Name is "$lower" or "$upper" or "$length")
                {
                    Require(capabilities, ProviderCapabilities.StringOperations, "string operations");
                }

                if (call.Name is "$date")
                    Require(capabilities, ProviderCapabilities.DateOperations, "date operations");

                foreach (var argument in call.Arguments)
                    VisitExpression(argument, capabilities);
                return;

            case ObjectConstruct obj:
                Require(capabilities, ProviderCapabilities.JsonProjection, "JSON projection");
                foreach (var field in obj.Fields)
                    VisitExpression(field.Value, capabilities);
                return;

            case ArrayConstruct array:
                Require(capabilities, ProviderCapabilities.JsonProjection, "JSON projection");
                foreach (var element in array.Elements)
                    VisitExpression(element, capabilities);
                return;

            case IndexExpression index:
                Require(capabilities, ProviderCapabilities.WindowFunctions, "window functions");
                if (index.Base is not null)
                    VisitExpression(index.Base, capabilities);
                return;

            case Field field:
                VisitExpression(field.Target, capabilities);
                return;

            case Subquery subquery:
                VisitPlan(subquery.Plan, capabilities);
                return;

            default:
                throw new QueryException(
                    QueryErrorCode.NotSupported,
                    "The provider does not support this plan expression.");
        }
    }

    private static void Require(
        ProviderCapabilities capabilities,
        ProviderCapabilities required,
        string feature)
    {
        if ((capabilities & required) != required)
        {
            throw new QueryException(
                QueryErrorCode.NotSupported,
                $"The provider does not support {feature}.");
        }
    }
}
