# Phase 1 design: regulation RAG

Date: 2026-11-12. Status: draft for review.

## Intent

Phase 1 turns Normetry from an authenticated, tenant-isolated shell into a product someone can try. A user from a food business logs in, picks one of their establishments and asks questions about the regulation that applies to it. The answer streams in, every statement links to the article it comes from, and follow-up questions keep the conversation's context.

This is a public portfolio project. The demo has to show what the README promises: retrieval written by hand, citations that can be checked, refusals when the regulation has no answer, and evals with measured numbers. Nobody on the team has legal training, so correctness rests on literal quotes from the official texts, not on legal opinion.

## Goals

1. Ingest the three regulations in `corpus/` (FDA Food Code 2026 in English, Regulation (EC) 852/2004 in English and Portuguese, Anvisa RDC 216/2004 in Portuguese) into `ai.chunks`, cut along their own structure.
2. Hybrid retrieval: pgvector top-k plus PostgreSQL full-text top-k, Reciprocal Rank Fusion, then a multilingual re-ranker (ADR 006). Filtered by jurisdiction.
3. A multi-turn chat with streamed answers and verifiable citations (ADR 007), scoped to one establishment.
4. A web app with login, an establishment picker and the chat screen.
5. A golden dataset of about 60 questions, built and reviewed in development sessions, and evals that report measured numbers.
6. A per-user daily chat quota whose limits are configuration, not code.

## Non-goals

- Tenant document upload, the outbox and the queue (phase 2; message contracts stay in #16).
- Answers that compare jurisdictions. The design keeps this a small change later (see "Jurisdiction scope").
- Hosted embedding models. The only paid provider is Anthropic, which has no embeddings API.
- Langfuse, Azure deploy, the MCP server (phase 3).
- Billing, plan self-service and an admin interface.

## Anti-goals

The phase fails, even if every feature works, if any of these happens:

- An answer cites a regulation from another jurisdiction than the establishment's.
- A citation points at text that does not support the claim it is attached to.
- A claim reaches the user before the citation guard has checked it.
- CI makes paid API calls.
- A model id, a quota or a price is hard-coded instead of configured.
- Any of the invariants in the root `CLAUDE.md` is bent (schema ownership, RLS, tenant from the JWT, Python never public, no secrets in the repository).

## Decisions

### Jurisdiction scope

- A conversation belongs to one establishment, and its jurisdiction is fixed for the conversation.
- The client never sends the jurisdiction. The chat routes carry the establishment id. The core loads that establishment under RLS (404 if it is not the caller's tenant), then sets `X-Normetry-Establishment-Id` and `X-Normetry-Jurisdiction`, the two headers reserved in `src/shared/contracts/internal-http.md`. That contract gets updated in the same pull request as the code.
- Retrieval takes a list of jurisdictions, never a single value. A policy function in the AI service maps the request context to that list; today it returns the establishment's jurisdiction only. Comparing jurisdictions later means changing that function and labelling the jurisdiction on citations, then running the evals.
- Every regulation chunk carries its jurisdiction in a form retrieval can filter on efficiently.

### Languages

- BR establishments ask in Portuguese, US establishments in English, EU establishments in either (both official texts are ingested). The chat answers in the language of the question.
- The AI service detects the question's language locally (Portuguese or English). For an EU establishment, retrieval searches only the version of 852/2004 in that language, so one provision never comes back twice and the dataset's quotes stay in the item's language. (Author's call.)
- A citation opens the article in the language of the text that was retrieved.

### Conversation and follow-ups

- Conversations and messages live in schema `ai`, under RLS by tenant. A user lists and opens only their own conversations, and can delete one.
- A stored answer keeps its claims and citations, so old citations still open.
- Before retrieval, a follow-up is rewritten into a standalone question from the recent turns. The rewrite uses a configurable model (default Claude Haiku 5.5). The evals compare it against a free baseline (the last user turns joined into one search query), and the better one stays.
- The model sees the last N turns; N is configuration.

### Answers and citations (ADR 007, as applied here)

- Each retrieved chunk enters the prompt with an id, delimited as data, never as instructions.
- The model returns claims. Each claim has its text, the ids of the chunks that support it and, for each id, a literal quote from that chunk. (Author's call: this goes past ADR 007, which only checks ids, so that support is checked at runtime and not only in the evals. Update ADR 007.)
- The guard drops a claim when a cited id is outside the retrieved set or its quote does not appear in that chunk (after normalizing whitespace). If no claim survives, the answer is "not enough basis in the regulation".
- If the re-ranker's best score is under a configured threshold, the system refuses before calling the model.
- Only guarded claims and fixed system messages (refusal, unavailable, quota) reach the user. There is no free introductory text.
- Streaming is at claim granularity: the server sends a claim only after the guard has checked it, and the screen reveals its text progressively, which gives the token-by-token look DESIGN.md asks for. Since refusal means zero surviving claims, a claim already shown is never taken back.
- Each stored answer keeps a copy of what it cited: the chunk text, the quote, `section_path`, the source title and version. "View source →" opens that copy with the quote highlighted, plus a link to the official page from `corpus/manifest.json`. A new corpus version never breaks an old citation, and the app never serves the regulation files themselves.
- The chat model is configuration. It starts as `claude-opus-5-5`; the model comparison run (below) gives the user the numbers to choose the default.
- An Anthropic error (rate limit, exhausted credit, outage) becomes a clear "the assistant is unavailable right now" message, not a 500. The account has a monthly cap of about US$ 5 set in the Anthropic console, so this will happen on the public demo.

### Daily quota per user

- Plans and their limits are configuration in the core, for example `demo` = 20 chat messages per user per day, `standard` = 200, `unlimited` = no limit. A tenant without an entry gets `demo`; the configuration maps other tenants to their plan by tenant id. Changing a client's limit is a configuration change and a restart, never a code change or a deploy. (Author's call: there is no admin interface or billing yet. When billing arrives, the plan moves to the database.)
- Counts live in PostgreSQL (schema `core`, RLS by tenant), one row per tenant, user and UTC day, so they survive restarts and work with more than one API instance.
- Every chat message counts, follow-ups included. Over the limit, the core answers 429 with `Retry-After` until the next UTC day and does not forward the request.
- Responses carry `RateLimit-Limit`, `RateLimit-Remaining` and `RateLimit-Reset`, so the screen can show what is left. Not `X-Normetry-*`: the gateway strips those from responses.
- The AI service records tokens and cost per answer, per tenant and user, in schema `ai`. This is the start of the usage and budget context; budgets themselves are not in phase 1.

### Models that run locally

- Embeddings: BGE-M3 (1024 dimensions, matching `ai.chunks.embedding`), on CPU.
- Re-ranker: bge-reranker-v2-m3, on CPU.
- ADR 008's embedding comparison becomes BGE-M3 against a second multilingual local model, measured by the retrieval evals.

### Ingestion

- A command in the AI service that the developer runs, also available as an AppHost resource started on demand. It runs as `corpus_ingestor`.
- Sources: the files listed in `corpus/manifest.json`, fetched and verified by `corpus/scripts/fetch_corpus.py`. PDFs and the RDC HTML page are parsed with Docling (already the plan in `src/ai/CLAUDE.md`).
- What is ingested, and therefore citable (author's call):
  - FDA Food Code: chapters 1 to 8, the code provisions. No annexes in phase 1: Annex 3 repeats the section numbers to explain them and binds nothing, and Annex 4 holds the third-party table.
  - Regulation 852/2004: the articles and the annexes (Annex II holds most of the hygiene requirements). No recitals.
  - RDC 216/2004: the Technical Regulation, items 1 to 5 of the annex. The resolution's preamble articles stay out.
  - A "not covered" item in the dataset means not covered by these parts.
- Chunks follow the structure of each text: FDA sections (for example `3-501.16`), EU articles and annex chapters, RDC items (for example `4.8.15`). `section_path` holds that location; the source, version and language come from the manifest.
- Idempotent: running it twice changes nothing, and a new version of a source replaces its chunks (content hash, as `ai.chunks` already supports).

### Evals: what runs where, and what costs money

| What | Where | Cost |
|---|---|---|
| Building and reviewing the golden dataset | Claude Code sessions (the developer's subscription) | none |
| Retrieval evals: recall@k and MRR against the golden dataset | CI on pull requests that touch ingestion, retrieval or the dataset; also locally | none (local models, local PostgreSQL) |
| Generation pipeline tests with a fake model: guard, refusal threshold, jurisdiction filter, claim streaming, quota | CI on every pull request | none |
| Generation quality: real answers from the real pipeline | on demand, run by the developer when prompts or models change | cents per run with Haiku, about US$ 2 with Opus |
| Judging generated answers (faithfulness, citation precision, refusal correctness) | Claude Code sessions | none |
| Model comparison (Haiku 5.5, Sonnet 5.5, Opus 5.5 on the golden dataset) | once, on demand | about US$ 3 in total |

No paid API call runs in CI. The README's Evals section and roadmap say "generation evals in CI" and "LLM-as-judge (DeepEval or Ragas)"; both get rewritten to match this table.

### Golden dataset

- About 60 items across the three regulations, roughly 20 each, in the languages above. About 10 of them are 2 or 3 turn conversations that test follow-ups.
- Item types: a direct fact (a temperature, a frequency, a requirement), a question that needs two provisions, a question the regulation does not answer (expected: refusal), a wrong-jurisdiction question (for example a BR establishment asking about something only the FDA Food Code covers; expected: the regulation does not cover it, with no foreign citation) and questions with a misleading premise.
- Each item records: id, establishment jurisdiction, language, the turns, the expected answer, the expected behavior (answer or refuse), and for each supporting provision the source id, `section_path` and a literal quote from the official text.
- Labels point at quotes, not chunks. A retrieved chunk counts as a hit when it contains the quote, so changing the chunking does not invalidate the dataset.
- Validation in three passes:
  1. A script checks that every quote appears in the parsed source text, after normalizing whitespace and line-break hyphenation. It runs in CI.
  2. The author rereads every item against the source.
  3. An independent reviewer agent per regulation, briefed as a lawyer specialized in food-safety regulation, gets only the source text and the items and rules each item correct, wrong or ambiguous with a reason. Wrong items are fixed, ambiguous ones dropped. The verdicts are kept next to the dataset.
- The dataset is not labeled as legal advice or as lawyer-reviewed.

### Web app (`src/web`, first version)

- React and TypeScript, following `docs/design/DESIGN.md` and `reference.png` (`normetry-design-system` skill). The `ux-ui-designer` agent writes the screen spec before building and reviews after (root `CLAUDE.md`).
- Login with Keycloak through a public `normetry-web` client (authorization code with PKCE). The API already accepts `normetry-web` tokens; the realm does not have the client yet.
- One screen with three parts: conversation list on the left, the open conversation in the middle with each claim revealed as it arrives, and a source panel that opens a citation's stored copy ("View source →" in DESIGN.md). An establishment picker starts a conversation.
- The remaining quota is visible. A 429 and the "unavailable" state have their own messages.
- Upload, review and dashboards are phase 2.

### Development seed

- The Development-only seed adds demo establishments so each jurisdiction can be tried with the existing demo users, for example a BR and an EU establishment for Demo Bistro and a US one for Demo Bakery.

## Success measures

These targets are the author's proposal, to be confirmed once the first numbers exist:

- Retrieval: recall@10 of at least 0.9 on answerable items.
- Citations: 100% of shown claims cite a retrieved chunk with a quote found in it (enforced by the guard, tested in CI).
- Faithfulness: zero claims on the golden dataset whose quote does not actually support them, judged in the review sessions.
- Wrong-jurisdiction citations: zero.
- Refusals: at least 90% of the "not covered" items refused.
- p95 time to first streamed claim under 6 seconds on a developer machine.

## Constraints

- The invariants in the root `CLAUDE.md`: Python writes only `ai`, .NET only `core`; RLS with the tenant per transaction; tenant from the JWT; the AI service reachable only through the gateway; nothing secret in the repository.
- Every new tenant-owned table follows the `rls-table` skill, with its isolation tests.
- Every core change to persistence, auth or the gateway gets a `tenant-isolation-reviewer` review.
- The Anthropic API key goes in user secrets and AppHost parameters, never in files.
- Local models run on CPU and must keep a retrieval eval run in CI reasonable; caching model files and corpus embeddings in CI is expected.
- Workflow from the root `CLAUDE.md`: one issue per feature, branch, pull request, atomic Conventional Commits.

## Left to the builder

- Table and column names, and whether jurisdiction is a column or comes from a sources table.
- Chunk size limits inside a structural unit, and how annex tables are cut.
- Prompt wording, the JSON shape of claims, the refusal threshold value (tuned with the evals).
- The SSE event names and payloads.
- The web stack details (bundler, router, state, test tools), within the design system.
- How the retrieval eval fits in CI time (model and embedding caches, a reduced corpus for pull requests).
- The order of work and how it splits into issues, except that the golden dataset comes first, since every other piece is measured against it.

## Records to update

- README: Evals section, roadmap row for phase 1, Capabilities when the chat works.
- ADR 007: claims carry a literal quote, and the guard checks it.
- ADR 008: hosted embeddings out of scope for now; comparison between local models.
- A new ADR for plans and quotas if the reviewer of the plan considers it significant.
