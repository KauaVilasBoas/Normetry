using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Establishments.GetEstablishment;

internal sealed class GetEstablishmentHandler(IEstablishmentRepository establishments)
    : IQueryHandler<GetEstablishmentQuery, EstablishmentResponse>
{
    // Another tenant's establishment is invisible under row-level security, so it is not found
    // either: the answer never tells whether the id exists elsewhere.
    internal static readonly Error NotFound = new(
        "establishment.not_found",
        "The establishment was not found.",
        ErrorType.NotFound
    );

    public async Task<Result<EstablishmentResponse>> HandleAsync(
        GetEstablishmentQuery request,
        CancellationToken cancellationToken
    ) =>
        await establishments.GetAsync(request.Id, cancellationToken) is { } establishment
            ? EstablishmentResponse.From(establishment)
            : NotFound;
}
