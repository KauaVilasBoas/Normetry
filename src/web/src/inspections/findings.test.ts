import { describe, expect, it } from "vitest";
import { calendarDate, localDate } from "../copy";
import { fakeFinding } from "../test/fakes";
import {
  actionPlan,
  draftOf,
  dueBadge,
  editChanges,
  editErrors,
  findingsCaption,
  originalValues,
  sortFindings,
  themeOptions,
  verdictOptions,
} from "./findings";

const action = (dueOn: string, status = "Open") => ({
  id: `a-${dueOn}`,
  text: "Record the walk-in temperature twice a day.",
  dueOn,
  status,
  updatedBy: null,
  updatedAt: null,
});

describe("due dates", () => {
  // Late evening: a UTC parse would already be on the next day east of UTC, or the day before
  // west of it.
  const now = new Date(2026, 10, 16, 23, 30);

  it("parses a date with no time as a local date", () => {
    const date = localDate("2026-11-23");
    expect([date.getFullYear(), date.getMonth(), date.getDate(), date.getHours()]).toEqual([
      2026, 10, 23, 0,
    ]);
    expect(calendarDate("2026-11-23", now)).toBe("Nov 23");
    expect(calendarDate("2027-01-04", now)).toBe("Jan 4, 2027");
  });

  it("counts days from today, and is overdue only after the due day", () => {
    expect(dueBadge("2026-11-23", now)).toEqual({ overdue: false, label: "Due in 7 days" });
    expect(dueBadge("2026-11-17", now)).toEqual({ overdue: false, label: "Due tomorrow" });
    expect(dueBadge("2026-11-16", now)).toEqual({ overdue: false, label: "Due today" });
    expect(dueBadge("2026-11-15", now)).toEqual({ overdue: true, label: "Overdue" });
  });

  it("counts whole days across a daylight saving change", () => {
    expect(dueBadge("2026-04-02", new Date(2026, 2, 26, 12))).toMatchObject({
      label: "Due in 7 days",
    });
  });
});

describe("findings order and filters", () => {
  const findings = [
    fakeFinding({ id: "minor", severity: "minor", title: "A" }),
    fakeFinding({ id: "compliant", severity: "critical", verdict: "compliant", title: "A" }),
    fakeFinding({ id: "z", severity: "critical", title: "Z" }),
    fakeFinding({ id: "a", severity: "critical", title: "a", theme: "waste" }),
    fakeFinding({ id: "insufficient", severity: "critical", verdict: "insufficient_evidence" }),
    fakeFinding({ id: "major", severity: "major", reviewStatus: "Accepted" }),
  ];

  it("sorts by severity, then verdict, then title; the other way keeps the rest", () => {
    expect(sortFindings(findings).map((f) => f.id)).toEqual([
      "a",
      "z",
      "insufficient",
      "compliant",
      "major",
      "minor",
    ]);
    expect(sortFindings(findings, true).map((f) => f.id)).toEqual([
      "minor",
      "major",
      "a",
      "z",
      "insufficient",
      "compliant",
    ]);
  });

  it("counts every verdict and the themes present, in checklist order", () => {
    expect(verdictOptions(findings).map((o) => o.label)).toEqual([
      "All verdicts",
      "Non-compliant (4)",
      "Insufficient evidence (1)",
      "Compliant (1)",
    ]);
    expect(themeOptions(findings).map((o) => o.label)).toEqual([
      "All themes",
      "Temperature control (5)",
      "Waste (1)",
    ]);
    expect(findingsCaption(findings)).toBe("6 findings · 5 pending review");
    expect(findingsCaption([fakeFinding({ reviewStatus: "Edited" })])).toBe(
      "1 finding · All reviewed",
    );
  });
});

describe("action plan", () => {
  it("leaves out rejected findings and sorts by due date, severity, then title", () => {
    const plan = actionPlan([
      fakeFinding({ id: "late", actionItem: action("2026-12-01", "Done") }),
      fakeFinding({ id: "minor", severity: "minor", actionItem: action("2026-11-20") }),
      fakeFinding({ id: "b", title: "B", actionItem: action("2026-11-20") }),
      fakeFinding({ id: "a", title: "A", actionItem: action("2026-11-20") }),
      fakeFinding({ id: "rejected", reviewStatus: "Rejected", actionItem: action("2026-11-01") }),
      fakeFinding({ id: "none" }),
    ]);
    expect(plan.map((f) => f.id)).toEqual(["a", "b", "minor", "late"]);
  });
});

describe("edit", () => {
  const withAction = fakeFinding({ actionItem: action("2026-11-23") });
  const without = fakeFinding();

  it("sends only the fields that changed, trimmed", () => {
    expect(editChanges(withAction, draftOf(withAction))).toEqual({});
    expect(
      editChanges(withAction, {
        ...draftOf(withAction),
        severity: "major",
        rationale: ` ${withAction.rationale}\n`,
        dueOn: "2026-11-30",
      }),
    ).toEqual({ severity: "major", dueOn: "2026-11-30" });
    expect(
      editChanges(without, { ...draftOf(without), actionText: " Fix it. ", dueOn: "2026-12-01" }),
    ).toEqual({ actionText: "Fix it.", dueOn: "2026-12-01" });
  });

  it("needs a rationale, and an action with its due date", () => {
    expect(editErrors(without, draftOf(without))).toEqual({});
    expect(editErrors(without, { ...draftOf(without), rationale: "  " })).toEqual({
      rationale: "Enter a rationale.",
    });
    expect(editErrors(without, { ...draftOf(without), actionText: "Fix it." })).toEqual({
      dueOn: "Choose a due date.",
    });
    expect(editErrors(without, { ...draftOf(without), dueOn: "2026-12-01" })).toEqual({
      actionText: "Describe the action.",
    });
    expect(editErrors(withAction, { ...draftOf(withAction), actionText: "", dueOn: "" })).toEqual({
      actionText: "Describe the action.",
      dueOn: "Choose a due date.",
    });
  });

  it("lists only the original values an edit changed", () => {
    const edited = fakeFinding({
      verdict: "insufficient_evidence",
      actionItem: action("2026-11-23"),
      original: {
        verdict: "non_compliant",
        severity: "critical",
        rationale: without.rationale,
        actionText: null,
        dueOn: null,
      },
    });
    expect(originalValues(edited).map(({ label, value }) => [label, value])).toEqual([
      ["Verdict", "Non-compliant"],
      ["Action", "No action needed."],
    ]);
    expect(originalValues(without)).toEqual([]);
  });
});
