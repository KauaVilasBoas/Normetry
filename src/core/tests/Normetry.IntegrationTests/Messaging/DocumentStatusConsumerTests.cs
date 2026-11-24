using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Normetry.Application.Documents;
using Normetry.Application.Inspections;
using Normetry.Infrastructure.Messaging;
using Normetry.IntegrationTests.Database;
using Normetry.IntegrationTests.Documents;
using Normetry.IntegrationTests.Inspections;
using RabbitMQ.Client;
using Xunit;

namespace Normetry.IntegrationTests.Messaging;

/// <summary>
/// The status consumer against a real broker: it declares core.document-status, applies what the
/// AI service publishes there and dead-letters a body off the contract at once.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DocumentStatusConsumerTests : IClassFixture<RabbitMqFixture>
{
    private readonly DatabaseFixture db;
    private readonly RabbitMqFixture broker;

    public DocumentStatusConsumerTests(DatabaseFixture db, RabbitMqFixture broker)
    {
        db.SkipWithoutDocker();
        broker.SkipWithoutDocker();
        this.db = db;
        this.broker = broker;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Status_events_published_by_the_AI_service_are_applied_and_bad_ones_dead_lettered()
    {
        var uploaded = await DocumentStatusTests.UploadAsync(db);
        await using var api = new ApiFactory
        {
            CoreConnectionString = db.ConnectionString("app_core"),
            DispatcherConnectionString = db.ConnectionString("core_dispatcher"),
            RabbitMqConnectionString = broker.ConnectionString,
        };
        var consumer = ActivatorUtilities.CreateInstance<DocumentStatusConsumer>(api.Services);
        await using var connection = await broker.ConnectAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        // Declared by the consumer too; the arguments must match, or the broker refuses one of them.
        await Topology.DeclareDocumentStatusQueueAsync(channel, Ct);
        await consumer.StartAsync(Ct);
        try
        {
            await PublishAsync(
                channel,
                DocumentIndexed.RoutingKey,
                DocumentStatusTests.Body(
                    uploaded.Tenant,
                    new DocumentIndexed(
                        uploaded.Establishment,
                        uploaded.Document,
                        uploaded.Version,
                        1,
                        8,
                        2,
                        "en"
                    )
                )
            );
            await PublishAsync(channel, DocumentIndexed.RoutingKey, "not json"u8.ToArray());

            Assert.True(
                await EventuallyAsync(async () => await StatusAsync(uploaded) == "Indexed")
            );
            Assert.True(
                await EventuallyAsync(async () =>
                    await channel.BasicGetAsync(Topology.DocumentStatusDeadLetterQueue, true, Ct)
                        is { } dead
                    && Encoding.UTF8.GetString(dead.Body.Span) == "not json"
                )
            );
        }
        finally
        {
            await consumer.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task The_consumer_comes_back_when_the_broker_cancels_it()
    {
        var uploaded = await DocumentStatusTests.UploadAsync(db);
        await using var api = new ApiFactory
        {
            CoreConnectionString = db.ConnectionString("app_core"),
            DispatcherConnectionString = db.ConnectionString("core_dispatcher"),
            RabbitMqConnectionString = broker.ConnectionString,
        };
        var consumer = ActivatorUtilities.CreateInstance<DocumentStatusConsumer>(api.Services);
        await using var connection = await broker.ConnectAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        await Topology.DeclareDocumentStatusQueueAsync(channel, Ct);
        await consumer.StartAsync(Ct);
        try
        {
            Assert.True(await EventuallyAsync(() => ConsumedAsync(channel)));
            // Deleting the queue makes the broker cancel the consumer (basic.cancel).
            await channel.QueueDeleteAsync(Topology.DocumentStatusQueue, cancellationToken: Ct);

            Assert.True(await EventuallyAsync(() => ConsumedAsync(channel), attempts: 300));
            await PublishAsync(
                channel,
                DocumentIndexed.RoutingKey,
                DocumentStatusTests.Body(
                    uploaded.Tenant,
                    new DocumentIndexed(
                        uploaded.Establishment,
                        uploaded.Document,
                        uploaded.Version,
                        1,
                        8,
                        2,
                        "en"
                    )
                )
            );
            Assert.True(
                await EventuallyAsync(async () => await StatusAsync(uploaded) == "Indexed")
            );
        }
        finally
        {
            await consumer.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task The_inspection_status_queue_takes_the_four_inspection_events_and_dead_letters_them_apart()
    {
        await using var connection = await broker.ConnectAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        // Twice: a declaration with other arguments than the first closes the channel.
        await Topology.DeclareInspectionStatusQueueAsync(channel, Ct);
        await Topology.DeclareInspectionStatusQueueAsync(channel, Ct);
        foreach (var key in Topology.InspectionStatusRoutingKeys)
        {
            await PublishAsync(channel, key, "{}"u8.ToArray());
        }

        var keys = new List<string>();
        ulong last = 0;
        Assert.True(
            await EventuallyAsync(async () =>
            {
                while (
                    await channel.BasicGetAsync(Topology.InspectionStatusQueue, false, Ct)
                        is { } got
                )
                {
                    keys.Add(got.RoutingKey);
                    last = got.DeliveryTag;
                }
                return keys.Count == Topology.InspectionStatusRoutingKeys.Length;
            })
        );
        Assert.Equal(Topology.InspectionStatusRoutingKeys.Order(), keys.Order());

        // Rejected without requeue: it goes to the queue's own dead-letter queue.
        await channel.BasicRejectAsync(last, false, Ct);
        Assert.True(
            await EventuallyAsync(async () =>
                await channel.BasicGetAsync(Topology.InspectionStatusDeadLetterQueue, true, Ct)
                    is not null
            )
        );
    }

    [Fact]
    public async Task Inspection_events_published_by_the_AI_service_are_applied_and_bad_ones_dead_lettered()
    {
        var target = await InspectionEndpointTests.NewTargetAsync(db);
        using var client = InspectionEndpointTests.Client(db.Api, target);
        var inspection = await InspectionEndpointTests.StartAsync(client, target);
        await using var api = new ApiFactory
        {
            CoreConnectionString = db.ConnectionString("app_core"),
            DispatcherConnectionString = db.ConnectionString("core_dispatcher"),
            RabbitMqConnectionString = broker.ConnectionString,
        };
        var consumer = ActivatorUtilities.CreateInstance<InspectionStatusConsumer>(api.Services);
        await using var connection = await broker.ConnectAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        await Topology.DeclareInspectionStatusQueueAsync(channel, Ct);
        await consumer.StartAsync(Ct);
        try
        {
            await PublishAsync(
                channel,
                InspectionStarted.RoutingKey,
                DocumentStatusTests.Body(
                    target.Tenant,
                    new InspectionStarted(target.Establishment, inspection, 4, [])
                )
            );
            await PublishAsync(channel, InspectionFailed.RoutingKey, "not json"u8.ToArray());

            Assert.True(
                await EventuallyAsync(async () =>
                    (
                        await client.GetFromJsonAsync<JsonElement>(
                            $"{InspectionEndpointTests.StartPath(target)}/{inspection}",
                            Ct
                        )
                    )
                        .GetProperty("status")
                        .GetString() == "Running"
                )
            );
            Assert.True(
                await EventuallyAsync(async () =>
                    await channel.BasicGetAsync(Topology.InspectionStatusDeadLetterQueue, true, Ct)
                        is { } dead
                    && Encoding.UTF8.GetString(dead.Body.Span) == "not json"
                )
            );
        }
        finally
        {
            await consumer.StopAsync(Ct);
        }
    }

    /// <summary>Whether a consumer is on the status queue (declared again if it was deleted).</summary>
    private static async Task<bool> ConsumedAsync(IChannel channel)
    {
        await Topology.DeclareDocumentStatusQueueAsync(channel, Ct);
        var queue = await channel.QueueDeclarePassiveAsync(Topology.DocumentStatusQueue, Ct);
        return queue.ConsumerCount == 1;
    }

    private static Task PublishAsync(IChannel channel, string routingKey, byte[] body) =>
        channel
            .BasicPublishAsync(
                Topology.Events,
                routingKey,
                mandatory: false,
                new BasicProperties
                {
                    ContentType = "application/json",
                    DeliveryMode = DeliveryModes.Persistent,
                },
                body,
                Ct
            )
            .AsTask();

    private async Task<string> StatusAsync(DocumentStatusTests.Uploaded uploaded)
    {
        await using var session = await Session.BeginAsync(
            db.ConnectionString("app_core"),
            uploaded.Tenant
        );
        return await session.ScalarAsync<string>(
            "SELECT status FROM core.document_versions WHERE id = $1",
            uploaded.Version
        );
    }

    private static async Task<bool> EventuallyAsync(Func<Task<bool>> condition, int attempts = 100)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (await condition())
            {
                return true;
            }
            await Task.Delay(100, Ct);
        }
        return false;
    }
}
