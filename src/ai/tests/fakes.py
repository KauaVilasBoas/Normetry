"""Offline stand-ins for the blob container and the embedding model."""

import hashlib
from collections.abc import Callable, Sequence
from dataclasses import dataclass
from types import SimpleNamespace

from azure.core.exceptions import ResourceNotFoundError


class FakeContainer:
    """The subset of azure.storage.blob.ContainerClient that BlobStore uses, over a dict."""

    def __init__(self, blobs: dict[str, bytes] | None = None) -> None:
        self.blobs = dict(blobs or {})
        self.requested: list[str] = []
        self.downloaded: list[str] = []
        self.down = False

    def get_container_properties(self) -> SimpleNamespace:
        if self.down:
            raise ConnectionError("blob storage unreachable")
        return SimpleNamespace()

    def get_blob_client(self, path: str) -> "FakeBlob":
        self.requested.append(path)
        return FakeBlob(self, path)


@dataclass
class FakeBlob:
    container: FakeContainer
    path: str

    def _data(self) -> bytes:
        if self.path not in self.container.blobs:
            raise ResourceNotFoundError("The specified blob does not exist.")
        return self.container.blobs[self.path]

    def get_blob_properties(self) -> SimpleNamespace:
        return SimpleNamespace(size=len(self._data()))

    def download_blob(self) -> SimpleNamespace:
        data = self._data()
        self.container.downloaded.append(self.path)
        return SimpleNamespace(readall=lambda: data)


class FakeEmbedder:
    """Deterministic unit-free vectors from each text's hash; records what it embeds and can run
    a callback while "embedding", to put another message in flight at that moment."""

    name = "fake-embedder"

    def __init__(self) -> None:
        self.embedded: list[str] = []
        self.during: Callable[[], None] | None = None

    def embed_documents(self, texts: Sequence[str]) -> list[list[float]]:
        self.embedded += texts
        if self.during is not None:
            during, self.during = self.during, None
            during()
        # 32 bytes, each in (0, 1], repeated to 1024 dimensions: never the zero vector.
        return [[(b + 1) / 256 for b in hashlib.sha256(t.encode()).digest()] * 32 for t in texts]

    def embed_query(self, text: str) -> list[float]:
        return self.embed_documents([text])[0]
