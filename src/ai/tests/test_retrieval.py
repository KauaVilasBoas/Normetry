"""Hybrid retrieval: the jurisdiction policy, language detection, fusion, and the corpus filter.

The database tests (marked `integration`) use fake models: every source holds a chunk that is
exactly the question, so a chunk the filter let through would rank first.
"""

import ast
import hashlib
from collections.abc import Sequence
from pathlib import Path
from uuid import UUID, uuid4

import pytest
from sqlalchemy import Engine, text

from normetry_ai.db import tenant_transaction
from normetry_ai.evals.dataset import load_items
from normetry_ai.ingestion.corpus import Section
from normetry_ai.ingestion.ingest import ingest_source
from normetry_ai.retrieval.hybrid import Retriever, reciprocal_rank_fusion
from normetry_ai.retrieval.language import detect_language
from normetry_ai.retrieval.policy import JurisdictionScope, corpus_versions, jurisdictions_for
from normetry_ai.settings import RetrievalSettings

PACKAGE = Path(__file__).resolve().parents[1] / "normetry_ai"

BR_QUESTION = "Qual a temperatura de conservação dos alimentos preparados no balcão térmico?"
EU_PT_QUESTION = "Os resíduos alimentares têm de ser retirados das salas onde estão os alimentos?"
EU_EN_QUESTION = "Must food waste be removed from rooms where food is present?"
US_QUESTION = "How long can we keep the soup on the steam table?"
QUESTIONS = (BR_QUESTION, EU_PT_QUESTION, EU_EN_QUESTION, US_QUESTION)


# --- Policy, language and fusion ----------------------------------------------------------------


def test_the_policy_returns_the_establishment_jurisdiction_only() -> None:
    assert jurisdictions_for("BR") == JurisdictionScope(("BR",))
    with pytest.raises(ValueError, match="unknown jurisdiction"):
        jurisdictions_for("br")


@pytest.mark.parametrize(
    ("jurisdiction", "language", "versions"),
    [
        ("EU", "pt", [("EU", "pt")]),
        ("EU", "en", [("EU", "en")]),
        ("BR", "pt", [("BR", "pt")]),
        ("BR", "en", [("BR", "pt")]),
        ("US", "pt", [("US", "en")]),
    ],
)
def test_one_language_version_is_searched_only_where_there_are_several(
    jurisdiction: str, language: str, versions: list[tuple[str, str]]
) -> None:
    assert corpus_versions(jurisdictions_for(jurisdiction), language) == versions


def test_only_the_policy_builds_a_jurisdiction_scope() -> None:
    builders = []
    for path in PACKAGE.rglob("*.py"):
        for node in ast.walk(ast.parse(path.read_text(encoding="utf-8"))):
            if (
                isinstance(node, ast.Call)
                and getattr(node.func, "id", getattr(node.func, "attr", None))
                == "JurisdictionScope"
            ):
                builders.append(path.relative_to(PACKAGE).as_posix())

    assert builders == ["retrieval/policy.py"]


def test_retrieval_refuses_a_jurisdiction_list_built_elsewhere() -> None:
    retriever = Retriever(FakeEmbedder(), FakeReranker(), RetrievalSettings(_env_file=None))

    with pytest.raises(TypeError, match="jurisdictions_for"):
        retriever.retrieve(None, BR_QUESTION, ["BR"])  # type: ignore[arg-type]


def test_every_dataset_question_is_detected_in_its_language() -> None:
    wrong = [
        (item["id"], turn)
        for item in load_items()
        for turn in (*item["turns"], " ".join(item["turns"]))
        if detect_language(turn) != item["language"]
    ]

    assert wrong == []


@pytest.mark.parametrize(
    ("question", "language"),
    [(BR_QUESTION, "pt"), (EU_PT_QUESTION, "pt"), (EU_EN_QUESTION, "en"), ("Salmonella?", "en")],
)
def test_detect_language(question: str, language: str) -> None:
    assert detect_language(question) == language


def test_reciprocal_rank_fusion_rewards_agreement() -> None:
    # b, second in one list and first in the other, beats a, first in one list only.
    assert reciprocal_rank_fusion([["a", "b"], ["b", "c"]], k=60) == ["b", "a", "c"]
    assert reciprocal_rank_fusion([["x", "y"], []], k=60) == ["x", "y"]


# --- The corpus filter, on a real database --------------------------------------------------------


class FakeEmbedder:
    """Deterministic vectors from each text's hash: a query matches its own text exactly."""

    name = "fake-retrieval"

    def embed_documents(self, texts: Sequence[str]) -> list[list[float]]:
        return [[(b + 1) / 256 for b in hashlib.sha256(t.encode()).digest()] * 32 for t in texts]

    def embed_query(self, text: str) -> list[float]:
        return self.embed_documents([text])[0]


class FakeReranker:
    name = "fake"

    def score(self, query: str, texts: Sequence[str]) -> list[float]:
        return [1.0 if text == query else 0.5 for text in texts]


SOURCES = {
    ("US", "en"): "FDA Food Code",
    ("EU", "en"): "Regulation (EC) No 852/2004",
    ("EU", "pt"): "Regulamento (CE) n.º 852/2004",
    ("BR", "pt"): "RDC 216/2004",
}


@pytest.fixture(scope="module")
def sources(corpus: Engine) -> dict[tuple[str, str], dict]:
    """One source per jurisdiction and language, each with one article per test question."""
    sources = {}
    for (jurisdiction, language), title in SOURCES.items():
        source = {
            "id": f"test-{uuid4()}",
            "jurisdiction": jurisdiction,
            "code": "code",
            "title": title,
            "version": "2026-01-01",
            "language": language,
            "official_url": f"https://example.org/{jurisdiction}/{language}",
        }
        sections = [
            Section(source["id"], jurisdiction, language, source["version"], f"Article {n}", q)
            for n, q in enumerate(QUESTIONS, start=1)
        ]
        ingest_source(corpus, source, sections, FakeEmbedder())
        sources[jurisdiction, language] = source
    return sources


@pytest.fixture(scope="module")
def retriever() -> Retriever:
    return Retriever(FakeEmbedder(), FakeReranker(), RetrievalSettings(_env_file=None))


def corpus_source_id(engine: Engine, manifest_id: str) -> UUID:
    with engine.begin() as connection:
        return connection.execute(
            text("SELECT id FROM ai.sources WHERE manifest_id = :id"), {"id": manifest_id}
        ).scalar_one()


@pytest.mark.integration
@pytest.mark.parametrize(
    ("question", "jurisdiction", "language"),
    [
        (BR_QUESTION, "BR", "pt"),
        (EU_PT_QUESTION, "EU", "pt"),
        (EU_EN_QUESTION, "EU", "en"),
        (US_QUESTION, "US", "en"),
        # One language version only: searched whatever the question's language.
        (US_QUESTION, "BR", "pt"),
        (BR_QUESTION, "US", "en"),
    ],
)
def test_retrieval_returns_only_the_scope_jurisdiction_and_version(
    app_ai: Engine,
    sources: dict[tuple[str, str], dict],
    retriever: Retriever,
    question: str,
    jurisdiction: str,
    language: str,
) -> None:
    with tenant_transaction(app_ai, uuid4()) as connection:
        chunks = retriever.retrieve(connection, question, jurisdictions_for(jurisdiction))

    # Every other source holds the question word for word, so a leak would rank first.
    assert chunks
    assert {(chunk.jurisdiction, chunk.language) for chunk in chunks} == {(jurisdiction, language)}
    top, source = chunks[0], sources[jurisdiction, language]
    assert (top.content, top.score) == (question, 1.0)
    assert top.source_id == corpus_source_id(app_ai, source["id"])
    assert (top.source_title, top.source_version, top.official_url, top.section_path) == (
        source["title"],
        source["version"],
        source["official_url"],
        f"Article {QUESTIONS.index(question) + 1}",
    )


@pytest.mark.integration
def test_a_br_question_never_returns_fda_or_eu_chunks(
    app_ai: Engine, sources: dict[tuple[str, str], dict], retriever: Retriever
) -> None:
    # Asked in English, it still gets the Portuguese RDC text, never the English FDA or EU text.
    with tenant_transaction(app_ai, uuid4()) as connection:
        portuguese = retriever.retrieve(connection, BR_QUESTION, jurisdictions_for("BR"))
        english = retriever.retrieve(connection, US_QUESTION, jurisdictions_for("BR"))

    for chunks in (portuguese, english):
        assert chunks and {(c.jurisdiction, c.language) for c in chunks} == {("BR", "pt")}
    assert english[0].content == US_QUESTION


@pytest.mark.integration
def test_a_tenant_chunk_is_never_returned_as_regulation_text(
    app_ai: Engine, sources: dict[tuple[str, str], dict], retriever: Retriever
) -> None:
    # A tenant row may reuse a corpus source id (the foreign key skips tenant rows); this one is
    # the question word for word, with its exact embedding.
    tenant = uuid4()
    with tenant_transaction(app_ai, tenant) as connection:
        tenant_chunk = connection.execute(
            text(
                """
                INSERT INTO ai.chunks (tenant_id, source_type, source_id, source_version, language,
                                       content, content_hash, embedding)
                VALUES (:tenant_id, 'tenant_document', :source_id, 1, 'pt', :content, :hash,
                        CAST(:embedding AS public.vector))
                RETURNING id
                """
            ),
            {
                "tenant_id": tenant,
                "source_id": corpus_source_id(app_ai, sources["BR", "pt"]["id"]),
                "content": BR_QUESTION,
                "hash": uuid4().hex,
                "embedding": str(FakeEmbedder().embed_query(BR_QUESTION)),
            },
        ).scalar_one()

    with tenant_transaction(app_ai, tenant) as connection:
        trace = retriever.trace(connection, BR_QUESTION, jurisdictions_for("BR"))

    assert tenant_chunk not in {*trace.vector, *trace.text}
    assert trace.chunks[0].jurisdiction == "BR"


@pytest.mark.integration
def test_retrieval_runs_only_inside_a_tenant_transaction(
    app_ai: Engine, retriever: Retriever
) -> None:
    with pytest.raises(RuntimeError, match="tenant_transaction"), app_ai.begin() as connection:
        retriever.retrieve(connection, BR_QUESTION, jurisdictions_for("BR"))
