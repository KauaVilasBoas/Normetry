using Normetry.Application.Establishments;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Establishments;

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
        var found = await establishments.FindOrNotFoundAsync(
            request.EstablishmentId,
            cancellationToken
        );
        if (!found.IsSuccess)
        {
            return found.Error;
        }
        var establishment = found.Value;
        var regulation = Regulations.Of(establishment.Jurisdiction);
        return (await inspections.ListAsync(establishment.Id, cancellationToken))
            .Select(row => row with { Regulation = regulation })
            .ToArray();
    }
}
