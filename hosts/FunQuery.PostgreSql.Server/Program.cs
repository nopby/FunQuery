using FunQuery.AspNetCore;
using FunQuery.PostgreSql;
using FunQuery.SemanticTypes;
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
builder.Services.AddFunQuery();

var ordersSchema = new ObjectType(
    new Dictionary<string, SemanticType>(StringComparer.Ordinal)
    {
        ["id"] = SemanticTypeOptions.Int,
        ["customer"] = SemanticTypeOptions.String,
        ["total"] = SemanticTypeOptions.Decimal,
        ["ordered_on"] = SemanticTypeOptions.Date,
    });

builder.Services.AddFunQuerySources(sources => sources.Add(
    new PostgreSqlSource("orders", ordersSchema, "orders", dataSource)
        .AsQuerySource("orders", ordersSchema)));

var app = builder.Build();

app.MapFunQueryEndpoints();

app.Run();

/// <summary>Public agar integration test dapat memakai WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program
{
}
