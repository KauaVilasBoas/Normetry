"""The local models load once per process, even when the first questions arrive together."""

import threading
import time
from concurrent.futures import ThreadPoolExecutor

import pytest
import sentence_transformers

from normetry_ai.llm.embeddings import SentenceTransformerEmbedder
from normetry_ai.llm.reranker import CrossEncoderReranker


class _SlowModel:
    loads = 0
    _count_lock = threading.Lock()

    def __init__(self, *args: object, **kwargs: object) -> None:
        with _SlowModel._count_lock:
            _SlowModel.loads += 1
        time.sleep(0.05)  # long enough for the other threads to reach the load

    def encode_query(self, text: str, **kwargs: object) -> "_Vector":
        return _Vector()

    def predict(self, pairs: list[tuple[str, str]], **kwargs: object) -> "_Vector":
        return _Vector()


class _Vector:
    def tolist(self) -> list[float]:
        return [0.0]


@pytest.mark.parametrize(
    ("attribute", "build", "call"),
    [
        (
            "SentenceTransformer",
            lambda: SentenceTransformerEmbedder("model", "rev"),
            lambda m: m.embed_query("q"),
        ),
        (
            "CrossEncoder",
            lambda: CrossEncoderReranker("model", "rev", max_length=512),
            lambda m: m.score("q", ["text"]),
        ),
    ],
)
def test_concurrent_first_calls_load_the_model_once(monkeypatch, attribute, build, call) -> None:
    monkeypatch.setattr(sentence_transformers, attribute, _SlowModel)
    _SlowModel.loads = 0
    adapter = build()

    with ThreadPoolExecutor(max_workers=4) as pool:
        list(pool.map(lambda _: call(adapter), range(4)))

    assert _SlowModel.loads == 1
