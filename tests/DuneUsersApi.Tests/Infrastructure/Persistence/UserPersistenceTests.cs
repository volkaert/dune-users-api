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
        stored.CreatedAt.ShouldBe(user.CreatedAt);
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
    public async Task Sets_created_at_from_application_clock()
    {
        var now = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        app.Time.SetUtcNow(now);
        var user = TestUsers.New();

        await Insert(user);
        var stored = await Find(user.Id);

        stored.CreatedAt.ShouldBe(now);
        stored.CreatedAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task Keeps_created_at_on_update()
    {
        var user = TestUsers.New();
        await Insert(user);
        var createdAt = user.CreatedAt;

        app.Time.Advance(TimeSpan.FromHours(1));
        await WithDb(async db =>
        {
            var tracked = await db.Users.SingleAsync(u => u.Id == user.Id, Ct);
            tracked.FirstName = "Leto";
            await db.SaveChangesAsync(Ct);
        });
        var stored = await Find(user.Id);

        stored.FirstName.ShouldBe("Leto");
        stored.CreatedAt.ShouldBe(createdAt);
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
