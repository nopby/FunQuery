using FunQuery.AspNetCore;
using FunQuery.Caching;
using FunQuery.Playground;
using FunQuery.PostgreSql;
using FunQuery.PostgreSql.Server.Sources;
using FunQuery.SemanticTypes;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration["PostgreSql:ConnectionString"];

if (string.IsNullOrWhiteSpace(connectionString))
    connectionString = Environment.GetEnvironmentVariable("FUNQUERY_POSTGRES");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Set PostgreSql:ConnectionString or the FUNQUERY_POSTGRES environment variable.");
}

var dataSource = NpgsqlDataSource.Create(connectionString);

builder.Services.AddSingleton(dataSource);
builder.Services.AddFunQuery(options =>
{
    options.EnableOpenApi = true;
    options.EnableApiDocs = true;
});

var ordersSchema = new ObjectType(
    new Dictionary<string, SemanticType>(StringComparer.Ordinal)
    {
        ["id"] = SemanticTypeOptions.Int,
        ["customer"] = SemanticTypeOptions.String,
        ["total"] = SemanticTypeOptions.Decimal,
        ["ordered_on"] = SemanticTypeOptions.Date,
    });

builder.Services.AddFunQuerySources(sources => sources
    .Add(new PeopleSource())
    .Add(new PostgreSqlSource("orders", ordersSchema, "orders", dataSource)
        .AsQuerySource("orders", ordersSchema)));

var cacheConnection = builder.Configuration.GetConnectionString("Valkey")
    ?? builder.Configuration["Cache:Valkey"];
var cacheEnabled = ReadBool(
        builder.Configuration["Cache:Enabled"],
        defaultValue: false) ||
    !string.IsNullOrWhiteSpace(cacheConnection);

if (!string.IsNullOrWhiteSpace(cacheConnection))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = cacheConnection;
        options.InstanceName = "funquery:";
    });
}

builder.Services.AddFunQueryQueryCache(options =>
{
    options.Enabled = cacheEnabled;
    options.LocalTtl = TimeSpan.FromSeconds(
        ReadInt(builder.Configuration["Cache:LocalTtlSeconds"], 2));
    options.DistributedTtl = TimeSpan.FromSeconds(
        ReadInt(builder.Configuration["Cache:DistributedTtlSeconds"], 30));
    options.MaxEntryBytes = ReadInt(
        builder.Configuration["Cache:MaxEntryBytes"],
        256 * 1024);
    options.MaxEntries = ReadInt(
        builder.Configuration["Cache:MaxEntries"],
        1024);
    options.KeyPrefix = builder.Configuration["Cache:KeyPrefix"] ?? "fq:q:v1:";
    options.Version = builder.Configuration["Cache:Version"] ?? "1";
});

var playground = new PlaygroundOptions
{
    Enabled = ReadBool(builder.Configuration["Playground:Enabled"], defaultValue: true),
    ExposeSourceNames = ReadBool(
        builder.Configuration["Playground:ExposeSourceNames"],
        defaultValue: true),
    SourceNames = ["orders", "people"],
    Examples =
    [
        .. PlaygroundExamples.Create(),
        new(
            "PostgreSQL: orders by date",
            "$source(@source).$filter(ordered_on gt $date('2024-02-01'))" +
            ".$select(id, customer, ordered_on)",
            "orders",
            null),
    ],
};

builder.Services.AddSingleton(playground);

var app = builder.Build();

var playgroundOptions = app.Services.GetRequiredService<PlaygroundOptions>();

if (playgroundOptions.Enabled)
{
    app.UsePlaygroundSecurityHeaders();
    app.UsePlaygroundRedirect();
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFunQueryPlayground(playgroundOptions);
}

app.UseFunQueryApiDocsSecurityHeaders();

app.MapFunQueryEndpoints();

app.Run();

static bool ReadBool(string? value, bool defaultValue) =>
    bool.TryParse(value, out var parsed) ? parsed : defaultValue;

static int ReadInt(string? value, int defaultValue) =>
    int.TryParse(value, out var parsed) ? parsed : defaultValue;

/// <summary>Public agar integration test dapat memakai WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program
{
}
