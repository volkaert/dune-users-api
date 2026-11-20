using FastEndpoints;
using FluentValidation;

namespace DuneUsersApi.Features.Reconciliations;

public sealed class GetUserOnboardingsValidator : Validator<GetUserOnboardingsRequest>
{
    public const int MaxPageSize = 1000;

    /// <summary>
    /// Borne le numéro de page pour que le décalage <c>(page - 1) * pageSize</c> tienne dans un <c>int</c>.
    /// </summary>
    public const int MaxPage = 1_000_000;

    public GetUserOnboardingsValidator()
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

        RuleFor(r => r.AsOf)
            .NotNull();

        RuleFor(r => r.Page)
            .InclusiveBetween(1, MaxPage);

        RuleFor(r => r.PageSize)
            .InclusiveBetween(1, MaxPageSize);
    }
}
