"""The inspection consumer's decisions (`messaging/inspections.handle`) with a fake broker: offline
with a fake worker (the contract, retries, outages, event ids), and on the shared PostgreSQL
(marker `integration`) with the real `InspectionWorker` over documents indexed by
`DocumentWorker`, with the fakes of `tests/fakes.py` for the models (started, progress, completed,
a redelivery of a finished inspection, no documents, the last delivery's failure). RabbitMQ
itself: test_rabbitmq.py."""

import hashlib
import json
import logging
from dataclasses import dataclass, field, replace
from pathlib import Path
from typing import Any
from uuid import UUID, uuid4

import anyio
import pytest
from fakes import EchoJudge, FakeContainer, FakeEmbedder, FakeReranker
from sqlalchemy import Engine
from test_consumer import FakeBroker
from test_contracts import errors, schema_name
from test_inspection_graph import ITEMS, SETTINGS, stub_regulation

from normetry_ai.ingestion.documents import BlobStore, DocumentWorker
from normetry_ai.ingestion.parsers import TXT
from normetry_ai.inspection import checklist
from normetry_ai.inspection.checklist import Checklist
from normetry_ai.inspection.worker import CORPUS_INCOMPLETE, NO_DOCUMENTS, InspectionWorker
from normetry_ai.llm.claude import ModelUnavailable
from normetry_ai.messaging import inspections
from normetry_ai.messaging.consumer import DELIVERY_LIMIT
from normetry_ai.messaging.contracts import (
    DocumentUploaded,
    InspectionCompleted,
    InspectionFailed,
    InspectionProgressed,
    InspectionRequested,
    InspectionStarted,
    ThemeProgress,
    envelope,
)
from normetry_ai.settings import InspectionSettings, RetrievalSettings

FIXTURES = Path(__file__).resolve().parents[2] / "shared" / "contracts" / "messages" / "fixtures"
TENANT = uuid4()


def requested(establishment: UUID | None = None) -> InspectionRequested:
    return InspectionRequested(
        establishment_id=establishment or uuid4(),
        inspection_id=uuid4(),
        jurisdiction="US",
        establishment_type="Restaurant",
        token_ceiling=300_000,
        requested_by="user-1",
    )


def deliver(
    worker: Any, payload: Any, delivery_count: int = 0, broker: FakeBroker | None = None
) -> tuple[str, FakeBroker]:
    broker = broker or FakeBroker()
    body = envelope(TENANT, payload).model_dump_json().encode()
    action = anyio.run(inspections.handle, body, delivery_count, worker, broker.publish)
    return action, broker


@dataclass
class FakeWorker:
    """Publishes one started and one progress event, then returns its result or raises."""

    result: Any = None
    error: Exception | None = None
    up: bool = True
    calls: list[tuple[UUID, InspectionRequested]] = field(default_factory=list)

    def reachable(self) -> bool:
        return self.up

    async def inspect(self, tenant_id: UUID, request: InspectionRequested, publish: Any) -> Any:
        self.calls.append((tenant_id, request))
        ids = {"establishment_id": request.establishment_id, "inspection_id": request.inspection_id}
        themes = (ThemeProgress(theme="waste", items_total=1, items_evaluated=0),)
        await publish(InspectionStarted(**ids, items_total=1, themes=themes))
        if self.error is not None:
            raise self.error
        done = (ThemeProgress(theme="waste", items_total=1, items_evaluated=1),)
        await publish(InspectionProgressed(**ids, items_evaluated=1, items_total=1, themes=done))
        return self.result or InspectionCompleted(
            **ids, input_tokens=1, output_tokens=1, cost_usd=0.0, findings=()
        )


@pytest.mark.parametrize(
    "body",
    [
        b"not json",
        (FIXTURES / "inspection-requested.v1.invalid.json").read_bytes(),
        (FIXTURES / "document-uploaded.v1.valid.json").read_bytes(),  # not for this queue
        (FIXTURES / "inspection-completed.v1.valid.json").read_bytes(),  # ai's own event
    ],
    ids=["not-json", "invalid", "document-queue", "result-event"],
)
def test_a_message_off_this_queues_contract_is_dead_lettered_untouched(body: bytes) -> None:
    worker, broker = FakeWorker(), FakeBroker()

    assert anyio.run(inspections.handle, body, 0, worker, broker.publish) == "dead-letter"
    assert (worker.calls, broker.sent) == ([], [])


def test_the_result_is_published_for_the_message_tenant_then_acked() -> None:
    request = requested()
    worker = FakeWorker()

    action, broker = deliver(worker, request)

    assert action == "ack"
    assert worker.calls == [(TENANT, request)]
    assert [kind for kind, _ in broker.sent] == [
        "InspectionStarted",
        "InspectionProgressed",
        "InspectionCompleted",
    ]
    assert {message.tenant_id for message in broker.published} == {TENANT}
    for message in broker.published:
        body = json.loads(message.model_dump_json())
        assert errors(schema_name(type(message.payload)), body) == []


def test_event_ids_come_from_tenant_type_inspection_and_progress_step() -> None:
    request = requested()
    first = deliver(FakeWorker(), request)[1].published
    again = deliver(FakeWorker(), request)[1].published

    assert [m.message_id for m in first] == [m.message_id for m in again]
    assert len({m.message_id for m in first}) == 3
    progressed = first[1].payload
    later = progressed.model_copy(update={"items_evaluated": 2})
    assert inspections.event(TENANT, later).message_id != first[1].message_id
    assert inspections.event(uuid4(), progressed).message_id != first[1].message_id


def test_a_failure_is_retried_then_fails_the_inspection_and_is_dead_lettered() -> None:
    request = requested()
    for count in range(DELIVERY_LIMIT):
        action, broker = deliver(FakeWorker(error=ModelUnavailable("x")), request, count)
        assert action == "retry"
        assert [kind for kind, _ in broker.sent] == ["InspectionStarted"]

    action, broker = deliver(FakeWorker(error=ModelUnavailable("x")), request, DELIVERY_LIMIT)

    assert action == "dead-letter"
    kind, failed = broker.sent[-1]
    assert (kind, failed.reason_code, failed.reason_text) == (
        "InspectionFailed",
        "error",
        inspections.ERROR,
    )


def test_a_failure_during_an_outage_waits_without_spending_a_delivery() -> None:
    worker = FakeWorker(error=OSError("database down"), up=False)

    action, broker = deliver(worker, requested(), DELIVERY_LIMIT)

    assert action == "wait"
    assert "InspectionFailed" not in [kind for kind, _ in broker.sent]


def test_a_result_that_cannot_be_published_is_retried() -> None:
    broker = FakeBroker(refuses="InspectionCompleted")

    assert deliver(FakeWorker(), requested(), 0, broker)[0] == "retry"


def test_the_queue_dead_letters_after_the_limit_the_consumer_counts_to() -> None:
    assert inspections.QUEUE_ARGUMENTS == {
        "x-queue-type": "quorum",
        "x-dead-letter-exchange": "normetry.dead-letter",
        "x-dead-letter-routing-key": "ai.inspections",
        "x-delivery-limit": DELIVERY_LIMIT,
    }


# --- The real worker on PostgreSQL --------------------------------------------------------------


@dataclass
class Establishment:
    engine: Engine
    corpus: Engine
    judge: EchoJudge = field(default_factory=EchoJudge)
    documents: dict[UUID, UUID] = field(default_factory=dict)  # establishment -> its document

    @property
    def worker(self) -> InspectionWorker:
        return InspectionWorker(
            self.engine,
            FakeEmbedder(),
            FakeReranker(),
            self.judge,
            answers=SETTINGS,
            retrieval=RetrievalSettings(_env_file=None),
            inspection=InspectionSettings(_env_file=None),
        )

    def with_document(self, tenant: UUID = TENANT) -> UUID:
        """A new establishment with one indexed document."""
        establishment, document, version = uuid4(), uuid4(), uuid4()
        data = b"Our daily log records every cooler reading, every cleaning and every delivery."
        path = f"tenants/{tenant}/establishments/{establishment}/documents/{document}/{version}"
        blobs = FakeContainer({path: data})
        message = DocumentUploaded(
            establishment_id=establishment,
            document_id=document,
            version_id=version,
            version_number=1,
            blob_path=path,
            content_type=TXT,
            size=len(data),
            sha256=hashlib.sha256(data).hexdigest(),
            title="Daily log",
            document_type="Log",
        )
        worker = DocumentWorker(self.engine, FakeEmbedder(), BlobStore(blobs))
        assert worker.ingest(tenant, message, lambda: None) is not None
        self.documents[establishment] = document
        return establishment


@pytest.fixture(scope="module")
def regulation(corpus: Engine) -> None:
    stub_regulation(corpus, ITEMS)


@pytest.fixture
def establishment(app_ai: Engine, corpus: Engine, regulation: None) -> Establishment:
    return Establishment(app_ai, corpus)


@pytest.mark.integration
def test_an_inspection_reports_its_start_progress_and_findings(
    establishment: Establishment, caplog: pytest.LogCaptureFixture
) -> None:
    caplog.set_level(logging.INFO, logger="normetry_ai.inspection.judge")
    request = requested(establishment.with_document())
    # Another establishment of the tenant holds the very same text.
    establishment.with_document()

    action, broker = deliver(establishment.worker, request)

    assert action == "ack"
    kinds = [kind for kind, _ in broker.sent]
    assert kinds == ["InspectionStarted"] + ["InspectionProgressed"] * len(ITEMS) + [
        "InspectionCompleted"
    ]
    started, completed = broker.sent[0][1], broker.sent[-1][1]
    assert started.items_total == len(ITEMS)
    assert [p.items_evaluated for _, p in broker.sent[1:-1]] == list(range(1, len(ITEMS) + 1))
    assert sorted(f.item_id for f in completed.findings) == sorted(i.id for i in ITEMS)
    # Evidence only from the inspected establishment's document.
    assert {c.document_id for f in completed.findings for c in f.evidence_citations} == {
        establishment.documents[request.establishment_id]
    }
    # The judge's log lines carry the envelope's tenant, which the judge itself never sees.
    judged = [r for r in caplog.records if r.name == "normetry_ai.inspection.judge"]
    assert len(judged) == len(ITEMS)
    assert {getattr(record, "tenant_id", None) for record in judged} == {str(TENANT)}
    assert completed.input_tokens > 0 and completed.cost_usd > 0
    for message in broker.published:
        body = json.loads(message.model_dump_json())
        assert errors(schema_name(type(message.payload)), body) == []


@pytest.mark.integration
def test_a_redelivered_finished_inspection_publishes_its_result_again(
    establishment: Establishment,
) -> None:
    request = requested(establishment.with_document())
    first = deliver(establishment.worker, request)[1].published[-1]
    judged = len(establishment.judge.judged)

    action, broker = deliver(establishment.worker, request)

    assert action == "ack"
    [again] = broker.published
    assert (again.type, again.message_id, again.payload) == (
        "InspectionCompleted",
        first.message_id,
        first.payload,
    )
    assert len(establishment.judge.judged) == judged  # nothing judged again


@pytest.mark.integration
def test_an_establishment_without_an_indexed_document_fails_no_documents(
    establishment: Establishment,
) -> None:
    establishment.with_document()  # another establishment of the tenant has one
    request = requested()

    action, broker = deliver(establishment.worker, request)

    assert action == "ack"
    [(kind, failed)] = broker.sent
    assert (kind, failed.reason_code, failed.reason_text) == (
        "InspectionFailed",
        "no_documents",
        NO_DOCUMENTS,
    )
    assert establishment.judge.judged == []
    # Another tenant's documents under the same establishment id do not count either.
    other = establishment.with_document(uuid4())
    assert deliver(establishment.worker, requested(other))[1].sent[0][1].reason_code == (
        "no_documents"
    )


@pytest.mark.integration
def test_a_checklist_section_missing_from_the_corpus_fails_before_any_item_is_judged(
    establishment: Establishment,
    monkeypatch: pytest.MonkeyPatch,
    caplog: pytest.LogCaptureFixture,
) -> None:
    # A corpus ingested before a parser fix: an item cites a section it has and one it lacks.
    kept, gone = ITEMS[0].section_paths[0], f"test-{uuid4()}"
    items = (replace(ITEMS[0], section_paths=(kept, gone)), *ITEMS[1:])
    fake = Checklist("food-code", "US", items)
    monkeypatch.setattr(checklist, "load_checklist", lambda jurisdiction: fake)
    request = requested(establishment.with_document())

    action, broker = deliver(establishment.worker, request)

    assert action == "ack"
    [(kind, failed)] = broker.sent
    assert (kind, failed.reason_code, failed.reason_text) == (
        "InspectionFailed",
        "error",
        CORPUS_INCOMPLETE,
    )
    [message] = broker.published
    assert errors(schema_name(type(message.payload)), json.loads(message.model_dump_json())) == []
    assert establishment.judge.judged == []
    [warning] = [r for r in caplog.records if r.name == "normetry_ai.inspection.worker"]
    assert gone in warning.getMessage() and kept not in warning.getMessage()


@pytest.mark.integration
def test_the_last_delivery_of_a_failing_inspection_reports_an_error(
    establishment: Establishment,
) -> None:
    establishment.judge = EchoJudge(fail=lambda title: ModelUnavailable("overloaded"))
    request = requested(establishment.with_document())

    actions = [deliver(establishment.worker, request, count)[0] for count in range(DELIVERY_LIMIT)]
    action, broker = deliver(establishment.worker, request, DELIVERY_LIMIT)

    assert actions == ["retry"] * DELIVERY_LIMIT
    assert action == "dead-letter"
    kind, failed = broker.sent[-1]
    assert (kind, failed.reason_code) == ("InspectionFailed", "error")
    assert isinstance(failed, InspectionFailed)


@pytest.mark.integration
def test_an_inspection_id_reused_for_another_establishment_is_dead_lettered(
    establishment: Establishment,
) -> None:
    request = requested(establishment.with_document())
    deliver(establishment.worker, request)
    judged = len(establishment.judge.judged)
    other = request.model_copy(update={"establishment_id": establishment.with_document()})

    action, broker = deliver(establishment.worker, other)

    # Neither the first establishment's findings published again, nor a run mixing both.
    assert (action, broker.sent) == ("dead-letter", [])
    assert len(establishment.judge.judged) == judged


@pytest.mark.parametrize(
    "logger", ["normetry_ai.inspection.judge", "normetry_ai.llm.claude", "normetry_ai.db"]
)
def test_judge_and_model_logs_carry_the_envelope_tenant_only_while_handled(
    logger: str, caplog: pytest.LogCaptureFixture
) -> None:
    class LoggingWorker(FakeWorker):
        async def inspect(self, tenant_id: UUID, request: InspectionRequested, publish: Any) -> Any:
            logging.getLogger(logger).warning("inside")
            return await super().inspect(tenant_id, request, publish)

    deliver(LoggingWorker(), requested())
    logging.getLogger(logger).warning("outside")

    stamped = {r.getMessage(): getattr(r, "tenant_id", None) for r in caplog.records}
    expected = str(TENANT) if logger != "normetry_ai.db" else None  # only the two filtered
    assert stamped == {"inside": expected, "outside": None}
