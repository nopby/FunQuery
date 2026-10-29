using System.Net;
using FunQuery.Server.Playground;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FunQuery.AspNetCore.Tests;

/// <summary>
/// M6.0a: the query playground is opt-in, served from wwwroot, and never exposes source names
/// unless explicitly enabled.
/// </summary>
public class PlaygroundTests
{
    private static WebApplicationFactory<Program> CreateFactory(
        PlaygroundOptions? options = null) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    if (options is not null)
                        services.AddSingleton(options);
                }));

    [Fact]
    public async Task ThePlaygroundPageIsServedWhenEnabled()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync("/playground/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("FunQuery Playground", html);
        Assert.Contains("/playground/playground.js", html);
        Assert.Contains("href=\"/docs\"", html);
        Assert.Contains("id=\"copy-curl\"", html);
        Assert.Contains("id=\"copy-body\"", html);
        Assert.Contains("id=\"request-body\"", html);
        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
    }

    [Fact]
    public async Task ThePlaygroundRedirectsWithoutATrailingSlash()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/playground");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/playground/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task TheStaticAssetsAreServed()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var script = await client.GetAsync("/playground/playground.js");
        var style = await client.GetAsync("/playground/playground.css");

        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Contains(
            "javascript",
            script.Content.Headers.ContentType?.MediaType ?? string.Empty);
        var scriptBody = await script.Content.ReadAsStringAsync();
        Assert.Contains("fetch(\"/query\"", scriptBody);
        Assert.Contains("navigator.clipboard", scriptBody);
        Assert.Contains("curl -X POST", scriptBody);
        Assert.Contains("request-body", scriptBody);

        Assert.Equal(HttpStatusCode.OK, style.StatusCode);
        Assert.Equal("text/css", style.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task TheConfigListsSourcesAndExamplesWhenAllowed()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync("/playground/config");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"exposeSourceNames\":true", json);
        Assert.Contains("\"orders\"", json);
        Assert.Contains("\"people\"", json);
        Assert.Contains("Inline: count", json);
        Assert.Contains("Variables: inline filter", json);
        Assert.Contains("PostgreSQL: orders over 100", json);
    }

    [Fact]
    public async Task ThePlaygroundSetsSecurityHeaders()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync("/playground/");

        Assert.True(response.Headers.TryGetValues("Content-Security-Policy", out var csp));
        Assert.Contains("default-src 'self'", csp.Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task ThePlaygroundIsNotServedWhenDisabled()
    {
        using var factory = CreateFactory(new PlaygroundOptions { Enabled = false });
        var client = factory.CreateClient();

        var page = await client.GetAsync("/playground/");
        var config = await client.GetAsync("/playground/config");

        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, config.StatusCode);
    }

    [Fact]
    public async Task SourceNamesAreHiddenWhenNotExposed()
    {
        var options = new PlaygroundOptions
        {
            Enabled = true,
            ExposeSourceNames = false,
            SourceNames = ["orders"],
            Examples =
            [
                new PlaygroundExample("Inline", "$source([1]).$count()", null, null),
                new PlaygroundExample("Orders", "$source(@source).$count()", "orders", null),
            ],
        };

        using var factory = CreateFactory(options);

        var response = await factory.CreateClient().GetAsync("/playground/config");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"exposeSourceNames\":false", json);
        Assert.Contains("\"sources\":[]", json);
        Assert.Contains("Inline", json);
        Assert.DoesNotContain("\"orders\"", json);
        Assert.DoesNotContain("\"Orders\"", json);
    }
}
