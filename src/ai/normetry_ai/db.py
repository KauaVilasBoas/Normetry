"""Database access as the app role (app_ai). Every query runs inside a tenant-scoped transaction."""

from collections.abc import Iterator
from contextlib import contextmanager
from typing import Any
from uuid import UUID

from sqlalchemy import Connection, Engine, create_engine, event, text

from normetry_ai.settings import Settings


def create_db_engine(settings: Settings) -> Engine:
    """Pooled engine for NORMETRY_DATABASE_URL (app_ai, DML only, subject to RLS)."""
    if settings.database_url is None:
        raise RuntimeError("Set NORMETRY_DATABASE_URL (app_ai role) to use the database.")
    engine = create_engine(settings.database_url.get_secret_value())
    event.listen(engine, "checkin", _reset_tenant)
    return engine


def _reset_tenant(dbapi_connection: Any, _record: object) -> None:
    # Runs after the pool's rollback, on an idle connection. tenant_transaction only sets the
    # tenant transaction-locally, but a stray session-level SET would otherwise reach the next
    # checkout. RESET is transactional, hence the commit.
    if dbapi_connection is None:  # invalidated
        return
    with dbapi_connection.cursor() as cursor:
        cursor.execute("RESET app.tenant_id")
    dbapi_connection.commit()


@contextmanager
def tenant_transaction(engine: Engine, tenant_id: UUID) -> Iterator[Connection]:
    """Open a transaction scoped to one tenant: commits on success, rolls back on error.

    The tenant comes from the internal headers set by YARP or from the queue message, never from a
    request body. `set_config(..., true)` is transaction-local, so the pooled connection does not
    carry the tenant into the next transaction. This is the only code that sets app.tenant_id.
    """
    # A str from a header must be parsed first: a non-UUID never reaches the database.
    if not isinstance(tenant_id, UUID):
        raise TypeError("tenant_id must be a UUID")
    if tenant_id.int == 0:
        raise ValueError("tenant_id must not be the nil UUID")
    with engine.begin() as connection:
        connection.execute(
            text("SELECT set_config('app.tenant_id', :tenant, true)"), {"tenant": str(tenant_id)}
        )
        yield connection


def check_runtime_role(connection: Connection) -> None:
    """Refuse a runtime role that can get around RLS.

    A superuser, a BYPASSRLS role, or the owner of schema ai or of anything in it (directly or by
    membership) could read every tenant or switch RLS off. Raises RuntimeError naming the problem.
    """
    user, superuser, bypassrls, owner = connection.execute(
        text(
            """
            SELECT current_user, rolsuper, rolbypassrls,
                   EXISTS (SELECT FROM pg_namespace
                           WHERE nspname = 'ai' AND pg_has_role(nspowner, 'MEMBER'))
                   OR EXISTS (SELECT FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                              WHERE n.nspname = 'ai' AND pg_has_role(c.relowner, 'MEMBER'))
            FROM pg_roles WHERE rolname = current_user
            """
        )
    ).one()
    problems = [
        reason
        for reason, found in (
            ("is a superuser", superuser),
            ("has BYPASSRLS", bypassrls),
            ("owns schema ai or objects in it", owner),
        )
        if found
    ]
    if problems:
        raise RuntimeError(
            f"NORMETRY_DATABASE_URL connects as {user!r}, which {' and '.join(problems)}. "
            "The service must connect as app_ai."
        )
