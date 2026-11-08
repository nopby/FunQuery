using FunQuery.SemanticTypes;

namespace FunQuery.Caching.Tests;

public sealed class QueryCacheKeyBuilderTests
{
    private static readonly QueryCacheOptions Options = new()
    {
        KeyPrefix = "fq:q:v1:",
        Version = "1",
    };

    private static QueryEnvironment Variables(
        params (string Name, SemanticType Type, object? Value)[] variables)
    {
        var environment = QueryEnvironment.Empty;

        foreach (var (name, type, value) in variables)
            environment = environment.With(name, new QueryParameter(type, value));

        return environment;
    }

    private static string Key(
        string query = "$source(@source).$count()",
        string? source = "orders",
        QueryEnvironment? variables = null,
        string? scope = "tenant-a",
        QueryCacheOptions? options = null) =>
        new QueryCacheKeyBuilder(options ?? Options).Create(
            query,
            source,
            variables ?? QueryEnvironment.Empty,
            scope);

    [Fact]
    public void SameRequestProducesTheSameKey()
    {
        Assert.Equal(Key(), Key());
    }

    [Fact]
    public void VariableOrderDoesNotChangeTheKey()
    {
        var first = Variables(
            ("min", SemanticTypeOptions.Int, 1),
            ("name", SemanticTypeOptions.String, "Ana"));

        var second = Variables(
            ("name", SemanticTypeOptions.String, "Ana"),
            ("min", SemanticTypeOptions.Int, 1));

        Assert.Equal(Key(variables: first), Key(variables: second));
    }

    [Fact]
    public void SourceValueIsNotPartOfTheKey()
    {
        var rows = new[]
        {
            new Dictionary<string, object?> { ["id"] = 1 },
        };

        var withSource = Variables(
            ("source", new ArrayType(SemanticTypeOptions.Int), rows),
            ("min", SemanticTypeOptions.Int, 1));

        var withoutSource = Variables(("min", SemanticTypeOptions.Int, 1));

        Assert.Equal(Key(variables: withSource), Key(variables: withoutSource));
    }

    [Fact]
    public void QuerySourceScopeAndVersionChangeTheKey()
    {
        var baseline = Key();

        Assert.NotEqual(baseline, Key(query: "$source(@source).$count().$take(1)"));
        Assert.NotEqual(baseline, Key(source: "people"));
        Assert.NotEqual(baseline, Key(scope: "tenant-b"));

        var versioned = new QueryCacheOptions
        {
            KeyPrefix = "fq:q:v2:",
            Version = "2",
        };

        Assert.NotEqual(baseline, Key(options: versioned));
    }

    [Fact]
    public void VariableTypeAndValueChangeTheKey()
    {
        var intValue = Variables(("min", SemanticTypeOptions.Int, 1));
        var longValue = Variables(("min", SemanticTypeOptions.Long, 1L));
        var otherValue = Variables(("min", SemanticTypeOptions.Int, 2));

        Assert.NotEqual(Key(variables: intValue), Key(variables: longValue));
        Assert.NotEqual(Key(variables: intValue), Key(variables: otherValue));
    }

    [Fact]
    public void NullScopeIsStable()
    {
        Assert.Equal(Key(scope: null), Key(scope: null));
        Assert.NotEqual(Key(scope: null), Key(scope: "tenant-a"));
    }

    [Fact]
    public void NamedKeyIncludesAppTenantVersionAndQuery()
    {
        var builder = new QueryCacheKeyBuilder(Options);

        string Named(
            string app = "app",
            string tenant = "tenant",
            int version = 1,
            string query = "$source(@source).$count()") =>
            builder.CreateNamed(
                app,
                tenant,
                "orders",
                version,
                query,
                "orders",
                QueryEnvironment.Empty);

        var baseline = Named();

        Assert.Equal(baseline, Named());
        Assert.NotEqual(baseline, Named(app: "other"));
        Assert.NotEqual(baseline, Named(tenant: "other"));
        Assert.NotEqual(baseline, Named(version: 2));
        Assert.NotEqual(baseline, Named(query: "$source(@source).$select(id)"));
    }

    [Fact]
    public void NamedAndAdHocKeysDoNotCollide()
    {
        var builder = new QueryCacheKeyBuilder(Options);

        var adHoc = builder.Create(
            "$source(@source).$count()",
            "orders",
            QueryEnvironment.Empty,
            "app");

        var named = builder.CreateNamed(
            "app",
            "tenant",
            "orders",
            1,
            "$source(@source).$count()",
            "orders",
            QueryEnvironment.Empty);

        Assert.NotEqual(adHoc, named);
    }
}
