"""Retrieval evals: recall@k and MRR of each retrieval step against the golden dataset.

A chunk is a hit for a quote when it contains the quote after `normalize_text`, the function the
dataset's quote check uses, so the labels survive a change of chunking. Each item is searched with
its turns joined into one query (the follow-up baseline of the phase 1 design) and the scope
`jurisdictions_for` gives its jurisdiction. Run it with `uv run pytest -m eval` (evals/README.md).
"""

import statistics
from collections.abc import Callable, Sequence
from dataclasses import dataclass

from normetry_ai.ingestion.normalize import normalize_text
from normetry_ai.retrieval.hybrid import Trace

K_VALUES = (1, 3, 5, 10)
STEPS = ("vector", "text", "fused", "reranked")
# The four corpus sources, by item jurisdiction and language.
GROUPS = {
    ("US", "en"): "FDA Food Code (US, en)",
    ("EU", "en"): "Reg. 852/2004 (EU, en)",
    ("EU", "pt"): "Reg. 852/2004 (EU, pt)",
    ("BR", "pt"): "RDC 216/2004 (BR, pt)",
}


def query_of(item: dict) -> str:
    """The search query of an item: its turns joined, as the follow-up baseline does."""
    return " ".join(item["turns"])


def quote_ranks(contents: Sequence[str], quotes: Sequence[str]) -> list[int | None]:
    """For each quote, the 1-based rank of the first chunk that contains it, or None."""
    chunks = [normalize_text(content) for content in contents]
    return [
        next((rank for rank, chunk in enumerate(chunks, 1) if normalize_text(q) in chunk), None)
        for q in quotes
    ]


def recall_at(ranks: Sequence[int | None], k: int) -> float:
    """Share of an item's quotes found in its top k chunks."""
    return sum(rank is not None and rank <= k for rank in ranks) / len(ranks)


def reciprocal_rank(ranks: Sequence[int | None], k: int) -> float:
    """1 / the rank of the first top-k chunk holding any of the item's quotes, else 0."""
    found = [rank for rank in ranks if rank is not None and rank <= k]
    return 1 / min(found) if found else 0.0


@dataclass(frozen=True)
class ItemResult:
    item: dict
    trace: Trace
    # Per step, the rank of each expected quote (same order as the item's provisions).
    ranks: dict[str, list[int | None]]

    @property
    def answerable(self) -> bool:
        return self.item["expected_behavior"] == "answer"


def evaluate(items: Sequence[dict], run: Callable[[dict], Trace]) -> list[ItemResult]:
    """Run retrieval on every item and rank its quotes in each step's output."""
    results = []
    for item in items:
        trace = run(item)
        quotes = [provision["quote"] for provision in item["provisions"]]
        steps = {
            "vector": [trace.content[chunk_id] for chunk_id in trace.vector],
            "text": [trace.content[chunk_id] for chunk_id in trace.text],
            "fused": [trace.content[chunk_id] for chunk_id in trace.fused],
            "reranked": [chunk.content for chunk in trace.chunks],
        }
        ranks = {step: quote_ranks(contents, quotes) for step, contents in steps.items()}
        results.append(ItemResult(item, trace, ranks))
    return results


def metrics(results: Sequence[ItemResult], step: str) -> dict[str, float]:
    """Mean recall@k for each k and MRR@10 over the answerable results."""
    answerable = [result for result in results if result.answerable]
    if not answerable:
        return {}
    scores = {
        f"recall@{k}": statistics.fmean(recall_at(r.ranks[step], k) for r in answerable)
        for k in K_VALUES
    }
    scores["MRR@10"] = statistics.fmean(reciprocal_rank(r.ranks[step], 10) for r in answerable)
    return scores


def out_of_scope(result: ItemResult) -> list[str]:
    """Returned chunks of another jurisdiction, or (EU) of the other language version."""
    item = result.item
    return [
        f"{chunk.jurisdiction}/{chunk.language} {chunk.section_path}"
        for chunk in result.trace.chunks
        if (chunk.jurisdiction, chunk.language) != (item["jurisdiction"], item["language"])
    ]


def _table(rows: list[tuple[str, dict[str, float]]]) -> list[str]:
    columns = [f"recall@{k}" for k in K_VALUES] + ["MRR@10"]
    lines = ["| | " + " | ".join(columns) + " |", "|---" * (len(columns) + 1) + "|"]
    for label, scores in rows:
        lines.append(f"| {label} | " + " | ".join(f"{scores[c]:.3f}" for c in columns) + " |")
    return lines


def _spread(values: Sequence[float]) -> str:
    if not values:
        return "none"
    return f"min {min(values):.3f}, median {statistics.median(values):.3f}, max {max(values):.3f}"


def _best_scores(results: Sequence[ItemResult]) -> list[float]:
    return [result.trace.chunks[0].score for result in results if result.trace.chunks]


def render(results: Sequence[ItemResult], header: Sequence[str], candidates: int) -> str:
    """The Markdown report: metrics per step and per regulation, misses, scope and timings.

    `candidates` is how many fused chunks the re-ranker scored: their recall is its ceiling.
    """
    answerable = [result for result in results if result.answerable]
    quotes = sum(len(result.item["provisions"]) for result in answerable)
    ceiling = statistics.fmean(recall_at(r.ranks["fused"], candidates) for r in answerable)
    lines = ["# Retrieval eval", "", *header, ""]
    lines += [
        f"{len(results)} items: {len(answerable)} answerable ({quotes} quotes), scored below;"
        f" {len(results) - len(answerable)} refusals, checked for scope only. Metrics are means"
        " over answerable items: recall@k is the share of an item's quotes found in its top k"
        " chunks, MRR@10 uses the first top-10 chunk that holds any of its quotes.",
        "",
        "## Each step, all regulations",
        "",
        *_table([(step, metrics(results, step)) for step in STEPS]),
        "",
        f"The re-ranker scores the fused top {candidates}, which hold {ceiling:.3f} of the quotes"
        " (the most its top 10 can reach).",
        "",
        "## Each step, per regulation (recall@10 / MRR@10)",
        "",
        "| | " + " | ".join(STEPS) + " |",
        "|---" * (len(STEPS) + 1) + "|",
    ]
    for (jurisdiction, language), label in GROUPS.items():
        group = [
            r
            for r in results
            if (r.item["jurisdiction"], r.item["language"]) == (jurisdiction, language)
        ]
        count = sum(result.answerable for result in group)
        if count:
            cells = []
            for step in STEPS:
                scores = metrics(group, step)
                cells.append(f"{scores['recall@10']:.3f} / {scores['MRR@10']:.3f}")
            lines.append(f"| {label}, {count} items | " + " | ".join(cells) + " |")
    lines.append("")

    lines += ["## Quotes missing from the re-ranked top 10", ""]
    misses = [
        (result, provision, fused)
        for result in answerable
        for provision, rank, fused in zip(
            result.item["provisions"], result.ranks["reranked"], result.ranks["fused"], strict=True
        )
        if rank is None or rank > 10
    ]
    if misses:
        lines += ["| item | section | rank after fusion | quote |", "|---|---|---|---|"]
        for result, provision, fused in misses:
            quote = " ".join(provision["quote"].split()[:12]) + " ..."
            where = str(fused) if fused is not None else "not retrieved"
            lines.append(
                f"| {result.item['id']} | {provision['section_path']} | {where} | {quote} |"
            )
    else:
        lines.append("None.")
    lines.append("")

    leaks = [(result.item["id"], out_of_scope(result)) for result in results]
    leaks = [(item_id, chunks) for item_id, chunks in leaks if chunks]
    lines += [
        "## Scope",
        "",
        "Chunks returned outside the item's jurisdiction (or, for EU, outside the question's"
        f" language): {sum(len(chunks) for _, chunks in leaks)}.",
        *(f"- {item_id}: {', '.join(chunks)}" for item_id, chunks in leaks),
        "",
        "## Best re-ranker score (input for the refusal threshold)",
        "",
        f"- answerable items: {_spread(_best_scores(answerable))}",
        f"- refusals: {_spread(_best_scores([r for r in results if not r.answerable]))}",
        "",
        "## Time per question (seconds)",
        "",
        "| step | mean | max |",
        "|---|---|---|",
    ]
    for step in results[0].trace.seconds if results else ():
        values = [result.trace.seconds[step] for result in results]
        lines.append(f"| {step} | {statistics.fmean(values):.3f} | {max(values):.3f} |")
    return "\n".join(lines) + "\n"
