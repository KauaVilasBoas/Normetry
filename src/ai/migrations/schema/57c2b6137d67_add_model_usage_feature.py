"""add model usage feature

What made each model call: the chat answer, the follow-up rewrite or an inspection's judge, so an
inspection's spend can be told apart from the chat's. Nullable: rows from before have none. Grants
and the tenant policies are unchanged (app_ai's SELECT and INSERT cover the new column).

Revision ID: 57c2b6137d67
Revises: a67eab9b29e6
Create Date: 2026-11-17 14:23:10

"""

from collections.abc import Sequence

from alembic import op

# revision identifiers, used by Alembic.
revision: str = "57c2b6137d67"
down_revision: str | Sequence[str] | None = "a67eab9b29e6"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    # The values of llm/usage.py UsageFeature; a new feature adds its value here.
    op.execute(
        """
        ALTER TABLE ai.model_usage
            ADD COLUMN feature text
                CONSTRAINT model_usage_feature_check
                CHECK (feature IN ('chat', 'rewrite', 'inspection'))
        """
    )


def downgrade() -> None:
    op.execute("ALTER TABLE ai.model_usage DROP COLUMN feature")
