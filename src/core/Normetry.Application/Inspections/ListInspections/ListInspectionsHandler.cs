using Normetry.Application.Establishments;
using Normetry.Application.Establishments.GetEstablishment;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Inspections.ListInspections;

internal sealed class ListInspectionsHandler(
    IEstablishmentRepository establishments,
    IInspectionRepository inspections
) : IQueryHandler<ListInspectionsQuery, IReadOnlyList<InspectionSummary>>
{
    public async Task<Result<IReadOnlyList<InspectionSummary>>> HandleAsync(
        ListInspectionsQuery request,
        CancellationToken cancellationToken
    )
    {
        if (
            await establishments.GetAsync(request.EstablishmentId, cancellationToken)
            is not { } establishment
        )
        {
            return GetEstablishmentHandler.NotFound;
        }
        var regulation = InspectionResponse.RegulationOf(establishment.Jurisdiction);
        return (await inspections.ListAsync(establishment.Id, cancellationToken))
            .Select(row => row with { Regulation = regulation })
            .ToArray();
    }
}
