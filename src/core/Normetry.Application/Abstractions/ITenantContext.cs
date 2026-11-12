namespace Normetry.Application.Abstractions;

/// <summary>
/// The tenant of the current request, read from the validated access token and never from the
/// request body, query string or headers. RLS scopes every query to it (ADR 003).
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// Throws when the request has no valid tenant. Tenant-scoped endpoints answer 403 to such
    /// requests before any handler runs, so a handler reaching the throw is a missing policy.
    /// </summary>
    Guid TenantId { get; }
}
