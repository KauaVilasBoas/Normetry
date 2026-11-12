# 002. Use one PostgreSQL database with `core` and `ai` schemas

- **Status:** Accepted
- **Date:** 2026-11-11

## Context

The core stores tenants, establishments, documents, assessments and usage. The AI service stores chunks with their embeddings and `tsvector`, and the agents' checkpoints. Retrieval needs vector search and full-text search over the same chunks, and both must respect tenant isolation (ADR 003). Chunks belong to documents the core owns, so a separate vector store would hold a second copy of tenant data that has to be kept in sync and isolated on its own.

## Decision

One PostgreSQL database with two schemas:

- `core`, created by EF Core migrations and written only by .NET (role `app_core`).
- `ai`, created by Alembic migrations and written only by Python (role `app_ai`).

pgvector (HNSW indexes) and full-text search run on `ai.chunks`, in the same engine as the business data and under the same RLS policies. AI results the core needs, such as document status and findings, arrive as queue events (ADR 005), and the core writes them to `core` itself.

## Consequences

- One database to operate and back up. Vector and lexical search run next to the business data with RLS on both, and there is no external index to keep in sync.
- Each service migrates only its own schema with its own tool, so a migration in one service never alters tables the other writes.
- Both services share one server's capacity, so heavy ingestion can slow the core's queries.
- At millions of chunks, pgvector index size and latency may call for a dedicated engine such as Qdrant or Azure AI Search. Retrieval sits behind an interface, and phase 3 adds a second adapter (Azure AI Search) that the evals will compare against pgvector.

## Alternatives considered

- **External vector database from the start (Qdrant, Azure AI Search)** — a second system to run and sync, with tenant isolation enforced again outside PostgreSQL.
- **SQL Server** — SQL Server 2025 has a vector type and would work. PostgreSQL has the larger ecosystem for this workload: pgvector is the default in Python AI tooling, HNSW indexes and full-text search run in the same engine, and Azure offers it managed (Flexible Server).
- **One database per service** — two servers to run, when separate schemas and app roles already keep write ownership apart.
