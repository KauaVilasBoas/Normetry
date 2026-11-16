using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Identity.UnassignProfile;

/// <summary>
/// Takes a profile from a user. Refused for the tenant's last Owner (the lockout guard), so a
/// tenant always keeps someone who can manage profiles. Idempotent otherwise.
/// </summary>
public sealed record UnassignProfileCommand(Guid UserId, Guid ProfileId) : ICommand;
