using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Establishments.ListEstablishments;

/// <summary>The caller's tenant's establishments, ordered by name.</summary>
public sealed record ListEstablishmentsQuery : IQuery<IReadOnlyList<EstablishmentResponse>>;
