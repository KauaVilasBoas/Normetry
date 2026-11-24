"""The simulated inspection as a LangGraph graph (ADR 006; research R1, R5, R6).

plan -> one `evaluate` branch per theme (Send) -> consolidate -> action_plan.

- plan: the checklist items for the jurisdiction and establishment type, by theme (`items_for`);
  no model call.
- evaluate: one branch per theme. Each item is a LangGraph task: its excerpts (regulation text and
  evidence search, in a worker thread), then `judge_item`. A finished task's result is stored with
  the checkpoint, so a resumed run (a redelivered message, after a crash) gets it back instead of
  judging the item again, and a finished branch does not run again at all. At most `concurrency`
  items are evaluated at once, across all branches. `on_progress` gets the per-theme snapshot
  after each newly evaluated item.
- consolidate: one result per item (duplicates dropped), findings by severity, then in checklist
  order. If the budget stopped any item, the inspection fails `budget_exceeded` with no findings.
- action_plan: each finding as the contract's `Finding`, its action due in 7, 30 or 90 days by
  severity (research R5); a compliant finding has none.

The state holds only plain values (dicts, lists, strings, numbers), which the saver's serde stores
as they are. The `findings` key is written last, by action_plan: it marks a finished inspection.
"""

import asyncio
import operator
from collections.abc import Awaitable, Callable, Sequence
from dataclasses import dataclass, field, fields
from decimal import Decimal
from itertools import chain
from typing import Annotated, Any, TypedDict
from uuid import UUID

import anyio
from langchain_core.runnables import RunnableConfig
from langgraph.checkpoint.base import BaseCheckpointSaver
from langgraph.func import task
from langgraph.graph import END, START, StateGraph
from langgraph.types import Send

from normetry_ai.inspection.checklist import ChecklistItem, items_for
from normetry_ai.inspection.guard import JudgedFinding
from normetry_ai.inspection.judge import JudgeModel, judge_item
from normetry_ai.llm.usage import Usage
from normetry_ai.messaging.contracts import Finding, InspectionRequested, Severity, ThemeProgress
from normetry_ai.retrieval.models import RetrievedChunk
from normetry_ai.settings import AnswerSettings

# Days to fix a non_compliant or insufficient_evidence finding, from completion (research R5).
DUE_IN_DAYS = {Severity.CRITICAL: 7, Severity.MAJOR: 30, Severity.MINOR: 90}
# Findings are listed most severe first, in the order Severity declares them.
SEVERITY_RANK = {severity: rank for rank, severity in enumerate(Severity)}
# The budget's estimate of a prompt's tokens. English runs about 4 characters a token, Portuguese
# fewer; 3 overestimates both, so the ceiling holds.
CHARS_PER_TOKEN = 3
# The graph's node names. Plain strings, not enum members: LangGraph keeps them in each checkpoint.
PLAN_NODE = "plan"
EVALUATE_NODE = "evaluate"
CONSOLIDATE_NODE = "consolidate"
ACTION_PLAN_NODE = "action_plan"

Snapshot = tuple[ThemeProgress, ...]
# An item's regulation chunks and evidence chunks.
Excerpts = Callable[[ChecklistItem], tuple[Sequence[RetrievedChunk], Sequence[RetrievedChunk]]]


class ItemResultState(TypedDict):
    """One item's result, as `_result` builds it and the state stores it: a plain dict."""

    item_id: str
    theme: str
    finding: dict[str, Any] | None  # JudgedFinding's plain fields; None when dropped or not judged
    input_tokens: int  # cached ones included
    output_tokens: int
    cost_usd: float
    budget_exceeded: bool  # stopped before its call, never judged


class State(TypedDict, total=False):
    # The run's input (`start`): what the message asked for, kept to check later deliveries.
    establishment_id: str
    jurisdiction: str
    establishment_type: str
    plan: dict[str, list[str]]  # theme -> item ids, in checklist order
    results: Annotated[list[ItemResultState], operator.add]  # one per evaluated item
    judged: list[dict[str, Any]]  # the findings, deduplicated and in severity order
    budget_exceeded: bool
    findings: list[dict[str, Any]]  # contract Findings (camelCase JSON), with due days


def tokens(usage: Usage) -> int:
    """What a call spends of the ceiling: every input token, cached or not, and every output."""
    return (
        usage.input_tokens
        + usage.cache_creation_input_tokens
        + usage.cache_read_input_tokens
        + usage.output_tokens
    )


class BudgetExceeded(Exception):
    """The next judge call could pass the inspection's token ceiling."""


@dataclass
class Budget:
    """The request's token ceiling around the judge model (research R6); a JudgeModel itself.

    Before a call: if the tokens spent, the estimates of the calls in flight and this call's
    estimate (its prompt at CHARS_PER_TOKEN characters a token, plus the whole max_tokens) add up
    to more than the ceiling, it raises BudgetExceeded, and so does every later call. Once the
    call returns, its real usage counts instead of its estimate.
    """

    model: JudgeModel
    ceiling: int
    max_output_tokens: int
    used: int = 0
    in_flight: int = 0
    exceeded: bool = False

    async def judge(self, system: str, user: str) -> tuple[Any, Usage]:
        estimate = (len(system) + len(user)) // CHARS_PER_TOKEN + self.max_output_tokens
        if self.exceeded or self.used + self.in_flight + estimate > self.ceiling:
            self.exceeded = True
            raise BudgetExceeded
        self.in_flight += estimate
        try:
            raw, usage = await self.model.judge(system, user)
        finally:
            self.in_flight -= estimate
        self.used += tokens(usage)
        return raw, usage


@dataclass
class Progress:
    """Items evaluated per theme: the `themes` of InspectionStarted and InspectionProgressed."""

    plan: dict[str, list[ChecklistItem]]
    evaluated: set[str] = field(default_factory=set)

    def add(self, item_id: str) -> bool:
        """Count an evaluated item; False when it was counted already (a resumed run)."""
        if item_id in self.evaluated:
            return False
        self.evaluated.add(item_id)
        return True

    def snapshot(self) -> Snapshot:
        return tuple(
            ThemeProgress(
                theme=theme,
                items_total=len(items),
                items_evaluated=sum(item.id in self.evaluated for item in items),
            )
            for theme, items in self.plan.items()
        )


@dataclass(frozen=True)
class Saved:
    """An inspection's last checkpoint: its state and every item result stored so far."""

    exists: bool
    values: State
    # One per item: from finished super-steps, finished branches and finished item tasks.
    results: list[ItemResultState]

    @property
    def finished(self) -> bool:
        return "findings" in self.values

    def is_for(self, request: InspectionRequested) -> bool:
        """Whether the stored run was started for the establishment, jurisdiction and type that
        `request` names. ponytail: the first checkpoint, stored just before the first step,
        holds no input yet, so a run that died in between is not checked."""
        return all(self.values.get(key, value) == value for key, value in start(request).items())


def start(request: InspectionRequested) -> State:
    """The graph's input for an inspection request."""
    return {
        "establishment_id": str(request.establishment_id),
        "jurisdiction": request.jurisdiction,
        "establishment_type": request.establishment_type,
    }


def config(inspection_id: UUID) -> RunnableConfig:
    """The graph config of an inspection: its id is the checkpoint thread."""
    return {"configurable": {"thread_id": str(inspection_id)}}


def _is_result(value: object) -> bool:
    """Whether a pending write is an item task's return value."""
    return isinstance(value, dict) and ItemResultState.__required_keys__ <= value.keys()


def _unique(results: Sequence[ItemResultState]) -> list[ItemResultState]:
    """One result per item, the first one stored."""
    seen: dict[str, ItemResultState] = {}
    for result in results:
        seen.setdefault(result["item_id"], result)
    return list(seen.values())


async def load(checkpointer: BaseCheckpointSaver, inspection_id: UUID) -> Saved:
    """The inspection's last checkpoint, with the results its pending writes hold: a branch's
    results, and the return value of each item task that finished in an unfinished branch."""
    saved = await checkpointer.aget_tuple(config(inspection_id))
    if saved is None:
        return Saved(exists=False, values={}, results=[])
    values = saved.checkpoint["channel_values"]
    results: list[ItemResultState] = list(values.get("results", []))
    for _, channel, value in saved.pending_writes or []:
        if channel == "results":
            results += value
        elif _is_result(value):
            results.append(value)
    return Saved(exists=True, values=values, results=_unique(results))


def _plain(finding: JudgedFinding) -> dict[str, Any]:
    plain = {name.name: getattr(finding, name.name) for name in fields(finding)}
    # The saver's serde would store an enum member with its class, which LangGraph is moving to
    # refuse on load; the state keeps the values only.
    for key in ("severity", "verdict", "confidence"):
        plain[key] = str(plain[key])
    for key in ("regulation_citations", "evidence_citations"):
        plain[key] = [citation.model_dump(mode="json") for citation in plain[key]]
    return plain


def _result(
    item: ChecklistItem,
    finding: JudgedFinding | None,
    usages: Sequence[Usage],
    settings: AnswerSettings,
    *,
    budget_exceeded: bool = False,
) -> ItemResultState:
    cost = sum((u.cost_usd(settings.model_prices[u.model]) for u in usages), Decimal(0))
    return {
        "item_id": item.id,
        "theme": item.theme,
        "finding": None if finding is None else _plain(finding),
        "input_tokens": sum(tokens(u) - u.output_tokens for u in usages),
        "output_tokens": sum(u.output_tokens for u in usages),
        "cost_usd": float(cost),
        "budget_exceeded": budget_exceeded,
    }


def _read_error(future: asyncio.Future[Any]) -> None:
    # An item's error stops the run, which cancels the other branches before they read every
    # future; asyncio would then log each unread error with its text, which can quote stored
    # rows. The run still raises it.
    if not future.cancelled():
        future.exception()


def _finding(judged: dict[str, Any]) -> dict[str, Any]:
    action = judged["action"]
    due = (
        None
        if action is None
        else {"text": action, "due_in_days": DUE_IN_DAYS[Severity(judged["severity"])]}
    )
    return Finding.model_validate({**judged, "action": due}).model_dump(mode="json")


def plan(state: State) -> dict[str, Any]:
    planned = items_for(state["jurisdiction"], state["establishment_type"])
    return {"plan": {theme: [item.id for item in items] for theme, items in planned.items()}}


def branches(state: State) -> list[Send] | str:
    sends = [
        Send(EVALUATE_NODE, {"theme": theme, "item_ids": item_ids})
        for theme, item_ids in state["plan"].items()
    ]
    return sends or CONSOLIDATE_NODE


def consolidate(state: State) -> dict[str, Any]:
    results = _unique(state.get("results", []))
    if any(result["budget_exceeded"] for result in results):
        return {"judged": [], "budget_exceeded": True}
    order = {item_id: i for i, item_id in enumerate(chain.from_iterable(state["plan"].values()))}
    judged = [result["finding"] for result in results if result["finding"] is not None]
    judged.sort(key=lambda f: (SEVERITY_RANK[Severity(f["severity"])], order[f["item_id"]]))
    return {"judged": judged, "budget_exceeded": False}


def action_plan(state: State) -> dict[str, Any]:
    return {"findings": [_finding(judged) for judged in state.get("judged", [])]}


def _graph(
    progress: Progress,
    budget: Budget,
    *,
    settings: AnswerSettings,
    concurrency: int,
    excerpts: Excerpts,
    record_usage: Callable[[Usage], None],
    on_progress: Callable[[Snapshot], Awaitable[None]],
) -> StateGraph:
    items = {item.id: item for item in chain.from_iterable(progress.plan.values())}
    limit = asyncio.Semaphore(concurrency)

    @task
    async def evaluate_item(item_id: str) -> ItemResultState:
        item = items[item_id]
        async with limit:
            if budget.exceeded:  # spare the search too
                return _result(item, None, [], settings, budget_exceeded=True)
            regulation, evidence = await anyio.to_thread.run_sync(excerpts, item)
            usages: list[Usage] = []

            def record(usage: Usage) -> None:  # in judge_item's worker thread
                record_usage(usage)
                usages.append(usage)

            try:
                finding = await judge_item(
                    item, regulation, evidence, model=budget, settings=settings, record_usage=record
                )
            except BudgetExceeded:
                return _result(item, None, [], settings, budget_exceeded=True)
        return _result(item, finding, usages, settings)

    async def evaluate(branch: dict[str, Any]) -> dict[str, Any]:
        # Every task is created up front, in checklist order: a resumed run must call them in
        # the same order to get the stored results back.
        futures = [evaluate_item(item_id) for item_id in branch["item_ids"]]
        for future in futures:
            future.add_done_callback(_read_error)
        results: list[ItemResultState] = []
        for future in asyncio.as_completed(futures):
            result = await future
            results.append(result)
            if not result["budget_exceeded"] and progress.add(result["item_id"]):
                await on_progress(progress.snapshot())
        return {"results": results}

    graph = StateGraph(State)
    graph.add_node(PLAN_NODE, plan)
    graph.add_node(EVALUATE_NODE, evaluate)
    graph.add_node(CONSOLIDATE_NODE, consolidate)
    graph.add_node(ACTION_PLAN_NODE, action_plan)
    graph.add_edge(START, PLAN_NODE)
    graph.add_conditional_edges(PLAN_NODE, branches, [EVALUATE_NODE, CONSOLIDATE_NODE])
    graph.add_edge(EVALUATE_NODE, CONSOLIDATE_NODE)
    graph.add_edge(CONSOLIDATE_NODE, ACTION_PLAN_NODE)
    graph.add_edge(ACTION_PLAN_NODE, END)
    return graph


async def run(
    checkpointer: BaseCheckpointSaver,
    request: InspectionRequested,
    saved: Saved,
    *,
    model: JudgeModel,
    settings: AnswerSettings,
    max_output_tokens: int,
    concurrency: int,
    excerpts: Excerpts,
    record_usage: Callable[[Usage], None],
    on_start: Callable[[Snapshot], Awaitable[None]],
    on_progress: Callable[[Snapshot], Awaitable[None]],
) -> Saved:
    """Run the inspection, or resume it from `saved` (its last checkpoint, from `load`), and
    return its final checkpoint.

    The items already evaluated count toward the progress and the budget from the start;
    `on_start` gets the snapshot before anything runs. `record_usage` stores each judge call
    (typically `llm.usage.record_usage` for the message's tenant and requesting user). Raises
    whatever an item raises (ModelUnavailable, a database error): the stored results stand, and a
    later run resumes after them.
    """
    progress = Progress(
        items_for(request.jurisdiction, request.establishment_type),
        {result["item_id"] for result in saved.results if not result["budget_exceeded"]},
    )
    budget = Budget(
        model,
        request.token_ceiling,
        max_output_tokens,
        used=sum(result["input_tokens"] + result["output_tokens"] for result in saved.results),
        exceeded=any(result["budget_exceeded"] for result in saved.results),
    )
    await on_start(progress.snapshot())
    graph = _graph(
        progress,
        budget,
        settings=settings,
        concurrency=concurrency,
        excerpts=excerpts,
        record_usage=record_usage,
        on_progress=on_progress,
    ).compile(checkpointer=checkpointer)
    # durability="sync": each checkpoint is stored before the next step starts, so a crash loses
    # at most the items in flight.
    await graph.ainvoke(
        None if saved.exists else start(request), config(request.inspection_id), durability="sync"
    )
    return await load(checkpointer, request.inspection_id)
