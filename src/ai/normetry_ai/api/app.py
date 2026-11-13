from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import FastAPI

from normetry_ai import telemetry
from normetry_ai.api import conversations, health
from normetry_ai.api.auth import InternalTokenMiddleware
from normetry_ai.db import check_runtime_role, create_db_engine
from normetry_ai.llm.claude import ClaudeClaimModel, ClaudeTextModel
from normetry_ai.llm.embeddings import SentenceTransformerEmbedder
from normetry_ai.llm.reranker import CrossEncoderReranker
from normetry_ai.retrieval import hybrid
from normetry_ai.settings import (
    AnswerSettings,
    ConversationSettings,
    EmbeddingSettings,
    RetrievalSettings,
    Settings,
)


def create_app(
    settings: Settings | None = None,
    *,
    answer_settings: AnswerSettings | None = None,
    conversation_settings: ConversationSettings | None = None,
) -> FastAPI:
    """Build the API. Raises pydantic's ValidationError when the internal token is missing or
    short, and ValueError when the rewrite model has no price.

    Run with: uv run uvicorn normetry_ai.api:create_app --factory
    """
    if settings is None:
        settings = Settings()
    answer_settings = answer_settings or AnswerSettings()
    conversation_settings = conversation_settings or ConversationSettings()
    chat = conversations.Chat(
        claim_model=ClaudeClaimModel(answer_settings),
        rewrite_model=ClaudeTextModel(
            answer_settings,
            conversation_settings.rewrite_model,
            conversation_settings.rewrite_max_tokens,
        ),
        answer_settings=answer_settings,
        conversation_settings=conversation_settings,
    )

    @asynccontextmanager
    async def lifespan(app: FastAPI) -> AsyncIterator[None]:
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
        app.state.retriever = hybrid.Retriever(
            SentenceTransformerEmbedder(
                embedding.embedding_model, embedding.embedding_model_revision
            ),
            CrossEncoderReranker(
                retrieval.reranker_model,
                retrieval.reranker_model_revision,
                retrieval.reranker_max_length,
            ),
            retrieval,
        )
        try:
            yield
        finally:
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
