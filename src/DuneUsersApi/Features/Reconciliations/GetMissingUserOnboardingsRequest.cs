namespace DuneUsersApi.Features.Reconciliations;

/// <summary>
/// Paramètres de query de <see cref="GetMissingUserOnboardingsEndpoint"/>. Contrat indépendant de
/// <see cref="GetUserOnboardingsRequest"/>, même si les paramètres communs ont aujourd'hui les mêmes règles. Les
/// paramètres obligatoires sont nullables pour qu'un paramètre absent soit signalé par le validateur, au lieu
/// d'être lié à sa valeur par défaut.
/// </summary>
public sealed class GetMissingUserOnboardingsRequest
{
    public const int DefaultPageSize = 1000;

    public string? TenantId { get; set; }

    /// <summary>Borne incluse de la plage de dates d'onboarding.</summary>
    public DateTimeOffset? FromOnboardedAt { get; set; }

    /// <summary>Borne exclue de la plage de dates d'onboarding.</summary>
    public DateTimeOffset? ToOnboardedAt { get; set; }

    /// <summary>Numéro de page, à partir de 1.</summary>
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>
    /// Borne exclue de la fenêtre d'insertion : seuls les users insérés en base après cet instant
    /// (<c>inserted_at &gt; missingsSince</c>) sont renvoyés. Normalement le <c>asOf</c> du parcours précédent.
    /// </summary>
    public DateTimeOffset? MissingsSince { get; set; }

    /// <summary>
    /// Borne incluse de la fenêtre d'insertion (<c>inserted_at &lt;= asOf</c>), fournie par l'appelant. L'appelant
    /// passe la même valeur pour toutes les pages, puis la réutilise comme <c>missingsSince</c> à l'appel suivant.
    /// </summary>
    public DateTimeOffset? AsOf { get; set; }
}
