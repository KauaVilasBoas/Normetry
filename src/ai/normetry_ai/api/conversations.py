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
from enum import StrEnum
from functools import partial
from typing import Annotated, Any
from uuid import UUID, uuid4

import anyio
from fastapi import APIRouter, Depends, HTTPException, Request, Response
from fastapi.sse import EventSourceResponse, ServerSentEvent
from pydantic import BaseModel, ConfigDict, StringConstraints
from sqlalchemy import Engine

from normetry_ai.answers import (
    Answered,
    Citation,
    Claim,
    ClaimModel,
    Refused,
    Unavailable,
    answer,
)
from normetry_ai.api.context import (
    EstablishmentContext,
    RequestContext,
    establishment_context,
    request_context,
)
from normetry_ai.conversations import store
from normetry_ai.conversations.rewrite import TextModel, Turn, standalone_question
from normetry_ai.db import tenant_transaction
from normetry_ai.llm.usage import Usage, UsageFeature, record_usage
from normetry_ai.retrieval import hybrid
from normetry_ai.retrieval.language import detect_language
from normetry_ai.retrieval.models import RetrievedChunk
from normetry_ai.retrieval.policy import jurisdictions_for
from normetry_ai.settings import AnswerSettings, ConversationSettings

logger = logging.getLogger(__name__)

MAX_QUESTION_CHARS = 2000
_NOT_FOUND = "Conversation not found."


class AnswerEvent(StrEnum):
    """The SSE event names of an answer (chat-api.md): claims, then exactly one of the others."""

    CLAIM = "claim"
    DONE = "done"
    REFUSAL = "refusal"
    UNAVAILABLE = "unavailable"


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
    gives the establishment and that establishment's current documents (#107): the establishment
    of the internal headers, which `open_turn` checked is the conversation's. A caller without
    documents.read gets the corpus only.
    """
    retriever: hybrid.Retriever = request.app.state.retriever

    def retrieve(question: str, ctx: EstablishmentContext) -> list[RetrievedChunk]:
        # ponytail: the pooled connection stays checked out while the query is embedded and
        # re-ranked (seconds on CPU, longer while the weights first load), so a burst of turns
        # can drain the pool; split retrieval so only the two searches hold it if that shows up.
        with tenant_transaction(engine, ctx.tenant_id) as connection:
            return retriever.retrieve(
                connection,
                question,
                jurisdictions_for(ctx.jurisdiction),
                establishment_id=ctx.establishment_id if ctx.documents_read else None,
            )

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
        conversation = store.get_conversation(
            connection, ctx.user_id, conversation_id, documents=ctx.documents_read
        )
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
                documents=ctx.documents_read,
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
    claims: list[dict[str, Any]] = []
    language: str | None = None
    ending: Answered | Refused | Unavailable | None = None
    try:
        question = await standalone_question(
            turn.question,
            turn.history,
            model=chat.rewrite_model,
            settings=chat.conversation_settings,
            record_usage=_usage_recorder(engine, ctx, chat, UsageFeature.REWRITE),
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
            record_usage=_usage_recorder(engine, ctx, chat, UsageFeature.CHAT),
        )
        async with aclosing(events):
            async for event in events:
                if isinstance(event, Claim):
                    claims.append(claim_json(event))
                    # The stream also says the language, so a live claim gets its `lang`.
                    yield ServerSentEvent(
                        event=AnswerEvent.CLAIM, data={**claims[-1], "language": language}
                    )
                else:
                    ending = event
    except Exception as error:
        # The type only: the message of a database error can carry the question.
        logger.error("Answer failed: %s", type(error).__name__)
        ending = Unavailable()
    finally:
        stored = await _store_answer_shielded(
            engine, ctx, turn.conversation_id, ending, language, claims
        )
        if not stored:  # the answer is lost, so the stream ends unavailable
            ending = Unavailable()
    yield _final_event(ending)


def _usage_recorder(
    engine: Engine, ctx: EstablishmentContext, chat: Chat, feature: UsageFeature
) -> Callable[[Usage], None]:
    """`record_usage` for one kind of model call of the turn, for the tenant and user of the
    internal headers only."""
    return partial(
        record_usage,
        engine,
        tenant_id=ctx.tenant_id,
        user_id=ctx.user_id,
        prices=chat.answer_settings.model_prices,
        feature=feature,
    )


async def _store_answer_shielded(
    engine: Engine,
    ctx: EstablishmentContext,
    conversation_id: UUID,
    ending: Answered | Refused | Unavailable | None,
    language: str | None,
    claims: list[dict[str, Any]],
) -> bool:
    """Store the answer as it ended, in a worker thread; False when it could not be stored.
    Shielded: a cancelled stream (the client went away) still stores what it sent."""
    kind, reason = _stored_as(ending, claims)
    with anyio.CancelScope(shield=True):
        try:
            await anyio.to_thread.run_sync(
                partial(_store_answer, engine, ctx, conversation_id, kind, reason, language, claims)
            )
        except Exception as error:
            # The type only: the message of a database error can carry the question.
            logger.error("Storing the answer failed: %s", type(error).__name__)
            return False
    return True


def _final_event(ending: Answered | Refused | Unavailable | None) -> ServerSentEvent:
    """The event that ends the stream: `done`, `refusal` with its reason, or `unavailable`."""
    match ending:
        case Answered():
            return ServerSentEvent(event=AnswerEvent.DONE, data={})
        case Refused(reason=refusal):
            return ServerSentEvent(event=AnswerEvent.REFUSAL, data={"reason": refusal.value})
        case _:
            return ServerSentEvent(event=AnswerEvent.UNAVAILABLE, data={})


def _stored_as(
    ending: Answered | Refused | Unavailable | None, claims: list[dict[str, Any]]
) -> tuple[store.MessageKind, str | None]:
    match ending:
        case Answered():
            return store.MessageKind.ANSWER, None
        case Refused(reason=reason):
            return store.MessageKind.REFUSAL, reason.value
        case Unavailable():
            return store.MessageKind.UNAVAILABLE, None
    # Stopped early: what was sent is the answer, or nothing came at all.
    return (store.MessageKind.ANSWER if claims else store.MessageKind.UNAVAILABLE), None


def _store_answer(
    engine: Engine,
    ctx: EstablishmentContext,
    conversation_id: UUID,
    kind: store.MessageKind,
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
    return {"text": claim.text, "citations": [citation_json(c) for c in claim.citations]}


def citation_json(citation: Citation) -> dict[str, Any]:
    """A citation of the regulation or of one of the establishment's documents (chat-api.md)."""
    chunk = citation.chunk
    shown = {
        "id": str(uuid4()),
        "language": chunk.language,
        "sectionPath": chunk.section_path,
        "text": chunk.content,
        "quote": citation.quote,
        "quoteStart": citation.quote_start,
        "quoteEnd": citation.quote_end,
    }
    if chunk.is_document:
        return {
            **shown,
            "sourceType": "document",
            "sourceTitle": None,
            "sourceVersion": None,
            "officialUrl": None,
            "documentId": str(chunk.document_id),
            "documentTitle": chunk.document_title,
            "documentType": chunk.document_type,
            "documentVersion": chunk.document_version,
        }
    return {
        **shown,
        "sourceType": "regulation",
        "sourceTitle": chunk.source_title,
        "sourceVersion": chunk.source_version,
        "officialUrl": chunk.official_url,
    }
