using FunQuery.Enums;
using FunQuery.SemanticTypes;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

/// <summary>
/// M5.6: cooperative execution timeout, cancellation, and result-size limit.
/// Contract in docs/HttpApi.md.
/// </summary>
public class ExecutionLimitTests
{
    private static List<object?> Numbers(int count) =>
        Enumerable.Range(0, count).Select(value => (object?)value).ToList();

    [Fact]
    public void AQueryOverTheTimeLimitFails()
    {
        var engine = new QueryEngine(
            new QueryLimits { MaxExecutionTime = TimeSpan.FromTicks(1) });

        var environment = QueryEnvironment.From(
            "items",
            new QueryParameter(
                SemanticTypeOptions.Array(SemanticTypeOptions.Int),
                Numbers(10_000)));

        QueryAssert.Fails(
            QueryErrorCode.ExecutionTimeout,
            () => engine.Execute("$source(@items).$filter(~ gt 0)", environment));
    }

    [Fact]
    public void ACancelledTokenStopsExecution()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var engine = new QueryEngine();

        Assert.ThrowsAny<OperationCanceledException>(
            () => engine.Execute(
                "$source([1, 2, 3]).$count()",
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public void AResultOverTheItemLimitFails()
    {
        var engine = new QueryEngine(new QueryLimits { MaxResultItems = 2 });

        QueryAssert.Fails(
            QueryErrorCode.ResultTooLarge,
            () => engine.Execute("$source([1, 2, 3])"));
    }

    [Fact]
    public void AResultExactlyAtTheLimitSucceeds()
    {
        var engine = new QueryEngine(new QueryLimits { MaxResultItems = 3 });

        Assert.Equal(
            "[1,2,3]",
            engine.Execute("$source([1, 2, 3])").ToJson());
    }

    [Fact]
    public void AScalarResultIsNotSubjectToTheItemLimit()
    {
        var engine = new QueryEngine(new QueryLimits { MaxResultItems = 0 });

        Assert.Equal(
            "3",
            engine.Execute("$source([1, 2, 3]).$count()").ToJson());
    }

    [Fact]
    public void AnEmptyArrayIsAllowedAtAZeroLimit()
    {
        var engine = new QueryEngine(new QueryLimits { MaxResultItems = 0 });

        Assert.Equal("[]", engine.Execute("$source([])").ToJson());
    }

    [Fact]
    public void ANormalQueryFinishesWithinTheDefaultLimit()
    {
        Assert.Equal(
            "3",
            new QueryEngine().Execute("$source([1, 2, 3]).$count()").ToJson());
    }
}
