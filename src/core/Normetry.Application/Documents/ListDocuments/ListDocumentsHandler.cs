using Normetry.Application.Establishments;
using Normetry.Application.Establishments.GetEstablishment;
using Normetry.Application.Identity;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Documents;

namespace Normetry.Application.Documents.ListDocuments;

internal sealed class ListDocumentsHandler(
    IEstablishmentRepository establishments,
    IDocumentRepository documents,
    IUserRepository users,
    TimeProvider clock
) : IQueryHandler<ListDocumentsQuery, IReadOnlyList<DocumentSummary>>
{
    public async Task<Result<IReadOnlyList<DocumentSummary>>> HandleAsync(
        ListDocumentsQuery request,
        CancellationToken cancellationToken
    )
    {
        if (await establishments.GetAsync(request.EstablishmentId, cancellationToken) is null)
        {
            return GetEstablishmentHandler.NotFound;
        }
        // Filtered in memory: an establishment holds at most MaxDocumentsPerEstablishment.
        IEnumerable<Document> list = await documents.ListAsync(
            request.EstablishmentId,
            cancellationToken
        );
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (request.Expired)
        {
            list = list.Where(document => document.ExpiresOn < today);
        }
        if (request.ExpiringWithinDays is { } days)
        {
            list = list.Where(document =>
                document.ExpiresOn >= today && document.ExpiresOn <= today.AddDays(days)
            );
        }
        var filtered = list.ToArray();
        var uploaders = await users.GetDisplayNamesAsync(
            filtered.Select(document => document.Newest.UploadedBy).Distinct(),
            cancellationToken
        );
        return filtered
            .OrderByDescending(document => document.UpdatedAt)
            .ThenByDescending(document => document.Id)
            .Select(document => DocumentSummary.From(document, uploaders))
            .ToArray();
    }
}
