"""Retrieval eval scoring of document questions on fabricated traces: no database, no model
(evals/retrieval.py)."""

from uuid import uuid4

from fakes import document_chunk, regulation_chunk

from normetry_ai.evals.retrieval import (
    evaluate,
    evaluate_documents,
    foreign_documents,
    metrics,
    out_of_scope,
    render_documents,
)
from normetry_ai.retrieval.hybrid import Trace
from normetry_ai.retrieval.models import RetrievedChunk

RULE = "Cold food shall be held at 5°C or below"
OWN = uuid4()


# Regulation 852/2004 in Portuguese, the items' corpus version.
EU_PT = {"jurisdiction": "EU", "language": "pt", "section_path": "Annex II"}


def trace(chunks: list[RetrievedChunk], candidates: list[RetrievedChunk] = ()) -> Trace:
    return Trace(
        language="pt",
        vector=[],
        text=[],
        fused=[],
        documents=[chunk.chunk_id for chunk in candidates],
        chunks=chunks,
        content={chunk.chunk_id: chunk.content for chunk in [*chunks, *candidates]},
        seconds={"rerank": 0.1},
    )


def item(item_id: str, quotes: list[str], provisions: list[str], behavior: str = "answer"):
    return {
        "id": item_id,
        "establishment": "Bistro Lisboa",
        "jurisdiction": "EU",
        "language": "pt",
        "expected_behavior": behavior,
        "document_quotes": [{"file": "plan.txt", "quote": quote} for quote in quotes],
        "provisions": [{"section_path": "Annex II", "quote": quote} for quote in provisions],
    }


def test_document_quotes_count_only_in_document_chunks_and_rules_only_in_regulation() -> None:
    # The rule also appears in the plan: a document chunk holding it is not a corpus hit.
    plan = document_chunk(
        f"Our plan copies the rule: {RULE}. The slicer is cleaned weekly.", document_id=OWN
    )
    missed = document_chunk("The slicer log for October.", document_id=OWN)
    traces = {
        "eu-001": trace(
            [plan, regulation_chunk("other text", **EU_PT), regulation_chunk(RULE, **EU_PT)],
            [plan, missed],
        ),
        "eu-002": trace([regulation_chunk("x", **EU_PT)], [missed]),
        "sec-001": trace([document_chunk("Austin's pest report", title="Pest report")]),
    }
    items = [
        item("eu-001", ["slicer is cleaned weekly"], [RULE]),
        item("eu-002", ["slicer log for October", "never there"], []),
        item("sec-001", [], [], "refuse"),
    ]

    results = evaluate_documents(items, lambda it: traces[it["id"]])

    first, second, refusal = results
    assert first.ranks == {"candidates": [1], "documents": [1], "corpus": [3]}
    assert second.ranks == {"candidates": [1, None], "documents": [None, None], "corpus": []}
    scores = metrics(results, "documents")
    assert (scores["recall@1"], scores["recall@10"], scores["MRR@10"]) == (0.5, 0.5, 0.5)
    assert metrics(results, "candidates")["recall@10"] == 0.75
    assert foreign_documents(first.trace, {OWN}) == []
    assert foreign_documents(refusal.trace, {OWN}) == [
        f"Pest report ({refusal.trace.chunks[0].document_id})"
    ]
    # A document chunk has no jurisdiction, and is not out of the corpus scope.
    assert out_of_scope(first) == []

    corpus = [{**item("eu-001", [], [RULE]), "type": "direct"}]
    without = evaluate(corpus, lambda _: trace([regulation_chunk(RULE, **EU_PT)]))
    crowded = evaluate(corpus, lambda _: trace([plan, regulation_chunk(RULE, **EU_PT)]))
    assert (without[0].ranks["reranked"], crowded[0].ranks["reranked"]) == ([1], [2])

    report = render_documents(results, without, crowded, ["Pest report (x)"], 3)
    assert "| Bistro Lisboa | 2 | 0.500 / 0.500 | 0.50 |" in report
    assert "| eu-002 | plan.txt | 1 | slicer log for October ... |" in report
    assert "| eu-002 | plan.txt | not a candidate | never there ... |" in report
    assert "Items whose recall@10 dropped: none." in report
    assert "any result (document questions and corpus questions with documents): 1." in report
