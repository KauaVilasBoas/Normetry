using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Identity;

/// <summary>The stable codes of the profile and user management refusals.</summary>
internal static class IdentityErrors
{
    // Another tenant's profile or user is invisible under row-level security, so it is not found
    // either: the answer never tells whether the id exists elsewhere.
    public static readonly Error ProfileNotFound = new(
        "profile.not_found",
        "The profile was not found.",
        ErrorType.NotFound
    );

    public static readonly Error UserNotFound = new(
        "user.not_found",
        "The user was not found.",
        ErrorType.NotFound
    );

    public static readonly Error NameTaken = new(
        "profile.name_taken",
        "Another profile of your organization already has this name.",
        ErrorType.Conflict
    );

    public static readonly Error SystemProfile = new(
        "profile.system_profile",
        "Owner, Manager and Auditor are system profiles: they cannot be renamed or deleted.",
        ErrorType.Conflict
    );

    public static readonly Error OwnerLocked = new(
        "profile.owner_locked",
        "The Owner profile grants every permission and cannot be changed.",
        ErrorType.Conflict
    );

    public static readonly Error LastOwner = new(
        "profile.last_owner",
        "This user is the organization's last Owner. Make another user an Owner first.",
        ErrorType.Conflict
    );
}
