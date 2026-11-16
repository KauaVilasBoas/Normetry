using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Identity.ListPermissions;

/// <summary>The permission catalog, for the profile editor.</summary>
public sealed record ListPermissionsQuery : IQuery<IReadOnlyList<PermissionResponse>>;
