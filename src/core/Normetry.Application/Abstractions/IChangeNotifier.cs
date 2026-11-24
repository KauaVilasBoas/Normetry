namespace Normetry.Application.Abstractions;

/// <summary>
/// Tells the web clients watching an establishment about a change (a document version's status, an
/// inspection's progress). Called only after the change is committed; a client that misses one
/// refetches on reconnect.
/// </summary>
public interface IChangeNotifier<in TChange>
{
    Task NotifyAsync(Guid tenantId, TChange change, CancellationToken cancellationToken);
}
