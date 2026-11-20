namespace DuneUsersApi.Infrastructure.Persistence.Users;

public sealed class User
{
    private DateTimeOffset _onboardedAt;

    public Guid Id { get; private set; }

    public required string TenantId { get; init; }

    /// <summary>Identifiant du user dans son tenant.</summary>
    public required string TenantUserId { get; init; }

    /// <summary>
    /// Instant d'insertion du user en base, en UTC. Renseigné par PostgreSQL (<c>DEFAULT now()</c>, heure de début
    /// de la transaction) et relu après l'insertion : l'application ne peut ni le fixer ni le modifier.
    /// </summary>
    public DateTimeOffset InsertedAt { get; private set; }

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
