using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Normetry.Application.Documents;
using Normetry.Application.Inspections;
using Normetry.Domain.Establishments;
using Normetry.Infrastructure.Messaging;
using Normetry.IntegrationTests.Database;
using Xunit;
using static Normetry.IntegrationTests.Inspections.InspectionEndpointTests;

namespace Normetry.IntegrationTests.Inspections;

/// <summary>
/// The AI service's inspection events as the consumer hands them over (without the broker), and
/// the running timeout. Each test makes its own tenants and inspections.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class InspectionStatusTests
{
    private readonly DatabaseFixture db;

    public InspectionStatusTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Started(Target Target, Guid Inspection);

    private async Task<Started> StartAsync()
    {
        var target = await NewTargetAsync(db);
        using var client = Client(db.Api, target);
        return new Started(target, await InspectionEndpointTests.StartAsync(client, target));
    }

    private Task<ConsumeOutcome> HandleAsync<TMessage>(
        Started started,
        TMessage payload,
        Guid? messageId = null
    )
        where TMessage : Application.Abstractions.IIntegrationMessage =>
        InspectionEndpointTests.HandleAsync(db.Api, started.Target.Tenant, payload, messageId);

    private async Task<JsonElement> GetAsync(Started started)
    {
        using var client = Client(db.Api, started.Target);
        return await client.GetFromJsonAsync<JsonElement>(
            $"{StartPath(started.Target)}/{started.Inspection}",
            Ct
        );
    }

    private async Task<(string Status, int Evaluated, string? Failure)> StateAsync(Started started)
    {
        var inspection = await GetAsync(started);
        var failure = inspection.GetProperty("failureReasonCode");
        return (
            inspection.GetProperty("status").GetString()!,
            inspection.GetProperty("itemsEvaluated").GetInt32(),
            failure.ValueKind == JsonValueKind.Null ? null : failure.GetString()
        );
    }

    [Fact]
    public async Task Events_move_the_inspection_and_progress_never_goes_back()
    {
        var run = await StartAsync();
        var (establishment, id) = (run.Target.Establishment, run.Inspection);

        Assert.Equal(
            ConsumeOutcome.Ack,
            await HandleAsync(
                run,
                new InspectionStarted(establishment, id, 5, [new("temperature_control", 5, 0)])
            )
        );
        Assert.Equal(("Running", 0, (string?)null), await StateAsync(run));

        await HandleAsync(
            run,
            new InspectionProgressed(establishment, id, 3, 5, [new("temperature_control", 5, 3)])
        );
        // A retried, older progress arrives late: it changes nothing.
        Assert.Equal(
            ConsumeOutcome.Ack,
            await HandleAsync(
                run,
                new InspectionProgressed(
                    establishment,
                    id,
                    2,
                    5,
                    [new("temperature_control", 5, 2)]
                )
            )
        );
        var running = await GetAsync(run);
        Assert.Equal(
            (3, 3),
            (
                running.GetProperty("itemsEvaluated").GetInt32(),
                running.GetProperty("themes")[0].GetProperty("itemsEvaluated").GetInt32()
            )
        );

        await HandleAsync(run, Completed(establishment, id));
        var completed = await GetAsync(run);
        Assert.Equal("Completed", completed.GetProperty("status").GetString());
        Assert.Equal(3, completed.GetProperty("findings").GetArrayLength());
    }

    [Fact]
    public async Task A_redelivered_event_is_applied_once()
    {
        var run = await StartAsync();
        var messageId = Guid.NewGuid();
        var failed = new InspectionFailed(
            run.Target.Establishment,
            run.Inspection,
            "error",
            "Boom."
        );

        await HandleAsync(run, failed, messageId);
        // The inspection moves on; a redelivery of the failure must not fail it again.
        await using (
            var session = await Session.BeginAsync(
                db.ConnectionString("app_core"),
                run.Target.Tenant
            )
        )
        {
            await session.ExecuteAsync(
                "UPDATE core.inspections SET status = 'Running', started_at = now(), failure_reason_code = NULL WHERE id = $1",
                run.Inspection
            );
            await session.CommitAsync();
        }
        Assert.Equal(ConsumeOutcome.Ack, await HandleAsync(run, failed, messageId));

        Assert.Equal(("Running", 0, (string?)null), await StateAsync(run));
        await using var check = await Session.BeginAsync(
            db.ConnectionString("app_core"),
            run.Target.Tenant
        );
        Assert.Equal(
            1L,
            await check.ScalarAsync<long>(
                "SELECT count(*) FROM core.inbox_messages WHERE message_id = $1",
                messageId
            )
        );
    }

    [Fact]
    public async Task An_event_with_ids_of_another_establishment_tenant_or_none_is_dropped()
    {
        var a = await StartAsync();
        var b = await StartAsync();
        var otherEstablishment = await TestTenants.CreateEstablishmentAsync(db, a.Target.Tenant);

        // The payload names another establishment of the same tenant.
        Assert.Equal(
            ConsumeOutcome.Ack,
            await HandleAsync(
                a,
                new InspectionFailed(otherEstablishment, a.Inspection, "error", "Boom.")
            )
        );
        // Tenant B's envelope naming tenant A's ids: B's transaction cannot see A's rows.
        Assert.Equal(
            ConsumeOutcome.Ack,
            await HandleAsync(
                b,
                new InspectionFailed(a.Target.Establishment, a.Inspection, "error", "Boom.")
            )
        );
        // An inspection the core never had.
        Assert.Equal(
            ConsumeOutcome.Ack,
            await HandleAsync(a, Completed(a.Target.Establishment, Guid.NewGuid()))
        );

        Assert.Equal(("Requested", 0, (string?)null), await StateAsync(a));
        Assert.Equal(("Requested", 0, (string?)null), await StateAsync(b));
    }

    [Fact]
    public async Task A_completion_citing_a_document_outside_the_establishment_fails_the_inspection()
    {
        var (sibling, unknown, crossTenant, valid) = (
            await StartAsync(),
            await StartAsync(),
            await StartAsync(),
            await StartAsync()
        );
        var siblingDocument = await AddDocumentAsync(
            db,
            sibling.Target with
            {
                Establishment = await TestTenants.CreateEstablishmentAsync(
                    db,
                    sibling.Target.Tenant
                ),
            },
            "Indexed"
        );
        // A deleted document is still the establishment's: the run may have read it before.
        var deleted = await AddDocumentAsync(db, valid.Target, "Indexed", deleted: true);

        foreach (
            var (run, document) in new[]
            {
                (sibling, siblingDocument),
                (unknown, Guid.NewGuid()),
                // Another tenant's document: RLS hides it, so it is not the establishment's either.
                (crossTenant, valid.Target.Establishment),
                (valid, deleted),
            }
        )
        {
            Assert.Equal(
                ConsumeOutcome.Ack,
                await HandleAsync(
                    run,
                    Citing(Completed(run.Target.Establishment, run.Inspection), document)
                )
            );
        }

        foreach (var refused in new[] { sibling, unknown, crossTenant })
        {
            Assert.Equal(("Failed", 0, "error"), await StateAsync(refused));
            Assert.Equal(0, (await GetAsync(refused)).GetProperty("findings").GetArrayLength());
        }
        Assert.Equal(("Completed", 0, (string?)null), await StateAsync(valid));
        Assert.Equal(3, (await GetAsync(valid)).GetProperty("findings").GetArrayLength());
    }

    /// <summary>The completion with its last finding's evidence citing <paramref name="document"/> instead.</summary>
    private static InspectionCompleted Citing(InspectionCompleted completion, Guid document)
    {
        var last = completion.Findings[^1];
        return completion with
        {
            Findings =
            [
                .. completion.Findings.SkipLast(1),
                last with
                {
                    EvidenceCitations = [last.EvidenceCitations[0] with { DocumentId = document }],
                },
            ],
        };
    }

    [Fact]
    public async Task A_body_off_the_contract_or_meant_for_another_queue_is_dead_lettered()
    {
        var run = await StartAsync();
        var messages = db.Api.Services.GetRequiredService<InspectionStatusMessages>();

        Assert.Equal(ConsumeOutcome.DeadLetter, await messages.HandleAsync("{}"u8.ToArray(), Ct));
        Assert.Equal(
            ConsumeOutcome.DeadLetter,
            await HandleAsync(
                run,
                new InspectionRequested(
                    run.Target.Establishment,
                    run.Inspection,
                    Jurisdiction.US,
                    EstablishmentType.Restaurant,
                    1000,
                    run.Target.Owner
                )
            )
        );
        Assert.Equal(
            ConsumeOutcome.DeadLetter,
            await HandleAsync(
                run,
                new DocumentIngestionStarted(
                    run.Target.Establishment,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    1
                )
            )
        );
        Assert.Equal(("Requested", 0, (string?)null), await StateAsync(run));
    }

    [Fact]
    public async Task A_run_past_the_timeout_fails_and_a_late_completion_still_applies()
    {
        var stuck = await StartAsync();
        var waiting = await StartAsync();
        var recent = await StartAsync();
        foreach (var run in new[] { stuck, recent })
        {
            await HandleAsync(
                run,
                new InspectionStarted(run.Target.Establishment, run.Inspection, 3, [])
            );
        }
        await AgeAsync(stuck, "started_at", TimeSpan.FromMinutes(31));
        // Requested never times out: the inspection waits for the AI service.
        await AgeAsync(waiting, "requested_at", TimeSpan.FromHours(2));
        var sweeper = ActivatorUtilities.CreateInstance<ProcessingTimeoutSweeper>(db.Api.Services);

        // Other tenants' sweeps never reach the stuck inspection.
        Assert.Empty(await sweeper.SweepInspectionsAsync(waiting.Target.Tenant, Ct));
        Assert.Empty(await sweeper.SweepInspectionsAsync(recent.Target.Tenant, Ct));
        Assert.Equal(("Running", 0, (string?)null), await StateAsync(stuck));
        var change = Assert.Single(await sweeper.SweepInspectionsAsync(stuck.Target.Tenant, Ct));

        Assert.Equal(
            (stuck.Inspection, "timed_out"),
            (change.InspectionId, change.FailureReasonCode)
        );
        Assert.Equal(("Failed", 0, "timed_out"), await StateAsync(stuck));
        Assert.Equal(("Requested", 0, (string?)null), await StateAsync(waiting));
        Assert.Equal(("Running", 0, (string?)null), await StateAsync(recent));

        await HandleAsync(stuck, Completed(stuck.Target.Establishment, stuck.Inspection));
        Assert.Equal(("Completed", 0, (string?)null), await StateAsync(stuck));
    }

    private async Task AgeAsync(Started run, string column, TimeSpan age)
    {
        await using var session = await Session.BeginAsync(
            db.ConnectionString("app_core"),
            run.Target.Tenant
        );
        await session.ExecuteAsync(
            $"UPDATE core.inspections SET {column} = now() - $2 WHERE id = $1",
            run.Inspection,
            age
        );
        await session.CommitAsync();
    }
}
