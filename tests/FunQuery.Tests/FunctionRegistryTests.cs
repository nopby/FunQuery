using FunQuery.Enums;
using FunQuery.SemanticTypes;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

public class FunctionRegistryTests
{
    private static FunctionDefinition Definition(string name) =>
        new() { Name = name, Parameters = [] };

    // A third-party extension: "$count" returns the number of elements of an array.
    private sealed class CountExtension : IQueryExtension
    {
        public string Name => "count-extension";

        public void Register(FunctionRegistry registry) =>
            registry.Add(new FunctionDefinition
            {
                Name = "$count",
                Parameters = [],
                TargetRule = TargetRule.Required,
                AcceptsTarget = target => target is ArrayType,
                ReturnType = _ => SemanticTypeOptions.Int,
            });
    }

    // Tries to claim a name that the core extension already owns.
    private sealed class DuplicateSourceExtension : IQueryExtension
    {
        public string Name => "duplicate";

        public void Register(FunctionRegistry registry) =>
            registry.Add(Definition("$source"));
    }

    // ------------------------------------------------------------------
    // Core registry
    // ------------------------------------------------------------------

    [Fact]
    public void CoreFunctions_RegistersSourceAndFilter()
    {
        var registry = QueryPipeline.CoreRegistry();

        Assert.True(registry.TryGet("$source", out var source));
        Assert.Equal("$source", source.Name);
        Assert.True(registry.TryGet("$filter", out _));
        Assert.False(registry.TryGet("$nope", out _));
        Assert.Contains(registry.Functions, f => f.Name == "$source");
        Assert.Contains(registry.Functions, f => f.Name == "$filter");
    }

    [Fact]
    public void FunctionNames_AreCaseSensitive()
    {
        var registry = QueryPipeline.CoreRegistry();

        Assert.False(registry.TryGet("$SOURCE", out _));
    }

    // ------------------------------------------------------------------
    // Registration rules (configuration errors fail at startup)
    // ------------------------------------------------------------------

    [Fact]
    public void RegisteringTheSameNameTwice_IsRejected()
    {
        var registry = QueryPipeline.CoreRegistry();

        var exception = Assert.Throws<InvalidOperationException>(
            () => registry.AddExtension(new DuplicateSourceExtension()));

        Assert.Contains("already registered", exception.Message);
        Assert.Contains("core", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("$")]
    [InlineData("count")]
    [InlineData("$Count")]
    [InlineData("$1count")]
    [InlineData("$has-dash")]
    [InlineData("$has space")]
    public void InvalidFunctionName_IsRejected(string name)
    {
        var registry = new FunctionRegistry();

        Assert.Throws<ArgumentException>(() => registry.Add(Definition(name)));
    }

    [Theory]
    [InlineData("$count")]
    [InlineData("$_private")]
    [InlineData("$take2")]
    public void ValidFunctionName_IsAccepted(string name)
    {
        var registry = new FunctionRegistry().Add(Definition(name));

        Assert.True(registry.TryGet(name, out _));
    }

    [Fact]
    public void VariadicFunction_NeedsAParameterToTypeItsArguments()
    {
        var definition = new FunctionDefinition
        {
            Name = "$many",
            Parameters = [],
            IsVariadic = true,
        };

        Assert.Throws<ArgumentException>(() => new FunctionRegistry().Add(definition));
    }

    [Fact]
    public void RequiredParameter_CannotFollowAnOptionalOne()
    {
        var definition = new FunctionDefinition
        {
            Name = "$bad",
            Parameters =
            [
                ParameterDefinition.Of<IntType>("first", "int", required: false),
                ParameterDefinition.Of<IntType>("second", "int"),
            ],
        };

        Assert.Throws<ArgumentException>(() => new FunctionRegistry().Add(definition));
    }

    // ------------------------------------------------------------------
    // Freezing
    // ------------------------------------------------------------------

    [Fact]
    public void FrozenRegistry_RejectsNewFunctions()
    {
        var registry = QueryPipeline.CoreRegistry().Freeze();

        Assert.True(registry.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => registry.Add(Definition("$late")));
    }

    [Fact]
    public void Analyzing_FreezesTheRegistry_AndItCanBeReused()
    {
        var registry = QueryPipeline.CoreRegistry();
        Assert.False(registry.IsFrozen);

        QueryPipeline.Analyze("$source([1])", functions: registry);
        QueryPipeline.Analyze("$source(['a'])", functions: registry);

        Assert.True(registry.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => registry.Add(Definition("$late")));
    }

    // ------------------------------------------------------------------
    // Extensions
    // ------------------------------------------------------------------

    [Fact]
    public void ExtensionFunction_IsUsableInQueries()
    {
        var registry = QueryPipeline.CoreRegistry().AddExtension(new CountExtension());

        var result = QueryPipeline.Analyze(
            "$source([{id: 1}, {id: 2}]).$count()",
            functions: registry);

        Assert.Equal(SemanticTypeOptions.Int, result.SemanticType);
    }

    [Fact]
    public void ExtensionFunction_HonoursItsTargetRule()
    {
        var registry = QueryPipeline.CoreRegistry().AddExtension(new CountExtension());

        QueryAssert.Fails(
            QueryErrorCode.InvalidTarget,
            () => QueryPipeline.Analyze("$count()", functions: registry));
    }

    [Fact]
    public void ExtensionFunction_IsUnknownWhenTheExtensionIsNotRegistered()
    {
        QueryAssert.Fails(
            QueryErrorCode.UnknownFunction,
            () => QueryPipeline.Analyze("$source([1]).$count()"));
    }
}
