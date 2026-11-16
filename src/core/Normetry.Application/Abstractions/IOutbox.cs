namespace Normetry.Application.Abstractions;

/// <summary>
/// The payload of a queue message in <c>src/shared/contracts/messages</c>, with the name, version
/// and routing key of its schema.
/// </summary>
public interface IIntegrationMessage
{
    static abstract string Type { get; }

    static abstract int Version { get; }

    static abstract string RoutingKey { get; }
}

/// <summary>
/// Messages for other services, written with the state change they announce (ADR 005). A message
/// is saved when the request's transaction commits, so it exists if and only if the change does;
/// a background dispatcher publishes it afterwards, at least once.
/// </summary>
public interface IOutbox
{
    /// <summary>Adds <paramref name="message"/> for the current tenant.</summary>
    Task EnqueueAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
        where TMessage : IIntegrationMessage;
}
