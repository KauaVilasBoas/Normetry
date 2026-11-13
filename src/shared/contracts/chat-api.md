# Chat API: conversations and answers over SSE

The AI service's chat routes, as the web app calls them through the core's gateway. Paths below are what the web app calls: `/api` is the web app's own prefix for the core (its dev server strips it), and the core's gateway forwards `/ai/...` to the AI service without `/ai` (`internal-http.md`). The browser never sends tenant, user, establishment or jurisdiction: the gateway derives them from the token and from the establishment in the path. Every body is JSON in camelCase; times are ISO 8601 with an offset (`2026-11-13T14:44:54.836893+00:00`). The AI service implements this in `src/ai/normetry_ai/api/conversations.py`.

## Routes

| Method and path | Body | Success | Errors |
|---|---|---|---|
| `POST /api/ai/establishments/{establishmentId}/conversations` | none | `201` with a [conversation summary](#conversation-summary) | `404`: establishment not the caller's (core) |
| `GET /api/ai/conversations` | | `200` with an array of summaries, the caller's conversations in every establishment, latest `updatedAt` first | |
| `GET /api/ai/conversations/{conversationId}` | | `200` with a [conversation](#conversation) | `404`: not the caller's conversation; `422`: not a UUID |
| `DELETE /api/ai/conversations/{conversationId}` | | `204`, no body; the messages go too | `404`, `422` as above |
| `POST /api/ai/establishments/{establishmentId}/conversations/{conversationId}/messages` | `{"text": "..."}` | `200`, `text/event-stream`: [events](#stream) | `404`: not the caller's conversation, or posted under another establishment or jurisdiction than the conversation's; `422`: invalid body; `429`: daily quota (core); `503`: the question could not be stored, so nothing is answered |

- A conversation belongs to one user and one establishment. Other users of the same tenant, and other tenants, get `404` for it on every route.
- No query parameter names the establishment, so the list holds every establishment's conversations; the web filters by `establishmentId`.
- A conversation keeps the jurisdiction of the establishment it was started in.
- The message body has exactly one field, `text`: 1 to 2,000 characters after trimming. Any other field, or none, gets `422` and nothing is stored or answered.
- Each posted message counts against the daily quota in the core, which sets the `RateLimit-*` headers (see `internal-http.md`). The AI service neither counts nor limits.
- Errors carry a JSON `detail`, for logs, not for display.

## Shapes

### Conversation summary

```json
{
  "id": "6f1c...",
  "establishmentId": "2b9c...",
  "title": "How cold must food be held?",
  "updatedAt": "2026-11-13T14:44:54.836893+00:00"
}
```

`title` is the first question, `null` until one is asked. `updatedAt` moves with every question and answer.

### Conversation

A summary plus `messages`, oldest first:

```json
{
  "id": "6f1c...",
  "establishmentId": "2b9c...",
  "title": "How cold must food be held?",
  "updatedAt": "...",
  "messages": [
    {"id": "...", "role": "user", "text": "How cold must food be held?", "createdAt": "..."},
    {
      "id": "...",
      "role": "assistant",
      "kind": "answer",
      "reason": null,
      "language": "en",
      "claims": [{"text": "...", "citations": [{"id": "...", "...": "..."}]}],
      "createdAt": "..."
    }
  ]
}
```

An assistant message:

| Field | Value |
|---|---|
| `kind` | `answer`, `refusal` or `unavailable` |
| `reason` | for a refusal only: `low_score` (nothing relevant enough was found; no model call) or `no_supported_claims` (no claim survived the citation guard); otherwise `null` |
| `language` | `en` or `pt`, the language of the question, which the claims are written in. Each citation keeps its chunk's own `language`, which can differ: an English question for a BR establishment cites the Portuguese RDC. `null` only when the turn failed before retrieval (`unavailable`) |
| `claims` | the [claims](#claim) as they were streamed; empty for a refusal, possibly some for `unavailable` |

Refusals and unavailable answers carry no display text: the web shows its own copy.

### Claim

```json
{
  "text": "Hold cold food at 5 °C (41 °F) or below.",
  "citations": [
    {
      "id": "0b6e...",
      "sourceTitle": "FDA Food Code",
      "sourceVersion": "2026",
      "language": "en",
      "sectionPath": "3-501.16",
      "text": "... the whole cited chunk ...",
      "quote": "shall be maintained at 5°C (41°F) or less",
      "quoteStart": 412,
      "quoteEnd": 454,
      "officialUrl": "https://www.fda.gov/..."
    }
  ]
}
```

- A claim has at least one citation, each checked against its chunk before the claim was sent (ADR 007).
- A citation is a copy stored with the answer: `text` is the chunk as it was cited, so an old citation still opens after its source gets a new version. The app never needs to fetch it.
- `text.slice(quoteStart, quoteEnd)` is the quote as the chunk writes it, to highlight. Both are `null` when the quote could not be mapped back exactly; `quote` (normalized whitespace) is then the only guide.
- `id` is unique to the citation, a key for the UI. It identifies nothing else.

## Stream

`POST .../messages` answers `200` with `Content-Type: text/event-stream` (`Cache-Control: no-cache`, `X-Accel-Buffering: no`; the gateway does not buffer). Events:

| Event | `data` | When |
|---|---|---|
| `claim` | a [claim](#claim) plus `language` (the answer's, `pt` or `en`) | zero or more, in order, each already guarded |
| `done` | `{}` | last event of an answer |
| `refusal` | `{"reason": "low_score"}` or `{"reason": "no_supported_claims"}` | the only event of a refusal |
| `unavailable` | `{}` | last event when the model failed (rate limit, credit, outage, no key) or something else broke; claims before it stand |

Every stream ends with exactly one of `done`, `refusal` or `unavailable`. A refusal is a single event with no claims before it. A claim already sent is never taken back. Comment lines (`: ping`) keep an idle stream open; ignore them.

The question is stored before the stream starts, and the answer just before the final event. If the client goes away mid-stream, the answer is stored with the claims sent so far, as `kind: "answer"` (or `unavailable` if none came), so reopening the conversation shows what was stored. If the answer itself cannot be stored, the stream ends with `unavailable`: the claims sent before it stand, but the conversation will not show them.

A follow-up is rewritten into a standalone question from the last turns before retrieval. The rewrite is not shown or stored.
