"""Service configuration, read from `NORMETRY_*` environment variables (or `src/ai/.env`)."""

from decimal import Decimal
from pathlib import Path
from typing import Literal, Self

from pydantic import BaseModel, Field, SecretStr, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

_CONFIG = SettingsConfigDict(
    env_prefix="NORMETRY_",
    # Always src/ai/.env, whatever the working directory: never the repository root's .env.
    env_file=Path(__file__).resolve().parents[1] / ".env",
    extra="ignore",
    # A rejected value never appears in the error message, so a bad token is not logged.
    hide_input_in_errors=True,
)


class Settings(BaseSettings):
    """Runtime settings of the API and workers."""

    model_config = _CONFIG

    # Shared secret YARP sends on every request. Required: without it the app refuses to start.
    internal_token: SecretStr = Field(min_length=32)
    # The app role (app_ai, DML only). SQLAlchemy URL with the psycopg 3 driver:
    # postgresql+psycopg://user:password@host:5432/db
    database_url: SecretStr | None = None


class EmbeddingSettings(BaseSettings):
    """The embedding model (ADR 008), pinned to a Hub commit so every run embeds the same way."""

    model_config = _CONFIG

    embedding_model: str = "BAAI/bge-m3"
    embedding_model_revision: str = "5617a9f61b028005a4858fdac845db406aefb181"


class RetrievalSettings(BaseSettings):
    """Hybrid retrieval (ADR 006): candidate counts, fusion, and the re-ranker pinned like the
    embedding model. Tuned with the retrieval evals (evals/README.md)."""

    model_config = _CONFIG

    # Candidates each search returns before fusion.
    retrieval_vector_k: int = Field(default=50, ge=1)
    retrieval_text_k: int = Field(default=50, ge=1)
    # Reciprocal Rank Fusion constant: score = sum of 1 / (rrf_k + rank).
    retrieval_rrf_k: int = Field(default=60, ge=0)
    # Fused candidates the re-ranker scores (its cost grows with this), and chunks returned.
    retrieval_rerank_candidates: int = Field(default=30, ge=1)
    retrieval_top_k: int = Field(default=10, ge=1)
    # HNSW candidate list per vector search (pgvector's hnsw.ef_search).
    retrieval_hnsw_ef_search: int = Field(default=100, ge=1)
    reranker_model: str = "BAAI/bge-reranker-v2-m3"
    reranker_model_revision: str = "953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e"
    # Tokens per (question, chunk) pair; the longest corpus chunk is about 535 tokens.
    reranker_max_length: int = Field(default=1024, ge=1)


class ModelPrice(BaseModel):
    """US dollars per million tokens of one model."""

    input: Decimal
    output: Decimal
    cache_write: Decimal  # 5-minute cache writes
    cache_read: Decimal


class AnswerSettings(BaseSettings):
    """The chat model and the citation guard (ADR 007).

    Environment only, never src/ai/.env: the Anthropic API key comes from the AppHost parameter
    `anthropic-api-key` (AppHost user secrets) or from the shell, never from a file.
    """

    model_config = SettingsConfigDict({**_CONFIG, "env_file": None})

    # Without it the service still starts, and every answer is "unavailable".
    anthropic_api_key: SecretStr | None = None
    chat_model: str = "claude-opus-5-5"
    # Thinking counts toward it.
    chat_max_tokens: int = Field(default=16000, gt=0)
    chat_effort: Literal["low", "medium", "high", "xhigh", "max"] = "medium"
    # Below this best re-ranker score the answer is refused before any model call, on the scale of
    # RetrievedChunk.score. 0 refuses only when retrieval returns nothing: in the 2026-11-14 eval
    # the best scores of answerable items (median 0.12, lowest 0.0001) and of refusals (median
    # 0.002, highest 0.72) overlap, and 0.1 would refuse 19 of 44 answerable items. The citation
    # guard does the refusing instead, at the cost of one model call per question it refuses.
    answer_min_rerank_score: float = Field(default=0.0, ge=0)
    # A shorter quote proves nothing: a word like "food" is in most chunks.
    answer_min_quote_words: int = Field(default=4, ge=1)
    # Anthropic list prices (November 2026). A model call with no price here is refused at startup.
    # Set NORMETRY_MODEL_PRICES as JSON to change them.
    model_prices: dict[str, ModelPrice] = {
        "claude-opus-5-5": ModelPrice(
            input=Decimal("4"),
            output=Decimal("20"),
            cache_write=Decimal("5"),
            cache_read=Decimal("0.2"),
        ),
        "claude-sonnet-5-5": ModelPrice(
            input=Decimal("2"),
            output=Decimal("10"),
            cache_write=Decimal("2.5"),
            cache_read=Decimal("0.1"),
        ),
        # Prompts up to 100K tokens; answers stay far below that.
        "claude-haiku-5-5": ModelPrice(
            input=Decimal("0.1"),
            output=Decimal("0.5"),
            cache_write=Decimal("0.125"),
            cache_read=Decimal("0.01"),
        ),
    }

    @model_validator(mode="after")
    def _chat_model_has_a_price(self) -> Self:
        if self.chat_model not in self.model_prices:
            raise ValueError(f"NORMETRY_MODEL_PRICES has no price for {self.chat_model!r}")
        return self


class CorpusSettings(BaseSettings):
    """Read only by the corpus ingestion command, as corpus_ingestor: never by the service.

    corpus_ingestor writes the public corpus every tenant reads, so, like the owner URL, its URL
    comes from the environment only, never from src/ai/.env.
    """

    model_config = SettingsConfigDict({**_CONFIG, "env_file": None})

    corpus_database_url: SecretStr | None = None


class MigrationSettings(BaseSettings):
    """Read only by migrations/env.py: the owner role that runs DDL, never the app role.

    Environment only, never src/ai/.env: the owner password must not sit in the service's env file.
    """

    model_config = SettingsConfigDict({**_CONFIG, "env_file": None})

    migrations_database_url: SecretStr | None = None
