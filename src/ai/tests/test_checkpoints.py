"""The inspection checkpoint saver (`inspection/checkpoints.py`) and the RLS of its tables, on the
shared PostgreSQL as app_ai: what a run stores comes back, and one tenant never reads, writes or
forges another's checkpoints (the `rls-table` skill)."""

from collections.abc import Iterator
from contextlib import contextmanager
from typing import Any
from uuid import UUID, uuid4

import pytest
from langgraph.checkpoint.base import empty_checkpoint
from sqlalchemy import Engine, text
from sqlalchemy.exc import DBAPIError

from normetry_ai.db import tenant_transaction
from normetry_ai.inspection.checkpoints import TenantCheckpointSaver

pytestmark = pytest.mark.integration

TABLES = ("ai.inspection_checkpoints", "ai.inspection_checkpoint_writes")
INSERT = {
    "ai.inspection_checkpoints": text(
        """
        INSERT INTO ai.inspection_checkpoints (tenant_id, thread_id, checkpoint_ns, checkpoint_id,
                                               type, checkpoint, metadata)
        VALUES (:tenant_id, :thread_id, '', :checkpoint_id, 'msgpack', '\\x00', '{}')
        """
    ),
    "ai.inspection_checkpoint_writes": text(
        """
        INSERT INTO ai.inspection_checkpoint_writes (tenant_id, thread_id, checkpoint_ns,
                                                     checkpoint_id, task_id, idx, task_path,
                                                     channel, type, blob)
        VALUES (:tenant_id, :thread_id, '', :checkpoint_id, 'task', 0, '', 'results', 'msgpack',
                '\\x00')
        """
    ),
}


@contextmanager
def denied() -> Iterator[None]:
    """SQLSTATE 42501: a privilege is missing, or a new row violates a row-level security policy."""
    with pytest.raises(DBAPIError) as error:
        yield
    assert error.value.orig.sqlstate == "42501"


def config(thread: UUID, checkpoint_id: str | None = None) -> dict[str, Any]:
    configurable: dict[str, Any] = {"thread_id": str(thread), "checkpoint_ns": ""}
    if checkpoint_id is not None:
        configurable["checkpoint_id"] = checkpoint_id
    return {"configurable": configurable}


def checkpoint(values: dict[str, Any]) -> dict[str, Any]:
    return {**empty_checkpoint(), "channel_values": values}


def store(saver: TenantCheckpointSaver, thread: UUID) -> tuple[str, str]:
    """Two checkpoints of a thread, the second with two tasks' writes; their ids."""
    first, second = checkpoint({"plan": {"waste": ["us.a"]}}), checkpoint({"results": [1]})
    saved = saver.put(config(thread), first, {"source": "input", "step": -1}, {})
    saved = saver.put(saved, second, {"source": "loop", "step": 0}, {})
    saver.put_writes(saved, [("results", [{"item_id": "us.b"}])], "task-b", "~b")
    saver.put_writes(saved, [("results", [{"item_id": "us.a"}]), ("other", None)], "task-a", "~a")
    return first["id"], second["id"]


def rows(engine: Engine, tenant: UUID | None) -> dict[str, int]:
    def count(connection: Any) -> dict[str, int]:
        return {
            table: connection.execute(text(f"SELECT count(*) FROM {table}")).scalar_one()  # noqa: S608
            for table in TABLES
        }

    if tenant is None:
        with engine.begin() as connection:
            return count(connection)
    with tenant_transaction(engine, tenant) as connection:
        return count(connection)


def test_a_stored_checkpoint_comes_back_with_its_writes_in_order(app_ai: Engine) -> None:
    saver, thread = TenantCheckpointSaver(app_ai, uuid4()), uuid4()
    first, second = store(saver, thread)

    latest = saver.get_tuple(config(thread))

    assert latest.config == config(thread, second)
    assert latest.parent_config == config(thread, first)
    assert latest.checkpoint["channel_values"] == {"results": [1]}
    assert latest.metadata == {"source": "loop", "step": 0}
    # By task path: the order the run applied them.
    assert latest.pending_writes == [
        ("task-a", "results", [{"item_id": "us.a"}]),
        ("task-a", "other", None),
        ("task-b", "results", [{"item_id": "us.b"}]),
    ]
    older = saver.get_tuple(config(thread, first))
    assert older.checkpoint["channel_values"] == {"plan": {"waste": ["us.a"]}}
    assert (older.parent_config, older.pending_writes) == (None, [])
    assert saver.get_tuple(config(uuid4())) is None


def test_a_repeated_write_keeps_the_first_and_a_repeated_checkpoint_raises(
    app_ai: Engine,
) -> None:
    saver, thread = TenantCheckpointSaver(app_ai, uuid4()), uuid4()
    _, second = store(saver, thread)

    saver.put_writes(config(thread, second), [("results", ["again"])], "task-b", "~b")

    assert ("task-b", "results", [{"item_id": "us.b"}]) in saver.get_tuple(
        config(thread)
    ).pending_writes
    # Checkpoint ids are new on every step: storing one twice is a bug, not a no-op.
    with pytest.raises(DBAPIError):
        saver.put(config(thread), checkpoint({}) | {"id": second}, {}, {})


def test_a_thread_is_an_inspection_id(app_ai: Engine) -> None:
    saver = TenantCheckpointSaver(app_ai, uuid4())
    with pytest.raises(ValueError):
        saver.get_tuple({"configurable": {"thread_id": "1' OR '1'='1"}})
    with pytest.raises(TypeError):
        TenantCheckpointSaver(app_ai, str(uuid4()))  # type: ignore[arg-type]


def test_another_tenant_cannot_read_change_or_forge_checkpoints(app_ai: Engine) -> None:
    tenant_a, tenant_b, thread = uuid4(), uuid4(), uuid4()
    _, second = store(TenantCheckpointSaver(app_ai, tenant_a), thread)

    # B sees nothing, even with A's inspection id.
    assert TenantCheckpointSaver(app_ai, tenant_b).get_tuple(config(thread)) is None
    assert rows(app_ai, tenant_b) == dict.fromkeys(TABLES, 0)
    params = {"tenant_id": tenant_a, "thread_id": thread, "checkpoint_id": second}
    for table, insert in INSERT.items():
        # B cannot add a row carrying A's tenant: not a checkpoint, not a write to A's checkpoint.
        with denied(), tenant_transaction(app_ai, tenant_b) as connection:
            connection.execute(insert, {**params, "checkpoint_id": f"{second}-forged"})
        # Nobody changes or deletes a stored row: app_ai has neither privilege.
        for tenant in (tenant_a, tenant_b):
            for statement in (f"UPDATE {table} SET type = 'x'", f"DELETE FROM {table}"):  # noqa: S608
                with denied(), tenant_transaction(app_ai, tenant) as connection:
                    connection.execute(text(statement))
    # B's own checkpoints for the same inspection id live beside A's, which stay as they were.
    store(TenantCheckpointSaver(app_ai, tenant_b), thread)
    a = TenantCheckpointSaver(app_ai, tenant_a).get_tuple(config(thread))
    assert (a.config, len(a.pending_writes)) == (config(thread, second), 3)
    assert rows(app_ai, tenant_a) == rows(app_ai, tenant_b)


def test_without_a_tenant_no_checkpoint_is_visible_and_none_can_be_written(
    app_ai: Engine,
) -> None:
    tenant, thread = uuid4(), uuid4()
    _, second = store(TenantCheckpointSaver(app_ai, tenant), thread)

    assert rows(app_ai, None) == dict.fromkeys(TABLES, 0)
    params = {"tenant_id": tenant, "thread_id": thread, "checkpoint_id": f"{second}-new"}
    for insert in INSERT.values():
        with denied(), app_ai.begin() as connection:
            connection.execute(insert, params)


def test_only_app_ai_sees_checkpoints(app_ai: Engine, owner: Engine, corpus: Engine) -> None:
    tenant = uuid4()
    store(TenantCheckpointSaver(app_ai, tenant), uuid4())

    # FORCE ROW LEVEL SECURITY with no policy for the owner: nothing, even with the tenant set.
    assert rows(owner, tenant) == dict.fromkeys(TABLES, 0)
    with denied():
        rows(corpus, tenant)
