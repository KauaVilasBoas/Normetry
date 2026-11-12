"""revoke app_ai on alembic_version

The bootstrap's default privileges give app_ai DML on every table ai_owner creates, and Alembic
creates ai.alembic_version (as ai_owner) in the same transaction, just before this revision runs.
The app role never needs the migration history.

Revision ID: 529fb3d627af
Revises:
Create Date: 2026-11-12 08:38:56.991422

"""

from collections.abc import Sequence

from alembic import op

# revision identifiers, used by Alembic.
revision: str = "529fb3d627af"
down_revision: str | Sequence[str] | None = None
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.execute("REVOKE ALL ON ai.alembic_version FROM app_ai")


def downgrade() -> None:
    # Deliberately not re-granted: the table outlives a downgrade to base, and app_ai must not
    # rewrite the migration history.
    pass
