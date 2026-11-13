"""Claude through the Anthropic SDK: the chat model, answering in claims (structured outputs),
and a plain-text model for short tasks such as rewriting a follow-up.

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
from normetry_ai.settings import AnswerSettings

logger = logging.getLogger(__name__)

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
                model=settings.chat_model,
                max_tokens=settings.chat_max_tokens,
                output_config={
                    "effort": settings.chat_effort,
                    "format": {"type": "json_schema", "schema": CLAIMS_SCHEMA},
                },
                system=system,
                messages=[{"role": "user", "content": user}],
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
        if message.stop_reason != "end_turn":
            # max_tokens cuts the last claim (never handed on); refusal ends the answer early.
            logger.warning("Claude stopped with %s", message.stop_reason)
        yield _usage(settings.chat_model, message.usage)


class ClaudeTextModel:
    """One short plain-text completion (the follow-up rewrite) from a configured Claude model."""

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

    async def complete(self, system: str, user: str) -> tuple[str, Usage]:
        """The model's text, stripped, and the call's Usage. Raises ModelUnavailable."""
        if self._client is None:
            raise ModelUnavailable("no Anthropic API key")
        try:
            message = await self._client.messages.create(
                model=self._model,
                max_tokens=self._max_tokens,
                # A rewrite needs little thought; low effort keeps it fast and cheap.
                output_config={"effort": "low"},
                system=system,
                messages=[{"role": "user", "content": user}],
            )
        except anthropic.AnthropicError as error:
            raise _unavailable(error) from error
        text = "".join(block.text for block in message.content if block.type == "text")
        return text.strip(), _usage(self._model, message.usage)


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
