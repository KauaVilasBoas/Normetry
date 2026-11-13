"""Answer a question from retrieved chunks, one guarded claim at a time (ADR 007).

`answer()` yields zero or more Claim events, then exactly one final event:

- Answered: at least one claim survived the guard.
- Refused: not enough basis in the regulation, either before any model call (best re-ranker
  score under the threshold) or because no claim survived the guard. No Claim came before it.
- Unavailable: the model failed (rate limit, exhausted credit, outage, no API key). Claims
  yielded before it were checked and stand; the answer is incomplete.

Nothing the model wrote reaches the caller except claims that passed the guard.
"""

import asyncio
import logging
from collections.abc import AsyncGenerator, Callable, Sequence
from contextlib import aclosing
from dataclasses import dataclass
from enum import StrEnum
from typing import Any, Protocol

from normetry_ai.answers.guard import Claim, check_claim
from normetry_ai.answers.prompt import excerpt_ids, system_prompt, user_message
from normetry_ai.llm.claude import ModelUnavailable
from normetry_ai.llm.usage import Usage
from normetry_ai.retrieval.models import RetrievedChunk
from normetry_ai.settings import AnswerSettings

logger = logging.getLogger(__name__)


class RefusalReason(StrEnum):
    LOW_SCORE = "low_score"  # retrieval found nothing relevant enough; no model call
    NO_SUPPORTED_CLAIMS = "no_supported_claims"  # the guard dropped every claim


@dataclass(frozen=True, slots=True)
class Answered:
    pass


@dataclass(frozen=True, slots=True)
class Refused:
    reason: RefusalReason


@dataclass(frozen=True, slots=True)
class Unavailable:
    pass


AnswerEvent = Claim | Answered | Refused | Unavailable


class ClaimModel(Protocol):
    """ClaudeClaimModel (llm/claude.py), or a fake in tests."""

    def stream_claims(self, system: str, user: str) -> AsyncGenerator[Any | Usage, None]:
        """Raw claim objects, then one Usage; raises ModelUnavailable when the model fails."""
        ...


async def answer(
    question: str,
    chunks: Sequence[RetrievedChunk],
    *,
    model: ClaimModel,
    settings: AnswerSettings,
    record_usage: Callable[[Usage], None],
) -> AsyncGenerator[AnswerEvent, None]:
    """Yield guarded claims, then Answered, Refused or Unavailable.

    `record_usage` stores the model call's tokens and cost before the final event, typically
    `functools.partial(llm.usage.record_usage, engine, tenant_id=..., user_id=...,
    prices=settings.model_prices)` with the tenant and user from the internal headers. It runs
    in a worker thread.
    """
    if not chunks or max(chunk.score for chunk in chunks) < settings.answer_min_rerank_score:
        yield Refused(RefusalReason.LOW_SCORE)
        return
    excerpts = excerpt_ids(chunks)
    items = model.stream_claims(
        system_prompt(settings.answer_min_quote_words), user_message(question, excerpts)
    )
    shown = dropped = 0
    try:
        # aclosing: a caller that stops early (the client went away) closes the model stream now,
        # not when the generator is garbage-collected.
        async with aclosing(items):
            async for item in items:
                if isinstance(item, Usage):
                    await asyncio.to_thread(record_usage, item)
                    continue
                claim = check_claim(item, excerpts, settings.answer_min_quote_words)
                if claim is None:
                    dropped += 1
                    continue
                shown += 1
                yield claim
    except ModelUnavailable:
        # ponytail: a call that fails mid-stream records no usage (the SDK reports it only at the
        # end); record the partial counts if budgets need them.
        yield Unavailable()
        return
    logger.info("Answer: %d claims shown, %d dropped by the guard", shown, dropped)
    yield Answered() if shown else Refused(RefusalReason.NO_SUPPORTED_CLAIMS)
