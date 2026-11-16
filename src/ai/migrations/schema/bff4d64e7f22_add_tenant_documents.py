"""add tenant documents

ai.tenant_documents holds one row per tenant document the ingestion consumer has heard of: its
establishment, the version whose chunks are current (with what DocumentIndexed reported for it)
and a deleted marker. The consumer locks the row for each version it stores, so a late or
out-of-order message never undoes newer state. Tenant data, with RLS forced (the `rls-table`
skill). Written only by the consumer, from queue messages.

Revision ID: bff4d64e7f22
Revises: 613c834c7365
Create Date: 2026-11-16 00:41:27.346793

"""

from collections.abc import Sequence

from alembic import op

# revision identifiers, used by Alembic.
revision: str = "bff4d64e7f22"
down_revision: str | Sequence[str] | None = "613c834c7365"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None

TENANT = "(SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)"


def upgrade() -> None:
    # document_id and establishment_id are the core's ids, from the messages. indexed_version is
    # the version number whose chunks are in ai.chunks (source_version); null until one is stored.
    # chunk_count, page_count (null for formats without pages) and language are that version's
    # DocumentIndexed fields, kept to publish it again after a redelivery. A deleted document
    # keeps its row, so a late DocumentUploaded still finds the marker.
    op.execute(
        """
        CREATE TABLE ai.tenant_documents (
            tenant_id uuid NOT NULL,
            document_id uuid NOT NULL,
            establishment_id uuid NOT NULL,
            indexed_version int
                CONSTRAINT tenant_documents_indexed_version_check CHECK (indexed_version >= 1),
            chunk_count int
                CONSTRAINT tenant_documents_chunk_count_check CHECK (chunk_count >= 1),
            page_count int
                CONSTRAINT tenant_documents_page_count_check CHECK (page_count >= 1),
            language text
                CONSTRAINT tenant_documents_language_check CHECK (language IN ('pt', 'en')),
            deleted_at timestamptz,
            updated_at timestamptz NOT NULL DEFAULT now(),
            -- Leads with the tenant: the consumer's lookups and locks are by tenant and document.
            CONSTRAINT tenant_documents_pkey PRIMARY KEY (tenant_id, document_id),
            CONSTRAINT tenant_documents_indexed_check CHECK (
                (indexed_version IS NULL) = (chunk_count IS NULL)
                AND (indexed_version IS NULL) = (language IS NULL)
            )
        )
        """
    )
    op.execute("ALTER TABLE ai.tenant_documents ENABLE ROW LEVEL SECURITY")
    op.execute("ALTER TABLE ai.tenant_documents FORCE ROW LEVEL SECURITY")
    # With no tenant set the subquery is NULL: no row is visible and every write is rejected.
    op.execute(
        f"CREATE POLICY tenant_documents_tenant ON ai.tenant_documents FOR ALL TO app_ai"
        f" USING (tenant_id = {TENANT}) WITH CHECK (tenant_id = {TENANT})"
    )
    # app_ai got SELECT, INSERT, UPDATE, DELETE from the bootstrap's default privileges. A row is
    # never deleted: a deleted document keeps its marker. corpus_ingestor has nothing.
    op.execute("REVOKE DELETE ON ai.tenant_documents FROM app_ai")


def downgrade() -> None:
    # Dropping the table drops its policy and grants.
    op.execute("DROP TABLE ai.tenant_documents")
