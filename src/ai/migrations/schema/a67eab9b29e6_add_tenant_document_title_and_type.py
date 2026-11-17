"""add tenant document title and type

The title and type of a tenant document travel on DocumentUploaded (optional in v1), so citations
of an establishment's documents can name them. They describe the indexed version: the consumer
writes them with it. Grants and the tenant policy are unchanged (app_ai already has SELECT,
INSERT and UPDATE on the whole table).

Revision ID: a67eab9b29e6
Revises: bff4d64e7f22
Create Date: 2026-11-17 06:25:23.581419

"""

from collections.abc import Sequence

from alembic import op

# revision identifiers, used by Alembic.
revision: str = "a67eab9b29e6"
down_revision: str | Sequence[str] | None = "bff4d64e7f22"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    # Null until a version with them is indexed. Same bounds as DocumentUploaded.
    op.execute(
        """
        ALTER TABLE ai.tenant_documents
            ADD COLUMN title text
                CONSTRAINT tenant_documents_title_check
                CHECK (char_length(title) BETWEEN 1 AND 200),
            ADD COLUMN document_type text
                CONSTRAINT tenant_documents_document_type_check
                CHECK (document_type IN ('Sop', 'License', 'Log', 'Report', 'Other'))
        """
    )


def downgrade() -> None:
    op.execute("ALTER TABLE ai.tenant_documents DROP COLUMN title, DROP COLUMN document_type")
