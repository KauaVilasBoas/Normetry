using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Identity.CreateProfile;

/// <summary>Creates a profile of the caller's tenant, with no permissions, and returns its id.</summary>
public sealed record CreateProfileCommand(string Name) : ICommand<Guid>;
