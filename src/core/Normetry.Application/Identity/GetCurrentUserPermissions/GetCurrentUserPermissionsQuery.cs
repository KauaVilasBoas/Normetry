using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Identity.GetCurrentUserPermissions;

/// <summary>What the caller's profiles grant, read per request for the permission checks.</summary>
public sealed record GetCurrentUserPermissionsQuery : IQuery<IReadOnlyList<string>>;
