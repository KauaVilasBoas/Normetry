"""Integration tests against a real PostgreSQL: the shared bootstrap, the ai migrations and RLS.

One pgvector container per session runs the bootstrap from deploy/postgres/init/ with random
passwords, then `alembic upgrade head` as ai_owner. Assertions about app behavior connect as the
real roles, never as the superuser. Without Docker the tests are skipped, except in CI (where `CI`
is set), where they fail instead.
"""

import os
import secrets
import subprocess
import sys
from collections.abc import Iterator
from contextlib import contextmanager
from dataclasses import dataclass
from pathlib import Path
from uuid import UUID, uuid4

import pytest
from docker.errors import DockerException
from fastapi.testclient import TestClient
from sqlalchemy import Connection, Engine, create_engine, text
from sqlalchemy.exc import DBAPIError
from testcontainers.community.postgres import PostgresContainer

from normetry_ai.api import create_app
from normetry_ai.api.auth import INTERNAL_TOKEN_HEADER
from normetry_ai.db import check_runtime_role, create_db_engine, tenant_transaction
from normetry_ai.settings import Settings

pytestmark = pytest.mark.integration

AI_DIR = Path(__file__).resolve().parents[1]
BOOTSTRAP = AI_DIR.parents[1] / "deploy" / "postgres" / "init" / "01-roles-and-schemas.sh"
IMAGE = "pgvector/pgvector:0.8.7-pg17"
ROLES = ("core_owner", "ai_owner", "app_core", "app_ai", "corpus_ingestor")
TOKEN = "x" * 32
# SQLSTATE 42501: a privilege is missing, or a new row violates a row-level security policy.
DENIED = "42501"

# Chunk ids come from the database (DEFAULT gen_random_uuid()), never from the caller.
INSERT_CHUNK = text(
    """
    INSERT INTO ai.chunks (tenant_id, source_type, source_id, source_version, language, content,
                           content_hash)
    VALUES (:tenant_id, :source_type, :source_id, 1, :language, :content, :content_hash)
    RETURNING id
    """
)


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


def insert_chunk(
    connection: Connection,
    tenant_id: UUID | None,
    language: str = "en",
    content: str = "Keep cold food at 5 C or below.",
) -> UUID:
    params = {
        "tenant_id": tenant_id,
        "source_type": "regulation" if tenant_id is None else "tenant_document",
        "source_id": uuid4(),
        "language": language,
        "content": content,
        "content_hash": uuid4().hex,
    }
    return connection.execute(INSERT_CHUNK, params).scalar_one()


def visible(connection: Connection, *ids: UUID) -> set[UUID]:
    query = text("SELECT id FROM ai.chunks WHERE id = ANY(:ids)")
    return set(connection.execute(query, {"ids": list(ids)}).scalars())


def delete(connection: Connection, *ids: UUID) -> int:
    return connection.execute(
        text("DELETE FROM ai.chunks WHERE id = ANY(:ids)"), {"ids": list(ids)}
    ).rowcount


@contextmanager
def denied() -> Iterator[None]:
    """The block must fail with a privilege or row-level security error."""
    with pytest.raises(DBAPIError) as error:
        yield
    assert error.value.orig.sqlstate == DENIED


@dataclass(frozen=True)
class Seed:
    tenant_a: UUID
    tenant_b: UUID
    chunk_a: UUID
    chunk_b: UUID
    corpus_chunk: UUID

    @property
    def all(self) -> tuple[UUID, UUID, UUID]:
        return self.chunk_a, self.chunk_b, self.corpus_chunk


@pytest.fixture
def seed(app_ai: Engine, corpus: Engine) -> Seed:
    """One chunk for each of two fresh tenants (written by app_ai) and one corpus chunk."""
    tenant_a, tenant_b = uuid4(), uuid4()
    with tenant_transaction(app_ai, tenant_a) as connection:
        chunk_a = insert_chunk(connection, tenant_a)
    with tenant_transaction(app_ai, tenant_b) as connection:
        chunk_b = insert_chunk(connection, tenant_b)
    with corpus.begin() as connection:
        corpus_chunk = insert_chunk(connection, None)
    return Seed(tenant_a, tenant_b, chunk_a, chunk_b, corpus_chunk)


def test_each_tenant_reads_its_own_chunks_and_the_corpus(app_ai: Engine, seed: Seed) -> None:
    with tenant_transaction(app_ai, seed.tenant_a) as connection:
        assert visible(connection, *seed.all) == {seed.chunk_a, seed.corpus_chunk}
    with tenant_transaction(app_ai, seed.tenant_b) as connection:
        assert visible(connection, *seed.all) == {seed.chunk_b, seed.corpus_chunk}


def test_app_ai_cannot_insert_corpus_or_other_tenant_rows(app_ai: Engine, seed: Seed) -> None:
    for tenant_id in (None, seed.tenant_b):
        with denied(), tenant_transaction(app_ai, seed.tenant_a) as connection:
            insert_chunk(connection, tenant_id)


def test_app_ai_deletes_only_its_own_chunks(app_ai: Engine, seed: Seed) -> None:
    with tenant_transaction(app_ai, seed.tenant_a) as connection:
        assert delete(connection, *seed.all) == 1
    with tenant_transaction(app_ai, seed.tenant_b) as connection:
        assert visible(connection, *seed.all) == {seed.chunk_b, seed.corpus_chunk}


def test_app_ai_cannot_update_any_chunk(app_ai: Engine, seed: Seed) -> None:
    for chunk_id in seed.all:
        with denied(), tenant_transaction(app_ai, seed.tenant_a) as connection:
            connection.execute(
                text("UPDATE ai.chunks SET content = 'changed' WHERE id = :id"), {"id": chunk_id}
            )


def test_corpus_ingestor_reads_and_writes_only_the_corpus(corpus: Engine, seed: Seed) -> None:
    with corpus.begin() as connection:
        assert visible(connection, *seed.all) == {seed.corpus_chunk}
        assert delete(connection, seed.chunk_a, seed.chunk_b) == 0
    with denied(), corpus.begin() as connection:
        insert_chunk(connection, seed.tenant_a)
    with denied(), corpus.begin() as connection:
        connection.execute(text("UPDATE ai.chunks SET content = 'changed'"))
    with corpus.begin() as connection:
        new_chunk = insert_chunk(connection, None)
        assert delete(connection, new_chunk, seed.corpus_chunk) == 2


def test_tenant_policies_do_not_apply_to_corpus_ingestor(corpus: Engine, seed: Seed) -> None:
    # A tenant set on its transaction must not open tenant rows: the tenant policies are TO app_ai.
    with tenant_transaction(corpus, seed.tenant_a) as connection:
        assert visible(connection, *seed.all) == {seed.corpus_chunk}
        assert delete(connection, seed.chunk_a, seed.chunk_b) == 0
    with denied(), tenant_transaction(corpus, seed.tenant_a) as connection:
        insert_chunk(connection, seed.tenant_a)


def test_ai_owner_reads_and_writes_no_chunks(owner: Engine, seed: Seed) -> None:
    # FORCE ROW LEVEL SECURITY with no policy for the owner: nothing, even with a tenant set.
    with tenant_transaction(owner, seed.tenant_a) as connection:
        assert visible(connection, *seed.all) == set()
        assert delete(connection, *seed.all) == 0
    with denied(), tenant_transaction(owner, seed.tenant_a) as connection:
        insert_chunk(connection, seed.tenant_a)


def test_without_a_tenant_only_the_corpus_is_visible_and_writes_fail(
    app_ai: Engine, seed: Seed
) -> None:
    with app_ai.begin() as connection:
        assert visible(connection, *seed.all) == {seed.corpus_chunk}
        tenant_rows = text("SELECT count(*) FROM ai.chunks WHERE tenant_id IS NOT NULL")
        assert connection.execute(tenant_rows).scalar_one() == 0
        assert connection.execute(text("DELETE FROM ai.chunks")).rowcount == 0
    for tenant_id in (seed.tenant_a, None):
        with denied(), app_ai.begin() as connection:
            insert_chunk(connection, tenant_id)


def test_tenant_does_not_survive_into_the_next_transaction(
    urls: dict[str, str], seed: Seed
) -> None:
    class Failure(Exception):
        pass

    # One pooled connection, so every transaction below runs on the same session.
    with engine_for(urls["app_ai"], pool_size=1, max_overflow=0) as engine:
        with tenant_transaction(engine, seed.tenant_a) as connection:
            backend = connection.execute(text("SELECT pg_backend_pid()")).scalar_one()
            assert visible(connection, seed.chunk_a) == {seed.chunk_a}
        with pytest.raises(Failure), tenant_transaction(engine, seed.tenant_a):
            raise Failure  # the rollback path must reset the tenant too

        with engine.begin() as connection:
            assert connection.execute(text("SELECT pg_backend_pid()")).scalar_one() == backend
            tenant = text("SELECT current_setting('app.tenant_id', true)")
            assert connection.execute(tenant).scalar_one() in ("", None)
            assert visible(connection, seed.chunk_a) == set()


def test_session_level_tenant_does_not_survive_check_in(urls: dict[str, str], seed: Seed) -> None:
    # A fresh pool from the service's factory: both checkouts get its single connection.
    settings = Settings(internal_token=TOKEN, database_url=urls["app_ai"], _env_file=None)
    engine = create_db_engine(settings)
    try:
        with engine.begin() as connection:
            backend = connection.execute(text("SELECT pg_backend_pid()")).scalar_one()
            # A stray session-level SET that commits would otherwise outlive the checkout.
            connection.execute(text(f"SET app.tenant_id = '{seed.tenant_a}'"))
        with engine.begin() as connection:
            assert connection.execute(text("SELECT pg_backend_pid()")).scalar_one() == backend
            tenant = text("SELECT current_setting('app.tenant_id', true)")
            assert connection.execute(tenant).scalar_one() in ("", None)
            assert visible(connection, seed.chunk_a) == set()
    finally:
        engine.dispose()


def test_upserts_by_content_hash_are_idempotent_per_tenant(app_ai: Engine, corpus: Engine) -> None:
    upsert = text(
        """
        INSERT INTO ai.chunks (tenant_id, source_type, source_id, source_version, language,
                               content, content_hash)
        VALUES (:tenant_id, :source_type, :source_id, 1, 'en', 'Same text.', 'same-hash')
        ON CONFLICT ON CONSTRAINT chunks_source_content_key DO NOTHING
        """
    )
    source_id = uuid4()

    def insert(engine: Engine, tenant_id: UUID | None) -> int:
        params = {
            "tenant_id": tenant_id,
            "source_type": "regulation" if tenant_id is None else "tenant_document",
            "source_id": source_id,
        }
        if tenant_id is None:
            with engine.begin() as connection:
                return connection.execute(upsert, params).rowcount
        with tenant_transaction(engine, tenant_id) as connection:
            return connection.execute(upsert, params).rowcount

    tenant_a, tenant_b = uuid4(), uuid4()
    # Tenant B's identical chunk is not blocked by tenant A's: the key starts with the tenant.
    assert [insert(app_ai, tenant) for tenant in (tenant_a, tenant_a, tenant_b)] == [1, 0, 1]
    # NULLS NOT DISTINCT: corpus chunks deduplicate too.
    assert [insert(corpus, None), insert(corpus, None)] == [1, 0]


def test_reusing_another_tenants_chunk_id_fails(app_ai: Engine, seed: Seed) -> None:
    # The conflict target is the content key only, so a primary-key clash raises instead of being
    # skipped as a silent 0-row insert.
    upsert = text(
        """
        INSERT INTO ai.chunks (id, tenant_id, source_type, source_id, source_version, language,
                               content, content_hash)
        VALUES (:id, :tenant_id, 'tenant_document', :source_id, 1, 'en', 'Text.', :content_hash)
        ON CONFLICT ON CONSTRAINT chunks_source_content_key DO NOTHING
        """
    )
    params = {
        "id": seed.chunk_b,
        "tenant_id": seed.tenant_a,
        "source_id": uuid4(),
        "content_hash": uuid4().hex,
    }
    with pytest.raises(DBAPIError) as error, tenant_transaction(app_ai, seed.tenant_a) as c:
        c.execute(upsert, params)
    assert error.value.orig.sqlstate == "23505"  # unique_violation on chunks_pkey


@pytest.mark.parametrize(
    ("language", "config", "content"),
    [("pt", "portuguese", "Alimentos refrigerados"), ("en", "english", "Refrigerated foods")],
)
def test_tsv_uses_the_text_search_config_of_the_chunk_language(
    corpus: Engine, language: str, config: str, content: str
) -> None:
    with corpus.begin() as connection:
        chunk_id = insert_chunk(connection, None, language=language, content=content)
        same = text(
            "SELECT tsv = to_tsvector(CAST(:config AS regconfig), content) FROM ai.chunks"
            " WHERE id = :id"
        )
        assert connection.execute(same, {"config": config, "id": chunk_id}).scalar_one()


@pytest.mark.parametrize(
    "statement",
    [
        "SELECT * FROM ai.alembic_version",
        "INSERT INTO ai.alembic_version VALUES ('x')",
        "UPDATE ai.alembic_version SET version_num = 'x'",
        "DELETE FROM ai.alembic_version",
    ],
)
def test_app_ai_cannot_touch_the_migration_history(app_ai: Engine, statement: str) -> None:
    with denied(), app_ai.begin() as connection:
        connection.execute(text(statement))


def test_chunks_grants_are_exactly_what_the_roles_need(owner: Engine) -> None:
    grants = text(
        """
        SELECT a.grantee::regrole::text, a.privilege_type
        FROM pg_class c, aclexplode(c.relacl) a
        WHERE c.oid = 'ai.chunks'::regclass AND a.grantee <> c.relowner
        """
    )
    with owner.begin() as connection:
        granted = set(connection.execute(grants).all())

    assert granted == {
        (role, privilege)
        for role in ("app_ai", "corpus_ingestor")
        for privilege in ("SELECT", "INSERT", "DELETE")
    }


def test_policies_are_scoped_to_roles_never_public(owner: Engine) -> None:
    policies = text(
        "SELECT tablename, policyname, cmd, roles::text[] FROM pg_policies WHERE schemaname = 'ai'"
    )
    with owner.begin() as connection:
        rows = connection.execute(policies).all()

    assert [row for row in rows if "public" in row.roles] == []
    chunks = {
        (row.policyname, row.cmd, tuple(row.roles)) for row in rows if row.tablename == "chunks"
    }
    assert chunks == {
        ("chunks_read", "SELECT", ("app_ai",)),
        ("chunks_tenant_insert", "INSERT", ("app_ai",)),
        ("chunks_tenant_delete", "DELETE", ("app_ai",)),
        ("chunks_corpus_write", "ALL", ("corpus_ingestor",)),
    }


def test_every_ai_table_the_app_roles_can_use_forces_rls(owner: Engine) -> None:
    # No allow-list needed: the first migration revokes app_ai's access to ai.alembic_version.
    tables = text(
        """
        SELECT c.relname, c.relrowsecurity AND c.relforcerowsecurity
        FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = 'ai' AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
          AND (has_table_privilege('app_ai', c.oid, 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE')
               OR has_table_privilege('corpus_ingestor', c.oid,
                                      'SELECT, INSERT, UPDATE, DELETE, TRUNCATE'))
        """
    )
    with owner.begin() as connection:
        forced = dict(connection.execute(tables).all())

    assert "chunks" in forced  # the check is not vacuous
    assert all(forced.values()), forced


def test_bootstrap_roles_have_least_privilege(owner: Engine) -> None:
    roles = {"roles": list(ROLES)}
    attributes = text(
        """
        SELECT rolname, rolsuper OR rolbypassrls OR rolcreaterole OR rolcreatedb OR rolreplication
        FROM pg_roles WHERE rolname = ANY(CAST(:roles AS text[]))
        """
    )
    schema_privileges = text(
        """
        SELECT r.role, s.schema, p.privilege
        FROM unnest(CAST(:roles AS text[])) r(role),
             unnest(ARRAY['core', 'ai', 'public']) s(schema),
             unnest(ARRAY['USAGE', 'CREATE']) p(privilege)
        WHERE has_schema_privilege(r.role, s.schema, p.privilege)
        """
    )
    database_privileges = text(
        """
        SELECT r.role, p.privilege
        FROM unnest(CAST(:roles AS text[])) r(role),
             unnest(ARRAY['CREATE', 'TEMPORARY']) p(privilege)
        WHERE has_database_privilege(r.role, current_database(), p.privilege)
        """
    )
    memberships = text(
        """
        SELECT a.role, b.role
        FROM unnest(CAST(:roles AS text[])) a(role), unnest(CAST(:roles AS text[])) b(role)
        WHERE a.role <> b.role AND pg_has_role(a.role, b.role, 'MEMBER')
        """
    )
    owners = text(
        "SELECT nspname, nspowner::regrole::text FROM pg_namespace WHERE nspname IN ('ai', 'core')"
    )
    with owner.begin() as connection:
        assert dict(connection.execute(attributes, roles).all()) == dict.fromkeys(ROLES, False)
        assert set(connection.execute(schema_privileges, roles).all()) == {
            *((role, "public", "USAGE") for role in ROLES),
            ("core_owner", "core", "USAGE"),
            ("core_owner", "core", "CREATE"),
            ("app_core", "core", "USAGE"),
            ("ai_owner", "ai", "USAGE"),
            ("ai_owner", "ai", "CREATE"),
            ("app_ai", "ai", "USAGE"),
            ("corpus_ingestor", "ai", "USAGE"),
        }
        assert connection.execute(database_privileges, roles).all() == []
        assert connection.execute(memberships, roles).all() == []
        assert dict(connection.execute(owners).all()) == {"ai": "ai_owner", "core": "core_owner"}


def test_runtime_role_check_accepts_app_ai_and_rejects_the_owner(
    app_ai: Engine, owner: Engine
) -> None:
    with app_ai.connect() as connection:
        check_runtime_role(connection)
    with pytest.raises(RuntimeError, match="'ai_owner'.*owns schema ai"), owner.connect() as c:
        check_runtime_role(c)


def test_app_starts_as_app_ai_and_refuses_the_owner(urls: dict[str, str]) -> None:
    def app(role: str) -> TestClient:
        settings = Settings(internal_token=TOKEN, database_url=urls[role], _env_file=None)
        return TestClient(create_app(settings))

    with app("app_ai") as client:  # entering runs the lifespan, which checks the role
        assert client.get("/health", headers={INTERNAL_TOKEN_HEADER: TOKEN}).status_code == 200
    with pytest.raises(RuntimeError, match="must connect as app_ai"), app("ai_owner"):
        pass


def test_downgrade_to_base_and_upgrade_again(urls: dict[str, str], owner: Engine) -> None:
    chunks = text("SELECT to_regclass('ai.chunks') IS NOT NULL")

    migrate(urls["ai_owner"], "downgrade", "base")
    with owner.begin() as connection:
        assert not connection.execute(chunks).scalar_one()

    migrate(urls["ai_owner"], "upgrade", "head")
    with owner.begin() as connection:
        assert connection.execute(chunks).scalar_one()


@pytest.mark.parametrize(
    ("tenant_id", "error"),
    [(str(uuid4()), TypeError), (UUID(int=0), ValueError)],
    ids=["not-a-uuid", "nil-uuid"],
)
def test_tenant_transaction_rejects_an_invalid_tenant(
    tenant_id: object, error: type[Exception]
) -> None:
    engine = create_engine("postgresql+psycopg://")  # never connects
    with pytest.raises(error), tenant_transaction(engine, tenant_id):
        pass
