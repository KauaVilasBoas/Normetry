"""Answer a question from retrieved chunks, one guarded claim at a time (ADR 007).

`answer()` yields zero or more Claim events, then exactly one final event:

- Answered: at least one claim survived the guard.
- Refused: not enough basis in the regulation, either before any model call (best re-ranker
  score under the threshold) or because no claim survived the guard. No Claim came before it.
- Unavailable: the model failed (rate limit, exhausted credit, outage, no API key). Claims
  yielded before it were checked and stand; the answer is incomplete.

Nothing the model wrote reaches the caller except claims that passed the guard.
"""

import logging
from collections.abc import AsyncGenerator, Callable, Sequence
from contextlib import aclosing
from dataclasses import dataclass
from enum import StrEnum
from typing import Any, Protocol

import anyio

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
        """Raw claim objects and Usage snapshots: the last Usage handed on is what the call used.

        Raises ModelUnavailable when the model fails.
        """
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

    `record_usage` stores the model call's tokens and cost once, before the final event, typically
    `functools.partial(llm.usage.record_usage, engine, tenant_id=..., user_id=...,
    prices=settings.model_prices)` with the tenant and user from the internal headers. It runs
    in a worker thread, also when the caller stops early or the model fails mid-stream: then it
    gets the usage so far.
    """
    if not chunks or max(chunk.score for chunk in chunks) < settings.answer_min_rerank_score:
        yield Refused(RefusalReason.LOW_SCORE)
        return
    excerpts = excerpt_ids(chunks)
    items = model.stream_claims(
        system_prompt(settings.answer_min_quote_words), user_message(question, excerpts)
    )
    shown = dropped = 0
    usage: Usage | None = None  # the latest snapshot: what the call has used so far
    failed = False
    try:
        # aclosing: a caller that stops early (the client went away) closes the model stream now,
        # not when the generator is garbage-collected.
        async with aclosing(items):
            async for item in items:
                if isinstance(item, Usage):
                    usage = item
                    continue
                claim = check_claim(item, excerpts, settings.answer_min_quote_words)
                if claim is None:
                    dropped += 1
                    continue
                shown += 1
                yield claim
    except ModelUnavailable:
        failed = True
    finally:
        # Also when the caller stopped early or the model failed: a call cut short is paid for.
        # ponytail: the SDK reports output tokens only with the final message_delta, so a call cut
        # short records its input tokens and none of its output; count streamed text if budgets
        # need the rest.
        if usage is not None:
            # Shielded: a cancelled request (the client went away) still writes the row.
            with anyio.CancelScope(shield=True):
                await anyio.to_thread.run_sync(record_usage, usage)
    if failed:
        yield Unavailable()
        return
    logger.info("Answer: %d claims shown, %d dropped by the guard", shown, dropped)
    yield Answered() if shown else Refused(RefusalReason.NO_SUPPORTED_CLAIMS)
