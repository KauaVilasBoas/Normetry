"""add model usage

ai.model_usage keeps the tokens and cost of every model call, per tenant and user: the start of
usage and budget accounting. It is tenant data, with RLS forced (the `rls-table` skill). Rows are
append-only: app_ai inserts and reads its own tenant's rows and can never change or delete one.
No index yet: nothing reads the table; the budget checks that will read it add one leading with
tenant_id.

Revision ID: c62f2de325dc
Revises: 5c1e9dc10c8a
Create Date: 2026-11-13 12:29:55.189568

"""

from collections.abc import Sequence

from alembic import op

# revision identifiers, used by Alembic.
revision: str = "c62f2de325dc"
down_revision: str | Sequence[str] | None = "5c1e9dc10c8a"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    # user_id is the Keycloak `sub` from X-Normetry-User-Id. cost_usd is unconstrained numeric, so
    # a call that costs a fraction of a micro-dollar is stored exactly.
    op.execute(
        """
        CREATE TABLE ai.model_usage (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            tenant_id uuid NOT NULL,
            user_id text NOT NULL,
            model text NOT NULL,
            input_tokens int NOT NULL,
            output_tokens int NOT NULL,
            cache_creation_input_tokens int NOT NULL,
            cache_read_input_tokens int NOT NULL,
            cost_usd numeric NOT NULL,
            created_at timestamptz NOT NULL DEFAULT now()
        )
        """
    )
    op.execute("ALTER TABLE ai.model_usage ENABLE ROW LEVEL SECURITY")
    op.execute("ALTER TABLE ai.model_usage FORCE ROW LEVEL SECURITY")
    # With no tenant set the subquery is NULL: no row is visible and every insert is rejected.
    op.execute(
        """
        CREATE POLICY model_usage_read ON ai.model_usage FOR SELECT TO app_ai
            USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid))
        """
    )
    op.execute(
        """
        CREATE POLICY model_usage_insert ON ai.model_usage FOR INSERT TO app_ai
            WITH CHECK (
                tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
            )
        """
    )
    # app_ai got SELECT, INSERT, UPDATE, DELETE from the bootstrap's default privileges. Usage is
    # a record of what was spent, so UPDATE and DELETE go, and neither has a policy.
    op.execute("REVOKE UPDATE, DELETE ON ai.model_usage FROM app_ai")


def downgrade() -> None:
    # Dropping the table drops its policies and grants.
    op.execute("DROP TABLE ai.model_usage")
