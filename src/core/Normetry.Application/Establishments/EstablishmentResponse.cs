using Normetry.Domain.Establishments;

namespace Normetry.Application.Establishments;

public sealed record EstablishmentResponse(
    Guid Id,
    string Name,
    EstablishmentType Type,
    Jurisdiction Jurisdiction
)
{
    internal static EstablishmentResponse From(Establishment establishment) =>
        new(establishment.Id, establishment.Name, establishment.Type, establishment.Jurisdiction);
}
