using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Identity.ListProfiles;

/// <summary>The tenant's profiles, system ones included, ordered by name.</summary>
public sealed record ListProfilesQuery : IQuery<IReadOnlyList<ProfileListItem>>;
