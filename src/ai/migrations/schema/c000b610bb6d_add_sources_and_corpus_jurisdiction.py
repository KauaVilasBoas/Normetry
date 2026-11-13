"""add sources and corpus jurisdiction

ai.sources registers each regulation of corpus/manifest.json; corpus chunks point at their source
and carry its jurisdiction, so retrieval filters on a column of ai.chunks, without a join.

Revision ID: c000b610bb6d
Revises: 54bd0cc782b6
Create Date: 2026-11-12 23:33:34.155415

"""

from collections.abc import Sequence

from alembic import op

# revision identifiers, used by Alembic.
revision: str = "c000b610bb6d"
down_revision: str | Sequence[str] | None = "54bd0cc782b6"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    # One row per manifest source. `version` is the manifest's label ("2021-03-24"); `revision`
    # counts the versions ingested, and the chunks of the current one carry it as source_version.
    op.execute(
        """
        CREATE TABLE ai.sources (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            manifest_id text NOT NULL CONSTRAINT sources_manifest_id_key UNIQUE,
            jurisdiction text NOT NULL
                CONSTRAINT sources_jurisdiction_check CHECK (jurisdiction IN ('BR', 'EU', 'US')),
            code text NOT NULL,
            title text NOT NULL,
            version text NOT NULL,
            revision int NOT NULL DEFAULT 1,
            language text NOT NULL
                CONSTRAINT sources_language_check CHECK (language IN ('pt', 'en')),
            official_url text NOT NULL,
            -- Target of chunks_source_fkey, which also pins a chunk's jurisdiction and language.
            CONSTRAINT sources_chunk_key UNIQUE (id, jurisdiction, language)
        )
        """
    )
    # Corpus sources have no tenant: every row is public. RLS is forced anyway, like on every ai
    # table, so the policies decide who reads and writes, and the owner sees nothing.
    op.execute("ALTER TABLE ai.sources ENABLE ROW LEVEL SECURITY")
    op.execute("ALTER TABLE ai.sources FORCE ROW LEVEL SECURITY")
    op.execute("CREATE POLICY sources_read ON ai.sources FOR SELECT TO app_ai USING (true)")
    op.execute(
        "CREATE POLICY sources_corpus_write ON ai.sources FOR ALL TO corpus_ingestor"
        " USING (true) WITH CHECK (true)"
    )
    # app_ai got DML from the bootstrap's default privileges and only reads. corpus_ingestor
    # registers sources and moves them to a new version; it never deletes one.
    op.execute("REVOKE INSERT, UPDATE, DELETE ON ai.sources FROM app_ai")
    op.execute("GRANT SELECT, INSERT, UPDATE ON ai.sources TO corpus_ingestor")

    # Set on corpus rows, null on tenant rows. The composite foreign key holds every corpus chunk to
    # a registered source with the same jurisdiction and language; with a null jurisdiction
    # (MATCH SIMPLE) it skips tenant rows, whose source_id names a tenant document.
    op.execute("ALTER TABLE ai.chunks ADD COLUMN jurisdiction text")
    op.execute(
        "ALTER TABLE ai.chunks ADD CONSTRAINT chunks_corpus_jurisdiction_check"
        " CHECK ((jurisdiction IS NOT NULL) = (tenant_id IS NULL))"
    )
    # No index on the referencing columns: sources are never deleted, and their key never changes.
    op.execute(
        "ALTER TABLE ai.chunks ADD CONSTRAINT chunks_source_fkey"
        " FOREIGN KEY (source_id, jurisdiction, language)"
        " REFERENCES ai.sources (id, jurisdiction, language)"
    )
    # Retrieval's corpus filter: tenant_id IS NULL AND jurisdiction = ANY(...) AND language = ...
    op.execute(
        "CREATE INDEX chunks_corpus_jurisdiction_idx ON ai.chunks (jurisdiction, language)"
        " WHERE tenant_id IS NULL"
    )


def downgrade() -> None:
    # Dropping the column drops its index and constraints.
    op.execute("ALTER TABLE ai.chunks DROP COLUMN jurisdiction")
    op.execute("DROP TABLE ai.sources")
