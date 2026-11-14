"""Generation evals: what the real answer pipeline did on the golden dataset, per chat model.

The runner (tests/test_generation_eval.py, paid, on demand) fills in one PassResult per item and
rewrite mode, holding one ModelAnswer per chat model; this module scores and renders them, with
no database and no model. A follow-up runs twice: rewritten by the rewrite model ("model", the
default `followup_rewrite`, which the main metrics use) and by the free baseline ("baseline"). A
one-turn item runs once ("none"). Faithfulness is not scored here: a Claude Code session judges it
from the JSONL (evals/README.md).
"""

import json
import statistics
from collections.abc import Callable, Sequence
from dataclasses import dataclass, field
from decimal import Decimal

from normetry_ai.evals.retrieval import quote_ranks, recall_at
from normetry_ai.retrieval.policy import jurisdictions_for

REVIEW_PENDING = "Faithfulness and citation review: pending (judged in a Claude Code session)"
REFUSAL_TYPES = ("not_covered", "wrong_jurisdiction", "follow_up")


@dataclass
class ModelAnswer:
    """One chat model's answer in one pass."""

    outcome: str  # "answered", "refused" or "unavailable"
    refusal_reason: str | None = None
    # The shown claims: text, and citations with chunk_id, source, section_path, jurisdiction,
    # language and quote.
    claims: list[dict] = field(default_factory=list)
    cited: dict[str, str] = field(default_factory=dict)  # chunk id -> text of each cited chunk
    raw_claims: int = 0  # claim objects the model streamed, before the guard
    stop_reason: str | None = None  # set when the model stopped other than with end_turn
    input_tokens: int = 0
    output_tokens: int = 0
    cost_usd: Decimal = Decimal(0)
    first_claim_seconds: float | None = None  # from the start of answer() to its first claim
    answer_seconds: float = 0.0  # from the start of answer() to its final event

    @property
    def dropped(self) -> int:
        """Claims the guard dropped."""
        return self.raw_claims - len(self.claims)


@dataclass
class PassResult:
    """One item with one rewrite mode: the question retrieval saw, and each model's answer."""

    item: dict
    rewrite: str  # "none", "model" or "baseline"
    standalone: str
    retrieved: list[str]  # the retrieved chunks' text, best first
    rewrite_seconds: float
    retrieval_seconds: float
    answers: dict[str, ModelAnswer]
    rewrite_cost_usd: Decimal = Decimal(0)  # counted once, shared by the models

    def time_to_first_claim(self, model: str) -> float | None:
        """Rewrite + retrieval + answer() to its first claim; None for an answer with no claim."""
        first = self.answers[model].first_claim_seconds
        return None if first is None else self.rewrite_seconds + self.retrieval_seconds + first

    def total_seconds(self, model: str) -> float:
        return self.rewrite_seconds + self.retrieval_seconds + self.answers[model].answer_seconds


def correct(item: dict, answer: ModelAnswer) -> bool:
    """Answered when the item expects an answer, refused when it expects a refusal."""
    return answer.outcome == ("answered" if item["expected_behavior"] == "answer" else "refused")


def out_of_scope(item: dict, answer: ModelAnswer) -> list[dict]:
    """Citations outside `jurisdictions_for(item jurisdiction)`, or, for EU, in another language."""
    allowed = jurisdictions_for(item["jurisdiction"]).jurisdictions
    return [
        citation
        for claim in answer.claims
        for citation in claim["citations"]
        if citation["jurisdiction"] not in allowed
        or (item["jurisdiction"] == "EU" and citation["language"] != item["language"])
    ]


def gold_found(item: dict, answer: ModelAnswer) -> list[bool]:
    """For each gold quote of the item, whether one of the cited chunks holds it."""
    quotes = [provision["quote"] for provision in item["provisions"]]
    return [rank is not None for rank in quote_ranks(list(answer.cited.values()), quotes)]


def percentile(values: Sequence[float], q: int) -> float | None:
    if len(values) < 2:
        return values[0] if values else None
    return statistics.quantiles(values, n=100, method="inclusive")[q - 1]


def metrics(passes: Sequence[PassResult], model: str) -> dict:
    """One model's success measures over the main passes (a follow-up's "model" rewrite)."""
    runs = [(p.item, p.answers[model], p) for p in passes if p.rewrite != "baseline"]
    answerable = [run for run in runs if run[0]["expected_behavior"] == "answer"]
    refusals = [run for run in runs if run[0]["expected_behavior"] == "refuse"]
    answered = [run for run in answerable if run[1].outcome == "answered"]
    found = [gold_found(item, answer) for item, answer, _ in answered]
    first = [t for *_, p in runs if (t := p.time_to_first_claim(model)) is not None]
    total = [p.total_seconds(model) for *_, p in runs]
    cost = sum((answer.cost_usd for _, answer, _ in runs), Decimal(0))

    def count(check: Callable[[dict, ModelAnswer], object]) -> int:
        return sum(bool(check(item, answer)) for item, answer, _ in runs)

    def refused(kind: str | None) -> tuple[int, int]:
        group = [run for run in refusals if kind in (None, run[0]["type"])]
        return sum(correct(item, answer) for item, answer, _ in group), len(group)

    return {
        "answered": (len(answered), len(answerable)),
        "refused": refused(None),
        **{f"refused {kind}": refused(kind) for kind in REFUSAL_TYPES},
        "out of scope": sum(len(out_of_scope(item, answer)) for item, answer, _ in runs),
        "shown": sum(len(answer.claims) for _, answer, _ in runs),
        "dropped": sum(answer.dropped for _, answer, _ in runs),
        "model refusals": count(lambda _, answer: answer.stop_reason == "refusal"),
        "unavailable": count(lambda _, answer: answer.outcome == "unavailable"),
        "gold coverage": statistics.fmean(statistics.fmean(f) for f in found) if found else None,
        "citing gold": (sum(any(f) for f in found), len(found)),
        "first claim": (percentile(first, 50), percentile(first, 95)),
        "total": (percentile(total, 50), percentile(total, 95)),
        "cost": cost,
        "cost per answer": cost / len(runs) if runs else None,
        "tokens": (
            sum(answer.input_tokens for _, answer, _ in runs),
            sum(answer.output_tokens for _, answer, _ in runs),
        ),
    }


def rewrite_comparison(passes: Sequence[PassResult], model: str) -> dict[str, dict]:
    """Per rewrite mode, over the follow-ups run with both: retrieval recall@10 of the gold
    quotes, this model's correct outcomes and the rewrite cost."""
    by_mode = {
        mode: {p.item["id"]: p for p in passes if p.rewrite == mode}
        for mode in ("model", "baseline")
    }
    ids = sorted(by_mode["model"].keys() & by_mode["baseline"].keys())
    comparison = {}
    for mode, runs in by_mode.items():
        paired = [runs[item_id] for item_id in ids]
        recall = [
            recall_at(quote_ranks(p.retrieved, [q["quote"] for q in p.item["provisions"]]), 10)
            for p in paired
            if p.item["expected_behavior"] == "answer"
        ]
        comparison[mode] = {
            "recall@10": statistics.fmean(recall) if recall else None,
            "correct": (sum(correct(p.item, p.answers[model]) for p in paired), len(paired)),
            "rewrite cost": sum((p.rewrite_cost_usd for p in paired), Decimal(0)),
        }
    return comparison


def _fraction(value: tuple[int, int]) -> str:
    n, total = value
    return f"{n}/{total} ({n / total:.0%})" if total else "0/0"


def _seconds(value: float | None) -> str:
    return "n/a" if value is None else f"{value:.2f} s"


def _usd(value: Decimal | None) -> str:
    return "n/a" if value is None else f"US$ {value:.4f}"


def _outcome(answer: ModelAnswer) -> str:
    text = answer.outcome + (f" ({answer.refusal_reason})" if answer.refusal_reason else "")
    return text + (f", stop: {answer.stop_reason}" if answer.stop_reason else "")


def render(passes: Sequence[PassResult], model: str, header: Sequence[str]) -> str:
    """The Markdown report of one model: success measures, rewrite comparison, every pass."""
    m = metrics(passes, model)
    rows = [
        ("Answerable items answered", _fraction(m["answered"]), ""),
        ("Expected refusals refused", _fraction(m["refused"]), ""),
        ("- not_covered", _fraction(m["refused not_covered"]), "≥ 90%"),
        ("- wrong_jurisdiction", _fraction(m["refused wrong_jurisdiction"]), ""),
        ("- follow-up", _fraction(m["refused follow_up"]), ""),
        ("Wrong-jurisdiction citations", str(m["out of scope"]), "0"),
        ("Claims shown / dropped by the guard", f"{m['shown']} / {m['dropped']}", ""),
        ("Model refusals (stop_reason refusal)", str(m["model refusals"]), ""),
        ("Unavailable answers", str(m["unavailable"]), ""),
        (
            "Gold quotes held by the cited chunks (answered answerable items)",
            "n/a" if m["gold coverage"] is None else f"{m['gold coverage']:.3f}",
            "",
        ),
        ("Answered answerable items citing a gold quote", _fraction(m["citing gold"]), ""),
        ("Time to first claim p50 / p95", " / ".join(map(_seconds, m["first claim"])), "p95 < 6 s"),
        ("Total time p50 / p95", " / ".join(map(_seconds, m["total"])), ""),
        ("Cost / per answer", f"{_usd(m['cost'])} / {_usd(m['cost per answer'])}", ""),
        ("Tokens in / out", f"{m['tokens'][0]} / {m['tokens'][1]}", ""),
    ]
    lines = [f"# Generation eval: {model}", "", *header, ""]
    lines += [
        "## Success measures",
        "",
        "Over one pass per item: a follow-up's question rewritten by the rewrite model (the"
        " default). A citation is out of scope when its chunk is outside the item's"
        " jurisdiction or, for EU items, in the other language.",
        "",
        "| measure | value | target |",
        "|---|---|---|",
        *(f"| {label} | {value} | {target} |" for label, value, target in rows),
        "",
        "## Follow-up rewrite: model against baseline",
        "",
        "The dataset has no assistant turns, so a follow-up's history is its earlier user turns"
        " only. Recall@10 is of the gold quotes in the retrieved chunks (answerable follow-ups).",
        "",
        "| rewrite | retrieval recall@10 | correct outcome | rewrite cost |",
        "|---|---|---|---|",
    ]
    for mode, scores in rewrite_comparison(passes, model).items():
        recall = "n/a" if scores["recall@10"] is None else f"{scores['recall@10']:.3f}"
        lines.append(
            f"| {mode} | {recall} | {_fraction(scores['correct'])}"
            f" | {_usd(scores['rewrite cost'])} |"
        )
    lines += [
        "",
        "## Items",
        "",
        "| item | type | rewrite | expected | outcome | shown / dropped | first claim | cost |",
        "|---|---|---|---|---|---|---|---|",
    ]
    for p in passes:
        answer = p.answers[model]
        lines.append(
            f"| {p.item['id']} | {p.item['type']} | {p.rewrite} | {p.item['expected_behavior']}"
            f" | {_outcome(answer)} | {len(answer.claims)} / {answer.dropped}"
            f" | {_seconds(p.time_to_first_claim(model))} | {_usd(answer.cost_usd)} |"
        )
    lines += ["", "## Review", "", REVIEW_PENDING]
    return "\n".join(lines) + "\n"


def jsonl(passes: Sequence[PassResult], model: str) -> str:
    """One JSON line per item and pass, for the faithfulness review: what was asked, expected,
    and shown, with each citation's quote and place (not the chunk text)."""
    lines = []
    for p in passes:
        item, answer = p.item, p.answers[model]
        record = {
            **{key: item[key] for key in ("id", "type", "jurisdiction", "language", "turns")},
            "rewrite": p.rewrite,
            "standalone_question": p.standalone,
            "expected_behavior": item["expected_behavior"],
            "expected_answer": item["expected_answer"],
            "gold_quotes": item["provisions"],
            "outcome": answer.outcome,
            "refusal_reason": answer.refusal_reason,
            "stop_reason": answer.stop_reason,
            "claims": answer.claims,
            "claims_dropped": answer.dropped,
            "input_tokens": answer.input_tokens,
            "output_tokens": answer.output_tokens,
            "cost_usd": float(answer.cost_usd),
            "rewrite_cost_usd": float(p.rewrite_cost_usd),
            "seconds": {
                "rewrite": p.rewrite_seconds,
                "retrieval": p.retrieval_seconds,
                "time_to_first_claim": p.time_to_first_claim(model),
                "total": p.total_seconds(model),
            },
        }
        lines.append(json.dumps(record, ensure_ascii=False))
    return "".join(line + "\n" for line in lines)
