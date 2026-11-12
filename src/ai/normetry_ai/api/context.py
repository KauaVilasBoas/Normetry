"""Who is calling, read from the internal headers the core's gateway sets.

Contract: src/shared/contracts/internal-http.md. InternalTokenMiddleware runs before routing, so a
route that depends on request_context only ever sees headers that came with a valid token.
"""

from dataclasses import dataclass
from uuid import UUID

from fastapi import HTTPException
from starlette.requests import HTTPConnection

TENANT_HEADER = "X-Normetry-Tenant-Id"
USER_HEADER = "X-Normetry-User-Id"
_DETAIL = "Missing or invalid internal context headers."


@dataclass(frozen=True)
class RequestContext:
    tenant_id: UUID
    user_id: str


def request_context(request: HTTPConnection) -> RequestContext:
    """FastAPI dependency: exactly one tenant header (a non-nil UUID) and one non-empty user header.

    Anything else is a gateway bug, never something to guess around, so it answers 400.
    HTTPConnection rather than Request, so websocket routes can depend on it too.
    """
    tenants = request.headers.getlist(TENANT_HEADER)
    users = request.headers.getlist(USER_HEADER)
    if len(tenants) != 1 or len(users) != 1 or not users[0]:
        raise HTTPException(status_code=400, detail=_DETAIL)
    tenant = tenants[0]
    try:
        tenant_id = UUID(tenant)
    except ValueError:
        raise HTTPException(status_code=400, detail=_DETAIL) from None
    # Only the lower-case 8-4-4-4-12 form the gateway writes; UUID() also takes braces, urn:,
    # upper case and no dashes.
    if str(tenant_id) != tenant or tenant_id.int == 0:
        raise HTTPException(status_code=400, detail=_DETAIL)
    return RequestContext(tenant_id=tenant_id, user_id=users[0])
