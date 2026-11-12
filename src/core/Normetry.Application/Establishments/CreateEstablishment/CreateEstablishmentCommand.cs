using Normetry.BuildingBlocks.Cqrs;
using Normetry.Domain.Establishments;

namespace Normetry.Application.Establishments.CreateEstablishment;

/// <summary>Creates an establishment in the caller's tenant and returns its id.</summary>
public sealed record CreateEstablishmentCommand(
    string Name,
    EstablishmentType Type,
    Jurisdiction Jurisdiction
) : ICommand<Guid>;
