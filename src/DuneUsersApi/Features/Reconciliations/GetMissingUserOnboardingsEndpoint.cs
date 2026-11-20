using DuneUsersApi.Infrastructure.Persistence;
using FastEndpoints;

namespace DuneUsersApi.Features.Reconciliations;

public sealed class GetMissingUserOnboardingsEndpoint(AppDbContext db)
    : Endpoint<GetMissingUserOnboardingsRequest, GetUserOnboardingsResponse>
{
    public override void Configure()
    {
        Get("/v1/reconciliations/user-onboardings/missings");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Liste paginée des onboardings manqués par un parcours de /v1/reconciliations/user-onboardings.";
            s.Description =
                "Renvoie, comme /v1/reconciliations/user-onboardings, les users du tenant dont onboardedAt est dans "
                + "[fromOnboardedAt, toOnboardedAt), mais seulement ceux insérés en base dans [missingsSince, asOf). "
                + "Avec missingsSince = le asOf du parcours précédent, la réponse est exactement le complément de ce "
                + "parcours, sans doublon. Tri par onboardedAt puis tenantUserId ; une page de moins de pageSize "
                + "éléments est la dernière.";
            s.Responses[200] = "Une page de résultats, éventuellement vide.";
            s.Responses[400] = "Paramètre manquant, mal formé ou invalide.";
        });
    }

    public override async Task<GetUserOnboardingsResponse> ExecuteAsync(
        GetMissingUserOnboardingsRequest req,
        CancellationToken ct)
    {
        // Npgsql refuse un paramètre timestamptz dont l'offset n'est pas nul.
        var missingsSince = req.MissingsSince!.Value.ToUniversalTime();
        var asOf = req.AsOf!.Value.ToUniversalTime();

        var userOnboardings = await db.Users
            .OnboardedBetween(req.TenantId!, req.FromOnboardedAt!.Value, req.ToOnboardedAt!.Value)
            // [missingsSince, asOf) : complément exact d'un parcours qui a renvoyé inserted_at < missingsSince.
            .Where(u => u.InsertedAt >= missingsSince && u.InsertedAt < asOf)
            .ToUserOnboardingsPageAsync(req.Page, req.PageSize, ct);

        return new GetUserOnboardingsResponse(userOnboardings);
    }
}
