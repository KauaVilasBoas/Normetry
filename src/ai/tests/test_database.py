"""Integration tests against a real PostgreSQL: the shared bootstrap, the ai migrations and RLS.

The container and the role engines are the session fixtures of conftest.py. Assertions about app
behavior connect as the real roles, never as the superuser.
"""

import hashlib
import os
from collections.abc import Iterator, Sequence
from contextlib import AbstractContextManager, contextmanager
from dataclasses import dataclass
from uuid import UUID, uuid4

import pytest
from conftest import ROLES, TOKEN, engine_for, migrate
from fastapi.testclient import TestClient
from sqlalchemy import Connection, Engine, create_engine, text
from sqlalchemy.exc import DBAPIError

from normetry_ai.api import create_app
from normetry_ai.api.auth import INTERNAL_TOKEN_HEADER
from normetry_ai.db import check_runtime_role, create_db_engine, tenant_transaction
from normetry_ai.ingestion.chunking import chunk_sections, content_hash
from normetry_ai.ingestion.corpus import Section, load_manifest, parse_source
from normetry_ai.ingestion.ingest import ingest_source
from normetry_ai.settings import Settings

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

    assert {"chunks", "sources"} <= forced.keys()  # the check is not vacuous
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
    tables = text(
        "SELECT to_regclass('ai.chunks') IS NOT NULL, to_regclass('ai.sources') IS NOT NULL"
    )

    migrate(urls["ai_owner"], "downgrade", "base")
    with owner.begin() as connection:
        assert tuple(connection.execute(tables).one()) == (False, False)

    migrate(urls["ai_owner"], "upgrade", "head")
    with owner.begin() as connection:
        assert tuple(connection.execute(tables).one()) == (True, True)


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
