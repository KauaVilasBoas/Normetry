"""The inspection judge (research R4): one structured-output call per checklist item, then the
guard (inspection/guard.py).

For each item the inspection gives the judge:
- the regulation text, by section: `regulation_chunks` loads the corpus chunks of the item's
  `section_paths`, in the establishment's corpus language;
- the evidence: `search_establishment` (retrieval/establishment.py) with `evidence_query(item)`.

The judge has no tools; its only output is its reply, and only what the guard verifies of it
reaches the inspection. Never log the prompt or the reply.
"""

import logging
from collections.abc import Callable, Sequence
from typing import Any, Protocol
from uuid import UUID

import anyio
from sqlalchemy import Connection, text

from normetry_ai.answers.prompt import excerpt_ids
from normetry_ai.db import current_tenant
from normetry_ai.inspection.checklist import ChecklistItem
from normetry_ai.inspection.guard import JudgedFinding, check_finding
from normetry_ai.inspection.prompt import system_prompt, user_message
from normetry_ai.llm.usage import Usage
from normetry_ai.retrieval.models import RetrievedChunk
from normetry_ai.retrieval.policy import corpus_versions, jurisdictions_for
from normetry_ai.settings import AnswerSettings

logger = logging.getLogger(__name__)


class JudgeModel(Protocol):
    """ClaudeJudgeModel (llm/claude.py), or a fake in tests."""

    async def judge(self, system: str, user: str) -> tuple[Any, Usage]:
        """The decoded reply (None when there is none) and the call's Usage.

        Raises ModelUnavailable when the model fails.
        """
        ...


async def judge_item(
    item: ChecklistItem,
    regulation_chunks: Sequence[RetrievedChunk],
    evidence_chunks: Sequence[RetrievedChunk],
    *,
    model: JudgeModel,
    settings: AnswerSettings,
    record_usage: Callable[[Usage], None],
) -> JudgedFinding | None:
    """The guarded finding for `item`, or None when the guard drops it (no verified regulation
    quote, a malformed reply) or there is no regulation text to judge it by (no call then).

    `record_usage` stores the call's tokens and cost once, in a worker thread, also when the
    finding is dropped, typically `functools.partial(llm.usage.record_usage, engine,
    tenant_id=..., user_id=<requestedBy>, prices=settings.model_prices,
    feature=UsageFeature.INSPECTION)`
    with the tenant and user from the queue message; a budget can wrap it to count tokens.
    Raises ModelUnavailable when the model fails (nothing recorded: no usage came back).
    """
    if not regulation_chunks:
        logger.warning("Judge item=%s: no regulation text, not judged", item.id)
        return None
    regulation = excerpt_ids(regulation_chunks, "R")
    documents = excerpt_ids(evidence_chunks, "D")
    words = settings.answer_min_quote_words
    # ponytail: a call cancelled before its response records no usage (non-streaming: tokens
    # come only with the response).
    raw, usage = await model.judge(system_prompt(words), user_message(item, regulation, documents))
    with anyio.CancelScope(shield=True):  # the call was paid for
        await anyio.to_thread.run_sync(record_usage, usage)
    finding = check_finding(raw, item, regulation, documents, words)
    # The item id and outcome only: never the reply or the excerpts.
    logger.info(
        "Judge item=%s verdict=%s regulation=%d evidence=%d",
        item.id,
        finding.verdict if finding else "dropped",
        len(finding.regulation_citations) if finding else 0,
        len(finding.evidence_citations) if finding else 0,
    )
    return finding


def evidence_query(item: ChecklistItem) -> str:
    """The establishment search query for an item: what it requires and which records show it."""
    return f"{item.title}. {item.evidence_hint}"


# The language most of the establishment's indexed documents are in (ties go to 'en'; no row
# when it has none). A deleted document or one not indexed yet has no say.
_DOCUMENT_LANGUAGE = text(
    """
    SELECT language FROM ai.tenant_documents
    WHERE establishment_id = :establishment_id AND deleted_at IS NULL AND language IS NOT NULL
    GROUP BY language
    ORDER BY count(*) DESC, language
    LIMIT 1
    """
)
# Corpus rows only (tenant_id IS NULL, so the corpus index applies and a tenant chunk with the
# same section path never comes back), in the versions corpus_versions picks, joined to
# ai.sources on all three foreign key columns ("Retrieval under RLS" in src/ai/CLAUDE.md). In the
# item's section order, then each section's chunks in text order (a section over CHUNK_LIMIT is
# several chunks), with the id as tie-break for rows stored before positions were (all at 0).
_REGULATION_CHUNKS = text(
    """
    SELECT c.id, c.content, c.section_path, c.language, c.jurisdiction, c.source_id,
           s.title AS source_title, s.version AS source_version, s.official_url
    FROM ai.chunks c
    JOIN ai.sources s
      ON s.id = c.source_id AND s.jurisdiction = c.jurisdiction AND s.language = c.language
    WHERE c.tenant_id IS NULL
      AND (c.jurisdiction, c.language) IN (
          SELECT * FROM unnest(CAST(:jurisdictions AS text[]), CAST(:languages AS text[])))
      AND c.section_path = ANY(CAST(:section_paths AS text[]))
    ORDER BY array_position(CAST(:section_paths AS text[]), c.section_path),
             c.position_in_section, c.id
    """
)


def regulation_chunks(
    connection: Connection,
    establishment_id: UUID,
    jurisdiction: str,
    section_paths: Sequence[str],
) -> list[RetrievedChunk]:
    """The corpus chunks of `section_paths` in the establishment's corpus language: BR's and US's
    only one (pt, en); for EU, the language most of the establishment's indexed documents are in,
    en when it has none (`corpus_versions` with that language gives the same for BR and US).

    The jurisdiction comes from the core (InspectionRequested), never from a client. Runs on a
    connection from `tenant_transaction` (the documents' language is tenant data) and raises
    without one.
    """
    current_tenant(connection)
    language = connection.execute(
        _DOCUMENT_LANGUAGE, {"establishment_id": establishment_id}
    ).scalar_one_or_none()
    pairs = corpus_versions(jurisdictions_for(jurisdiction), language or "en")
    rows = connection.execute(
        _REGULATION_CHUNKS,
        {
            "jurisdictions": [pair[0] for pair in pairs],
            "languages": [pair[1] for pair in pairs],
            "section_paths": list(section_paths),
        },
    ).all()
    return [
        RetrievedChunk(
            chunk_id=row.id,
            content=row.content,
            section_path=row.section_path,
            language=row.language,
            jurisdiction=row.jurisdiction,
            source_id=row.source_id,
            source_title=row.source_title,
            source_version=row.source_version,
            official_url=row.official_url,
            score=1.0,  # chosen by section, not ranked
        )
        for row in rows
    ]
