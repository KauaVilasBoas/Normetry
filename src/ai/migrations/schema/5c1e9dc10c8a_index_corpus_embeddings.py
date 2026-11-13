"""index corpus embeddings

Replaces the HNSW index over every chunk with one over the corpus only. An approximate search over
all tenants returns a candidate list that RLS filters afterwards, so the corpus or a large tenant
could crowd a small tenant out of its own results. Tenant rows get exact KNN instead, through the
btree that leads with tenant_id (src/ai/CLAUDE.md, "Retrieval under RLS").

Revision ID: 5c1e9dc10c8a
Revises: c000b610bb6d
Create Date: 2026-11-12 23:33:35.567840

"""

from collections.abc import Sequence

from alembic import op

# revision identifiers, used by Alembic.
revision: str = "5c1e9dc10c8a"
down_revision: str | Sequence[str] | None = "c000b610bb6d"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.execute("DROP INDEX ai.chunks_embedding_idx")
    # Used only by queries that say tenant_id IS NULL.
    op.execute(
        "CREATE INDEX chunks_corpus_embedding_idx ON ai.chunks"
        " USING hnsw (embedding public.vector_cosine_ops) WHERE tenant_id IS NULL"
    )


def downgrade() -> None:
    op.execute("DROP INDEX ai.chunks_corpus_embedding_idx")
    op.execute(
        "CREATE INDEX chunks_embedding_idx ON ai.chunks"
        " USING hnsw (embedding public.vector_cosine_ops)"
    )
