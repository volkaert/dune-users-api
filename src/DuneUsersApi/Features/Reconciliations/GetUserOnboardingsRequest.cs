namespace DuneUsersApi.Features.Reconciliations;

/// <summary>
/// Paramètres de query de <see cref="GetUserOnboardingsEndpoint"/>. Les paramètres obligatoires sont nullables pour
/// qu'un paramètre absent soit signalé par le validateur, au lieu d'être lié à sa valeur par défaut.
/// </summary>
public sealed class GetUserOnboardingsRequest
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
}
