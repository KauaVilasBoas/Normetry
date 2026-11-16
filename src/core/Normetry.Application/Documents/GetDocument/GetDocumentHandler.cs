using Normetry.Application.Identity;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Documents.GetDocument;

internal sealed class GetDocumentHandler(IDocumentRepository documents, IUserRepository users)
    : IQueryHandler<GetDocumentQuery, DocumentResponse>
{
    public async Task<Result<DocumentResponse>> HandleAsync(
        GetDocumentQuery request,
        CancellationToken cancellationToken
    )
    {
        if (await documents.GetAsync(request.Id, cancellationToken) is not { } document)
        {
            return DocumentErrors.NotFound;
        }
        return await DocumentUploads.ResponseAsync(users, document, cancellationToken);
    }
}
