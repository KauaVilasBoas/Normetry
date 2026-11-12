"""create chunks

ai.chunks holds the public regulation corpus (tenant_id IS NULL) and tenant documents, with RLS
forced so even the owner sees no rows without a policy. See the `rls-table` skill.

Revision ID: 54bd0cc782b6
Revises: 529fb3d627af
Create Date: 2026-11-12 08:38:59.262000

"""

from collections.abc import Sequence

from alembic import op

# revision identifiers, used by Alembic.
revision: str = "54bd0cc782b6"
down_revision: str | Sequence[str] | None = "529fb3d627af"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    # The vector extension lives in schema public, hence the qualified type and operator class.
    # ponytail: gen_random_uuid() is UUIDv4; switch the default to uuidv7() on PostgreSQL 18.
    op.execute(
        """
        CREATE TABLE ai.chunks (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            tenant_id uuid,
            source_type text NOT NULL
                CONSTRAINT chunks_source_type_check
                CHECK (source_type IN ('regulation', 'tenant_document')),
            source_id uuid NOT NULL,
            source_version int NOT NULL,
            section_path text,
            language text NOT NULL
                CONSTRAINT chunks_language_check CHECK (language IN ('pt', 'en')),
            content text NOT NULL,
            content_hash text NOT NULL,
            embedding public.vector(1024),
            tsv tsvector GENERATED ALWAYS AS (
                to_tsvector(
                    CASE language
                        WHEN 'pt' THEN 'portuguese'::regconfig
                        WHEN 'en' THEN 'english'::regconfig
                    END,
                    content
                )
            ) STORED,
            metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
            -- A null tenant marks the public corpus, which holds regulations and nothing else.
            CONSTRAINT chunks_corpus_is_regulation_check
                CHECK ((source_type = 'regulation') = (tenant_id IS NULL)),
            -- Idempotent upserts (INSERT ... ON CONFLICT DO NOTHING) by content hash per source
            -- version. tenant_id comes first so a conflict never involves another tenant's rows,
            -- and the index also serves source lookups such as replacing a document's chunks.
            -- NULLS NOT DISTINCT makes corpus rows (null tenant) collide too.
            CONSTRAINT chunks_source_content_key UNIQUE NULLS NOT DISTINCT
                (tenant_id, source_id, source_version, content_hash)
        )
        """
    )
    op.execute(
        "CREATE INDEX chunks_embedding_idx ON ai.chunks"
        " USING hnsw (embedding public.vector_cosine_ops)"
    )
    op.execute("CREATE INDEX chunks_tsv_idx ON ai.chunks USING gin (tsv)")

    op.execute("ALTER TABLE ai.chunks ENABLE ROW LEVEL SECURITY")
    op.execute("ALTER TABLE ai.chunks FORCE ROW LEVEL SECURITY")
    # The tenant is wrapped in a scalar subquery so PostgreSQL evaluates it once per statement
    # (an InitPlan), not once per row. With no tenant set it is NULL: tenant rows stay hidden and
    # tenant writes are rejected. No UPDATE policy: a new version replaces chunks with delete and
    # insert.
    op.execute(
        """
        CREATE POLICY chunks_read ON ai.chunks FOR SELECT TO app_ai
            USING (
                tenant_id IS NULL
                OR tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
            )
        """
    )
    op.execute(
        """
        CREATE POLICY chunks_tenant_insert ON ai.chunks FOR INSERT TO app_ai
            WITH CHECK (
                tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
            )
        """
    )
    op.execute(
        """
        CREATE POLICY chunks_tenant_delete ON ai.chunks FOR DELETE TO app_ai
            USING (
                tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
            )
        """
    )
    op.execute(
        """
        CREATE POLICY chunks_corpus_write ON ai.chunks FOR ALL TO corpus_ingestor
            USING (tenant_id IS NULL) WITH CHECK (tenant_id IS NULL)
        """
    )

    # app_ai got SELECT, INSERT, UPDATE, DELETE from the bootstrap's default privileges; it has no
    # update policy, so UPDATE goes too. corpus_ingestor has no default privileges.
    op.execute("REVOKE UPDATE ON ai.chunks FROM app_ai")
    op.execute("GRANT SELECT, INSERT, DELETE ON ai.chunks TO corpus_ingestor")


def downgrade() -> None:
    # Dropping the table drops its indexes, policies and grants.
    op.execute("DROP TABLE ai.chunks")
