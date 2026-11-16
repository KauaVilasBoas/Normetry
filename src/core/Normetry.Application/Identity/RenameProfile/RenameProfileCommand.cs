using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Identity.RenameProfile;

/// <summary>Renames one of the tenant's own profiles; system profiles keep their name.</summary>
public sealed record RenameProfileCommand(Guid Id, string Name) : ICommand;
