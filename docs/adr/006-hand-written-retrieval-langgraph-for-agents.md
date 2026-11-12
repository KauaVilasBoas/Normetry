# 006. Write retrieval by hand and use LangGraph only for agents

- **Status:** Accepted
- **Date:** 2026-11-11

## Context

Answer quality depends on retrieval, and when an eval metric drops we need to know which step caused it: the vector search, the lexical search, the fusion or the re-ranker. The corpus is in Portuguese and English, and a question in one language may need a passage in the other. The simulated inspection is a different kind of problem: a long run with parallel batches that must survive a worker crash and stop for human review.

## Decision

- Retrieval is plain code in `normetry_ai/retrieval/`: pgvector top-k and PostgreSQL full-text top-k, merged with Reciprocal Rank Fusion, then a multilingual re-ranker (for example bge-reranker-v2-m3). It uses no framework retrievers.
- LangGraph runs the simulated-inspection agent, where it provides Postgres checkpoints so a run resumes after a crash, `Send` to fan out one branch per batch of requirements, and a human-review step. It also runs the change monitor. Chat and search do not use it.

## Consequences

- Each step has inputs and outputs we can log, trace and score, so the planned experiments (vector only against hybrid, with and without the re-ranker) change one step at a time.
- An answer can be explained down to the chunks each search returned, their fused rank and the re-ranker score.
- More code of our own: query building, fusion, thresholds and their tests. Improvements to framework retrievers do not reach us unless we port them.
- We would revisit this if a library covered the same steps with the same visibility and the evals showed no loss from switching.

## Alternatives considered

- **Framework retrievers (LangChain, LlamaIndex)** — they hide fusion and ranking details behind their abstractions, which makes a regression in the evals hard to attribute to one step.
- **Hand-written agent orchestration** — we would have to build checkpoints, fan-out and resumable human review ourselves, and LangGraph already provides them.
