"""The search over one establishment's documents (`retrieval/establishment.py`), on the shared
PostgreSQL container as app_ai. Documents are indexed by the real `DocumentWorker` with the fakes
of `tests/fakes.py`. Every document that must stay out holds the query word for word, with its
exact embedding, so a leak would be returned."""

import hashlib
from collections.abc import Sequence
from dataclasses import dataclass, field
from uuid import UUID, uuid4

import pytest
from fakes import FakeContainer, FakeEmbedder
from sqlalchemy import Engine, text

from normetry_ai.db import tenant_transaction
from normetry_ai.ingestion.documents import BlobStore, DocumentWorker
from normetry_ai.ingestion.parsers import TXT
from normetry_ai.messaging.contracts import DocumentDeleted, DocumentUploaded
from normetry_ai.retrieval.establishment import search_establishment
from normetry_ai.retrieval.models import RetrievedChunk
from normetry_ai.settings import RetrievalSettings

pytestmark = pytest.mark.integration

QUERY = "Keep cold food at five degrees and below in the walk in cooler."


class FakeReranker:
    def score(self, query: str, texts: Sequence[str]) -> list[float]:
        return [1.0 if text == query else 0.5 for text in texts]


@dataclass
class Documents:
    """Indexes documents as the consumer would, from DocumentUploaded messages."""

    engine: Engine
    container: FakeContainer = field(default_factory=FakeContainer)

    @property
    def worker(self) -> DocumentWorker:
        return DocumentWorker(self.engine, FakeEmbedder(), BlobStore(self.container))

    def index(
        self,
        tenant: UUID,
        establishment: UUID,
        document: UUID,
        number: int = 1,
        content: str = QUERY,
        **fields: str,
    ) -> None:
        version, data = uuid4(), content.encode()
        path = f"tenants/{tenant}/establishments/{establishment}/documents/{document}/{version}"
        self.container.blobs[path] = data
        message = DocumentUploaded(
            establishment_id=establishment,
            document_id=document,
            version_id=version,
            version_number=number,
            blob_path=path,
            content_type=TXT,
            size=len(data),
            sha256=hashlib.sha256(data).hexdigest(),
            **fields,
        )
        assert self.worker.ingest(tenant, message, lambda: None) is not None

    def stale_chunk(self, tenant: UUID, document: UUID, version: int) -> UUID:
        """A chunk the worker would have removed: the query, with its exact embedding."""
        insert = text(
            """
            INSERT INTO ai.chunks (tenant_id, source_type, source_id, source_version, language,
                                   content, content_hash, embedding)
            VALUES (:tenant, 'tenant_document', :document, :version, 'en', :content, :hash,
                    CAST(:embedding AS public.vector))
            RETURNING id
            """
        )
        with tenant_transaction(self.engine, tenant) as connection:
            return connection.execute(
                insert,
                {
                    "tenant": tenant,
                    "document": document,
                    "version": version,
                    "content": QUERY,
                    "hash": uuid4().hex,
                    "embedding": str(FakeEmbedder().embed_query(QUERY)),
                },
            ).scalar_one()


def search(engine: Engine, tenant: UUID, establishment: UUID, **knobs: int) -> list[RetrievedChunk]:
    # Room for every candidate, so a leak is never cut off by the top k.
    settings = RetrievalSettings(
        _env_file=None,
        **{
            "retrieval_establishment_rerank_candidates": 50,
            "retrieval_establishment_top_k": 50,
            **knobs,
        },
    )
    with tenant_transaction(engine, tenant) as connection:
        return search_establishment(
            connection,
            establishment,
            QUERY,
            embedder=FakeEmbedder(),
            reranker=FakeReranker(),
            settings=settings,
        )


@pytest.fixture
def documents(app_ai: Engine) -> Documents:
    return Documents(app_ai)


def test_only_the_establishments_current_documents_are_returned(documents: Documents) -> None:
    tenant, other_tenant, establishment = uuid4(), uuid4(), uuid4()
    own, deleted, superseded = uuid4(), uuid4(), uuid4()
    documents.index(tenant, establishment, own, title="Cold holding SOP", document_type="Sop")
    # Another tenant, even under the same establishment id, and another establishment of the
    # same tenant.
    documents.index(other_tenant, establishment, uuid4())
    documents.index(tenant, uuid4(), uuid4())
    # Deleted, and superseded by version 2; the worker removed their chunks, so these put stale
    # ones back to show the filters themselves keep them out.
    documents.index(tenant, establishment, deleted)
    documents.worker.delete(
        tenant, DocumentDeleted(establishment_id=establishment, document_id=deleted)
    )
    documents.index(tenant, establishment, superseded)
    documents.index(tenant, establishment, superseded, 2)
    stale = {
        documents.stale_chunk(tenant, deleted, 1),
        documents.stale_chunk(tenant, superseded, 1),
    }

    chunks = search(documents.engine, tenant, establishment)

    assert not stale & {chunk.chunk_id for chunk in chunks}
    assert sorted((chunk.document_id, chunk.document_version) for chunk in chunks) == sorted(
        [(own, 1), (superseded, 2)]
    )
    found = {chunk.document_id: chunk for chunk in chunks}
    assert found[own] == RetrievedChunk(
        chunk_id=found[own].chunk_id,
        content=QUERY,
        section_path=None,
        language="en",
        jurisdiction="",
        source_id=own,
        source_title="Cold holding SOP",
        source_version="1",
        official_url="",
        score=1.0,
        source_type="tenant_document",
        document_id=own,
        document_title="Cold holding SOP",
        document_type="Sop",
        document_version=1,
    )
    assert (found[superseded].document_title, found[superseded].source_title) == (None, "")
    assert search(documents.engine, other_tenant, establishment) != []  # its own, not ours


def test_a_new_versions_title_and_type_replace_the_old_ones_unless_it_has_none(
    documents: Documents,
) -> None:
    tenant, establishment, document = uuid4(), uuid4(), uuid4()
    documents.index(tenant, establishment, document, 1, title="Draft", document_type="Other")
    documents.index(tenant, establishment, document, 2, title="Cooling log", document_type="Log")
    documents.index(tenant, establishment, document, 3)  # an older publisher: neither field

    [chunk] = search(documents.engine, tenant, establishment)

    assert (chunk.document_title, chunk.document_type, chunk.document_version) == (
        "Cooling log",
        "Log",
        3,
    )


def test_the_full_text_search_finds_what_the_vector_search_does_not(
    documents: Documents,
) -> None:
    tenant, establishment = uuid4(), uuid4()
    exact, shared_words, unrelated = uuid4(), uuid4(), uuid4()
    documents.index(tenant, establishment, exact)
    documents.index(
        tenant, establishment, shared_words, content="The walk in cooler is checked every morning."
    )
    documents.index(tenant, establishment, unrelated, content="Hands are washed before work.")

    # The vector search keeps only the exact match; the full-text search adds the shared words.
    chunks = search(documents.engine, tenant, establishment, retrieval_vector_k=1)

    assert [chunk.document_id for chunk in chunks] == [exact, shared_words]
    assert search(documents.engine, tenant, uuid4()) == []


def test_the_search_runs_only_inside_a_tenant_transaction(app_ai: Engine) -> None:
    settings = RetrievalSettings(_env_file=None)
    with pytest.raises(RuntimeError, match="tenant_transaction"), app_ai.begin() as connection:
        search_establishment(
            connection,
            uuid4(),
            QUERY,
            embedder=FakeEmbedder(),
            reranker=FakeReranker(),
            settings=settings,
        )
