using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Normetry.Application.Documents;
using Normetry.Infrastructure.Messaging;
using Normetry.IntegrationTests.Database;
using Normetry.IntegrationTests.Documents;
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
        await Topology.DeclareStatusQueueAsync(channel, Ct);
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
                    await channel.BasicGetAsync(Topology.StatusDeadLetterQueue, true, Ct)
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

    private static async Task<bool> EventuallyAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
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
