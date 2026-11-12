using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Establishments.GetEstablishment;

public sealed record GetEstablishmentQuery(Guid Id) : IQuery<EstablishmentResponse>;
