namespace Normetry.Application.Documents;

/// <summary>
/// Tells the web clients watching an establishment that a version's status changed. Called only
/// after the change is committed; a client that misses one refetches on reconnect.
/// </summary>
public interface IDocumentStatusNotifier
{
    Task NotifyAsync(
        Guid tenantId,
        DocumentStatusChange change,
        CancellationToken cancellationToken
    );
}
