using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace Normetry.Infrastructure.Messaging;

/// <summary>Names and arguments of <c>src/shared/contracts/messages/README.md</c>, "Topology".</summary>
internal static class Topology
{
    public const string Events = "normetry.events";
    public const string DeadLetter = "normetry.dead-letter";
    public const string StatusQueue = "core.document-status";
    public const string StatusDeadLetterQueue = "core.document-status.dead-letter";
    public const string InspectionStatusQueue = "core.inspection-status";
    public const string InspectionStatusDeadLetterQueue = "core.inspection-status.dead-letter";

    /// <summary>A message gets five deliveries, then goes to the dead-letter queue.</summary>
    public const int DeliveryLimit = 4;

    public static readonly string[] StatusRoutingKeys =
    [
        Application.Documents.DocumentIngestionStarted.RoutingKey,
        Application.Documents.DocumentIndexed.RoutingKey,
        Application.Documents.DocumentIngestionFailed.RoutingKey,
    ];

    public static readonly string[] InspectionStatusRoutingKeys =
    [
        Application.Inspections.InspectionStarted.RoutingKey,
        Application.Inspections.InspectionProgressed.RoutingKey,
        Application.Inspections.InspectionCompleted.RoutingKey,
        Application.Inspections.InspectionFailed.RoutingKey,
    ];

    /// <summary>Both exchanges. Idempotent, as long as the arguments match the contract.</summary>
    public static async Task DeclareExchangesAsync(
        IChannel channel,
        CancellationToken cancellationToken
    )
    {
        await channel.ExchangeDeclareAsync(
            Events,
            ExchangeType.Topic,
            durable: true,
            cancellationToken: cancellationToken
        );
        await channel.ExchangeDeclareAsync(
            DeadLetter,
            ExchangeType.Direct,
            durable: true,
            cancellationToken: cancellationToken
        );
    }

    /// <summary>The document status queue, its dead-letter queue and their bindings.</summary>
    public static Task DeclareStatusQueueAsync(
        IChannel channel,
        CancellationToken cancellationToken
    ) =>
        DeclareConsumedQueueAsync(
            channel,
            StatusQueue,
            StatusDeadLetterQueue,
            StatusRoutingKeys,
            cancellationToken
        );

    /// <summary>The inspection status queue, its dead-letter queue and their bindings.</summary>
    public static Task DeclareInspectionStatusQueueAsync(
        IChannel channel,
        CancellationToken cancellationToken
    ) =>
        DeclareConsumedQueueAsync(
            channel,
            InspectionStatusQueue,
            InspectionStatusDeadLetterQueue,
            InspectionStatusRoutingKeys,
            cancellationToken
        );

    /// <summary>A queue the core consumes, its dead-letter queue and their bindings.</summary>
    private static async Task DeclareConsumedQueueAsync(
        IChannel channel,
        string queue,
        string deadLetterQueue,
        string[] routingKeys,
        CancellationToken cancellationToken
    )
    {
        await DeclareExchangesAsync(channel, cancellationToken);
        await channel.QueueDeclareAsync(
            queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-dead-letter-exchange"] = DeadLetter,
                ["x-dead-letter-routing-key"] = queue,
                ["x-delivery-limit"] = DeliveryLimit,
            },
            cancellationToken: cancellationToken
        );
        foreach (var key in routingKeys)
        {
            await channel.QueueBindAsync(queue, Events, key, cancellationToken: cancellationToken);
        }
        await channel.QueueDeclareAsync(
            deadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
            cancellationToken: cancellationToken
        );
        await channel.QueueBindAsync(
            deadLetterQueue,
            DeadLetter,
            queue,
            cancellationToken: cancellationToken
        );
    }
}

/// <summary>The AMQP headers the core writes or reads.</summary>
internal static class MessageHeaders
{
    /// <summary>The W3C trace context of the span that published the message.</summary>
    public const string TraceParent = "traceparent";

    public const string TraceState = "tracestate";

    /// <summary>The broker's count of the message's earlier deliveries; absent on the first.</summary>
    public const string DeliveryCount = "x-delivery-count";
}

/// <summary>
/// The one broker connection of the process (<c>ConnectionStrings:rabbitmq</c>, an AMQP URI),
/// opened on first use and recovered by the client after a network failure.
/// </summary>
internal sealed class RabbitMqConnection(IConfiguration configuration) : IAsyncDisposable
{
    public const string ConnectionStringName = "rabbitmq";

    private readonly Uri uri = new(
        configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Set ConnectionStrings:{ConnectionStringName} (the RabbitMQ AMQP URI)."
            )
    );
    private readonly SemaphoreSlim gate = new(1, 1);
    private IConnection? connection;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken)
    {
        if (connection is not null)
        {
            return connection;
        }
        await gate.WaitAsync(cancellationToken);
        try
        {
            return connection ??= await new ConnectionFactory
            {
                Uri = uri,
                ClientProvidedName = "normetry-core",
                AutomaticRecoveryEnabled = true,
            }.CreateConnectionAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (connection is not null)
        {
            await connection.DisposeAsync();
        }
        gate.Dispose();
    }
}

/// <summary>
/// Publishes to <c>normetry.events</c> with publisher confirms and the mandatory flag: the call
/// returns only once the broker has taken the message, and throws on a nack or when no queue is
/// bound to the routing key. Used by one dispatcher at a time.
/// </summary>
internal sealed class RabbitMqPublisher(RabbitMqConnection broker) : IAsyncDisposable
{
    private IChannel? channel;

    /// <summary>
    /// <paramref name="expiresIn"/> becomes the per-message TTL (AMQP <c>expiration</c>, whole
    /// milliseconds). One already past is 0: only a consumer waiting at that moment would receive
    /// it; otherwise the queue expires and dead-letters it at once.
    /// </summary>
    public async Task PublishAsync(
        OutboxMessage message,
        TimeSpan? expiresIn,
        CancellationToken cancellationToken
    )
    {
        var channel = await ChannelAsync(cancellationToken);
        using var activity = MessagingTelemetry.StartActivity(
            $"{message.RoutingKey} publish",
            ActivityKind.Producer,
            message.TraceParent,
            message.TraceState,
            message.Id.ToString()
        );
        activity?.SetTag(MessagingTelemetry.TenantIdTag, message.TenantId.ToString());

        var headers = new Dictionary<string, object?>();
        // This span when telemetry listens; the writing request's otherwise.
        if ((activity?.Id ?? message.TraceParent) is { } traceParent)
        {
            headers[MessageHeaders.TraceParent] = traceParent;
            if ((activity is null ? message.TraceState : activity.TraceStateString) is { } state)
            {
                headers[MessageHeaders.TraceState] = state;
            }
        }
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = message.Id.ToString(),
            Type = message.Type,
            Headers = headers,
            Expiration = expiresIn is { } ttl
                ? Math.Max(0, (long)Math.Ceiling(ttl.TotalMilliseconds))
                    .ToString(CultureInfo.InvariantCulture)
                : null,
        };
        await channel.BasicPublishAsync(
            Topology.Events,
            message.RoutingKey,
            mandatory: true,
            properties,
            Encoding.UTF8.GetBytes(message.Body),
            cancellationToken
        );
    }

    private async Task<IChannel> ChannelAsync(CancellationToken cancellationToken)
    {
        if (channel is { IsOpen: true })
        {
            return channel;
        }
        if (channel is not null)
        {
            await channel.DisposeAsync();
            channel = null;
        }
        var connection = await broker.GetAsync(cancellationToken);
        var opened = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true
            ),
            cancellationToken
        );
        await Topology.DeclareExchangesAsync(opened, cancellationToken);
        return channel = opened;
    }

    public async ValueTask DisposeAsync()
    {
        if (channel is not null)
        {
            await channel.DisposeAsync();
        }
    }
}

/// <summary>Spans for publishing and consuming; ServiceDefaults listens to it.</summary>
internal static class MessagingTelemetry
{
    /// <summary>The span tag that holds the message's tenant.</summary>
    public const string TenantIdTag = "normetry.tenant_id";

    private const string SystemTag = "messaging.system";
    private const string MessageIdTag = "messaging.message.id";
    private const string RabbitMqSystem = "rabbitmq";

    private static readonly ActivitySource Source = new("Normetry.Messaging");

    /// <summary>
    /// Starts the span of one message as a child of the W3C trace context it carries (of the
    /// current span, if any, when that is absent or malformed), tagged with the messaging system
    /// and the message id. Null when nothing listens.
    /// </summary>
    public static Activity? StartActivity(
        string name,
        ActivityKind kind,
        string? traceParent,
        string? traceState,
        string? messageId
    )
    {
        ActivityContext.TryParse(traceParent, traceState, out var parent);
        var activity = Source.StartActivity(name, kind, parent);
        activity?.SetTag(SystemTag, RabbitMqSystem);
        activity?.SetTag(MessageIdTag, messageId);
        return activity;
    }
}
