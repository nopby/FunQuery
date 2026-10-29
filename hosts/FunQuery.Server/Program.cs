using FunQuery.AspNetCore;
using FunQuery.Server.Sources;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFunQuery();
builder.Services.AddFunQuerySources(sources => sources
    .Add(new OrdersSource())
    .Add(new PeopleSource()));

var app = builder.Build();

app.MapFunQueryEndpoints();

app.Run();

/// <summary>Public agar integration test dapat memakai WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program
{
}
