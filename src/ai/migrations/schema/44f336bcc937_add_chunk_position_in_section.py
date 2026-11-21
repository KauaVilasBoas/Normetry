"""add chunk position in section

A section longer than CHUNK_LIMIT is cut into several chunks, and the judge reads a section's
chunks together; chunk ids are random, so they cannot keep the paragraphs in order. Both
ingestions write each chunk's position within its section (0, 1, ...). Rows stored before this
revision get 0 and keep the id order until re-ingested: corpus-ingest rewrites a source whose
stored positions differ from the parsed ones. Grants and policies are unchanged (table-level
INSERT covers the new column).

Revision ID: 44f336bcc937
Revises: 002dc9439046
Create Date: 2026-11-21 18:31:14.652948

"""

from collections.abc import Sequence

from alembic import op

# revision identifiers, used by Alembic.
revision: str = "44f336bcc937"
down_revision: str | Sequence[str] | None = "002dc9439046"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.execute(
        """
        ALTER TABLE ai.chunks
            ADD COLUMN position_in_section int NOT NULL DEFAULT 0
                CONSTRAINT chunks_position_in_section_check CHECK (position_in_section >= 0)
        """
    )


def downgrade() -> None:
    op.execute("ALTER TABLE ai.chunks DROP COLUMN position_in_section")
