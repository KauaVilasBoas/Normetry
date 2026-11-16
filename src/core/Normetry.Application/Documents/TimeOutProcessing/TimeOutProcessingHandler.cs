using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Documents.TimeOutProcessing;

internal sealed class TimeOutProcessingHandler(
    IDocumentRepository documents,
    DocumentsOptions options,
    TimeProvider clock
) : ICommandHandler<TimeOutProcessingCommand, IReadOnlyList<DocumentStatusChange>>
{
    public async Task<Result<IReadOnlyList<DocumentStatusChange>>> HandleAsync(
        TimeOutProcessingCommand request,
        CancellationToken cancellationToken
    )
    {
        var now = clock.GetUtcNow();
        var changes = new List<DocumentStatusChange>();
        foreach (
            var document in await documents.ListProcessingSinceForUpdateAsync(
                now - options.ProcessingTimeout,
                cancellationToken
            )
        )
        {
            foreach (var version in document.Versions)
            {
                if (version.TimeOut(options.ProcessingTimeout, now))
                {
                    changes.Add(DocumentStatusChange.From(document, version));
                }
            }
        }
        return changes;
    }
}
