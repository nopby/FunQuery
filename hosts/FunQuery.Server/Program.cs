using FunQuery.AspNetCore;
using FunQuery.Caching;
using FunQuery.Playground;
using FunQuery.Server.Sources;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFunQuery(options =>
{
    options.EnableOpenApi = ReadBool(
        builder.Configuration["OpenApi:Enabled"],
        defaultValue: true);
    options.EnableApiDocs = ReadBool(
        builder.Configuration["ApiDocs:Enabled"],
        defaultValue: true);
});

var orders = new OrdersSource();
var people = new PeopleSource();

builder.Services.AddFunQuerySources(sources => sources
    .Add(orders)
    .Add(people));

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
    SourceNames = [orders.Name, people.Name],
    Examples =
    [
        .. PlaygroundExamples.Create(),
        new(
            "Orders: recent dates",
            "$source(@source).$filter(orderedOn gt $date('2024-02-01'))" +
            ".$select(id, customer, orderedOn)",
            orders.Name,
            null),
    ],
};

builder.Services.AddSingleton(playground);

var app = builder.Build();

// Diambil dari DI supaya test/host bisa mengganti opsi tanpa mengubah Program.
var playgroundOptions = app.Services.GetRequiredService<PlaygroundOptions>();

if (playgroundOptions.Enabled)
{
    app.UsePlaygroundSecurityHeaders();
    app.UsePlaygroundRedirect();
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFunQueryPlayground(playgroundOptions);
}

var funQueryOptions = app.Services.GetRequiredService<FunQueryOptions>();

if (funQueryOptions.EnableApiDocs)
    app.UseFunQueryApiDocsSecurityHeaders(funQueryOptions.ApiDocsRoute);

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
