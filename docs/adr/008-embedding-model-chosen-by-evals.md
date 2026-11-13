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

## Update, 2026-11-13: the comparison is between local models

This changes only the first comparison the decision plans.

- Hosted embeddings are out of scope for phase 1: the only paid provider the project uses, Anthropic, has no embeddings API, and the phase 1 design keeps paid calls out of CI, where the retrieval eval runs. Hosted adapters remain possible behind the interface.
- The first comparison is BGE-M3 against Qwen3-Embedding-0.6B (Apache 2.0, multilingual, 1024 dimensions, so it fits the existing column without a migration). It is an environment change, `NORMETRY_EMBEDDING_MODEL` and `NORMETRY_EMBEDDING_MODEL_REVISION`, then `uv run pytest -m eval` ([`evals/README.md`](../../evals/README.md)). The interface embeds queries with `embed_query`, which applies the model's own query prompt (Qwen3 has one, BGE-M3 none).
- BGE-M3, measured ([report](../../evals/reports/2026-11-13-retrieval-bge-m3.md)): the vector search alone finds the expected quote in its top 10 for recall@10 0.943, MRR@10 0.842, on the 44 answerable items of the golden dataset; per regulation, recall@10 is 0.893 (FDA Food Code), 1.000 (852/2004 en and pt) and 0.933 (RDC 216/2004).
- Qwen3-Embedding-0.6B: not run yet. Its weights, like the re-ranker's, could not be downloaded on the machine that ran the BGE-M3 eval (the Hugging Face file CDN timed out). BGE-M3 stays the model until that run exists; the comparison is recorded here when it does.
