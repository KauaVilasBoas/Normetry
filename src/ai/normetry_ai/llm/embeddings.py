"""The embedding interface (ADR 008): ingestion and retrieval embed through it, never a model SDK.

Each model, local or hosted, is an adapter. Chunks and queries must come from the same model, and
`ai.chunks.embedding` is `vector(1024)`, the dimension of BGE-M3.
"""

from collections.abc import Sequence
from functools import cached_property
from typing import TYPE_CHECKING, Protocol

if TYPE_CHECKING:
    from sentence_transformers import SentenceTransformer


class Embedder(Protocol):
    # Stored with every chunk: ingestion re-embeds a source whose chunks came from another model.
    name: str

    def embed_documents(self, texts: Sequence[str]) -> list[list[float]]:
        """One unit-length vector per text, in order."""
        ...


class SentenceTransformerEmbedder:
    """A local model run on CPU with sentence-transformers: BGE-M3 by default (settings).

    The weights come from the Hugging Face Hub on first use and stay in its cache (`HF_HOME`,
    by default ~/.cache/huggingface). The model loads on the first call, so a run with nothing to
    embed never pays for it.
    """

    def __init__(self, model: str, revision: str, batch_size: int = 16) -> None:
        self.model = model
        self.revision = revision
        self.batch_size = batch_size
        self.name = f"{model}@{revision}"

    @cached_property
    def _model(self) -> "SentenceTransformer":
        # Imported here: torch takes seconds to import, and the API does not embed anything yet.
        from sentence_transformers import SentenceTransformer

        return SentenceTransformer(self.model, revision=self.revision, device="cpu")

    def embed_documents(self, texts: Sequence[str]) -> list[list[float]]:
        vectors = self._model.encode(
            list(texts), batch_size=self.batch_size, normalize_embeddings=True
        )
        return vectors.tolist()
