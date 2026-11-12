# 007. Verify every citation against the retrieved chunks

- **Status:** Accepted
- **Date:** 2026-11-11

## Context

A compliance answer is useful only if the user can check it against the regulation. An LLM asked to cite in free text can invent an article number or quote a passage that was never retrieved, and a user who acts on a fake citation is worse off than one who got no answer. Sometimes retrieval finds nothing relevant, and then the system should say so instead of answering from the model's general knowledge.

## Decision

- Each retrieved chunk enters the prompt with an ID, delimited as data.
- The model answers in structured JSON: `answer` plus `claims`, each claim listing the IDs of the chunks that support it.
- A deterministic guard drops any claim that cites an ID outside the retrieved set. If too little survives, the answer becomes "not enough basis in the regulation".
- If the re-ranker's best score is below a threshold, the system refuses before generation.

## Consequences

- Every claim the user sees points to a chunk that was retrieved, so the UI can show the article and the passage behind it.
- Citation precision becomes a deterministic check in the generation evals, alongside the LLM-as-judge metrics.
- Each answer goes through one extra validation step, and some answers become refusals. The score threshold and the minimum that must survive the guard are tuned with the generation evals.
- The guard checks that a cited chunk was retrieved. Whether that chunk supports the claim is measured separately, by the faithfulness eval.

## Alternatives considered

- **Free-text answers with citations requested in the prompt** — nothing checks the citations, so the model can invent one that looks right.
- **Post-hoc similarity check only** — a similarity score between the whole answer and the retrieved text cannot point to the claim that lacks support, so the system has nothing precise to drop.
