using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Establishments.GetEstablishment;

internal sealed class GetEstablishmentHandler(IEstablishmentRepository establishments)
    : IQueryHandler<GetEstablishmentQuery, EstablishmentResponse>
{
    public async Task<Result<EstablishmentResponse>> HandleAsync(
        GetEstablishmentQuery request,
        CancellationToken cancellationToken
    )
    {
        var found = await establishments.FindOrNotFoundAsync(request.Id, cancellationToken);
        return found.IsSuccess ? EstablishmentResponse.From(found.Value) : found.Error;
    }
}
