"""The real embedding model: BGE-M3 on CPU, through the embedding interface.

Marked `model` and left out of a plain `uv run pytest` (pyproject.toml): the first run downloads
about 2.3 GB into the Hugging Face cache, and loading takes seconds. Run it with
`uv run pytest -m model`; CI runs it in its own job, with that cache restored.
"""

import math

import pytest

from normetry_ai.llm.embeddings import SentenceTransformerEmbedder
from normetry_ai.settings import EmbeddingSettings

pytestmark = pytest.mark.model


def test_bge_m3_puts_both_languages_of_a_provision_close_together() -> None:
    settings = EmbeddingSettings(_env_file=None)
    embedder = SentenceTransformerEmbedder(
        settings.embedding_model, settings.embedding_model_revision
    )

    en, pt, unrelated = embedder.embed_documents(
        [
            "Food premises are to be kept clean and maintained in good repair and condition.",
            "As instalações do sector alimentar devem ser mantidas limpas e em boas condições.",
            "The board approved the annual financial statements of the company.",
        ]
    )

    def cosine(a: list[float], b: list[float]) -> float:
        return sum(x * y for x, y in zip(a, b, strict=True))

    assert len(en) == 1024  # ai.chunks.embedding is vector(1024)
    assert math.isclose(cosine(en, en), 1, abs_tol=1e-4)  # unit length
    # Cross-language retrieval (ADR 008) needs a provision closer to its translation than to
    # unrelated text.
    assert cosine(en, pt) > cosine(en, unrelated) + 0.2


def test_bge_m3_embeds_a_query_like_a_document() -> None:
    # BGE-M3 has no query prompt, so questions and the stored chunks share one embedding space.
    settings = EmbeddingSettings(_env_file=None)
    embedder = SentenceTransformerEmbedder(
        settings.embedding_model, settings.embedding_model_revision
    )
    question = "Can we keep the bleach in the kitchen?"

    query, document = embedder.embed_query(question), embedder.embed_documents([question])[0]

    assert sum(x * y for x, y in zip(query, document, strict=True)) > 0.9999
