"""add conversations and messages

ai.conversations holds each user's chat conversations, one establishment each, and ai.messages
their questions and stored answers. Both are tenant data with RLS forced (the `rls-table` skill).
RLS keeps a tenant to its own rows; the queries keep a user to their own conversations.

An assistant message keeps a copy of everything its claims cited (chunk text, quote, offsets,
section, source title and version, language, official URL) in `claims`, so an old citation still
opens after its source is replaced.

Revision ID: 613c834c7365
Revises: c62f2de325dc
Create Date: 2026-11-13 14:44:54.836893

"""

from collections.abc import Sequence

from alembic import op

# revision identifiers, used by Alembic.
revision: str = "613c834c7365"
down_revision: str | Sequence[str] | None = "c62f2de325dc"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None

TENANT = "(SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)"


def upgrade() -> None:
    # user_id is the Keycloak `sub` from X-Normetry-User-Id; establishment_id and jurisdiction
    # come from the establishment headers the core sets. A conversation stays in the jurisdiction
    # it was started in. title is the first question, null until one is asked.
    op.execute(
        """
        CREATE TABLE ai.conversations (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            tenant_id uuid NOT NULL,
            user_id text NOT NULL,
            establishment_id uuid NOT NULL,
            jurisdiction text NOT NULL
                CONSTRAINT conversations_jurisdiction_check
                CHECK (jurisdiction IN ('BR', 'EU', 'US')),
            title text,
            created_at timestamptz NOT NULL DEFAULT now(),
            updated_at timestamptz NOT NULL DEFAULT now(),
            -- Target of messages_conversation_fkey, which holds a message to its conversation's
            -- tenant.
            CONSTRAINT conversations_tenant_key UNIQUE (tenant_id, id)
        )
        """
    )
    # A user's conversations, latest first.
    op.execute(
        "CREATE INDEX conversations_user_idx"
        " ON ai.conversations (tenant_id, user_id, updated_at DESC)"
    )
    # A user message has its text; an assistant message has its kind, the refusal reason for a
    # refusal, the language of its claims and the claims with their cited copies (camelCase JSON,
    # as the chat API returns them).
    op.execute(
        """
        CREATE TABLE ai.messages (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            tenant_id uuid NOT NULL,
            conversation_id uuid NOT NULL,
            role text NOT NULL CONSTRAINT messages_role_check CHECK (role IN ('user', 'assistant')),
            text text,
            kind text
                CONSTRAINT messages_kind_check CHECK (kind IN ('answer', 'refusal', 'unavailable')),
            reason text,
            language text,
            claims jsonb NOT NULL DEFAULT '[]'
                CONSTRAINT messages_claims_check CHECK (jsonb_typeof(claims) = 'array'),
            created_at timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT messages_shape_check CHECK (
                CASE role
                    WHEN 'user' THEN text IS NOT NULL AND kind IS NULL AND reason IS NULL
                    ELSE text IS NULL AND kind IS NOT NULL
                         AND (reason IS NOT NULL) = (kind = 'refusal')
                END
            ),
            -- The tenant is part of the key: a message can only join a conversation of its own
            -- tenant. Deleting a conversation deletes its messages.
            CONSTRAINT messages_conversation_fkey FOREIGN KEY (tenant_id, conversation_id)
                REFERENCES ai.conversations (tenant_id, id) ON DELETE CASCADE
        )
        """
    )
    # A conversation's messages in order; also serves the cascade from messages_conversation_fkey.
    op.execute(
        "CREATE INDEX messages_conversation_idx"
        " ON ai.messages (tenant_id, conversation_id, created_at)"
    )

    # With no tenant set the subquery is NULL: no row is visible and every write is rejected.
    for table in ("conversations", "messages"):
        op.execute(f"ALTER TABLE ai.{table} ENABLE ROW LEVEL SECURITY")
        op.execute(f"ALTER TABLE ai.{table} FORCE ROW LEVEL SECURITY")
    op.execute(
        f"CREATE POLICY conversations_tenant ON ai.conversations FOR ALL TO app_ai"
        f" USING (tenant_id = {TENANT}) WITH CHECK (tenant_id = {TENANT})"
    )
    op.execute(
        f"CREATE POLICY messages_read ON ai.messages FOR SELECT TO app_ai"
        f" USING (tenant_id = {TENANT})"
    )
    op.execute(
        f"CREATE POLICY messages_insert ON ai.messages FOR INSERT TO app_ai"
        f" WITH CHECK (tenant_id = {TENANT})"
    )
    # app_ai got SELECT, INSERT, UPDATE, DELETE on both from the bootstrap's default privileges.
    # A message is written once and never changed; it goes only with its conversation, through the
    # cascade, which PostgreSQL runs as the table owner.
    op.execute("REVOKE UPDATE, DELETE ON ai.messages FROM app_ai")


def downgrade() -> None:
    # Dropping the tables drops their indexes, policies and grants.
    op.execute("DROP TABLE ai.messages")
    op.execute("DROP TABLE ai.conversations")
