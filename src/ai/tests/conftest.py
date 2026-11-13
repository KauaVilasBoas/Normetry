"""The PostgreSQL every integration test shares: one pgvector container per session.

It runs the bootstrap from deploy/postgres/init/ with random passwords, then `alembic upgrade
head` as ai_owner. Tests connect as the real roles, never as the superuser. Without Docker the
tests that use it are skipped, except in CI (where `CI` is set), where they fail instead.
"""

import os
import secrets
import subprocess
import sys
from collections.abc import Iterator
from contextlib import contextmanager
from pathlib import Path

import pytest
from docker.errors import DockerException
from sqlalchemy import Engine, create_engine
from testcontainers.community.postgres import PostgresContainer

from normetry_ai.db import create_db_engine
from normetry_ai.settings import Settings

AI_DIR = Path(__file__).resolve().parents[1]
BOOTSTRAP = AI_DIR.parents[1] / "deploy" / "postgres" / "init" / "01-roles-and-schemas.sh"
IMAGE = "pgvector/pgvector:0.8.7-pg17"
ROLES = ("core_owner", "ai_owner", "app_core", "app_ai", "corpus_ingestor")
TOKEN = "x" * 32


def migrate(owner_url: str, *args: str) -> None:
    """Run Alembic as ai_owner, like the AppHost's ai-migrate step."""
    env = {**os.environ, "NORMETRY_MIGRATIONS_DATABASE_URL": owner_url}
    command = [sys.executable, "-m", "alembic", *args]
    subprocess.run(command, cwd=AI_DIR, env=env, check=True)  # noqa: S603 (fixed argv, no shell)


@pytest.fixture(scope="session")
def urls() -> Iterator[dict[str, str]]:
    """SQLAlchemy URL per bootstrap role, on a migrated database."""
    passwords = {role: secrets.token_hex(16) for role in ROLES}
    try:
        container = PostgresContainer(
            IMAGE,
            username="postgres",
            password=secrets.token_hex(16),
            dbname="normetry",
            driver="psycopg",
            env={f"{role.upper()}_PASSWORD": password for role, password in passwords.items()},
            transferables=[(BOOTSTRAP, "/docker-entrypoint-initdb.d/" + BOOTSTRAP.name, 0o755)],
        )
        container.start()
    except DockerException as error:
        if os.environ.get("CI"):
            raise
        pytest.skip(f"Docker is not available: {error}")
    try:
        host, port = container.get_container_host_ip(), container.get_exposed_port(5432)
        urls = {
            role: f"postgresql+psycopg://{role}:{password}@{host}:{port}/normetry"
            for role, password in passwords.items()
        }
        migrate(urls["ai_owner"], "upgrade", "head")
        yield urls
    finally:
        container.stop()


@contextmanager
def engine_for(url: str, **kwargs: object) -> Iterator[Engine]:
    engine = create_engine(url, **kwargs)
    try:
        yield engine
    finally:
        engine.dispose()


@pytest.fixture(scope="session")
def app_ai(urls: dict[str, str]) -> Iterator[Engine]:
    # The service's own engine factory, pointed at the container.
    settings = Settings(internal_token=TOKEN, database_url=urls["app_ai"], _env_file=None)
    engine = create_db_engine(settings)
    yield engine
    engine.dispose()


@pytest.fixture(scope="session")
def corpus(urls: dict[str, str]) -> Iterator[Engine]:
    with engine_for(urls["corpus_ingestor"]) as engine:
        yield engine


@pytest.fixture(scope="session")
def owner(urls: dict[str, str]) -> Iterator[Engine]:
    with engine_for(urls["ai_owner"]) as engine:
        yield engine
