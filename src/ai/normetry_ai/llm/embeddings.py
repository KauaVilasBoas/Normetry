"""The embedding interface (ADR 008): ingestion and retrieval embed through it, never a model SDK.

Each model, local or hosted, is an adapter. Chunks and queries must come from the same model, and
`ai.chunks.embedding` is `vector(1024)`, the dimension of BGE-M3.
"""

import threading
from collections.abc import Sequence
from typing import TYPE_CHECKING, Protocol

if TYPE_CHECKING:
    from sentence_transformers import SentenceTransformer


class Embedder(Protocol):
    # Stored with every chunk: ingestion re-embeds a source whose chunks came from another model.
    name: str

    def embed_documents(self, texts: Sequence[str]) -> list[list[float]]:
        """One unit-length vector per text, in order."""
        ...

    def embed_query(self, text: str) -> list[float]:
        """The unit-length vector of a search query (some models embed queries differently)."""
        ...


class SentenceTransformerEmbedder:
    """A local model run on CPU with sentence-transformers: BGE-M3 by default (settings).

    The weights come from the Hugging Face Hub on first use and stay in its cache (`HF_HOME`,
    by default ~/.cache/huggingface). The model loads on the first call, so a run with nothing to
    embed never pays for it. Query and document prompts are the model's own (BGE-M3 has none).
    """

    def __init__(self, model: str, revision: str, batch_size: int = 16) -> None:
        self.model = model
        self.revision = revision
        self.batch_size = batch_size
        self.name = f"{model}@{revision}"
        self._loaded: SentenceTransformer | None = None
        # Two first questions at once would otherwise load the weights (gigabytes) twice.
        self._load_lock = threading.Lock()

    @property
    def _model(self) -> "SentenceTransformer":
        with self._load_lock:
            if self._loaded is None:
                # Imported here: torch takes seconds to import, and a run may embed nothing.
                from sentence_transformers import SentenceTransformer

                self._loaded = SentenceTransformer(self.model, revision=self.revision, device="cpu")
            return self._loaded

    def embed_documents(self, texts: Sequence[str]) -> list[list[float]]:
        vectors = self._model.encode_document(
            list(texts), batch_size=self.batch_size, normalize_embeddings=True
        )
        return vectors.tolist()

    def embed_query(self, text: str) -> list[float]:
        return self._model.encode_query(text, normalize_embeddings=True).tolist()
