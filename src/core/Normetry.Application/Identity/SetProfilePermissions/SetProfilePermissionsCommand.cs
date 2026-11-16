using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Identity.SetProfilePermissions;

/// <summary>
/// Replaces what a profile grants. Each area's <c>.read</c> is added when another permission of
/// that area is given. Returns the profile as stored. Owner cannot be changed.
/// </summary>
public sealed record SetProfilePermissionsCommand(Guid Id, IReadOnlyList<string> Permissions)
    : ICommand<ProfileResponse>;
