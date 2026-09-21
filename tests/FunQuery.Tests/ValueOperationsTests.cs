using FunQuery.Execution;

namespace FunQuery.Tests;

/// <summary>Runtime comparison semantics from docs/Operators.md.</summary>
public class ValueOperationsTests
{
    [Fact]
    public void Equal_TreatsNullAsEqualOnlyToNull()
    {
        Assert.True(ValueOperations.Equal(null, null));
        Assert.False(ValueOperations.Equal(null, 1));
        Assert.False(ValueOperations.Equal(1, null));
        Assert.False(ValueOperations.Equal(null, "a"));
        Assert.False(ValueOperations.Equal(false, null));
    }

    [Fact]
    public void Equal_ComparesNumbersOfDifferentTypesByValue()
    {
        Assert.True(ValueOperations.Equal(1, 1L));
        Assert.True(ValueOperations.Equal(1, 1.0m));
        Assert.True(ValueOperations.Equal(1.50m, 1.5m));
        Assert.True(ValueOperations.Equal(2147483648L, 2147483648m));
        Assert.False(ValueOperations.Equal(1, 2L));
        Assert.False(ValueOperations.Equal(1, 1.5m));
    }

    [Fact]
    public void Equal_ComparesStringsOrdinally()
    {
        Assert.True(ValueOperations.Equal("a", "a"));
        Assert.False(ValueOperations.Equal("a", "A"));
        Assert.False(ValueOperations.Equal("a", "a "));
    }

    [Fact]
    public void Equal_NeverEqualsAcrossTypeFamilies()
    {
        Assert.False(ValueOperations.Equal("1", 1));
        Assert.False(ValueOperations.Equal(1, "1"));
        Assert.False(ValueOperations.Equal(true, 1));
        Assert.False(ValueOperations.Equal(true, "true"));
    }

    [Fact]
    public void Equal_ComparesBooleans()
    {
        Assert.True(ValueOperations.Equal(true, true));
        Assert.True(ValueOperations.Equal(false, false));
        Assert.False(ValueOperations.Equal(true, false));
    }

    [Fact]
    public void TryCompare_OrdersNumbersAcrossTypes()
    {
        Assert.True(ValueOperations.TryCompare(1, 2L, out var less));
        Assert.True(less < 0);

        Assert.True(ValueOperations.TryCompare(2.5m, 2, out var greater));
        Assert.True(greater > 0);

        Assert.True(ValueOperations.TryCompare(1.0m, 1, out var same));
        Assert.Equal(0, same);
    }

    [Fact]
    public void TryCompare_OrdersStringsOrdinally()
    {
        Assert.True(ValueOperations.TryCompare("B", "a", out var result));
        Assert.True(result < 0);

        Assert.True(ValueOperations.TryCompare("a", "a", out var same));
        Assert.Equal(0, same);
    }

    [Fact]
    public void TryCompare_HasNoResultForNullOrForValuesWithoutAnOrder()
    {
        Assert.False(ValueOperations.TryCompare(null, 1, out _));
        Assert.False(ValueOperations.TryCompare(1, null, out _));
        Assert.False(ValueOperations.TryCompare(null, null, out _));
        Assert.False(ValueOperations.TryCompare("a", 1, out _));
        Assert.False(ValueOperations.TryCompare(true, false, out _));
    }

    [Fact]
    public void FloatingPointValues_CanStillBeOrdered()
    {
        // The language rejects eq/neq on float and double, but ordering must keep working
        // for providers that expose them.
        Assert.True(ValueOperations.TryCompare(1.5, 2, out var result));
        Assert.True(result < 0);

        Assert.True(ValueOperations.TryCompare(2.5f, 2.5, out var same));
        Assert.Equal(0, same);
    }

    [Fact]
    public void IsTrue_OnlyAcceptsTheBooleanTrue()
    {
        Assert.True(ValueOperations.IsTrue(true));
        Assert.False(ValueOperations.IsTrue(false));
        Assert.False(ValueOperations.IsTrue(null));
        Assert.False(ValueOperations.IsTrue("true"));
        Assert.False(ValueOperations.IsTrue(1));
    }
}
