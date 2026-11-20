using DuneUsersApi.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

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
                + "Une page de moins de pageSize éléments est la dernière.";
            s.Responses[200] = "Une page de résultats, éventuellement vide.";
            s.Responses[400] = "Paramètre manquant, mal formé ou invalide.";
        });
    }

    public override async Task<GetUserOnboardingsResponse> ExecuteAsync(GetUserOnboardingsRequest req, CancellationToken ct)
    {
        // Npgsql refuse un paramètre timestamptz dont l'offset n'est pas nul.
        var from = req.FromOnboardedAt!.Value.ToUniversalTime();
        var to = req.ToOnboardedAt!.Value.ToUniversalTime();

        var userOnboardings = await db.Users
            .AsNoTracking()
            .Where(u => u.TenantId == req.TenantId && u.OnboardedAt >= from && u.OnboardedAt < to)
            // tenant_user_id ne sert qu'à départager les ex æquo (ordre total et stable pour la pagination) :
            // c'est une chaîne opaque, sans ordre métier.
            .OrderBy(u => u.OnboardedAt).ThenBy(u => u.TenantUserId)
            .Skip((req.Page - 1) * req.PageSize)
            .Take(req.PageSize)
            .Select(u => new UserOnboarding(u.TenantUserId, u.OnboardedAt))
            .ToListAsync(ct);

        return new GetUserOnboardingsResponse(userOnboardings);
    }
}
