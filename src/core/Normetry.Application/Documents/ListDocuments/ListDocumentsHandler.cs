using Normetry.Application.Establishments;
using Normetry.Application.Establishments.GetEstablishment;
using Normetry.Application.Identity;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Documents.ListDocuments;

internal sealed class ListDocumentsHandler(
    IEstablishmentRepository establishments,
    IDocumentRepository documents,
    IUserRepository users
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
        var list = await documents.ListAsync(request.EstablishmentId, cancellationToken);
        var uploaders = await users.GetDisplayNamesAsync(
            list.Select(document => document.Newest.UploadedBy).Distinct(),
            cancellationToken
        );
        return list.OrderByDescending(document => document.UpdatedAt)
            .ThenByDescending(document => document.Id)
            .Select(document => DocumentSummary.From(document, uploaders))
            .ToArray();
    }
}
