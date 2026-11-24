"""Offline stand-ins for the blob container, the embedding model, the re-ranker, the broker and
the inspection judge, and builders of the chunks retrieval returns."""

import hashlib
import re
from collections.abc import Callable, Sequence
from dataclasses import dataclass, field
from types import SimpleNamespace
from typing import Any
from uuid import UUID, uuid4
from xml.sax.saxutils import unescape

from azure.core.exceptions import ResourceNotFoundError

from normetry_ai.llm.usage import Usage
from normetry_ai.messaging.contracts import Envelope
from normetry_ai.retrieval.models import RetrievedChunk, SourceType


class FakeContainer:
    """The subset of azure.storage.blob.ContainerClient that BlobStore uses, over a dict. Like the
    service's read-only container SAS, it has no container-level call."""

    def __init__(self, blobs: dict[str, bytes] | None = None) -> None:
        self.blobs = dict(blobs or {})
        self.requested: list[str] = []
        self.downloaded: list[str] = []
        self.down = False

    def get_blob_client(self, path: str) -> "FakeBlob":
        self.requested.append(path)
        return FakeBlob(self, path)


@dataclass
class FakeBlob:
    container: FakeContainer
    path: str

    def exists(self) -> bool:
        if self.container.down:
            raise ConnectionError("blob storage unreachable")
        return self.path in self.container.blobs

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
    """Deterministic unit-free vectors from each text's hash, so a query matches its own text
    exactly; records what it embeds and can run a callback while "embedding", to put another
    message in flight at that moment. `name` is the embedding model it claims to be."""

    def __init__(self, name: str = "fake-embedder") -> None:
        self.name = name
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


class FakeReranker:
    """Scores a text equal to the query 1.0 and anything else 0.5."""

    name = "fake-reranker"

    def score(self, query: str, texts: Sequence[str]) -> list[float]:
        return [1.0 if text == query else 0.5 for text in texts]


@dataclass
class FakeBroker:
    """Records what the consumers publish; `failing` refuses every message, `refuses` one type."""

    published: list[Envelope[Any]] = field(default_factory=list)
    failing: bool = False
    refuses: str | None = None

    async def publish(self, message: Envelope[Any]) -> None:
        if self.failing or message.type == self.refuses:
            raise ConnectionError("broker gone")
        self.published.append(message)

    @property
    def sent(self) -> list[tuple[str, Any]]:
        return [(message.type, message.payload) for message in self.published]


def regulation_chunk(content: str, **fields: Any) -> RetrievedChunk:
    """A corpus chunk of FDA Food Code 3-501.16 (US, en), scored 0.9; `fields` replace any of
    its fields."""
    return RetrievedChunk(
        **{
            "chunk_id": uuid4(),
            "content": content,
            "section_path": "3-501.16",
            "language": "en",
            "jurisdiction": "US",
            "source_id": uuid4(),
            "source_title": "FDA Food Code",
            "source_version": "2026",
            "official_url": "https://example.org/food-code",
            "score": 0.9,
        }
        | fields
    )


def document_chunk(
    content: str,
    *,
    document_id: UUID | None = None,
    title: str | None = "Cold holding SOP",
    document_type: str = "Sop",
    version: int = 3,
    **fields: Any,
) -> RetrievedChunk:
    """A chunk of a tenant document's page 1 (en), scored 0.9. Its source fields repeat the
    document's, as `establishment.py` fills them; `fields` replace any other field."""
    document_id = document_id or uuid4()
    return RetrievedChunk(
        **{
            "chunk_id": uuid4(),
            "content": content,
            "section_path": "Page 1",
            "language": "en",
            "jurisdiction": "",
            "source_id": document_id,
            "source_title": title or "",
            "source_version": str(version),
            "official_url": "",
            "score": 0.9,
            "source_type": SourceType.TENANT_DOCUMENT,
            "document_id": document_id,
            "document_title": title,
            "document_type": document_type,
            "document_version": version,
        }
        | fields
    )


_TITLE = re.compile(r"<title>(.*?)</title>")


def _excerpt(user: str, element: str) -> str | None:
    """The text of the first <regulation> or <document> excerpt of a judge prompt."""
    found = re.search(rf"<{element} id=\"[RD]1\"[^>]*>\n(.*?)\n</{element}>", user, re.DOTALL)
    return None if found is None else unescape(found.group(1))


def _quote(excerpt: str) -> str:
    return " ".join(excerpt.split()[:8])


class EchoJudge:
    """A judge model that quotes its excerpts, so the real guard keeps its findings: the first
    words of R1, and of D1 unless the verdict is insufficient_evidence (the default without a
    document). `verdicts` sets the verdict of an item by its title; `fail(title)` can return an
    exception to raise instead of answering. `judged` lists every title answered."""

    def __init__(
        self,
        verdicts: dict[str, str] | None = None,
        fail: Callable[[str], Exception | None] | None = None,
    ) -> None:
        self.verdicts = verdicts or {}
        self.fail = fail
        self.judged: list[str] = []

    async def judge(self, system: str, user: str) -> tuple[Any, Usage]:
        title = unescape(_TITLE.search(user).group(1))
        if self.fail is not None and (error := self.fail(title)) is not None:
            raise error
        regulation, document = _excerpt(user, "regulation"), _excerpt(user, "document")
        verdict = self.verdicts.get(title, "compliant" if document else "insufficient_evidence")
        evidence = document is not None and verdict != "insufficient_evidence"
        self.judged.append(title)
        reply = {
            "regulation_quotes": [{"id": "R1", "quote": _quote(regulation or "")}],
            "evidence_quotes": [{"id": "D1", "quote": _quote(document)}] if evidence else [],
            "rationale": "The document shows what the rule asks for.",
            "verdict": verdict,
            "confidence": "high",
            "action": None if verdict == "compliant" else "Keep the record and show it.",
        }
        usage = Usage(model="claude-haiku-5-5", input_tokens=len(user) // 4, output_tokens=200)
        return reply, usage
