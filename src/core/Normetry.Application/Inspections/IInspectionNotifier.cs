namespace Normetry.Application.Inspections;

/// <summary>
/// Tells the web clients watching an establishment that an inspection changed. Called only after
/// the change is committed; a client that misses one refetches on reconnect.
/// </summary>
public interface IInspectionNotifier
{
    Task NotifyAsync(Guid tenantId, InspectionChange change, CancellationToken cancellationToken);
}
