using System.Net;
using FunQuery.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FunQuery.AspNetCore.Tests;

public sealed class AuthorizationTests
{
    private const string Query = "$source([1, 2, 3]).$count()";

    private static string Url() => $"/query?q={Uri.EscapeDataString(Query)}";

    [Fact]
    public async Task WithoutAuthorizer_RequestSucceeds()
    {
        using var factory = new WebApplicationFactory<Program>();
        var response = await factory.CreateClient().GetAsync(Url());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizerAllow_RequestSucceeds()
    {
        var authorizer = new CapturingAuthorizer();

        using var factory = CreateFactory(services =>
        {
            services.AddSingleton<IQueryAuthorizer>(authorizer);
            services.AddSingleton<IQueryIdentityProvider, FixedIdentityProvider>();
        });

        var response = await factory.CreateClient().GetAsync(Url());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(authorizer.LastRequest);
    }

    [Fact]
    public async Task AuthorizerForbidden_Returns403()
    {
        var authorizer = new CapturingAuthorizer { Decision = QueryAuthorizationDecision.Forbidden("no access") };

        using var factory = CreateFactory(services => services.AddSingleton<IQueryAuthorizer>(authorizer));

        var response = await factory.CreateClient().GetAsync(Url());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("FORBIDDEN", body);
        Assert.Contains("no access", body);
    }

    [Fact]
    public async Task AuthorizerUnauthenticated_Returns401()
    {
        var authorizer = new CapturingAuthorizer { Decision = QueryAuthorizationDecision.Unauthenticated() };

        using var factory = CreateFactory(services => services.AddSingleton<IQueryAuthorizer>(authorizer));

        var response = await factory.CreateClient().GetAsync(Url());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authorizer_ReceivesTenantAppAndPermissions()
    {
        var authorizer = new CapturingAuthorizer();

        using var factory = CreateFactory(services =>
        {
            services.AddSingleton<IQueryAuthorizer>(authorizer);
            services.AddSingleton<IQueryIdentityProvider, FixedIdentityProvider>();
        });

        await factory.CreateClient().GetAsync(Url());

        var identity = authorizer.LastRequest!.Identity;
        Assert.Equal("tenant-1", identity.TenantId);
        Assert.Equal("orbita", identity.AppId);
        Assert.True(identity.HasPermission("orders.read"));
        Assert.Null(authorizer.LastRequest.SourceName); // inline $source has no source name
    }

    [Fact]
    public async Task PermissionAuthorizer_RequiresThePermission()
    {
        var policy = new QuerySourcePermissionPolicy(
            [new QuerySourcePermission("orders", "orders.read")]);
        var authorizer = new PermissionQueryAuthorizer(policy);
        var context = new DefaultHttpContext();

        var allowed = await authorizer.AuthorizeAsync(Request(context, new QueryIdentity { Permissions = ["orders.read"] }));
        var missing = await authorizer.AuthorizeAsync(Request(context, new QueryIdentity()));
        var unlisted = await authorizer.AuthorizeAsync(Request(context, new QueryIdentity { Permissions = ["orders.read"] }, "payments"));

        Assert.True(allowed.IsAllowed);
        Assert.False(missing.IsAllowed);
        Assert.False(unlisted.IsAllowed);
    }

    private static QueryAuthorizationRequest Request(
        HttpContext context,
        QueryIdentity identity,
        string sourceName = "orders")
        => new()
        {
            SourceName = sourceName,
            Query = Query,
            Identity = identity,
            HttpContext = context,
        };

    private static WebApplicationFactory<Program> CreateFactory(Action<IServiceCollection> configure)
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(configure));

    private sealed class CapturingAuthorizer : IQueryAuthorizer
    {
        public QueryAuthorizationDecision Decision { get; set; } = QueryAuthorizationDecision.Allow();

        public QueryAuthorizationRequest? LastRequest { get; private set; }

        public ValueTask<QueryAuthorizationDecision> AuthorizeAsync(
            QueryAuthorizationRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return ValueTask.FromResult(Decision);
        }
    }

    private sealed class FixedIdentityProvider : IQueryIdentityProvider
    {
        public ValueTask<QueryIdentity?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<QueryIdentity?>(new QueryIdentity
            {
                TenantId = "tenant-1",
                AppId = "orbita",
                SubjectId = "user-1",
                Permissions = ["orders.read"],
            });
    }
}
