using DuneUsersApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

// Nécessaire pour que FastEndpoints appelle OnCachedWafDisposedAsync (arrêt du conteneur en fin de run).
[assembly: EnableAdvancedTesting]

namespace DuneUsersApi.Tests.Fixtures;

/// <summary>
/// Démarre l'API en mémoire, branchée sur une vraie instance PostgreSQL lancée par Testcontainers.
/// </summary>
/// <remarks>
/// FastEndpoints met en cache l'application (WAF) par type de fixture : toutes les classes de tests qui utilisent
/// <see cref="ApiFixture"/> partagent la même application, la même base et la même horloge. PreSetupAsync,
/// ConfigureApp et ConfigureServices ne sont exécutés qu'une fois ; SetupAsync et TearDownAsync le sont par classe.
/// </remarks>
public sealed class ApiFixture : AppFixture<Program>
{
    /// <summary>Heure initiale de l'horloge de test.</summary>
    public static readonly DateTimeOffset StartTime = new(2026, 1, 15, 9, 30, 0, TimeSpan.Zero);

    // Statique : un seul conteneur pour l'application en cache, quelle que soit l'instance de fixture.
    private static PostgreSqlContainer? _postgres;

    /// <summary>
    /// Horloge de l'application (celle enregistrée dans l'injection de dépendances). Partagée par toutes les classes
    /// de tests : un test qui en dépend doit la positionner lui-même (<c>SetUtcNow</c>) plutôt que supposer sa valeur.
    /// </summary>
    public FakeTimeProvider Time => (FakeTimeProvider)Services.GetRequiredService<TimeProvider>();

    /// <summary>Heure courante de PostgreSQL (<c>now()</c>), l'horloge qui renseigne <c>users.inserted_at</c>.</summary>
    public async Task<DateTimeOffset> DatabaseNowAsync(CancellationToken ct)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Database.SqlQuery<DateTimeOffset>($"SELECT now() AS \"Value\"").SingleAsync(ct);
    }

    protected override async ValueTask PreSetupAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _postgres.StartAsync();
    }

    protected override void ConfigureApp(IWebHostBuilder a)
    {
        a.UseEnvironment("Testing");
        a.UseSetting("ConnectionStrings:Postgres", _postgres!.GetConnectionString());
    }

    protected override void ConfigureServices(IServiceCollection s)
    {
        s.AddSingleton<TimeProvider>(new FakeTimeProvider(StartTime));
    }

    protected override async ValueTask SetupAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    protected override async ValueTask OnCachedWafDisposedAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }
}
