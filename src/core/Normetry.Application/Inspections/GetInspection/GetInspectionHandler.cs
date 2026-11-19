using Normetry.Application.Establishments;
using Normetry.Application.Identity;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Inspections.GetInspection;

internal sealed class GetInspectionHandler(
    IEstablishmentRepository establishments,
    IInspectionRepository inspections,
    IUserRepository users
) : IQueryHandler<GetInspectionQuery, InspectionResponse>
{
    public async Task<Result<InspectionResponse>> HandleAsync(
        GetInspectionQuery request,
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
        // Another establishment's inspection is not found here either.
        if (
            await inspections.GetAsync(request.Id, cancellationToken) is not { } inspection
            || inspection.EstablishmentId != establishment.Id
        )
        {
            return InspectionErrors.NotFound;
        }
        return InspectionResponse.From(
            inspection,
            InspectionResponse.RegulationOf(establishment.Jurisdiction),
            await users.GetDisplayNamesAsync(
                InspectionResponse.UserIds(inspection),
                cancellationToken
            )
        );
    }
}
