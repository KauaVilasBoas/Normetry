# Generation eval: claude-haiku-5-5

- Run: 2026-11-15 01:57 UTC; 59 of 59 items, 69 passes
- Chat model: `claude-haiku-5-5`, effort medium, max_tokens 16000; minimum quote words 4, minimum re-ranker score 0.0
- Retrieval: `BAAI/bge-m3@5617a9f61b028005a4858fdac845db406aefb181`, re-ranker `BAAI/bge-reranker-v2-m3@953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e`; vector_k=50, text_k=50, rrf_k=60, rerank_candidates=30, top_k=10, hnsw_ef_search=100, reranker_model=BAAI/bge-reranker-v2-m3, reranker_model_revision=953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e, reranker_max_length=1024
- Rewrite model: `claude-haiku-5-5` (effort low, max_tokens 1024), last 4 turns
- Corpus: 724 chunks
- Budget: US$ 0.0642 spent of US$ 1 (every model call of the run: all models, both rewrite modes, rewrites); complete

## Success measures

Over one pass per item: a follow-up's question rewritten by the rewrite model (the default). A citation is out of scope when its chunk is outside the item's jurisdiction or, for EU items, in the other language.

| measure | value | target |
|---|---|---|
| Answerable items answered | 43/44 (98%) |  |
| Expected refusals refused | 14/15 (93%) |  |
| - not_covered | 9/9 (100%) | ≥ 90% |
| - wrong_jurisdiction | 4/5 (80%) |  |
| - follow-up | 1/1 (100%) |  |
| Wrong-jurisdiction citations | 0 | 0 |
| Claims shown / dropped by the guard | 109 / 1 |  |
| Model refusals (stop_reason refusal) | 0 |  |
| Unavailable answers | 0 |  |
| Gold quotes held by the cited chunks (answered answerable items) | 0.936 |  |
| Answered answerable items citing a gold quote | 43/43 (100%) |  |
| Time to first claim p50 / p95 | 18.45 s / 27.74 s | p95 < 6 s |
| Total time p50 / p95 | 17.48 s / 28.25 s |  |
| Cost / per answer | US$ 0.0522 / US$ 0.0009 |  |
| Tokens in / out | 295489 / 45298 |  |

## Follow-up rewrite: model against baseline

The dataset has no assistant turns, so a follow-up's history is its earlier user turns only. Recall@10 is of the gold quotes in the retrieved chunks (answerable follow-ups).

| rewrite | retrieval recall@10 | correct outcome | rewrite cost |
|---|---|---|---|
| model | 1.000 | 10/10 (100%) | US$ 0.0007 |
| baseline | 1.000 | 9/10 (90%) | US$ 0.0000 |

## Items

| item | type | rewrite | expected | outcome | shown / dropped | first claim | cost |
|---|---|---|---|---|---|---|---|
| us-001 | direct | none | answer | answered | 2 / 0 | 26.30 s | US$ 0.0012 |
| us-002 | direct | none | answer | answered | 3 / 0 | 27.02 s | US$ 0.0014 |
| us-003 | direct | none | answer | answered | 2 / 0 | 22.85 s | US$ 0.0011 |
| us-004 | direct | none | answer | answered | 4 / 0 | 26.26 s | US$ 0.0014 |
| us-005 | direct | none | answer | answered | 2 / 0 | 20.87 s | US$ 0.0009 |
| us-006 | direct | none | answer | answered | 3 / 0 | 23.97 s | US$ 0.0012 |
| us-007 | two_provisions | none | answer | answered | 3 / 0 | 27.76 s | US$ 0.0012 |
| us-008 | two_provisions | none | answer | answered | 4 / 0 | 30.66 s | US$ 0.0016 |
| us-009 | two_provisions | none | answer | answered | 4 / 0 | 24.92 s | US$ 0.0014 |
| us-010 | not_covered | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0007 |
| us-011 | not_covered | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0006 |
| us-012 | not_covered | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0006 |
| us-013 | wrong_jurisdiction | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0007 |
| us-014 | wrong_jurisdiction | none | refuse | answered | 3 / 0 | 27.13 s | US$ 0.0012 |
| us-015 | misleading_premise | none | answer | answered | 4 / 0 | 20.72 s | US$ 0.0012 |
| us-016 | misleading_premise | none | answer | answered | 3 / 0 | 28.20 s | US$ 0.0016 |
| us-017 | follow_up | model | answer | answered | 2 / 0 | 27.66 s | US$ 0.0013 |
| us-017 | follow_up | baseline | answer | answered | 3 / 0 | 28.39 s | US$ 0.0016 |
| us-018 | follow_up | model | answer | answered | 1 / 1 | 22.03 s | US$ 0.0011 |
| us-018 | follow_up | baseline | answer | answered | 2 / 0 | 24.35 s | US$ 0.0011 |
| us-019 | follow_up | model | answer | answered | 3 / 0 | 26.33 s | US$ 0.0013 |
| us-019 | follow_up | baseline | answer | answered | 4 / 0 | 29.46 s | US$ 0.0019 |
| us-020 | follow_up | model | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0003 |
| us-020 | follow_up | baseline | refuse | answered | 1 / 0 | 14.34 s | US$ 0.0005 |
| eu-001 | direct | none | answer | answered | 1 / 0 | 16.89 s | US$ 0.0008 |
| eu-002 | direct | none | answer | answered | 2 / 0 | 16.71 s | US$ 0.0007 |
| eu-003 | direct | none | answer | answered | 1 / 0 | 13.92 s | US$ 0.0006 |
| eu-004 | direct | none | answer | answered | 3 / 0 | 16.21 s | US$ 0.0007 |
| eu-005 | two_provisions | none | answer | answered | 5 / 0 | 18.83 s | US$ 0.0013 |
| eu-006 | two_provisions | none | answer | answered | 4 / 0 | 18.27 s | US$ 0.0012 |
| eu-007 | not_covered | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0005 |
| eu-008 | wrong_jurisdiction | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0005 |
| eu-009 | misleading_premise | none | answer | answered | 2 / 0 | 17.15 s | US$ 0.0008 |
| eu-010 | follow_up | model | answer | answered | 2 / 0 | 18.38 s | US$ 0.0009 |
| eu-010 | follow_up | baseline | answer | answered | 3 / 0 | 15.97 s | US$ 0.0007 |
| eu-011 | direct | none | answer | answered | 2 / 0 | 19.29 s | US$ 0.0011 |
| eu-012 | direct | none | answer | answered | 3 / 0 | 18.53 s | US$ 0.0013 |
| eu-013 | direct | none | answer | answered | 2 / 0 | 17.61 s | US$ 0.0011 |
| eu-014 | two_provisions | none | answer | answered | 5 / 0 | 21.68 s | US$ 0.0019 |
| eu-015 | not_covered | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0007 |
| eu-016 | not_covered | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0007 |
| eu-017 | wrong_jurisdiction | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0007 |
| eu-018 | misleading_premise | none | answer | answered | 2 / 0 | 20.25 s | US$ 0.0012 |
| eu-019 | follow_up | model | answer | answered | 2 / 0 | 18.83 s | US$ 0.0010 |
| eu-019 | follow_up | baseline | answer | answered | 4 / 0 | 17.21 s | US$ 0.0012 |
| eu-020 | follow_up | model | answer | answered | 3 / 0 | 19.03 s | US$ 0.0012 |
| eu-020 | follow_up | baseline | answer | answered | 4 / 0 | 22.62 s | US$ 0.0021 |
| br-001 | direct | none | answer | answered | 2 / 0 | 9.32 s | US$ 0.0005 |
| br-002 | direct | none | answer | answered | 2 / 0 | 10.59 s | US$ 0.0005 |
| br-003 | direct | none | answer | answered | 1 / 0 | 8.45 s | US$ 0.0006 |
| br-004 | direct | none | answer | answered | 3 / 0 | 10.22 s | US$ 0.0008 |
| br-005 | direct | none | answer | answered | 1 / 0 | 8.83 s | US$ 0.0005 |
| br-006 | direct | none | answer | answered | 1 / 0 | 9.51 s | US$ 0.0006 |
| br-007 | direct | none | answer | answered | 2 / 0 | 11.02 s | US$ 0.0007 |
| br-008 | two_provisions | none | answer | answered | 4 / 0 | 9.12 s | US$ 0.0009 |
| br-009 | two_provisions | none | answer | answered | 2 / 0 | 12.14 s | US$ 0.0011 |
| br-010 | two_provisions | none | answer | answered | 3 / 0 | 11.03 s | US$ 0.0008 |
| br-011 | not_covered | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0004 |
| br-012 | not_covered | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0004 |
| br-013 | not_covered | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0004 |
| br-014 | wrong_jurisdiction | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0004 |
| br-016 | misleading_premise | none | answer | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0004 |
| br-017 | misleading_premise | none | answer | answered | 2 / 0 | 10.73 s | US$ 0.0008 |
| br-018 | follow_up | model | answer | answered | 1 / 0 | 9.30 s | US$ 0.0006 |
| br-018 | follow_up | baseline | answer | answered | 2 / 0 | 7.94 s | US$ 0.0006 |
| br-019 | follow_up | model | answer | answered | 1 / 0 | 8.73 s | US$ 0.0003 |
| br-019 | follow_up | baseline | answer | answered | 2 / 0 | 9.24 s | US$ 0.0006 |
| br-020 | follow_up | model | answer | answered | 2 / 0 | 12.29 s | US$ 0.0007 |
| br-020 | follow_up | baseline | answer | answered | 5 / 0 | 9.91 s | US$ 0.0009 |

## Review

This run tests two changes made after the [first-prompt run](2026-11-15-generation-claude-haiku-5-5-first-prompt.md). The answer prompt now asks for quotes that keep their conditions, rules stated in the excerpts' own category terms, and no claim about what the excerpts leave out. The follow-up rewrite now goes through structured output. The review used the same method as the first run: Claude Code sessions with one reviewer per jurisdiction. Every claim was read against its quotes, and against the section text wherever needed. Verdicts are in [`2026-11-15-generation-claude-haiku-5-5.review.json`](2026-11-15-generation-claude-haiku-5-5.review.json). Counts cover one pass per item.

| | first prompt | this run |
|---|---|---|
| Claims: supported / partial / unsupported | 97 / 24 / 1 of 122 (80% / 20% / 1%) | 87 / 21 / 1 of 109 (80% / 19% / 1%) |
| Claims that don't help with the question | 7 | 5 |
| Answerable items: complete / partial / wrong / refused | 40 / 4 / 0 / 0 of 44 | 38 / 4 / 1 / 1 of 44 |
| Expected refusals refused | 13 of 15 | 14 of 15 |
| Gold quotes held by the cited chunks | 0.915 | 0.936 |
| Cost | US$ 0.0589 | US$ 0.0642 |

What changed:

- Every rewritten follow-up is now one standalone question; the first run had leaked scratch work into br-019.
- eu-017 (days for cooked food in the fridge) is now refused. us-014 (food safety culture) still opens with what the Code leaves out and then answers a nearby question, and eu-010 (steam) now carries the run's one unsupported claim in the same pattern. The prompt rule did not remove claims about what the excerpts lack: there are two in each run.
- br-016 (monthly pest control, a wrong premise) was answered in the first run from nearby rules. Now it is refused. The sections that correct the premise, 4.3.1 and 4.3.2, never reach the re-ranked top 10, so the cause is retrieval and not the prompt. Section 4.3.2 is at fused rank 31, one place past the 30 candidates the re-ranker scores.
- us-018 (do waiters need hair restraints) became the run's one wrong answer. The guard dropped one claim, and the claim left states the hair-restraint rule without the wait-staff exemption from ¶ (B). The first run answered it completely. This is run-to-run variation of the model; neither change explains it.
- Partial claims are about as common as before, and of the same kinds: a quote that stops before a condition the claim states, an overstated count or scope, or a rule applied to the dish the question names.

Two runs of the same model differ by about as much as the prompt change moved anything. The effects on faithfulness are within that noise; the rewrite fix is not. The changes stay: the refusals and the rewrite improved, and faithfulness did not get worse.

Follow-up rewrite over both runs: the model rewrite refused the follow-up refusal (us-020) both times, and the baseline answered it both times, from the earlier turn. On answerable follow-ups the two are level: the model rewrite gave 16 complete answers of 18 and the baseline 17 of 18, with recall@10 at 1.0 for both. The model rewrite stays the default.

Against the phase 1 success measures:
- recall@10 0.934 (≥ 0.9): met.
- Shown claims citing a retrieved chunk with a quote found in it: 100%, enforced by the guard.
- Wrong-jurisdiction citations: 0, met.
- Not-covered items refused: 9 of 9 (≥ 90%), met.
- Faithfulness: 1 unsupported claim, against a target of zero.
- p95 time to first claim: 27.7 s, against 6 s. Retrieval accounts for about 15 s of it (the CPU re-ranker, #72). Haiku takes about 4 s from the start of the call to the first claim.

What's left: absence claims, and quotes that leave out a condition. The model comparison (#73) checks whether a stronger model avoids them.
