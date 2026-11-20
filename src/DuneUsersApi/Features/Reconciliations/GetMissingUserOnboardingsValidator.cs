using FastEndpoints;
using FluentValidation;

namespace DuneUsersApi.Features.Reconciliations;

/// <summary>
/// Indépendant de <see cref="GetUserOnboardingsValidator"/> : les règles communes y sont redéclarées pour que les
/// deux contrats puissent évoluer séparément.
/// </summary>
public sealed class GetMissingUserOnboardingsValidator : Validator<GetMissingUserOnboardingsRequest>
{
    public const int MaxPageSize = 1000;

    /// <summary>
    /// Borne le numéro de page pour que le décalage <c>(page - 1) * pageSize</c> tienne dans un <c>int</c>.
    /// </summary>
    public const int MaxPage = 1_000_000;

    public GetMissingUserOnboardingsValidator()
    {
        RuleFor(r => r.TenantId)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(r => r.FromOnboardedAt)
            .NotNull();

        RuleFor(r => r.ToOnboardedAt)
            .NotNull()
            .GreaterThan(r => r.FromOnboardedAt)
            .When(r => r.FromOnboardedAt is not null, ApplyConditionTo.CurrentValidator)
            .WithMessage("'{PropertyName}' must be after '{ComparisonProperty}'.");

        RuleFor(r => r.MissingsSince)
            .NotNull();

        RuleFor(r => r.AsOf)
            .NotNull()
            .GreaterThan(r => r.MissingsSince)
            .When(r => r.MissingsSince is not null, ApplyConditionTo.CurrentValidator)
            .WithMessage("'{PropertyName}' must be after '{ComparisonProperty}'.");

        RuleFor(r => r.Page)
            .InclusiveBetween(1, MaxPage);

        RuleFor(r => r.PageSize)
            .InclusiveBetween(1, MaxPageSize);
    }
}
