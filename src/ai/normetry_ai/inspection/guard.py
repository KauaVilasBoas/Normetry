"""The inspection guard: a judged checklist item reaches the inspection only as far as its quotes
check out (FR-006). Deterministic, no model call. It matches quotes as the answers guard does
(answers/guard.py `verify_quote`: normalize_text, at least `min_quote_words` words, an id that was
in the prompt), one quote at a time:

- A quote with an id that was not in its list of excerpts, or a passage not in that excerpt, is
  removed. A document id in regulation_quotes is unknown there, and the other way round.
- No regulation quote left: the finding is dropped (None).
- compliant or non_compliant with no document quote left becomes insufficient_evidence. Its
  rationale and action argued from passages that are not there, so they are replaced by fixed
  text and the confidence is low.
- insufficient_evidence keeps no document quote.
- compliant has no action; any other verdict has one, the fixed template when the model gave none.

Item id, theme, title and severity come from the checklist item, never from the model.
"""

from collections.abc import Mapping
from dataclasses import dataclass
from typing import Literal

from normetry_ai.answers.guard import Citation, verify_quote
from normetry_ai.inspection.checklist import ChecklistItem
from normetry_ai.messaging.contracts import EvidenceCitation, RegulationCitation
from normetry_ai.retrieval.models import RetrievedChunk

Verdict = Literal["compliant", "non_compliant", "insufficient_evidence"]
Confidence = Literal["low", "medium", "high"]
VERDICTS: tuple[Verdict, ...] = ("compliant", "non_compliant", "insufficient_evidence")
CONFIDENCES: tuple[Confidence, ...] = ("low", "medium", "high")
# The bounds of contracts.py Finding.rationale and Action.text.
RATIONALE_LIMIT, ACTION_LIMIT = 4000, 2000
UNVERIFIED_RATIONALE = (
    "The judge cited passages of the establishment's documents that could not be found in them, "
    "so no document shows whether this requirement is met."
)


def fallback_action(item: ChecklistItem) -> str:
    return f"Provide records showing: {item.title}"


@dataclass(frozen=True, slots=True)
class JudgedFinding:
    """One checklist item's guarded verdict: contracts.py `Finding` but for the action's due date,
    which the action plan sets from the severity."""

    item_id: str
    theme: str
    title: str
    severity: str
    verdict: Verdict
    confidence: Confidence
    rationale: str  # English
    regulation_citations: tuple[RegulationCitation, ...]  # at least one
    evidence_citations: tuple[EvidenceCitation, ...]  # at least one unless insufficient_evidence
    action: str | None  # None exactly when compliant


def check_finding(
    raw: object,
    item: ChecklistItem,
    regulation: Mapping[str, RetrievedChunk],
    documents: Mapping[str, RetrievedChunk],
    min_quote_words: int,
) -> JudgedFinding | None:
    """The finding for `item` from the model's reply, or None when it cannot stand.

    `regulation` and `documents` are the excerpts by the ids the prompt gave them. `raw` is model
    output, so nothing about its shape is assumed.
    """
    if not isinstance(raw, dict):
        return None
    verdict, confidence = raw.get("verdict"), raw.get("confidence")
    rationale, action = raw.get("rationale"), raw.get("action")
    if verdict not in VERDICTS or confidence not in CONFIDENCES:
        return None
    if not isinstance(rationale, str) or not rationale.strip():
        return None
    regulation_quotes = _verified(raw.get("regulation_quotes"), regulation, min_quote_words)
    if not regulation_quotes:
        return None
    evidence_quotes = _verified(raw.get("evidence_quotes"), documents, min_quote_words)
    if verdict != "insufficient_evidence" and not evidence_quotes:
        verdict, confidence, rationale, action = (
            "insufficient_evidence",
            "low",
            UNVERIFIED_RATIONALE,
            None,
        )
    if verdict == "insufficient_evidence":
        evidence_quotes = []
    if verdict == "compliant":
        action = None
    elif not isinstance(action, str) or not action.strip():
        action = fallback_action(item)
    return JudgedFinding(
        item_id=item.id,
        theme=item.theme,
        title=item.title,
        severity=item.severity,
        verdict=verdict,
        confidence=confidence,
        # ponytail: an over-long text is cut at the contract's bound, mid-word if need be.
        rationale=rationale.strip()[:RATIONALE_LIMIT],
        regulation_citations=tuple(map(_regulation_citation, regulation_quotes)),
        evidence_citations=tuple(map(_evidence_citation, evidence_quotes)),
        action=None if action is None else action.strip()[:ACTION_LIMIT],
    )


def _verified(
    quotes: object, excerpts: Mapping[str, RetrievedChunk], min_quote_words: int
) -> list[Citation]:
    if not isinstance(quotes, list):
        return []
    checked = (
        verify_quote(quote.get("id"), quote.get("quote"), excerpts, min_quote_words)
        for quote in quotes
        if isinstance(quote, dict)
    )
    return [citation for citation in checked if citation is not None]


def _regulation_citation(citation: Citation) -> RegulationCitation:
    chunk = citation.chunk
    return RegulationCitation(
        language=chunk.language,
        text=chunk.content,
        quote=citation.quote,
        quote_start=citation.quote_start,
        quote_end=citation.quote_end,
        source_title=chunk.source_title,
        source_version=chunk.source_version,
        section_path=chunk.section_path or "",
    )


def _evidence_citation(citation: Citation) -> EvidenceCitation:
    chunk = citation.chunk
    return EvidenceCitation(
        language=chunk.language,
        text=chunk.content,
        quote=citation.quote,
        quote_start=citation.quote_start,
        quote_end=citation.quote_end,
        document_id=chunk.document_id,
        document_title=chunk.document_title,
        document_type=chunk.document_type,
        version_number=chunk.document_version,
        section_path=chunk.section_path or "",
    )
