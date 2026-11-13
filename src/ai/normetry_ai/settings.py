"""Service configuration, read from `NORMETRY_*` environment variables (or `src/ai/.env`)."""

from pathlib import Path

from pydantic import Field, SecretStr
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
