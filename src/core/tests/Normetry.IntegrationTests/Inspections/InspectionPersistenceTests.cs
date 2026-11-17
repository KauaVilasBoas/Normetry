using Microsoft.EntityFrameworkCore;
using Normetry.Application.Abstractions;
using Normetry.Domain.Documents;
using Normetry.Domain.Inspections;
using Normetry.Infrastructure.Persistence;
using Normetry.IntegrationTests.Database;
using Xunit;

namespace Normetry.IntegrationTests.Inspections;

/// <summary>The EF Core mapping of an inspection, its findings (JSON copies included) and their actions.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class InspectionPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture db;

    public InspectionPersistenceTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<T> InTenantAsync<T>(Guid tenant, Func<CoreDbContext, Task<T>> work)
    {
        await using var context = new CoreDbContext(
            new DbContextOptionsBuilder<CoreDbContext>()
                .UseTenantScopedCoreDatabase(
                    db.ConnectionString("app_core"),
                    new FixedTenant(tenant)
                )
                .Options
        );
        await using var transaction = await context.Database.BeginTransactionAsync(Ct);
        var result = await work(context);
        await context.SaveChangesAsync(Ct);
        await transaction.CommitAsync(Ct);
        return result;
    }

    private static Task<Inspection> LoadAsync(CoreDbContext context, Guid id) =>
        context
            .Inspections.Include(inspection => inspection.Findings)
                .ThenInclude(finding => finding.ActionItem)
            .SingleAsync(inspection => inspection.Id == id, Ct);

    [Fact]
    public async Task An_inspection_round_trips_with_its_snapshot_findings_reviews_and_actions()
    {
        var tenant = await TestTenants.CreateAsync(db);
        var establishment = await TestTenants.CreateEstablishmentAsync(db, tenant);
        await TestTenants.GrantAsync(db, tenant, "reviewer");
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant);
        var reviewer = await session.ScalarAsync<Guid>(
            "SELECT id FROM core.users WHERE sub = 'reviewer'"
        );
        var regulation = new RegulationCitation(
            "FDA Food Code",
            "2022",
            "3-501.16",
            "en",
            "Cold holding text.",
            "41°F",
            0,
            4
        );
        var evidence = new EvidenceCitation(
            Guid.NewGuid(),
            "Cooler log",
            DocumentType.Log,
            2,
            "Week 2",
            "en",
            "Reading 45°F.",
            "45°F",
            null,
            null
        );

        var id = await InTenantAsync(tenant, context =>
        {
            var inspection = Inspection.Request(tenant, establishment, "owner-sub", Now);
            inspection.Start(2, [new("temperature_control", 2, 0)], Now);
            context.Inspections.Add(inspection);
            return Task.FromResult(inspection.Id);
        });
        await InTenantAsync(tenant, async context =>
        {
            var inspection = await LoadAsync(context, id);
            Assert.True(inspection.Progress(1, 2, [new("temperature_control", 2, 1)], Now));
            return inspection.Complete(
                100,
                20,
                0.004321m,
                [
                    new(
                        "us-temp-01",
                        "temperature_control",
                        "Cold holding",
                        "critical",
                        "non_compliant",
                        "high",
                        "The log shows 45°F.",
                        [regulation],
                        [evidence],
                        new("Recalibrate the cooler.", 7)
                    ),
                    new(
                        "us-temp-02",
                        "temperature_control",
                        "Hot holding",
                        "major",
                        "insufficient_evidence",
                        "low",
                        "No hot holding records.",
                        [regulation],
                        [],
                        null
                    ),
                ],
                Now.AddMinutes(5)
            );
        });
        await InTenantAsync(tenant, async context =>
        {
            var inspection = await LoadAsync(context, id);
            var (first, second) = (
                inspection.Findings.Single(finding => finding.ItemId == "us-temp-01"),
                inspection.Findings.Single(finding => finding.ItemId == "us-temp-02")
            );
            first.Edit(reviewer, new(Severity: "major", DueOn: new(2026, 12, 1)), Now);
            first.ActionItem!.MarkDone(reviewer, Now);
            second.Edit(
                reviewer,
                new(ActionText: "Keep a hot holding log.", DueOn: new(2026, 12, 15)),
                Now
            );
            return true;
        });

        var stored = await InTenantAsync(tenant, context => LoadAsync(context, id));
        Assert.Equal(
            (InspectionStatus.Completed, 2, 1, 100, 20, 0.004321m),
            (
                stored.Status,
                stored.ItemsTotal,
                stored.ItemsEvaluated,
                stored.InputTokens,
                stored.OutputTokens,
                stored.CostUsd
            )
        );
        Assert.Equal([new ThemeProgress("temperature_control", 2, 1)], stored.Themes);
        var edited = stored.Findings.Single(finding => finding.ItemId == "us-temp-01");
        Assert.Equal([regulation], edited.RegulationCitations);
        Assert.Equal([evidence], edited.EvidenceCitations);
        Assert.Equal(
            (ReviewStatus.Edited, (Guid?)reviewer, "major"),
            (edited.ReviewStatus, edited.ReviewedBy, edited.Severity)
        );
        Assert.Equal(
            new FindingOriginal(
                "non_compliant",
                "critical",
                "The log shows 45°F.",
                "Recalibrate the cooler.",
                new DateOnly(2026, 11, 24)
            ),
            edited.Original
        );
        Assert.Equal(
            (new DateOnly(2026, 12, 1), ActionItemStatus.Done, (Guid?)reviewer),
            (edited.ActionItem!.DueOn, edited.ActionItem.Status, edited.ActionItem.UpdatedBy)
        );
        var created = stored.Findings.Single(finding => finding.ItemId == "us-temp-02");
        Assert.Empty(created.EvidenceCitations);
        Assert.Equal(
            ("Keep a hot holding log.", new DateOnly(2026, 12, 15), ActionItemStatus.Open),
            (created.ActionItem!.Text, created.ActionItem.DueOn, created.ActionItem.Status)
        );
        Assert.Equal(
            new FindingOriginal(
                "insufficient_evidence",
                "major",
                "No hot holding records.",
                null,
                null
            ),
            created.Original
        );
        // Domain enums inside the JSON go by name, like everywhere else.
        Assert.Equal(
            "Log",
            await session.ScalarAsync<string>(
                "SELECT evidence_citations -> 0 ->> 'DocumentType' FROM core.findings WHERE item_id = 'us-temp-01'"
            )
        );
    }

    private sealed record FixedTenant(Guid TenantId) : ITenantContext;
}
