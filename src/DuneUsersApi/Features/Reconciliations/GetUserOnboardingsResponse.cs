namespace DuneUsersApi.Features.Reconciliations;

public sealed record GetUserOnboardingsResponse(IReadOnlyList<UserOnboarding> UserOnboardings);

/// <param name="TenantUserId">Identifiant du user dans son tenant.</param>
/// <param name="OnboardedAt">Instant de l'onboarding, en UTC.</param>
public sealed record UserOnboarding(string TenantUserId, DateTimeOffset OnboardedAt);
