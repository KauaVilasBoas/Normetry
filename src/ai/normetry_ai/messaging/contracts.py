"""The queue message contracts of src/shared/contracts/messages, as pydantic models.

The JSON Schemas there are the source of truth; `tests/test_contracts.py` validates these models'
output against them, and the shared fixtures against both. Fields are camelCase on the wire.
Readers are tolerant: a payload field this side does not know yet is ignored, so adding an
optional field is not a breaking change (README.md there, "Versions").
"""

import json
from datetime import UTC, datetime
from typing import Any, ClassVar, Literal, Self
from uuid import UUID, uuid4

from pydantic import AwareDatetime, BaseModel, ConfigDict, Field, model_validator
from pydantic.alias_generators import to_camel

FailureReason = Literal[
    "unsupported_format",
    "no_text_layer",
    "too_many_pages",
    "too_large",
    "blob_missing",
    "blob_path_outside_tenant",
    "parse_error",
    "internal",
]


class _Model(BaseModel):
    model_config = ConfigDict(
        alias_generator=to_camel,
        validate_by_name=True,
        validate_by_alias=True,
        serialize_by_alias=True,
        frozen=True,
    )


class Payload(_Model):
    """A message's fields. Each subclass names its message, version and routing key."""

    TYPE: ClassVar[str]
    VERSION: ClassVar[int]
    ROUTING_KEY: ClassVar[str]


class DocumentPayload(Payload):
    establishment_id: UUID
    document_id: UUID


class VersionPayload(DocumentPayload):
    version_id: UUID
    version_number: int = Field(ge=1)


class DocumentUploaded(VersionPayload):
    TYPE = "DocumentUploaded"
    VERSION = 1
    ROUTING_KEY = "document.uploaded.v1"

    blob_path: str = Field(min_length=1, max_length=1024)
    content_type: str = Field(min_length=1, max_length=255)
    size: int = Field(ge=0)
    sha256: str = Field(pattern=r"^[0-9a-f]{64}$")


class DocumentDeleted(DocumentPayload):
    TYPE = "DocumentDeleted"
    VERSION = 1
    ROUTING_KEY = "document.deleted.v1"


class DocumentIngestionStarted(VersionPayload):
    TYPE = "DocumentIngestionStarted"
    VERSION = 1
    ROUTING_KEY = "document.ingestion-started.v1"


class DocumentIndexed(VersionPayload):
    TYPE = "DocumentIndexed"
    VERSION = 1
    ROUTING_KEY = "document.indexed.v1"

    chunk_count: int = Field(ge=1)
    # None for DOCX, TXT and CSV, which have no fixed pages.
    page_count: int | None = Field(ge=1)
    language: Literal["en", "pt"]


class DocumentIngestionFailed(VersionPayload):
    TYPE = "DocumentIngestionFailed"
    VERSION = 1
    ROUTING_KEY = "document.ingestion-failed.v1"

    reason_code: FailureReason
    reason_text: str = Field(min_length=1, max_length=500)


MESSAGES: dict[tuple[str, int], type[Payload]] = {
    (payload.TYPE, payload.VERSION): payload
    for payload in (
        DocumentUploaded,
        DocumentDeleted,
        DocumentIngestionStarted,
        DocumentIndexed,
        DocumentIngestionFailed,
    )
}


class Envelope[P: Payload](_Model):
    """The body of every message: who, what and when, around the payload."""

    message_id: UUID
    type: str
    version: int
    occurred_at: AwareDatetime
    tenant_id: UUID
    payload: P

    @model_validator(mode="after")
    def _check(self) -> Self:
        if self.tenant_id.int == 0:
            raise ValueError("tenantId must not be the nil UUID")
        if (self.type, self.version) != (self.payload.TYPE, self.payload.VERSION):
            raise ValueError("type and version do not match the payload")
        return self


def envelope[P: Payload](
    tenant_id: UUID, payload: P, message_id: UUID | None = None
) -> Envelope[P]:
    """A new message for `tenant_id`, occurring now; a fresh message id unless one is given."""
    return Envelope[type(payload)](
        message_id=message_id or uuid4(),
        type=payload.TYPE,
        version=payload.VERSION,
        occurred_at=datetime.now(UTC),
        tenant_id=tenant_id,
        payload=payload,
    )


def parse(body: bytes | str) -> Envelope[Any]:
    """Read a message body. Raises ValueError (pydantic's ValidationError is one) when it is not
    JSON, names an unknown type or version, or breaks its schema."""
    data = json.loads(body)
    if not isinstance(data, dict):
        raise ValueError("a message body is a JSON object")
    kind, version = data.get("type"), data.get("version")
    # A list or an object is unhashable: a lookup would raise TypeError, which no caller expects.
    scalar = isinstance(kind, str) and isinstance(version, int)
    payload = MESSAGES.get((kind, version)) if scalar else None
    if payload is None:
        raise ValueError("unknown message type or version")
    return Envelope[payload].model_validate(data)
