import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { expectNoAxeViolations } from "../test/fakes";
import {
  ChecklistRow,
  FilterSelect,
  QuotedPassage,
  RadialProgress,
  StatusPill,
  Stepper,
  TextAreaField,
  TimeBadge,
} from "./primitives";
import { pillVariant } from "./statuses";

/** What a screen reader gets from an element: its text without the aria-hidden parts. */
function spoken(element: Element) {
  const copy = element.cloneNode(true) as Element;
  copy.querySelectorAll('[aria-hidden="true"]').forEach((hidden) => hidden.remove());
  return copy.textContent;
}

describe("pillVariant", () => {
  it.each([
    ["ingestion", "Indexed", "done"],
    ["inspection", "Requested", "waiting"],
    ["inspection", "Running", "processing"],
    ["inspection", "Completed", "done"],
    ["inspection", "Failed", "attention"],
    ["verdict", "compliant", "compliant"],
    ["verdict", "non_compliant", "nonCompliant"],
    ["verdict", "insufficient_evidence", "attention"],
    ["review", "Pending", "waiting"],
    ["review", "Accepted", "done"],
    ["review", "Edited", "edited"],
    ["review", "Rejected", "rejected"],
    ["verdict", "not_applicable", "waiting"],
    ["review", "Escalated", "waiting"],
  ] as const)("%s %s shows as %s", (kind, status, variant) => {
    expect(pillVariant(kind, status)).toBe(variant);
  });
});

describe("StatusPill", () => {
  it("keeps its label and a glyph; non-compliant text stays white on the danger fill", () => {
    render(<StatusPill variant="nonCompliant" label="Non-compliant" />);
    const pill = screen.getByText("Non-compliant");

    expect(pill).toHaveClass("border-danger", "bg-danger/10", "text-text-primary");
    expect(pill.querySelector("svg")).toHaveClass("text-danger");
  });
});

describe("Stepper", () => {
  it("marks the steps before the current one done and the current one with aria-current", () => {
    render(
      <Stepper
        label="Inspection steps"
        steps={["Analysis", "Findings", "Action plan"]}
        current={1}
        doneLabel="Done:"
      />,
    );
    const steps = within(screen.getByRole("list", { name: "Inspection steps" })).getAllByRole(
      "listitem",
    );

    expect(steps.map(spoken)).toEqual(["Done: Analysis", "Findings", "Action plan"]);
    expect(steps.map((step) => step.getAttribute("aria-current"))).toEqual([null, "step", null]);
  });

  it("shows only the current label below 640px, keeping the others for screen readers", () => {
    render(
      <Stepper
        label="Steps"
        steps={["Analysis", "Findings", "Action plan"]}
        current={1}
        doneLabel="Done:"
      />,
    );

    expect(screen.getByText("Findings")).not.toHaveClass("sr-only");
    for (const other of ["Analysis", "Action plan"])
      expect(screen.getByText(other)).toHaveClass("sr-only", "sm:not-sr-only");
  });
});

describe("RadialProgress", () => {
  const ring = (value: number, max: number) =>
    render(
      <RadialProgress
        value={value}
        max={max}
        label="Requirements checked"
        valueText={`${value} of ${max} requirements checked`}
        title="Inspection in progress…"
        lines={[`${value} of ${max} requirements checked`]}
      />,
    );

  it("is a progressbar that reads the count, with the percentage rounded down", () => {
    ring(27, 43);
    const bar = screen.getByRole("progressbar", { name: "Requirements checked" });

    expect(bar).toHaveAttribute("aria-valuenow", "27");
    expect(bar).toHaveAttribute("aria-valuemax", "43");
    expect(bar).toHaveAttribute("aria-valuetext", "27 of 43 requirements checked");
    expect(bar).toHaveTextContent("62%");
    expect(bar).not.toHaveAttribute("aria-live");
    expect(screen.getByText("Inspection in progress…")).toBeVisible();
  });

  it.each([
    [29, 100, "29%"],
    [199, 200, "99%"],
    [43, 43, "100%"],
  ])("%i of %i shows %s", (value, max, percent) => {
    ring(value, max);

    expect(screen.getByRole("progressbar")).toHaveTextContent(percent);
  });

  it("draws no arc before the totals are known", () => {
    ring(0, 0);
    const bar = screen.getByRole("progressbar");

    expect(bar).toHaveTextContent("0%");
    expect(bar.querySelectorAll("circle")).toHaveLength(1);
    // Indeterminate: no value until there is a total.
    expect(bar).not.toHaveAttribute("aria-valuenow");
    expect(bar).not.toHaveAttribute("aria-valuemax");
  });
});

describe("ChecklistRow", () => {
  it("reads its state before the label, and its count", () => {
    render(
      <ul aria-label="Themes">
        <ChecklistRow progress="done" state="Done:" label="Personal hygiene" count="6 of 6" />
        <ChecklistRow progress="active" state="In progress:" label="Waste" count="1 of 3" />
        <ChecklistRow progress="pending" state="Not started:" label="Pest control" />
      </ul>,
    );

    expect(
      within(screen.getByRole("list", { name: "Themes" }))
        .getAllByRole("listitem")
        .map((row) => row.textContent),
    ).toEqual([
      "Done: Personal hygiene6 of 6",
      "In progress: Waste1 of 3",
      "Not started: Pest control",
    ]);
  });
});

describe("FilterSelect", () => {
  it("is named by its label and reports the choice", async () => {
    const onChange = vi.fn<(value: string) => void>();
    render(
      <FilterSelect
        label="Filter by verdict"
        value=""
        onChange={(event) => onChange(event.target.value)}
        options={[
          { value: "", label: "All verdicts" },
          { value: "non_compliant", label: "Non-compliant (6)" },
        ]}
      />,
    );

    await userEvent.selectOptions(
      screen.getByRole("combobox", { name: "Filter by verdict" }),
      "Non-compliant (6)",
    );

    expect(onChange).toHaveBeenCalledWith("non_compliant");
  });
});

describe("TextAreaField", () => {
  it("is labelled, described by its hint and error, and invalid with an error", () => {
    render(
      <TextAreaField
        label="Reason"
        hint="Say why this finding is wrong."
        error="Enter a reason."
        maxLength={1000}
      />,
    );
    const field = screen.getByRole("textbox", { name: "Reason" });

    expect(field.tagName).toBe("TEXTAREA");
    expect(field).toHaveAccessibleDescription("Say why this finding is wrong. Enter a reason.");
    expect(field).toHaveAttribute("aria-invalid", "true");
    expect(field).toHaveAttribute("maxlength", "1000");
  });
});

describe("TimeBadge", () => {
  it("is a time element for its date", () => {
    render(<TimeBadge dateTime="2026-11-23" label="Due in 7 days" />);

    expect(screen.getByText("Due in 7 days").tagName).toBe("TIME");
    expect(screen.getByText("Due in 7 days")).toHaveAttribute("datetime", "2026-11-23");
  });
});

describe("QuotedPassage", () => {
  const text =
    "Os alimentos devem ser conservados sob refrigeração a temperaturas inferiores a 5ºC.";
  const quote = "conservados sob refrigeração a temperaturas inferiores a 5ºC";
  const citation = {
    text,
    quote,
    quoteStart: text.indexOf(quote),
    quoteEnd: text.indexOf(quote) + quote.length,
    language: "pt",
  };

  it("shows the quote alone, marked, in its language, between the hidden markers", () => {
    const { container } = render(<QuotedPassage citation={citation} mode="quote" />);
    const block = container.querySelector("blockquote");

    expect(block).toHaveAttribute("lang", "pt");
    expect(block?.querySelector("mark")).toHaveTextContent(quote);
    expect(block).not.toHaveTextContent("Os alimentos");
    expect([...(block?.children ?? [])].map((child) => [child.tagName, child.textContent])).toEqual(
      [
        ["SPAN", "Quoted passage:"],
        ["MARK", quote],
        ["SPAN", "End of quoted passage."],
      ],
    );
    expect(screen.getByText("Quoted passage:")).toHaveAttribute("lang", "en");
  });

  it("falls back on the stored quote when the offsets don't map", () => {
    const { container } = render(
      <QuotedPassage
        citation={{ ...citation, quote: "inferiores a 5ºC", quoteStart: 3, quoteEnd: 999 }}
        mode="quote"
      />,
    );

    expect(container.querySelector("mark")).toHaveTextContent(/^inferiores a 5ºC$/);
  });

  it("shows the whole passage with the quote marked, or unmarked when the offsets don't map", () => {
    const { container, rerender } = render(<QuotedPassage citation={citation} mode="passage" />);

    expect(container.firstElementChild).toHaveAttribute("lang", "pt");
    expect(container.firstElementChild).toHaveTextContent("Os alimentos devem ser");
    expect(container.querySelector("mark")).toHaveTextContent(quote);

    rerender(
      <QuotedPassage citation={{ ...citation, quoteStart: null, quoteEnd: null }} mode="passage" />,
    );

    expect(container.querySelector("mark")).toBeNull();
    expect(container.firstElementChild).toHaveTextContent(text);
  });
});

it("has no axe violations", async () => {
  render(
    <main>
      <Stepper
        label="Inspection steps"
        steps={["Analysis", "Findings"]}
        current={0}
        doneLabel="Done:"
      />
      <RadialProgress
        value={3}
        max={7}
        label="Requirements checked"
        valueText="3 of 7 requirements checked"
        title="Inspection in progress…"
        lines={["3 of 7 requirements checked"]}
      />
      <ul aria-label="Themes">
        <ChecklistRow progress="active" state="In progress:" label="Waste" count="1 of 3" />
      </ul>
      <StatusPill variant="compliant" label="Compliant" />
      <StatusPill variant="rejected" label="Rejected" />
      <FilterSelect label="Filter by theme" options={[{ value: "", label: "All themes" }]} />
      <TextAreaField label="Rationale" />
      <TimeBadge dateTime="2026-11-18" label="Due tomorrow" />
      <QuotedPassage
        citation={{ text: "a b c", quote: "b", quoteStart: 2, quoteEnd: 3, language: "pt" }}
        mode="quote"
      />
    </main>,
  );

  await expectNoAxeViolations();
});
