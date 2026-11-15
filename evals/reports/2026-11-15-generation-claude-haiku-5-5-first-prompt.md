# Generation eval: claude-haiku-5-5, first prompt

- Run: 2026-11-15 00:54 UTC; 59 of 59 items, 69 passes
- Chat model: `claude-haiku-5-5`, effort medium, max_tokens 16000; minimum quote words 4, minimum re-ranker score 0.0
- Retrieval: `BAAI/bge-m3@5617a9f61b028005a4858fdac845db406aefb181`, re-ranker `BAAI/bge-reranker-v2-m3@953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e`; vector_k=50, text_k=50, rrf_k=60, rerank_candidates=30, top_k=10, hnsw_ef_search=100, reranker_model=BAAI/bge-reranker-v2-m3, reranker_model_revision=953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e, reranker_max_length=1024
- Rewrite model: `claude-haiku-5-5` (effort low, max_tokens 1024), last 4 turns
- Corpus: 724 chunks
- Budget: US$ 0.0589 spent of US$ 1 (every model call of the run: all models, both rewrite modes, rewrites); complete

## Success measures

Over one pass per item: a follow-up's question rewritten by the rewrite model (the default). A citation is out of scope when its chunk is outside the item's jurisdiction or, for EU items, in the other language.

| measure | value | target |
|---|---|---|
| Answerable items answered | 44/44 (100%) |  |
| Expected refusals refused | 13/15 (87%) |  |
| - not_covered | 9/9 (100%) | ≥ 90% |
| - wrong_jurisdiction | 3/5 (60%) |  |
| - follow-up | 1/1 (100%) |  |
| Wrong-jurisdiction citations | 0 | 0 |
| Claims shown / dropped by the guard | 122 / 0 |  |
| Model refusals (stop_reason refusal) | 0 |  |
| Unavailable answers | 0 |  |
| Gold quotes held by the cited chunks (answered answerable items) | 0.915 |  |
| Answered answerable items citing a gold quote | 43/44 (98%) |  |
| Time to first claim p50 / p95 | 18.86 s / 26.15 s | p95 < 6 s |
| Total time p50 / p95 | 18.92 s / 27.44 s |  |
| Cost / per answer | US$ 0.0487 / US$ 0.0008 |  |
| Tokens in / out | 288772 / 39658 |  |

## Follow-up rewrite: model against baseline

The dataset has no assistant turns, so a follow-up's history is its earlier user turns only. Recall@10 is of the gold quotes in the retrieved chunks (answerable follow-ups).

| rewrite | retrieval recall@10 | correct outcome | rewrite cost |
|---|---|---|---|
| model | 1.000 | 10/10 (100%) | US$ 0.0005 |
| baseline | 1.000 | 9/10 (90%) | US$ 0.0000 |

## Items

| item | type | rewrite | expected | outcome | shown / dropped | first claim | cost |
|---|---|---|---|---|---|---|---|
| us-001 | direct | none | answer | answered | 2 / 0 | 20.52 s | US$ 0.0010 |
| us-002 | direct | none | answer | answered | 2 / 0 | 22.70 s | US$ 0.0013 |
| us-003 | direct | none | answer | answered | 2 / 0 | 18.53 s | US$ 0.0008 |
| us-004 | direct | none | answer | answered | 4 / 0 | 20.90 s | US$ 0.0009 |
| us-005 | direct | none | answer | answered | 2 / 0 | 17.47 s | US$ 0.0006 |
| us-006 | direct | none | answer | answered | 3 / 0 | 21.68 s | US$ 0.0011 |
| us-007 | two_provisions | none | answer | answered | 4 / 0 | 25.94 s | US$ 0.0013 |
| us-008 | two_provisions | none | answer | answered | 6 / 0 | 32.72 s | US$ 0.0023 |
| us-009 | two_provisions | none | answer | answered | 2 / 0 | 21.38 s | US$ 0.0010 |
| us-010 | not_covered | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0007 |
| us-011 | not_covered | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0006 |
| us-012 | not_covered | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0006 |
| us-013 | wrong_jurisdiction | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0007 |
| us-014 | wrong_jurisdiction | none | refuse | answered | 7 / 0 | 25.98 s | US$ 0.0015 |
| us-015 | misleading_premise | none | answer | answered | 4 / 0 | 19.05 s | US$ 0.0012 |
| us-016 | misleading_premise | none | answer | answered | 5 / 0 | 26.20 s | US$ 0.0015 |
| us-017 | follow_up | model | answer | answered | 2 / 0 | 24.71 s | US$ 0.0012 |
| us-017 | follow_up | baseline | answer | answered | 2 / 1 | 23.71 s | US$ 0.0013 |
| us-018 | follow_up | model | answer | answered | 2 / 0 | 21.12 s | US$ 0.0011 |
| us-018 | follow_up | baseline | answer | answered | 3 / 0 | 20.95 s | US$ 0.0010 |
| us-019 | follow_up | model | answer | answered | 3 / 0 | 23.10 s | US$ 0.0011 |
| us-019 | follow_up | baseline | answer | answered | 6 / 0 | 26.24 s | US$ 0.0017 |
| us-020 | follow_up | model | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0003 |
| us-020 | follow_up | baseline | refuse | answered | 1 / 0 | 13.88 s | US$ 0.0005 |
| eu-001 | direct | none | answer | answered | 1 / 0 | 14.02 s | US$ 0.0006 |
| eu-002 | direct | none | answer | answered | 2 / 0 | 15.75 s | US$ 0.0006 |
| eu-003 | direct | none | answer | answered | 1 / 0 | 12.91 s | US$ 0.0005 |
| eu-004 | direct | none | answer | answered | 3 / 0 | 15.42 s | US$ 0.0007 |
| eu-005 | two_provisions | none | answer | answered | 4 / 0 | 23.46 s | US$ 0.0010 |
| eu-006 | two_provisions | none | answer | answered | 4 / 0 | 19.95 s | US$ 0.0013 |
| eu-007 | not_covered | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0005 |
| eu-008 | wrong_jurisdiction | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0006 |
| eu-009 | misleading_premise | none | answer | answered | 3 / 0 | 17.60 s | US$ 0.0009 |
| eu-010 | follow_up | model | answer | answered | 2 / 0 | 20.50 s | US$ 0.0009 |
| eu-010 | follow_up | baseline | answer | answered | 3 / 0 | 16.26 s | US$ 0.0007 |
| eu-011 | direct | none | answer | answered | 2 / 0 | 17.60 s | US$ 0.0010 |
| eu-012 | direct | none | answer | answered | 4 / 0 | 19.80 s | US$ 0.0010 |
| eu-013 | direct | none | answer | answered | 2 / 0 | 19.69 s | US$ 0.0011 |
| eu-014 | two_provisions | none | answer | answered | 6 / 0 | 21.66 s | US$ 0.0014 |
| eu-015 | not_covered | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0007 |
| eu-016 | not_covered | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0006 |
| eu-017 | wrong_jurisdiction | none | refuse | answered | 1 / 0 | 20.24 s | US$ 0.0007 |
| eu-018 | misleading_premise | none | answer | answered | 2 / 0 | 24.61 s | US$ 0.0011 |
| eu-019 | follow_up | model | answer | answered | 3 / 0 | 32.37 s | US$ 0.0012 |
| eu-019 | follow_up | baseline | answer | answered | 4 / 0 | 30.48 s | US$ 0.0013 |
| eu-020 | follow_up | model | answer | answered | 3 / 0 | 25.91 s | US$ 0.0012 |
| eu-020 | follow_up | baseline | answer | answered | 2 / 0 | 29.31 s | US$ 0.0015 |
| br-001 | direct | none | answer | answered | 2 / 0 | 12.96 s | US$ 0.0005 |
| br-002 | direct | none | answer | answered | 1 / 0 | 17.08 s | US$ 0.0004 |
| br-003 | direct | none | answer | answered | 1 / 0 | 8.70 s | US$ 0.0004 |
| br-004 | direct | none | answer | answered | 3 / 0 | 12.47 s | US$ 0.0007 |
| br-005 | direct | none | answer | answered | 2 / 0 | 14.78 s | US$ 0.0005 |
| br-006 | direct | none | answer | answered | 1 / 0 | 15.22 s | US$ 0.0006 |
| br-007 | direct | none | answer | answered | 2 / 0 | 15.97 s | US$ 0.0008 |
| br-008 | two_provisions | none | answer | answered | 4 / 0 | 13.60 s | US$ 0.0009 |
| br-009 | two_provisions | none | answer | answered | 2 / 0 | 12.34 s | US$ 0.0008 |
| br-010 | two_provisions | none | answer | answered | 3 / 0 | 12.83 s | US$ 0.0005 |
| br-011 | not_covered | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0004 |
| br-012 | not_covered | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0003 |
| br-013 | not_covered | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0003 |
| br-014 | wrong_jurisdiction | none | refuse | refused (no_supported_claims) | 0 / 0 | n/a | US$ 0.0003 |
| br-016 | misleading_premise | none | answer | answered | 2 / 0 | 15.17 s | US$ 0.0008 |
| br-017 | misleading_premise | none | answer | answered | 2 / 0 | 13.02 s | US$ 0.0007 |
| br-018 | follow_up | model | answer | answered | 1 / 0 | 11.09 s | US$ 0.0005 |
| br-018 | follow_up | baseline | answer | answered | 2 / 0 | 13.26 s | US$ 0.0006 |
| br-019 | follow_up | model | answer | answered | 1 / 0 | 18.68 s | US$ 0.0003 |
| br-019 | follow_up | baseline | answer | answered | 2 / 0 | 14.67 s | US$ 0.0006 |
| br-020 | follow_up | model | answer | answered | 2 / 0 | 12.21 s | US$ 0.0006 |
| br-020 | follow_up | baseline | answer | answered | 4 / 0 | 12.20 s | US$ 0.0006 |

## Review

The review was done in Claude Code sessions, one reviewer per jurisdiction. Each claim was read against its own quotes, and against the full section text wherever a condition or a refusal was in question. Each answer was compared with the item's expected answer. Verdicts: [`2026-11-15-generation-claude-haiku-5-5-first-prompt.review.json`](2026-11-15-generation-claude-haiku-5-5-first-prompt.review.json). The counts below cover one pass per item (the model rewrite for follow-ups).

| Claims (122) | count |
|---|---|
| Supported: the quotes state the claim, conditions included | 97 (80%) |
| Partial: the gist holds, but a condition or detail is outside the quote, or a category rule is applied to a named dish or business | 24 (20%) |
| Unsupported | 1 (eu-017: "there is no day limit", backed by a quote about rapid cooling) |
| True to the quote but no help with the question | 7 |

| Answers | count |
|---|---|
| Answerable items: complete / partial / wrong | 40 / 4 / 0 of 44 |
| Expected refusals: refused / answered a nearby question instead | 13 / 2 of 15 |

What the review found:

- No claim invents a fact. Every partial claim restates its own section, with one of three slips:
  - the quote stops before a condition or exception that the claim states, or that the claim leaves out ("where necessary", "unless…", "if there is a likelihood of contamination");
  - a time/temperature control for safety food rule is applied to the dish the question names ("cooked chili must…");
  - an extra word stretches the claim past its quote.
- Both refusal misses (us-014 food safety culture, eu-017 days for cooked food in the fridge) follow the same pattern. The model writes a claim about what the excerpts do not say ("the excerpts do not mention…", "no day limit is set"), cites a nearby passage, and then answers a neighbouring question. The guard lets these claims through because their quotes are real.
- The follow-up rewrite once returned its own scratch work as the question (br-019: "Better to be clean and precise: … Hmm, the rules say…"). The answer was still complete, but that text went to retrieval and into the answer prompt.
- Follow-ups, model rewrite against baseline: the rewrite answers 9 complete plus 1 correct refusal. The baseline answers 8 complete, 1 partial (us-017 covers fish, not chicken) and 1 answered refusal (us-020 answers the first turn). Retrieval recall@10 is 1.0 for both. The model rewrite stays the default: it costs US$ 0.00005 and about 1 s per follow-up.

Changes made after this run: the answer prompt now says that quotes carry their conditions, that a rule is stated in the excerpts' own category terms, and that what the excerpts leave out is never claimed; the rewrite now goes through structured output. The run that measures those changes is `2026-11-15-generation-claude-haiku-5-5.md`.
