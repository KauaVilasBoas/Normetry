"""Claude through the Anthropic SDK: the chat model, answering in claims, the rewrite model,
turning a follow-up into a standalone question, and the inspection judge, giving one checklist
item a verdict. All three use structured outputs.

The answer comes back as JSON constrained by CLAIMS_SCHEMA and is streamed: each claim object is
handed on as soon as it is complete, so the citation guard can check it while the rest is still
being generated. Any SDK error (rate limit, exhausted credit, outage, network) becomes
ModelUnavailable, never an exception the client sees.
"""

import json
import logging
import re
from collections.abc import AsyncGenerator
from typing import Any

import anthropic

from normetry_ai.llm.usage import Usage
from normetry_ai.messaging.contracts import Confidence, Verdict
from normetry_ai.settings import AnswerSettings

logger = logging.getLogger(__name__)

# The stop reason of a call whose reply is complete.
END_TURN = "end_turn"
# Logged, with the model and the stop reason, by a call that ended other than with END_TURN
# (max_tokens cut the reply, or the model refused). The paid evals count these records by this
# exact text, which they import.
STOPPED = "Claude %s stopped with %s"
# A rewrite needs little thought; low effort keeps it fast and cheap.
REWRITE_EFFORT = "low"
# ponytail: the judge's effort is fixed at the model's default; make it a setting if the eval asks.
JUDGE_EFFORT = "medium"

# What the model must return. The guard (answers/guard.py) checks every claim again anyway.
CLAIMS_SCHEMA: dict[str, object] = {
    "type": "object",
    "properties": {
        "claims": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "text": {"type": "string"},
                    "citations": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "excerpt_id": {"type": "string"},
                                "quote": {"type": "string"},
                            },
                            "required": ["excerpt_id", "quote"],
                            "additionalProperties": False,
                        },
                    },
                },
                "required": ["text", "citations"],
                "additionalProperties": False,
            },
        }
    },
    "required": ["claims"],
    "additionalProperties": False,
}

# What the rewrite model must return: the question alone, so no draft or note reaches retrieval.
QUESTION_SCHEMA: dict[str, object] = {
    "type": "object",
    "properties": {"question": {"type": "string"}},
    "required": ["question"],
    "additionalProperties": False,
}

_QUOTES: dict[str, object] = {
    "type": "array",
    "items": {
        "type": "object",
        "properties": {"id": {"type": "string"}, "quote": {"type": "string"}},
        "required": ["id", "quote"],
        "additionalProperties": False,
    },
}
# What the inspection judge must return for one checklist item. Quotes and the rationale come
# before the verdict, so the verdict is written after the evidence. Severity, theme and title are
# not asked for: they come from the checklist. The guard (inspection/guard.py) checks it all again.
JUDGE_SCHEMA: dict[str, object] = {
    "type": "object",
    "properties": {
        "regulation_quotes": _QUOTES,
        "evidence_quotes": _QUOTES,
        "rationale": {"type": "string"},
        "verdict": {"type": "string", "enum": [verdict.value for verdict in Verdict]},
        "confidence": {"type": "string", "enum": [confidence.value for confidence in Confidence]},
        "action": {"anyOf": [{"type": "string"}, {"type": "null"}]},
    },
    "required": [
        "regulation_quotes",
        "evidence_quotes",
        "rationale",
        "verdict",
        "confidence",
        "action",
    ],
    "additionalProperties": False,
}


class ModelUnavailable(Exception):
    """The model could not answer: no API key, or the provider failed."""


class ClaimParser:
    """Pulls each complete claim object out of the streamed `{"claims": [...]}` text."""

    _START = re.compile(r'\s*\{\s*"claims"\s*:\s*\[')

    def __init__(self) -> None:
        self._text = ""
        self._position: int | None = None  # just after the last claim read, once inside the list
        self._decoder = json.JSONDecoder()

    def feed(self, delta: str) -> list[Any]:
        self._text += delta
        if self._position is None:
            start = self._START.match(self._text)
            if start is None:
                return []
            self._position = start.end()
        claims = []
        while True:
            position = self._position
            while position < len(self._text) and self._text[position] in " \t\r\n,":
                position += 1
            self._position = position
            # Only an object can be a claim; a partial object never decodes, so nothing is
            # handed on before its closing brace arrives.
            if position >= len(self._text) or self._text[position] != "{":
                return claims
            try:
                claim, self._position = self._decoder.raw_decode(self._text, position)
            except json.JSONDecodeError:
                return claims
            claims.append(claim)


class ClaudeClaimModel:
    """Streams claims from the configured Claude model, then its Usage as the last item."""

    def __init__(
        self, settings: AnswerSettings, client: anthropic.AsyncAnthropic | None = None
    ) -> None:
        self._settings = settings
        self._client = client or _client(settings)

    async def stream_claims(self, system: str, user: str) -> AsyncGenerator[Any | Usage, None]:
        """A first Usage (input tokens), raw claim objects (untrusted: the guard checks them), then
        the final Usage.

        Raises ModelUnavailable, possibly after some claims: those were complete, so they stand.
        """
        if self._client is None:
            logger.error("NORMETRY_ANTHROPIC_API_KEY is not set: every answer is unavailable.")
            raise ModelUnavailable("no Anthropic API key")
        settings, parser = self._settings, ClaimParser()
        try:
            async with self._client.messages.stream(
                **_request(
                    settings.chat_model,
                    settings.chat_max_tokens,
                    settings.chat_effort,
                    CLAIMS_SCHEMA,
                    system,
                    user,
                )
            ) as stream:
                async for event in stream:
                    if event.type == "message_start":
                        # Input tokens are known from the start, output tokens only at the end:
                        # handed on now, so a call cut short still records what it read.
                        yield _usage(settings.chat_model, event.message.usage)
                    elif event.type == "text":
                        for claim in parser.feed(event.text):
                            yield claim
                message = await stream.get_final_message()
        except anthropic.AnthropicError as error:
            raise _unavailable(error) from error
        # max_tokens cuts the last claim (never handed on); refusal ends the answer early.
        _warn_if_stopped(settings.chat_model, message.stop_reason)
        yield _usage(settings.chat_model, message.usage)


class _StructuredModel:
    """One non-streaming call to a configured Claude model whose reply is constrained JSON."""

    def __init__(
        self,
        settings: AnswerSettings,
        model: str,
        max_tokens: int,
        client: anthropic.AsyncAnthropic | None = None,
    ) -> None:
        # Refused at startup, before any call is paid for without a price to record it at.
        if model not in settings.model_prices:
            raise ValueError(f"NORMETRY_MODEL_PRICES has no price for {model!r}")
        self._model, self._max_tokens = model, max_tokens
        self._client = client or _client(settings)

    async def _call(
        self, system: str, user: str, effort: str, schema: dict[str, object]
    ) -> tuple[Any, Usage, str | None]:
        """The decoded JSON reply (None when it does not decode: cut by max_tokens, a refusal),
        the call's Usage, also then, since the call was paid for, and the stop reason.

        Raises ModelUnavailable."""
        if self._client is None:
            raise ModelUnavailable("no Anthropic API key")
        try:
            message = await self._client.messages.create(
                **_request(self._model, self._max_tokens, effort, schema, system, user)
            )
        except anthropic.AnthropicError as error:
            raise _unavailable(error) from error
        text = "".join(block.text for block in message.content if block.type == "text")
        try:
            reply = json.loads(text)
        except json.JSONDecodeError:
            reply = None
        return reply, _usage(self._model, message.usage), message.stop_reason


class ClaudeRewriteModel(_StructuredModel):
    """The standalone question for a follow-up, from a configured Claude model (QUESTION_SCHEMA)."""

    async def complete(self, system: str, user: str) -> tuple[str, Usage]:
        """The question, stripped, and the call's Usage. Raises ModelUnavailable.

        The question is "" when the reply does not hold one (cut by max_tokens, a refusal): the
        call was still paid for, so its Usage comes back with it.
        """
        reply, usage, _ = await self._call(system, user, REWRITE_EFFORT, QUESTION_SCHEMA)
        question = reply.get("question") if isinstance(reply, dict) else None
        return (question.strip() if isinstance(question, str) else ""), usage


class ClaudeJudgeModel(_StructuredModel):
    """The inspection judge (inspection/judge.py): one verdict per checklist item (JUDGE_SCHEMA).
    It has no tools: its only output is the JSON reply, which the inspection guard checks."""

    async def judge(self, system: str, user: str) -> tuple[Any, Usage]:
        """The decoded reply (untrusted: inspection/guard.py checks it; None when the reply holds
        no JSON) and the call's Usage. Raises ModelUnavailable."""
        reply, usage, stop_reason = await self._call(system, user, JUDGE_EFFORT, JUDGE_SCHEMA)
        _warn_if_stopped(self._model, stop_reason)
        return reply, usage


def _request(
    model: str, max_tokens: int, effort: str, schema: dict[str, object], system: str, user: str
) -> dict[str, Any]:
    """The Messages API arguments of one call: the instructions as the system prompt, the data in
    the one user message, and the reply constrained to `schema`."""
    return {
        "model": model,
        "max_tokens": max_tokens,
        "output_config": {"effort": effort, "format": {"type": "json_schema", "schema": schema}},
        "system": system,
        "messages": [{"role": "user", "content": user}],
    }


def _warn_if_stopped(model: str, stop_reason: str | None) -> None:
    """Log a call that ended other than with END_TURN. The model name lets the evals tell
    concurrent models' stops apart."""
    if stop_reason != END_TURN:
        logger.warning(STOPPED, model, stop_reason)


def _client(settings: AnswerSettings) -> anthropic.AsyncAnthropic | None:
    key = settings.anthropic_api_key
    # The key from settings only: never ANTHROPIC_API_KEY or a local CLI profile.
    return None if key is None else anthropic.AsyncAnthropic(api_key=key.get_secret_value())


def _unavailable(error: anthropic.AnthropicError) -> ModelUnavailable:
    # Status, error type and request id only: never the prompt or the documents.
    logger.warning(
        "Claude call failed: %s status=%s type=%s request_id=%s",
        type(error).__name__,
        getattr(error, "status_code", None),
        getattr(error, "type", None),
        getattr(error, "request_id", None),
    )
    return ModelUnavailable(type(error).__name__)


def _usage(model: str, usage: anthropic.types.Usage) -> Usage:
    return Usage(
        model=model,
        input_tokens=usage.input_tokens,
        output_tokens=usage.output_tokens,
        cache_creation_input_tokens=usage.cache_creation_input_tokens or 0,
        cache_read_input_tokens=usage.cache_read_input_tokens or 0,
    )
