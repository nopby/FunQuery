using System.Net;
using System.Net.Http.Json;
using FunQuery.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FunQuery.NamedQueries.Tests;

public sealed class NamedQueryAdminEndpointTests
{
    private static async Task<(WebApplication App, HttpClient Client, FakeNamedQueryStore Store)>
        CreateAppAsync(bool allow)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddFunQuery();
        builder.Services.AddFunQuerySources(sources => sources.Add(
            new TestSource("orders", TestSchema.Orders)));

        var store = new FakeNamedQueryStore();
        builder.Services.AddSingleton<INamedQueryStore>(store);
        builder.Services.AddSingleton<INamedQueryAdminService, NamedQueryAdminService>();
        builder.Services.AddFunQueryNamedQueryAdmin(options =>
            options.Route = "/fq/admin");
        builder.Services.AddSingleton<INamedQueryAdminAuthorizer>(
            new AllowAuthorizer(allow));

        var app = builder.Build();
        app.MapFunQueryNamedQueryAdmin();
        await app.StartAsync();

        return (app, app.GetTestClient(), store);
    }

    [Fact]
    public async Task CrudRoundTripWorks()
    {
        var (app, client, _) = await CreateAppAsync(allow: true);

        try
        {
            var request = new NamedQueryWriteRequest
            {
                AppId = "app",
                TenantId = "tenant",
                Key = "orders",
                Query = "$source(@source).$count()",
                Source = "orders",
            };

            var created = await client.PostAsJsonAsync("/fq/admin", request);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            var read = await client.GetAsync(
                "/fq/admin/orders?app=app&tenant=tenant");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);

            var put = new HttpRequestMessage(HttpMethod.Put, "/fq/admin/orders?app=app&tenant=tenant")
            {
                Content = JsonContent.Create(request with
                {
                    Query = "$source(@source).$select(id)",
                }),
            };
            put.Headers.TryAddWithoutValidation("If-Match", "\"1\"");

            var updated = await client.SendAsync(put);
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

            var delete = new HttpRequestMessage(
                HttpMethod.Delete,
                "/fq/admin/orders?app=app&tenant=tenant&purge=true");
            delete.Headers.TryAddWithoutValidation("If-Match", "\"2\"");

            var deleted = await client.SendAsync(delete);
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

            var missing = await client.GetAsync(
                "/fq/admin/orders?app=app&tenant=tenant");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task MissingAuthorizerPermissionReturns403()
    {
        var (app, client, _) = await CreateAppAsync(allow: false);

        try
        {
            var response = await client.GetAsync("/fq/admin?app=app&tenant=tenant");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public void MappingTwiceFailsFast()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddFunQuery();
        builder.Services.AddFunQuerySources(sources => sources.Add(
            new TestSource("orders", TestSchema.Orders)));
        builder.Services.AddSingleton<INamedQueryStore>(new FakeNamedQueryStore());
        builder.Services.AddFunQueryNamedQueryAdmin(options =>
            options.Route = "/fq/admin");

        using var app = builder.Build();
        app.MapFunQueryNamedQueryAdmin();

        Assert.Throws<InvalidOperationException>(
            () => app.MapFunQueryNamedQueryAdmin());
    }

    private sealed class AllowAuthorizer(bool allow) : INamedQueryAdminAuthorizer
    {
        public ValueTask<bool> CanManageAsync(
            HttpContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(allow);
    }
}
