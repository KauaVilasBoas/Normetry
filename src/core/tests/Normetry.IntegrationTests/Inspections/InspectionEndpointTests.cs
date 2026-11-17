using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Normetry.Application.Abstractions;
using Normetry.Application.Inspections;
using Normetry.Domain.Documents;
using Normetry.Domain.Inspections;
using Normetry.Infrastructure.Messaging;
using Normetry.IntegrationTests.Database;
using Normetry.IntegrationTests.Documents;
using Xunit;

namespace Normetry.IntegrationTests.Inspections;

/// <summary>
/// Starting, listing and reading inspections through the API. Each test makes its own tenants;
/// the AI service's events go through <see cref="InspectionStatusMessages"/>, without the broker.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class InspectionEndpointTests
{
    private readonly DatabaseFixture db;

    public InspectionEndpointTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A tenant, its Owner and one establishment (US).</summary>
    internal sealed record Target(Guid Tenant, Guid Establishment, string Owner);

    [Fact]
    public async Task A_start_is_accepted_announced_counted_and_listed_as_requested()
    {
        var target = await NewTargetAsync(db);
        using var client = Client(db.Api, target);

        using var response = await client.PostAsync(StartPath(target), null, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("id")
            .GetGuid();
        Assert.Equal(
            $"/establishments/{target.Establishment}/inspections/{id}",
            response.Headers.Location!.ToString()
        );
        await using var session = await Session.BeginAsync(
            db.ConnectionString("app_core"),
            target.Tenant
        );
        var body = JsonDocument.Parse(
            await session.ScalarAsync<string>(
                "SELECT body::text FROM core.outbox_messages WHERE type = 'InspectionRequested'"
            )
        );
        var payload = body.RootElement.GetProperty("payload");
        Assert.Equal(
            (target.Establishment, id, "US", "Restaurant", 300000, target.Owner),
            (
                payload.GetProperty("establishmentId").GetGuid(),
                payload.GetProperty("inspectionId").GetGuid(),
                payload.GetProperty("jurisdiction").GetString(),
                payload.GetProperty("establishmentType").GetString(),
                payload.GetProperty("tokenCeiling").GetInt32(),
                payload.GetProperty("requestedBy").GetString()
            )
        );
        Assert.Equal(1, await CountAsync(db, target.Tenant));
        var row = Assert.Single(await ListAsync(client, target));
        Assert.Equal(
            (id, "Requested", "FDA Food Code 2026", 0, 0, JsonValueKind.Null),
            (
                row.GetProperty("id").GetGuid(),
                row.GetProperty("status").GetString(),
                row.GetProperty("regulation").GetString(),
                row.GetProperty("itemsTotal").GetInt32(),
                row.GetProperty("verdictCounts").GetProperty("nonCompliant").GetInt32(),
                row.GetProperty("completedAt").ValueKind
            )
        );
    }

    [Fact]
    public async Task A_start_while_one_is_in_flight_is_refused_and_not_counted()
    {
        var target = await NewTargetAsync(db);
        using var client = Client(db.Api, target);
        var first = await StartAsync(client, target);

        using var refused = await client.PostAsync(StartPath(target), null, Ct);

        await AssertProblemAsync(refused, HttpStatusCode.Conflict, "inspection_running");
        Assert.Equal(1, await CountAsync(db, target.Tenant));
        // Once it ends, the next one starts.
        await HandleAsync(
            db.Api,
            target.Tenant,
            new InspectionFailed(target.Establishment, first, "error", "Boom.")
        );
        await StartAsync(client, target);
        Assert.Equal(2, await CountAsync(db, target.Tenant));
    }

    [Fact]
    public async Task Concurrent_starts_leave_one_inspection_and_refuse_the_others()
    {
        // Each start passes the in-flight check before another commits, so the losers end at the
        // partial unique index: a 409 like the check's, never a 500, and their count rolls back.
        var target = await NewTargetAsync(db);
        using var client = Client(db.Api, target);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => client.PostAsync(StartPath(target), null, Ct))
        );

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Accepted);
        foreach (var refused in responses.Where(r => r.StatusCode != HttpStatusCode.Accepted))
        {
            await AssertProblemAsync(refused, HttpStatusCode.Conflict, "inspection_running");
        }
        Assert.Single(await ListAsync(client, target));
        Assert.Equal(1, await CountAsync(db, target.Tenant));
    }

    [Fact]
    public async Task A_tenant_runs_at_most_two_inspections_at_once_and_other_tenants_go_on()
    {
        var first = await NewTargetAsync(db);
        var (second, third) = (await SiblingAsync(db, first), await SiblingAsync(db, first));
        var other = await NewTargetAsync(db);
        using var client = Client(db.Api, first);
        var running = await StartAsync(client, first);
        await StartAsync(client, second);

        using var refused = await client.PostAsync(StartPath(third), null, Ct);
        using var otherClient = Client(db.Api, other);
        using var accepted = await otherClient.PostAsync(StartPath(other), null, Ct);

        await AssertProblemAsync(refused, HttpStatusCode.Conflict, "inspection_capacity");
        Assert.Empty(await ListAsync(client, third));
        Assert.Equal(2, await CountAsync(db, first.Tenant));
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        // Once one ends, the third starts.
        await HandleAsync(
            db.Api,
            first.Tenant,
            new InspectionFailed(first.Establishment, running, "error", "Boom.")
        );
        await StartAsync(client, third);
    }

    [Fact]
    public async Task Concurrent_starts_across_establishments_never_pass_the_tenants_cap()
    {
        // Each start passes its establishment's check, so only the tenant's day row, which the
        // count locks, keeps them from all seeing fewer than two in flight.
        var first = await NewTargetAsync(db);
        Target[] targets =
        [
            first,
            await SiblingAsync(db, first),
            await SiblingAsync(db, first),
            await SiblingAsync(db, first),
        ];
        using var client = Client(db.Api, first);

        var responses = await Task.WhenAll(
            targets.Select(target => client.PostAsync(StartPath(target), null, Ct))
        );

        Assert.Equal(
            2,
            responses.Count(response => response.StatusCode == HttpStatusCode.Accepted)
        );
        foreach (var refused in responses.Where(r => r.StatusCode != HttpStatusCode.Accepted))
        {
            await AssertProblemAsync(refused, HttpStatusCode.Conflict, "inspection_capacity");
        }
        Assert.Equal(2, await CountAsync(db, first.Tenant));
    }

    /// <summary>Another establishment of <paramref name="target"/>'s tenant, with its indexed document.</summary>
    private static async Task<Target> SiblingAsync(DatabaseFixture db, Target target)
    {
        var sibling = target with
        {
            Establishment = await TestTenants.CreateEstablishmentAsync(db, target.Tenant),
        };
        await AddDocumentAsync(db, sibling, "Indexed", id: sibling.Establishment);
        return sibling;
    }

    [Fact]
    public async Task An_establishment_without_an_indexed_document_cannot_be_inspected()
    {
        var target = await NewTargetAsync(db, indexed: false);
        using var client = Client(db.Api, target);
        // Neither a document still waiting nor a deleted indexed one counts.
        await AddDocumentAsync(db, target, "Uploaded");
        await AddDocumentAsync(db, target, "Indexed", deleted: true);

        using var refused = await client.PostAsync(StartPath(target), null, Ct);

        await AssertProblemAsync(refused, HttpStatusCode.Conflict, "no_indexed_documents");
        Assert.Equal(0, await CountAsync(db, target.Tenant));
        Assert.Empty(await ListAsync(client, target));
    }

    [Fact]
    public async Task Past_the_daily_quota_a_start_is_refused_until_midnight_and_other_tenants_go_on()
    {
        var clock = new TestClock(new DateTimeOffset(2031, 3, 4, 22, 30, 0, TimeSpan.Zero));
        await using var api = db.Api.WithWebHostBuilder(host =>
            host.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock))
        );
        var (full, other) = (await NewTargetAsync(db), await NewTargetAsync(db));
        await using (
            var session = await Session.BeginAsync(db.ConnectionString("app_core"), full.Tenant)
        )
        {
            // The demo plan's five starts of the day, used up.
            await session.ExecuteAsync(
                "INSERT INTO core.inspection_counts (tenant_id, day, count) VALUES ($1, $2, 5)",
                full.Tenant,
                new DateOnly(2031, 3, 4)
            );
            await session.CommitAsync();
        }
        using var fullClient = Client(api, full);
        using var otherClient = Client(api, other);

        using var refused = await fullClient.PostAsync(StartPath(full), null, Ct);
        using var accepted = await otherClient.PostAsync(StartPath(other), null, Ct);

        await AssertProblemAsync(refused, HttpStatusCode.TooManyRequests, "inspection_quota");
        Assert.Equal(
            (90 * 60).ToString(CultureInfo.InvariantCulture),
            refused.Headers.GetValues("Retry-After").Single()
        );
        Assert.Empty(await ListAsync(fullClient, full));
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        // The next UTC day starts a new count.
        clock.Now = new DateTimeOffset(2031, 3, 5, 0, 0, 1, TimeSpan.Zero);
        using var tomorrow = await fullClient.PostAsync(StartPath(full), null, Ct);
        Assert.Equal(HttpStatusCode.Accepted, tomorrow.StatusCode);
    }

    [Fact]
    public async Task Another_tenants_or_another_establishments_inspection_is_not_found()
    {
        var a = await NewTargetAsync(db);
        var b = await NewTargetAsync(db);
        using var clientA = Client(db.Api, a);
        using var clientB = Client(db.Api, b);
        var inspection = await StartAsync(clientA, a);
        var otherEstablishment = await TestTenants.CreateEstablishmentAsync(db, a.Tenant);

        using var startForeign = await clientB.PostAsync(StartPath(a), null, Ct);
        using var listForeign = await clientB.GetAsync(StartPath(a), Ct);
        using var getForeign = await clientB.GetAsync($"{StartPath(a)}/{inspection}", Ct);
        using var getThroughOwn = await clientB.GetAsync($"{StartPath(b)}/{inspection}", Ct);
        using var getThroughOther = await clientA.GetAsync(
            $"/establishments/{otherEstablishment}/inspections/{inspection}",
            Ct
        );

        await AssertProblemAsync(startForeign, HttpStatusCode.NotFound, "establishment.not_found");
        await AssertProblemAsync(listForeign, HttpStatusCode.NotFound, "establishment.not_found");
        await AssertProblemAsync(getForeign, HttpStatusCode.NotFound, "establishment.not_found");
        await AssertProblemAsync(getThroughOwn, HttpStatusCode.NotFound, "inspection.not_found");
        await AssertProblemAsync(getThroughOther, HttpStatusCode.NotFound, "inspection.not_found");
        Assert.Equal(0, await CountAsync(db, b.Tenant));
    }

    [Fact]
    public async Task A_completed_inspection_shows_its_progress_findings_reviews_and_actions()
    {
        var target = await NewTargetAsync(db);
        using var client = Client(db.Api, target);
        var id = await StartAsync(client, target);
        await HandleAsync(
            db.Api,
            target.Tenant,
            new InspectionStarted(target.Establishment, id, 3, [new("temperature_control", 3, 0)])
        );
        await HandleAsync(
            db.Api,
            target.Tenant,
            new InspectionProgressed(
                target.Establishment,
                id,
                3,
                3,
                [new("temperature_control", 3, 3)]
            )
        );
        await HandleAsync(db.Api, target.Tenant, Completed(target.Establishment, id));
        // A reviewer's decision, as the review endpoints will store it.
        await using (
            var session = await Session.BeginAsync(db.ConnectionString("app_core"), target.Tenant)
        )
        {
            await session.ExecuteAsync(
                """
                UPDATE core.findings SET review_status = 'Edited', reviewed_at = now(),
                    reviewed_by = (SELECT id FROM core.users WHERE sub = $1),
                    severity = 'major',
                    original = '{"Verdict":"non_compliant","Severity":"critical","Rationale":"The log shows 45°F.","ActionText":"Recalibrate the cooler.","DueOn":"2031-01-08"}'
                WHERE item_id = 'us-temp-01'
                """,
                target.Owner
            );
            await session.ExecuteAsync(
                "UPDATE core.users SET display_name = 'Olga Owner' WHERE sub = $1",
                target.Owner
            );
            await session.CommitAsync();
        }

        var inspection = await client.GetFromJsonAsync<JsonElement>(
            $"{StartPath(target)}/{id}",
            Ct
        );

        Assert.Equal(
            ("Completed", "FDA Food Code 2026", 3, 3, JsonValueKind.Null),
            (
                inspection.GetProperty("status").GetString(),
                inspection.GetProperty("regulation").GetString(),
                inspection.GetProperty("itemsTotal").GetInt32(),
                inspection.GetProperty("itemsEvaluated").GetInt32(),
                inspection.GetProperty("failureReasonCode").ValueKind
            )
        );
        var theme = Assert.Single(inspection.GetProperty("themes").EnumerateArray());
        Assert.Equal(
            ("temperature_control", 3, 3),
            (
                theme.GetProperty("theme").GetString(),
                theme.GetProperty("itemsTotal").GetInt32(),
                theme.GetProperty("itemsEvaluated").GetInt32()
            )
        );
        var findings = inspection.GetProperty("findings").EnumerateArray().ToArray();
        // Most severe first: the edit made us-temp-01 major.
        Assert.Equal(
            ["us-temp-02", "us-temp-03", "us-temp-01"],
            findings.Select(finding => finding.GetProperty("itemId").GetString())
        );
        var edited = findings[2];
        Assert.Equal(
            ("Edited", "major", "Olga Owner", "Recalibrate the cooler.", "2031-01-08"),
            (
                edited.GetProperty("reviewStatus").GetString(),
                edited.GetProperty("severity").GetString(),
                edited.GetProperty("reviewedBy").GetProperty("displayName").GetString(),
                edited.GetProperty("original").GetProperty("actionText").GetString(),
                edited.GetProperty("original").GetProperty("dueOn").GetString()
            )
        );
        var evidence = Assert.Single(edited.GetProperty("evidenceCitations").EnumerateArray());
        Assert.Equal(
            ("Cooler log", "Log", 2),
            (
                evidence.GetProperty("documentTitle").GetString(),
                evidence.GetProperty("documentType").GetString(),
                evidence.GetProperty("versionNumber").GetInt32()
            )
        );
        var regulation = Assert.Single(edited.GetProperty("regulationCitations").EnumerateArray());
        Assert.Equal("41°F", regulation.GetProperty("quote").GetString());
        var action = edited.GetProperty("actionItem");
        Assert.Equal(
            ("Recalibrate the cooler.", "Open", JsonValueKind.Null),
            (
                action.GetProperty("text").GetString(),
                action.GetProperty("status").GetString(),
                action.GetProperty("updatedBy").ValueKind
            )
        );
        var pending = findings[0];
        Assert.Equal(
            ("Pending", JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.Null),
            (
                pending.GetProperty("reviewStatus").GetString(),
                pending.GetProperty("reviewedBy").ValueKind,
                pending.GetProperty("original").ValueKind,
                pending.GetProperty("actionItem").ValueKind
            )
        );
        var row = Assert.Single(await ListAsync(client, target));
        var counts = row.GetProperty("verdictCounts");
        Assert.Equal(
            (1, 1, 1),
            (
                counts.GetProperty("compliant").GetInt32(),
                counts.GetProperty("nonCompliant").GetInt32(),
                counts.GetProperty("insufficientEvidence").GetInt32()
            )
        );
    }

    /// <summary>
    /// A new tenant with its Owner and a US establishment, with one indexed document unless told
    /// otherwise. That document's id is the establishment's, so <see cref="Completed"/> can cite it.
    /// </summary>
    internal static async Task<Target> NewTargetAsync(DatabaseFixture db, bool indexed = true)
    {
        var owner = Guid.NewGuid().ToString();
        var tenant = await TestTenants.CreateAsync(db, null, owner);
        var target = new Target(
            tenant,
            await TestTenants.CreateEstablishmentAsync(db, tenant),
            owner
        );
        if (indexed)
        {
            await AddDocumentAsync(db, target, "Indexed", id: target.Establishment);
        }
        return target;
    }

    /// <summary>A document of the establishment whose one version has <paramref name="status"/>. Returns its id.</summary>
    internal static async Task<Guid> AddDocumentAsync(
        DatabaseFixture db,
        Target target,
        string status,
        bool deleted = false,
        Guid? id = null
    )
    {
        var document = id ?? Guid.CreateVersion7();
        await using var session = await Session.BeginAsync(
            db.ConnectionString("app_core"),
            target.Tenant
        );
        await session.ExecuteAsync(
            """
            INSERT INTO core.documents (id, tenant_id, establishment_id, title, type, created_at, deleted_at)
            VALUES ($1, $2, $3, 'SOP', 'Sop', now(), CASE WHEN $4 THEN now() END)
            """,
            document,
            target.Tenant,
            target.Establishment,
            deleted
        );
        await session.ExecuteAsync(
            """
            INSERT INTO core.document_versions (id, tenant_id, document_id, number, file_name,
                content_type, size, sha256, blob_path, uploaded_by, uploaded_at, status, status_changed_at)
            SELECT $1, $2, $3, 1, 'sop.pdf', 'application/pdf', 1, '00', 'path', id, now(), $4, now()
            FROM core.users WHERE sub = $5
            """,
            Guid.CreateVersion7(),
            target.Tenant,
            document,
            status,
            target.Owner
        );
        await session.CommitAsync();
        return document;
    }

    internal static HttpClient Client(WebApplicationFactory<Program> api, Target target) =>
        api.CreateClient(ApiFactory.CreateToken(target.Tenant.ToString(), target.Owner));

    internal static string StartPath(Target target) =>
        $"/establishments/{target.Establishment}/inspections";

    /// <summary>Starts an inspection; returns its id.</summary>
    internal static async Task<Guid> StartAsync(HttpClient client, Target target)
    {
        using var response = await client.PostAsync(StartPath(target), null, Ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("id")
            .GetGuid();
    }

    internal static async Task<JsonElement[]> ListAsync(HttpClient client, Target target) =>
        (await client.GetFromJsonAsync<JsonElement>(StartPath(target), Ct))
            .EnumerateArray()
            .ToArray();

    /// <summary>The body the AI service would publish for <paramref name="payload"/>, handled as the consumer does.</summary>
    internal static Task<ConsumeOutcome> HandleAsync<TMessage>(
        ApiFactory api,
        Guid tenant,
        TMessage payload,
        Guid? messageId = null
    )
        where TMessage : IIntegrationMessage =>
        api
            .Services.GetRequiredService<InspectionStatusMessages>()
            .HandleAsync(DocumentStatusTests.Body(tenant, payload, messageId), Ct);

    /// <summary>
    /// Three findings, one per verdict; the non-compliant one has an action due in 7 days. Each
    /// cites the indexed document <see cref="NewTargetAsync"/> made (its id is the establishment's).
    /// </summary>
    internal static InspectionCompleted Completed(Guid establishment, Guid inspection)
    {
        RegulationCitation regulation = new(
            "FDA Food Code",
            "2022",
            "3-501.16",
            "en",
            "Cold holding at 41°F.",
            "41°F",
            15,
            19
        );
        FindingResult Result(string itemId, string verdict, ProposedAction? action) =>
            new(
                itemId,
                "temperature_control",
                $"Requirement {itemId}",
                "critical",
                verdict,
                "high",
                "The log shows 45°F.",
                [regulation],
                [
                    new(
                        establishment,
                        "Cooler log",
                        DocumentType.Log,
                        2,
                        "Week 2",
                        "en",
                        "Reading 45°F.",
                        "45°F",
                        null,
                        null
                    ),
                ],
                action
            );
        return new(
            establishment,
            inspection,
            1200,
            300,
            0.0123m,
            [
                Result("us-temp-01", "non_compliant", new("Recalibrate the cooler.", 7)),
                Result("us-temp-02", "compliant", null),
                Result("us-temp-03", "insufficient_evidence", new("Keep a cooling log.", 30)),
            ]
        );
    }

    internal static async Task<int> CountAsync(DatabaseFixture db, Guid tenant)
    {
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant);
        return (int)(
            await session.ScalarAsync<long>(
                "SELECT coalesce(sum(count), 0) FROM core.inspection_counts"
            )
        );
    }

    internal static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code
    )
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
