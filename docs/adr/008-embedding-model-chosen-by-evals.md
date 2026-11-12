# 008. Choose the embedding model by evals, behind an interface

- **Status:** Accepted
- **Date:** 2026-11-11

## Context

Retrieval quality depends on the embedding model, and the corpus is multilingual: a question in Portuguese can need a passage in English, and the reverse. Candidates include local models such as BGE-M3 (1024 dimensions, multilingual PT/EN) and hosted embedding APIs, which differ in cost, latency and where the text is sent. No eval has run yet, so no measurement favors one of them.

## Decision

- Ingestion and retrieval call an embedding interface. Each model, local or hosted, is an adapter behind it.
- The retrieval evals choose the model: recall@k and MRR on a set of questions with annotated expected chunks. The first planned comparison is local BGE-M3 against hosted embeddings.
- The schema starts with `vector(1024)`, the dimension of BGE-M3.

## Consequences

- Changing the model is an experiment with a measured result: embed the eval corpus with the candidate, run the retrieval evals and compare.
- Queries and chunks must be embedded by the same model, and the vector column's dimension is tied to the model. Changing models means re-embedding every chunk, public corpus and tenant documents alike, plus a migration of the column and its HNSW index when the dimension changes.

## Alternatives considered

- **Pick one model and call its SDK directly** — less code, but a later switch touches ingestion and retrieval, and the choice rests on opinion with no measurement.
- **Choose from a public leaderboard (MTEB)** — it ranks models on general datasets, not on regulation text with cross-language questions.
