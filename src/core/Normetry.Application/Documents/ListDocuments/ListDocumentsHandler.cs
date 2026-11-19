using Normetry.Application.Establishments;
using Normetry.Application.Identity;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.BuildingBlocks.Time;
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
        var found = await establishments.FindOrNotFoundAsync(
            request.EstablishmentId,
            cancellationToken
        );
        if (!found.IsSuccess)
        {
            return found.Error;
        }
        var listed = await documents.ListAsync(request.EstablishmentId, cancellationToken);
        var filtered = KeepRequestedExpiry(listed, request, clock.UtcToday());
        var uploaders = await users.GetDisplayNamesAsync(
            filtered.Select(document => document.Newest.UploadedBy).Distinct(),
            cancellationToken
        );
        return SummariesNewestFirst(filtered, uploaders);
    }

    /// <summary>
    /// The documents the expiry filter keeps: all of them, the expired ones or the ones expiring
    /// within the requested days. Filtered in memory: an establishment holds at most
    /// MaxDocumentsPerEstablishment.
    /// </summary>
    private static Document[] KeepRequestedExpiry(
        IEnumerable<Document> list,
        ListDocumentsQuery request,
        DateOnly today
    )
    {
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
        return list.ToArray();
    }

    /// <summary>The summaries, most recently updated first.</summary>
    private static DocumentSummary[] SummariesNewestFirst(
        Document[] filtered,
        IReadOnlyDictionary<Guid, string?> uploaders
    ) =>
        filtered
            .OrderByDescending(document => document.UpdatedAt)
            .ThenByDescending(document => document.Id)
            .Select(document => DocumentSummary.From(document, uploaders))
            .ToArray();
}
