using DuneUsersApi.Infrastructure.Persistence;
using DuneUsersApi.Infrastructure.Persistence.Users;
using Microsoft.Extensions.DependencyInjection;

namespace DuneUsersApi.Tests.Fixtures;

/// <summary>Fabrique et insertion de users de test.</summary>
public static class TestUsers
{
    /// <summary>
    /// Crée un user valide. Sans <paramref name="tenantId"/>, un tenant unique est généré : les tests partagent la
    /// même base et s'isolent ainsi les uns des autres.
    /// </summary>
    public static User New(
        string? tenantId = null,
        string? tenantUserId = null,
        DateTimeOffset? onboardedAt = null,
        string countryCode = "FR") => new()
    {
        TenantId = tenantId ?? Guid.NewGuid().ToString(),
        TenantUserId = tenantUserId ?? Guid.NewGuid().ToString(),
        OnboardedAt = onboardedAt ?? new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero),
        FirstName = "Paul",
        LastName = "Atreides",
        BirthDate = new DateOnly(1990, 4, 12),
        Address = new Address
        {
            Street = "1 rue d'Arrakeen",
            PostalCode = "75001",
            City = "Paris",
            CountryCode = countryCode,
        },
    };

    /// <summary>Insère les users en une seule sauvegarde, avec un <see cref="AppDbContext"/> dédié.</summary>
    public static async Task InsertAsync(this ApiFixture app, IEnumerable<User> users, CancellationToken ct)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.AddRange(users);
        await db.SaveChangesAsync(ct);
    }
}
