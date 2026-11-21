"""Tenant document ingestion (`ingestion/documents.py`): the blob store's tenant check offline, and
the worker against the shared PostgreSQL container, as app_ai, with a fake blob store and
embedder. Out-of-order, duplicate and racing messages are run in the order that triggers them."""

import hashlib
import logging
from dataclasses import dataclass, field
from uuid import UUID, uuid4

import pytest
import requests
import urllib3
from azure.core.exceptions import HttpResponseError
from azure.core.pipeline.transport import RequestsTransport
from azure.storage.blob import ContainerClient
from fakes import FakeContainer, FakeEmbedder
from sample_files import LINE, make_docx, make_pdf
from sqlalchemy import Engine, text
from urllib3.util import parse_url

from normetry_ai.db import tenant_transaction
from normetry_ai.ingestion import documents
from normetry_ai.ingestion.documents import BlobStore, DocumentWorker, in_tenant
from normetry_ai.ingestion.parsers import CSV, DOCX, MAX_BYTES, PDF, TXT, Rejected
from normetry_ai.messaging.contracts import DocumentDeleted, DocumentIndexed, DocumentUploaded

integration = pytest.mark.integration

TENANT = UUID("3f0c2a9e-6b1d-4c8e-9a57-2d4e8b1c7f60")
TEXT = f"{LINE} {LINE}".encode()


def blob_path(tenant: UUID, establishment: UUID, document: UUID, version: UUID) -> str:
    return (
        f"tenants/{tenant}/establishments/{establishment}/documents/{document}"
        f"/versions/{version}/file"
    )


@pytest.mark.parametrize(
    "path",
    [
        f"tenants/{TENANT}/establishments/e/documents/d/versions/v/sop.pdf",
        f"tenants/{TENANT}/a file with spaces (1).txt",
    ],
)
def test_a_path_under_the_tenant_is_accepted(path: str) -> None:
    assert in_tenant(TENANT, path)


@pytest.mark.parametrize(
    "path",
    [
        f"tenants/{uuid4()}/establishments/e/documents/d/versions/v/sop.pdf",  # another tenant
        f"tenants/{TENANT}",
        f"tenants/{TENANT}/",
        f"tenants/{TENANT}x/sop.pdf",
        f"tenants/{str(TENANT).upper()}/sop.pdf",
        f"/tenants/{TENANT}/sop.pdf",
        f"tenants/{TENANT}/../{uuid4()}/sop.pdf",
        f"tenants/{TENANT}/./sop.pdf",
        f"tenants/{TENANT}//sop.pdf",
        f"tenants/{TENANT}/docs/",
        f"tenants/{TENANT}/..\\{uuid4()}\\sop.pdf",
        f"tenants/{TENANT}/sop.pdf\n",
        f"other/tenants/{TENANT}/sop.pdf",
    ],
)
def test_a_path_outside_the_tenant_is_refused_before_storage_is_asked(path: str) -> None:
    container = FakeContainer({path: b"x"})

    assert not in_tenant(TENANT, path)
    with pytest.raises(Rejected) as error:
        BlobStore(container).download(TENANT, path)
    assert error.value.reason == "blob_path_outside_tenant"
    assert container.requested == []


@pytest.mark.parametrize(
    "name", ["%2e%2e/%2e%2e/other/sop.pdf", "a%2fb.pdf", "sop.pdf?comp=list", "sop.pdf#x"]
)
def test_the_sdk_keeps_an_accepted_path_under_the_tenant_in_the_request_url(name: str) -> None:
    # in_tenant passes these; the SDK must quote them so no request leaves the prefix.
    path = f"tenants/{TENANT}/{name}"
    container = ContainerClient.from_connection_string("UseDevelopmentStorage=true", "documents")

    url = parse_url(container.get_blob_client(path).url)  # builds the URL, sends nothing

    assert in_tenant(TENANT, path)
    assert url.path.startswith(f"/devstoreaccount1/documents/tenants/{TENANT}/")
    assert (url.query, url.fragment) == (None, None)


class ReadOnlySas(requests.adapters.BaseAdapter):
    """Blob storage as a read-only container SAS (`sr=c`, `sp=r`) sees it: a container-level call
    is refused (403), a blob that is not there is 404. `refusing` refuses blobs too (a revoked
    credential); `requested` lists each request's method and path."""

    def __init__(self, refusing: bool = False) -> None:
        super().__init__()
        self.refusing = refusing
        self.requested: list[tuple[str, str]] = []

    def send(self, request: requests.PreparedRequest, **kwargs: object) -> requests.Response:
        path = parse_url(request.url).path or ""
        self.requested.append((request.method or "", path))
        blob = path.count("/") > 2  # /devstoreaccount1/documents/<blob>
        response = requests.Response()
        response.status_code = 404 if blob and not self.refusing else 403
        response.headers["x-ms-error-code"] = (
            "BlobNotFound" if response.status_code == 404 else "AuthorizationPermissionMismatch"
        )
        response.raw = urllib3.HTTPResponse(body=b"", status=response.status_code)
        response._content = b""
        response.request, response.url = request, request.url or ""
        return response

    def close(self) -> None:
        pass


def sas_container(adapter: ReadOnlySas) -> ContainerClient:
    session = requests.Session()
    session.mount("http://", adapter)
    return ContainerClient(
        "http://127.0.0.1:10000/devstoreaccount1",
        "documents",
        credential="sv=2024-11-04&sr=c&sp=r&sig=x",  # a made-up SAS: nothing is sent anywhere
        transport=RequestsTransport(session=session),
        retry_total=0,
    )


def test_blob_storage_answers_through_a_blob_probe_that_a_read_only_sas_may_make() -> None:
    adapter = ReadOnlySas()
    container = sas_container(adapter)

    BlobStore(container).reachable()  # the probe blob is missing: storage answered

    assert [method for method, _ in adapter.requested] == ["HEAD"]
    # What the probe used to be: a container-level call the SAS may not make.
    with pytest.raises(HttpResponseError):
        container.get_container_properties()


def test_a_refused_credential_or_an_outage_makes_blob_storage_unreachable() -> None:
    with pytest.raises(HttpResponseError):
        BlobStore(sas_container(ReadOnlySas(refusing=True))).reachable()
    down = FakeContainer()
    down.down = True
    with pytest.raises(ConnectionError):
        BlobStore(down).reachable()


def test_request_urls_with_file_names_stay_out_of_info_logs() -> None:
    policy = logging.getLogger("azure.core.pipeline.policies.http_logging_policy")
    assert not policy.isEnabledFor(logging.INFO)


def test_a_missing_or_oversized_blob_is_refused_and_a_large_one_never_downloaded() -> None:
    small, large = f"tenants/{TENANT}/small.txt", f"tenants/{TENANT}/large.txt"
    container = FakeContainer({small: b"hello", large: bytes(MAX_BYTES + 1)})
    store = BlobStore(container)

    assert store.download(TENANT, small) == b"hello"
    for path, reason in [(f"tenants/{TENANT}/gone.txt", "blob_missing"), (large, "too_large")]:
        with pytest.raises(Rejected) as error:
            store.download(TENANT, path)
        assert error.value.reason == reason
    assert container.downloaded == [small]


# --- The worker, on the database -------------------------------------------------------------


@dataclass
class Docs:
    """One worker over a fake container; uploads go to the container as the core would put them."""

    engine: Engine
    container: FakeContainer = field(default_factory=FakeContainer)
    embedder: FakeEmbedder = field(default_factory=FakeEmbedder)
    started: list[int] = field(default_factory=list)

    @property
    def worker(self) -> DocumentWorker:
        return DocumentWorker(self.engine, self.embedder, BlobStore(self.container))

    def upload(
        self,
        tenant: UUID,
        document: UUID,
        number: int,
        data: bytes = TEXT,
        content_type: str = TXT,
        establishment: UUID | None = None,
    ) -> DocumentUploaded:
        establishment = establishment or uuid4()
        version = uuid4()
        path = blob_path(tenant, establishment, document, version)
        self.container.blobs[path] = data
        return DocumentUploaded(
            establishment_id=establishment,
            document_id=document,
            version_id=version,
            version_number=number,
            blob_path=path,
            content_type=content_type,
            size=len(data),
            sha256=hashlib.sha256(data).hexdigest(),
        )

    def ingest(self, tenant: UUID, message: DocumentUploaded) -> DocumentIndexed | None:
        return self.worker.ingest(
            tenant, message, lambda: self.started.append(message.version_number)
        )

    def delete(self, tenant: UUID, document: UUID) -> None:
        self.worker.delete(tenant, DocumentDeleted(establishment_id=uuid4(), document_id=document))

    def chunks(self, tenant: UUID, document: UUID) -> list[tuple]:
        """(version, section path, content) of the document's chunks visible to `tenant`."""
        query = text(
            """
            SELECT source_version, section_path, content FROM ai.chunks
            WHERE source_id = :document ORDER BY source_version, section_path, content
            """
        )
        with tenant_transaction(self.engine, tenant) as connection:
            return [tuple(row) for row in connection.execute(query, {"document": document})]

    def row(self, tenant: UUID, document: UUID) -> tuple | None:
        query = text(
            "SELECT indexed_version, deleted_at IS NOT NULL FROM ai.tenant_documents"
            " WHERE document_id = :document"
        )
        with tenant_transaction(self.engine, tenant) as connection:
            row = connection.execute(query, {"document": document}).one_or_none()
            return None if row is None else tuple(row)


@pytest.fixture
def docs(app_ai: Engine) -> Docs:
    return Docs(app_ai)


@integration
def test_the_worker_is_reachable_only_while_the_database_and_blob_storage_answer(
    docs: Docs,
) -> None:
    assert docs.worker.reachable()
    docs.container.down = True
    assert not docs.worker.reachable()


@integration
@pytest.mark.parametrize(
    ("data", "content_type", "pages", "paths"),
    [
        (TEXT, TXT, None, [None]),
        (make_pdf([[LINE, LINE], [LINE]]), PDF, 2, ["Page 1", "Page 2"]),
        (make_docx([("h1", "Cold holding"), ("p", LINE)]), DOCX, None, ["Cold holding"]),
        (f"Date,Fridge\n2026-11-16,{LINE}\n".encode(), CSV, None, ["Row 1"]),
    ],
    ids=["txt", "pdf", "docx", "csv"],
)
def test_a_version_is_stored_as_tenant_chunks_with_its_result(
    docs: Docs, data: bytes, content_type: str, pages: int | None, paths: list[str | None]
) -> None:
    tenant, document = uuid4(), uuid4()
    message = docs.upload(tenant, document, 1, data, content_type)

    result = docs.ingest(tenant, message)

    assert result == DocumentIndexed(
        establishment_id=message.establishment_id,
        document_id=document,
        version_id=message.version_id,
        version_number=1,
        chunk_count=len(paths),
        page_count=pages,
        language="en",
    )
    assert docs.started == [1]
    assert [path for _, path, _ in docs.chunks(tenant, document)] == paths
    assert docs.row(tenant, document) == (1, False)
    stored = text(
        """
        SELECT DISTINCT tenant_id, source_type, jurisdiction, language, metadata,
               embedding IS NOT NULL, tsv <> ''::tsvector
        FROM ai.chunks WHERE source_id = :document
        """
    )
    with tenant_transaction(docs.engine, tenant) as connection:
        assert connection.execute(stored, {"document": document}).all() == [
            (
                tenant,
                "tenant_document",
                None,
                "en",
                {"embedding_model": "fake-embedder"},
                True,
                True,
            )
        ]


@integration
def test_a_long_section_keeps_its_chunks_positions_in_text_order(docs: Docs) -> None:
    tenant, document = uuid4(), uuid4()
    body = " ".join(f"Reading {n} was taken at the walk-in cooler." for n in range(80))
    docs.ingest(tenant, docs.upload(tenant, document, 1, body.encode(), TXT))
    query = text(
        "SELECT position_in_section, content FROM ai.chunks WHERE source_id = :document"
        " ORDER BY position_in_section"
    )
    with tenant_transaction(docs.engine, tenant) as connection:
        rows = connection.execute(query, {"document": document}).all()

    assert len(rows) > 1
    assert [position for position, _ in rows] == list(range(len(rows)))
    assert " ".join(content for _, content in rows) == body


@integration
def test_a_new_version_replaces_the_previous_versions_chunks(docs: Docs) -> None:
    tenant, document = uuid4(), uuid4()
    docs.ingest(tenant, docs.upload(tenant, document, 1, b"Version one of the cold holding SOP."))
    docs.ingest(tenant, docs.upload(tenant, document, 2, b"Version two of the cold holding SOP."))

    assert docs.chunks(tenant, document) == [(2, None, "Version two of the cold holding SOP.")]
    assert docs.row(tenant, document) == (2, False)


@integration
def test_an_older_version_arriving_late_is_dropped_without_any_work(docs: Docs) -> None:
    tenant, document = uuid4(), uuid4()
    old = docs.upload(tenant, document, 1, b"Version one.")
    docs.ingest(tenant, docs.upload(tenant, document, 2, b"Version two."))
    embedded = len(docs.embedder.embedded)

    assert docs.ingest(tenant, old) is None
    assert docs.started == [2]  # no DocumentIngestionStarted for the dropped version
    assert len(docs.embedder.embedded) == embedded
    assert old.blob_path not in docs.container.downloaded
    assert docs.chunks(tenant, document) == [(2, None, "Version two.")]


@integration
def test_a_redelivered_version_is_not_redone_and_reports_the_same_result(docs: Docs) -> None:
    tenant, document = uuid4(), uuid4()
    message = docs.upload(tenant, document, 1)
    first = docs.ingest(tenant, message)
    rows = text("SELECT id, xmin::text FROM ai.chunks WHERE source_id = :document")
    with tenant_transaction(docs.engine, tenant) as connection:
        before = connection.execute(rows, {"document": document}).all()
    embedded = len(docs.embedder.embedded)

    # Published again, so a DocumentIndexed lost after the commit still reaches the core.
    assert docs.ingest(tenant, message) == first
    assert docs.started == [1]
    assert len(docs.embedder.embedded) == embedded
    with tenant_transaction(docs.engine, tenant) as connection:
        assert connection.execute(rows, {"document": document}).all() == before


@integration
def test_a_delete_while_a_version_is_in_flight_stores_nothing(docs: Docs) -> None:
    tenant, document = uuid4(), uuid4()
    docs.ingest(tenant, docs.upload(tenant, document, 1, b"Version one."))
    # The delete commits while version 2 is being embedded, outside its transaction.
    docs.embedder.during = lambda: docs.delete(tenant, document)

    assert docs.ingest(tenant, docs.upload(tenant, document, 2, b"Version two.")) is None
    assert docs.chunks(tenant, document) == []
    assert docs.row(tenant, document) == (1, True)


@integration
def test_a_delete_delivered_before_the_upload_drops_the_upload(docs: Docs) -> None:
    tenant, document = uuid4(), uuid4()
    docs.delete(tenant, document)

    assert docs.ingest(tenant, docs.upload(tenant, document, 1)) is None
    assert docs.started == []
    assert docs.chunks(tenant, document) == []
    assert docs.row(tenant, document) == (None, True)


@integration
def test_a_delete_removes_the_chunks_and_a_redelivered_delete_changes_nothing(docs: Docs) -> None:
    tenant, document = uuid4(), uuid4()
    docs.ingest(tenant, docs.upload(tenant, document, 1))
    docs.delete(tenant, document)
    deleted_at = text("SELECT deleted_at FROM ai.tenant_documents WHERE document_id = :document")
    with tenant_transaction(docs.engine, tenant) as connection:
        first = connection.execute(deleted_at, {"document": document}).scalar_one()

    docs.delete(tenant, document)

    assert docs.chunks(tenant, document) == []
    with tenant_transaction(docs.engine, tenant) as connection:
        assert connection.execute(deleted_at, {"document": document}).scalar_one() == first


@integration
def test_a_newer_version_stored_first_wins_the_race(docs: Docs) -> None:
    tenant, document = uuid4(), uuid4()
    v1, v2 = docs.upload(tenant, document, 1, b"Version one."), docs.upload(tenant, document, 2)
    # Version 2 is stored while version 1 is being embedded.
    docs.embedder.during = lambda: docs.ingest(tenant, v2)

    assert docs.ingest(tenant, v1) is None
    assert [version for version, _, _ in docs.chunks(tenant, document)] == [2]
    assert docs.row(tenant, document) == (2, False)


@integration
@pytest.mark.parametrize(
    ("change", "reason"),
    [
        ({"blob_path": "tenants/elsewhere/file"}, "blob_path_outside_tenant"),
        ({"sha256": "0" * 64}, "internal"),
        ({"content_type": "image/png"}, "unsupported_format"),
    ],
    ids=["outside-tenant", "checksum", "format"],
)
def test_a_failed_version_leaves_the_previous_one_current(
    docs: Docs, change: dict, reason: str
) -> None:
    tenant, document = uuid4(), uuid4()
    docs.ingest(tenant, docs.upload(tenant, document, 1, b"Version one."))
    message = docs.upload(tenant, document, 2).model_copy(update=change)

    with pytest.raises(Rejected) as error:
        docs.ingest(tenant, message)

    assert error.value.reason == reason
    assert docs.started == [1, 2]  # started, then failed
    assert docs.chunks(tenant, document) == [(1, None, "Version one.")]
    assert docs.row(tenant, document) == (1, False)


@integration
def test_a_version_with_too_many_chunks_fails_before_it_is_embedded(
    docs: Docs, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setattr(documents, "MAX_CHUNKS", 2)
    tenant, document = uuid4(), uuid4()
    pages = make_pdf([[LINE] * 3] * 3)  # one chunk per page

    with pytest.raises(Rejected) as error:
        docs.ingest(tenant, docs.upload(tenant, document, 1, pages, PDF))

    assert error.value.reason == "too_large"
    assert docs.embedder.embedded == []
    assert docs.row(tenant, document) is None


@integration
def test_a_missing_blob_fails_its_version(docs: Docs) -> None:
    tenant, document = uuid4(), uuid4()
    message = docs.upload(tenant, document, 1)
    del docs.container.blobs[message.blob_path]

    with pytest.raises(Rejected) as error:
        docs.ingest(tenant, message)
    assert error.value.reason == "blob_missing"
    assert docs.row(tenant, document) is None


@integration
def test_tenants_never_see_or_touch_each_others_document_chunks(docs: Docs) -> None:
    tenant_a, tenant_b, document = uuid4(), uuid4(), uuid4()
    # The same document id in two tenants: each tenant's rows and chunks stay its own.
    docs.ingest(tenant_a, docs.upload(tenant_a, document, 1, b"Tenant A's SOP."))
    docs.ingest(tenant_b, docs.upload(tenant_b, document, 3, b"Tenant B's SOP."))

    assert docs.chunks(tenant_a, document) == [(1, None, "Tenant A's SOP.")]
    assert docs.chunks(tenant_b, document) == [(3, None, "Tenant B's SOP.")]
    # B's delete, and a B message naming A's blob, leave A as it was.
    docs.delete(tenant_b, document)
    stolen = docs.upload(tenant_a, uuid4(), 1)
    with pytest.raises(Rejected) as error:
        docs.ingest(tenant_b, stolen)
    assert error.value.reason == "blob_path_outside_tenant"
    assert stolen.blob_path not in docs.container.requested
    assert docs.chunks(tenant_a, document) == [(1, None, "Tenant A's SOP.")]
    assert docs.row(tenant_a, document) == (1, False)
    assert docs.chunks(tenant_b, document) == []
    assert docs.row(tenant_b, document) == (3, True)
