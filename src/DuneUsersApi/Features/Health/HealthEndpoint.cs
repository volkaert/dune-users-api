using DuneUsersApi.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DuneUsersApi.Features.Health;

public sealed record HealthResponse(string Status, string Database);

public sealed class HealthEndpoint(AppDbContext db)
    : EndpointWithoutRequest<Results<Ok<HealthResponse>, JsonHttpResult<HealthResponse>>>
{
    public override void Configure()
    {
        Get("/health");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Vérifie l'état de l'API et de sa base de données.";
            s.Responses[200] = "L'API et la base de données sont disponibles.";
            s.Responses[503] = "La base de données est injoignable.";
        });
    }

    public override async Task<Results<Ok<HealthResponse>, JsonHttpResult<HealthResponse>>> ExecuteAsync(CancellationToken ct)
    {
        bool databaseUp;
        try
        {
            databaseUp = await db.Database.CanConnectAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Health check: database connection failed");
            databaseUp = false;
        }

        return databaseUp
            ? TypedResults.Ok(new HealthResponse("Healthy", "Up"))
            : TypedResults.Json(new HealthResponse("Unhealthy", "Down"), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
