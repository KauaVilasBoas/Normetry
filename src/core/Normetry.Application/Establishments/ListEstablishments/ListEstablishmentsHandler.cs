using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Establishments.ListEstablishments;

internal sealed class ListEstablishmentsHandler(IEstablishmentRepository establishments)
    : IQueryHandler<ListEstablishmentsQuery, IReadOnlyList<EstablishmentResponse>>
{
    public async Task<Result<IReadOnlyList<EstablishmentResponse>>> HandleAsync(
        ListEstablishmentsQuery request,
        CancellationToken cancellationToken
    ) =>
        (await establishments.ListAsync(cancellationToken))
            .Select(EstablishmentResponse.From)
            .ToArray();
}
