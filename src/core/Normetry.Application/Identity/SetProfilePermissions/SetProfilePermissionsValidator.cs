using FluentValidation;
using Normetry.Domain.Identity;

namespace Normetry.Application.Identity.SetProfilePermissions;

internal sealed class SetProfilePermissionsValidator
    : AbstractValidator<SetProfilePermissionsCommand>
{
    public SetProfilePermissionsValidator()
    {
        RuleFor(command => command.Permissions).NotNull();
        RuleForEach(command => command.Permissions)
            .Must(name => Permissions.All.Contains(name))
            .WithMessage("'{PropertyValue}' is not in the permission catalog.");
    }
}
