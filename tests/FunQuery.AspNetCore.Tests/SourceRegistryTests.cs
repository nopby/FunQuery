using FunQuery.AspNetCore.Tests.Support;
using FunQuery.SemanticTypes;

namespace FunQuery.AspNetCore.Tests;

/// <summary>M5.3: the source whitelist is configured once and frozen.</summary>
public class SourceRegistryTests
{
    private static TestSource Source(string name = "orders") =>
        new(name, TestSource.SchemaOf(("id", SemanticTypeOptions.Int)));

    [Fact]
    public void AddThenTryGetReturnsTheSameSource()
    {
        var registry = new SourceRegistry();
        var source = Source();

        registry.Add(source);

        Assert.True(registry.TryGet("orders", out var found));
        Assert.Same(source, found);
        Assert.False(registry.IsFrozen);
    }

    [Fact]
    public void NamesAreCaseSensitive()
    {
        var registry = new SourceRegistry().Add(Source("Orders"));

        Assert.False(registry.TryGet("orders", out _));
        Assert.True(registry.TryGet("Orders", out _));
    }

    [Fact]
    public void ADuplicateNameIsRejectedAtStartup()
    {
        var registry = new SourceRegistry().Add(Source());

        Assert.Throws<InvalidOperationException>(() => registry.Add(Source()));
    }

    [Fact]
    public void AddingAfterFreezeIsRejected()
    {
        var registry = new SourceRegistry().Freeze();

        Assert.Throws<InvalidOperationException>(() => registry.Add(Source()));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    [InlineData("slash/name")]
    public void InvalidNamesAreRejected(string name)
    {
        var registry = new SourceRegistry();

        Assert.Throws<ArgumentException>(() => registry.Add(Source(name)));
    }

    [Fact]
    public void ANameLongerThanTheLimitIsRejected()
    {
        var registry = new SourceRegistry();
        var name = new string('a', SourceRegistry.MaxNameLength + 1);

        Assert.Throws<ArgumentException>(() => registry.Add(Source(name)));
    }

    [Fact]
    public void AllowedNameCharactersAreAccepted()
    {
        var registry = new SourceRegistry().Add(Source("orders-2024.v1"));

        Assert.True(registry.TryGet("orders-2024.v1", out _));
    }

    [Fact]
    public void TheEmptyRegistryIsFrozenAndHasNoSources()
    {
        Assert.True(SourceRegistry.Empty.IsFrozen);
        Assert.False(SourceRegistry.Empty.TryGet("orders", out _));
    }
}
