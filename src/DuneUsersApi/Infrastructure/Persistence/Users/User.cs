namespace DuneUsersApi.Infrastructure.Persistence.Users;

public sealed class User
{
    private DateTimeOffset _onboardedAt;

    public Guid Id { get; private set; }

    public required string TenantId { get; init; }

    /// <summary>Identifiant du user dans son tenant.</summary>
    public required string TenantUserId { get; init; }

    /// <summary>
    /// Date de création du user en base, en UTC. Calculée par l'application à l'insertion
    /// (<see cref="CreatedAtInterceptor"/>) : toute valeur affectée avant l'insertion est écrasée.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Instant de l'onboarding. Accepte n'importe quel offset, mais la valeur est convertie en UTC :
    /// PostgreSQL (<c>timestamptz</c>) ne conserve pas l'offset d'origine.
    /// </summary>
    public required DateTimeOffset OnboardedAt
    {
        get => _onboardedAt;
        set => _onboardedAt = value.ToUniversalTime();
    }

    public required string FirstName { get; set; }

    public required string LastName { get; set; }

    public required DateOnly BirthDate { get; set; }

    public required Address Address { get; set; }
}
