"""Generation eval scoring on fabricated results: no database, no model (evals/generation.py)."""

from decimal import Decimal

from normetry_ai.evals.generation import (
    REVIEW_PENDING,
    ModelAnswer,
    PassResult,
    jsonl,
    metrics,
    render,
    rewrite_comparison,
)

GOLD = "Time/temperature control for safety food shall be maintained at 5°C (41°F) or less"


def item(item_id: str, kind: str, behavior: str, jurisdiction: str = "US", language: str = "en"):
    provisions = [{"source_id": "x", "section_path": "3-501.16", "quote": GOLD}]
    return {
        "id": item_id,
        "type": kind,
        "jurisdiction": jurisdiction,
        "language": language,
        "turns": ["q"],
        "expected_behavior": behavior,
        "expected_answer": "a",
        "provisions": provisions if behavior == "answer" else [],
    }


def answered(jurisdiction: str, language: str, content: str, **kwargs) -> ModelAnswer:
    citation = {"chunk_id": "c1", "jurisdiction": jurisdiction, "language": language, "quote": "q"}
    return ModelAnswer(
        "answered",
        claims=[{"text": "t", "citations": [citation]}],
        cited={"c1": content},
        raw_claims=2,
        first_claim_seconds=1.0,
        answer_seconds=2.0,
        cost_usd=Decimal("0.01"),
        **kwargs,
    )


def run(it: dict, answer: ModelAnswer, rewrite: str = "none", retrieved=()) -> PassResult:
    return PassResult(it, rewrite, "q", list(retrieved), 0.5, 0.5, {"m": answer}, Decimal("0.001"))


def test_generation_metrics() -> None:
    follow_up = item("us-017", "follow_up", "answer")
    passes = [
        # Answered from the gold chunk, one of two streamed claims shown.
        run(item("us-001", "direct", "answer"), answered("US", "en", f"... {GOLD} ...")),
        # Answered from the right jurisdiction in the wrong language: out of scope, no gold quote.
        run(item("eu-011", "direct", "answer", "EU", "pt"), answered("EU", "en", "other text")),
        run(item("us-010", "not_covered", "refuse"), ModelAnswer("refused", "no_supported_claims")),
        # A refusal expected, a foreign citation shown.
        run(item("us-015", "wrong_jurisdiction", "refuse"), answered("BR", "pt", "x")),
        run(follow_up, answered("US", "en", GOLD), "model", [GOLD]),
        run(follow_up, ModelAnswer("unavailable", stop_reason="refusal"), "baseline", ["x"]),
    ]

    m = metrics(passes, "m")
    assert m["answered"] == (3, 3)
    assert m["refused"] == (1, 2)
    assert m["refused not_covered"] == (1, 1)
    assert m["refused wrong_jurisdiction"] == (0, 1)
    assert m["out of scope"] == 2
    assert (m["shown"], m["dropped"]) == (4, 4)
    assert (m["model refusals"], m["unavailable"]) == (0, 0)  # the baseline pass is not scored
    assert m["gold coverage"] == 2 / 3
    assert m["citing gold"] == (2, 3)
    assert m["first claim"] == (2.0, 2.0)
    assert m["cost"] == Decimal("0.04")

    comparison = rewrite_comparison(passes, "m")
    assert comparison["model"]["recall@10"] == 1.0
    assert comparison["baseline"]["recall@10"] == 0.0
    assert comparison["model"]["correct"] == (1, 1)
    assert comparison["baseline"]["correct"] == (0, 1)

    report = render(passes, "m", ["- header"])
    assert REVIEW_PENDING in report
    assert "unavailable, stop: refusal" in report
    assert len(jsonl(passes, "m").splitlines()) == len(passes)
