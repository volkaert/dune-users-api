using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DuneUsersApi.Features.Reconciliations;
using DuneUsersApi.Tests.Fixtures;

namespace DuneUsersApi.Tests.Features.Reconciliations;

/// <summary>
/// Les appels utilisent une URL construite explicitement pour tester le contrat réel : noms des paramètres,
/// encodage des offsets (<c>+</c> → <c>%2B</c>) et noms des propriétés JSON.
/// </summary>
public sealed class GetUserOnboardingsEndpointTests(ApiFixture app) : TestBase<ApiFixture>
{
    private const string Route = "/v1/reconciliations/user-onboardings";

    private static readonly DateTimeOffset Day = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NextDay = Day.AddDays(1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_users_onboarded_in_half_open_range()
    {
        var tenantId = NewTenantId();
        await app.InsertAsync(
        [
            TestUsers.New(tenantId, "before", Day.AddTicks(-10)),
            TestUsers.New(tenantId, "at-from", Day),
            TestUsers.New(tenantId, "middle", Day.AddHours(12)),
            TestUsers.New(tenantId, "last-second", NextDay.AddSeconds(-1)),
            TestUsers.New(tenantId, "at-to", NextDay),
        ], Ct);

        var page = await GetPage(tenantId, Day, NextDay);

        page.Select(o => o.TenantUserId).ShouldBe(["at-from", "middle", "last-second"]);
    }

    [Fact]
    public async Task Accepts_bounds_with_offset_and_returns_utc_dates()
    {
        var tenantId = NewTenantId();
        await app.InsertAsync(
        [
            TestUsers.New(tenantId, "before", DateTimeOffset.Parse("2026-11-19T23:59:59Z")),
            TestUsers.New(tenantId, "first", DateTimeOffset.Parse("2026-11-20T00:00:00Z")),
            TestUsers.New(tenantId, "second", DateTimeOffset.Parse("2026-11-20T02:30:00+02:00")),
            TestUsers.New(tenantId, "after", DateTimeOffset.Parse("2026-11-20T01:00:00Z")),
        ], Ct);

        // [00:00Z, 01:00Z) exprimé avec un offset de +02:00.
        var page = await GetPage(
            tenantId,
            DateTimeOffset.Parse("2026-11-20T02:00:00+02:00"),
            DateTimeOffset.Parse("2026-11-20T03:00:00+02:00"));

        page.ShouldBe(
        [
            new UserOnboarding("first", DateTimeOffset.Parse("2026-11-20T00:00:00Z")),
            new UserOnboarding("second", DateTimeOffset.Parse("2026-11-20T00:30:00Z")),
        ]);
        page.ShouldAllBe(o => o.OnboardedAt.Offset == TimeSpan.Zero);
    }

    [Fact]
    public async Task Sorts_by_onboarded_at()
    {
        var tenantId = NewTenantId();
        await app.InsertAsync(
        [
            TestUsers.New(tenantId, "c", Day.AddHours(3)),
            TestUsers.New(tenantId, "a", Day.AddHours(1)),
            TestUsers.New(tenantId, "b", Day.AddHours(2)),
        ], Ct);

        var page = await GetPage(tenantId, Day, NextDay);

        page.Select(o => o.TenantUserId).ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public async Task Paginates_users_onboarded_at_same_instant_in_stable_order()
    {
        // tenantUserId est une chaîne opaque : on ne vérifie pas un ordre précis entre ex æquo (il dépend de la
        // collation), seulement qu'il est stable et que la pagination renvoie chaque user une seule fois.
        var tenantId = NewTenantId();
        string[] tenantUserIds = ["Z-9", "42", Guid.NewGuid().ToString(), "jdoe@acme", "a"];
        await app.InsertAsync(tenantUserIds.Select(id => TestUsers.New(tenantId, id, Day.AddHours(8))), Ct);

        var fullPage = await GetPage(tenantId, Day, NextDay);
        var fullPageAgain = await GetPage(tenantId, Day, NextDay);
        var onePerPage = new List<UserOnboarding>();
        for (var pageNumber = 1; pageNumber <= tenantUserIds.Length; pageNumber++)
        {
            onePerPage.AddRange(await GetPage(tenantId, Day, NextDay, pageNumber, pageSize: 1));
        }

        fullPage.Select(o => o.TenantUserId).ShouldBe(tenantUserIds, ignoreOrder: true);
        fullPageAgain.ShouldBe(fullPage);
        onePerPage.ShouldBe(fullPage);
        (await GetPage(tenantId, Day, NextDay, page: tenantUserIds.Length + 1, pageSize: 1)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Paginates_with_page_and_page_size()
    {
        var tenantId = NewTenantId();
        var users = Enumerable.Range(0, 5)
            .Select(i => TestUsers.New(tenantId, $"user-{i}", Day.AddMinutes(i)))
            .ToList();
        await app.InsertAsync(users, Ct);

        var pages = new List<IReadOnlyList<UserOnboarding>>();
        for (var pageNumber = 1; pageNumber <= 4; pageNumber++)
        {
            pages.Add(await GetPage(tenantId, Day, NextDay, pageNumber, pageSize: 2));
        }

        pages.Select(p => p.Count).ShouldBe([2, 2, 1, 0]);
        pages.SelectMany(p => p).Select(o => o.TenantUserId).ShouldBe(users.Select(u => u.TenantUserId));
    }

    [Fact]
    public async Task Uses_first_page_of_1000_by_default()
    {
        var tenantId = NewTenantId();
        var users = Enumerable.Range(0, GetUserOnboardingsRequest.DefaultPageSize + 1)
            .Select(i => TestUsers.New(tenantId, $"user-{i:D4}", Day.AddSeconds(i)))
            .ToList();
        await app.InsertAsync(users, Ct);

        var page = await GetPage(tenantId, Day, NextDay);

        page.Count.ShouldBe(GetUserOnboardingsRequest.DefaultPageSize);
        page[0].TenantUserId.ShouldBe("user-0000");
        page[^1].TenantUserId.ShouldBe("user-0999");
    }

    [Fact]
    public async Task Ignores_users_of_other_tenants()
    {
        var tenantId = NewTenantId();
        var tenantUser = TestUsers.New(tenantId, "shared-id", Day.AddHours(1));
        var otherTenantUser = TestUsers.New(NewTenantId(), "shared-id", Day.AddHours(2));
        var otherTenantOnlyUser = TestUsers.New(otherTenantUser.TenantId, "other", Day.AddHours(3));
        await app.InsertAsync([tenantUser, otherTenantUser, otherTenantOnlyUser], Ct);

        var page = await GetPage(tenantId, Day, NextDay);

        page.ShouldBe([new UserOnboarding("shared-id", tenantUser.OnboardedAt)]);
    }

    [Fact]
    public async Task Returns_empty_list_for_unknown_tenant()
    {
        var rsp = await Get(Query(NewTenantId(), Day, NextDay, asOf: await app.DatabaseNowAsync(Ct)));

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await rsp.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("userOnboardings").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Returns_json_in_spec_format()
    {
        var tenantId = NewTenantId();
        await app.InsertAsync([TestUsers.New(tenantId, "u-001", DateTimeOffset.Parse("2026-11-20T10:00:00+02:00"))], Ct);

        var rsp = await Get(Query(tenantId, Day, NextDay, asOf: await app.DatabaseNowAsync(Ct)));

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await rsp.Content.ReadAsStringAsync(Ct));
        json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["userOnboardings"]);
        var item = json.RootElement.GetProperty("userOnboardings").EnumerateArray().ShouldHaveSingleItem();
        item.EnumerateObject().Select(p => p.Name).ShouldBe(["tenantUserId", "onboardedAt"]);
        item.GetProperty("tenantUserId").GetString().ShouldBe("u-001");
        item.GetProperty("onboardedAt").GetString().ShouldBe("2026-11-20T08:00:00+00:00");
    }

    [Fact]
    public async Task Includes_users_inserted_before_as_of_exclusive_whatever_its_offset()
    {
        var tenantId = NewTenantId();
        var user = TestUsers.New(tenantId, "u-001", Day.AddHours(8));
        await app.InsertAsync([user], Ct);
        var plusTwo = TimeSpan.FromHours(2);

        // user.InsertedAt est relu après l'insertion ; exprimé ici avec un offset de +02:00.
        var atInsertion = await GetPage(tenantId, Day, NextDay, asOf: user.InsertedAt.ToOffset(plusTwo));
        var justAfter = await GetPage(tenantId, Day, NextDay, asOf: user.InsertedAt.AddTicks(10).ToOffset(plusTwo));

        atInsertion.ShouldBeEmpty();
        justAfter.Select(o => o.TenantUserId).ShouldBe(["u-001"]);
    }

    [Fact]
    public async Task Excludes_users_inserted_after_as_of()
    {
        var tenantId = NewTenantId();
        await app.InsertAsync([TestUsers.New(tenantId, "before-as-of", Day.AddHours(2))], Ct);
        var asOf = await app.DatabaseNowAsync(Ct);
        await app.InsertAsync([TestUsers.New(tenantId, "after-as-of", Day.AddHours(1))], Ct);
        var laterAsOf = await app.DatabaseNowAsync(Ct);

        var withAsOf = await GetPage(tenantId, Day, NextDay, asOf: asOf);
        var withLaterAsOf = await GetPage(tenantId, Day, NextDay, asOf: laterAsOf);

        withAsOf.Select(o => o.TenantUserId).ShouldBe(["before-as-of"]);
        withLaterAsOf.Select(o => o.TenantUserId).ShouldBe(["after-as-of", "before-as-of"]);
    }

    [Fact]
    public async Task Keeps_pages_stable_when_users_are_inserted_during_traversal_with_same_as_of()
    {
        var tenantId = NewTenantId();
        await app.InsertAsync(
        [
            TestUsers.New(tenantId, "A", Day.AddHours(8)),
            TestUsers.New(tenantId, "B", Day.AddHours(9)),
            TestUsers.New(tenantId, "C", Day.AddHours(10)),
            TestUsers.New(tenantId, "D", Day.AddHours(11)),
        ], Ct);
        var asOf = await app.DatabaseNowAsync(Ct);

        var page1 = await GetPage(tenantId, Day, NextDay, page: 1, pageSize: 2, asOf: asOf);
        // Inséré pendant le parcours, avec un onboardedAt situé dans la page déjà lue.
        await app.InsertAsync([TestUsers.New(tenantId, "X", Day.AddHours(8.5))], Ct);
        var page2 = await GetPage(tenantId, Day, NextDay, page: 2, pageSize: 2, asOf: asOf);
        var page2WithNewAsOf = await GetPage(
            tenantId, Day, NextDay, page: 2, pageSize: 2, asOf: await app.DatabaseNowAsync(Ct));

        page1.Select(o => o.TenantUserId).ShouldBe(["A", "B"]);
        page2.Select(o => o.TenantUserId).ShouldBe(["C", "D"]);
        // Avec un asOf postérieur à l'insertion, les pages se décalent : B est renvoyé deux fois et X n'est jamais vu.
        page2WithNewAsOf.Select(o => o.TenantUserId).ShouldBe(["B", "C"]);
    }

    [Fact]
    public async Task Error_messages_name_parameters_as_exposed_by_the_api()
    {
        var invalidRules = await GetErrors(Query(
            ("fromOnboardedAt", To),
            ("toOnboardedAt", From),
            ("page", "0"),
            ("pageSize", "5000")));
        var unreadableDate = await GetErrors(Query(
            ("tenantId", "acme"),
            ("fromOnboardedAt", "pas-une-date"),
            ("toOnboardedAt", To),
            ("asOf", AsOf)));
        var unreadableAsOf = await GetErrors(Query(
            ("tenantId", "acme"),
            ("fromOnboardedAt", From),
            ("toOnboardedAt", To),
            ("asOf", "hier")));
        var missingBounds = await GetErrors(Query(("tenantId", "acme"), ("asOf", AsOf)));

        invalidRules.Keys.ShouldBe(["tenantId", "toOnboardedAt", "asOf", "page", "pageSize"], ignoreOrder: true);
        invalidRules["tenantId"].ShouldBe(["'tenantId' must not be empty."]);
        invalidRules["toOnboardedAt"].ShouldBe(["'toOnboardedAt' must be after 'fromOnboardedAt'."]);
        invalidRules["asOf"].ShouldBe(["'asOf' must not be empty."]);
        invalidRules["page"].ShouldBe(["'page' must be between 1 and 1000000. You entered 0."]);
        invalidRules["pageSize"].ShouldBe(["'pageSize' must be between 1 and 1000. You entered 5000."]);
        unreadableDate["fromOnboardedAt"]
            .ShouldContain("'fromOnboardedAt' must be a valid DateTimeOffset. You entered 'pas-une-date'.");
        unreadableAsOf["asOf"].ShouldBe(["'asOf' must be a valid DateTimeOffset. You entered 'hier'."]);
        // La condition de la comparaison ne doit pas masquer le NotNull de toOnboardedAt.
        missingBounds.Keys.ShouldBe(["fromOnboardedAt", "toOnboardedAt"], ignoreOrder: true);
        missingBounds["toOnboardedAt"].ShouldBe(["'toOnboardedAt' must not be empty."]);
    }

    [Theory]
    [InlineData(null, From, To, null, null, "tenantId")]
    [InlineData("", From, To, null, null, "tenantId")]
    [InlineData(TooLongTenantId, From, To, null, null, "tenantId")]
    [InlineData("acme", null, To, null, null, "fromOnboardedAt")]
    [InlineData("acme", From, null, null, null, "toOnboardedAt")]
    [InlineData("acme", "pas-une-date", To, null, null, "fromOnboardedAt")]
    [InlineData("acme", From, From, null, null, "toOnboardedAt")]
    [InlineData("acme", To, From, null, null, "toOnboardedAt")]
    [InlineData("acme", "2026-11-20T02:00:00+02:00", "2026-11-20T00:00:00Z", null, null, "toOnboardedAt")]
    [InlineData("acme", From, To, "0", null, "page")]
    [InlineData("acme", From, To, "1000001", null, "page")]
    [InlineData("acme", From, To, null, "0", "pageSize")]
    [InlineData("acme", From, To, null, "1001", "pageSize")]
    [InlineData("acme", From, To, null, null, "asOf", null)]
    [InlineData("acme", From, To, null, null, "asOf", "hier")]
    public async Task Rejects_invalid_parameters(
        string? tenantId,
        string? from,
        string? to,
        string? page,
        string? pageSize,
        string expectedErrorKey,
        string? asOf = AsOf)
    {
        var rsp = await Get(Query(
            ("tenantId", tenantId),
            ("fromOnboardedAt", from),
            ("toOnboardedAt", to),
            ("page", page),
            ("pageSize", pageSize),
            ("asOf", asOf)));

        rsp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var error = await rsp.Content.ReadFromJsonAsync<ErrorResponse>(Ct);
        error.ShouldNotBeNull().Errors.Keys.ShouldContain(expectedErrorKey);
    }

    private const string From = "2026-11-20T00:00:00Z";
    private const string To = "2026-11-21T00:00:00Z";
    private const string AsOf = "2026-11-21T06:00:00Z";
    private const string TooLongTenantId =
        "0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789X";

    private static string NewTenantId() => Guid.NewGuid().ToString();

    /// <summary>Appelle l'endpoint ; sans <paramref name="asOf"/>, utilise l'heure courante de la base.</summary>
    private async Task<IReadOnlyList<UserOnboarding>> GetPage(
        string tenantId,
        DateTimeOffset from,
        DateTimeOffset to,
        int? page = null,
        int? pageSize = null,
        DateTimeOffset? asOf = null)
    {
        asOf ??= await app.DatabaseNowAsync(Ct);
        var rsp = await Get(Query(tenantId, from, to, page, pageSize, asOf));

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
        int? page = null,
        int? pageSize = null,
        DateTimeOffset? asOf = null) => Query(
        ("tenantId", tenantId),
        // Format ISO 8601 avec offset explicite : le "+" est encodé en %2B par Uri.EscapeDataString.
        ("fromOnboardedAt", from.ToString("O")),
        ("toOnboardedAt", to.ToString("O")),
        ("page", page?.ToString()),
        ("pageSize", pageSize?.ToString()),
        ("asOf", asOf?.ToString("O")));

    private static string Query(params (string Name, string? Value)[] parameters) => string.Join(
        "&",
        parameters
            .Where(p => p.Value is not null)
            .Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value!)}"));
}
