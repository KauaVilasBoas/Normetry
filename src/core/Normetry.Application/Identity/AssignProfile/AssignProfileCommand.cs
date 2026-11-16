using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Identity.AssignProfile;

/// <summary>Gives a user of the tenant one of its profiles. Idempotent.</summary>
public sealed record AssignProfileCommand(Guid UserId, Guid ProfileId) : ICommand;
