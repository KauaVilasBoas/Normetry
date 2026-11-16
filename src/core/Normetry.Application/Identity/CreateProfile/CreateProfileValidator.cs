using FluentValidation;
using Normetry.Domain.Identity;

namespace Normetry.Application.Identity.CreateProfile;

internal sealed class CreateProfileValidator : AbstractValidator<CreateProfileCommand>
{
    public CreateProfileValidator() =>
        RuleFor(command => command.Name).NotEmpty().MaximumLength(Profile.NameMaxLength);
}
