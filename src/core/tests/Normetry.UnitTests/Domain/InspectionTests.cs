using Normetry.Domain.Documents;
using Normetry.Domain.Inspections;
using Xunit;

namespace Normetry.UnitTests.Domain;

public sealed class InspectionTests
{
    // Late in the UTC day, so the due date counts from the UTC day of completion.
    private static readonly DateTimeOffset Now = new(
        2026,
        11,
        17,
        23,
        0,
        0,
        TimeSpan.FromHours(-3)
    );
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Establishment = Guid.NewGuid();
    private static readonly Guid Reviewer = Guid.NewGuid();

    private static readonly ThemeProgress[] Started =
    [
        new("personal_hygiene", 2, 0),
        new("temperature_control", 3, 0),
    ];

    private static ThemeProgress[] Snapshot(int hygiene, int temperature) =>
        [new("personal_hygiene", 2, hygiene), new("temperature_control", 3, temperature)];

    private static FindingResult Result(
        string verdict = "non_compliant",
        ProposedAction? action = null,
        string theme = "temperature_control",
        string rationale = "The log shows 45°F twice."
    ) =>
        new(
            "us-temp-01",
            theme,
            "Cold holding at 41°F or below",
            "critical",
            verdict,
            "high",
            rationale,
            [new("FDA Food Code", "2022", "3-501.16", "en", "Text.", "41°F", 0, 4)],
            [
                new(
                    Guid.NewGuid(),
                    "Cooler log",
                    DocumentType.Log,
                    1,
                    "Week 2",
                    "en",
                    "45°F",
                    "45°F",
                    null,
                    null
                ),
            ],
            action
        );

    private static Inspection Requested() =>
        Inspection.Request(Tenant, Establishment, "user-sub", Now);

    private static Inspection Running()
    {
        var inspection = Requested();
        inspection.Start(5, Started, Now);
        return inspection;
    }

    private static Finding Completed(ProposedAction? action, string verdict = "non_compliant")
    {
        var inspection = Running();
        inspection.Complete(10, 20, 0.01m, [Result(verdict, action)], Now);
        return Assert.Single(inspection.Findings);
    }

    [Fact]
    public void Text_limits_count_characters_like_the_message_contract()
    {
        // 4000 emoji are 8000 UTF-16 units; MessageJson accepts them, so the domain must too.
        static string Emoji(int count) => string.Concat(Enumerable.Repeat("🌡", count));
        var inspection = Running();

        inspection.Complete(10, 20, 0.01m, [Result(rationale: Emoji(4000))], Now);

        Assert.Equal(8000, Assert.Single(inspection.Findings).Rationale.Length);
        Assert.Throws<ArgumentException>(() =>
            Running().Complete(10, 20, 0.01m, [Result(rationale: Emoji(4001))], Now)
        );
    }

    [Fact]
    public void A_new_inspection_is_requested_by_a_sub_with_no_progress()
    {
        var inspection = Requested();

        Assert.Equal(
            (InspectionStatus.Requested, "user-sub", Now, (DateTimeOffset?)null, 0, 0),
            (
                inspection.Status,
                inspection.RequestedBy,
                inspection.RequestedAt,
                inspection.StartedAt,
                inspection.ItemsTotal,
                inspection.ItemsEvaluated
            )
        );
        Assert.Empty(inspection.Themes);
        Assert.Throws<ArgumentException>(() => Inspection.Request(Tenant, Establishment, " ", Now));
        Assert.Throws<ArgumentException>(() =>
            Inspection.Request(Guid.Empty, Establishment, "user-sub", Now)
        );
    }

    [Fact]
    public void Starting_moves_it_to_running_with_the_first_snapshot_once()
    {
        var inspection = Requested();

        Assert.True(inspection.Start(5, Started, Now.AddMinutes(1)));
        Assert.False(inspection.Start(5, Started, Now.AddMinutes(2)));

        Assert.Equal(
            (InspectionStatus.Running, Now.AddMinutes(1), 5, 0),
            (
                inspection.Status,
                inspection.StartedAt,
                inspection.ItemsTotal,
                inspection.ItemsEvaluated
            )
        );
        Assert.Equal(Started, inspection.Themes);
    }

    [Fact]
    public void A_progress_before_the_start_also_moves_it_to_running()
    {
        var inspection = Requested();

        Assert.True(inspection.Progress(1, 5, Snapshot(1, 0), Now));

        Assert.Equal(
            (InspectionStatus.Running, Now, 1),
            (inspection.Status, inspection.StartedAt, inspection.ItemsEvaluated)
        );
        // The late start changes nothing: its snapshot has fewer items evaluated.
        Assert.False(inspection.Start(5, Started, Now.AddMinutes(1)));
        Assert.Equal(Snapshot(1, 0), inspection.Themes);
    }

    [Fact]
    public void The_snapshot_is_replaced_only_by_one_with_more_items_evaluated()
    {
        var inspection = Running();

        Assert.True(inspection.Progress(3, 5, Snapshot(2, 1), Now));
        Assert.False(inspection.Progress(3, 5, Snapshot(1, 2), Now)); // as many
        Assert.False(inspection.Progress(2, 5, Snapshot(2, 0), Now)); // a late retry

        Assert.Equal(3, inspection.ItemsEvaluated);
        Assert.Equal(Snapshot(2, 1), inspection.Themes);
    }

    [Fact]
    public void Progress_after_the_end_changes_nothing()
    {
        var failed = Running();
        failed.Fail("error", "Boom.", Now);
        var completed = Running();
        completed.Complete(1, 1, 0m, [], Now);

        foreach (var inspection in new[] { failed, completed })
        {
            Assert.False(inspection.Progress(4, 5, Snapshot(2, 2), Now));
            Assert.Equal(0, inspection.ItemsEvaluated);
        }
    }

    [Fact]
    public void Completing_stores_the_usage_and_the_findings_with_their_actions_once()
    {
        var inspection = Running();

        Assert.True(
            inspection.Complete(
                1200,
                300,
                0.0123m,
                [Result(action: new("Recalibrate the cooler.", 7)), Result("compliant")],
                Now.AddMinutes(5)
            )
        );
        Assert.False(inspection.Complete(1, 1, 1m, [Result()], Now.AddMinutes(6)));

        Assert.Equal(
            (InspectionStatus.Completed, Now.AddMinutes(5), 1200, 300, 0.0123m),
            (
                inspection.Status,
                inspection.CompletedAt,
                inspection.InputTokens,
                inspection.OutputTokens,
                inspection.CostUsd
            )
        );
        Assert.Equal(2, inspection.Findings.Count);
        var (nonCompliant, compliant) = (inspection.Findings.First(), inspection.Findings.Last());
        Assert.Equal(
            (Tenant, inspection.Id, ReviewStatus.Pending, (FindingOriginal?)null),
            (
                nonCompliant.TenantId,
                nonCompliant.InspectionId,
                nonCompliant.ReviewStatus,
                nonCompliant.Original
            )
        );
        Assert.Equal("41°F", Assert.Single(nonCompliant.RegulationCitations).Quote);
        Assert.Equal("Cooler log", Assert.Single(nonCompliant.EvidenceCitations).DocumentTitle);
        var action = nonCompliant.ActionItem!;
        // Completed at 02:05 UTC on Nov 18: due 7 days after that UTC day.
        Assert.Equal(
            ("Recalibrate the cooler.", new DateOnly(2026, 11, 25), ActionItemStatus.Open),
            (action.Text, action.DueOn, action.Status)
        );
        Assert.Equal(
            (nonCompliant.TenantId, nonCompliant.Id, (Guid?)null),
            (action.TenantId, action.FindingId, action.UpdatedBy)
        );
        Assert.Null(compliant.ActionItem);
    }

    [Fact]
    public void A_late_completion_replaces_a_failure()
    {
        var inspection = Running();
        Assert.True(inspection.TimeOut(TimeSpan.FromMinutes(30), Now.AddMinutes(30)));

        Assert.True(inspection.Complete(1, 2, 0.5m, [Result()], Now.AddMinutes(40)));

        Assert.Equal(
            (InspectionStatus.Completed, (string?)null, (string?)null, Now.AddMinutes(40)),
            (
                inspection.Status,
                inspection.FailureReasonCode,
                inspection.FailureReason,
                inspection.CompletedAt
            )
        );
        Assert.Single(inspection.Findings);
    }

    [Fact]
    public void A_completion_is_accepted_even_when_the_start_never_arrived()
    {
        var inspection = Requested();

        Assert.True(inspection.Complete(1, 1, 0m, [Result()], Now));
        Assert.Equal(InspectionStatus.Completed, inspection.Status);
    }

    [Theory]
    [InlineData("severity")]
    [InlineData("verdict")]
    [InlineData("confidence")]
    [InlineData("theme")]
    public void A_finding_with_an_unknown_code_fails_the_whole_completion(string field)
    {
        var inspection = Running();
        var bad = field switch
        {
            "severity" => Result() with { Severity = "huge" },
            "verdict" => Result() with { Verdict = "maybe" },
            "confidence" => Result() with { Confidence = "certain" },
            _ => Result(theme: "parking"),
        };

        Assert.Throws<ArgumentException>(() => inspection.Complete(1, 1, 0m, [Result(), bad], Now));
        Assert.Equal(InspectionStatus.Running, inspection.Status);
        Assert.Empty(inspection.Findings);
    }

    [Fact]
    public void Failing_works_from_requested_or_running_only_and_truncates_the_reason()
    {
        var requested = Requested();
        var running = Running();
        var completed = Running();
        completed.Complete(1, 1, 0m, [], Now);

        Assert.True(requested.Fail("no_documents", "Nothing indexed.", Now));
        Assert.True(running.Fail("error", new string('x', 600), Now.AddMinutes(1)));
        Assert.False(running.Fail("budget_exceeded", "Later.", Now.AddMinutes(2)));
        Assert.False(completed.Fail("error", "Too late.", Now));

        Assert.Equal(
            (InspectionStatus.Failed, "no_documents", "Nothing indexed.", Now),
            (
                requested.Status,
                requested.FailureReasonCode,
                requested.FailureReason,
                requested.CompletedAt
            )
        );
        Assert.Equal(
            ("error", Inspection.FailureReasonMaxLength, Now.AddMinutes(1)),
            (running.FailureReasonCode, running.FailureReason!.Length, running.CompletedAt)
        );
        Assert.Equal(InspectionStatus.Completed, completed.Status);
    }

    [Fact]
    public void A_long_failure_reason_is_cut_to_the_limit_in_characters_without_splitting_one()
    {
        // The emoji is two UTF-16 units: cutting 500 units would end inside it.
        var kept = new string('x', Inspection.FailureReasonMaxLength - 1) + "🌡";
        var running = Running();

        running.Fail(InspectionFailureReasons.Error, kept + " and more.", Now);

        Assert.Equal(kept, running.FailureReason);
    }

    [Fact]
    public void Only_a_run_or_a_request_older_than_the_timeout_times_out()
    {
        var timeout = TimeSpan.FromMinutes(30);
        var requested = Requested();
        var running = Running();

        Assert.False(requested.TimeOut(timeout, Now.AddMinutes(29)));
        Assert.True(requested.TimeOut(timeout, Now.AddMinutes(30)));
        Assert.False(requested.TimeOut(timeout, Now.AddMinutes(31)));
        Assert.False(running.TimeOut(timeout, Now.AddMinutes(29)));
        Assert.True(running.TimeOut(timeout, Now.AddMinutes(30)));
        Assert.False(running.TimeOut(timeout, Now.AddMinutes(31)));

        Assert.Equal(
            (InspectionStatus.Failed, InspectionFailureReasons.TimedOut),
            (requested.Status, requested.FailureReasonCode)
        );
        Assert.Equal(
            (InspectionStatus.Failed, InspectionFailureReasons.TimedOut),
            (running.Status, running.FailureReasonCode)
        );
    }

    [Fact]
    public void Accepting_records_the_reviewer_and_the_time()
    {
        var finding = Completed(action: null);

        finding.Accept(Reviewer, Now);

        Assert.Equal(
            (ReviewStatus.Accepted, (Guid?)Reviewer, (DateTimeOffset?)Now, (string?)null),
            (finding.ReviewStatus, finding.ReviewedBy, finding.ReviewedAt, finding.ReviewNote)
        );
        Assert.Throws<ArgumentException>(() => finding.Accept(Guid.Empty, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_rejection_needs_a_note(string note)
    {
        var finding = Completed(action: null);

        Assert.Throws<ArgumentException>(() => finding.Reject(Reviewer, note, Now));
        Assert.Throws<ArgumentException>(() =>
            finding.Reject(Reviewer, new string('x', Finding.ReviewNoteMaxLength + 1), Now)
        );
        Assert.Equal(ReviewStatus.Pending, finding.ReviewStatus);
    }

    [Fact]
    public void The_latest_decision_wins()
    {
        var finding = Completed(action: null);

        finding.Reject(Reviewer, " The log is from another cooler. ", Now);
        Assert.Equal(
            (ReviewStatus.Rejected, "The log is from another cooler."),
            (finding.ReviewStatus, finding.ReviewNote)
        );

        var other = Guid.NewGuid();
        finding.Accept(other, Now.AddMinutes(1));
        Assert.Equal(
            (
                ReviewStatus.Accepted,
                (Guid?)other,
                (DateTimeOffset?)Now.AddMinutes(1),
                (string?)null
            ),
            (finding.ReviewStatus, finding.ReviewedBy, finding.ReviewedAt, finding.ReviewNote)
        );
    }

    [Fact]
    public void The_first_edit_keeps_the_inspections_values_and_later_ones_keep_them_too()
    {
        var finding = Completed(new("Recalibrate the cooler.", 7));
        var dueOn = finding.ActionItem!.DueOn;

        finding.Edit(Reviewer, new(Verdict: "insufficient_evidence", Rationale: " Unclear. "), Now);
        finding.Edit(Reviewer, new(Severity: "minor", DueOn: dueOn.AddDays(3)), Now.AddMinutes(1));

        Assert.Equal(
            new FindingOriginal(
                "non_compliant",
                "critical",
                "The log shows 45°F twice.",
                "Recalibrate the cooler.",
                dueOn
            ),
            finding.Original
        );
        Assert.Equal(
            (ReviewStatus.Edited, "insufficient_evidence", "minor", "Unclear.", "high"),
            (
                finding.ReviewStatus,
                finding.Verdict,
                finding.Severity,
                finding.Rationale,
                finding.Confidence
            )
        );
        Assert.Equal(
            ("Recalibrate the cooler.", dueOn.AddDays(3), (Guid?)Reviewer, Now.AddMinutes(1)),
            (
                finding.ActionItem!.Text,
                finding.ActionItem.DueOn,
                finding.ActionItem.UpdatedBy,
                finding.ActionItem.UpdatedAt!.Value
            )
        );
    }

    [Fact]
    public void An_edit_after_a_rejection_clears_the_note()
    {
        var finding = Completed(action: null);
        finding.Reject(Reviewer, "Wrong.", Now);

        finding.Edit(Reviewer, new(Verdict: "compliant"), Now);

        Assert.Equal(
            (ReviewStatus.Edited, (string?)null),
            (finding.ReviewStatus, finding.ReviewNote)
        );
    }

    [Fact]
    public void Editing_the_action_of_a_finding_with_none_creates_it_and_needs_both_fields()
    {
        var finding = Completed(action: null, verdict: "insufficient_evidence");
        var dueOn = new DateOnly(2026, 12, 1);

        Assert.Throws<ArgumentException>(() =>
            finding.Edit(Reviewer, new(ActionText: "Keep a log."), Now)
        );
        Assert.Throws<ArgumentException>(() => finding.Edit(Reviewer, new(DueOn: dueOn), Now));
        Assert.Equal(
            (ReviewStatus.Pending, (FindingOriginal?)null),
            (finding.ReviewStatus, finding.Original)
        );

        finding.Edit(Reviewer, new(ActionText: "Keep a log.", DueOn: dueOn), Now);

        var action = finding.ActionItem!;
        Assert.Equal(
            ("Keep a log.", dueOn, ActionItemStatus.Open, (Guid?)Reviewer, (DateTimeOffset?)Now),
            (action.Text, action.DueOn, action.Status, action.UpdatedBy, action.UpdatedAt)
        );
        Assert.Null(finding.Original!.ActionText);
        Assert.Null(finding.Original.DueOn);
    }

    [Fact]
    public void An_edit_off_the_rules_changes_nothing()
    {
        var finding = Completed(new("Recalibrate the cooler.", 7));
        var tooLongRationale = new string('x', Finding.EditedRationaleMaxLength + 1);
        var tooLongAction = new string('x', ActionItem.EditedTextMaxLength + 1);

        foreach (
            var edit in new FindingEdit[]
            {
                new(),
                new(Verdict: "maybe", Rationale: "Fine."),
                new(Severity: "huge"),
                new(Rationale: " "),
                new(Rationale: tooLongRationale),
                new(Verdict: "compliant", ActionText: tooLongAction),
                new(ActionText: ""),
            }
        )
        {
            Assert.Throws<ArgumentException>(() => finding.Edit(Reviewer, edit, Now));
        }
        Assert.Throws<ArgumentException>(() =>
            finding.Edit(Guid.Empty, new(Verdict: "compliant"), Now)
        );

        Assert.Equal(
            (
                ReviewStatus.Pending,
                "non_compliant",
                "The log shows 45°F twice.",
                (FindingOriginal?)null
            ),
            (finding.ReviewStatus, finding.Verdict, finding.Rationale, finding.Original)
        );
        Assert.Equal("Recalibrate the cooler.", finding.ActionItem!.Text);
        // The limits a reviewer gets, exactly.
        finding.Edit(
            Reviewer,
            new(Rationale: tooLongRationale[1..], ActionText: tooLongAction[1..]),
            Now
        );
        Assert.Equal(ReviewStatus.Edited, finding.ReviewStatus);
    }

    [Fact]
    public void An_action_is_marked_done_and_reopened_by_whoever_changed_it_last()
    {
        var action = Completed(new("Recalibrate the cooler.", 7)).ActionItem!;
        var other = Guid.NewGuid();

        action.MarkDone(Reviewer, Now);
        action.MarkDone(other, Now.AddMinutes(1)); // already done: nothing changes
        Assert.Equal(
            (ActionItemStatus.Done, (Guid?)Reviewer, (DateTimeOffset?)Now),
            (action.Status, action.UpdatedBy, action.UpdatedAt)
        );

        action.Reopen(other, Now.AddMinutes(2));
        Assert.Equal(
            (ActionItemStatus.Open, (Guid?)other, (DateTimeOffset?)Now.AddMinutes(2)),
            (action.Status, action.UpdatedBy, action.UpdatedAt)
        );
        Assert.Throws<ArgumentException>(() => action.MarkDone(Guid.Empty, Now));
    }
}
