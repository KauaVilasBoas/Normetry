using System.Diagnostics;
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

    /// <summary>A message gets five deliveries, then goes to the dead-letter queue.</summary>
    public const int DeliveryLimit = 4;

    public static readonly string[] StatusRoutingKeys =
    [
        Application.Documents.DocumentIngestionStarted.RoutingKey,
        Application.Documents.DocumentIndexed.RoutingKey,
        Application.Documents.DocumentIngestionFailed.RoutingKey,
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

    /// <summary>The queue the core consumes, its dead-letter queue and their bindings.</summary>
    public static async Task DeclareStatusQueueAsync(
        IChannel channel,
        CancellationToken cancellationToken
    )
    {
        await DeclareExchangesAsync(channel, cancellationToken);
        await channel.QueueDeclareAsync(
            StatusQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-dead-letter-exchange"] = DeadLetter,
                ["x-dead-letter-routing-key"] = StatusQueue,
                ["x-delivery-limit"] = DeliveryLimit,
            },
            cancellationToken: cancellationToken
        );
        foreach (var key in StatusRoutingKeys)
        {
            await channel.QueueBindAsync(
                StatusQueue,
                Events,
                key,
                cancellationToken: cancellationToken
            );
        }
        await channel.QueueDeclareAsync(
            StatusDeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
            cancellationToken: cancellationToken
        );
        await channel.QueueBindAsync(
            StatusDeadLetterQueue,
            DeadLetter,
            StatusQueue,
            cancellationToken: cancellationToken
        );
    }
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

    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var channel = await ChannelAsync(cancellationToken);
        ActivityContext.TryParse(message.TraceParent, message.TraceState, out var parent);
        using var activity = MessagingTelemetry.Source.StartActivity(
            $"{message.RoutingKey} publish",
            ActivityKind.Producer,
            parent
        );
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.message.id", message.Id.ToString());
        activity?.SetTag("normetry.tenant_id", message.TenantId.ToString());

        var headers = new Dictionary<string, object?>();
        // This span when telemetry listens; the writing request's otherwise.
        if ((activity?.Id ?? message.TraceParent) is { } traceParent)
        {
            headers["traceparent"] = traceParent;
            if ((activity is null ? message.TraceState : activity.TraceStateString) is { } state)
            {
                headers["tracestate"] = state;
            }
        }
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = message.Id.ToString(),
            Type = message.Type,
            Headers = headers,
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

internal static class MessagingTelemetry
{
    /// <summary>Spans for publishing and consuming; ServiceDefaults listens to it.</summary>
    public static readonly ActivitySource Source = new("Normetry.Messaging");
}
