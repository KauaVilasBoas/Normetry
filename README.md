<!-- TODO(fill later): replace the text title with the logo once docs/brand/ exists -->
<h1 align="center">Normetry</h1>

<p align="center">
  <i>Multi-tenant <b>compliance copilot</b> for regulated establishments.<br/>
  Ask the regulation and get an answer that cites the article, run a simulated inspection
  against your own documents, and get warned when a rule you depend on changes.</i>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/status-phase_0_·_foundation-F2C744?labelColor=0B1220" alt="Status: phase 0"/>
  <!-- TODO(fill later): CI badge once .github/workflows/ci.yml exists
  <a href="https://github.com/KauaVilasBoas/Normetry/actions/workflows/ci.yml">
    <img src="https://github.com/KauaVilasBoas/Normetry/actions/workflows/ci.yml/badge.svg" alt="CI"/>
  </a>
  -->
  <!-- TODO(fill later): evals badge (retrieval recall@k / citation precision) once the eval suite runs in CI -->
  <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white&labelColor=0B1220" alt=".NET 10"/>
  <img src="https://img.shields.io/badge/Python-3.12+-3776AB?logo=python&logoColor=white&labelColor=0B1220" alt="Python 3.12+"/>
  <img src="https://img.shields.io/badge/PostgreSQL-pgvector-4169E1?logo=postgresql&logoColor=white&labelColor=0B1220" alt="PostgreSQL + pgvector"/>
  <img src="https://img.shields.io/badge/LangGraph-agents-1C3C3C?logo=langchain&logoColor=white&labelColor=0B1220" alt="LangGraph"/>
  <a href="https://www.conventionalcommits.org/">
    <img src="https://img.shields.io/badge/Conventional_Commits-1.0.0-FE5196?logo=conventionalcommits&logoColor=white&labelColor=0B1220" alt="Conventional Commits"/>
  </a>
  <a href="LICENSE">
    <img src="https://img.shields.io/badge/License-MIT-3FB950?labelColor=0B1220" alt="MIT"/>
  </a>
</p>

<p align="center">
  <b>3</b> jurisdictions &nbsp;·&nbsp; <b>2</b> languages (PT · EN) &nbsp;·&nbsp; <b>3</b> AI capabilities &nbsp;·&nbsp; <b>0</b> tolerated cross-tenant leaks
</p>

<p align="center">
  <a href="#what-is-this">Why</a> &nbsp;·&nbsp;
  <a href="#capabilities">Capabilities</a> &nbsp;·&nbsp;
  <a href="#architecture-at-a-glance">Architecture</a> &nbsp;·&nbsp;
  <a href="#engineering-decisions">Decisions</a> &nbsp;·&nbsp;
  <a href="#evals">Evals</a> &nbsp;·&nbsp;
  <a href="#getting-started">Run it</a> &nbsp;·&nbsp;
  <a href="#roadmap">Roadmap</a>
</p>

> [!NOTE]
> **Work in progress.** Normetry is being built in public, phase by phase (see the [Roadmap](#roadmap)).
> This README describes the target architecture; sections marked *coming soon* are filled in as each phase ships.

---

## What is this?

A restaurant manager has to comply with a food-safety code that runs to hundreds of pages. In
practice, compliance lives in a binder: SOPs nobody re-reads, temperature logs filled in by hand,
licenses that expire without warning. When the inspector arrives, the question is always the same:
*"which rule does this document satisfy, and where is the evidence?"* — and nobody can answer it
quickly.

**Normetry answers that question.** A customer (a single restaurant, a chain, or a compliance
consultancy) registers its establishments and uploads its documents: SOPs, licenses, temperature
logs, lab reports. Normetry then reads them **against the regulation itself**, and every claim it
makes points back to a specific article and a specific passage of the customer's own document.

The MVP targets **food service** across three jurisdictions:

| Jurisdiction | Regulation |
|---|---|
| 🇺🇸 United States | FDA Food Code |
| 🇪🇺 European Union | Regulation (EC) No 852/2004 on the hygiene of foodstuffs |
| 🇧🇷 Brazil | Anvisa RDC 216/2004 — good practices for food services |

Questions can be asked in **Portuguese or English**, and answers cite the regulation **in its
original language**: ask in Portuguese, get a cited passage of the FDA Food Code in English.

The data model already supports other establishment types (pharmacies, clinics), so new verticals
are a corpus and an eval set, not a rewrite.

### Highlights

- **Citations you can verify, not vibes** — the model answers in structured JSON where every claim
  carries the IDs of the chunks that support it. A deterministic guard drops any claim whose
  citation was not actually retrieved; if too little survives, the answer becomes *"not enough basis
  in the regulation"*. Below a re-ranker score threshold, the system refuses instead of guessing.
- **Hybrid retrieval, hand-written** — pgvector top-k + PostgreSQL full-text top-k, fused with
  Reciprocal Rank Fusion, then a multilingual re-ranker. No retrieval framework hiding the logic.
- **Tenant isolation enforced by the database** — PostgreSQL Row-Level Security on every
  tenant-owned table. Even a buggy query cannot return another tenant's data. The tenant is set
  per transaction, so pooled connections never leak it. Without a tenant, the policy sees only the
  public corpus and blocks writes: it **fails closed**.
- **A poisoned corpus is impossible by design** — the public regulation corpus (`tenant_id IS NULL`)
  can only be written by a dedicated `corpus_ingestor` role. No tenant can inject text into the
  shared regulations.
- **Agents with checkpoints and a human in the loop** — the simulated inspection is a LangGraph agent
  with Postgres checkpoints: if a worker dies mid-inspection, it resumes where it stopped. Findings
  are born *pending*; a human accepts, rejects or edits each one, and every rejection becomes a new
  eval example.
- **Evals are part of CI** — every PR touching prompts, retrieval or agents runs a reduced eval suite.
  The PR fails if a metric drops below its threshold, and the results table is posted as a comment.
- **One trace from click to LLM call** — OpenTelemetry in both services with W3C `traceparent`
  propagated over HTTP *and* through the message queue, plus Langfuse for prompts, tokens and cost.

---

## Capabilities

| Capability | What it does | Core technique |
|---|---|---|
| 💬 **Ask the regulation** | Chat that answers regulatory questions citing the exact article | Hybrid RAG + verifiable citations |
| 🔍 **Simulated inspection** | Evaluates the establishment's documents against applicable requirements; produces findings + an action plan | LangGraph agent with checkpoints and human review |
| 🔔 **Change monitor** | Detects when a regulation changes and warns the tenants whose documents are affected | Structural diff + semantic search over tenant documents |

<!-- TODO(fill later): screenshots / GIF of the three capabilities
## Screenshots

![Ask the regulation](docs/screenshots/chat.png)

| Simulated inspection | Change monitor |
|---|---|
| ![Inspection](docs/screenshots/inspection.png) | ![Monitor](docs/screenshots/monitor.png) |

<sub>Captured from the public demo — fictional establishments, no real customer data.</sub>
-->

<!-- TODO(fill later): live demo link + showcase video (in English)
**Live demo:** https://...  ·  **Showcase video:** https://...
-->

---

## Architecture at a glance

Two services with a hard ownership boundary: **.NET owns the business**, **Python owns the AI**.
The .NET core is the only door to the outside world; the Python service is never exposed to the
internet.

```mermaid
flowchart LR
    WEB["🖥️ Web App<br/><sub>React + TypeScript</sub>"]

    subgraph CORE["🟣 Core · .NET 10"]
        API["API REST<br/><sub>Clean Arch · DDD · CQRS</sub>"]
        YARP["Gateway YARP<br/><sub>/ai/* routes</sub>"]
        HUB["SignalR<br/><sub>real-time progress</sub>"]
        OUTBOX["Outbox<br/><sub>dispatcher</sub>"]
    end

    subgraph AI["🐍 AI Service · Python 3.12+"]
        FAST["FastAPI<br/><sub>SSE chat · search</sub>"]
        WORKER["Workers<br/><sub>ingestion · inspection · monitor</sub>"]
        GRAPH["LangGraph<br/><sub>checkpointed agents</sub>"]
    end

    PG[("PostgreSQL<br/><sub>schemas core + ai<br/>pgvector · full-text · RLS</sub>")]
    MQ[["RabbitMQ<br/><sub>Service Bus in production</sub>"]]
    BLOB[("Blob Storage<br/><sub>MinIO / Azure Blob</sub>")]
    LLM["LLM providers<br/><sub>via OpenRouter</sub>"]
    OBS["OpenTelemetry Collector<br/><sub>+ Langfuse</sub>"]

    WEB -->|HTTPS + JWT| API
    WEB -->|SSE| YARP
    WEB <-->|WebSocket| HUB
    YARP -->|internal token + tenant| FAST
    API --> PG
    API --> BLOB
    OUTBOX --> MQ
    MQ --> WORKER
    WORKER -->|result events| MQ
    MQ --> API
    WORKER --> GRAPH
    FAST --> PG
    WORKER --> PG
    WORKER --> BLOB
    FAST --> LLM
    GRAPH --> LLM
    CORE -.-> OBS
    AI -.-> OBS
```

**Ownership rule:** the `core` schema is written only by .NET, the `ai` schema only by Python.
AI results (findings, document status) flow back to the core **as events**, never as direct writes.

### Bounded contexts (core)

| Context | Responsibility |
|---|---|
| **Tenancy & Identity** | Tenant, User, roles (Owner · Manager · Auditor). OIDC via Keycloak locally |
| **Establishments** | Establishment with type (restaurant, pharmacy, clinic) and jurisdiction |
| **Documents** | Document, versions, type (SOP, license, log, report), expiry, status `Uploaded → Processing → Indexed \| Failed` |
| **Regulations** | Regulation catalog (code, jurisdiction, version, effective date). Read-only in the core: ingestion belongs to the AI service |
| **Assessments** | Simulated inspection, findings, action items, review (Accepted · Rejected · Edited) |
| **Usage & Budget** | Tokens and cost per tenant and per feature, with limits |
| **Notifications** | Change-monitor alerts and expiring-document warnings |

<details>
<summary><b>📐 Data model</b></summary>

```mermaid
erDiagram
    TENANT ||--o{ ESTABLISHMENT : "owns"
    ESTABLISHMENT ||--o{ DOCUMENT : "has"
    DOCUMENT ||--o{ CHUNK : "produces (ai schema)"
    REGULATION_SOURCE ||--o{ CHUNK : "produces (null tenant)"
    ESTABLISHMENT ||--o{ ASSESSMENT : "undergoes"
    ASSESSMENT ||--o{ FINDING : "contains"
    FINDING }o--|| CHUNK : "regulation requirement"
    FINDING }o--o{ CHUNK : "customer evidence"
    FINDING ||--o{ ACTION_ITEM : "generates"
    TENANT ||--o{ LLM_USAGE : "consumes"
```

The central table is `ai.chunks`: regulations and tenant documents share it, separated by
`tenant_id` (`NULL` = public corpus) and protected by RLS policies.

```sql
-- read: public corpus + the caller's own tenant
CREATE POLICY chunks_read ON ai.chunks FOR SELECT
  USING (tenant_id IS NULL
         OR tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

-- public corpus: only the regulation-ingestion role can write it
CREATE POLICY chunks_corpus_write ON ai.chunks FOR ALL TO corpus_ingestor
  USING (tenant_id IS NULL) WITH CHECK (tenant_id IS NULL);
```

</details>

<details>
<summary><b>📥 Document ingestion flow</b></summary>

Long-running work is asynchronous. The outbox guarantees no event is lost between the database
commit and the queue.

```mermaid
sequenceDiagram
    participant W as Web
    participant A as Core API · .NET
    participant B as Blob
    participant Q as Queue
    participant I as Ingestion worker · Python
    participant P as PostgreSQL
    W->>A: POST /documents (file)
    A->>B: store file
    A->>P: Document = Uploaded + outbox event (same transaction)
    A-->>W: 202 Accepted
    A->>Q: publish DocumentUploaded
    Q->>I: deliver message
    I->>B: download file
    I->>I: parse · chunk · embed
    I->>P: write ai.chunks with the message's tenant
    I->>Q: publish DocumentIndexed
    Q->>A: deliver message
    A->>P: Document = Indexed
    A-->>W: SignalR: document ready
```

1. **Parse** — Docling for PDF/DOCX (keeps headings and tables); scans and photos go through OCR or a multimodal LLM.
2. **Structural chunking** — regulations split by article/section, keeping the hierarchy in `section_path` (e.g. `Chapter 3 > 3-501.16`). Tables such as temperature logs become structured text + metadata (dates, out-of-range values).
3. **Enrichment** — language, document type and expiry date extracted with structured output.
4. **Indexing** — embedding + `tsvector` using the chunk's language config (`portuguese` / `english`).
5. **Idempotency** — content hash; a new document version replaces the previous chunks in the same transaction.

</details>

<details>
<summary><b>💬 Retrieval & generation (chat)</b></summary>

```mermaid
flowchart LR
    Q["Question<br/><sub>PT or EN</sub>"] --> RW["Query rewrite<br/><sub>cheap model</sub>"]
    RW --> V["Vector top-k<br/><sub>pgvector</sub>"]
    RW --> L["Lexical top-k<br/><sub>full-text</sub>"]
    V --> RRF["Reciprocal<br/>Rank Fusion"]
    L --> RRF
    RRF --> RR["Multilingual<br/>re-ranker"]
    RR -->|score below threshold| REF["Refuse"]
    RR --> GEN["Answer + claims<br/><sub>strong model · JSON</sub>"]
    GEN --> GUARD["Citation guard<br/><sub>cited IDs ⊆ retrieved IDs</sub>"]
    GUARD --> SSE["SSE stream<br/><sub>FastAPI → YARP → web</sub>"]
```

- Tenant, establishment and jurisdiction reach the AI service in **internal headers derived from the validated JWT** — never from the request body.
- **Model routing:** a cheap model for query rewriting and classification; a strong model only for the final answer and for inspection judgments.

</details>

<details>
<summary><b>🔍 Simulated inspection agent</b></summary>

```mermaid
stateDiagram-v2
    [*] --> Plan
    Plan --> Evaluate: applicable requirements grouped in batches
    state Evaluate {
        [*] --> GatherEvidence
        GatherEvidence --> Judge
        Judge --> [*]
    }
    Evaluate --> Consolidate: all batches evaluated
    Consolidate --> ActionPlan
    ActionPlan --> HumanReview
    HumanReview --> [*]
```

- **Plan** — selects applicable requirements from regulation metadata (establishment type + jurisdiction). Deterministic, no LLM. Groups them into themes: personal hygiene, temperature control, pests, water, waste.
- **Evaluate** — parallel fan-out per batch (LangGraph `Send`). For each requirement, gather evidence from the establishment's documents; an LLM judge classifies it as *compliant*, *non-compliant* or *insufficient evidence*, with a **double citation** (regulation article + customer passage) and a confidence level.
- **Consolidate** — deduplicate and sort by severity.
- **Action plan** — one concrete action per finding, with a suggested deadline.
- **Human review** — findings are born pending; rejections feed the eval dataset.
- **Resilience & budget** — Postgres checkpoints (`langgraph-checkpoint-postgres`) and a token ceiling per inspection, charged to the tenant's budget.

</details>

<details>
<summary><b>🔔 Change monitor</b></summary>

- A scheduled job compares the new version of a regulation with the previous one, article by article (`section_path` + hash).
- Changed articles are searched semantically across the documents of tenants in the same jurisdiction.
- The result is a notification with **what changed** and **which passage of the customer's SOP is affected**.
- For a reproducible demo, a v1 and a v2 with controlled changes are ingested.

</details>

---

## Engineering decisions

Each row becomes a full record in [`docs/adr/`](docs/adr/).
<!-- TODO(fill later): link each ADR number to its file once docs/adr/ is written -->

| ADR | Decision | Why | Trade-off |
|---|---|---|---|
| 001 | **Core in .NET, AI in Python** | .NET is a strong fit for B2B domain modeling; the RAG and eval ecosystem is more mature in Python | Two runtimes to maintain |
| 002 | **One PostgreSQL**, schemas `core` (EF Core) and `ai` (Alembic) | pgvector + full-text next to the business rules, no sync with an external vector DB | At millions of chunks, evaluate Qdrant or Azure AI Search |
| 003 | **Tenant isolation with Row-Level Security** | Defense in depth: even with a query bug, the database won't return another tenant's rows | Requires care with connection pooling (tenant set per transaction) |
| 004 | **.NET as the single entry point** (YARP in front of Python) | Auth, rate limiting and token budget in one place; Python is never exposed | One extra network hop on chat |
| 005 | **Async with Outbox for long tasks, SSE for chat** | Ingestion and inspection can take minutes; the outbox guarantees no event is lost | Eventual consistency in the UI, solved with SignalR |
| 006 | **Hand-written retrieval, LangGraph only for agents** | Full control, and every step is explainable | More code of our own |
| 007 | **Verifiable citations** | Structured output + a check that every cited passage was actually retrieved | One extra validation step |
| 008 | **Embedding model behind an interface, chosen by eval** | Swapping models becomes an experiment with a number, not an opinion | — |

<details>
<summary><b>Why PostgreSQL and not SQL Server?</b></summary>

SQL Server 2025 ships a vector type and would work. pgvector won on ecosystem: it is the default
in the Python AI tooling, it has HNSW indexes and mature full-text search in the same engine, and
Azure offers it managed (Flexible Server).

</details>

---

## Security

- **Indirect prompt injection** — document content always enters prompts delimited as *data*; agents have **no write tools**; every state change goes through a human. The eval suite includes planted malicious documents.
- **Isolation** — RLS + integration tests for cross-tenant leaks + adversarial evals (*"show me the other customer's SOP"*). Target: zero leaks.
- **Edge** — the Python service has no public ingress; YARP and FastAPI talk over an internal token.
- **Abuse & cost** — ASP.NET Core rate limiting + per-tenant token budget.
- **Uploads** — type and size validation.
- **Personal data** — employee names found in documents are masked in logs and LLM traces.
- **Secrets** — nothing secret lives in this repository. Local development uses `.env` files (git-ignored) and `dotnet user-secrets`; CI and deploy read from **GitHub Actions secrets**.

---

## Evals

Evals are the differentiator: every quality claim in this README should be backed by a number.

| Layer | Metric | How it's measured |
|---|---|---|
| Retrieval | recall@k, MRR | 80–100 questions with annotated expected chunks |
| Generation | faithfulness, citation precision, relevance | LLM-as-judge (DeepEval or Ragas) + deterministic check of cited IDs |
| Inspection | finding precision & recall | 3–5 synthetic establishments with planted non-conformities |
| Security | cross-tenant leaks, successful injections | Adversarial prompts + malicious documents |
| Operations | cost per question / per inspection, p95 latency | Langfuse + OpenTelemetry |

- **Calibrated judge** — before trusting LLM-as-judge, it is compared against ~30 hand-labeled examples and the agreement rate is reported.
- **Experiments** — vector-only vs hybrid · with vs without re-ranker · local BGE-M3 vs hosted embeddings.

<!-- TODO(fill later): experiment results table (one row per experiment, with the measured numbers) -->

---

## Stack

<p>
  <img src="https://img.shields.io/badge/.NET%2010-512BD4?logo=dotnet&logoColor=white" alt=".NET 10"/>
  <img src="https://img.shields.io/badge/ASP.NET%20Core-512BD4?logo=dotnet&logoColor=white" alt="ASP.NET Core"/>
  <img src="https://img.shields.io/badge/EF%20Core-68217A?logo=dotnet&logoColor=white" alt="EF Core"/>
  <img src="https://img.shields.io/badge/.NET%20Aspire-512BD4?logo=dotnet&logoColor=white" alt=".NET Aspire"/>
  <img src="https://img.shields.io/badge/YARP-gateway-512BD4" alt="YARP"/>
  <img src="https://img.shields.io/badge/SignalR-realtime-512BD4" alt="SignalR"/>
  <img src="https://img.shields.io/badge/Python-3.12+-3776AB?logo=python&logoColor=white" alt="Python"/>
  <img src="https://img.shields.io/badge/FastAPI-009688?logo=fastapi&logoColor=white" alt="FastAPI"/>
  <img src="https://img.shields.io/badge/LangGraph-1C3C3C?logo=langchain&logoColor=white" alt="LangGraph"/>
  <img src="https://img.shields.io/badge/Docling-parsing-FF6F00" alt="Docling"/>
  <img src="https://img.shields.io/badge/React-61DAFB?logo=react&logoColor=black" alt="React"/>
  <img src="https://img.shields.io/badge/TypeScript-3178C6?logo=typescript&logoColor=white" alt="TypeScript"/>
  <img src="https://img.shields.io/badge/PostgreSQL-4169E1?logo=postgresql&logoColor=white" alt="PostgreSQL"/>
  <img src="https://img.shields.io/badge/pgvector-HNSW-4169E1" alt="pgvector"/>
  <img src="https://img.shields.io/badge/RabbitMQ-FF6600?logo=rabbitmq&logoColor=white" alt="RabbitMQ"/>
  <img src="https://img.shields.io/badge/MinIO-C72E49?logo=minio&logoColor=white" alt="MinIO"/>
  <img src="https://img.shields.io/badge/Keycloak-OIDC-4D4D4D?logo=keycloak&logoColor=white" alt="Keycloak"/>
  <img src="https://img.shields.io/badge/OpenTelemetry-000000?logo=opentelemetry&logoColor=white" alt="OpenTelemetry"/>
  <img src="https://img.shields.io/badge/Langfuse-LLM%20observability-0A0A0A" alt="Langfuse"/>
  <img src="https://img.shields.io/badge/Azure%20Container%20Apps-0078D4?logo=microsoftazure&logoColor=white" alt="Azure Container Apps"/>
  <img src="https://img.shields.io/badge/Terraform-7B42BC?logo=terraform&logoColor=white" alt="Terraform"/>
  <img src="https://img.shields.io/badge/Testcontainers-tests-5FA04E" alt="Testcontainers"/>
  <img src="https://img.shields.io/badge/NetArchTest-boundaries-8B5CF6" alt="NetArchTest"/>
  <img src="https://img.shields.io/badge/GitHub%20Actions-CI-2088FF?logo=githubactions&logoColor=white" alt="GitHub Actions"/>
</p>

---

## Getting started

> *Coming soon — available once phase 0 ships.*

<!-- TODO(fill later): real prerequisites and commands once the monorepo is scaffolded
Prerequisites: .NET 10 SDK, Python 3.12+, Node 20+, Docker.

```bash
git clone https://github.com/KauaVilasBoas/Normetry.git
cd Normetry
cp .env.example .env              # fill in your own LLM provider key
dotnet run --project src/core/Normetry.AppHost   # Aspire boots everything
```

The Aspire dashboard shows every service, container and OpenTelemetry trace.
`docker compose -f deploy/docker-compose.yml up` is the alternative without Aspire.
-->

### Tests

<!-- TODO(fill later): test commands for both services -->
- **Domain** — unit tests.
- **Integration** — Testcontainers with a real PostgreSQL, **including RLS**: tenant A must never read tenant B's chunks.
- **Architecture** — NetArchTest rules that fail the build when a layer reaches where it shouldn't.
- **Evals** — reduced suite on every PR that touches prompts, retrieval or agents.

---

## Project structure

```
normetry/
├── src/
│   ├── core/                      # .NET 10
│   │   ├── Normetry.Domain/
│   │   ├── Normetry.Application/
│   │   ├── Normetry.Infrastructure/
│   │   ├── Normetry.Api/
│   │   └── Normetry.AppHost/      # Aspire
│   ├── ai/                        # Python
│   │   ├── normetry_ai/
│   │   │   ├── api/               # FastAPI: chat and search
│   │   │   ├── ingestion/
│   │   │   ├── retrieval/
│   │   │   ├── agents/            # LangGraph
│   │   │   └── llm/               # providers, routing, cost
│   │   └── tests/
│   └── web/                       # React + TypeScript
├── evals/
│   ├── datasets/
│   ├── scenarios/                 # synthetic establishments
│   └── reports/
├── corpus/                        # versioned regulations + download scripts
├── docs/
│   ├── adr/
│   └── architecture.md
├── deploy/
│   ├── docker-compose.yml
│   └── terraform/
└── .github/workflows/
```

---

## Roadmap

| Phase | Delivery | Status |
|---|---|---|
| **0 · Foundation** | Monorepo, Aspire, auth, tenants & establishments, PostgreSQL with RLS + isolation tests, CI | 🚧 In progress |
| **1 · Regulation RAG** | Ingest the 3 regulations, hybrid search + re-ranker, cited chat, retrieval & generation evals in CI | Planned |
| **2 · Simulated inspection** | Async upload & ingestion, LangGraph agent, human review, planted scenarios | Planned |
| **3 · Polish** | Change monitor, MCP server, Langfuse, Azure deploy, showcase video | Planned |

**Phase 3 extras:**
- **MCP server** — the .NET core exposes read-only tools (establishments, expiring documents, latest findings) through the official C# MCP SDK, so Claude Desktop can connect straight to the product.
- **Second retrieval adapter** — Azure AI Search behind the same interface, compared against pgvector by the evals.

**Out of the MVP:** billing, corporate SSO, mobile app.

<!-- TODO(fill later): "What I learned" + "What I'd do differently" sections after phase 1 ships -->

---

## Author

**Kauã Vilas Boas,** Backend Engineer (.NET · C#) · builds production-grade .NET systems and
libraries with documented architecture and CI/CD.

<p>
  <a href="https://www.linkedin.com/in/kauavilasboas/">
    <img src="https://img.shields.io/badge/LinkedIn-kauavilasboas-0A66C2?logo=linkedin&logoColor=white" alt="LinkedIn"/>
  </a>
  <a href="https://github.com/KauaVilasBoas">
    <img src="https://img.shields.io/badge/GitHub-KauaVilasBoas-181717?logo=github&logoColor=white" alt="GitHub"/>
  </a>
  <a href="mailto:kauavboas@gmail.com">
    <img src="https://img.shields.io/badge/Email-kauavboas%40gmail.com-EA4335?logo=gmail&logoColor=white" alt="Email"/>
  </a>
</p>

Based in Brazil (UTC−3), with full overlap with US East Coast and European afternoon working hours.
Open to remote opportunities.

---

## License

[MIT](LICENSE)
