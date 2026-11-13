"""The re-ranker interface: retrieval scores (question, chunk) pairs through it, never a model SDK.

The local adapter runs bge-reranker-v2-m3 (multilingual) on CPU with sentence-transformers.
"""

from collections.abc import Sequence
from functools import cached_property
from typing import TYPE_CHECKING, Protocol

if TYPE_CHECKING:
    from sentence_transformers import CrossEncoder


class Reranker(Protocol):
    name: str

    def score(self, query: str, texts: Sequence[str]) -> list[float]:
        """One relevance score per text, in order: higher is more relevant."""
        ...


class CrossEncoderReranker:
    """A local cross-encoder on CPU. Scores are the model's sigmoid output, between 0 and 1.

    Like the embedder, the weights come from the Hugging Face Hub cache and load on first use.
    """

    def __init__(self, model: str, revision: str, max_length: int, batch_size: int = 8) -> None:
        self.model = model
        self.revision = revision
        self.max_length = max_length
        self.batch_size = batch_size
        self.name = f"{model}@{revision}"

    @cached_property
    def _model(self) -> "CrossEncoder":
        from sentence_transformers import CrossEncoder

        return CrossEncoder(
            self.model, revision=self.revision, device="cpu", max_length=self.max_length
        )

    def score(self, query: str, texts: Sequence[str]) -> list[float]:
        if not texts:
            return []
        pairs = [(query, text) for text in texts]
        return self._model.predict(pairs, batch_size=self.batch_size).tolist()
