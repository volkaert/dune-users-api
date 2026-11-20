using DuneUsersApi.Infrastructure.Persistence.Users;
using Microsoft.EntityFrameworkCore;

namespace DuneUsersApi.Features.Reconciliations;

/// <summary>
/// Requête commune aux endpoints de réconciliation des onboardings : chacun n'y ajoute que son filtre sur
/// <see cref="User.InsertedAt"/>. Servie par l'index <c>ix_users_tenant_id_onboarded_at_tenant_user_id</c>.
/// </summary>
internal static class UserOnboardingQueries
{
    /// <summary>Users du tenant dont <c>onboardedAt</c> est dans <c>[from, to)</c>.</summary>
    public static IQueryable<User> OnboardedBetween(
        this IQueryable<User> users,
        string tenantId,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        // Npgsql refuse un paramètre timestamptz dont l'offset n'est pas nul.
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();

        return users.Where(u => u.TenantId == tenantId && u.OnboardedAt >= fromUtc && u.OnboardedAt < toUtc);
    }

    /// <summary>Page de résultats, triée par <c>onboardedAt</c> puis <c>tenantUserId</c>.</summary>
    public static Task<List<UserOnboarding>> ToUserOnboardingsPageAsync(
        this IQueryable<User> users,
        int page,
        int pageSize,
        CancellationToken ct) => users
        .AsNoTracking()
        // tenant_user_id ne sert qu'à départager les ex æquo (ordre total et stable pour la pagination) :
        // c'est une chaîne opaque, sans ordre métier.
        .OrderBy(u => u.OnboardedAt).ThenBy(u => u.TenantUserId)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(u => new UserOnboarding(u.TenantUserId, u.OnboardedAt))
        .ToListAsync(ct);
}
