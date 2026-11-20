using DuneUsersApi.Infrastructure.Persistence;
using DuneUsersApi.Infrastructure.Persistence.Users;
using DuneUsersApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DuneUsersApi.Tests.Infrastructure.Persistence;

public sealed class UserPersistenceTests(ApiFixture app) : TestBase<ApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Persists_and_reads_back_all_fields()
    {
        var user = TestUsers.New();

        await Insert(user);
        var stored = await Find(user.Id);

        stored.TenantId.ShouldBe(user.TenantId);
        stored.TenantUserId.ShouldBe(user.TenantUserId);
        stored.InsertedAt.ShouldBe(user.InsertedAt);
        stored.OnboardedAt.ShouldBe(user.OnboardedAt);
        stored.FirstName.ShouldBe(user.FirstName);
        stored.LastName.ShouldBe(user.LastName);
        stored.BirthDate.ShouldBe(user.BirthDate);
        stored.Address.ShouldBe(user.Address);
    }

    [Theory]
    [InlineData("2026-11-20T10:00:00+02:00", "2026-11-20T08:00:00Z")]
    [InlineData("2026-11-20T10:00:00-05:00", "2026-11-20T15:00:00Z")]
    [InlineData("2026-11-20T10:00:00Z", "2026-11-20T10:00:00Z")]
    public async Task Stores_onboarded_at_as_utc_instant(string onboardedAt, string expectedUtc)
    {
        var user = TestUsers.New(onboardedAt: DateTimeOffset.Parse(onboardedAt));

        await Insert(user);
        var stored = await Find(user.Id);

        stored.OnboardedAt.ShouldBe(DateTimeOffset.Parse(expectedUtc));
        stored.OnboardedAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task Sets_inserted_at_from_database_clock()
    {
        // Encadré par l'heure de PostgreSQL : la valeur vient de la base, pas de l'horloge de l'application
        // (FakeTimeProvider, figée sur une autre date).
        var user = TestUsers.New();

        var before = await DatabaseNow();
        await Insert(user);
        var after = await DatabaseNow();
        var stored = await Find(user.Id);

        stored.InsertedAt.ShouldBeInRange(before, after);
        stored.InsertedAt.Offset.ShouldBe(TimeSpan.Zero);
        user.InsertedAt.ShouldBe(stored.InsertedAt, "valeur relue après l'insertion");
    }

    [Fact]
    public async Task Ignores_inserted_at_set_by_application()
    {
        var forced = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var user = TestUsers.New();

        var before = await DatabaseNow();
        await WithDb(async db =>
        {
            db.Users.Add(user);
            db.Entry(user).Property(u => u.InsertedAt).CurrentValue = forced;
            await db.SaveChangesAsync(Ct);
        });
        var stored = await Find(user.Id);

        stored.InsertedAt.ShouldNotBe(forced);
        stored.InsertedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public async Task Keeps_inserted_at_on_update()
    {
        var user = TestUsers.New();
        await Insert(user);
        var inserted = await Find(user.Id);

        await WithDb(async db =>
        {
            var tracked = await db.Users.SingleAsync(u => u.Id == user.Id, Ct);
            tracked.FirstName = "Leto";
            db.Entry(tracked).Property(u => u.InsertedAt).CurrentValue = DateTimeOffset.UnixEpoch;
            await db.SaveChangesAsync(Ct);
        });
        var stored = await Find(user.Id);

        stored.FirstName.ShouldBe("Leto");
        stored.InsertedAt.ShouldBe(inserted.InsertedAt);
    }

    [Fact]
    public async Task Filters_on_onboarded_at_by_utc_range()
    {
        var user = TestUsers.New(onboardedAt: DateTimeOffset.Parse("2026-11-20T23:30:00-02:00"));
        await Insert(user);

        var dayStart = new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
        var ids = await WithDb(db => db.Users
            .Where(u => u.TenantId == user.TenantId && u.OnboardedAt >= dayStart && u.OnboardedAt < dayEnd)
            .Select(u => u.Id)
            .ToListAsync(Ct));

        ids.ShouldBe([user.Id]);
    }

    [Fact]
    public async Task Rejects_duplicate_tenant_user_in_same_tenant()
    {
        var first = TestUsers.New();
        var duplicate = TestUsers.New(first.TenantId, first.TenantUserId);
        await Insert(first);

        var ex = await Should.ThrowAsync<DbUpdateException>(() => Insert(duplicate));

        ex.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Accepts_same_tenant_user_id_in_different_tenants()
    {
        var first = TestUsers.New();
        var second = TestUsers.New(tenantUserId: first.TenantUserId);

        await Insert(first);
        await Insert(second);

        (await Find(second.Id)).TenantUserId.ShouldBe(first.TenantUserId);
    }

    [Theory]
    [InlineData("fr", PostgresErrorCodes.CheckViolation)]
    [InlineData("F1", PostgresErrorCodes.CheckViolation)]
    [InlineData("FRA", PostgresErrorCodes.StringDataRightTruncation)]
    public async Task Rejects_invalid_country_code(string countryCode, string expectedSqlState)
    {
        var user = TestUsers.New(countryCode: countryCode);

        var ex = await Should.ThrowAsync<DbUpdateException>(() => Insert(user));

        ex.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(expectedSqlState);
    }

    [Fact]
    public async Task Model_has_no_pending_changes()
    {
        var pending = await WithDb(db => Task.FromResult(db.Database.HasPendingModelChanges()));

        pending.ShouldBeFalse();
    }

    private Task Insert(User user) => app.InsertAsync([user], Ct);

    private Task<DateTimeOffset> DatabaseNow() => app.DatabaseNowAsync(Ct);

    private Task<User> Find(Guid id) => WithDb(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == id, Ct));

    private async Task WithDb(Func<AppDbContext, Task> action)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<T> WithDb<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
