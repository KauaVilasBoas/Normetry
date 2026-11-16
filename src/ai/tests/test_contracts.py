"""Queue message contracts (src/shared/contracts/messages): the shared JSON Schemas, their fixtures
and the Python models. The .NET side validates the same fixtures against the same schemas."""

import json
from pathlib import Path
from uuid import UUID, uuid4

import pytest
from jsonschema import Draft202012Validator
from pydantic import ValidationError
from referencing import Registry, Resource

from normetry_ai.messaging.contracts import (
    MESSAGES,
    DocumentDeleted,
    DocumentIndexed,
    DocumentIngestionFailed,
    DocumentIngestionStarted,
    DocumentUploaded,
    Payload,
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


def required_shape(schema: dict) -> dict[str, dict]:
    """The required fields of a schema and what makes each one what it is."""

    def shape(prop: dict) -> dict:
        return {key: prop[key] for key in SHAPE if key in prop}

    properties = schema["properties"]
    if "required" in schema:  # the envelope
        return {field: shape(properties[field]) for field in schema["required"]}
    payload = properties["payload"]
    return {
        "type": shape(properties["type"]),
        "version": shape(properties["version"]),
        **{
            f"payload.{field}": shape(payload["properties"][field]) for field in payload["required"]
        },
    }


def test_published_versions_keep_their_required_fields() -> None:
    """A required field added, removed, renamed or retyped breaks every consumer of that version:
    publish a new version (a new schema file and its entry in published.json) instead."""
    published = json.loads((CONTRACTS / "published.json").read_text(encoding="utf-8"))

    # A published version is never deleted, and every schema is published.
    assert published.keys() == SCHEMAS.keys()
    for name, schema in SCHEMAS.items():
        assert required_shape(schema) == published[name], name
