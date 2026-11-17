import os
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import FastAPI

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
        # Without a database URL (health-only runs, offline tests) the chat routes answer 503.
        engine = None
        if settings.database_url is not None:
            engine = create_db_engine(settings)
            try:
                with engine.connect() as connection:
                    check_runtime_role(connection)
            except BaseException:
                engine.dispose()
                raise
        app.state.engine = engine
        # One retriever per process. Its models load on the first question (#49), so the app starts
        # without their weights; a turn that cannot load them answers unavailable.
        embedding, retrieval = EmbeddingSettings(), RetrievalSettings()
        embedder = SentenceTransformerEmbedder(
            embedding.embedding_model, embedding.embedding_model_revision
        )
        reranker = CrossEncoderReranker(
            retrieval.reranker_model,
            retrieval.reranker_model_revision,
            retrieval.reranker_max_length,
        )
        app.state.retriever = hybrid.Retriever(embedder, reranker, retrieval)
        consumers = []
        try:
            # Without a RabbitMQ URL (offline tests, health-only runs) nothing is consumed. The
            # settings guarantee the database and blob storage when it is set.
            if settings.rabbitmq_url is not None:
                # The chat's embedder: the model loads once per process, for both.
                app.state.documents = DocumentWorker(
                    engine,
                    embedder,
                    BlobStore.connect(
                        settings.blob_connection_string.get_secret_value(),
                        settings.blob_container,
                    ),
                )
                url = settings.rabbitmq_url.get_secret_value()
                consumers.append(await start_consumer(url, app.state.documents))
                # The same embedder and re-ranker: each model loads once per process. A judge
                # model without a price raises ValueError here.
                inspection = InspectionSettings()
                app.state.inspections = InspectionWorker(
                    engine,
                    embedder,
                    reranker,
                    ClaudeJudgeModel(
                        answer_settings,
                        inspection.inspection_model,
                        inspection.inspection_max_tokens,
                    ),
                    answers=answer_settings,
                    retrieval=retrieval,
                    inspection=inspection,
                )
                consumers.append(await inspections.start(url, app.state.inspections))
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
