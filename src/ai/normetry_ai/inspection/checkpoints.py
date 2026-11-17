"""LangGraph checkpoints of an inspection under tenant RLS (research R1).

`langgraph-checkpoint-postgres` needs an autocommit connection, so the tenant could only be a
session setting. This saver runs every read and write inside `tenant_transaction(engine,
tenant_id)` instead, over `ai.inspection_checkpoints` and `ai.inspection_checkpoint_writes`, as
app_ai. One saver serves one tenant, taken from the queue message; the thread id is the
inspection id.

It implements what a run calls: `get_tuple`, `put`, `put_writes` and their async versions (each in
a worker thread, since the engine is synchronous). `list` (state history) and the delete and prune
methods keep the base class's NotImplementedError: nothing here uses them, and app_ai cannot
delete checkpoint rows.

ponytail: each checkpoint stores the whole graph state in one blob (no per-channel blob table),
and rows are never pruned. Fine for an inspection's few super-steps; add blobs per channel and a
retention job (with DELETE granted) if states or volumes grow.
"""

import json
from collections.abc import Sequence
from typing import Any
from uuid import UUID

import anyio
from langchain_core.runnables import RunnableConfig
from langgraph.checkpoint.base import (
    WRITES_IDX_MAP,
    BaseCheckpointSaver,
    ChannelVersions,
    Checkpoint,
    CheckpointMetadata,
    CheckpointTuple,
    get_checkpoint_id,
    get_serializable_checkpoint_metadata,
)
from langgraph.checkpoint.serde.base import SerializerProtocol
from sqlalchemy import Engine, text

from normetry_ai.db import tenant_transaction

_CHECKPOINT = """
    SELECT checkpoint_id, parent_checkpoint_id, type, checkpoint, metadata
    FROM ai.inspection_checkpoints
    WHERE thread_id = :thread_id AND checkpoint_ns = :checkpoint_ns
"""
_ONE = text(_CHECKPOINT + " AND checkpoint_id = :checkpoint_id")
# checkpoint_id is a uuid6 string: the largest is the latest. The primary key serves both.
_LATEST = text(_CHECKPOINT + " ORDER BY checkpoint_id DESC LIMIT 1")
# In the order the run applied them (langgraph's writes_sort_key).
_WRITES = text(
    """
    SELECT task_id, channel, type, blob FROM ai.inspection_checkpoint_writes
    WHERE thread_id = :thread_id AND checkpoint_ns = :checkpoint_ns
      AND checkpoint_id = :checkpoint_id
    ORDER BY task_path, task_id, idx
    """
)
# A checkpoint id is new on every super-step: a clash is a bug and raises.
_INSERT_CHECKPOINT = text(
    """
    INSERT INTO ai.inspection_checkpoints (tenant_id, thread_id, checkpoint_ns, checkpoint_id,
                                           parent_checkpoint_id, type, checkpoint, metadata)
    VALUES (:tenant_id, :thread_id, :checkpoint_ns, :checkpoint_id, :parent_checkpoint_id,
            :type, :checkpoint, CAST(:metadata AS jsonb))
    """
)
# A task's writes are stored once: a repeated write keeps the first, as LangGraph's savers do for
# regular writes. They also overwrite a special write (an error) repeated by the same task at the
# same checkpoint; that needs UPDATE, and the first error is as good for a resume, which re-runs
# every task without a regular write.
_INSERT_WRITE = text(
    """
    INSERT INTO ai.inspection_checkpoint_writes (tenant_id, thread_id, checkpoint_ns,
                                                 checkpoint_id, task_id, idx, task_path,
                                                 channel, type, blob)
    VALUES (:tenant_id, :thread_id, :checkpoint_ns, :checkpoint_id, :task_id, :idx, :task_path,
            :channel, :type, :blob)
    ON CONFLICT ON CONSTRAINT inspection_checkpoint_writes_pkey DO NOTHING
    """
)


def _keys(config: RunnableConfig) -> dict[str, Any]:
    configurable = config["configurable"]
    # A thread is an inspection id; anything else never reaches the database.
    return {
        "thread_id": UUID(str(configurable["thread_id"])),
        "checkpoint_ns": configurable.get("checkpoint_ns", ""),
    }


def _config(keys: dict[str, Any], checkpoint_id: str) -> RunnableConfig:
    return {
        "configurable": {
            "thread_id": str(keys["thread_id"]),
            "checkpoint_ns": keys["checkpoint_ns"],
            "checkpoint_id": checkpoint_id,
        }
    }


class TenantCheckpointSaver(BaseCheckpointSaver[int]):
    """The checkpoints of one tenant's inspections, every query in that tenant's transaction."""

    def __init__(
        self, engine: Engine, tenant_id: UUID, *, serde: SerializerProtocol | None = None
    ) -> None:
        super().__init__(serde=serde)
        if not isinstance(tenant_id, UUID):  # tenant_transaction checks again, per call
            raise TypeError("tenant_id must be a UUID")
        self.engine, self.tenant_id = engine, tenant_id

    def get_tuple(self, config: RunnableConfig) -> CheckpointTuple | None:
        """The checkpoint `config` names, or the thread's latest; None when there is none."""
        keys, checkpoint_id = _keys(config), get_checkpoint_id(config)
        with tenant_transaction(self.engine, self.tenant_id) as connection:
            query, params = (
                (_ONE, {**keys, "checkpoint_id": checkpoint_id})
                if checkpoint_id
                else (_LATEST, keys)
            )
            row = connection.execute(query, params).one_or_none()
            if row is None:
                return None
            writes = connection.execute(_WRITES, {**keys, "checkpoint_id": row.checkpoint_id}).all()
        parent = row.parent_checkpoint_id
        return CheckpointTuple(
            config=_config(keys, row.checkpoint_id),
            checkpoint=self.serde.loads_typed((row.type, row.checkpoint)),
            metadata=row.metadata,
            parent_config=_config(keys, parent) if parent else None,
            pending_writes=[
                (write.task_id, write.channel, self.serde.loads_typed((write.type, write.blob)))
                for write in writes
            ],
        )

    def put(
        self,
        config: RunnableConfig,
        checkpoint: Checkpoint,
        metadata: CheckpointMetadata,
        new_versions: ChannelVersions,
    ) -> RunnableConfig:
        """Store a checkpoint (with every channel value) after the one `config` names."""
        keys = _keys(config)
        kind, blob = self.serde.dumps_typed(checkpoint)
        with tenant_transaction(self.engine, self.tenant_id) as connection:
            connection.execute(
                _INSERT_CHECKPOINT,
                {
                    **keys,
                    "tenant_id": self.tenant_id,
                    "checkpoint_id": checkpoint["id"],
                    "parent_checkpoint_id": get_checkpoint_id(config),
                    "type": kind,
                    "checkpoint": blob,
                    "metadata": json.dumps(get_serializable_checkpoint_metadata(config, metadata)),
                },
            )
        return _config(keys, checkpoint["id"])

    def put_writes(
        self,
        config: RunnableConfig,
        writes: Sequence[tuple[str, Any]],
        task_id: str,
        task_path: str = "",
    ) -> None:
        """Store a task's writes, linked to the checkpoint `config` names."""
        if not writes:
            return
        keys = {**_keys(config), "checkpoint_id": config["configurable"]["checkpoint_id"]}
        rows = []
        for idx, (channel, value) in enumerate(writes):
            kind, blob = self.serde.dumps_typed(value)
            rows.append(
                {
                    **keys,
                    "tenant_id": self.tenant_id,
                    "task_id": task_id,
                    "idx": WRITES_IDX_MAP.get(channel, idx),
                    "task_path": task_path,
                    "channel": channel,
                    "type": kind,
                    "blob": blob,
                }
            )
        with tenant_transaction(self.engine, self.tenant_id) as connection:
            connection.execute(_INSERT_WRITE, rows)

    async def aget_tuple(self, config: RunnableConfig) -> CheckpointTuple | None:
        return await anyio.to_thread.run_sync(self.get_tuple, config)

    async def aput(
        self,
        config: RunnableConfig,
        checkpoint: Checkpoint,
        metadata: CheckpointMetadata,
        new_versions: ChannelVersions,
    ) -> RunnableConfig:
        return await anyio.to_thread.run_sync(self.put, config, checkpoint, metadata, new_versions)

    async def aput_writes(
        self,
        config: RunnableConfig,
        writes: Sequence[tuple[str, Any]],
        task_id: str,
        task_path: str = "",
    ) -> None:
        await anyio.to_thread.run_sync(self.put_writes, config, writes, task_id, task_path)
