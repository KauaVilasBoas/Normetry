using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Normetry.Domain.Identity;
using Normetry.IntegrationTests.Database;
using Xunit;
using static Normetry.IntegrationTests.Inspections.InspectionEndpointTests;

namespace Normetry.IntegrationTests.Inspections;

/// <summary>
/// Reviewing findings and their actions, and the Development feedback export, through the API.
/// Each test completes its own inspection (<see cref="Completed"/>: us-temp-01 non-compliant with
/// an action, us-temp-02 compliant without one, us-temp-03 insufficient evidence with one).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class FindingReviewTests
{
    private readonly DatabaseFixture db;

    public FindingReviewTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Run(
        Target Target,
        Guid Inspection,
        Dictionary<string, JsonElement> Findings
    )
    {
        public string Path => $"{StartPath(Target)}/{Inspection}";

        public string FindingPath(string itemId) => $"{Path}/findings/{Id(itemId)}";

        public Guid Id(string itemId) => Findings[itemId].GetProperty("id").GetGuid();

        public string ActionPath(string itemId) =>
            $"{Path}/action-items/{Findings[itemId].GetProperty("actionItem").GetProperty("id").GetGuid()}";
    }

    private async Task<Run> CompleteAsync(Target? target = null)
    {
        target ??= await NewTargetAsync(db);
        using var client = Client(db.Api, target);
        var id = await StartAsync(client, target);
        await HandleAsync(db.Api, target.Tenant, Completed(target.Establishment, id));
        var inspection = await client.GetFromJsonAsync<JsonElement>(
            $"{StartPath(target)}/{id}",
            Ct
        );
        return new Run(
            target,
            id,
            inspection
                .GetProperty("findings")
                .EnumerateArray()
                .ToDictionary(finding => finding.GetProperty("itemId").GetString()!)
        );
    }

    private async Task<Guid> UserIdAsync(Target target)
    {
        await using var session = await Session.BeginAsync(
            db.ConnectionString("app_core"),
            target.Tenant
        );
        return await session.ScalarAsync<Guid>(
            "SELECT id FROM core.users WHERE sub = $1",
            target.Owner
        );
    }

    /// <summary>The field errors of a <c>validation</c> problem (the body can be read once).</summary>
    private static async Task<JsonElement> ValidationErrorsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("validation", problem.GetProperty("code").GetString());
        return problem.GetProperty("errors");
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<JsonElement> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body = null
    )
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        using var response = await client.SendAsync(request, Ct);
        return await OkAsync(response);
    }

    private static Task<HttpResponseMessage> PatchAsync(
        HttpClient client,
        string path,
        object body
    ) => client.PatchAsJsonAsync(path, body, Ct);

    private static async Task<JsonElement> GetFindingAsync(
        HttpClient client,
        Run run,
        string itemId
    ) =>
        (await client.GetFromJsonAsync<JsonElement>(run.Path, Ct))
            .GetProperty("findings")
            .EnumerateArray()
            .Single(finding => finding.GetProperty("itemId").GetString() == itemId);

    private static string? Text(JsonElement element, string property) =>
        element.GetProperty(property) is { ValueKind: JsonValueKind.Null }
            ? null
            : element.GetProperty(property).GetString();

    [Fact]
    public async Task Accept_and_reject_record_the_reviewer_and_the_latest_decision_wins()
    {
        var run = await CompleteAsync();
        using var client = Client(db.Api, run.Target);
        var reviewer = await UserIdAsync(run.Target);

        var accepted = await SendAsync(
            client,
            HttpMethod.Post,
            $"{run.FindingPath("us-temp-01")}/accept"
        );

        Assert.Equal(
            (run.Id("us-temp-01"), "Accepted", reviewer, (string?)null),
            (
                accepted.GetProperty("id").GetGuid(),
                accepted.GetProperty("reviewStatus").GetString(),
                accepted.GetProperty("reviewedBy").GetProperty("id").GetGuid(),
                Text(accepted, "reviewNote")
            )
        );
        Assert.NotEqual(JsonValueKind.Null, accepted.GetProperty("reviewedAt").ValueKind);

        var rejected = await SendAsync(
            client,
            HttpMethod.Post,
            $"{run.FindingPath("us-temp-01")}/reject",
            new { note = "  The log is from another cooler.  " }
        );
        Assert.Equal(
            ("Rejected", "The log is from another cooler."),
            (rejected.GetProperty("reviewStatus").GetString(), Text(rejected, "reviewNote"))
        );

        // Accepting again replaces the rejection, and drops its note.
        await SendAsync(client, HttpMethod.Post, $"{run.FindingPath("us-temp-01")}/accept");
        var stored = await GetFindingAsync(client, run, "us-temp-01");
        Assert.Equal(
            ("Accepted", (string?)null, JsonValueKind.Null),
            (
                stored.GetProperty("reviewStatus").GetString(),
                Text(stored, "reviewNote"),
                stored.GetProperty("original").ValueKind
            )
        );
        Assert.Equal(
            "Pending",
            (await GetFindingAsync(client, run, "us-temp-02"))
                .GetProperty("reviewStatus")
                .GetString()
        );
    }

    [Fact]
    public async Task A_rejection_needs_a_note_of_at_most_1000_characters()
    {
        var run = await CompleteAsync();
        using var client = Client(db.Api, run.Target);

        foreach (
            object body in new object[]
            {
                new { },
                new { note = "   " },
                new { note = new string('x', 1001) },
            }
        )
        {
            using var refused = await client.PostAsJsonAsync(
                $"{run.FindingPath("us-temp-01")}/reject",
                body,
                Ct
            );
            Assert.True((await ValidationErrorsAsync(refused)).TryGetProperty("Note", out _));
        }

        Assert.Equal(
            "Pending",
            (await GetFindingAsync(client, run, "us-temp-01"))
                .GetProperty("reviewStatus")
                .GetString()
        );
        var longest = await SendAsync(
            client,
            HttpMethod.Post,
            $"{run.FindingPath("us-temp-01")}/reject",
            new { note = new string('x', 1000) }
        );
        Assert.Equal("Rejected", longest.GetProperty("reviewStatus").GetString());
    }

    [Fact]
    public async Task Edits_change_only_the_fields_given_and_keep_the_inspections_values()
    {
        var run = await CompleteAsync();
        using var client = Client(db.Api, run.Target);
        var reviewer = await UserIdAsync(run.Target);
        var proposed = run.Findings["us-temp-01"].GetProperty("actionItem");
        var proposedDueOn = proposed.GetProperty("dueOn").GetString();

        using var first = await PatchAsync(
            client,
            run.FindingPath("us-temp-01"),
            new { severity = "major" }
        );
        var once = await OkAsync(first);
        using var second = await PatchAsync(
            client,
            run.FindingPath("us-temp-01"),
            new
            {
                verdict = "compliant",
                rationale = "The 45°F reading is the freezer's door alarm.",
                actionText = "Label the freezer log.",
                dueOn = "2031-02-01",
            }
        );
        var twice = await OkAsync(second);

        Assert.Equal(
            ("Edited", "major", "non_compliant"),
            (
                once.GetProperty("reviewStatus").GetString(),
                once.GetProperty("severity").GetString(),
                once.GetProperty("verdict").GetString()
            )
        );
        Assert.Equal(
            (
                "Edited",
                "major",
                "compliant",
                "The 45°F reading is the freezer's door alarm.",
                reviewer
            ),
            (
                twice.GetProperty("reviewStatus").GetString(),
                twice.GetProperty("severity").GetString(),
                twice.GetProperty("verdict").GetString(),
                Text(twice, "rationale"),
                twice.GetProperty("reviewedBy").GetProperty("id").GetGuid()
            )
        );
        var action = twice.GetProperty("actionItem");
        Assert.Equal(
            (
                proposed.GetProperty("id").GetGuid(),
                "Label the freezer log.",
                "2031-02-01",
                "Open",
                reviewer
            ),
            (
                action.GetProperty("id").GetGuid(),
                Text(action, "text"),
                Text(action, "dueOn"),
                Text(action, "status"),
                action.GetProperty("updatedBy").GetProperty("id").GetGuid()
            )
        );
        // After two edits, original still holds what the inspection said.
        foreach (var finding in new[] { twice, await GetFindingAsync(client, run, "us-temp-01") })
        {
            var original = finding.GetProperty("original");
            Assert.Equal(
                (
                    "non_compliant",
                    "critical",
                    "The log shows 45°F.",
                    "Recalibrate the cooler.",
                    proposedDueOn
                ),
                (
                    Text(original, "verdict"),
                    Text(original, "severity"),
                    Text(original, "rationale"),
                    Text(original, "actionText"),
                    Text(original, "dueOn")
                )
            );
        }

        // Accepting the edited finding keeps the edited values and the original.
        var accepted = await SendAsync(
            client,
            HttpMethod.Post,
            $"{run.FindingPath("us-temp-01")}/accept"
        );
        Assert.Equal(
            ("Accepted", "compliant", "critical"),
            (
                accepted.GetProperty("reviewStatus").GetString(),
                accepted.GetProperty("verdict").GetString(),
                accepted.GetProperty("original").GetProperty("severity").GetString()
            )
        );
    }

    [Fact]
    public async Task An_edit_creates_a_missing_action_only_from_its_text_and_due_date_together()
    {
        var run = await CompleteAsync();
        using var client = Client(db.Api, run.Target);
        var path = run.FindingPath("us-temp-02");

        foreach (
            object half in new object[]
            {
                new { actionText = "Keep a log." },
                new { dueOn = "2031-02-01" },
            }
        )
        {
            using var refused = await PatchAsync(client, path, half);
            var errors = await ValidationErrorsAsync(refused);
            Assert.True(errors.TryGetProperty("ActionText", out _));
            Assert.True(errors.TryGetProperty("DueOn", out _));
        }
        Assert.Equal(
            "Pending",
            (await GetFindingAsync(client, run, "us-temp-02"))
                .GetProperty("reviewStatus")
                .GetString()
        );

        using var created = await PatchAsync(
            client,
            path,
            new { actionText = "Keep a log.", dueOn = "2031-02-01" }
        );
        var finding = await OkAsync(created);
        Assert.Equal(
            ("Edited", "Keep a log.", "2031-02-01", "Open"),
            (
                finding.GetProperty("reviewStatus").GetString(),
                Text(finding.GetProperty("actionItem"), "text"),
                Text(finding.GetProperty("actionItem"), "dueOn"),
                Text(finding.GetProperty("actionItem"), "status")
            )
        );
        Assert.Equal(
            (JsonValueKind.Null, JsonValueKind.Null),
            (
                finding.GetProperty("original").GetProperty("actionText").ValueKind,
                finding.GetProperty("original").GetProperty("dueOn").ValueKind
            )
        );

        // Now that it has one, the due date alone moves it.
        using var moved = await PatchAsync(client, path, new { dueOn = "2031-03-01" });
        Assert.Equal("2031-03-01", Text((await OkAsync(moved)).GetProperty("actionItem"), "dueOn"));
    }

    [Fact]
    public async Task An_edit_off_the_rules_is_refused_and_changes_nothing()
    {
        var run = await CompleteAsync();
        using var client = Client(db.Api, run.Target);

        foreach (
            var (body, field) in new (object, string)[]
            {
                (new { }, "Edit"),
                (new { verdict = "Compliant" }, "Verdict"),
                (new { severity = "high" }, "Severity"),
                (new { rationale = " " }, "Rationale"),
                (new { rationale = new string('x', 2001) }, "Rationale"),
                (new { actionText = new string('x', 1001) }, "ActionText"),
            }
        )
        {
            using var refused = await PatchAsync(client, run.FindingPath("us-temp-01"), body);
            Assert.True((await ValidationErrorsAsync(refused)).TryGetProperty(field, out _), field);
        }
        // Not an ISO date: the body does not bind.
        using var badDate = await PatchAsync(
            client,
            run.FindingPath("us-temp-01"),
            new { dueOn = "01/02/2031" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, badDate.StatusCode);

        var stored = await GetFindingAsync(client, run, "us-temp-01");
        Assert.Equal(
            ("Pending", "critical", JsonValueKind.Null),
            (
                stored.GetProperty("reviewStatus").GetString(),
                stored.GetProperty("severity").GetString(),
                stored.GetProperty("original").ValueKind
            )
        );
    }

    [Fact]
    public async Task An_action_is_marked_done_and_reopened()
    {
        var run = await CompleteAsync();
        using var client = Client(db.Api, run.Target);
        var reviewer = await UserIdAsync(run.Target);

        var done = await SendAsync(client, HttpMethod.Post, $"{run.ActionPath("us-temp-03")}/done");
        var again = await SendAsync(
            client,
            HttpMethod.Post,
            $"{run.ActionPath("us-temp-03")}/done"
        );

        Assert.Equal(
            (run.Id("us-temp-03"), "Done", reviewer, "Pending"),
            (
                done.GetProperty("id").GetGuid(),
                Text(done.GetProperty("actionItem"), "status"),
                done.GetProperty("actionItem").GetProperty("updatedBy").GetProperty("id").GetGuid(),
                done.GetProperty("reviewStatus").GetString()
            )
        );
        // Marking it done twice changes nothing, not even when (PostgreSQL keeps microseconds).
        Assert.Equal(
            done.GetProperty("actionItem").GetProperty("updatedAt").GetDateTimeOffset(),
            again.GetProperty("actionItem").GetProperty("updatedAt").GetDateTimeOffset(),
            TimeSpan.FromMicroseconds(1)
        );
        var reopened = await SendAsync(
            client,
            HttpMethod.Post,
            $"{run.ActionPath("us-temp-03")}/reopen"
        );
        Assert.Equal("Open", Text(reopened.GetProperty("actionItem"), "status"));
        Assert.Equal(
            "Open",
            Text(
                (await GetFindingAsync(client, run, "us-temp-03")).GetProperty("actionItem"),
                "status"
            )
        );
    }

    [Fact]
    public async Task Another_tenants_inspections_or_establishments_findings_and_actions_are_not_found()
    {
        var a = await CompleteAsync();
        var b = await CompleteAsync();
        var siblingTarget = a.Target with
        {
            Establishment = await TestTenants.CreateEstablishmentAsync(db, a.Target.Tenant),
        };
        await AddDocumentAsync(db, siblingTarget, "Indexed", id: siblingTarget.Establishment);
        var sibling = await CompleteAsync(siblingTarget);
        using var clientA = Client(db.Api, a.Target);
        using var clientB = Client(db.Api, b.Target);
        var findingA = a.Id("us-temp-01");
        var actionA = a.Findings["us-temp-01"]
            .GetProperty("actionItem")
            .GetProperty("id")
            .GetGuid();

        var attempts = new (HttpClient Client, string Path, string Code)[]
        {
            // Tenant B through tenant A's route, and through its own establishment and inspection.
            (clientB, $"{a.FindingPath("us-temp-01")}/accept", "finding.not_found"),
            (clientB, $"{b.Path}/findings/{findingA}/accept", "finding.not_found"),
            (clientB, $"{a.ActionPath("us-temp-01")}/done", "action_item.not_found"),
            (clientB, $"{b.Path}/action-items/{actionA}/done", "action_item.not_found"),
            // Tenant A through another of its establishments, or another of its inspections.
            (
                clientA,
                $"{StartPath(sibling.Target)}/{a.Inspection}/findings/{findingA}/accept",
                "finding.not_found"
            ),
            (clientA, $"{sibling.Path}/findings/{findingA}/accept", "finding.not_found"),
            (clientA, $"{sibling.Path}/action-items/{actionA}/done", "action_item.not_found"),
        };
        foreach (var (client, path, code) in attempts)
        {
            using var refused = await client.PostAsync(path, null, Ct);
            await AssertProblemAsync(refused, HttpStatusCode.NotFound, code);
        }
        using var patched = await PatchAsync(
            clientB,
            a.FindingPath("us-temp-01"),
            new { severity = "minor" }
        );
        await AssertProblemAsync(patched, HttpStatusCode.NotFound, "finding.not_found");

        var untouched = await GetFindingAsync(clientA, a, "us-temp-01");
        Assert.Equal(
            ("Pending", "critical", "Open"),
            (
                untouched.GetProperty("reviewStatus").GetString(),
                untouched.GetProperty("severity").GetString(),
                Text(untouched.GetProperty("actionItem"), "status")
            )
        );
    }

    [Fact]
    public async Task An_inspection_that_is_not_completed_cannot_be_reviewed()
    {
        var target = await NewTargetAsync(db);
        using var client = Client(db.Api, target);
        var id = await StartAsync(client, target);

        using var refused = await client.PostAsync(
            $"{StartPath(target)}/{id}/findings/{Guid.NewGuid()}/accept",
            null,
            Ct
        );

        await AssertProblemAsync(refused, HttpStatusCode.Conflict, "inspection_not_completed");
    }

    [Fact]
    public async Task Without_inspections_review_a_reader_cannot_review()
    {
        var run = await CompleteAsync();
        var reader = Guid.NewGuid().ToString();
        await TestTenants.GrantAsync(
            db,
            run.Target.Tenant,
            reader,
            [.. Permissions.All.Where(name => name != Permissions.Inspections.Review)]
        );
        using var client = db.Api.CreateClient(
            ApiFactory.CreateToken(run.Target.Tenant.ToString(), reader)
        );

        using var accept = await client.PostAsync(
            $"{run.FindingPath("us-temp-01")}/accept",
            null,
            Ct
        );
        using var done = await client.PostAsync($"{run.ActionPath("us-temp-01")}/done", null, Ct);

        await AssertProblemAsync(accept, HttpStatusCode.Forbidden, "permission.denied");
        await AssertProblemAsync(done, HttpStatusCode.Forbidden, "permission.denied");
        var untouched = await GetFindingAsync(client, run, "us-temp-01");
        Assert.Equal(
            ("Pending", "Open"),
            (
                untouched.GetProperty("reviewStatus").GetString(),
                Text(untouched.GetProperty("actionItem"), "status")
            )
        );
    }

    [Fact]
    public async Task The_feedback_export_writes_one_line_per_rejected_or_edited_finding()
    {
        var run = await CompleteAsync();
        var other = await CompleteAsync();
        using var client = Client(db.Api, run.Target);
        await SendAsync(
            client,
            HttpMethod.Post,
            $"{run.FindingPath("us-temp-01")}/reject",
            new { note = "Wrong cooler; não é este." }
        );
        await SendAsync(client, HttpMethod.Post, $"{run.FindingPath("us-temp-02")}/accept");
        using (
            var edited = await PatchAsync(
                client,
                run.FindingPath("us-temp-03"),
                new { verdict = "non_compliant", severity = "major" }
            )
        )
        {
            await OkAsync(edited);
        }
        var dueOn = Text(run.Findings["us-temp-03"].GetProperty("actionItem"), "dueOn");

        var export = await SendAsync(client, HttpMethod.Post, $"{run.Path}/feedback-export");

        var path = Path.Combine(db.Api.FeedbackDirectory, $"{run.Inspection}.jsonl");
        Assert.Equal(
            (path, 2),
            (export.GetProperty("path").GetString(), export.GetProperty("examples").GetInt32())
        );
        var text = await File.ReadAllTextAsync(path, Ct);
        Assert.EndsWith("}\n", text);
        // Read as written: no \u escapes for the reviewer's accents or the degree sign.
        Assert.Contains("não é este", text);
        Assert.Contains("45°F", text);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement)
            .ToArray();
        Assert.Equal(2, lines.Length);
        var rejected = lines[0];
        Assert.Equal(
            ["itemId", "original", "correction", "note", "reviewer"],
            rejected.EnumerateObject().Select(property => property.Name)
        );
        Assert.Equal(
            ("us-temp-01", "Wrong cooler; não é este.", run.Target.Owner),
            (Text(rejected, "itemId"), Text(rejected, "note"), Text(rejected, "reviewer"))
        );
        // A rejection without an edit: the correction is what the inspection said.
        Assert.Equal(
            rejected.GetProperty("original").GetRawText(),
            rejected.GetProperty("correction").GetRawText()
        );
        Assert.Equal(
            ["verdict", "severity", "rationale", "actionText", "dueOn"],
            rejected.GetProperty("original").EnumerateObject().Select(property => property.Name)
        );
        var edit = lines[1];
        Assert.Equal(
            ("us-temp-03", (string?)null, run.Target.Owner),
            (Text(edit, "itemId"), Text(edit, "note"), Text(edit, "reviewer"))
        );
        Assert.Equal(
            ("insufficient_evidence", "critical", "Keep a cooling log.", dueOn),
            (
                Text(edit.GetProperty("original"), "verdict"),
                Text(edit.GetProperty("original"), "severity"),
                Text(edit.GetProperty("original"), "actionText"),
                Text(edit.GetProperty("original"), "dueOn")
            )
        );
        Assert.Equal(
            ("non_compliant", "major", "The log shows 45°F.", "Keep a cooling log."),
            (
                Text(edit.GetProperty("correction"), "verdict"),
                Text(edit.GetProperty("correction"), "severity"),
                Text(edit.GetProperty("correction"), "rationale"),
                Text(edit.GetProperty("correction"), "actionText")
            )
        );

        // Another tenant cannot export it, through either establishment, and nothing is written:
        // the file is gone first, so even a rewrite with the same bytes would show.
        File.Delete(path);
        using var otherClient = Client(db.Api, other.Target);
        using var foreign = await otherClient.PostAsync($"{run.Path}/feedback-export", null, Ct);
        using var throughOwn = await otherClient.PostAsync(
            $"{StartPath(other.Target)}/{run.Inspection}/feedback-export",
            null,
            Ct
        );
        await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "inspection.not_found");
        await AssertProblemAsync(throughOwn, HttpStatusCode.NotFound, "inspection.not_found");
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task The_feedback_export_exists_only_in_Development()
    {
        var run = await CompleteAsync();
        await using var production = db.Api.WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production")
        );
        using var client = Client(production, run.Target);
        await SendAsync(
            client,
            HttpMethod.Post,
            $"{run.FindingPath("us-temp-01")}/reject",
            new { note = "Wrong." }
        );

        using var refused = await client.PostAsync($"{run.Path}/feedback-export", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.False(
            File.Exists(Path.Combine(db.Api.FeedbackDirectory, $"{run.Inspection}.jsonl"))
        );
    }
}
