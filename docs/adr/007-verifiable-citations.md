# 007. Verify every citation against the retrieved chunks

- **Status:** Accepted (amended 2026-11-13: each citation carries a literal quote, checked at runtime)
- **Date:** 2026-11-11

## Context

A compliance answer is useful only if the user can check it against the regulation. An LLM asked to cite in free text can invent an article number or quote a passage that was never retrieved, and a user who acts on a fake citation is worse off than one who got no answer. Sometimes retrieval finds nothing relevant, and then the system should say so instead of answering from the model's general knowledge.

Checking only that a cited chunk was retrieved leaves a gap: the model can attach a real chunk to a statement that chunk does not make. Nobody on the team has legal training, so support has to be checked mechanically, not judged.

## Decision

- Each retrieved chunk enters the prompt with an ID, delimited as data.
- The model answers in structured JSON: `answer` plus `claims`, each claim listing the IDs of the chunks that support it.
- A deterministic guard drops any claim that cites an ID outside the retrieved set. If too little survives, the answer becomes "not enough basis in the regulation".
- If the re-ranker's best score is below a threshold, the system refuses before generation.
- Amendment: for each cited ID the claim also gives a literal quote from that chunk. The guard drops the whole claim when any quote, after the same text normalization the corpus parser uses, is not found in its chunk or is shorter than a configured number of words. Claims are streamed one at a time, each only after the guard has checked it, and there is no free text besides claims: "not enough basis" means no claim survived.

## Consequences

- Every claim the user sees points to a chunk that was retrieved, so the UI can show the article and the passage behind it.
- Every claim also carries the exact passage that backs it, so the UI highlights the quote inside the article, and a claim attached to an unrelated passage of a retrieved chunk is dropped at runtime, not only measured in the evals.
- Citation precision becomes a deterministic check in the generation evals, alongside the LLM-as-judge metrics.
- Each answer goes through one extra validation step, and some answers become refusals. The score threshold, the minimum quote length and the minimum that must survive the guard are tuned with the generation evals.
- A literal quote proves the words are in the chunk, not that they support the claim (a quote can be cut to change its meaning), and not that the chunk is trustworthy: an instruction planted in a chunk can be quoted back. The faithfulness eval and the review sessions still measure support; the regulation corpus is official text, and tenant documents (phase 2) need more than this guard.
- A quote the model copies with a small slip (a changed word, an ellipsis) costs the whole claim.

## Alternatives considered

- **Free-text answers with citations requested in the prompt** — nothing checks the citations, so the model can invent one that looks right.
- **Post-hoc similarity check only** — a similarity score between the whole answer and the retrieved text cannot point to the claim that lacks support, so the system has nothing precise to drop.
- **IDs only, support checked by the evals** (the decision before the amendment) — an unsupported claim would reach users between eval runs.
- **Anthropic's citations feature** — it returns cited spans in free text, but it cannot be combined with structured outputs, and the answer would no longer be a list of claims the guard can check one by one.
