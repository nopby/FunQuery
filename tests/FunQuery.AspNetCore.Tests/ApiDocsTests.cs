using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FunQuery.AspNetCore.Tests;

/// <summary>M7.2: Scalar UI and its security headers.</summary>
public class ApiDocsTests
{
    private static WebApplicationFactory<Program> CreateFactory(
        Action<FunQueryOptions>? configure = null) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.AddFunQuery(options =>
                    {
                        options.EnableOpenApi = true;
                        options.EnableApiDocs = true;
                        configure?.Invoke(options);
                    });
                }));

    [Fact]
    public void ApiDocsRequiresOpenApi()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() =>
            services.AddFunQuery(options => options.EnableApiDocs = true));
    }

    [Fact]
    public async Task DocsServeScalarWhenEnabled()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync("/docs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("FunQuery API", body);
        Assert.Contains("/openapi/v1.json", body);
        Assert.Contains("scalar", body.ToLowerInvariant());
        Assert.DoesNotContain("https://", body);
    }

    [Fact]
    public async Task DocsAreAbsentWhenDisabled()
    {
        using var factory = CreateFactory(options => options.EnableApiDocs = false);

        var response = await factory.CreateClient().GetAsync("/docs");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DocsRouteCanBeCustomized()
    {
        using var factory = CreateFactory(options =>
        {
            options.ApiDocsRoute = "/api-docs";
        });

        var client = factory.CreateClient();

        var response = await client.GetAsync("/api-docs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());

        var script = await client.GetAsync("/api-docs/scalar.js");
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);

        var defaultResponse = await client.GetAsync("/docs");
        Assert.Equal(HttpStatusCode.NotFound, defaultResponse.StatusCode);
    }

    [Fact]
    public async Task DocsUseTheConfiguredOpenApiRoute()
    {
        using var factory = CreateFactory(options =>
        {
            options.Route = "/api/run";
            options.OpenApiRoute = "/openapi/custom.json";
        });

        var body = await factory.CreateClient()
            .GetStringAsync("/docs");

        Assert.Contains("/openapi/custom.json", body);
    }

    [Fact]
    public async Task DocsSetSecurityHeadersAndMatchingNonce()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync("/docs/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("no-store", response.Headers.GetValues("Cache-Control").Single());

        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'self'", policy);
        Assert.Contains("script-src 'self' 'nonce-", policy);
        Assert.Contains("frame-ancestors 'none'", policy);

        var nonce = Regex.Match(policy, "nonce-([^']+)").Groups[1].Value;
        Assert.NotEmpty(nonce);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains($"nonce=\"{nonce}\"", body);
    }
}
