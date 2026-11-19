"""Index tenant document versions into ai.chunks, as app_ai (phase 2a design, "Ingestion").

The queue consumer (`messaging/consumer.py`) calls `DocumentWorker` once per message, with the
tenant of the message's envelope and nothing else. Every query runs in `tenant_transaction`.

A version is stored in one transaction that locks its document's ai.tenant_documents row first.
If the document is deleted, or a newer version is already indexed, the work is dropped; otherwise
the version's chunks go in, every other version's chunks go out, and the row records the version
as current. So redeliveries, out-of-order versions and a delete racing an ingestion all end in
the newest state. Download, parsing and embedding run before that transaction, never inside it.
"""

import hashlib
import json
import logging
import re
from collections.abc import Callable
from enum import Enum, auto
from typing import Any
from uuid import UUID

from azure.core.exceptions import ResourceNotFoundError
from azure.storage.blob import ContainerClient
from sqlalchemy import Engine, text

from normetry_ai.db import tenant_transaction
from normetry_ai.ingestion.chunking import chunk_sections
from normetry_ai.ingestion.parsers import MAX_BYTES, Rejected
from normetry_ai.ingestion.sandbox import parse_in_subprocess
from normetry_ai.llm.embeddings import Embedder
from normetry_ai.messaging.contracts import (
    DocumentDeleted,
    DocumentFailureReason,
    DocumentIndexed,
    DocumentUploaded,
)
from normetry_ai.retrieval.language import detect_language
from normetry_ai.settings import ParseSettings

logger = logging.getLogger(__name__)
# azure-core logs every request URL at INFO, and a blob URL holds the uploaded file's name.
logging.getLogger("azure.core.pipeline.policies.http_logging_policy").setLevel(logging.WARNING)

# Chunks per version. The design allows 100 pages; a dense page is about 3,000 characters, two
# chunks, so 500 leaves room and keeps embedding a version to a few minutes of CPU. A 10 MiB text
# file would otherwise take thousands of chunks, and the queue it holds up serves every tenant.
# Parsing has its own wall-clock limit (sandbox.py, NORMETRY_PARSE_TIMEOUT_SECONDS).
MAX_CHUNKS = 500

# A segment that HTTP clients would resolve ("." and ".."), an empty one, a backslash (which some
# storage clients read as "/") or a control character could move a path out of its prefix.
_UNSAFE_PATH = re.compile(r"(^|/)\.{0,2}(/|$)|[\\\x00-\x1f\x7f]")


def in_tenant(tenant_id: UUID, path: str) -> bool:
    """Whether a blob path is a file under tenants/{tenant_id}/, with no segment that escapes it."""
    prefix = f"tenants/{tenant_id}/"
    return path.startswith(prefix) and len(path) > len(prefix) and not _UNSAFE_PATH.search(path)


# A blob name no upload has (uploads live under tenants/), asked for only to see storage answer.
_PROBE = "reachable-probe"


class BlobStore:
    """The core's upload container in Azure Blob Storage (Azurite locally): read only, and only
    under the tenant of the message being handled."""

    def __init__(self, container: ContainerClient) -> None:
        self._container = container

    @classmethod
    def connect(cls, connection_string: str, container: str) -> "BlobStore":
        return cls(ContainerClient.from_connection_string(connection_string, container))

    def download(self, tenant_id: UUID, path: str) -> bytes:
        """The file at `path`. Raises Rejected when the path is outside the tenant's prefix, the
        blob is missing or it is larger than MAX_BYTES (checked before downloading)."""
        if not in_tenant(tenant_id, path):
            raise Rejected(
                DocumentFailureReason.BLOB_PATH_OUTSIDE_TENANT,
                "The file is not stored under its tenant.",
            )
        blob = self._container.get_blob_client(path)
        try:
            if blob.get_blob_properties().size > MAX_BYTES:
                raise Rejected(DocumentFailureReason.TOO_LARGE, "The file is larger than 10 MiB.")
            return blob.download_blob().readall()
        except ResourceNotFoundError as error:
            raise Rejected(
                DocumentFailureReason.BLOB_MISSING, "The uploaded file is missing from storage."
            ) from error

    def reachable(self) -> None:
        """Raises when blob storage does not answer or refuses the credential. The AI service holds
        a read-only container SAS, which may not read the container's properties (403), so this
        asks whether a probe blob exists: a missing one answers False, anything else raises."""
        self._container.get_blob_client(_PROBE).exists()


_SELECT_DOCUMENT = text(
    """
    SELECT indexed_version, chunk_count, page_count, language, deleted_at
    FROM ai.tenant_documents WHERE tenant_id = :tenant_id AND document_id = :document_id
    """
)
_LOCK_DOCUMENT = text(f"{_SELECT_DOCUMENT.text} FOR UPDATE")
# Creates the row the version then locks; an existing row is left as it is.
_ENSURE_DOCUMENT = text(
    """
    INSERT INTO ai.tenant_documents (tenant_id, document_id, establishment_id)
    VALUES (:tenant_id, :document_id, :establishment_id)
    ON CONFLICT ON CONSTRAINT tenant_documents_pkey DO NOTHING
    """
)
_INSERT_CHUNK = text(
    """
    INSERT INTO ai.chunks (tenant_id, source_type, source_id, source_version, language,
                           section_path, content, content_hash, embedding, metadata)
    VALUES (:tenant_id, 'tenant_document', :document_id, :version, :language, :section_path,
            :content, :content_hash, CAST(:embedding AS public.vector), CAST(:metadata AS jsonb))
    ON CONFLICT ON CONSTRAINT chunks_source_content_key DO NOTHING
    """
)
# tenant_id leads chunks_source_content_key, so both deletes use it.
_DELETE_OTHER_VERSIONS = text(
    """
    DELETE FROM ai.chunks
    WHERE tenant_id = :tenant_id AND source_type = 'tenant_document'
      AND source_id = :document_id AND source_version <> :version
    """
)
# A message without a title or type (older publishers) keeps the ones stored before.
_SET_INDEXED = text(
    """
    UPDATE ai.tenant_documents
    SET indexed_version = :version, chunk_count = :chunk_count, page_count = :page_count,
        language = :language, title = coalesce(:title, title),
        document_type = coalesce(:document_type, document_type), updated_at = now()
    WHERE tenant_id = :tenant_id AND document_id = :document_id
    """
)
# A delete for a document never seen still leaves the marker, for an upload delivered later.
_MARK_DELETED = text(
    """
    INSERT INTO ai.tenant_documents AS d (tenant_id, document_id, establishment_id, deleted_at)
    VALUES (:tenant_id, :document_id, :establishment_id, now())
    ON CONFLICT ON CONSTRAINT tenant_documents_pkey
    DO UPDATE SET deleted_at = coalesce(d.deleted_at, now()), updated_at = now()
    """
)
_DELETE_CHUNKS = text(
    """
    DELETE FROM ai.chunks
    WHERE tenant_id = :tenant_id AND source_type = 'tenant_document' AND source_id = :document_id
    """
)


class IndexAction(Enum):
    """What a version's message calls for: index it, answer with the stored result (already
    current), or drop it (the document is deleted, or a newer version is indexed)."""

    INDEX = auto()
    CURRENT = auto()
    DROP = auto()


def _index_action(row: Any, version: int) -> IndexAction:
    """What a version's message calls for, given its document's row (None: never seen)."""
    if row is None:
        return IndexAction.INDEX
    if row.deleted_at is not None:
        return IndexAction.DROP
    if row.indexed_version is None or version > row.indexed_version:
        return IndexAction.INDEX
    return IndexAction.CURRENT if version == row.indexed_version else IndexAction.DROP


class DocumentWorker:
    """Indexes and deletes one tenant's document versions; shares the service's embedder."""

    def __init__(
        self,
        engine: Engine,
        embedder: Embedder,
        blobs: BlobStore,
        parsing: ParseSettings | None = None,
    ) -> None:
        self.engine = engine
        self.embedder = embedder
        self.blobs = blobs
        self.parsing = parsing or ParseSettings()

    def reachable(self) -> bool:
        """Whether the database and blob storage answer. A failure while one of them does not is
        an outage, not the message's fault."""
        try:
            with self.engine.connect() as connection:
                connection.execute(text("SELECT 1"))
            self.blobs.reachable()
        except Exception:
            return False
        return True

    def ingest(
        self, tenant_id: UUID, message: DocumentUploaded, on_start: Callable[[], None]
    ) -> DocumentIndexed | None:
        """Index one version and return its DocumentIndexed, also when the version was already
        current (a redelivery: nothing is redone). Returns None when the work is dropped: the
        document is deleted or a newer version is indexed. Calls `on_start` once, before any work.
        Raises Rejected for a file that cannot be indexed."""
        keys = {"tenant_id": tenant_id, "document_id": message.document_id}
        with tenant_transaction(self.engine, tenant_id) as connection:
            row = connection.execute(_SELECT_DOCUMENT, keys).one_or_none()
        action = _index_action(row, message.version_number)
        if action is not IndexAction.INDEX:
            what = "dropped" if action is IndexAction.DROP else "already indexed"
            self._log(what, tenant_id, message)
            return _indexed(message, row) if action is IndexAction.CURRENT else None

        on_start()
        data = self.blobs.download(tenant_id, message.blob_path)
        if hashlib.sha256(data).hexdigest() != message.sha256:
            raise Rejected(
                DocumentFailureReason.INTERNAL, "The stored file does not match the uploaded file."
            )
        # In a child process without credentials (sandbox.py): the file is untrusted.
        parsed = parse_in_subprocess(
            data,
            message.content_type,
            timeout_s=self.parsing.parse_timeout_seconds,
            memory_mb=self.parsing.parse_memory_mb,
        )
        chunks = chunk_sections(parsed.parts)
        if len(chunks) > MAX_CHUNKS:
            raise Rejected(
                DocumentFailureReason.TOO_LARGE, "The document holds more text than 100 pages."
            )
        language = detect_language(" ".join(part.text for part in parsed.parts))
        vectors = self.embedder.embed_documents([chunk.content for chunk in chunks])
        metadata = json.dumps({"embedding_model": self.embedder.name})

        with tenant_transaction(self.engine, tenant_id) as connection:
            connection.execute(
                _ENSURE_DOCUMENT, {**keys, "establishment_id": message.establishment_id}
            )
            row = connection.execute(_LOCK_DOCUMENT, keys).one()
            action = _index_action(row, message.version_number)
            if action is not IndexAction.INDEX:  # changed while it was parsed and embedded
                what = "dropped" if action is IndexAction.DROP else "already indexed"
                self._log(what, tenant_id, message)
                return _indexed(message, row) if action is IndexAction.CURRENT else None
            version = {**keys, "version": message.version_number}
            connection.execute(
                _INSERT_CHUNK,
                [
                    {
                        **version,
                        "language": language,
                        "section_path": chunk.section_path or None,
                        "content": chunk.content,
                        "content_hash": chunk.content_hash,
                        "embedding": "[" + ",".join(map(str, vector)) + "]",
                        "metadata": metadata,
                    }
                    for chunk, vector in zip(chunks, vectors, strict=True)
                ],
            )
            connection.execute(_DELETE_OTHER_VERSIONS, version)
            result = DocumentIndexed(
                establishment_id=message.establishment_id,
                document_id=message.document_id,
                version_id=message.version_id,
                version_number=message.version_number,
                chunk_count=len(chunks),
                page_count=parsed.page_count,
                language=language,
            )
            connection.execute(
                _SET_INDEXED,
                {
                    **version,
                    "chunk_count": result.chunk_count,
                    "page_count": result.page_count,
                    "language": language,
                    "title": message.title,
                    "document_type": message.document_type,
                },
            )
        self._log("indexed", tenant_id, message, chunks=len(chunks))
        return result

    def delete(self, tenant_id: UUID, message: DocumentDeleted) -> None:
        """Mark the document deleted and remove its chunks; an ingestion in flight then stores
        nothing."""
        keys = {"tenant_id": tenant_id, "document_id": message.document_id}
        with tenant_transaction(self.engine, tenant_id) as connection:
            connection.execute(
                _MARK_DELETED, {**keys, "establishment_id": message.establishment_id}
            )
            removed = connection.execute(_DELETE_CHUNKS, keys).rowcount
        logger.info(
            "Deleted document %s of tenant %s: %d chunks removed",
            message.document_id,
            tenant_id,
            removed,
            extra={"tenant_id": str(tenant_id)},
        )

    @staticmethod
    def _log(
        what: str, tenant_id: UUID, message: DocumentUploaded, chunks: int | None = None
    ) -> None:
        # Ids and counts only: never file names (blob paths), text or content.
        logger.info(
            "Version %d of document %s of tenant %s %s%s",
            message.version_number,
            message.document_id,
            tenant_id,
            what,
            "" if chunks is None else f": {chunks} chunks",
            extra={"tenant_id": str(tenant_id)},
        )


def _indexed(message: DocumentUploaded, row: Any) -> DocumentIndexed:
    """The DocumentIndexed of a version stored earlier, from its row."""
    return DocumentIndexed(
        establishment_id=message.establishment_id,
        document_id=message.document_id,
        version_id=message.version_id,
        version_number=message.version_number,
        chunk_count=row.chunk_count,
        page_count=row.page_count,
        language=row.language,
    )
