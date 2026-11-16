using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Normetry.Application.Abstractions;
using Normetry.Application.Documents;
using Normetry.Infrastructure.Messaging;
using Normetry.IntegrationTests.Database;
using Xunit;

namespace Normetry.IntegrationTests.Documents;

/// <summary>
/// The AI service's status events as the consumer hands them over (without the broker), and the
/// processing timeout. Each test makes its own tenant and document.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DocumentStatusTests
{
    private readonly DatabaseFixture db;

    public DocumentStatusTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal sealed record Uploaded(
        Guid Tenant,
        Guid Establishment,
        Guid Document,
        Guid Version,
        string Owner
    );

    [Fact]
    public async Task Started_indexed_and_failed_events_move_the_version()
    {
        var a = await UploadAsync();
        var b = await UploadAsync();

        Assert.Equal(
            ConsumeOutcome.Ack,
            await HandleAsync(
                a,
                new DocumentIngestionStarted(a.Establishment, a.Document, a.Version, 1)
            )
        );
        Assert.Equal(("Processing", (string?)null), await StatusAsync(a));

        await HandleAsync(
            a,
            new DocumentIndexed(a.Establishment, a.Document, a.Version, 1, 37, 12, "pt")
        );
        var detail = await DetailAsync(a);
        Assert.Equal(1, detail.GetProperty("currentVersionNumber").GetInt32());
        var version = detail.GetProperty("versions")[0];
        Assert.Equal(
            ("Indexed", 37, 12, "pt"),
            (
                version.GetProperty("status").GetString(),
                version.GetProperty("chunkCount").GetInt32(),
                version.GetProperty("pageCount").GetInt32(),
                version.GetProperty("language").GetString()
            )
        );

        await HandleAsync(
            b,
            new DocumentIngestionFailed(
                b.Establishment,
                b.Document,
                b.Version,
                1,
                "no_text_layer",
                "The PDF has no text layer."
            )
        );
        Assert.Equal(("Failed", "no_text_layer"), await StatusAsync(b));
        var failed = (await DetailAsync(b)).GetProperty("versions")[0];
        Assert.Equal("The PDF has no text layer.", failed.GetProperty("failureReason").GetString());
    }

    [Fact]
    public async Task A_redelivered_event_is_applied_once()
    {
        var a = await UploadAsync();
        var messageId = Guid.NewGuid();
        var started = new DocumentIngestionStarted(a.Establishment, a.Document, a.Version, 1);

        await HandleAsync(a, started, messageId);
        var changedAt = await ChangedAtAsync(a);
        // The version moves on; a redelivery of the first event must not move it back.
        await SetStatusAsync(a, "Uploaded");
        Assert.Equal(ConsumeOutcome.Ack, await HandleAsync(a, started, messageId));

        Assert.Equal(("Uploaded", (string?)null), await StatusAsync(a));
        await using var session = await Session.BeginAsync(
            db.ConnectionString("app_core"),
            a.Tenant
        );
        Assert.Equal(
            1L,
            await session.ScalarAsync<long>(
                "SELECT count(*) FROM core.inbox_messages WHERE message_id = $1",
                messageId
            )
        );
        Assert.True(changedAt <= await ChangedAtAsync(a));
    }

    [Fact]
    public async Task One_message_id_under_two_tenants_is_applied_in_each()
    {
        // The inbox key is (tenant, message id): tenant B's message never suppresses tenant A's.
        var a = await UploadAsync();
        var b = await UploadAsync();
        var messageId = Guid.NewGuid();

        await HandleAsync(
            a,
            new DocumentIngestionStarted(a.Establishment, a.Document, a.Version, 1),
            messageId
        );
        await HandleAsync(
            b,
            new DocumentIngestionStarted(b.Establishment, b.Document, b.Version, 1),
            messageId
        );

        Assert.Equal(("Processing", (string?)null), await StatusAsync(a));
        Assert.Equal(("Processing", (string?)null), await StatusAsync(b));
    }

    [Fact]
    public async Task An_event_reaches_only_the_tenant_its_envelope_names()
    {
        // Tenant B's message naming tenant A's ids: B's transaction cannot see A's rows.
        var a = await UploadAsync();
        var b = await UploadAsync();

        Assert.Equal(
            ConsumeOutcome.Ack,
            await HandleAsync(
                b,
                new DocumentIndexed(a.Establishment, a.Document, a.Version, 1, 3, null, "en")
            )
        );
        // Ids that do not belong together are dropped as well.
        await HandleAsync(
            a,
            new DocumentIndexed(b.Establishment, a.Document, a.Version, 1, 3, null, "en")
        );
        await HandleAsync(
            a,
            new DocumentIndexed(a.Establishment, a.Document, a.Version, 2, 3, null, "en")
        );

        Assert.Equal(("Uploaded", (string?)null), await StatusAsync(a));
        Assert.Equal(("Uploaded", (string?)null), await StatusAsync(b));
    }

    [Fact]
    public async Task A_body_off_the_contract_or_meant_for_the_AI_service_is_dead_lettered()
    {
        var a = await UploadAsync();
        var messages = db.Api.Services.GetRequiredService<DocumentStatusMessages>();

        Assert.Equal(ConsumeOutcome.DeadLetter, await messages.HandleAsync("{}"u8.ToArray(), Ct));
        Assert.Equal(
            ConsumeOutcome.DeadLetter,
            await HandleAsync(a, new DocumentDeleted(a.Establishment, a.Document))
        );
    }

    [Fact]
    public async Task A_version_processing_past_the_timeout_fails_and_a_late_index_still_counts()
    {
        var stuck = await UploadAsync();
        var waiting = await UploadAsync();
        var recent = await UploadAsync();
        await HandleAsync(
            stuck,
            new DocumentIngestionStarted(stuck.Establishment, stuck.Document, stuck.Version, 1)
        );
        await HandleAsync(
            recent,
            new DocumentIngestionStarted(recent.Establishment, recent.Document, recent.Version, 1)
        );
        await SetStatusAsync(stuck, "Processing", TimeSpan.FromMinutes(16));
        // Uploaded never times out: the version waits for the AI service.
        await SetStatusAsync(waiting, "Uploaded", TimeSpan.FromHours(2));
        var sweeper = ActivatorUtilities.CreateInstance<ProcessingTimeoutSweeper>(db.Api.Services);

        // Other tenants' sweeps never reach the stuck version.
        Assert.Empty(await sweeper.SweepAsync(waiting.Tenant, Ct));
        Assert.Empty(await sweeper.SweepAsync(recent.Tenant, Ct));
        Assert.Equal(("Processing", (string?)null), await StatusAsync(stuck));
        var changes = await sweeper.SweepAsync(stuck.Tenant, Ct);

        var change = Assert.Single(changes);
        Assert.Equal((stuck.Version, "timed_out"), (change.VersionId, change.FailureCode));
        Assert.Equal(("Failed", "timed_out"), await StatusAsync(stuck));
        Assert.Equal(("Uploaded", (string?)null), await StatusAsync(waiting));
        Assert.Equal(("Processing", (string?)null), await StatusAsync(recent));

        await HandleAsync(
            stuck,
            new DocumentIndexed(
                stuck.Establishment,
                stuck.Document,
                stuck.Version,
                1,
                5,
                null,
                "en"
            )
        );
        Assert.Equal(("Indexed", (string?)null), await StatusAsync(stuck));
    }

    /// <summary>A new tenant with a PDF uploaded through the API.</summary>
    internal static async Task<Uploaded> UploadAsync(DatabaseFixture db)
    {
        var owner = Guid.NewGuid().ToString();
        var tenant = await TestTenants.CreateAsync(db, null, owner);
        var establishment = await TestTenants.CreateEstablishmentAsync(db, tenant);
        using var client = db.Api.CreateClient(ApiFactory.CreateToken(tenant.ToString(), owner));
        var (document, version) = await DocumentEndpointTests.UploadPdfAsync(client, establishment);
        return new Uploaded(tenant, establishment, document, version, owner);
    }

    /// <summary>The body the AI service would publish for <paramref name="payload"/>, handled as the consumer does.</summary>
    internal static Task<ConsumeOutcome> HandleAsync<TMessage>(
        ApiFactory api,
        Guid tenant,
        TMessage payload,
        Guid? messageId = null
    )
        where TMessage : IIntegrationMessage =>
        api
            .Services.GetRequiredService<DocumentStatusMessages>()
            .HandleAsync(Body(tenant, payload, messageId), Ct);

    internal static byte[] Body<TMessage>(Guid tenant, TMessage payload, Guid? messageId = null)
        where TMessage : IIntegrationMessage =>
        Encoding.UTF8.GetBytes(
            MessageJson.Serialize(
                messageId ?? Guid.NewGuid(),
                tenant,
                DateTimeOffset.UtcNow,
                payload
            )
        );

    private Task<Uploaded> UploadAsync() => UploadAsync(db);

    private Task<ConsumeOutcome> HandleAsync<TMessage>(
        Uploaded target,
        TMessage payload,
        Guid? messageId = null
    )
        where TMessage : IIntegrationMessage =>
        HandleAsync(db.Api, target.Tenant, payload, messageId);

    private async Task<JsonElement> DetailAsync(Uploaded target)
    {
        using var client = db.Api.CreateClient(
            ApiFactory.CreateToken(target.Tenant.ToString(), target.Owner)
        );
        return await client.GetFromJsonAsync<JsonElement>($"/documents/{target.Document}", Ct);
    }

    private async Task<(string Status, string? FailureCode)> StatusAsync(Uploaded target)
    {
        var version = (await DetailAsync(target)).GetProperty("versions")[0];
        var code = version.GetProperty("failureCode");
        return (
            version.GetProperty("status").GetString()!,
            code.ValueKind == JsonValueKind.Null ? null : code.GetString()
        );
    }

    private async Task<DateTime> ChangedAtAsync(Uploaded target)
    {
        await using var session = await Session.BeginAsync(
            db.ConnectionString("app_core"),
            target.Tenant
        );
        return await session.ScalarAsync<DateTime>(
            "SELECT status_changed_at FROM core.document_versions WHERE id = $1",
            target.Version
        );
    }

    private async Task SetStatusAsync(Uploaded target, string status, TimeSpan? age = null)
    {
        await using var session = await Session.BeginAsync(
            db.ConnectionString("app_core"),
            target.Tenant
        );
        await session.ExecuteAsync(
            "UPDATE core.document_versions SET status = $2, status_changed_at = now() - $3 WHERE id = $1",
            target.Version,
            status,
            age ?? TimeSpan.Zero
        );
        await session.CommitAsync();
    }
}
