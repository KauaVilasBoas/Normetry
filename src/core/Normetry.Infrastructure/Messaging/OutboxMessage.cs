namespace Normetry.Infrastructure.Messaging;

/// <summary>
/// A message waiting to be published (<c>core.outbox_messages</c>), written in the transaction of
/// the state change it announces. <see cref="SentAt"/> is set only after the broker confirmed it.
/// </summary>
internal sealed class OutboxMessage
{
    public Guid TenantId { get; init; }

    /// <summary>Also the envelope's <c>messageId</c>.</summary>
    public Guid Id { get; init; }

    public required string Type { get; init; }

    public required string RoutingKey { get; init; }

    /// <summary>The whole envelope, as published.</summary>
    public required string Body { get; init; }

    /// <summary>W3C trace context of the request that wrote the message, if it had one.</summary>
    public string? TraceParent { get; init; }

    public string? TraceState { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? SentAt { get; set; }
}

/// <summary>
/// A tenant that has written outbox messages (<c>core.outbox_tenants</c>). <c>app_core</c> cannot
/// list tenants under RLS, so the background services visit the tenants listed here, each in its
/// own tenant-scoped transaction. The table holds nothing but the id.
/// </summary>
internal sealed record OutboxTenant(Guid TenantId);

/// <summary>
/// A consumed message (<c>core.inbox_messages</c>), recorded in the transaction that applies it,
/// so a redelivery changes nothing.
/// </summary>
internal sealed class InboxMessage
{
    public Guid TenantId { get; init; }

    public Guid MessageId { get; init; }

    public required string Type { get; init; }

    public DateTimeOffset ReceivedAt { get; init; }
}
