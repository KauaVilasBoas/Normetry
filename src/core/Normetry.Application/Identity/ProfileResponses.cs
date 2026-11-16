using Normetry.Domain.Identity;

namespace Normetry.Application.Identity;

/// <summary>A row of the permission catalog.</summary>
public sealed record PermissionResponse(string Name);

/// <summary>
/// A profile in the list. <see cref="PermissionCount"/> is the whole catalog for Owner
/// (<see cref="GrantsAll"/>).
/// </summary>
public sealed record ProfileListItem(
    Guid Id,
    string Name,
    string? SystemKey,
    bool GrantsAll,
    int PermissionCount
);

/// <summary>One profile with what it grants: the whole catalog for Owner.</summary>
public sealed record ProfileResponse(
    Guid Id,
    string Name,
    string? SystemKey,
    bool GrantsAll,
    IReadOnlyList<string> Permissions
)
{
    internal static ProfileResponse From(Profile profile, IReadOnlyList<string> catalog) =>
        new(
            profile.Id,
            profile.Name,
            profile.SystemKey,
            profile.GrantsAll,
            profile.GrantsAll
                ? catalog
                :
                [
                    .. profile
                        .GrantedPermissions.Select(granted => granted.Permission)
                        .Order(StringComparer.Ordinal),
                ]
        );
}

/// <summary>A user of the tenant with the profiles they hold.</summary>
public sealed record UserResponse(
    Guid Id,
    string? DisplayName,
    string? Email,
    bool IsCurrentUser,
    IReadOnlyList<ProfileSummary> Profiles
);
