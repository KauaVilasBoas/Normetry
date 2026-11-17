using FluentValidation;

namespace Normetry.Application.Documents.ListDocuments;

internal sealed class ListDocumentsValidator : AbstractValidator<ListDocumentsQuery>
{
    public ListDocumentsValidator() =>
        RuleFor(query => query.ExpiringWithinDays)
            .InclusiveBetween(1, 365)
            .Must((query, _) => !query.Expired)
            .WithMessage("Filter by expired or by expiring within days, not both.")
            .When(query => query.ExpiringWithinDays is not null);
}
