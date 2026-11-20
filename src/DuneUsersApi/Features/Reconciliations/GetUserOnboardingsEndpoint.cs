using DuneUsersApi.Infrastructure.Persistence;
using FastEndpoints;

namespace DuneUsersApi.Features.Reconciliations;

public sealed class GetUserOnboardingsEndpoint(AppDbContext db)
    : Endpoint<GetUserOnboardingsRequest, GetUserOnboardingsResponse>
{
    public override void Configure()
    {
        Get("/v1/reconciliations/user-onboardings");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Liste paginée des users d'un tenant onboardés sur une plage de dates.";
            s.Description =
                "Renvoie le tenantUserId et la date d'onboarding (UTC) des users dont onboardedAt est dans "
                + "[fromOnboardedAt, toOnboardedAt), triés par onboardedAt puis tenantUserId. "
                + "Une page de moins de pageSize éléments est la dernière. "
                + "Seuls les users insérés en base avant asOf (exclu) sont renvoyés : en passant le même asOf pour "
                + "toutes les pages, le parcours reste stable même si des users sont insérés pendant ce temps.";
            s.Responses[200] = "Une page de résultats, éventuellement vide.";
            s.Responses[400] = "Paramètre manquant, mal formé ou invalide.";
        });
    }

    public override async Task<GetUserOnboardingsResponse> ExecuteAsync(GetUserOnboardingsRequest req, CancellationToken ct)
    {
        // Npgsql refuse un paramètre timestamptz dont l'offset n'est pas nul.
        var asOf = req.AsOf!.Value.ToUniversalTime();

        var userOnboardings = await db.Users
            .OnboardedBetween(req.TenantId!, req.FromOnboardedAt!.Value, req.ToOnboardedAt!.Value)
            // Fige le parcours : un user inséré après asOf ne décale pas les pages suivantes.
            .Where(u => u.InsertedAt < asOf)
            .ToUserOnboardingsPageAsync(req.Page, req.PageSize, ct);

        return new GetUserOnboardingsResponse(userOnboardings);
    }
}
