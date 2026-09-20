namespace FunQuery.Tests;

public class QueryLimitsTests
{
    [Fact]
    public void Defaults_AreTheDocumentedOnes()
    {
        var limits = QueryLimits.Default;

        Assert.Equal(2048, limits.MaxInputLength);
        Assert.Equal(1024, limits.MaxTokens);
        Assert.Equal(64, limits.MaxDepth);
    }

    [Fact]
    public void NewInstance_StartsWithTheDefaults()
    {
        var limits = new QueryLimits();

        Assert.Equal(QueryLimits.Default.MaxInputLength, limits.MaxInputLength);
        Assert.Equal(QueryLimits.Default.MaxTokens, limits.MaxTokens);
        Assert.Equal(QueryLimits.Default.MaxDepth, limits.MaxDepth);
    }

    [Fact]
    public void InitializerValues_AreApplied()
    {
        var limits = new QueryLimits { MaxInputLength = 100, MaxTokens = 50, MaxDepth = 8 };

        Assert.Equal(100, limits.MaxInputLength);
        Assert.Equal(50, limits.MaxTokens);
        Assert.Equal(8, limits.MaxDepth);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MaxInputLength_MustBeAtLeastOne(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new QueryLimits { MaxInputLength = value });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MaxTokens_MustBeAtLeastOne(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new QueryLimits { MaxTokens = value });
    }

    [Fact]
    public void MaxDepth_MustBeAtLeastOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new QueryLimits { MaxDepth = 0 });
    }

    [Fact]
    public void MaxDepth_CannotExceedTheAbsoluteMaximum()
    {
        var atTheLimit = new QueryLimits { MaxDepth = QueryLimits.AbsoluteMaxDepth };
        Assert.Equal(QueryLimits.AbsoluteMaxDepth, atTheLimit.MaxDepth);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new QueryLimits { MaxDepth = QueryLimits.AbsoluteMaxDepth + 1 });
    }
}
