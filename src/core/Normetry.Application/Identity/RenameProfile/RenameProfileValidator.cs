using FluentValidation;
using Normetry.Domain.Identity;

namespace Normetry.Application.Identity.RenameProfile;

internal sealed class RenameProfileValidator : AbstractValidator<RenameProfileCommand>
{
    public RenameProfileValidator() =>
        RuleFor(command => command.Name).NotEmpty().MaximumLength(Profile.NameMaxLength);
}
