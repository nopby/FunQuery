using FunQuery.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFunQuery();

var app = builder.Build();

app.MapFunQueryEndpoints();

app.Run();

/// <summary>Public agar integration test dapat memakai WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program
{
}
