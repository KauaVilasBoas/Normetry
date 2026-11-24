"""The queue message contracts of src/shared/contracts/messages, as pydantic models.

The JSON Schemas there are the source of truth; `tests/test_contracts.py` validates these models'
output against them, and the shared fixtures against both. Fields are camelCase on the wire.
Readers are tolerant: a payload field this side does not know yet is ignored, so adding an
optional field is not a breaking change (README.md there, "Versions").

The reason codes, verdicts, confidences and severities are the StrEnums below, the only place their
values are spelled out; each serializes to its value, as the schemas say.
"""

import json
from datetime import UTC, datetime
from enum import StrEnum
from typing import Any, ClassVar, Literal, Self
from uuid import UUID, uuid4

from pydantic import AwareDatetime, BaseModel, ConfigDict, Field, model_validator
from pydantic.alias_generators import to_camel


class DocumentFailureReason(StrEnum):
    """The `reasonCode` of DocumentIngestionFailed."""

    UNSUPPORTED_FORMAT = "unsupported_format"
    NO_TEXT_LAYER = "no_text_layer"
    TOO_MANY_PAGES = "too_many_pages"
    TOO_LARGE = "too_large"
    BLOB_MISSING = "blob_missing"
    BLOB_PATH_OUTSIDE_TENANT = "blob_path_outside_tenant"
    PARSE_ERROR = "parse_error"
    INTERNAL = "internal"


class InspectionFailureReason(StrEnum):
    """The `reasonCode` of InspectionFailed."""

    BUDGET_EXCEEDED = "budget_exceeded"
    NO_DOCUMENTS = "no_documents"
    ERROR = "error"


class Verdict(StrEnum):
    """A finding's verdict: what the judge may answer and the guard keeps."""

    COMPLIANT = "compliant"
    NON_COMPLIANT = "non_compliant"
    INSUFFICIENT_EVIDENCE = "insufficient_evidence"


class Confidence(StrEnum):
    """How sure the judge is of a finding's verdict: what it may answer and the guard keeps."""

    LOW = "low"
    MEDIUM = "medium"
    HIGH = "high"


class Severity(StrEnum):
    """A checklist item's severity, most severe first: findings are listed in this order."""

    CRITICAL = "critical"
    MAJOR = "major"
    MINOR = "minor"


DocumentType = Literal["Sop", "License", "Log", "Report", "Other"]
Language = Literal["en", "pt"]
# The checklist themes (inspection/checklist.py THEMES, held equal by tests/test_contracts.py).
Theme = Literal[
    "personal_hygiene",
    "temperature_control",
    "cross_contamination_allergens",
    "cleaning_sanitizing",
    "pest_control",
    "water_premises",
    "waste",
    "documentation_training",
]


def _is_none(value: object) -> bool:
    return value is None


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
    # Optional, added to v1 later: older messages have neither. The schema has no null for them,
    # so an unset one is left out of the output.
    title: str | None = Field(default=None, min_length=1, max_length=200, exclude_if=_is_none)
    document_type: DocumentType | None = Field(default=None, exclude_if=_is_none)


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

    reason_code: DocumentFailureReason
    reason_text: str = Field(min_length=1, max_length=500)


class InspectionPayload(Payload):
    establishment_id: UUID
    inspection_id: UUID


class InspectionRequested(InspectionPayload):
    TYPE = "InspectionRequested"
    VERSION = 1
    ROUTING_KEY = "inspection.requested.v1"

    jurisdiction: Literal["US", "EU", "BR"]
    establishment_type: Literal["Restaurant", "Pharmacy", "Clinic"]
    token_ceiling: int = Field(ge=1)
    # The user's sub, for usage accounting.
    requested_by: str = Field(min_length=1, max_length=255)


class ThemeProgress(_Model):
    theme: Theme
    items_total: int = Field(ge=0)
    items_evaluated: int = Field(ge=0)


class InspectionStarted(InspectionPayload):
    TYPE = "InspectionStarted"
    VERSION = 1
    ROUTING_KEY = "inspection.started.v1"

    items_total: int = Field(ge=0)
    # A full snapshot of every theme, never a delta, so a redelivered event never counts twice.
    themes: tuple[ThemeProgress, ...]


class InspectionProgressed(InspectionPayload):
    TYPE = "InspectionProgressed"
    VERSION = 1
    ROUTING_KEY = "inspection.progressed.v1"

    items_evaluated: int = Field(ge=0)
    items_total: int = Field(ge=0)
    themes: tuple[ThemeProgress, ...]


class _Quoted(_Model):
    """A copy of a cited passage and the verified quote in it."""

    language: Language
    text: str = Field(min_length=1, max_length=20000)
    quote: str = Field(min_length=1, max_length=4000)
    # text[quote_start:quote_end] is the quote as the passage writes it; None when not mapped back.
    quote_start: int | None = Field(ge=0)
    quote_end: int | None = Field(ge=0)


class RegulationCitation(_Quoted):
    source_title: str = Field(min_length=1, max_length=500)
    source_version: str = Field(min_length=1, max_length=100)
    section_path: str = Field(min_length=1, max_length=1000)


class EvidenceCitation(_Quoted):
    document_id: UUID
    # None for a document uploaded before DocumentUploaded carried them.
    document_title: str | None = Field(min_length=1, max_length=200)
    document_type: DocumentType | None
    version_number: int = Field(ge=1)
    # Empty for a text file or a document without headings.
    section_path: str = Field(max_length=1000)


class Action(_Model):
    text: str = Field(min_length=1, max_length=2000)
    due_in_days: int = Field(ge=1)


class Finding(_Model):
    item_id: str = Field(min_length=1, max_length=100)
    theme: Theme
    title: str = Field(min_length=1, max_length=200)
    severity: Severity
    verdict: Verdict
    confidence: Confidence
    rationale: str = Field(min_length=1, max_length=4000)
    regulation_citations: tuple[RegulationCitation, ...] = Field(min_length=1)
    evidence_citations: tuple[EvidenceCitation, ...]
    action: Action | None

    @model_validator(mode="after")
    def _check(self) -> Self:
        # The schema cannot say these; the guard and the action plan make them hold.
        if self.verdict != Verdict.INSUFFICIENT_EVIDENCE and not self.evidence_citations:
            raise ValueError("a compliant or non_compliant verdict needs an evidence citation")
        if (self.action is None) != (self.verdict == Verdict.COMPLIANT):
            raise ValueError("every verdict but compliant has an action, and compliant none")
        return self


class InspectionCompleted(InspectionPayload):
    TYPE = "InspectionCompleted"
    VERSION = 1
    ROUTING_KEY = "inspection.completed.v1"

    input_tokens: int = Field(ge=0)
    output_tokens: int = Field(ge=0)
    cost_usd: float = Field(ge=0)
    findings: tuple[Finding, ...]


class InspectionFailed(InspectionPayload):
    TYPE = "InspectionFailed"
    VERSION = 1
    ROUTING_KEY = "inspection.failed.v1"

    reason_code: InspectionFailureReason
    reason_text: str = Field(min_length=1, max_length=500)


MESSAGES: dict[tuple[str, int], type[Payload]] = {
    (payload.TYPE, payload.VERSION): payload
    for payload in (
        DocumentUploaded,
        DocumentDeleted,
        DocumentIngestionStarted,
        DocumentIndexed,
        DocumentIngestionFailed,
        InspectionRequested,
        InspectionStarted,
        InspectionProgressed,
        InspectionCompleted,
        InspectionFailed,
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
