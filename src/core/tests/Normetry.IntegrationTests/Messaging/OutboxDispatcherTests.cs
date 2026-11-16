using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Normetry.Application.Abstractions;
using Normetry.Application.Documents;
using Normetry.Domain.Establishments;
using Normetry.Infrastructure.Messaging;
using Normetry.Infrastructure.Persistence;
using Normetry.IntegrationTests.Database;
using RabbitMQ.Client;
using Xunit;

namespace Normetry.IntegrationTests.Messaging;

/// <summary>
/// The outbox against a real broker: a message written with a state change is published by a
/// dispatcher that starts afterwards, and only a confirmed, routed publish marks it sent. Each
/// test makes its own tenant and calls the dispatcher for it alone.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OutboxDispatcherTests : IClassFixture<RabbitMqFixture>
{
    private readonly DatabaseFixture db;
    private readonly RabbitMqFixture broker;

    public OutboxDispatcherTests(DatabaseFixture db, RabbitMqFixture broker)
    {
        db.SkipWithoutDocker();
        broker.SkipWithoutDocker();
        this.db = db;
        this.broker = broker;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_message_written_with_a_state_change_is_published_after_a_dispatcher_restart()
    {
        var tenant = await TestTenants.CreateAsync(db);
        await using var api = NewApi();
        await using var connection = await broker.ConnectAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        var queue = await BindTestQueueAsync(channel, DocumentDeleted.RoutingKey);

        // A request's span, which the published message continues.
        using var listener = ListenTo("Normetry.Tests");
        using var source = new ActivitySource("Normetry.Tests");
        Guid establishment;
        ActivityTraceId trace;
        using (var activity = source.StartActivity("request"))
        {
            trace = activity!.TraceId;
            establishment = await WriteWithStateChangeAsync(api, tenant, id => new DocumentDeleted(
                id,
                Guid.CreateVersion7()
            ));
        }

        // Nothing has run yet: the row waits in the outbox.
        Assert.Equal([false], await SentAsync(tenant));

        // A dispatcher that starts afterwards (a restart: nothing in memory knows the message).
        var dispatcher = NewDispatcher(api);
        Assert.Equal(1, await dispatcher.DispatchAsync(tenant, Ct));

        var delivered = await channel.BasicGetAsync(queue, autoAck: true, Ct);
        Assert.NotNull(delivered);
        var body = Encoding.UTF8.GetString(delivered.Body.Span);
        var message = MessageJson.Parse(delivered.Body.Span);
        Assert.Equal(tenant, message.TenantId);
        Assert.Equal(establishment, ((DocumentDeleted)message.Payload).EstablishmentId);
        Assert.Equal("application/json", delivered.BasicProperties.ContentType);
        Assert.Equal(DeliveryModes.Persistent, delivered.BasicProperties.DeliveryMode);
        Assert.Equal(message.MessageId.ToString(), delivered.BasicProperties.MessageId);
        Assert.Equal("DocumentDeleted", delivered.BasicProperties.Type);
        // The same trace: the request's span, or the publish span under it when telemetry listens.
        var traceParent = Encoding.UTF8.GetString(
            (byte[])delivered.BasicProperties.Headers!["traceparent"]!
        );
        Assert.Equal(trace, ActivityContext.Parse(traceParent, null).TraceId);
        Assert.Equal(await StoredBodyAsync(tenant), body);
        Assert.Equal([true], await SentAsync(tenant));

        // Sent rows are not published again.
        Assert.Equal(0, await dispatcher.DispatchAsync(tenant, Ct));
    }

    [Fact]
    public async Task An_unroutable_message_stays_pending_until_a_queue_is_bound()
    {
        // No queue is bound to document.uploaded.v1 until the AI service declares its own: the
        // broker returns the mandatory message, which is a failed publish, never a sent one.
        var tenant = await TestTenants.CreateAsync(db);
        await using var api = NewApi();
        await WriteWithStateChangeAsync(api, tenant, id => new DocumentUploaded(
            id,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1,
            "tenants/x/file.pdf",
            "application/pdf",
            1,
            new string('0', 64)
        ));
        var dispatcher = NewDispatcher(api);

        Assert.Equal(0, await dispatcher.DispatchAsync(tenant, Ct));
        Assert.Equal([false], await SentAsync(tenant));

        await using var connection = await broker.ConnectAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        var queue = await BindTestQueueAsync(channel, DocumentUploaded.RoutingKey);

        Assert.Equal(1, await dispatcher.DispatchAsync(tenant, Ct));
        Assert.NotNull(await channel.BasicGetAsync(queue, autoAck: true, Ct));
        Assert.Equal([true], await SentAsync(tenant));
    }

    private ApiFactory NewApi() =>
        new()
        {
            CoreConnectionString = db.ConnectionString("app_core"),
            RabbitMqConnectionString = broker.ConnectionString,
        };

    private static OutboxDispatcher NewDispatcher(ApiFactory api) =>
        ActivatorUtilities.CreateInstance<OutboxDispatcher>(api.Services);

    /// <summary>
    /// Creates an establishment and enqueues a message about it in one transaction, the way a
    /// handler does under <c>TransactionBehavior</c>. Returns the establishment id.
    /// </summary>
    private static async Task<Guid> WriteWithStateChangeAsync<TMessage>(
        ApiFactory api,
        Guid tenant,
        Func<Guid, TMessage> message
    )
        where TMessage : IIntegrationMessage
    {
        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundTenant>().Set(tenant);
        var context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync(Ct);
        var establishment = Establishment.Create(
            tenant,
            "Kitchen",
            EstablishmentType.Restaurant,
            Jurisdiction.US
        );
        context.Establishments.Add(establishment);
        await scope
            .ServiceProvider.GetRequiredService<IOutbox>()
            .EnqueueAsync(message(establishment.Id), Ct);
        await context.SaveChangesAsync(Ct);
        await transaction.CommitAsync(Ct);
        return establishment.Id;
    }

    /// <summary>An exclusive queue of the test, bound like the AI service's own.</summary>
    private static async Task<string> BindTestQueueAsync(IChannel channel, string routingKey)
    {
        await channel.ExchangeDeclareAsync(
            "normetry.events",
            ExchangeType.Topic,
            durable: true,
            cancellationToken: Ct
        );
        var queue = await channel.QueueDeclareAsync(cancellationToken: Ct);
        await channel.QueueBindAsync(
            queue.QueueName,
            "normetry.events",
            routingKey,
            cancellationToken: Ct
        );
        return queue.QueueName;
    }

    private async Task<List<bool>> SentAsync(Guid tenant)
    {
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant);
        return await session.ColumnAsync<bool>(
            "SELECT sent_at IS NOT NULL FROM core.outbox_messages"
        );
    }

    private async Task<string> StoredBodyAsync(Guid tenant)
    {
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant);
        return await session.ScalarAsync<string>("SELECT body::text FROM core.outbox_messages");
    }

    private static ActivityListener ListenTo(string sourceName)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
