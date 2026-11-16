using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Identity.ListUsers;

/// <summary>The tenant's users with their profiles, the caller marked.</summary>
public sealed record ListUsersQuery : IQuery<IReadOnlyList<UserResponse>>;
