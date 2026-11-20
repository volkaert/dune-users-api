namespace DuneUsersApi.Infrastructure.Persistence.Users;

/// <summary>
/// Adresse postale d'un user. Type complexe EF Core : stockée dans les colonnes <c>address_*</c> de la table <c>users</c>.
/// </summary>
public sealed record Address
{
    public required string Street { get; init; }

    public required string PostalCode { get; init; }

    public required string City { get; init; }

    /// <summary>Code pays ISO 3166-1 alpha-2, en majuscules (ex. <c>FR</c>).</summary>
    public required string CountryCode { get; init; }
}
