"""The chat over the establishment's documents (#107), on the shared PostgreSQL as app_ai: the real
`get_retriever` and `hybrid.Retriever` (fake embedder and re-ranker), documents indexed by the real
`DocumentWorker`, and a claim model that cites every excerpt it is given, so a document that
leaked into retrieval would be cited. Every document that must stay out is the question word for
word, with its exact embedding."""

import re
from collections.abc import AsyncGenerator, Iterator, Sequence
from dataclasses import replace
from typing import Any
from uuid import UUID, uuid4
from xml.sax.saxutils import unescape

import pytest
from conftest import TOKEN
from fakes import FakeEmbedder, FakeReranker
from fastapi.testclient import TestClient
from sqlalchemy import Engine
from test_database import (
    ANSWERED,
    CHAT_SETTINGS,
    HISTORY_SETTINGS,
    ChatApi,
    Person,
    manifest_source,
    sections,
)
from test_establishment_search import Documents

from normetry_ai.api import create_app
from normetry_ai.api.conversations import Chat, get_chat, get_retriever
from normetry_ai.conversations import store as conversation_store
from normetry_ai.db import tenant_transaction
from normetry_ai.ingestion.ingest import ingest_source
from normetry_ai.ingestion.normalize import normalize_text
from normetry_ai.retrieval.hybrid import Retriever
from normetry_ai.retrieval.policy import jurisdictions_for
from normetry_ai.settings import AnswerSettings, RetrievalSettings, Settings

pytestmark = pytest.mark.integration

QUESTION = "How cold does the walk in cooler procedure say chilled food must be kept?"
_EXCERPT = re.compile(r'<excerpt id="(E\d+)" [^\n]*>\n(.*?)\n</excerpt>', re.DOTALL)


class EchoClaims:
    """A ClaimModel that makes one claim per excerpt, quoting its first six words."""

    def __init__(self) -> None:
        self.prompts: list[tuple[str, str]] = []

    async def stream_claims(self, system: str, user: str) -> AsyncGenerator[Any, None]:
        self.prompts.append((system, user))
        for excerpt_id, content in _EXCERPT.findall(user):
            quote = " ".join(normalize_text(unescape(content)).split()[:6])
            yield {
                "text": f"From {excerpt_id}.",
                "citations": [{"excerpt_id": excerpt_id, "quote": quote}],
            }
        yield ANSWERED


class CountingReranker(FakeReranker):
    def __init__(self) -> None:
        self.calls: list[list[str]] = []

    def score(self, query: str, texts: Sequence[str]) -> list[float]:
        self.calls.append(list(texts))
        return super().score(query, texts)


@pytest.fixture
def chat_api(urls: dict[str, str]) -> Iterator[ChatApi]:
    settings = Settings(internal_token=TOKEN, database_url=urls["app_ai"], _env_file=None)
    with TestClient(create_app(settings, answer_settings=CHAT_SETTINGS)) as client:
        yield ChatApi(client)


@pytest.fixture
def documents(app_ai: Engine) -> Documents:
    return Documents(app_ai)


@pytest.fixture(scope="module")
def regulation(corpus: Engine) -> dict:
    """A US corpus source whose one article is the question word for word."""
    source = manifest_source(jurisdiction="US", title="FDA Food Code", version="2026")
    ingest_source(corpus, source, sections(source, QUESTION), FakeEmbedder())
    return source


def real_retrieval(
    chat_api: ChatApi, model: Any = None, **knobs: int
) -> tuple[Any, CountingReranker]:
    """The real retrieval dependency with fake models, and the echoing claim model."""
    model, reranker = model or EchoClaims(), CountingReranker()
    chat_api.use(model=model)
    del chat_api.client.app.dependency_overrides[get_retriever]
    settings = RetrievalSettings(_env_file=None, **knobs)
    chat_api.client.app.state.retriever = Retriever(FakeEmbedder(), reranker, settings)
    return model, reranker


def citations(events: list[tuple[str, Any]]) -> list[dict]:
    return [c for name, data in events if name == "claim" for c in data["citations"]]


def test_a_document_citation_carries_its_document_in_the_stream_and_when_stored(
    chat_api: ChatApi, documents: Documents, regulation: dict
) -> None:
    alice = Person(uuid4(), "user-a", uuid4())
    sop = uuid4()
    documents.index(alice.tenant, alice.establishment, sop, 2, QUESTION, title="Cold holding SOP")
    documents.index(alice.tenant, alice.establishment, sop, 3, QUESTION, document_type="Sop")
    _, reranker = real_retrieval(chat_api)
    conversation = chat_api.create(alice)

    events = chat_api.events(alice, conversation, QUESTION)

    assert events[-1] == ("done", {})
    # One re-ranker call over the corpus and document candidates together.
    [scored] = reranker.calls
    assert scored.count(QUESTION) == 2
    [document] = [c for c in citations(events) if c["sourceType"] == "document"]
    quote = " ".join(QUESTION.split()[:6])
    assert document == {
        "id": document["id"],
        "sourceType": "document",
        "sourceTitle": None,
        "sourceVersion": None,
        "officialUrl": None,
        "documentId": str(sop),
        "documentTitle": "Cold holding SOP",
        "documentType": "Sop",
        "documentVersion": 3,
        "language": "en",
        "sectionPath": None,
        "text": QUESTION,
        "quote": quote,
        "quoteStart": 0,
        "quoteEnd": len(quote),
    }
    [law] = [c for c in citations(events) if c["text"] == QUESTION and c is not document]
    assert (law["sourceType"], law["sourceTitle"], law["sourceVersion"]) == (
        "regulation",
        "FDA Food Code",
        "2026",
    )
    assert "documentId" not in law
    stored = chat_api.get(alice, conversation).json()["messages"][-1]["claims"]
    streamed = [{k: v for k, v in data.items() if k != "language"} for _, data in events[:-1]]
    assert stored == streamed


def test_a_question_cites_only_its_own_establishments_documents(
    chat_api: ChatApi, documents: Documents, regulation: dict
) -> None:
    tenant_a, tenant_b, shared_establishment = uuid4(), uuid4(), uuid4()
    # Two establishments of one tenant, and another tenant holding the very same establishment id
    # (the ids come from the core): each holds a document that is the question word for word.
    callers = {
        Person(tenant_a, "user-a", shared_establishment): uuid4(),
        Person(tenant_a, "user-a", uuid4()): uuid4(),
        Person(tenant_b, "user-b", shared_establishment): uuid4(),
    }
    for caller, document in callers.items():
        documents.index(caller.tenant, caller.establishment, document, content=QUESTION)
    # Room for every candidate, so a leak is never cut off by a top k.
    model, _ = real_retrieval(
        chat_api, retrieval_top_k=50, retrieval_document_k=50, retrieval_document_top_k=49
    )

    for caller, document in callers.items():
        events = chat_api.events(caller, chat_api.create(caller), QUESTION)

        assert {c.get("documentId") for c in citations(events)} - {None} == {str(document)}
        assert len(model.prompts[-1][1].split('kind="document"')) == 2  # one excerpt in the prompt


def test_the_refusal_threshold_takes_the_best_score_of_either_source(
    chat_api: ChatApi, documents: Documents, regulation: dict
) -> None:
    # No corpus chunk is this question, so the corpus scores 0.5 at best: under the threshold.
    question = "Who signs the cooling log at the end of each kitchen shift?"
    alice = Person(uuid4(), "user-a", uuid4())
    model, _ = real_retrieval(chat_api)
    strict = AnswerSettings(
        anthropic_api_key=None,
        answer_min_rerank_score=0.75,
        answer_min_quote_words=4,
        _env_file=None,
    )
    chat = Chat(model, chat_api.rewrite, strict, HISTORY_SETTINGS)
    chat_api.client.app.dependency_overrides[get_chat] = lambda: chat
    conversation = chat_api.create(alice)

    assert chat_api.events(alice, conversation, question) == [("refusal", {"reason": "low_score"})]
    assert model.prompts == []

    log = uuid4()
    documents.index(alice.tenant, alice.establishment, log, content=question, document_type="Log")
    events = chat_api.events(alice, chat_api.create(alice), question)

    assert events[-1] == ("done", {})
    assert str(log) in {c.get("documentId") for c in citations(events)}


def test_the_chat_keeps_at_most_document_top_k_document_chunks(
    app_ai: Engine, documents: Documents, regulation: dict
) -> None:
    tenant, establishment = uuid4(), uuid4()
    for _ in range(6):
        documents.index(tenant, establishment, uuid4(), content=QUESTION)
    reranker = CountingReranker()
    retriever = Retriever(FakeEmbedder(), reranker, RetrievalSettings(_env_file=None))
    scope = jurisdictions_for("US")

    with tenant_transaction(app_ai, tenant) as connection:
        chat = retriever.trace(connection, QUESTION, scope, establishment_id=establishment)
        corpus_only = retriever.trace(connection, QUESTION, scope)

    assert len(chat.documents) == 5  # NORMETRY_RETRIEVAL_DOCUMENT_K candidates
    kinds = [chunk.source_type for chunk in chat.chunks]
    assert kinds.count("tenant_document") == 3  # NORMETRY_RETRIEVAL_DOCUMENT_TOP_K
    # A tie keeps the regulation first.
    assert (chat.chunks[0].content, chat.chunks[0].source_type) == (QUESTION, "regulation")
    assert [len(texts) for texts in reranker.calls] == [
        len(chat.fused[:30]) + 5,
        len(corpus_only.fused[:30]),
    ]
    assert corpus_only.documents == [] and "tenant_document" not in {
        chunk.source_type for chunk in corpus_only.chunks
    }


_KINDS = re.compile(r'<excerpt id="(E\d+)" kind="(\w+)"[^\n]*>\n(.*?)\n</excerpt>', re.DOTALL)


class KindClaims:
    """One claim per excerpt that names its kind, then one claim citing every excerpt."""

    def __init__(self) -> None:
        self.prompts: list[tuple[str, str]] = []

    async def stream_claims(self, system: str, user: str) -> AsyncGenerator[Any, None]:
        self.prompts.append((system, user))
        found = [
            (excerpt_id, kind, " ".join(normalize_text(unescape(content)).split()[:6]))
            for excerpt_id, kind, content in _KINDS.findall(user)
        ]
        for excerpt_id, kind, quote in found:
            yield {
                "text": f"The {kind} says so.",
                "citations": [{"excerpt_id": excerpt_id, "quote": quote}],
            }
        yield {
            "text": "Both say so.",
            "citations": [{"excerpt_id": e, "quote": quote} for e, _, quote in found],
        }
        yield ANSWERED


def kinds(claim: dict) -> set[str]:
    return {citation["sourceType"] for citation in claim["citations"]}


def test_without_documents_read_no_document_is_searched_cited_or_prompted(
    chat_api: ChatApi, documents: Documents, regulation: dict
) -> None:
    alice = Person(uuid4(), "user-a", uuid4(), documents_read=False)
    documents.index(alice.tenant, alice.establishment, uuid4(), content=QUESTION, title="SOP")
    model, reranker = real_retrieval(chat_api)

    events = chat_api.events(alice, chat_api.create(alice), QUESTION)

    assert events[-1] == ("done", {})
    assert {c["sourceType"] for c in citations(events)} == {"regulation"}
    # The document was never a candidate: the corpus's chunk is the only one that is the question.
    [scored] = reranker.calls
    assert scored.count(QUESTION) == 1
    assert 'kind="document"' not in model.prompts[-1][1]


def test_a_caller_without_documents_read_never_sees_a_stored_document_claim(
    chat_api: ChatApi, documents: Documents, regulation: dict
) -> None:
    alice = Person(uuid4(), "user-a", uuid4())
    documents.index(alice.tenant, alice.establishment, uuid4(), content=QUESTION, title="SOP")
    real_retrieval(chat_api, KindClaims())
    conversation = chat_api.create(alice)
    assert chat_api.events(alice, conversation, QUESTION)[-1] == ("done", {})
    # The same user, after documents.read was taken away.
    reader = replace(alice, documents_read=False)

    shown = chat_api.get(alice, conversation).json()["messages"][-1]["claims"]
    hidden = chat_api.get(reader, conversation).json()["messages"][-1]["claims"]

    assert [kinds(claim) for claim in shown] == [
        {"regulation"},
        {"document"},
        {"regulation", "document"},
    ]
    # A claim citing a document goes whole, also when it cites the regulation too.
    assert hidden == [shown[0]]
    # Nor does a follow-up's rewrite see the document claims of the history.
    chat_api.events(reader, conversation, "And for how long?")
    [prompt] = chat_api.rewrite.prompts
    assert "The regulation says so." in prompt
    assert "The document says so." not in prompt and "Both say so." not in prompt


OLD_CLAIMS = [
    {
        "text": "Hold cold food at 5 °C or below.",
        "citations": [
            {
                # Stored before #107: no sourceType and no document fields.
                "id": str(uuid4()),
                "sourceTitle": "FDA Food Code",
                "sourceVersion": "2022",
                "language": "en",
                "sectionPath": "3-501.16",
                "text": "Food shall be maintained at 5°C (41°F) or less.",
                "quote": "maintained at 5°C (41°F) or less",
                "quoteStart": 14,
                "quoteEnd": 46,
                "officialUrl": "https://example.org/food-code",
            }
        ],
    }
]


def test_an_answer_stored_before_document_citations_still_reads_and_continues(
    chat_api: ChatApi, app_ai: Engine
) -> None:
    alice = Person(uuid4(), "user-a", uuid4())
    conversation = chat_api.create(alice)
    with tenant_transaction(app_ai, alice.tenant) as connection:
        conversation_store.start_turn(
            connection,
            UUID(conversation),
            user_id=alice.user,
            establishment_id=alice.establishment,
            jurisdiction=alice.jurisdiction,
            question="How cold?",
            history_messages=4,
            documents=True,
        )
    # Its own transaction, so a later created_at than the question.
    with tenant_transaction(app_ai, alice.tenant) as connection:
        conversation_store.store_answer(
            connection,
            UUID(conversation),
            user_id=alice.user,
            kind="answer",
            reason=None,
            language="en",
            claims=OLD_CLAIMS,
        )

    assert chat_api.get(alice, conversation).json()["messages"][1]["claims"] == OLD_CLAIMS
    assert chat_api.events(alice, conversation, "And hot food?")[-1] == ("done", {})
    assert OLD_CLAIMS[0]["text"] in chat_api.rewrite.prompts[-1]
