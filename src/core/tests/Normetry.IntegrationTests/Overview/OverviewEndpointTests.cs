using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Normetry.Application.Inspections;
using Normetry.Domain.Identity;
using Normetry.Domain.Inspections;
using Normetry.Infrastructure.Messaging;
using Normetry.IntegrationTests.Database;
using Xunit;
using static Normetry.IntegrationTests.Inspections.InspectionEndpointTests;

namespace Normetry.IntegrationTests.Overview;

/// <summary>
/// <c>GET /establishments/{id}/overview</c> on inspections completed through
/// <see cref="InspectionStatusMessages"/> and documents written in SQL. Each test makes its own
/// tenant; the API's clock is the system's, so dates are relative to today (UTC).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OverviewEndpointTests
{
    private static readonly string[] InspectionSections =
    [
        "latestInspection",
        "openActionItems",
        "overdueActionItems",
        "pendingReviews",
        "series",
    ];

    private readonly DatabaseFixture db;

    public OverviewEndpointTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static string Path(Target target) => $"/establishments/{target.Establishment}/overview";

    [Fact]
    public async Task The_numbers_come_from_the_latest_completed_inspection_as_reviewed()
    {
        var target = await NewTargetAsync(db);
        using var client = Client(db.Api, target);
        // Older: one of three compliant.
        var older = await StartAsync(client, target);
        await CompleteAsync(target, Completed(target.Establishment, older));
        var latest = await StartAsync(client, target);
        await CompleteAsync(
            target,
            WithFindings(
                target.Establishment,
                latest,
                ("t-01", "temperature_control", "critical", "non_compliant"),
                ("t-02", "temperature_control", "major", "compliant"),
                ("p-01", "personal_hygiene", "minor", "non_compliant"),
                ("p-02", "personal_hygiene", "critical", "insufficient_evidence"),
                ("c-01", "cleaning_sanitizing", "major", "non_compliant"),
                ("pc-01", "pest_control", "major", "non_compliant"),
                ("w-01", "waste", "minor", "compliant"),
                ("x-01", "cross_contamination_allergens", "minor", "insufficient_evidence"),
                ("wp-01", "water_premises", "minor", "insufficient_evidence"),
                ("d-01", "documentation_training", "major", "insufficient_evidence")
            )
        );
        var findings = (
            await client.GetFromJsonAsync<JsonElement>($"{StartPath(target)}/{latest}", Ct)
        )
            .GetProperty("findings")
            .EnumerateArray()
            .ToDictionary(finding => finding.GetProperty("itemId").GetString()!);
        string FindingPath(string itemId) =>
            $"{StartPath(target)}/{latest}/findings/{findings[itemId].GetProperty("id").GetGuid()}";
        async Task OkAsync(Task<HttpResponseMessage> request)
        {
            using var response = await request;
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        // Rejected: out of every number. Edited: its new verdict counts, and its action is now
        // overdue. Accepted. One action done.
        await OkAsync(
            client.PostAsJsonAsync($"{FindingPath("c-01")}/reject", new { note = "Wrong." }, Ct)
        );
        await OkAsync(
            client.PatchAsJsonAsync(FindingPath("pc-01"), new { verdict = "compliant" }, Ct)
        );
        await OkAsync(
            client.PatchAsJsonAsync(
                FindingPath("p-01"),
                new { dueOn = Today.AddDays(-1).ToString("O", CultureInfo.InvariantCulture) },
                Ct
            )
        );
        await OkAsync(client.PostAsync($"{FindingPath("t-02")}/accept", null, Ct));
        var doneAction = findings["x-01"].GetProperty("actionItem").GetProperty("id").GetGuid();
        await OkAsync(
            client.PostAsync(
                $"{StartPath(target)}/{latest}/action-items/{doneAction}/done",
                null,
                Ct
            )
        );
        // A newer inspection that failed hides nothing.
        var failed = await StartAsync(client, target);
        Assert.Equal(
            ConsumeOutcome.Ack,
            await HandleAsync(
                db.Api,
                target.Tenant,
                new InspectionFailed(target.Establishment, failed, "error", "Boom.")
            )
        );

        var overview = await GetAsync(client, target);

        var inspection = overview.GetProperty("latestInspection");
        Assert.Equal(
            (latest, "FDA Food Code 2026"),
            (
                inspection.GetProperty("id").GetGuid(),
                inspection.GetProperty("regulation").GetString()
            )
        );
        // Compliant: t-02, w-01 and pc-01 (edited) of nine; c-01 is rejected.
        Assert.Equal(3.0 / 9, inspection.GetProperty("compliance").GetDouble(), 1e-9);
        var counts = inspection.GetProperty("verdictCounts");
        Assert.Equal(
            (3, 2, 4),
            (
                counts.GetProperty("compliant").GetInt32(),
                counts.GetProperty("nonCompliant").GetInt32(),
                counts.GetProperty("insufficientEvidence").GetInt32()
            )
        );
        Assert.Equal(
            [
                "personal_hygiene 0 1 1",
                "temperature_control 1 1 0",
                "cross_contamination_allergens 0 0 1",
                "pest_control 1 0 0",
                "water_premises 0 0 1",
                "waste 1 0 0",
                "documentation_training 0 0 1",
            ],
            inspection
                .GetProperty("byTheme")
                .EnumerateArray()
                .Select(theme =>
                    $"{theme.GetProperty("theme").GetString()} {theme.GetProperty("compliant").GetInt32()} {theme.GetProperty("nonCompliant").GetInt32()} {theme.GetProperty("insufficientEvidence").GetInt32()}"
                )
        );
        // critical NC 3; minor NC 1 + critical IE 1.5; major IE 1. Cross-contamination and water
        // (0.5 each) fall below the top 3; pest control (now compliant) and the rejected cleaning
        // finding score nothing.
        Assert.Equal(
            [
                ("temperature_control", 3.0),
                ("personal_hygiene", 2.5),
                ("documentation_training", 1.0),
            ],
            inspection
                .GetProperty("topRiskThemes")
                .EnumerateArray()
                .Select(theme =>
                    (theme.GetProperty("theme").GetString(), theme.GetProperty("score").GetDouble())
                )
        );
        // Open: t-01, p-01, p-02, pc-01, wp-01, d-01 (x-01 done, c-01 rejected); p-01 overdue.
        // Pending: t-01, p-02, w-01, x-01, wp-01, d-01.
        Assert.Equal(
            (6, 1, 6),
            (
                overview.GetProperty("openActionItems").GetInt32(),
                overview.GetProperty("overdueActionItems").GetInt32(),
                overview.GetProperty("pendingReviews").GetInt32()
            )
        );
        Assert.Equal(
            [(older, 1.0 / 3), (latest, 3.0 / 9)],
            overview
                .GetProperty("series")
                .EnumerateArray()
                .Select(point =>
                    (
                        point.GetProperty("inspectionId").GetGuid(),
                        point.GetProperty("compliance").GetDouble()
                    )
                )
        );
        Assert.Equal(
            inspection.GetProperty("completedAt").GetDateTimeOffset(),
            overview.GetProperty("series")[1].GetProperty("completedAt").GetDateTimeOffset()
        );
    }

    [Fact]
    public async Task Before_a_completed_inspection_the_inspection_sections_are_empty()
    {
        var target = await NewTargetAsync(db);
        using var client = Client(db.Api, target);
        var failed = await StartAsync(client, target);
        await HandleAsync(
            db.Api,
            target.Tenant,
            new InspectionFailed(target.Establishment, failed, "error", "Boom.")
        );

        var overview = await GetAsync(client, target);

        Assert.Equal(JsonValueKind.Null, overview.GetProperty("latestInspection").ValueKind);
        Assert.Equal(
            (0, 0, 0, 0),
            (
                overview.GetProperty("openActionItems").GetInt32(),
                overview.GetProperty("overdueActionItems").GetInt32(),
                overview.GetProperty("pendingReviews").GetInt32(),
                overview.GetProperty("series").GetArrayLength()
            )
        );
        var documents = overview.GetProperty("documents");
        Assert.Equal(
            (0, 0, 0),
            (
                documents.GetProperty("expired").GetInt32(),
                documents.GetProperty("expiringSoon").GetInt32(),
                documents.GetProperty("list").GetArrayLength()
            )
        );
    }

    [Fact]
    public async Task Documents_expired_or_expiring_within_30_days_are_counted_and_the_soonest_10_listed()
    {
        var target = await NewTargetAsync(db);
        var other = await TestTenants.CreateEstablishmentAsync(db, target.Tenant);
        await AddDatedAsync(target, target.Establishment, "Old permit", -10);
        await AddDatedAsync(target, target.Establishment, "Pest contract", -1);
        await AddDatedAsync(target, target.Establishment, "Today", 0);
        await AddDatedAsync(target, target.Establishment, "Day 30", 30);
        for (var day = 1; day <= 12; day++)
        {
            await AddDatedAsync(target, target.Establishment, $"Day {day}", day);
        }
        // None of these: too far, no date, deleted, another establishment's.
        await AddDatedAsync(target, target.Establishment, "Day 31", 31);
        await AddDatedAsync(target, target.Establishment, "Undated", null);
        await AddDatedAsync(target, target.Establishment, "Deleted", -5, deleted: true);
        await AddDatedAsync(target, other, "Elsewhere", -3);
        using var client = Client(db.Api, target);

        var documents = (await GetAsync(client, target)).GetProperty("documents");

        Assert.Equal(
            (2, 14),
            (
                documents.GetProperty("expired").GetInt32(),
                documents.GetProperty("expiringSoon").GetInt32()
            )
        );
        Assert.Equal(
            [
                "Old permit",
                "Pest contract",
                "Today",
                "Day 1",
                "Day 2",
                "Day 3",
                "Day 4",
                "Day 5",
                "Day 6",
                "Day 7",
            ],
            documents
                .GetProperty("list")
                .EnumerateArray()
                .Select(document => document.GetProperty("title").GetString())
        );
        var first = documents.GetProperty("list")[0];
        Assert.Equal(
            ("License", Today.AddDays(-10).ToString("O", CultureInfo.InvariantCulture)),
            (first.GetProperty("type").GetString(), first.GetProperty("expiresOn").GetString())
        );
        Assert.Equal(JsonValueKind.String, first.GetProperty("id").ValueKind);
    }

    [Fact]
    public async Task Each_section_needs_its_own_permission()
    {
        var target = await NewTargetAsync(db);
        await AddDatedAsync(target, target.Establishment, "Permit", -1);
        var (inspectionsOnly, documentsOnly) = (
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString()
        );
        await TestTenants.GrantAsync(
            db,
            target.Tenant,
            inspectionsOnly,
            Permissions.Inspections.Read
        );
        await TestTenants.GrantAsync(db, target.Tenant, documentsOnly, Permissions.Documents.Read);

        var inspections = await GetAsync(ClientOf(target, inspectionsOnly), target);
        var documents = await GetAsync(ClientOf(target, documentsOnly), target);

        Assert.Equal(InspectionSections, Properties(inspections));
        Assert.Equal(["documents"], Properties(documents));
        Assert.Equal(1, documents.GetProperty("documents").GetProperty("expired").GetInt32());
    }

    [Fact]
    public async Task Another_tenants_establishment_is_not_found()
    {
        var target = await NewTargetAsync(db);
        var stranger = await NewTargetAsync(db);
        using var client = Client(db.Api, stranger);

        using var response = await client.GetAsync(Path(target), Ct);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "establishment.not_found");
    }

    private HttpClient ClientOf(Target target, string sub) =>
        db.Api.CreateClient(ApiFactory.CreateToken(target.Tenant.ToString(), sub));

    private static async Task<JsonElement> GetAsync(HttpClient client, Target target)
    {
        using var response = await client.GetAsync(Path(target), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static string[] Properties(JsonElement element) =>
        [
            .. element
                .EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal),
        ];

    private async Task CompleteAsync(Target target, InspectionCompleted completed) =>
        Assert.Equal(ConsumeOutcome.Ack, await HandleAsync(db.Api, target.Tenant, completed));

    /// <summary>
    /// <see cref="Completed"/> with other findings (item, theme, severity, verdict); all but the
    /// compliant ones have an action due in 7 days.
    /// </summary>
    private static InspectionCompleted WithFindings(
        Guid establishment,
        Guid inspection,
        params (string Item, string Theme, string Severity, string Verdict)[] findings
    )
    {
        var completed = Completed(establishment, inspection);
        var template = completed.Findings[0];
        return completed with
        {
            Findings =
            [
                .. findings.Select(finding =>
                    template with
                    {
                        ItemId = finding.Item,
                        Theme = finding.Theme,
                        Severity = finding.Severity,
                        Verdict = finding.Verdict,
                        Action = finding.Verdict == "compliant" ? null : new("Fix it.", 7),
                    }
                ),
            ],
        };
    }

    /// <summary>A License of the establishment expiring <paramref name="days"/> from today (none when null).</summary>
    private async Task AddDatedAsync(
        Target target,
        Guid establishment,
        string title,
        int? days,
        bool deleted = false
    )
    {
        await using var session = await Session.BeginAsync(
            db.ConnectionString("app_core"),
            target.Tenant
        );
        await session.ExecuteAsync(
            """
            INSERT INTO core.documents (id, tenant_id, establishment_id, title, type, created_at, deleted_at, expires_on)
            VALUES ($1, $2, $3, $4, 'License', now(), CASE WHEN $5 THEN now() END, $6::date)
            """,
            Guid.CreateVersion7(),
            target.Tenant,
            establishment,
            title,
            deleted,
            days is { } offset ? Today.AddDays(offset) : null
        );
        await session.CommitAsync();
    }
}
