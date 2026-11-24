import os
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from aio_pika.abc import AbstractRobustConnection
from fastapi import FastAPI
from sqlalchemy import Engine

from normetry_ai import telemetry
from normetry_ai.api import conversations, health
from normetry_ai.api.auth import InternalTokenMiddleware
from normetry_ai.db import check_runtime_role, create_db_engine
from normetry_ai.ingestion.documents import BlobStore, DocumentWorker
from normetry_ai.ingestion.sandbox import protect_service_process
from normetry_ai.inspection.worker import InspectionWorker
from normetry_ai.llm.claude import ClaudeClaimModel, ClaudeJudgeModel, ClaudeRewriteModel
from normetry_ai.llm.embeddings import SentenceTransformerEmbedder
from normetry_ai.llm.reranker import CrossEncoderReranker
from normetry_ai.messaging import inspections
from normetry_ai.messaging.consumer import start as start_consumer
from normetry_ai.retrieval import hybrid
from normetry_ai.settings import (
    AnswerSettings,
    ConversationSettings,
    EmbeddingSettings,
    InspectionSettings,
    RetrievalSettings,
    Settings,
)

# langgraph pulls in langsmith, whose tracing would send prompts and documents to an outside
# service: the service refuses to start with any of these switched on.
TRACING_VARIABLES = (
    "LANGSMITH_TRACING",
    "LANGSMITH_TRACING_V2",
    "LANGCHAIN_TRACING",
    "LANGCHAIN_TRACING_V2",
)


def refuse_tracing() -> None:
    """Raise RuntimeError naming (never echoing) every tracing variable set to anything but an
    empty or false value."""
    on = [
        name
        for name in TRACING_VARIABLES
        if os.environ.get(name, "").strip().lower() not in {"", "0", "false", "no", "off"}
    ]
    if on:
        raise RuntimeError(
            f"{', '.join(on)} would send prompts and documents to LangSmith. Unset "
            f"{'it' if len(on) == 1 else 'them'}: the service never traces to an outside service."
        )


def create_app(
    settings: Settings | None = None,
    *,
    answer_settings: AnswerSettings | None = None,
    conversation_settings: ConversationSettings | None = None,
) -> FastAPI:
    """Build the API. Raises pydantic's ValidationError when the internal token is missing or
    short, and ValueError when the rewrite model has no price (the lifespan raises it too for the
    inspection judge, when the consumers start).

    Run with: uv run uvicorn normetry_ai.api:create_app --factory
    """
    if settings is None:
        settings = Settings()
    answer_settings = answer_settings or AnswerSettings()
    conversation_settings = conversation_settings or ConversationSettings()
    chat = conversations.Chat(
        claim_model=ClaudeClaimModel(answer_settings),
        rewrite_model=ClaudeRewriteModel(
            answer_settings,
            conversation_settings.rewrite_model,
            conversation_settings.rewrite_max_tokens,
        ),
        answer_settings=answer_settings,
        conversation_settings=conversation_settings,
    )

    @asynccontextmanager
    async def lifespan(app: FastAPI) -> AsyncIterator[None]:
        refuse_tracing()
        # Before any parsing child starts (ingestion/sandbox.py); a no-op off Linux.
        protect_service_process()
        engine = _open_engine(settings)
        app.state.engine = engine
        app.state.retriever = _retrieval_models()
        consumers: list[AbstractRobustConnection] = []
        try:
            await _start_consumers(app, settings, answer_settings, consumers)
            yield
        finally:
            for consumer in consumers:
                await consumer.close()
            if engine is not None:
                engine.dispose()

    # No OpenAPI or docs routes: an internal service has nothing to advertise.
    app = FastAPI(
        title="Normetry AI", docs_url=None, redoc_url=None, openapi_url=None, lifespan=lifespan
    )
    app.state.chat = chat
    app.add_middleware(InternalTokenMiddleware, token=settings.internal_token)
    app.include_router(health.router)
    app.include_router(conversations.router)
    app.include_router(conversations.establishment_router)
    telemetry.instrument(app)
    return app


def _open_engine(settings: Settings) -> Engine | None:
    """The app_ai engine, once its role passes `check_runtime_role`. None without a database URL
    (health-only runs, offline tests): the chat routes then answer 503."""
    if settings.database_url is None:
        return None
    engine = create_db_engine(settings)
    try:
        with engine.connect() as connection:
            check_runtime_role(connection)
    except BaseException:
        engine.dispose()
        raise
    return engine


def _retrieval_models() -> hybrid.Retriever:
    """One retriever per process, holding the embedder and re-ranker the consumers share. Its
    models load on the first question (#49), so the app starts without their weights; a turn that
    cannot load them answers unavailable."""
    embedding, retrieval = EmbeddingSettings(), RetrievalSettings()
    embedder = SentenceTransformerEmbedder(
        embedding.embedding_model, embedding.embedding_model_revision
    )
    reranker = CrossEncoderReranker(
        retrieval.reranker_model,
        retrieval.reranker_model_revision,
        retrieval.reranker_max_length,
    )
    return hybrid.Retriever(embedder, reranker, retrieval)


async def _start_consumers(
    app: FastAPI,
    settings: Settings,
    answer_settings: AnswerSettings,
    consumers: list[AbstractRobustConnection],
) -> None:
    """Start the document consumer, then the inspection consumer, their workers on the app's
    engine and the retriever's models, so each model loads once per process. Each connection goes
    into `consumers` once open, so the caller closes it also when a later step raises (a judge
    model without a price raises ValueError). Without a RabbitMQ URL (offline tests, health-only
    runs) nothing is consumed; the settings guarantee the database and blob storage when it is
    set."""
    if settings.rabbitmq_url is None:
        return
    engine, retriever = app.state.engine, app.state.retriever
    app.state.documents = DocumentWorker(
        engine,
        retriever.embedder,
        BlobStore.connect(
            settings.blob_connection_string.get_secret_value(), settings.blob_container
        ),
    )
    url = settings.rabbitmq_url.get_secret_value()
    consumers.append(await start_consumer(url, app.state.documents))
    inspection = InspectionSettings()
    app.state.inspections = InspectionWorker(
        engine,
        retriever.embedder,
        retriever.reranker,
        ClaudeJudgeModel(
            answer_settings, inspection.inspection_model, inspection.inspection_max_tokens
        ),
        answers=answer_settings,
        retrieval=retriever.settings,
        inspection=inspection,
    )
    consumers.append(await inspections.start(url, app.state.inspections))
