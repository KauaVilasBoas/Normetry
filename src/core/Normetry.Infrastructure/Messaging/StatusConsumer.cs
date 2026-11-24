using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Normetry.Infrastructure.Persistence;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Normetry.Infrastructure.Messaging;

/// <summary>
/// Consumes <c>core.document-status</c> (<see cref="DocumentStatusMessages"/>).
/// </summary>
internal sealed class DocumentStatusConsumer(
    RabbitMqConnection broker,
    DocumentStatusMessages messages,
    IConfiguration configuration,
    ILogger<DocumentStatusConsumer> logger
) : StatusConsumer(broker, configuration, logger)
{
    protected override string Queue => Topology.StatusQueue;

    protected override Task DeclareAsync(IChannel channel, CancellationToken cancellationToken) =>
        Topology.DeclareStatusQueueAsync(channel, cancellationToken);

    protected override Task<ConsumeOutcome> HandleAsync(
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken
    ) => messages.HandleAsync(body, cancellationToken);
}

/// <summary>
/// Consumes <c>core.inspection-status</c> (<see cref="InspectionStatusMessages"/>).
/// </summary>
internal sealed class InspectionStatusConsumer(
    RabbitMqConnection broker,
    InspectionStatusMessages messages,
    IConfiguration configuration,
    ILogger<InspectionStatusConsumer> logger
) : StatusConsumer(broker, configuration, logger)
{
    protected override string Queue => Topology.InspectionStatusQueue;

    protected override Task DeclareAsync(IChannel channel, CancellationToken cancellationToken) =>
        Topology.DeclareInspectionStatusQueueAsync(channel, cancellationToken);

    protected override Task<ConsumeOutcome> HandleAsync(
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken
    ) => messages.HandleAsync(body, cancellationToken);
}

/// <summary>
/// Consumes one of the core's status queues (declared here with its dead-letter queue, as the
/// contract says), one message at a time. An error is retried after 2, 10, 30, then 60 seconds by
/// a reject with requeue, which the broker counts; with <c>x-delivery-limit: 4</c> the fifth
/// delivery that fails goes to the queue's <c>.dead-letter</c>. A body off the contract is
/// dead-lettered at once. When the broker cancels the consumer (its queue was deleted) or closes
/// the channel, the channel and the consumer are created again. While the database does not
/// answer, a failed message is held (unacknowledged, so it spends none of its deliveries) and
/// handled again every 10 seconds; RabbitMQ's consumer timeout (30 minutes by default) bounds one
/// hold.
/// </summary>
internal abstract class StatusConsumer(
    RabbitMqConnection broker,
    IConfiguration configuration,
    ILogger logger
) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OutageWait = TimeSpan.FromSeconds(10);

    private readonly string connectionString = CoreDatabase.GetConnectionString(configuration);

    protected abstract string Queue { get; }

    /// <summary>Declares the queue, its dead-letter queue and their bindings.</summary>
    protected abstract Task DeclareAsync(IChannel channel, CancellationToken cancellationToken);

    protected abstract Task<ConsumeOutcome> HandleAsync(
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken
    );

    /// <summary>The waits before each requeue: 102 seconds in all, to outlast a database restart.</summary>
    private static readonly int[] RequeueSeconds = [2, 10, 30, 60];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(error, "Cannot consume {Queue}; retrying", Queue);
                await Task.Delay(RetryDelay, stoppingToken)
                    .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var connection = await broker.GetAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(
            cancellationToken: stoppingToken
        );
        await DeclareAsync(channel, stoppingToken);
        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 1,
            global: false,
            stoppingToken
        );
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) =>
            OnDeliveryAsync(channel, delivery, stoppingToken);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.UnregisteredAsync += (_, _) => Stop(stopped);
        consumer.ShutdownAsync += (_, _) => Stop(stopped);
        await channel.BasicConsumeAsync(Queue, autoAck: false, consumer, stoppingToken);
        await stopped
            .Task.WaitAsync(stoppingToken)
            .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (!stoppingToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("The broker cancelled the consumer or its channel");
        }
    }

    private static Task Stop(TaskCompletionSource stopped)
    {
        stopped.TrySetResult();
        return Task.CompletedTask;
    }

    private async Task OnDeliveryAsync(
        IChannel channel,
        BasicDeliverEventArgs delivery,
        CancellationToken cancellationToken
    )
    {
        var deliveryCount = DeliveryCountOf(delivery.BasicProperties);
        using var activity = StartProcessActivity(delivery.BasicProperties);
        var outcome = await HandleHoldingThroughOutagesAsync(
            delivery,
            deliveryCount,
            cancellationToken
        );
        await SettleAsync(channel, delivery.DeliveryTag, outcome, deliveryCount, cancellationToken);
    }

    /// <summary>The broker's count of the message's earlier deliveries: 0 on the first.</summary>
    private static int DeliveryCountOf(IReadOnlyBasicProperties properties) =>
        properties.Headers?.TryGetValue(MessageHeaders.DeliveryCount, out var value) == true
            ? Convert.ToInt32(value)
            : 0;

    /// <summary>The consumer span, a child of the publisher's (the message's trace headers).</summary>
    private Activity? StartProcessActivity(IReadOnlyBasicProperties properties) =>
        MessagingTelemetry.StartActivity(
            $"{Queue} process",
            ActivityKind.Consumer,
            Header(properties.Headers, MessageHeaders.TraceParent),
            Header(properties.Headers, MessageHeaders.TraceState),
            properties.MessageId
        );

    /// <summary>
    /// Handles the delivery and returns what to do with it. A failure while the database does not
    /// answer holds the message and handles it again every <see cref="OutageWait"/>; any other
    /// failure is a <see cref="ConsumeOutcome.Retry"/>.
    /// </summary>
    private async Task<ConsumeOutcome> HandleHoldingThroughOutagesAsync(
        BasicDeliverEventArgs delivery,
        int deliveryCount,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            try
            {
                return await HandleAsync(delivery.Body, cancellationToken);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                // Outside the handler's TenantId log scope, so the tenant goes in the line itself.
                if (!await DatabaseAnswersAsync(cancellationToken))
                {
                    logger.LogWarning(
                        error,
                        "Holding {MessageId} for tenant {TenantId}: the database does not answer",
                        delivery.BasicProperties.MessageId,
                        TenantOf(delivery.Body)
                    );
                    await Task.Delay(OutageWait, cancellationToken);
                    continue;
                }
                logger.LogWarning(
                    error,
                    "Delivery {DeliveryCount} of {MessageId} for tenant {TenantId} failed; retrying",
                    deliveryCount + 1,
                    delivery.BasicProperties.MessageId,
                    TenantOf(delivery.Body)
                );
                return ConsumeOutcome.Retry;
            }
        }
    }

    /// <summary>
    /// Acks the delivery, dead-letters it, or waits <see cref="RequeueSeconds"/> and requeues it,
    /// as its outcome says.
    /// </summary>
    private static async Task SettleAsync(
        IChannel channel,
        ulong deliveryTag,
        ConsumeOutcome outcome,
        int deliveryCount,
        CancellationToken cancellationToken
    )
    {
        switch (outcome)
        {
            case ConsumeOutcome.Ack:
                await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken);
                break;
            case ConsumeOutcome.DeadLetter:
                await channel.BasicRejectAsync(deliveryTag, requeue: false, cancellationToken);
                break;
            default:
                var seconds = RequeueSeconds[Math.Min(deliveryCount, RequeueSeconds.Length - 1)];
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
                await channel.BasicRejectAsync(deliveryTag, requeue: true, cancellationToken);
                break;
        }
    }

    private async Task<bool> DatabaseAnswersAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>
    /// The envelope's tenant, for a log line only, or null. A body off the contract is
    /// dead-lettered before anything can fail, so a failed delivery normally has one.
    /// </summary>
    private static Guid? TenantOf(ReadOnlyMemory<byte> body)
    {
        try
        {
            return MessageJson.Parse(body.Span).TenantId;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Header(IDictionary<string, object?>? headers, string name) =>
        headers?.TryGetValue(name, out var value) == true
            ? value switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string text => text,
                _ => null,
            }
            : null;
}
