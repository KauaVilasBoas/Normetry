"""Chat routes: a user's conversations, and each question answered over SSE (ADR 005, ADR 007).

Contracts: src/shared/contracts/chat-api.md (JSON and events) and internal-http.md (routes and
headers). Routes under /establishments/{establishment_id}/ read the establishment and its
jurisdiction through establishment_context; the others read only request_context. The tenant and
user never come from a body or a query string. The core counts and limits messages; this service
does not.
"""

import logging
from collections.abc import AsyncGenerator, AsyncIterable, Callable
from contextlib import aclosing
from dataclasses import dataclass
from functools import partial
from typing import Annotated, Any
from uuid import UUID, uuid4

import anyio
from fastapi import APIRouter, Depends, HTTPException, Request, Response
from fastapi.sse import EventSourceResponse, ServerSentEvent
from pydantic import BaseModel, ConfigDict, StringConstraints
from sqlalchemy import Engine

from normetry_ai.answers import Answered, Claim, ClaimModel, Refused, Unavailable, answer
from normetry_ai.api.context import (
    EstablishmentContext,
    RequestContext,
    establishment_context,
    request_context,
)
from normetry_ai.conversations import store
from normetry_ai.conversations.rewrite import TextModel, Turn, standalone_question
from normetry_ai.db import tenant_transaction
from normetry_ai.llm.usage import record_usage
from normetry_ai.retrieval import hybrid
from normetry_ai.retrieval.language import detect_language
from normetry_ai.retrieval.models import RetrievedChunk
from normetry_ai.retrieval.policy import jurisdictions_for
from normetry_ai.settings import AnswerSettings, ConversationSettings

logger = logging.getLogger(__name__)

MAX_QUESTION_CHARS = 2000
_NOT_FOUND = "Conversation not found."

# Runs in a worker thread: the standalone question and the caller (tenant, establishment,
# jurisdiction) in, the chunks to answer from out, best first.
Retriever = Callable[[str, EstablishmentContext], list[RetrievedChunk]]


@dataclass(frozen=True)
class Chat:
    """The models and settings the process answers with, built once by create_app."""

    claim_model: ClaimModel
    rewrite_model: TextModel
    answer_settings: AnswerSettings
    conversation_settings: ConversationSettings


def get_engine(request: Request) -> Engine:
    engine = getattr(request.app.state, "engine", None)
    if engine is None:
        raise HTTPException(status_code=503, detail="No database configured.")
    return engine


def get_chat(request: Request) -> Chat:
    return request.app.state.chat


Caller = Annotated[RequestContext, Depends(request_context)]
EstablishmentCaller = Annotated[EstablishmentContext, Depends(establishment_context)]
Database = Annotated[Engine, Depends(get_engine)]


def get_retriever(request: Request, engine: Database) -> Retriever:
    """Hybrid retrieval (#49) for a turn: the one dependency that returns it; tests override it.

    It runs inside the caller's tenant transaction, over the jurisdictions `jurisdictions_for`
    gives the establishment, which `open_turn` checked is the conversation's.
    """
    retriever: hybrid.Retriever = request.app.state.retriever

    def retrieve(question: str, ctx: EstablishmentContext) -> list[RetrievedChunk]:
        with tenant_transaction(engine, ctx.tenant_id) as connection:
            return retriever.retrieve(connection, question, jurisdictions_for(ctx.jurisdiction))

    return retrieve


Models = Annotated[Chat, Depends(get_chat)]

router = APIRouter()
# The router's dependency runs before any of the route's: a request without valid establishment
# headers gets 400 before anything else happens.
establishment_router = APIRouter(
    prefix="/establishments/{establishment_id}", dependencies=[Depends(establishment_context)]
)


@establishment_router.post("/conversations", status_code=201)
def create_conversation(ctx: EstablishmentCaller, engine: Database) -> dict[str, Any]:
    with tenant_transaction(engine, ctx.tenant_id) as connection:
        return store.create_conversation(
            connection,
            tenant_id=ctx.tenant_id,
            user_id=ctx.user_id,
            establishment_id=ctx.establishment_id,
            jurisdiction=ctx.jurisdiction,
        )


@router.get("/conversations")
def list_conversations(ctx: Caller, engine: Database) -> list[dict[str, Any]]:
    with tenant_transaction(engine, ctx.tenant_id) as connection:
        return store.list_conversations(connection, ctx.user_id)


@router.get("/conversations/{conversation_id}")
def get_conversation(conversation_id: UUID, ctx: Caller, engine: Database) -> dict[str, Any]:
    with tenant_transaction(engine, ctx.tenant_id) as connection:
        conversation = store.get_conversation(connection, ctx.user_id, conversation_id)
    if conversation is None:
        raise HTTPException(status_code=404, detail=_NOT_FOUND)
    return conversation


@router.delete("/conversations/{conversation_id}", status_code=204)
def delete_conversation(conversation_id: UUID, ctx: Caller, engine: Database) -> Response:
    with tenant_transaction(engine, ctx.tenant_id) as connection:
        deleted = store.delete_conversation(connection, ctx.user_id, conversation_id)
    if not deleted:
        raise HTTPException(status_code=404, detail=_NOT_FOUND)
    return Response(status_code=204)


class NewMessage(BaseModel):
    # Only the question: nothing in the body may name a tenant, user or establishment.
    model_config = ConfigDict(extra="forbid")

    text: Annotated[
        str, StringConstraints(strip_whitespace=True, min_length=1, max_length=MAX_QUESTION_CHARS)
    ]


@dataclass(frozen=True)
class OpenTurn:
    conversation_id: UUID
    question: str
    history: list[Turn]  # the messages before the question, oldest first


def open_turn(
    conversation_id: UUID,
    message: NewMessage,
    ctx: EstablishmentCaller,
    engine: Database,
    chat: Models,
) -> OpenTurn:
    """Store the question before the stream starts, so a conversation that is not the caller's
    (or not this establishment's) gets a plain 404 instead of an event stream."""
    try:
        with tenant_transaction(engine, ctx.tenant_id) as connection:
            history = store.start_turn(
                connection,
                conversation_id,
                user_id=ctx.user_id,
                establishment_id=ctx.establishment_id,
                jurisdiction=ctx.jurisdiction,
                question=message.text,
                history_messages=2 * chat.conversation_settings.chat_history_turns,
            )
    except Exception as error:
        # The type only: the message of a database error can carry the question.
        logger.error("Storing the question failed: %s", type(error).__name__)
        raise HTTPException(status_code=503, detail="The question could not be stored.") from None
    if history is None:
        raise HTTPException(status_code=404, detail=_NOT_FOUND)
    return OpenTurn(conversation_id, message.text, history)


@establishment_router.post(
    "/conversations/{conversation_id}/messages", response_class=EventSourceResponse
)
async def post_message(
    turn: Annotated[OpenTurn, Depends(open_turn)],
    ctx: EstablishmentCaller,
    engine: Database,
    chat: Models,
    retrieve: Annotated[Retriever, Depends(get_retriever)],
) -> AsyncIterable[ServerSentEvent]:
    async for event in answer_turn(turn, ctx, engine=engine, chat=chat, retrieve=retrieve):
        yield event


async def answer_turn(
    turn: OpenTurn,
    ctx: EstablishmentContext,
    *,
    engine: Engine,
    chat: Chat,
    retrieve: Retriever,
) -> AsyncGenerator[ServerSentEvent, None]:
    """`claim` events, then one of `done`, `refusal` or `unavailable`.

    The answer is stored before the final event, and also when the stream stops early (the
    client went away): then with the claims sent so far.
    """
    # Usage is recorded for the tenant and user of the internal headers only.
    record = partial(
        record_usage,
        engine,
        tenant_id=ctx.tenant_id,
        user_id=ctx.user_id,
        prices=chat.answer_settings.model_prices,
    )
    claims: list[dict[str, Any]] = []
    language: str | None = None
    ending: Answered | Refused | Unavailable | None = None
    try:
        question = await standalone_question(
            turn.question,
            turn.history,
            model=chat.rewrite_model,
            settings=chat.conversation_settings,
            record_usage=record,
        )
        # The claims are written in the question's language, detected as retrieval detects it;
        # a citation keeps its chunk's, which can differ (BR and US corpora have one language).
        language = detect_language(question)
        chunks = await anyio.to_thread.run_sync(retrieve, question, ctx)
        events = answer(
            question,
            chunks,
            model=chat.claim_model,
            settings=chat.answer_settings,
            record_usage=record,
        )
        async with aclosing(events):
            async for event in events:
                if isinstance(event, Claim):
                    claims.append(claim_json(event))
                    yield ServerSentEvent(event="claim", data=claims[-1])
                else:
                    ending = event
    except Exception as error:
        # The type only: the message of a database error can carry the question.
        logger.error("Answer failed: %s", type(error).__name__)
        ending = Unavailable()
    finally:
        kind, reason = _stored_as(ending, claims)
        # Shielded: a cancelled stream (the client went away) still stores what it sent.
        with anyio.CancelScope(shield=True):
            try:
                await anyio.to_thread.run_sync(
                    partial(
                        _store_answer,
                        engine,
                        ctx,
                        turn.conversation_id,
                        kind,
                        reason,
                        language,
                        claims,
                    )
                )
            except Exception as error:
                # As above. The answer is lost, so the stream ends unavailable.
                logger.error("Storing the answer failed: %s", type(error).__name__)
                ending = Unavailable()
    match ending:
        case Answered():
            yield ServerSentEvent(event="done", data={})
        case Refused(reason=refusal):
            yield ServerSentEvent(event="refusal", data={"reason": refusal.value})
        case _:
            yield ServerSentEvent(event="unavailable", data={})


def _stored_as(
    ending: Answered | Refused | Unavailable | None, claims: list[dict[str, Any]]
) -> tuple[str, str | None]:
    match ending:
        case Answered():
            return "answer", None
        case Refused(reason=reason):
            return "refusal", reason.value
        case Unavailable():
            return "unavailable", None
    # Stopped early: what was sent is the answer, or nothing came at all.
    return ("answer" if claims else "unavailable"), None


def _store_answer(
    engine: Engine,
    ctx: EstablishmentContext,
    conversation_id: UUID,
    kind: str,
    reason: str | None,
    language: str | None,
    claims: list[dict[str, Any]],
) -> None:
    with tenant_transaction(engine, ctx.tenant_id) as connection:
        store.store_answer(
            connection,
            conversation_id,
            user_id=ctx.user_id,
            kind=kind,
            reason=reason,
            language=language,
            claims=claims,
        )


def claim_json(claim: Claim) -> dict[str, Any]:
    """A claim as the API sends and stores it, with a copy of everything each citation shows."""
    return {
        "text": claim.text,
        "citations": [
            {
                "id": str(uuid4()),
                "sourceTitle": citation.chunk.source_title,
                "sourceVersion": citation.chunk.source_version,
                "language": citation.chunk.language,
                "sectionPath": citation.chunk.section_path,
                "text": citation.chunk.content,
                "quote": citation.quote,
                "quoteStart": citation.quote_start,
                "quoteEnd": citation.quote_end,
                "officialUrl": citation.chunk.official_url,
            }
            for citation in claim.citations
        ],
    }
