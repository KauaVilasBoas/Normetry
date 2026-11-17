"""Queue message contracts (src/shared/contracts/messages): the shared JSON Schemas, their fixtures
and the Python models. The .NET side validates the same fixtures against the same schemas."""

import json
from pathlib import Path
from typing import get_args
from uuid import UUID, uuid4

import pytest
from jsonschema import Draft202012Validator
from pydantic import ValidationError
from referencing import Registry, Resource

from normetry_ai.inspection.checklist import THEMES
from normetry_ai.messaging.contracts import (
    MESSAGES,
    Action,
    DocumentDeleted,
    DocumentIndexed,
    DocumentIngestionFailed,
    DocumentIngestionStarted,
    DocumentUploaded,
    EvidenceCitation,
    Finding,
    InspectionCompleted,
    InspectionFailed,
    InspectionProgressed,
    InspectionRequested,
    InspectionStarted,
    Payload,
    RegulationCitation,
    Theme,
    ThemeProgress,
    envelope,
    parse,
)

CONTRACTS = Path(__file__).resolve().parents[2] / "shared" / "contracts" / "messages"
SCHEMAS = {
    path.name.removesuffix(".schema.json"): json.loads(path.read_text(encoding="utf-8"))
    for path in sorted(CONTRACTS.glob("*.schema.json"))
}
REGISTRY = Registry().with_resources(
    (schema["$id"], Resource.from_contents(schema)) for schema in SCHEMAS.values()
)
FIXTURES = sorted((CONTRACTS / "fixtures").glob("*.json"))
# The keywords of a required field that make it what it is; descriptions and limits may change.
SHAPE = ("type", "const", "enum", "$ref")


def schema_name(payload: type[Payload]) -> str:
    """DocumentIngestionStarted v1 -> document-ingestion-started.v1"""
    kebab = "".join(f"-{c.lower()}" if c.isupper() else c for c in payload.TYPE).lstrip("-")
    return f"{kebab}.v{payload.VERSION}"


def validator(name: str) -> Draft202012Validator:
    return Draft202012Validator(SCHEMAS[name], registry=REGISTRY)


def errors(name: str, message: object) -> list[str]:
    return [error.message for error in validator(name).iter_errors(message)]


def test_the_schemas_are_valid_draft_2020_12() -> None:
    assert SCHEMAS.keys() >= {"envelope.v1", *map(schema_name, MESSAGES.values())}
    for schema in SCHEMAS.values():
        Draft202012Validator.check_schema(schema)


def test_every_message_has_a_valid_and_an_invalid_fixture() -> None:
    names = {path.name for path in FIXTURES}
    expected = {
        f"{schema_name(payload)}.{kind}.json"
        for payload in MESSAGES.values()
        for kind in ("valid", "invalid")
    }
    assert names == expected


@pytest.mark.parametrize("fixture", FIXTURES, ids=lambda path: path.name)
def test_fixtures_validate_against_their_schema_and_the_python_models(fixture: Path) -> None:
    name, kind = fixture.name.removesuffix(".json").rsplit(".", 1)
    message = json.loads(fixture.read_text(encoding="utf-8"))

    if kind == "valid":
        assert errors(name, message) == []
        parsed = parse(fixture.read_bytes())
        assert schema_name(type(parsed.payload)) == name
        # What Python reads, it writes back the same.
        assert errors(name, parsed.model_dump(mode="json")) == []
        assert parsed.payload.model_dump(mode="json") == message["payload"]
    else:
        assert len(errors(name, message)) == 1, "an invalid fixture breaks exactly one rule"
        with pytest.raises(ValidationError):
            parse(fixture.read_bytes())


VERSION = {
    "establishment_id": uuid4(),
    "document_id": uuid4(),
    "version_id": uuid4(),
    "version_number": 3,
}
SAMPLES: list[Payload] = [
    DocumentUploaded(
        **VERSION,
        blob_path="tenants/x/file.txt",
        content_type="text/plain",
        size=12,
        sha256="a" * 64,
    ),
    DocumentDeleted(establishment_id=uuid4(), document_id=uuid4()),
    DocumentIngestionStarted(**VERSION),
    DocumentIndexed(**VERSION, chunk_count=4, page_count=2, language="en"),
    DocumentIndexed(**VERSION, chunk_count=1, page_count=None, language="pt"),
    DocumentIngestionFailed(**VERSION, reason_code="too_large", reason_text="Over 10 MiB."),
]
INSPECTION = {"establishment_id": uuid4(), "inspection_id": uuid4()}
FINDING = Finding(
    item_id="br.cold-holding",
    theme="temperature_control",
    title="Chilled food is kept at 5 °C or below",
    severity="critical",
    verdict="non_compliant",
    confidence="high",
    rationale="The cold room log shows 9 °C.",
    regulation_citations=(
        RegulationCitation(
            source_title="RDC 216/2004",
            source_version="2004",
            section_path="4.8.15",
            language="pt",
            text="Conservar em temperatura inferior a 5 °C.",
            quote="inferior a 5 °C",
            quote_start=25,
            quote_end=40,
        ),
    ),
    evidence_citations=(
        EvidenceCitation(
            document_id=uuid4(),
            document_title=None,
            document_type=None,
            version_number=1,
            section_path="",
            language="pt",
            text="Câmara fria: 9 °C.",
            quote="9 °C",
            quote_start=None,
            quote_end=None,
        ),
    ),
    action=Action(text="Repair the cold room.", due_in_days=7),
)
NO_EVIDENCE = Finding(
    **FINDING.model_dump(by_alias=False)
    | {"verdict": "insufficient_evidence", "evidence_citations": ()}
)
SAMPLES += [
    DocumentUploaded(
        **SAMPLES[0].model_dump(by_alias=False), title="Cold SOP", document_type="Sop"
    ),
    InspectionRequested(
        **INSPECTION,
        jurisdiction="BR",
        establishment_type="Restaurant",
        token_ceiling=300_000,
        requested_by="a-user-sub",
    ),
    InspectionStarted(
        **INSPECTION,
        items_total=34,
        themes=(ThemeProgress(theme="waste", items_total=34, items_evaluated=0),),
    ),
    InspectionStarted(**INSPECTION, items_total=0, themes=()),
    InspectionProgressed(
        **INSPECTION,
        items_evaluated=5,
        items_total=34,
        themes=(
            ThemeProgress(theme="personal_hygiene", items_total=5, items_evaluated=5),
            ThemeProgress(theme="pest_control", items_total=29, items_evaluated=0),
        ),
    ),
    InspectionCompleted(
        **INSPECTION,
        input_tokens=1200,
        output_tokens=300,
        cost_usd=0.0042,
        findings=(FINDING, NO_EVIDENCE),
    ),
    InspectionCompleted(**INSPECTION, input_tokens=0, output_tokens=0, cost_usd=0, findings=()),
    InspectionFailed(**INSPECTION, reason_code="budget_exceeded", reason_text="Over the ceiling."),
]


@pytest.mark.parametrize("payload", SAMPLES, ids=lambda payload: type(payload).__name__)
def test_python_messages_serialize_to_their_schema(payload: Payload) -> None:
    tenant = uuid4()
    message = json.loads(envelope(tenant, payload).model_dump_json())

    assert errors(schema_name(type(payload)), message) == []
    assert (message["tenantId"], message["type"]) == (str(tenant), payload.TYPE)
    assert parse(json.dumps(message)).payload == payload


def test_every_message_model_is_sampled() -> None:
    assert {type(payload) for payload in SAMPLES} == set(MESSAGES.values())


def test_a_message_must_match_its_payload_and_have_a_tenant() -> None:
    body = json.loads(envelope(uuid4(), SAMPLES[2]).model_dump_json())
    for change in ({"version": 2}, {"type": "DocumentIndexed"}, {"tenantId": str(UUID(int=0))}):
        with pytest.raises(ValueError):
            parse(json.dumps(body | change))
    for not_a_message in (b"[]", b"not json", b"\xff"):
        with pytest.raises(ValueError):
            parse(not_a_message)


def test_readers_ignore_payload_fields_they_do_not_know() -> None:
    # Tolerant reader: a producer may add an optional field before this side knows it.
    body = json.loads(envelope(uuid4(), SAMPLES[1]).model_dump_json())
    body["payload"]["addedLater"] = True

    assert parse(json.dumps(body)).payload == SAMPLES[1]


def test_document_uploaded_without_title_or_type_is_still_valid() -> None:
    # Both were added to v1 as optional fields: a message from before them still reads, and
    # Python leaves an unset one out (the schema has no null for them).
    fixture = CONTRACTS / "fixtures" / "document-uploaded.v1.valid.json"
    message = json.loads(fixture.read_text(encoding="utf-8"))
    del message["payload"]["title"], message["payload"]["documentType"]

    assert errors("document-uploaded.v1", message) == []
    payload = parse(json.dumps(message)).payload
    assert (payload.title, payload.document_type) == (None, None)
    assert payload.model_dump(mode="json") == message["payload"]


@pytest.mark.parametrize(
    "change",
    [
        {"evidence_citations": ()},
        {"verdict": "compliant", "evidence_citations": (), "action": None},
        {"action": None},
        {"verdict": "compliant"},
    ],
    ids=["non-compliant-no-evidence", "compliant-no-evidence", "no-action", "compliant-action"],
)
def test_a_finding_keeps_the_rules_its_schema_cannot_state(change: dict) -> None:
    with pytest.raises(ValidationError):
        Finding(**FINDING.model_dump(by_alias=False) | change)


def test_message_themes_are_the_checklist_themes() -> None:
    # A new checklist theme is a breaking change of the inspection events: a new version.
    assert get_args(Theme) == THEMES
    for name in ("inspection-started.v1", "inspection-progressed.v1"):
        themes = SCHEMAS[name]["properties"]["payload"]["properties"]["themes"]
        assert tuple(themes["items"]["properties"]["theme"]["enum"]) == THEMES
    assert tuple(SCHEMAS["inspection-completed.v1"]["$defs"]["theme"]["enum"]) == THEMES


def required_shape(schema: dict) -> dict[str, dict]:
    """The required fields of a schema, nested ones too (`payload.findings[].itemId`), and what
    makes each one what it is."""

    def shape(prop: dict) -> dict:
        return {key: prop[key] for key in SHAPE if key in prop}

    def resolve(prop: dict) -> dict:
        # Inside a payload, a reference to the schema's own $defs stands for what it names.
        ref = prop.get("$ref", "")
        return schema["$defs"][ref.removeprefix("#/$defs/")] if ref.startswith("#/$defs/") else prop

    def fields(prop: dict, path: str) -> dict[str, dict]:
        prop = resolve(prop)
        if "items" in prop:
            return fields(prop["items"], f"{path}[]")
        found = {}
        for name in prop.get("required", []):
            child = resolve(prop["properties"][name])
            found[f"{path}.{name}"] = shape(child)
            found |= fields(child, f"{path}.{name}")
        return found

    properties = schema["properties"]
    if "required" in schema:  # the envelope
        return {field: shape(properties[field]) for field in schema["required"]}
    return {
        "type": shape(properties["type"]),
        "version": shape(properties["version"]),
        **fields(properties["payload"], "payload"),
    }


def test_published_versions_keep_their_required_fields() -> None:
    """A required field added, removed, renamed or retyped breaks every consumer of that version:
    publish a new version (a new schema file and its entry in published.json) instead."""
    published = json.loads((CONTRACTS / "published.json").read_text(encoding="utf-8"))

    # A published version is never deleted, and every schema is published.
    assert published.keys() == SCHEMAS.keys()
    for name, schema in SCHEMAS.items():
        assert required_shape(schema) == published[name], name
