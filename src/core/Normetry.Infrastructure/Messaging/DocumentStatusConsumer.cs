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
/// Consumes <c>core.document-status</c> (declared here with its dead-letter queue, as the contract
/// says), one message at a time. An error is retried after 2, 10, 30, then 60 seconds by a reject
/// with requeue, which the broker counts; with <c>x-delivery-limit: 4</c> the fifth delivery that
/// fails goes to <c>core.document-status.dead-letter</c>. A body off the contract is
/// dead-lettered at once. When the broker cancels the consumer (its queue was deleted) or closes
/// the channel, the channel and the consumer are created again. While the database does not
/// answer, a failed message is held (unacknowledged, so it spends none of its deliveries) and
/// handled again every 10 seconds; RabbitMQ's consumer timeout (30 minutes by default) bounds one
/// hold.
/// </summary>
internal sealed class DocumentStatusConsumer(
    RabbitMqConnection broker,
    DocumentStatusMessages messages,
    IConfiguration configuration,
    ILogger<DocumentStatusConsumer> logger
) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OutageWait = TimeSpan.FromSeconds(10);

    private readonly string connectionString = CoreDatabase.GetConnectionString(configuration);

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
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.UnregisteredAsync += (_, _) => Stop(stopped);
        consumer.ShutdownAsync += (_, _) => Stop(stopped);
        await channel.BasicConsumeAsync(
            Topology.StatusQueue,
            autoAck: false,
            consumer,
            stoppingToken
        );
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
        while (true)
        {
            try
            {
                outcome = await messages.HandleAsync(delivery.Body, cancellationToken);
                break;
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
                    count + 1,
                    delivery.BasicProperties.MessageId,
                    TenantOf(delivery.Body)
                );
                outcome = ConsumeOutcome.Retry;
                break;
            }
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
