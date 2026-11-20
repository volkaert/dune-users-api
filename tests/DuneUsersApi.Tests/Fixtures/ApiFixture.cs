using DuneUsersApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace DuneUsersApi.Tests.Fixtures;

/// <summary>
/// Démarre l'API en mémoire, branchée sur une vraie instance PostgreSQL lancée par Testcontainers.
/// </summary>
public sealed class ApiFixture : AppFixture<Program>
{
    private PostgreSqlContainer _postgres = null!;

    protected override async ValueTask PreSetupAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _postgres.StartAsync();
    }

    protected override void ConfigureApp(IWebHostBuilder a)
    {
        a.UseEnvironment("Testing");
        a.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
    }

    protected override async ValueTask SetupAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    protected override async ValueTask TearDownAsync()
    {
        await _postgres.DisposeAsync();
    }
}
