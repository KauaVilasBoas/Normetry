# 001. Build the core in .NET and the AI service in Python

- **Status:** Accepted
- **Date:** 2026-11-11

## Context

Normetry does two different kinds of work. The core is a transactional B2B application: tenants, users and roles, establishments, documents with versions and expiry dates, inspections with findings and their review, and usage limits per tenant. The AI side parses PDF and DOCX files, chunks and embeds them, runs hybrid retrieval, calls LLMs, runs agents and measures the results with evals. The libraries each side needs live in different ecosystems.

## Decision

The core is a .NET 10 service and the AI service is a Python 3.12+ service.

- .NET owns the domain and the edge: DDD aggregates, CQRS handlers, EF Core persistence, the outbox, the public API, auth and the YARP gateway. Aspire runs every service and container locally.
- Python owns ingestion, retrieval, generation and agents. The libraries for this work are most mature there: LangGraph, Docling, Ragas and DeepEval, the embedding and re-ranker models, and the pgvector client.
- The services talk over HTTP through YARP for chat and search (ADR 004) and over queue events for long-running work (ADR 005). Each writes only its own database schema (ADR 002).

## Consequences

- The domain gets strong typing, EF Core migrations and Aspire tooling, and the AI code calls the Python libraries directly.
- Two runtimes, two toolchains (`dotnet` and `uv`), two CI jobs and two dependency trees to keep current.
- A network boundary sits between the services. The internal HTTP API and the queue messages are contracts that need versioning, and traces cross the boundary through W3C `traceparent` headers.
- We would revisit this if the .NET ecosystem caught up on document parsing and evals, or if the AI service stayed small enough that a second runtime cost more than it saved.

## Alternatives considered

- **All .NET** — Semantic Kernel and Microsoft.Extensions.AI cover LLM calls and basic RAG, but the document parsing and eval libraries are thinner than in Python.
- **All Python** — one runtime, but the transactional B2B core (domain model, migrations, auth, real-time updates) fits .NET better, where EF Core, ASP.NET Core and Aspire already cover it.
