using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Normetry.Infrastructure.Messaging;

/// <summary>
/// Consumes <c>core.document-status</c> (declared here with its dead-letter queue, as the contract
/// says), one message at a time. An error is retried after 2, 10, 30, then 60 seconds by a reject
/// with requeue, which the broker counts; with <c>x-delivery-limit: 4</c> the fifth delivery that
/// fails goes to <c>core.document-status.dead-letter</c>. A body off the contract is
/// dead-lettered at once. The client recovers the connection, the queue and the consumer after a
/// network failure.
/// </summary>
internal sealed class DocumentStatusConsumer(
    RabbitMqConnection broker,
    DocumentStatusMessages messages,
    ILogger<DocumentStatusConsumer> logger
) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

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
                logger.LogWarning(error, "Cannot consume {Queue}; retrying", Topology.StatusQueue);
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
        await Topology.DeclareStatusQueueAsync(channel, stoppingToken);
        await channel.BasicQosAsync(0, 1, false, stoppingToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) =>
            OnDeliveryAsync(channel, delivery, stoppingToken);
        await channel.BasicConsumeAsync(
            Topology.StatusQueue,
            autoAck: false,
            consumer,
            stoppingToken
        );
        await Task.Delay(Timeout.Infinite, stoppingToken)
            .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private async Task OnDeliveryAsync(
        IChannel channel,
        BasicDeliverEventArgs delivery,
        CancellationToken cancellationToken
    )
    {
        var headers = delivery.BasicProperties.Headers;
        var count =
            headers?.TryGetValue("x-delivery-count", out var value) == true
                ? Convert.ToInt32(value)
                : 0;
        ActivityContext.TryParse(
            Header(headers, "traceparent"),
            Header(headers, "tracestate"),
            out var parent
        );
        using var activity = MessagingTelemetry.Source.StartActivity(
            $"{Topology.StatusQueue} process",
            ActivityKind.Consumer,
            parent
        );
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.message.id", delivery.BasicProperties.MessageId);

        ConsumeOutcome outcome;
        try
        {
            outcome = await messages.HandleAsync(delivery.Body, cancellationToken);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            // Outside the handler's TenantId log scope, so the tenant goes in the line itself.
            logger.LogWarning(
                error,
                "Delivery {DeliveryCount} of {MessageId} for tenant {TenantId} failed; retrying",
                count + 1,
                delivery.BasicProperties.MessageId,
                TenantOf(delivery.Body)
            );
            outcome = ConsumeOutcome.Retry;
        }

        switch (outcome)
        {
            case ConsumeOutcome.Ack:
                await channel.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
                break;
            case ConsumeOutcome.DeadLetter:
                await channel.BasicRejectAsync(delivery.DeliveryTag, false, cancellationToken);
                break;
            default:
                var seconds = RequeueSeconds[Math.Min(count, RequeueSeconds.Length - 1)];
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
                await channel.BasicRejectAsync(delivery.DeliveryTag, true, cancellationToken);
                break;
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
