using FunQuery.Enums;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

public sealed class NamedQuerySyntaxTests
{
    [Fact]
    public void NamedAtTheTopLevelProducesAKey()
    {
        var plan = new QueryEngine().Prepare("$named('orders-over-100')");

        Assert.Equal("orders-over-100", plan.NamedQueryKey);
    }

    [Fact]
    public void NormalQueriesHaveNoNamedKey()
    {
        var plan = new QueryEngine().Prepare("$source([1, 2]).$count()");

        Assert.Null(plan.NamedQueryKey);
    }

    [Theory]
    [InlineData("$named()")]
    [InlineData("$named('a', 'b')")]
    public void WrongArgumentCountIsRejected(string query)
    {
        QueryAssert.Fails(
            QueryErrorCode.InvalidArgumentCount,
            () => new QueryEngine().Prepare(query));
    }

    [Theory]
    [InlineData("$named(1)")]
    [InlineData("$named(@key)")]
    public void NonStringKeyIsRejected(string query)
    {
        QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => new QueryEngine().Prepare(query));
    }

    [Fact]
    public void EmptyKeyIsRejected()
    {
        QueryAssert.Fails(
            QueryErrorCode.InvalidNamedQueryKey,
            () => new QueryEngine().Prepare("$named('')"));
    }

    [Theory]
    [InlineData("$named('a').$count()")]
    [InlineData("$source([1]).$named('a')")]
    [InlineData("$source([1]).$map($named('a'))")]
    public void NonTopLevelNamedIsRejected(string query)
    {
        QueryAssert.Fails(
            QueryErrorCode.InvalidNamedQueryPosition,
            () => new QueryEngine().Prepare(query));
    }
}
