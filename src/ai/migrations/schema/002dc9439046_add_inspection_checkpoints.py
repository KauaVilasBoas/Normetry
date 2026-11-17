"""add inspection checkpoints

The simulated inspection's LangGraph checkpoints (research R1): one row per checkpoint, holding the
whole serialized graph state, and one row per pending write of a task (an evaluated item, a theme
branch's results) linked to the checkpoint it follows. `thread_id` is the inspection id. Tenant
data, with RLS forced (the `rls-table` skill). Written only by the inspection consumer, through
`inspection/checkpoints.py`, inside `tenant_transaction`.

Revision ID: 002dc9439046
Revises: 57c2b6137d67
Create Date: 2026-11-17 11:43:22.526227

"""

from collections.abc import Sequence

from alembic import op

# revision identifiers, used by Alembic.
revision: str = "002dc9439046"
down_revision: str | Sequence[str] | None = "57c2b6137d67"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None

TENANT = "(SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)"
TABLES = ("inspection_checkpoints", "inspection_checkpoint_writes")


def upgrade() -> None:
    # checkpoint_id is LangGraph's (a uuid6 string: ordered by time, so the latest is the largest).
    # type and checkpoint are the saver's serde output; metadata is plain JSON (step, source,
    # parents). No foreign key from the writes: a write never outlives its inspection's rows.
    op.execute(
        """
        CREATE TABLE ai.inspection_checkpoints (
            tenant_id uuid NOT NULL,
            thread_id uuid NOT NULL,
            checkpoint_ns text NOT NULL,
            checkpoint_id text NOT NULL,
            parent_checkpoint_id text,
            type text NOT NULL,
            checkpoint bytea NOT NULL,
            metadata jsonb NOT NULL,
            -- Leads with the tenant: every lookup is by tenant, inspection and checkpoint.
            CONSTRAINT inspection_checkpoints_pkey
                PRIMARY KEY (tenant_id, thread_id, checkpoint_ns, checkpoint_id)
        )
        """
    )
    # idx is the write's position in its task's writes, or LangGraph's negative index for a
    # special write (an error); task_path orders a checkpoint's writes as the run applied them.
    op.execute(
        """
        CREATE TABLE ai.inspection_checkpoint_writes (
            tenant_id uuid NOT NULL,
            thread_id uuid NOT NULL,
            checkpoint_ns text NOT NULL,
            checkpoint_id text NOT NULL,
            task_id text NOT NULL,
            idx int NOT NULL,
            task_path text NOT NULL,
            channel text NOT NULL,
            type text NOT NULL,
            blob bytea NOT NULL,
            CONSTRAINT inspection_checkpoint_writes_pkey
                PRIMARY KEY (tenant_id, thread_id, checkpoint_ns, checkpoint_id, task_id, idx)
        )
        """
    )
    for table in TABLES:
        op.execute(f"ALTER TABLE ai.{table} ENABLE ROW LEVEL SECURITY")
        op.execute(f"ALTER TABLE ai.{table} FORCE ROW LEVEL SECURITY")
        # With no tenant set the subquery is NULL: no row is visible and every insert is rejected.
        op.execute(
            f"CREATE POLICY {table}_read ON ai.{table} FOR SELECT TO app_ai"
            f" USING (tenant_id = {TENANT})"
        )
        op.execute(
            f"CREATE POLICY {table}_insert ON ai.{table} FOR INSERT TO app_ai"
            f" WITH CHECK (tenant_id = {TENANT})"
        )
        # app_ai got SELECT, INSERT, UPDATE, DELETE from the bootstrap's default privileges. The
        # saver only appends: a checkpoint id is new on every step, and a write already stored is
        # kept (ON CONFLICT DO NOTHING). corpus_ingestor has nothing.
        op.execute(f"REVOKE UPDATE, DELETE ON ai.{table} FROM app_ai")


def downgrade() -> None:
    # Dropping the tables drops their policies and grants.
    for table in reversed(TABLES):
        op.execute(f"DROP TABLE ai.{table}")
