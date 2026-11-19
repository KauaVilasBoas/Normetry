using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Establishments;

namespace Normetry.Application.Establishments;

/// <summary>
/// The one place a handler finds the establishment its route names, or refuses with
/// <see cref="NotFound"/>. Another tenant's establishment is invisible under row-level security,
/// so it is not found either: the answer never tells whether the id exists elsewhere.
/// </summary>
internal static class EstablishmentLookup
{
    internal static readonly Error NotFound = new(
        "establishment.not_found",
        "The establishment was not found.",
        ErrorType.NotFound
    );

    public static async Task<Result<Establishment>> FindOrNotFoundAsync(
        this IEstablishmentRepository establishments,
        Guid establishmentId,
        CancellationToken cancellationToken
    ) =>
        await establishments.GetAsync(establishmentId, cancellationToken) is { } establishment
            ? establishment
            : NotFound;

    /// <summary>
    /// Locks the establishment's row until the transaction ends
    /// (<see cref="IEstablishmentRepository.LockAsync"/>), or refuses with <see cref="NotFound"/>.
    /// </summary>
    public static async Task<Result> LockOrNotFoundAsync(
        this IEstablishmentRepository establishments,
        Guid establishmentId,
        CancellationToken cancellationToken
    ) =>
        await establishments.LockAsync(establishmentId, cancellationToken)
            ? Result.Success()
            : NotFound;
}
