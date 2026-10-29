using FunQuery.AspNetCore;
using FunQuery.Server.Playground;
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

var playground = new PlaygroundOptions
{
    Enabled = ReadBool(builder.Configuration["Playground:Enabled"], defaultValue: true),
    ExposeSourceNames = ReadBool(
        builder.Configuration["Playground:ExposeSourceNames"],
        defaultValue: true),
    SourceNames = [orders.Name, people.Name],
    Examples = PlaygroundExamples.Create(),
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

/// <summary>Public agar integration test dapat memakai WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program
{
}
