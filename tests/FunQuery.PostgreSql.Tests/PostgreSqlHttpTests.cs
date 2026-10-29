using System.Net;
using System.Text;
using System.Text.Json;
using FunQuery.SemanticTypes;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace FunQuery.PostgreSql.Tests;

/// <summary>
/// M6.5: the HTTP host serves PostgreSQL and in-memory sources through the same endpoints.
/// Set FUNQUERY_POSTGRES to run.
/// </summary>
public sealed class PostgreSqlHttpTests : IAsyncLifetime
{
    private string? _connectionString;
    private NpgsqlDataSource? _dataSource;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _connectionString = Environment.GetEnvironmentVariable("FUNQUERY_POSTGRES");

        if (string.IsNullOrWhiteSpace(_connectionString))
            return;

        _dataSource = NpgsqlDataSource.Create(_connectionString);

        await using (var command = _dataSource.CreateCommand("""
            DROP TABLE IF EXISTS "orders";
            CREATE TABLE "orders" (
                "id" integer PRIMARY KEY,
                "customer" text NOT NULL,
                "total" numeric NOT NULL,
                "ordered_on" date NOT NULL
            );
            INSERT INTO "orders" ("id", "customer", "total", "ordered_on") VALUES
                (1, 'Ana', 120.50, DATE '2024-01-15'),
                (2, 'Budi', 89.90, DATE '2024-02-03'),
                (3, 'Citra', 250.00, DATE '2024-03-21');
            """))
        {
            await command.ExecuteNonQueryAsync();
        }

        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();

        if (_factory is not null)
            await _factory.DisposeAsync();

        if (_dataSource is not null)
        {
            await using (var command = _dataSource.CreateCommand(
                "DROP TABLE IF EXISTS \"orders\""))
            {
                await command.ExecuteNonQueryAsync();
            }

            await _dataSource.DisposeAsync();
        }
    }

    private static string Url(string query, string? source = null) =>
        source is null
            ? $"/query?q={Uri.EscapeDataString(query)}"
            : $"/query?source={Uri.EscapeDataString(source)}&q={Uri.EscapeDataString(query)}";

    private static StringContent Json(string body) =>
        new(body, Encoding.UTF8, "application/json");

    private static string Body(string query, string? source = null)
    {
        var json = $"\"query\":{JsonSerializer.Serialize(query)}";

        if (source is not null)
            json += $",\"source\":{JsonSerializer.Serialize(source)}";

        return "{" + json + "}";
    }

    [Fact]
    public async Task GetAndPostUseThePostgreSqlSource()
    {
        if (_client is null) return;

        const string query =
            "$source(@source).$filter(total gt 100).$select(id, customer)";

        var get = await _client.GetAsync(Url(query, "orders"));
        var post = await _client.PostAsync("/query", Json(Body(query, "orders")));

        var expected = "[{\"id\":1,\"customer\":\"Ana\"}," +
                       "{\"id\":3,\"customer\":\"Citra\"}]";

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.Equal(expected, await get.Content.ReadAsStringAsync());
        Assert.Equal(expected, await post.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InMemorySourcesStillWorkInTheSameHost()
    {
        if (_client is null) return;

        var people = await _client.GetAsync(Url(
            "$source(@source).$filter(active).$count()",
            "people"));

        var inline = await _client.GetAsync(Url("$source([1, 2]).$count()"));

        Assert.Equal(HttpStatusCode.OK, people.StatusCode);
        Assert.Equal("2", await people.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, inline.StatusCode);
        Assert.Equal("2", await inline.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownFieldsFailBeforeSql()
    {
        if (_client is null) return;

        var response = await _client.GetAsync(Url(
            "$source(@source).$filter(nope eq 1)",
            "orders"));

        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("UNKNOWN_IDENTIFIER", body);
        Assert.DoesNotContain("SELECT", body);
        Assert.DoesNotContain("Host=", body);
        Assert.DoesNotContain("Password", body);
    }

    [Fact]
    public async Task PaginationWithoutSortUsesTheStructuredError()
    {
        if (_client is null) return;

        var response = await _client.GetAsync(Url(
            "$source(@source).$take(1)",
            "orders"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ORDER_REQUIRED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ConcurrentRequestsUseTheirOwnParameters()
    {
        if (_client is null) return;

        var requests = Enumerable.Range(0, 12).Select(async value =>
        {
            var response = await _client.GetAsync(Url(
                "$source(@source).$filter(id gt @min).$count()",
                "orders") + $"&v.min={value}");

            var body = await response.Content.ReadAsStringAsync();
            var expected = Math.Max(0, 3 - value).ToString();

            return (response.StatusCode, Expected: expected, Actual: body);
        });

        var results = await Task.WhenAll(requests);

        foreach (var result in results)
        {
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal(result.Expected, result.Actual);
        }
    }
}
