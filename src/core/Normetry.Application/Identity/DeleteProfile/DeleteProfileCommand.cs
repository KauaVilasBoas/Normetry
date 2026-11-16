using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Identity.DeleteProfile;

/// <summary>
/// Deletes one of the tenant's own profiles and takes it from every user who held it. System
/// profiles cannot be deleted.
/// </summary>
public sealed record DeleteProfileCommand(Guid Id) : ICommand;
