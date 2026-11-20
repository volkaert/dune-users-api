using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DuneUsersApi.Features.Reconciliations;
using DuneUsersApi.Tests.Fixtures;

namespace DuneUsersApi.Tests.Features.Reconciliations;

/// <summary>
/// Les instants d'insertion (<c>inserted_at</c>, horloge de PostgreSQL) sont encadrés en lisant l'heure de la base
/// entre deux insertions. Les appels utilisent une URL construite explicitement pour tester le contrat réel.
/// </summary>
public sealed class GetMissingUserOnboardingsEndpointTests(ApiFixture app) : TestBase<ApiFixture>
{
    private const string Route = "/v1/reconciliations/user-onboardings/missings";
    private const string InitialRoute = "/v1/reconciliations/user-onboardings";

    private static readonly DateTimeOffset Day = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NextDay = Day.AddDays(1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_only_users_inserted_from_missings_since_until_as_of()
    {
        var tenantId = NewTenantId();
        await app.InsertAsync([TestUsers.New(tenantId, "before-window", Day.AddHours(1))], Ct);
        var missingsSince = await app.DatabaseNowAsync(Ct);
        await app.InsertAsync([TestUsers.New(tenantId, "in-window", Day.AddHours(2))], Ct);
        var asOf = await app.DatabaseNowAsync(Ct);
        await app.InsertAsync([TestUsers.New(tenantId, "after-window", Day.AddHours(3))], Ct);

        var page = await GetPage(tenantId, Day, NextDay, missingsSince, asOf: asOf);

        page.Select(o => o.TenantUserId).ShouldBe(["in-window"]);
    }

    [Fact]
    public async Task Includes_missings_since_and_excludes_as_of_whatever_their_offset()
    {
        var tenantId = NewTenantId();
        var user = TestUsers.New(tenantId, "u-001", Day.AddHours(8));
        await app.InsertAsync([user], Ct);
        var plusTwo = TimeSpan.FromHours(2);
        var insertedAt = user.InsertedAt.ToOffset(plusTwo);
        var justAfter = user.InsertedAt.AddTicks(10).ToOffset(plusTwo);
        var justBefore = user.InsertedAt.AddTicks(-10).ToOffset(plusTwo);

        // user.InsertedAt est relu après l'insertion ; exprimé ici avec un offset de +02:00.
        var sinceInsertion = await GetPage(tenantId, Day, NextDay, missingsSince: insertedAt, asOf: justAfter);
        var sinceJustAfter = await GetPage(tenantId, Day, NextDay, missingsSince: justAfter);
        var untilInsertion = await GetPage(tenantId, Day, NextDay, missingsSince: justBefore, asOf: insertedAt);

        sinceInsertion.Select(o => o.TenantUserId).ShouldBe(["u-001"]);
        sinceJustAfter.ShouldBeEmpty();
        untilInsertion.ShouldBeEmpty();
    }

    [Fact]
    public async Task Complements_initial_traversal_without_duplicate_or_gap()
    {
        var tenantId = NewTenantId();
        await app.InsertAsync(
        [
            TestUsers.New(tenantId, "A", Day.AddHours(8)),
            TestUsers.New(tenantId, "B", Day.AddHours(10)),
        ], Ct);
        var firstAsOf = await app.DatabaseNowAsync(Ct);
        var initial = await GetInitialPage(tenantId, firstAsOf);

        // Insérés après le parcours initial, dans la plage déjà parcourue (dont un ex æquo d'onboardedAt avec A).
        await app.InsertAsync(
        [
            TestUsers.New(tenantId, "X", Day.AddHours(8)),
            TestUsers.New(tenantId, "Y", Day.AddHours(9)),
        ], Ct);
        var secondAsOf = await app.DatabaseNowAsync(Ct);
        var missings = await GetPage(tenantId, Day, NextDay, missingsSince: firstAsOf, asOf: secondAsOf);
        var full = await GetInitialPage(tenantId, secondAsOf);

        initial.Select(o => o.TenantUserId).ShouldBe(["A", "B"]);
        missings.Select(o => o.TenantUserId).ShouldBe(["X", "Y"]);
        initial.Concat(missings).ShouldBe(full, ignoreOrder: true);
    }

    [Fact]
    public async Task Ignores_users_outside_onboarding_range_or_of_other_tenants()
    {
        var tenantId = NewTenantId();
        var missingsSince = await app.DatabaseNowAsync(Ct);
        var inRange = TestUsers.New(tenantId, "in-range", Day);
        await app.InsertAsync(
        [
            inRange,
            TestUsers.New(tenantId, "before-range", Day.AddTicks(-10)),
            TestUsers.New(tenantId, "at-to", NextDay),
            TestUsers.New(NewTenantId(), "other-tenant", Day.AddHours(1)),
        ], Ct);

        var page = await GetPage(tenantId, Day, NextDay, missingsSince);

        page.ShouldBe([new UserOnboarding("in-range", inRange.OnboardedAt)]);
    }

    [Fact]
    public async Task Paginates_missing_users_sorted_by_onboarded_at()
    {
        var tenantId = NewTenantId();
        var missingsSince = await app.DatabaseNowAsync(Ct);
        await app.InsertAsync(
        [
            TestUsers.New(tenantId, "c", Day.AddHours(3)),
            TestUsers.New(tenantId, "a", Day.AddHours(1)),
            TestUsers.New(tenantId, "jdoe@acme", Day.AddHours(2)),
            TestUsers.New(tenantId, "42", Day.AddHours(2)),
            TestUsers.New(tenantId, "b", Day.AddHours(1.5)),
        ], Ct);
        var asOf = await app.DatabaseNowAsync(Ct);

        var full = await GetPage(tenantId, Day, NextDay, missingsSince, asOf: asOf);
        var pages = new List<IReadOnlyList<UserOnboarding>>();
        for (var pageNumber = 1; pageNumber <= 4; pageNumber++)
        {
            pages.Add(await GetPage(tenantId, Day, NextDay, missingsSince, pageNumber, pageSize: 2, asOf));
        }

        full.Count.ShouldBe(5);
        full.Select(o => o.OnboardedAt).ShouldBeInOrder();
        pages.Select(p => p.Count).ShouldBe([2, 2, 1, 0]);
        pages.SelectMany(p => p).ShouldBe(full);
    }

    [Fact]
    public async Task Uses_first_page_of_1000_by_default()
    {
        var tenantId = NewTenantId();
        var missingsSince = await app.DatabaseNowAsync(Ct);
        var users = Enumerable.Range(0, GetMissingUserOnboardingsRequest.DefaultPageSize + 1)
            .Select(i => TestUsers.New(tenantId, $"user-{i:D4}", Day.AddSeconds(i)))
            .ToList();
        await app.InsertAsync(users, Ct);

        var page = await GetPage(tenantId, Day, NextDay, missingsSince);

        page.Count.ShouldBe(GetMissingUserOnboardingsRequest.DefaultPageSize);
        page[0].TenantUserId.ShouldBe("user-0000");
        page[^1].TenantUserId.ShouldBe("user-0999");
    }

    [Fact]
    public async Task Returns_empty_list_for_unknown_tenant()
    {
        var page = await GetPage(NewTenantId(), Day, NextDay, missingsSince: Day);

        page.ShouldBeEmpty();
    }

    [Fact]
    public async Task Returns_json_in_spec_format()
    {
        var tenantId = NewTenantId();
        var missingsSince = await app.DatabaseNowAsync(Ct);
        await app.InsertAsync([TestUsers.New(tenantId, "u-001", DateTimeOffset.Parse("2026-11-20T10:00:00+02:00"))], Ct);

        var rsp = await Get(Query(tenantId, Day, NextDay, missingsSince, asOf: await app.DatabaseNowAsync(Ct)));

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await rsp.Content.ReadAsStringAsync(Ct));
        json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["userOnboardings"]);
        var item = json.RootElement.GetProperty("userOnboardings").EnumerateArray().ShouldHaveSingleItem();
        item.EnumerateObject().Select(p => p.Name).ShouldBe(["tenantUserId", "onboardedAt"]);
        item.GetProperty("tenantUserId").GetString().ShouldBe("u-001");
        item.GetProperty("onboardedAt").GetString().ShouldBe("2026-11-20T08:00:00+00:00");
    }

    [Fact]
    public async Task Error_messages_name_parameters_as_exposed_by_the_api()
    {
        var missingParameters = await GetErrors(Query(
            ("tenantId", "acme"),
            ("fromOnboardedAt", From),
            ("toOnboardedAt", To)));
        var asOfNotAfterMissingsSince = await GetErrors(Query(
            ("tenantId", "acme"),
            ("fromOnboardedAt", From),
            ("toOnboardedAt", To),
            ("missingsSince", AsOf),
            ("asOf", MissingsSince)));
        var unreadableMissingsSince = await GetErrors(Query(
            ("tenantId", "acme"),
            ("fromOnboardedAt", From),
            ("toOnboardedAt", To),
            ("missingsSince", "hier"),
            ("asOf", AsOf)));
        var emptyInsertionBounds = await GetErrors(
            $"tenantId=acme&fromOnboardedAt={Uri.EscapeDataString(From)}&toOnboardedAt={Uri.EscapeDataString(To)}"
            + "&missingsSince=&asOf=");

        missingParameters.Keys.ShouldBe(["missingsSince", "asOf"], ignoreOrder: true);
        missingParameters["missingsSince"].ShouldBe(["'missingsSince' must not be empty."]);
        missingParameters["asOf"].ShouldBe(["'asOf' must not be empty."]);
        asOfNotAfterMissingsSince.Keys.ShouldBe(["asOf"]);
        asOfNotAfterMissingsSince["asOf"].ShouldBe(["'asOf' must be after 'missingsSince'."]);
        unreadableMissingsSince["missingsSince"]
            .ShouldBe(["'missingsSince' must be a valid DateTimeOffset. You entered 'hier'."]);
        // Une date vide est traitée comme absente : "missingsSince=" ne doit pas renvoyer tout l'historique.
        emptyInsertionBounds.Keys.ShouldBe(["missingsSince", "asOf"], ignoreOrder: true);
        emptyInsertionBounds["missingsSince"].ShouldBe(["'missingsSince' must not be empty."]);
        emptyInsertionBounds["asOf"].ShouldBe(["'asOf' must not be empty."]);
    }

    [Theory]
    [InlineData(null, From, To, null, null, "tenantId")]
    [InlineData("", From, To, null, null, "tenantId")]
    [InlineData(TooLongTenantId, From, To, null, null, "tenantId")]
    [InlineData("acme", null, To, null, null, "fromOnboardedAt")]
    [InlineData("acme", From, null, null, null, "toOnboardedAt")]
    [InlineData("acme", "pas-une-date", To, null, null, "fromOnboardedAt")]
    [InlineData("acme", From, "pas-une-date", null, null, "toOnboardedAt")]
    [InlineData("acme", From, From, null, null, "toOnboardedAt")]
    [InlineData("acme", To, From, null, null, "toOnboardedAt")]
    [InlineData("acme", "2026-11-20T02:00:00+02:00", "2026-11-20T00:00:00Z", null, null, "toOnboardedAt")]
    [InlineData("acme", From, To, "0", null, "page")]
    [InlineData("acme", From, To, "1000001", null, "page")]
    [InlineData("acme", From, To, null, "0", "pageSize")]
    [InlineData("acme", From, To, null, "1001", "pageSize")]
    [InlineData("acme", From, To, null, null, "missingsSince", null)]
    [InlineData("acme", From, To, null, null, "missingsSince", "hier")]
    [InlineData("acme", From, To, null, null, "missingsSince", "")]
    [InlineData("acme", From, To, null, null, "asOf", MissingsSince, "")]
    [InlineData("acme", From, To, null, null, "asOf", MissingsSince, null)]
    [InlineData("acme", From, To, null, null, "asOf", MissingsSince, "hier")]
    [InlineData("acme", From, To, null, null, "asOf", MissingsSince, MissingsSince)]
    [InlineData("acme", From, To, null, null, "asOf", AsOf, MissingsSince)]
    [InlineData("acme", From, To, null, null, "asOf", "2026-11-21T08:00:00+02:00", "2026-11-21T06:00:00Z")]
    public async Task Rejects_invalid_parameters(
        string? tenantId,
        string? from,
        string? to,
        string? page,
        string? pageSize,
        string expectedErrorKey,
        string? missingsSince = MissingsSince,
        string? asOf = AsOf)
    {
        var rsp = await Get(Query(
            ("tenantId", tenantId),
            ("fromOnboardedAt", from),
            ("toOnboardedAt", to),
            ("page", page),
            ("pageSize", pageSize),
            ("missingsSince", missingsSince),
            ("asOf", asOf)));

        rsp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var error = await rsp.Content.ReadFromJsonAsync<ErrorResponse>(Ct);
        error.ShouldNotBeNull().Errors.Keys.ShouldContain(expectedErrorKey);
    }

    private const string From = "2026-11-20T00:00:00Z";
    private const string To = "2026-11-21T00:00:00Z";
    private const string MissingsSince = "2026-11-21T06:00:00Z";
    private const string AsOf = "2026-11-22T06:00:00Z";
    private const string TooLongTenantId =
        "0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789X";

    private static string NewTenantId() => Guid.NewGuid().ToString();

    /// <summary>Appelle l'endpoint ; sans <paramref name="asOf"/>, utilise l'heure courante de la base.</summary>
    private async Task<IReadOnlyList<UserOnboarding>> GetPage(
        string tenantId,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset missingsSince,
        int? page = null,
        int? pageSize = null,
        DateTimeOffset? asOf = null)
    {
        asOf ??= await app.DatabaseNowAsync(Ct);
        return await ReadPage(await Get(Query(tenantId, from, to, missingsSince, page, pageSize, asOf)));
    }

    /// <summary>Parcours initial (une seule page) de <c>/v1/reconciliations/user-onboardings</c> sur [Day, NextDay).</summary>
    private async Task<IReadOnlyList<UserOnboarding>> GetInitialPage(string tenantId, DateTimeOffset asOf)
    {
        var query = Query(
            ("tenantId", tenantId),
            ("fromOnboardedAt", Day.ToString("O")),
            ("toOnboardedAt", NextDay.ToString("O")),
            ("asOf", asOf.ToString("O")));
        return await ReadPage(await app.Client.GetAsync($"{InitialRoute}?{query}", Ct));
    }

    private static async Task<IReadOnlyList<UserOnboarding>> ReadPage(HttpResponseMessage rsp)
    {
        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await rsp.Content.ReadFromJsonAsync<GetUserOnboardingsResponse>(Ct);
        return body.ShouldNotBeNull().UserOnboardings;
    }

    private async Task<Dictionary<string, List<string>>> GetErrors(string query)
    {
        var rsp = await Get(query);

        rsp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var error = await rsp.Content.ReadFromJsonAsync<ErrorResponse>(Ct);
        return error.ShouldNotBeNull().Errors;
    }

    private Task<HttpResponseMessage> Get(string query) => app.Client.GetAsync($"{Route}?{query}", Ct);

    private static string Query(
        string tenantId,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset missingsSince,
        int? page = null,
        int? pageSize = null,
        DateTimeOffset? asOf = null) => Query(
        ("tenantId", tenantId),
        // Format ISO 8601 avec offset explicite : le "+" est encodé en %2B par Uri.EscapeDataString.
        ("fromOnboardedAt", from.ToString("O")),
        ("toOnboardedAt", to.ToString("O")),
        ("page", page?.ToString()),
        ("pageSize", pageSize?.ToString()),
        ("missingsSince", missingsSince.ToString("O")),
        ("asOf", asOf?.ToString("O")));

    private static string Query(params (string Name, string? Value)[] parameters) => string.Join(
        "&",
        parameters
            .Where(p => p.Value is not null)
            .Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value!)}"));
}
