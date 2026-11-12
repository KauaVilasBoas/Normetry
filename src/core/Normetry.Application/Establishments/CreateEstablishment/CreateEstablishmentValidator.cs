using FluentValidation;
using Normetry.Domain.Establishments;

namespace Normetry.Application.Establishments.CreateEstablishment;

internal sealed class CreateEstablishmentValidator : AbstractValidator<CreateEstablishmentCommand>
{
    public CreateEstablishmentValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(Establishment.NameMaxLength);
        RuleFor(command => command.Type).IsInEnum();
        RuleFor(command => command.Jurisdiction).IsInEnum();
    }
}
