"""Conversations and messages in ai.conversations and ai.messages.

Every function takes a connection from tenant_transaction: RLS keeps the tenant to its own rows,
and each query's user filter keeps a user to their own conversations. Results are the chat API's
JSON shapes (src/shared/contracts/chat-api.md); stored claims are kept in that shape already.
"""

import json
from collections.abc import Sequence
from typing import Any
from uuid import UUID

from sqlalchemy import Connection, RowMapping, text

from normetry_ai.conversations.rewrite import Turn

_SUMMARY = "id, establishment_id, title, updated_at"


def _summary(row: RowMapping) -> dict[str, Any]:
    return {
        "id": row["id"],
        "establishmentId": row["establishment_id"],
        "title": row["title"],
        "updatedAt": row["updated_at"],
    }


def _message(row: RowMapping) -> dict[str, Any]:
    if row["role"] == "user":
        return {
            "id": row["id"],
            "role": "user",
            "text": row["text"],
            "createdAt": row["created_at"],
        }
    return {
        "id": row["id"],
        "role": "assistant",
        "kind": row["kind"],
        "reason": row["reason"],
        "language": row["language"],
        "claims": row["claims"],
        "createdAt": row["created_at"],
    }


def create_conversation(
    connection: Connection,
    *,
    tenant_id: UUID,
    user_id: str,
    establishment_id: UUID,
    jurisdiction: str,
) -> dict[str, Any]:
    row = connection.execute(
        text(
            f"""
            INSERT INTO ai.conversations (tenant_id, user_id, establishment_id, jurisdiction)
            VALUES (:tenant_id, :user_id, :establishment_id, :jurisdiction)
            RETURNING {_SUMMARY}
            """  # noqa: S608 (fixed column list)
        ),
        {
            "tenant_id": tenant_id,
            "user_id": user_id,
            "establishment_id": establishment_id,
            "jurisdiction": jurisdiction,
        },
    ).mappings()
    return _summary(row.one())


def list_conversations(connection: Connection, user_id: str) -> list[dict[str, Any]]:
    """The user's conversations in every establishment, latest first."""
    rows = connection.execute(
        text(
            f"SELECT {_SUMMARY} FROM ai.conversations"  # noqa: S608 (fixed column list)
            " WHERE user_id = :user_id ORDER BY updated_at DESC, id"
        ),
        {"user_id": user_id},
    ).mappings()
    return [_summary(row) for row in rows]


def get_conversation(
    connection: Connection, user_id: str, conversation_id: UUID
) -> dict[str, Any] | None:
    """The user's conversation with its messages in order, or None."""
    row = (
        connection.execute(
            text(
                f"SELECT {_SUMMARY} FROM ai.conversations"  # noqa: S608 (fixed column list)
                " WHERE id = :id AND user_id = :user_id"
            ),
            {"id": conversation_id, "user_id": user_id},
        )
        .mappings()
        .one_or_none()
    )
    if row is None:
        return None
    messages = connection.execute(
        text(
            "SELECT id, role, text, kind, reason, language, claims, created_at FROM ai.messages"
            " WHERE conversation_id = :id ORDER BY created_at, id"
        ),
        {"id": conversation_id},
    ).mappings()
    return {**_summary(row), "messages": [_message(message) for message in messages]}


def delete_conversation(connection: Connection, user_id: str, conversation_id: UUID) -> bool:
    """Delete the user's conversation and its messages; False when there is none."""
    deleted = connection.execute(
        text("DELETE FROM ai.conversations WHERE id = :id AND user_id = :user_id"),
        {"id": conversation_id, "user_id": user_id},
    )
    return deleted.rowcount == 1


def start_turn(
    connection: Connection,
    conversation_id: UUID,
    *,
    user_id: str,
    establishment_id: UUID,
    jurisdiction: str,
    question: str,
    history_messages: int,
) -> list[Turn] | None:
    """Store the user's question and return the last `history_messages` messages before it.

    None when the conversation is not this user's, or belongs to another establishment or
    jurisdiction: nothing is written then. The first question becomes the title.
    """
    message_id = connection.execute(
        text(
            """
            WITH turn AS (
                UPDATE ai.conversations SET title = coalesce(title, :question), updated_at = now()
                WHERE id = :id AND user_id = :user_id AND establishment_id = :establishment_id
                  AND jurisdiction = :jurisdiction
                RETURNING tenant_id, id
            )
            INSERT INTO ai.messages (tenant_id, conversation_id, role, text)
            SELECT tenant_id, id, 'user', :question FROM turn
            RETURNING id
            """
        ),
        {
            "id": conversation_id,
            "user_id": user_id,
            "establishment_id": establishment_id,
            "jurisdiction": jurisdiction,
            "question": question,
        },
    ).scalar_one_or_none()
    if message_id is None:
        return None
    rows = connection.execute(
        text(
            """
            SELECT role, text, claims FROM ai.messages
            WHERE conversation_id = :id AND id <> :message_id
            ORDER BY created_at DESC, id DESC
            LIMIT :limit
            """
        ),
        {"id": conversation_id, "message_id": message_id, "limit": history_messages},
    ).all()
    return [
        Turn("user", row.text)
        if row.role == "user"
        else Turn("assistant", " ".join(claim["text"] for claim in row.claims))
        for row in reversed(rows)
    ]


def store_answer(
    connection: Connection,
    conversation_id: UUID,
    *,
    user_id: str,
    kind: str,
    reason: str | None,
    language: str | None,
    claims: Sequence[dict[str, Any]],
) -> None:
    """Store an answer in the user's conversation; nothing when it was deleted meanwhile (or is
    not the user's)."""
    connection.execute(
        text(
            """
            WITH turn AS (
                UPDATE ai.conversations SET updated_at = now() WHERE id = :id AND user_id = :user_id
                RETURNING tenant_id, id
            )
            INSERT INTO ai.messages (tenant_id, conversation_id, role, kind, reason, language,
                                     claims)
            SELECT tenant_id, id, 'assistant', :kind, :reason, :language, CAST(:claims AS jsonb)
            FROM turn
            """
        ),
        {
            "id": conversation_id,
            "user_id": user_id,
            "kind": kind,
            "reason": reason,
            "language": language,
            "claims": json.dumps(list(claims)),
        },
    )
