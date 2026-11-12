---
name: adr
description: Writes or updates an Architecture Decision Record in docs/adr/ using Normetry's format, and keeps the README "Engineering decisions" table in sync. Use when a significant technical decision is made, reversed or superseded, or when asked to document a decision.
---

# Architecture Decision Record

## When

A decision deserves an ADR when reversing it later would be costly, or when an interviewer could ask "why did you do it this way?": runtime or framework choice, data ownership, security model, messaging, a library with licensing or cost impact, a model choice driven by evals.

## File

`docs/adr/NNN-kebab-case-title.md`, where `NNN` is the next free 3-digit number. ADRs 001–008 are already summarized in the README "Engineering decisions" table: when writing one of them, reuse its number and expand its row.

```markdown
# NNN. Title in imperative form

- **Status:** Proposed | Accepted | Superseded by [NNN](NNN-title.md)
- **Date:** YYYY-MM-DD

## Context

The forces at play: requirements, constraints, what hurts today. Facts, not the solution.

## Decision

What we do, stated plainly.

## Consequences

What gets easier, what gets harder, the trade-off accepted, and when we would revisit it.

## Alternatives considered

- **Option** — why it lost.
```

## Rules

- English, and short: aim for one screen.
- Never rewrite the decision of an accepted ADR. Supersede it with a new ADR and update the old one's status line.
- After writing, update the README table: link the ADR number to its file and keep Decision / Why / Trade-off consistent.
- Numbers (benchmarks, eval results) come from a real run, with a link to the report.
