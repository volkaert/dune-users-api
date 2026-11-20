using System.Net;
using DuneUsersApi.Features.Health;
using DuneUsersApi.Tests.Fixtures;

namespace DuneUsersApi.Tests.Features.Health;

public sealed class HealthEndpointTests(ApiFixture app) : TestBase<ApiFixture>
{
    [Fact]
    public async Task Returns_healthy_when_database_is_reachable()
    {
        var (rsp, res) = await app.Client.GETAsync<HealthEndpoint, HealthResponse>();

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.Status.ShouldBe("Healthy");
        res.Database.ShouldBe("Up");
    }
}
