"""Integration tests against a real PostgreSQL: the shared bootstrap, the ai migrations and RLS.

The container and the role engines are the session fixtures of conftest.py. Assertions about app
behavior connect as the real roles, never as the superuser.
"""

import hashlib
import json
import logging
import os
from collections.abc import AsyncGenerator, Iterator, Sequence
from contextlib import AbstractContextManager, contextmanager
from dataclasses import dataclass, replace
from decimal import Decimal
from typing import Any
from uuid import UUID, uuid4

import anyio
import pytest
from conftest import ROLES, TOKEN, engine_for, migrate
from fastapi.sse import ServerSentEvent
from fastapi.testclient import TestClient
from sqlalchemy import Connection, Engine, create_engine, text
from sqlalchemy.exc import DBAPIError

from normetry_ai.api import create_app
from normetry_ai.api.auth import INTERNAL_TOKEN_HEADER
from normetry_ai.api.context import (
    ESTABLISHMENT_HEADER,
    JURISDICTION_HEADER,
    TENANT_HEADER,
    USER_HEADER,
    EstablishmentContext,
)
from normetry_ai.api.conversations import Chat, OpenTurn, answer_turn, get_chat, get_retriever
from normetry_ai.conversations import store as conversation_store
from normetry_ai.db import check_runtime_role, create_db_engine, tenant_transaction
from normetry_ai.ingestion.chunking import chunk_sections, content_hash
from normetry_ai.ingestion.corpus import Section, load_manifest, parse_source
from normetry_ai.ingestion.ingest import ingest_source
from normetry_ai.llm.claude import ModelUnavailable
from normetry_ai.llm.usage import Usage, record_usage
from normetry_ai.retrieval.models import RetrievedChunk
from normetry_ai.retrieval.policy import JurisdictionScope, jurisdictions_for
from normetry_ai.settings import AnswerSettings, ConversationSettings, Settings

pytestmark = pytest.mark.integration

# SQLSTATE 42501: a privilege is missing, or a new row violates a row-level security policy.
DENIED = "42501"

# Chunk ids come from the database (DEFAULT gen_random_uuid()), never from the caller.
INSERT_CHUNK = text(
    """
    INSERT INTO ai.chunks (tenant_id, source_type, source_id, source_version, jurisdiction,
                           language, content, content_hash)
    VALUES (:tenant_id, :source_type, :source_id, 1, :jurisdiction, :language, :content,
            :content_hash)
    RETURNING id
    """
)
INSERT_SOURCE = text(
    """
    INSERT INTO ai.sources (manifest_id, jurisdiction, code, title, version, language,
                            official_url)
    VALUES (:manifest_id, :jurisdiction, 'code', 'Title', '1', :language, 'https://example.org')
    RETURNING id
    """
)
JURISDICTION = {"en": "US", "pt": "BR"}


def register_source(connection: Connection, language: str = "en") -> UUID:
    """A fresh corpus source (written by corpus_ingestor): every corpus chunk points at one."""
    params = {
        "manifest_id": f"test-{uuid4()}",
        "jurisdiction": JURISDICTION[language],
        "language": language,
    }
    return connection.execute(INSERT_SOURCE, params).scalar_one()


def insert_chunk(
    connection: Connection,
    tenant_id: UUID | None,
    language: str = "en",
    content: str = "Keep cold food at 5 C or below.",
    source_id: UUID | None = None,
) -> UUID:
    """A tenant chunk, or a corpus chunk (no tenant) of `source_id` or of a new source."""
    corpus = tenant_id is None
    if corpus and source_id is None:
        source_id = register_source(connection, language)
    params = {
        "tenant_id": tenant_id,
        "source_type": "regulation" if corpus else "tenant_document",
        "source_id": source_id or uuid4(),
        "jurisdiction": JURISDICTION[language] if corpus else None,
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
def fails(sqlstate: str) -> Iterator[None]:
    """The block must fail with this SQLSTATE."""
    with pytest.raises(DBAPIError) as error:
        yield
    assert error.value.orig.sqlstate == sqlstate


def denied() -> AbstractContextManager[None]:
    """The block must fail with a privilege or row-level security error."""
    return fails(DENIED)


@dataclass(frozen=True)
class Seed:
    tenant_a: UUID
    tenant_b: UUID
    chunk_a: UUID
    chunk_b: UUID
    corpus_source: UUID
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
        corpus_source = register_source(connection)
        corpus_chunk = insert_chunk(connection, None, source_id=corpus_source)
    return Seed(tenant_a, tenant_b, chunk_a, chunk_b, corpus_source, corpus_chunk)


def test_each_tenant_reads_its_own_chunks_and_the_corpus(app_ai: Engine, seed: Seed) -> None:
    with tenant_transaction(app_ai, seed.tenant_a) as connection:
        assert visible(connection, *seed.all) == {seed.chunk_a, seed.corpus_chunk}
    with tenant_transaction(app_ai, seed.tenant_b) as connection:
        assert visible(connection, *seed.all) == {seed.chunk_b, seed.corpus_chunk}


def test_app_ai_cannot_insert_corpus_or_other_tenant_rows(app_ai: Engine, seed: Seed) -> None:
    for tenant_id in (None, seed.tenant_b):
        with denied(), tenant_transaction(app_ai, seed.tenant_a) as connection:
            insert_chunk(connection, tenant_id, source_id=seed.corpus_source)


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
            insert_chunk(connection, tenant_id, source_id=seed.corpus_source)


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
        INSERT INTO ai.chunks (tenant_id, source_type, source_id, source_version, jurisdiction,
                               language, content, content_hash)
        VALUES (:tenant_id, :source_type, :source_id, 1, :jurisdiction, 'en', 'Same text.',
                'same-hash')
        ON CONFLICT ON CONSTRAINT chunks_source_content_key DO NOTHING
        """
    )
    with corpus.begin() as connection:
        source_id = register_source(connection)

    def insert(engine: Engine, tenant_id: UUID | None) -> int:
        params = {
            "tenant_id": tenant_id,
            "source_type": "regulation" if tenant_id is None else "tenant_document",
            "source_id": source_id,
            "jurisdiction": "US" if tenant_id is None else None,
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


@pytest.mark.parametrize(
    ("table", "expected"),
    [
        (
            "ai.chunks",
            {
                (role, privilege)
                for role in ("app_ai", "corpus_ingestor")
                for privilege in ("SELECT", "INSERT", "DELETE")
            },
        ),
        (
            # app_ai only reads; corpus_ingestor registers and updates sources, never deletes one.
            "ai.sources",
            {
                ("app_ai", "SELECT"),
                *(("corpus_ingestor", privilege) for privilege in ("SELECT", "INSERT", "UPDATE")),
            },
        ),
        # Append-only: app_ai records usage and reads its own tenant's; corpus_ingestor nothing.
        ("ai.model_usage", {("app_ai", "SELECT"), ("app_ai", "INSERT")}),
        (
            "ai.conversations",
            {("app_ai", privilege) for privilege in ("SELECT", "INSERT", "UPDATE", "DELETE")},
        ),
        # Written once; deleted only with the conversation, by the cascade.
        ("ai.messages", {("app_ai", "SELECT"), ("app_ai", "INSERT")}),
    ],
)
def test_grants_are_exactly_what_the_roles_need(
    owner: Engine, table: str, expected: set[tuple[str, str]]
) -> None:
    grants = text(
        """
        SELECT a.grantee::regrole::text, a.privilege_type
        FROM pg_class c, aclexplode(c.relacl) a
        WHERE c.oid = CAST(:table AS regclass) AND a.grantee <> c.relowner
        """
    )
    with owner.begin() as connection:
        granted = set(connection.execute(grants, {"table": table}).all())

    assert granted == expected


def test_policies_are_scoped_to_roles_never_public(owner: Engine) -> None:
    policies = text(
        "SELECT tablename, policyname, cmd, roles::text[] FROM pg_policies WHERE schemaname = 'ai'"
    )
    with owner.begin() as connection:
        rows = connection.execute(policies).all()

    assert [row for row in rows if "public" in row.roles] == []
    assert {(row.tablename, row.policyname, row.cmd, tuple(row.roles)) for row in rows} == {
        ("chunks", "chunks_read", "SELECT", ("app_ai",)),
        ("chunks", "chunks_tenant_insert", "INSERT", ("app_ai",)),
        ("chunks", "chunks_tenant_delete", "DELETE", ("app_ai",)),
        ("chunks", "chunks_corpus_write", "ALL", ("corpus_ingestor",)),
        ("sources", "sources_read", "SELECT", ("app_ai",)),
        ("sources", "sources_corpus_write", "ALL", ("corpus_ingestor",)),
        ("model_usage", "model_usage_read", "SELECT", ("app_ai",)),
        ("model_usage", "model_usage_insert", "INSERT", ("app_ai",)),
        ("conversations", "conversations_tenant", "ALL", ("app_ai",)),
        ("messages", "messages_read", "SELECT", ("app_ai",)),
        ("messages", "messages_insert", "INSERT", ("app_ai",)),
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

    # The check is not vacuous.
    assert {"chunks", "sources", "model_usage", "conversations", "messages"} <= forced.keys()
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
    with pytest.raises(RuntimeError, match="inherits corpus_ingestor"), app("corpus_ingestor"):
        pass


def test_downgrade_to_base_and_upgrade_again(urls: dict[str, str], owner: Engine) -> None:
    names = ("chunks", "sources", "model_usage", "conversations", "messages")
    tables = text("SELECT " + ", ".join(f"to_regclass('ai.{name}') IS NOT NULL" for name in names))

    migrate(urls["ai_owner"], "downgrade", "base")
    with owner.begin() as connection:
        assert set(connection.execute(tables).one()) == {False}

    migrate(urls["ai_owner"], "upgrade", "head")
    with owner.begin() as connection:
        assert set(connection.execute(tables).one()) == {True}


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


# --- Model usage (ai.model_usage) ------------------------------------------------------------

USAGE = Usage("claude-opus-5-5", input_tokens=1200, output_tokens=300, cache_read_input_tokens=50)
PRICES = AnswerSettings(_env_file=None).model_prices
USAGE_ROWS = text("SELECT tenant_id, user_id, model, input_tokens, cost_usd FROM ai.model_usage")
INSERT_USAGE = text(
    """
    INSERT INTO ai.model_usage (tenant_id, user_id, model, input_tokens, output_tokens,
                                cache_creation_input_tokens, cache_read_input_tokens, cost_usd)
    VALUES (:tenant_id, 'user-b', 'claude-opus-5-5', 1, 1, 0, 0, 0)
    """
)


def usage_rows(engine: Engine, tenant_id: UUID) -> list[tuple]:
    with tenant_transaction(engine, tenant_id) as connection:
        return [tuple(row) for row in connection.execute(USAGE_ROWS)]


def test_usage_is_recorded_for_the_tenant_and_user_with_its_cost(app_ai: Engine) -> None:
    tenant = uuid4()

    record_usage(app_ai, USAGE, tenant_id=tenant, user_id="user-a", prices=PRICES)

    # 1200 x $4 + 300 x $20 + 50 x $0.20 per million tokens
    assert usage_rows(app_ai, tenant) == [
        (tenant, "user-a", "claude-opus-5-5", 1200, Decimal("0.01081"))
    ]


def test_another_tenant_cannot_read_change_or_forge_usage(app_ai: Engine) -> None:
    tenant_a, tenant_b = uuid4(), uuid4()
    record_usage(app_ai, USAGE, tenant_id=tenant_a, user_id="user-a", prices=PRICES)

    assert usage_rows(app_ai, tenant_b) == []
    # Append-only: no tenant can change or delete a usage row, its own included.
    for tenant, statement in [
        (tenant_b, "UPDATE ai.model_usage SET cost_usd = 0"),
        (tenant_b, "DELETE FROM ai.model_usage"),
        (tenant_a, "UPDATE ai.model_usage SET cost_usd = 0"),
        (tenant_a, "DELETE FROM ai.model_usage"),
    ]:
        with denied(), tenant_transaction(app_ai, tenant) as connection:
            connection.execute(text(statement))
    with denied(), tenant_transaction(app_ai, tenant_b) as connection:
        connection.execute(INSERT_USAGE, {"tenant_id": tenant_a})
    assert len(usage_rows(app_ai, tenant_a)) == 1


def test_without_a_tenant_no_usage_is_visible_and_none_can_be_recorded(app_ai: Engine) -> None:
    tenant = uuid4()
    record_usage(app_ai, USAGE, tenant_id=tenant, user_id="user-a", prices=PRICES)

    with app_ai.begin() as connection:
        assert connection.execute(USAGE_ROWS).all() == []
    with denied(), app_ai.begin() as connection:
        connection.execute(INSERT_USAGE, {"tenant_id": tenant})


def test_only_app_ai_sees_usage(owner: Engine, corpus: Engine, app_ai: Engine) -> None:
    tenant = uuid4()
    record_usage(app_ai, USAGE, tenant_id=tenant, user_id="user-a", prices=PRICES)

    # FORCE ROW LEVEL SECURITY with no policy for the owner: nothing, even with the tenant set.
    with tenant_transaction(owner, tenant) as connection:
        assert connection.execute(USAGE_ROWS).all() == []
    with denied(), tenant_transaction(corpus, tenant) as connection:
        connection.execute(USAGE_ROWS)


# --- Corpus sources and ingestion ------------------------------------------------------------


class FakeEmbedder:
    """Deterministic vectors from each text's hash; records every text it embeds."""

    def __init__(self, name: str = "fake") -> None:
        self.name = name
        self.embedded: list[str] = []

    def embed_documents(self, texts: Sequence[str]) -> list[list[float]]:
        self.embedded += texts
        # 32 bytes, each in (0, 1], repeated to 1024 dimensions: never the zero vector.
        return [[(b + 1) / 256 for b in hashlib.sha256(t.encode()).digest()] * 32 for t in texts]


def manifest_source(**fields: str) -> dict:
    """A manifest entry with a fresh id."""
    return {
        "id": f"test-{uuid4()}",
        "jurisdiction": "EU",
        "code": "reg-852-2004",
        "title": "Regulation (EC) No 852/2004",
        "version": "2021-03-24",
        "language": "en",
        "official_url": "https://eur-lex.europa.eu/",
        **fields,
    }


def sections(source: dict, *texts: str) -> list[Section]:
    """One section per text, at "Article 1", "Article 2", ..."""
    return [
        Section(
            source["id"],
            source["jurisdiction"],
            source["language"],
            source["version"],
            f"Article {number}",
            body,
        )
        for number, body in enumerate(texts, start=1)
    ]


def stored(engine: Engine, manifest_id: str) -> dict[str, object]:
    """The source row and its chunks, each with its xmin, so any rewrite shows."""
    with engine.begin() as connection:
        source = connection.execute(
            text(
                "SELECT id, xmin::text, version, revision FROM ai.sources"
                " WHERE manifest_id = :manifest_id"
            ),
            {"manifest_id": manifest_id},
        ).one()
        chunks = connection.execute(
            text(
                """
                SELECT id, xmin::text, source_version, section_path, content,
                       embedding::text AS embedding
                FROM ai.chunks WHERE tenant_id IS NULL AND source_id = :source_id
                """
            ),
            {"source_id": source.id},
        ).all()
    return {"source": tuple(source), "chunks": {tuple(chunk) for chunk in chunks}}


LONG_ARTICLE = " ".join(f"Paragraph {n} sets a hygiene requirement." for n in range(60))


def test_ingesting_twice_changes_nothing(corpus: Engine) -> None:
    source = manifest_source()
    parsed = sections(source, "Short article.", LONG_ARTICLE)
    embedder = FakeEmbedder()

    first = ingest_source(corpus, source, parsed, embedder)
    before = stored(corpus, source["id"])
    second = ingest_source(corpus, source, parsed, embedder)

    assert (first.status, second.status) == ("new", "unchanged")
    assert first.chunks == second.chunks == len(before["chunks"]) > 2  # the long article is split
    assert (first.embedded, second.embedded) == (first.chunks, 0)
    assert stored(corpus, source["id"]) == before


def test_a_new_source_version_replaces_its_chunks(corpus: Engine, app_ai: Engine) -> None:
    v1 = manifest_source(version="2004-04-29")
    v2 = {**v1, "version": "2021-03-24"}
    embedder = FakeEmbedder()
    ingest_source(corpus, v1, sections(v1, "Old wording.", "Unchanged article."), embedder)
    before = {chunk[4]: chunk[5] for chunk in stored(corpus, v1["id"])["chunks"]}
    embedder.embedded.clear()
    # A tenant row that looks like the coming chunk: same source id, hash and embedding model.
    # Ingestion must neither reuse its embedding nor delete it.
    tenant = uuid4()
    tenant_row = text(
        "SELECT id, xmin::text FROM ai.chunks"
        " WHERE tenant_id IS NOT NULL AND content = 'New wording.'"
    )
    with tenant_transaction(app_ai, tenant) as connection:
        connection.execute(
            text(
                """
                INSERT INTO ai.chunks (tenant_id, source_type, source_id, source_version, language,
                                       section_path, content, content_hash, embedding, metadata)
                VALUES (:tenant_id, 'tenant_document', :source_id, 1, 'en', 'Article 1',
                        'New wording.', :content_hash, CAST(:embedding AS public.vector),
                        '{"embedding_model": "fake"}')
                """
            ),
            {
                "tenant_id": tenant,
                "source_id": stored(corpus, v1["id"])["source"][0],
                "content_hash": content_hash("Article 1", "New wording."),
                "embedding": "[" + ",".join(["0.5"] * 1024) + "]",
            },
        )
        tenant_before = connection.execute(tenant_row).one()

    result = ingest_source(corpus, v2, sections(v2, "New wording.", "Unchanged article."), embedder)
    after = stored(corpus, v1["id"])

    assert result.status == "updated"
    assert after["source"][2:] == ("2021-03-24", 2)  # version label and revision
    assert {chunk[2:5] for chunk in after["chunks"]} == {
        (2, "Article 1", "New wording."),
        (2, "Article 2", "Unchanged article."),
    }
    # Only the new text was embedded; the unchanged chunk kept its stored embedding.
    assert embedder.embedded == ["New wording."]
    reused = {chunk[4]: chunk[5] for chunk in after["chunks"]}["Unchanged article."]
    assert reused == before["Unchanged article."]
    with tenant_transaction(app_ai, tenant) as connection:
        assert connection.execute(tenant_row).one() == tenant_before  # untouched


def test_another_embedding_model_re_embeds_the_source(corpus: Engine) -> None:
    source = manifest_source()
    parsed = sections(source, "First article.", "Second article.")
    ingest_source(corpus, source, parsed, FakeEmbedder("model-a"))
    other = FakeEmbedder("model-b")

    result = ingest_source(corpus, source, parsed, other)

    assert (result.status, sorted(other.embedded)) == (
        "updated",
        ["First article.", "Second article."],
    )
    assert stored(corpus, source["id"])["source"][3] == 1  # same version: same revision


def test_an_empty_parse_never_empties_a_source(corpus: Engine) -> None:
    source = manifest_source()
    ingest_source(corpus, source, sections(source, "Article text."), FakeEmbedder())
    before = stored(corpus, source["id"])

    with pytest.raises(ValueError, match="no sections"):
        ingest_source(corpus, source, [], FakeEmbedder())
    assert stored(corpus, source["id"]) == before


def test_the_real_corpus_chunks_carry_jurisdiction_language_path_and_embedding(
    corpus: Engine, app_ai: Engine
) -> None:
    try:
        parsed = [(source, parse_source(source)) for source in load_manifest()]
    except FileNotFoundError:
        if os.environ.get("CI"):
            raise
        pytest.skip(
            "Corpus not fetched: run `uv run corpus/scripts/fetch_corpus.py` from the root."
        )
    embedder = FakeEmbedder()

    first = [ingest_source(corpus, source, found, embedder) for source, found in parsed]
    second = [ingest_source(corpus, source, found, embedder) for source, found in parsed]

    assert {result.status for result in second} == {"unchanged"}
    # Read back as the service: every corpus chunk is complete and matches its source.
    summary = text(
        """
        SELECT s.manifest_id, count(*),
               bool_and(c.jurisdiction = s.jurisdiction AND c.language = s.language
                        AND c.source_type = 'regulation' AND c.source_version = s.revision
                        AND c.section_path <> '' AND c.embedding IS NOT NULL
                        AND c.tsv <> ''::tsvector)
        FROM ai.chunks c JOIN ai.sources s ON s.id = c.source_id
        WHERE c.tenant_id IS NULL AND s.manifest_id = ANY(:ids)
        GROUP BY s.manifest_id
        """
    )
    with app_ai.begin() as connection:
        rows = connection.execute(summary, {"ids": [s["id"] for s, _ in parsed]}).all()
    assert {row[0]: (row[1], row[2]) for row in rows} == {
        source["id"]: (len(chunk_sections(found)), True) for source, found in parsed
    }
    assert [result.chunks for result in first] == [len(chunk_sections(f)) for _, f in parsed]


def test_app_ai_reads_sources_but_cannot_write_them(
    app_ai: Engine, corpus: Engine, owner: Engine
) -> None:
    with corpus.begin() as connection:
        source_id = register_source(connection)
    count = text("SELECT count(*) FROM ai.sources WHERE id = :id")
    with app_ai.begin() as connection:
        assert connection.execute(count, {"id": source_id}).scalar_one() == 1
    # FORCE ROW LEVEL SECURITY with no policy for the owner: not even the owner reads a source.
    with owner.begin() as connection:
        assert connection.execute(count, {"id": source_id}).scalar_one() == 0

    params = {
        "id": source_id,
        "manifest_id": f"test-{uuid4()}",
        "jurisdiction": "US",
        "language": "en",
    }
    for statement in (
        INSERT_SOURCE,
        text("UPDATE ai.sources SET title = 'Poisoned' WHERE id = :id"),
        text("DELETE FROM ai.sources WHERE id = :id"),
    ):
        with denied(), app_ai.begin() as connection:
            connection.execute(statement, params)
    # corpus_ingestor moves sources to new versions but never deletes one.
    with denied(), corpus.begin() as connection:
        connection.execute(text("DELETE FROM ai.sources WHERE id = :id"), params)


def test_a_corpus_chunk_must_match_a_registered_source(corpus: Engine, app_ai: Engine) -> None:
    with corpus.begin() as connection:
        source_id = register_source(connection, "en")  # US, en
    base = {
        "tenant_id": None,
        "source_type": "regulation",
        "source_id": source_id,
        "jurisdiction": "US",
        "language": "en",
        "content": "Text.",
        "content_hash": uuid4().hex,
    }
    for overrides, sqlstate in (
        ({"jurisdiction": "BR"}, "23503"),  # not the source's jurisdiction
        ({"language": "pt"}, "23503"),  # not the source's language
        ({"source_id": uuid4()}, "23503"),  # no such source
        ({"jurisdiction": None}, "23514"),  # a corpus chunk without jurisdiction
    ):
        with fails(sqlstate), corpus.begin() as connection:
            connection.execute(INSERT_CHUNK, {**base, **overrides})
    # A tenant chunk carries no jurisdiction.
    tenant = uuid4()
    tenant_row = {**base, "tenant_id": tenant, "source_type": "tenant_document"}
    with fails("23514"), tenant_transaction(app_ai, tenant) as connection:
        connection.execute(INSERT_CHUNK, tenant_row)


# --- Conversations (ai.conversations, ai.messages) -------------------------------------------

INSERT_CONVERSATION = text(
    """
    INSERT INTO ai.conversations (tenant_id, user_id, establishment_id, jurisdiction)
    VALUES (:tenant_id, 'user-a', :establishment_id, 'BR')
    RETURNING id
    """
)
INSERT_MESSAGE = text(
    """
    INSERT INTO ai.messages (tenant_id, conversation_id, role, text)
    VALUES (:tenant_id, :conversation_id, 'user', 'How cold?')
    """
)
CHAT_ROWS = text(
    "SELECT (SELECT count(*) FROM ai.conversations), (SELECT count(*) FROM ai.messages)"
)


def new_conversation(engine: Engine, tenant: UUID) -> UUID:
    """A conversation with one question, written by app_ai for `tenant`."""
    with tenant_transaction(engine, tenant) as connection:
        params = {"tenant_id": tenant, "establishment_id": uuid4()}
        conversation = connection.execute(INSERT_CONVERSATION, params).scalar_one()
        connection.execute(INSERT_MESSAGE, {"tenant_id": tenant, "conversation_id": conversation})
    return conversation


def chat_rows(engine: Engine, tenant: UUID) -> tuple[int, int]:
    with tenant_transaction(engine, tenant) as connection:
        return tuple(connection.execute(CHAT_ROWS).one())


def test_another_tenant_cannot_read_change_delete_or_join_conversations(
    app_ai: Engine,
) -> None:
    tenant_a, tenant_b = uuid4(), uuid4()
    conversation = new_conversation(app_ai, tenant_a)

    assert chat_rows(app_ai, tenant_b) == (0, 0)
    with tenant_transaction(app_ai, tenant_b) as connection:
        for statement in (
            "UPDATE ai.conversations SET title = 'changed'",
            "DELETE FROM ai.conversations",
        ):
            assert connection.execute(text(statement)).rowcount == 0
    with denied(), tenant_transaction(app_ai, tenant_b) as connection:
        connection.execute(
            INSERT_CONVERSATION, {"tenant_id": tenant_a, "establishment_id": uuid4()}
        )
    with denied(), tenant_transaction(app_ai, tenant_b) as connection:
        connection.execute(INSERT_MESSAGE, {"tenant_id": tenant_a, "conversation_id": conversation})
    # With its own tenant id, B's message cannot point at A's conversation: the key has the tenant.
    with fails("23503"), tenant_transaction(app_ai, tenant_b) as connection:
        connection.execute(INSERT_MESSAGE, {"tenant_id": tenant_b, "conversation_id": conversation})
    assert chat_rows(app_ai, tenant_a) == (1, 1)


def test_without_a_tenant_no_conversation_is_visible_and_none_can_be_written(
    app_ai: Engine,
) -> None:
    tenant = uuid4()
    conversation = new_conversation(app_ai, tenant)

    with app_ai.begin() as connection:
        assert tuple(connection.execute(CHAT_ROWS).one()) == (0, 0)
    with denied(), app_ai.begin() as connection:
        connection.execute(INSERT_CONVERSATION, {"tenant_id": tenant, "establishment_id": uuid4()})
    with denied(), app_ai.begin() as connection:
        connection.execute(INSERT_MESSAGE, {"tenant_id": tenant, "conversation_id": conversation})


def test_messages_never_change_and_go_with_their_conversation(app_ai: Engine) -> None:
    tenant = uuid4()
    conversation = new_conversation(app_ai, tenant)

    for statement in (
        "UPDATE ai.messages SET text = 'changed'",
        "DELETE FROM ai.messages",
    ):
        with denied(), tenant_transaction(app_ai, tenant) as connection:
            connection.execute(text(statement))
    with tenant_transaction(app_ai, tenant) as connection:
        delete_conversation = text("DELETE FROM ai.conversations WHERE id = :id")
        assert connection.execute(delete_conversation, {"id": conversation}).rowcount == 1
    # The cascade runs as the owner, so FORCE ROW LEVEL SECURITY does not stop it.
    assert chat_rows(app_ai, tenant) == (0, 0)


def test_only_app_ai_sees_conversations(owner: Engine, corpus: Engine, app_ai: Engine) -> None:
    tenant = uuid4()
    new_conversation(app_ai, tenant)

    assert chat_rows(owner, tenant) == (0, 0)
    for table in ("ai.conversations", "ai.messages"):
        with denied(), tenant_transaction(corpus, tenant) as connection:
            connection.execute(text(f"SELECT count(*) FROM {table}"))  # noqa: S608 (fixed names)


@pytest.mark.parametrize(
    ("columns", "values"),
    [
        ("role", "'user'"),  # a question without its text
        ("role, text, kind", "'user', 'Hi', 'answer'"),
        ("role", "'assistant'"),  # an answer without its kind
        ("role, kind", "'assistant', 'refusal'"),  # a refusal without its reason
        ("role, kind, reason", "'assistant', 'answer', 'low_score'"),
        ("role, kind, claims", "'assistant', 'answer', '{}'"),
    ],
)
def test_a_message_has_the_shape_of_its_role(app_ai: Engine, columns: str, values: str) -> None:
    tenant = uuid4()
    conversation = new_conversation(app_ai, tenant)
    insert = text(
        f"INSERT INTO ai.messages (tenant_id, conversation_id, {columns})"  # noqa: S608
        f" VALUES (:tenant_id, :conversation_id, {values})"
    )

    with fails("23514"), tenant_transaction(app_ai, tenant) as connection:
        connection.execute(insert, {"tenant_id": tenant, "conversation_id": conversation})


# --- Chat API (conversations, messages over SSE) ---------------------------------------------

CHAT_SETTINGS = AnswerSettings(
    anthropic_api_key=None,
    answer_min_rerank_score=0.5,
    answer_min_quote_words=4,
    _env_file=None,
)
HISTORY_SETTINGS = ConversationSettings(chat_history_turns=2, _env_file=None)
STARTED = Usage("claude-opus-5-5", input_tokens=900, output_tokens=1)
ANSWERED = Usage("claude-opus-5-5", input_tokens=900, output_tokens=120)
REWRITTEN = Usage("claude-haiku-5-5", input_tokens=150, output_tokens=12)
COLD_TEXT = "Time/temperature control for safety food shall be maintained at 5°C (41°F) or less."
HOT_TEXT = "Hot food shall be maintained at 57°C (135°F) or above, except during preparation."
COLD_QUOTE = "shall be maintained at 5°C (41°F) or less"
HOT_QUOTE = "maintained at 57°C (135°F) or above"


def retrieved(content: str, score: float = 0.9) -> RetrievedChunk:
    return RetrievedChunk(
        chunk_id=uuid4(),
        content=content,
        section_path="3-501.16",
        language="en",
        jurisdiction="US",
        source_id=uuid4(),
        source_title="FDA Food Code",
        source_version="2026",
        official_url="https://example.org/food-code",
        score=score,
    )


COLD, HOT = retrieved(COLD_TEXT), retrieved(HOT_TEXT)
RDC_TEXT = "Os alimentos preparados e conservados sob refrigeração devem estar a 5ºC ou menos."
RDC_QUOTE = "devem estar a 5ºC ou menos"
RDC = replace(
    retrieved(RDC_TEXT),
    language="pt",
    jurisdiction="BR",
    section_path="4.8.15",
    source_title="RDC 216/2004",
    source_version="2014-10-01",
)


def raw_claim(text_: str, *citations: tuple[str, str]) -> dict[str, object]:
    return {
        "text": text_,
        "citations": [{"excerpt_id": excerpt, "quote": quote} for excerpt, quote in citations],
    }


COLD_CLAIM = raw_claim("Hold cold food at 5 °C or below.", ("E1", COLD_QUOTE))
HOT_CLAIM = raw_claim("Hold hot food at 57 °C or above.", ("E2", HOT_QUOTE))
INVENTED = raw_claim("Hold it at 30 °C.", ("E7", COLD_QUOTE))


class FakeClaims:
    """A ClaimModel: a usage snapshot, the given claims, then the final usage (or a failure)."""

    def __init__(self, *claims: object, fail: bool = False, hang: bool = False) -> None:
        self.claims, self.fail, self.hang = claims, fail, hang
        self.questions: list[str] = []

    async def stream_claims(self, system: str, user: str) -> AsyncGenerator[object, None]:
        self.questions.append(user.rsplit("<question>\n", 1)[1].split("\n</question>")[0])
        yield STARTED
        for claim in self.claims:
            yield claim
        if self.hang:
            await anyio.sleep_forever()  # still generating when the client goes away
        if self.fail:
            raise ModelUnavailable("overloaded")
        yield ANSWERED


class FakeRewrite:
    def __init__(self, text_: str = "How long can cooked rice stay out of the fridge?") -> None:
        self.text = text_
        self.prompts: list[str] = []

    async def complete(self, system: str, user: str) -> tuple[str, Usage]:
        self.prompts.append(user)
        return self.text, REWRITTEN


class FakeRetriever:
    """Records each (question, establishment, jurisdiction) it is called with."""

    def __init__(self, *chunks: RetrievedChunk) -> None:
        self.chunks = list(chunks)
        self.calls: list[tuple[str, UUID, str]] = []

    def __call__(self, question: str, ctx: EstablishmentContext) -> list[RetrievedChunk]:
        self.calls.append((question, ctx.establishment_id, ctx.jurisdiction))
        return self.chunks


class RecordingRetriever:
    """Stands in for hybrid.Retriever on app.state: records the question, the tenant set on the
    connection it is given and the scope, then returns its chunks."""

    def __init__(self, *chunks: RetrievedChunk) -> None:
        self.chunks = list(chunks)
        self.calls: list[tuple[str, str, JurisdictionScope]] = []

    def retrieve(
        self, connection: Connection, question: str, scope: JurisdictionScope
    ) -> list[RetrievedChunk]:
        setting = text("SELECT current_setting('app.tenant_id', true)")
        self.calls.append((question, connection.execute(setting).scalar_one(), scope))
        return self.chunks


@dataclass(frozen=True)
class Person:
    """A caller: what the core's gateway would put in the internal headers."""

    tenant: UUID
    user: str
    establishment: UUID
    jurisdiction: str = "US"

    @property
    def headers(self) -> dict[str, str]:
        return {
            INTERNAL_TOKEN_HEADER: TOKEN,
            TENANT_HEADER: str(self.tenant),
            USER_HEADER: self.user,
        }

    @property
    def establishment_headers(self) -> dict[str, str]:
        return {
            **self.headers,
            ESTABLISHMENT_HEADER: str(self.establishment),
            JURISDICTION_HEADER: self.jurisdiction,
        }


def person(tenant: UUID | None = None, user: str = "user-a", jurisdiction: str = "US") -> Person:
    return Person(tenant or uuid4(), user, uuid4(), jurisdiction)


class ChatApi:
    """The service as app_ai, with fake models and retrieval."""

    def __init__(self, client: TestClient) -> None:
        self.client = client
        self.use()

    def use(
        self,
        model: FakeClaims | None = None,
        retriever: FakeRetriever | None = None,
        rewrite: FakeRewrite | None = None,
    ) -> None:
        self.model = model or FakeClaims(COLD_CLAIM)
        self.retriever = retriever or FakeRetriever(COLD, HOT)
        self.rewrite = rewrite or FakeRewrite()
        chat = Chat(self.model, self.rewrite, CHAT_SETTINGS, HISTORY_SETTINGS)
        overrides = self.client.app.dependency_overrides
        overrides[get_chat] = lambda: chat
        overrides[get_retriever] = lambda: self.retriever

    def create(self, caller: Person) -> str:
        response = self.client.post(
            f"/establishments/{caller.establishment}/conversations",
            headers=caller.establishment_headers,
        )
        assert response.status_code == 201, response.text
        return response.json()["id"]

    def ask(
        self,
        caller: Person,
        conversation: str,
        question: str = "How cold must food be held?",
    ) -> Any:
        return self.client.post(
            f"/establishments/{caller.establishment}/conversations/{conversation}/messages",
            json={"text": question},
            headers=caller.establishment_headers,
        )

    def events(self, caller: Person, conversation: str, question: str = "How cold?") -> list:
        response = self.ask(caller, conversation, question)
        assert response.status_code == 200, response.text
        assert response.headers["content-type"].startswith("text/event-stream")
        return sse_events(response.text)

    def conversations(self, caller: Person) -> list[dict]:
        response = self.client.get("/conversations", headers=caller.headers)
        assert response.status_code == 200, response.text
        return response.json()

    def get(self, caller: Person, conversation: str) -> Any:
        return self.client.get(f"/conversations/{conversation}", headers=caller.headers)


def sse_events(body: str) -> list[tuple[str, object]]:
    events = []
    for block in body.split("\n\n"):
        lines = [line for line in block.splitlines() if line and not line.startswith(":")]
        if lines:
            fields = dict(line.split(": ", 1) for line in lines)
            events.append((fields["event"], json.loads(fields["data"])))
    return events


@pytest.fixture
def chat_api(urls: dict[str, str]) -> Iterator[ChatApi]:
    settings = Settings(internal_token=TOKEN, database_url=urls["app_ai"], _env_file=None)
    with TestClient(create_app(settings, answer_settings=CHAT_SETTINGS)) as client:
        yield ChatApi(client)


def usage_by_user(engine: Engine, tenant: UUID) -> list[tuple[str, str, int]]:
    with tenant_transaction(engine, tenant) as connection:
        rows = connection.execute(
            text("SELECT user_id, model, output_tokens FROM ai.model_usage ORDER BY created_at")
        )
        return sorted(tuple(row) for row in rows)


def test_a_user_lists_reads_and_deletes_only_their_own_conversations(
    chat_api: ChatApi,
) -> None:
    alice = person()
    bob = person(alice.tenant, user="user-b")  # same tenant, another user
    carol = person(user=alice.user)  # another tenant, the same user id
    mine, bobs, carols = (
        chat_api.create(alice),
        chat_api.create(bob),
        chat_api.create(carol),
    )

    assert [c["id"] for c in chat_api.conversations(alice)] == [mine]
    assert [c["id"] for c in chat_api.conversations(bob)] == [bobs]
    assert [c["id"] for c in chat_api.conversations(carol)] == [carols]
    for caller, conversation in (
        (alice, bobs),
        (alice, carols),
        (carol, mine),
        (bob, mine),
    ):
        assert chat_api.get(caller, conversation).status_code == 404
        delete_url = f"/conversations/{conversation}"
        assert chat_api.client.delete(delete_url, headers=caller.headers).status_code == 404
        assert chat_api.ask(caller, conversation).status_code == 404
    # Through the owner's own establishment route too: the conversation is still not theirs.
    intruder = Person(alice.tenant, bob.user, alice.establishment)
    assert chat_api.ask(intruder, mine).status_code == 404
    assert chat_api.get(bob, bobs).json()["messages"] == []
    assert chat_api.get(carol, carols).json()["messages"] == []
    assert chat_api.get(alice, mine).json()["messages"] == []

    response = chat_api.client.delete(f"/conversations/{mine}", headers=alice.headers)
    assert (response.status_code, response.content) == (204, b"")
    assert chat_api.conversations(alice) == []
    assert chat_api.get(alice, mine).status_code == 404
    assert [c["id"] for c in chat_api.conversations(bob)] == [bobs]


def test_a_turn_retrieves_in_the_callers_tenant_over_the_policy_scope(chat_api: ChatApi) -> None:
    alice = person(jurisdiction="BR")
    conversation = chat_api.create(alice)
    recorder = RecordingRetriever(RDC)
    chat_api.use(model=FakeClaims(raw_claim("Keep it at 5 °C or below.", ("E1", RDC_QUOTE))))
    # The real dependency, with the process's retriever replaced: no model weights needed.
    del chat_api.client.app.dependency_overrides[get_retriever]
    chat_api.client.app.state.retriever = recorder

    events = chat_api.events(alice, conversation, "Qual a temperatura dos alimentos frios?")

    assert [name for name, _ in events] == ["claim", "done"]
    assert recorder.calls == [
        ("Qual a temperatura dos alimentos frios?", str(alice.tenant), jurisdictions_for("BR"))
    ]


def test_an_answer_takes_the_questions_language_and_each_citation_its_chunks(
    chat_api: ChatApi,
) -> None:
    alice = person(jurisdiction="BR")
    conversation = chat_api.create(alice)
    chat_api.use(
        model=FakeClaims(raw_claim("Keep it at 5 °C or below.", ("E1", RDC_QUOTE))),
        retriever=FakeRetriever(RDC),
    )

    events = chat_api.events(alice, conversation, "How cold must prepared food be kept?")

    assert [name for name, _ in events] == ["claim", "done"]
    answer_ = chat_api.get(alice, conversation).json()["messages"][-1]
    assert answer_["language"] == "en"
    assert [c["language"] for claim in answer_["claims"] for c in claim["citations"]] == ["pt"]


def test_a_conversation_keeps_its_establishments_jurisdiction_on_every_turn(
    chat_api: ChatApi,
) -> None:
    alice = person(jurisdiction="BR")
    conversation = chat_api.create(alice)

    chat_api.events(alice, conversation, "Qual a temperatura dos alimentos frios?")
    chat_api.events(alice, conversation, "E dos quentes?")

    assert [call[1:] for call in chat_api.retriever.calls] == [(alice.establishment, "BR")] * 2
    # Another establishment of the same user, or other establishment headers: no such
    # conversation there, and nothing is stored.
    elsewhere = Person(alice.tenant, alice.user, uuid4(), "US")
    other_jurisdiction = Person(alice.tenant, alice.user, alice.establishment, "US")
    for caller in (elsewhere, other_jurisdiction):
        assert chat_api.ask(caller, conversation).status_code == 404
    stored = chat_api.get(alice, conversation).json()
    assert stored["establishmentId"] == str(alice.establishment)
    assert [m["role"] for m in stored["messages"]] == ["user", "assistant"] * 2
    assert len(chat_api.retriever.calls) == 2


def test_guarded_claims_stream_in_order_then_done_and_are_stored(
    chat_api: ChatApi,
) -> None:
    alice = person()
    conversation = chat_api.create(alice)
    chat_api.use(model=FakeClaims(COLD_CLAIM, INVENTED, HOT_CLAIM))

    events = chat_api.events(alice, conversation, "How must cold and hot food be held?")

    assert [name for name, _ in events] == ["claim", "claim", "done"]
    cold, hot = events[0][1], events[1][1]
    assert [cold["text"], hot["text"]] == [COLD_CLAIM["text"], HOT_CLAIM["text"]]
    [citation] = cold["citations"]
    assert citation == {
        "id": citation["id"],
        "sourceTitle": "FDA Food Code",
        "sourceVersion": "2026",
        "language": "en",
        "sectionPath": "3-501.16",
        "text": COLD_TEXT,
        "quote": COLD_QUOTE,
        "quoteStart": COLD_TEXT.index(COLD_QUOTE),
        "quoteEnd": COLD_TEXT.index(COLD_QUOTE) + len(COLD_QUOTE),
        "officialUrl": "https://example.org/food-code",
    }
    assert events[2][1] == {}

    stored = chat_api.get(alice, conversation).json()
    assert stored["title"] == "How must cold and hot food be held?"
    question, answer_ = stored["messages"]
    assert question["role"] == "user" and question["text"] == stored["title"]
    assert {key: answer_[key] for key in ("role", "kind", "reason", "language", "claims")} == {
        "role": "assistant",
        "kind": "answer",
        "reason": None,
        "language": "en",
        "claims": [cold, hot],
    }
    [listed] = chat_api.conversations(alice)
    assert listed == {
        "id": conversation,
        "establishmentId": str(alice.establishment),
        "title": stored["title"],
        "updatedAt": stored["updatedAt"],
    }


@pytest.mark.parametrize(
    ("model", "chunks", "reason"),
    [
        (FakeClaims(COLD_CLAIM), [retrieved(COLD_TEXT, score=0.1)], "low_score"),
        (FakeClaims(INVENTED), [COLD, HOT], "no_supported_claims"),
    ],
    ids=["low-score", "no-supported-claims"],
)
def test_a_refusal_arrives_as_a_single_event(
    chat_api: ChatApi, model: FakeClaims, chunks: list[RetrievedChunk], reason: str
) -> None:
    alice = person()
    conversation = chat_api.create(alice)
    chat_api.use(model=model, retriever=FakeRetriever(*chunks))

    assert chat_api.events(alice, conversation) == [("refusal", {"reason": reason})]
    stored = chat_api.get(alice, conversation).json()["messages"][-1]
    assert (stored["kind"], stored["reason"], stored["claims"]) == (
        "refusal",
        reason,
        [],
    )


LEAK = "the secret recipe of tenant A"


def logged(caplog: pytest.LogCaptureFixture) -> str:
    return "\n".join(f"{r.getMessage()} {r.exc_text or ''}" for r in caplog.records)


def test_an_answer_that_cannot_be_stored_ends_unavailable_and_logs_no_text(
    chat_api: ChatApi, caplog: pytest.LogCaptureFixture
) -> None:
    alice = person()
    conversation = chat_api.create(alice)
    # jsonb rejects \u0000: the database refuses the claims, with their text bound.
    chat_api.use(model=FakeClaims(raw_claim(f"Hold it cold.\x00 {LEAK}", ("E1", COLD_QUOTE))))

    with caplog.at_level(logging.INFO):
        events = chat_api.events(alice, conversation)

    assert [name for name, _ in events] == ["claim", "unavailable"]
    assert "Storing the answer failed: DataError" in logged(caplog)
    assert LEAK not in logged(caplog)
    assert [m["role"] for m in chat_api.get(alice, conversation).json()["messages"]] == ["user"]


def test_a_question_that_cannot_be_stored_gets_503_and_logs_no_text(
    chat_api: ChatApi, caplog: pytest.LogCaptureFixture
) -> None:
    alice = person()
    conversation = chat_api.create(alice)

    with caplog.at_level(logging.INFO):
        response = chat_api.ask(alice, conversation, f"How cold?\x00 {LEAK}")

    assert response.status_code == 503
    assert "Storing the question failed: DataError" in logged(caplog)
    assert LEAK not in logged(caplog) + response.text
    assert chat_api.retriever.calls == []
    assert chat_api.get(alice, conversation).json()["messages"] == []


def test_database_errors_carry_no_bound_parameters(urls: dict[str, str]) -> None:
    settings = Settings(internal_token=TOKEN, database_url=urls["app_ai"], _env_file=None)
    engine = create_db_engine(settings)
    try:
        with pytest.raises(DBAPIError) as error, engine.begin() as connection:
            connection.execute(text("SELECT 1 / 0 WHERE CAST(:q AS text) IS NOT NULL"), {"q": LEAK})
    finally:
        engine.dispose()

    assert "division by zero" in str(error.value)
    assert LEAK not in str(error.value)


def test_a_model_failure_arrives_as_unavailable_after_the_claims_so_far(
    chat_api: ChatApi,
) -> None:
    alice = person()
    conversation = chat_api.create(alice)
    chat_api.use(model=FakeClaims(fail=True))
    assert chat_api.events(alice, conversation) == [("unavailable", {})]

    chat_api.use(model=FakeClaims(COLD_CLAIM, fail=True))
    events = chat_api.events(alice, conversation)

    assert [name for name, _ in events] == ["claim", "unavailable"]
    stored = chat_api.get(alice, conversation).json()["messages"]
    assert [(m["kind"], len(m["claims"])) for m in stored[1::2]] == [
        ("unavailable", 0),
        ("unavailable", 1),
    ]


def test_a_follow_up_is_rewritten_from_the_last_turns_before_retrieval(
    chat_api: ChatApi, app_ai: Engine
) -> None:
    alice = person()
    conversation = chat_api.create(alice)
    chat_api.events(alice, conversation, "How long can cooked rice stay out?")

    chat_api.events(alice, conversation, "And in the fridge?")

    [prompt] = chat_api.rewrite.prompts  # the first question is used as asked
    assert "How long can cooked rice stay out?" in prompt and COLD_CLAIM["text"] in prompt
    standalone = chat_api.rewrite.text
    assert [call[0] for call in chat_api.retriever.calls] == [
        "How long can cooked rice stay out?",
        standalone,
    ]
    assert chat_api.model.questions[-1] == standalone
    # Two answers and one rewrite, each recorded for the caller.
    assert usage_by_user(app_ai, alice.tenant) == sorted(
        [("user-a", "claude-opus-5-5", 120)] * 2 + [("user-a", "claude-haiku-5-5", 12)]
    )


def test_usage_is_recorded_only_for_the_caller_of_the_internal_headers(
    chat_api: ChatApi, app_ai: Engine
) -> None:
    alice = person()
    eve = person(user="user-e")  # another tenant
    alices = chat_api.create(alice)
    chat_api.events(alice, alices)
    before = usage_by_user(app_ai, alice.tenant)

    # Eve aims at Alice's conversation, through her own establishment route or by naming Alice's
    # tenant and user in the body: nothing runs and nothing is recorded.
    assert chat_api.ask(eve, alices).status_code == 404
    response = chat_api.client.post(
        f"/establishments/{eve.establishment}/conversations/{alices}/messages",
        json={"text": "How cold?", "tenantId": str(alice.tenant), "userId": alice.user},
        headers=eve.establishment_headers,
    )
    assert response.status_code == 422
    eves = chat_api.create(eve)
    chat_api.events(eve, eves)

    assert before == [("user-a", "claude-opus-5-5", 120)]
    assert usage_by_user(app_ai, alice.tenant) == before
    assert usage_by_user(app_ai, eve.tenant) == [("user-e", "claude-opus-5-5", 120)]
    assert len(chat_api.retriever.calls) == 2  # Alice's and Eve's own questions only


@pytest.mark.parametrize(
    "body",
    [
        {},
        {"text": "   "},
        {"text": "x" * 2001},
        {"text": "How cold?", "establishmentId": "x"},
    ],
    ids=["no-text", "blank", "too-long", "extra-field"],
)
def test_a_message_carries_only_a_question(chat_api: ChatApi, body: dict) -> None:
    alice = person()
    conversation = chat_api.create(alice)

    response = chat_api.client.post(
        f"/establishments/{alice.establishment}/conversations/{conversation}/messages",
        json=body,
        headers=alice.establishment_headers,
    )

    assert response.status_code == 422
    assert chat_api.get(alice, conversation).json()["messages"] == []


def test_an_old_citation_still_opens_after_its_source_is_replaced(
    chat_api: ChatApi, corpus: Engine, app_ai: Engine
) -> None:
    v1 = manifest_source(version="2004-04-29")
    old_wording = "Food business operators shall keep chilled food at 5 degrees or below."
    ingest_source(corpus, v1, sections(v1, old_wording), FakeEmbedder())
    with app_ai.begin() as connection:  # the corpus chunk as retrieval would return it
        row = connection.execute(
            text(
                """
                SELECT c.id, c.content, c.section_path, c.language, c.jurisdiction, s.id AS source,
                       s.title, s.version, s.official_url
                FROM ai.chunks c
                JOIN ai.sources s ON (s.id, s.jurisdiction, s.language)
                                   = (c.source_id, c.jurisdiction, c.language)
                WHERE c.tenant_id IS NULL AND s.manifest_id = :manifest_id
                """
            ),
            {"manifest_id": v1["id"]},
        ).one()
    chunk = RetrievedChunk(*row, score=0.9)
    quote = "keep chilled food at 5 degrees or below"
    alice = person(jurisdiction="EU")
    conversation = chat_api.create(alice)
    chat_api.use(
        model=FakeClaims(raw_claim("Keep chilled food at 5 °C or below.", ("E1", quote))),
        retriever=FakeRetriever(chunk),
    )
    chat_api.events(alice, conversation)

    v2 = {**v1, "version": "2021-03-24"}
    ingest_source(corpus, v2, sections(v2, "New wording of the same article."), FakeEmbedder())

    with app_ai.begin() as connection:
        gone = text("SELECT count(*) FROM ai.chunks WHERE id = :id")
        assert connection.execute(gone, {"id": chunk.chunk_id}).scalar_one() == 0
    [[citation]] = [
        claim["citations"]
        for claim in chat_api.get(alice, conversation).json()["messages"][1]["claims"]
    ]
    assert citation["text"] == old_wording
    assert citation["text"][citation["quoteStart"] : citation["quoteEnd"]] == quote
    assert (citation["sourceVersion"], citation["sectionPath"]) == (
        "2004-04-29",
        "Article 1",
    )
    assert citation["sourceTitle"] == v1["title"]
    assert citation["officialUrl"] == v1["official_url"]


def test_an_answer_is_stored_only_in_the_users_own_conversation(app_ai: Engine) -> None:
    alice = person()
    with tenant_transaction(app_ai, alice.tenant) as connection:
        conversation = conversation_store.create_conversation(
            connection,
            tenant_id=alice.tenant,
            user_id=alice.user,
            establishment_id=alice.establishment,
            jurisdiction=alice.jurisdiction,
        )
        # Another user of the same tenant: RLS lets the row through, the user filter does not.
        conversation_store.store_answer(
            connection,
            UUID(str(conversation["id"])),
            user_id="user-b",
            kind="unavailable",
            reason=None,
            language="en",
            claims=[],
        )
        stored = conversation_store.get_conversation(
            connection, alice.user, UUID(str(conversation["id"]))
        )

    assert stored is not None and stored["messages"] == []


def test_an_answer_cut_short_by_the_client_is_stored_with_its_usage(
    app_ai: Engine,
) -> None:
    alice = person()
    ctx = EstablishmentContext(alice.tenant, alice.user, alice.establishment, alice.jurisdiction)
    with tenant_transaction(app_ai, alice.tenant) as connection:
        conversation = UUID(
            str(
                conversation_store.create_conversation(
                    connection,
                    tenant_id=alice.tenant,
                    user_id=alice.user,
                    establishment_id=alice.establishment,
                    jurisdiction=alice.jurisdiction,
                )["id"]
            )
        )
        conversation_store.start_turn(
            connection,
            conversation,
            user_id=alice.user,
            establishment_id=alice.establishment,
            jurisdiction=alice.jurisdiction,
            question="How cold?",
            history_messages=4,
        )
    chat = Chat(
        FakeClaims(COLD_CLAIM, hang=True),
        FakeRewrite(),
        CHAT_SETTINGS,
        HISTORY_SETTINGS,
    )
    seen: list[ServerSentEvent] = []

    async def client_goes_away() -> None:
        # The SSE route iterates answer_turn in a task that is cancelled when the client leaves.
        events = answer_turn(
            OpenTurn(conversation, "How cold?", []),
            ctx,
            engine=app_ai,
            chat=chat,
            retrieve=FakeRetriever(COLD),
        )

        async def stream() -> None:
            async for event in events:
                seen.append(event)

        async with anyio.create_task_group() as group:
            group.start_soon(stream)
            with anyio.fail_after(10):
                while not seen:
                    await anyio.sleep(0.01)
            group.cancel_scope.cancel()

    anyio.run(client_goes_away)

    assert [event.event for event in seen] == ["claim"]
    with tenant_transaction(app_ai, alice.tenant) as connection:
        stored = conversation_store.get_conversation(connection, alice.user, conversation)
    assert stored is not None
    answer_ = stored["messages"][-1]
    assert (answer_["kind"], answer_["claims"]) == ("answer", [seen[0].data])
    # The call was cut short: its input tokens are recorded, output tokens are not known yet.
    assert usage_by_user(app_ai, alice.tenant) == [("user-a", "claude-opus-5-5", 1)]
