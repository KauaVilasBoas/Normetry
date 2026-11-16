using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Identity.GetProfile;

public sealed record GetProfileQuery(Guid Id) : IQuery<ProfileResponse>;
